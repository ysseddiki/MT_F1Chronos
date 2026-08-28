using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.App.Services;

public sealed class ResultsSyncStatus
{
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public string Message { get; set; } = "Désactivé";
    public DateTime? LastOkUtc { get; set; }
}

/// <summary>
/// NAT-friendly pull: the sim always initiates HTTP. Jobs arrive in the sync response.
/// </summary>
public sealed class ResultsSyncClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<string>, ResultsSyncRequest> _buildSnapshot;
    private readonly Func<IReadOnlyList<ResultsCommand>, IReadOnlyList<string>> _applyCommands;
    private readonly Action<IReadOnlyList<string>> _acknowledgeDeleted;
    private readonly Action<string>? _onTokenReceived;
    private readonly DispatcherTimer _heartbeat;
    private readonly DispatcherTimer _debounce;
    private readonly object _gate = new();

    private HttpClient? _http;
    private AppSettings _settings = new();
    private bool _inFlight;
    private bool _queued;
    private List<string> _appliedIds = [];
    private bool _disposed;

    public ResultsSyncStatus Status { get; } = new();

    public ResultsSyncClient(
        Dispatcher dispatcher,
        Func<IReadOnlyList<string>, ResultsSyncRequest> buildSnapshot,
        Func<IReadOnlyList<ResultsCommand>, IReadOnlyList<string>> applyCommands,
        Action<IReadOnlyList<string>> acknowledgeDeleted,
        Action<string>? onTokenReceived = null)
    {
        _dispatcher = dispatcher;
        _buildSnapshot = buildSnapshot;
        _applyCommands = applyCommands;
        _acknowledgeDeleted = acknowledgeDeleted;
        _onTokenReceived = onTokenReceived;

        _heartbeat = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(ResultsSyncProtocol.DefaultSyncIntervalSeconds),
        };
        _heartbeat.Tick += (_, _) => _ = SyncAsync();

        _debounce = new DispatcherTimer { Interval = ResultsSyncProtocol.DebounceInterval };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = SyncAsync();
        };
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        RecreateHttp();

        var interval = ResultsSyncProtocol.NormalizeSyncInterval(settings.ResultsSyncIntervalSeconds);
        _heartbeat.Interval = TimeSpan.FromSeconds(interval);

        var enabled = settings.ResultsServerEnabled && TryCreateBaseUri(settings.ResultsServerUrl, out _);
        Status.Enabled = enabled;
        _heartbeat.IsEnabled = enabled;

        if (!enabled)
        {
            Status.Connected = false;
            Status.Message = settings.ResultsServerEnabled ? "URL invalide" : "Désactivé";
            return;
        }

        RequestSync();
    }

    public void RequestSync()
    {
        if (_disposed || !_settings.ResultsServerEnabled)
            return;

        _debounce.Stop();
        _debounce.Start();
    }

    public async Task<string> TestConnectionAsync()
    {
        if (!TryCreateBaseUri(_settings.ResultsServerUrl, out var uri))
            return "URL invalide. Exemple : https://classement.exemple.com (HTTPS, port 443)";

        try
        {
            using var client = CreateClient(uri);
            using var response = await client.GetAsync(ResultsSyncProtocol.HealthPath);
            if (!response.IsSuccessStatusCode)
                return $"Serveur HTTP {(int)response.StatusCode}.";

            Status.Connected = true;
            Status.Message = "Connecté";
            Status.LastOkUtc = DateTime.UtcNow;
            return "Serveur joignable. Sans jeton, l’enregistrement se fait à la première sync.";
        }
        catch (Exception ex)
        {
            Status.Connected = false;
            Status.Message = "Injoignable";
            return $"Impossible de joindre le serveur : {ex.Message}";
        }
    }

    private async Task SyncAsync()
    {
        if (_disposed || !_settings.ResultsServerEnabled)
            return;

        lock (_gate)
        {
            if (_inFlight)
            {
                _queued = true;
                return;
            }

            _inFlight = true;
        }

        try
        {
            await SendOnceAsync();
        }
        finally
        {
            var again = false;
            lock (_gate)
            {
                _inFlight = false;
                again = _queued;
                _queued = false;
            }

            if (again && !_disposed)
                _ = SyncAsync();
        }
    }

    private async Task SendOnceAsync()
    {
        var http = _http;
        if (http is null)
        {
            SetStatus(false, "URL invalide");
            return;
        }

        ResultsSyncRequest request;
        try
        {
            request = _buildSnapshot(_appliedIds);
        }
        catch (Exception ex)
        {
            SetStatus(false, ex.Message);
            return;
        }

        try
        {
            if (!await EnsureRegisteredAsync(http))
                return;

            using var response = await http.PostAsJsonAsync(ResultsSyncProtocol.SyncPath, request, JsonOptions);
            var payload = await response.Content.ReadFromJsonAsync<ResultsSyncResponse>(JsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                SetStatus(false, payload?.Message ?? $"HTTP {(int)response.StatusCode}");
                return;
            }

            if (payload is null)
            {
                SetStatus(false, "Réponse vide");
                return;
            }

            IReadOnlyList<string> applied = [];
            if (payload.Commands.Count > 0)
            {
                // Seules les commandes réellement appliquées sont ACKées : une commande
                // ignorée (type inconnu, handler absent) reste pending et sera renvoyée.
                applied = await _dispatcher.InvokeAsync(() => _applyCommands(payload.Commands));
            }

            _appliedIds = applied.ToList();
            if (request.DeletedEntryIds.Count > 0)
                _acknowledgeDeleted(request.DeletedEntryIds);

            SetStatus(true, "Connecté");
            Status.LastOkUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            SetStatus(false, ex.Message);
        }
    }

    private async Task<bool> EnsureRegisteredAsync(HttpClient http)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ResultsServerToken))
            return true;

        var body = new
        {
            simulatorId = _settings.SimulatorId,
            simulatorLabel = string.IsNullOrWhiteSpace(_settings.SimulatorLabel)
                ? "Simulateur"
                : _settings.SimulatorLabel.Trim(),
            syncIntervalSeconds = ResultsSyncProtocol.NormalizeSyncInterval(_settings.ResultsSyncIntervalSeconds),
        };

        using var response = await http.PostAsJsonAsync(ResultsSyncProtocol.RegisterPath, body, JsonOptions);
        var payload = await response.Content.ReadFromJsonAsync<ResultsRegisterResponse>(JsonOptions);
        if (!response.IsSuccessStatusCode || payload is null || !payload.Ok || string.IsNullOrWhiteSpace(payload.Token))
        {
            SetStatus(false, payload?.Message ?? $"Enregistrement HTTP {(int)response.StatusCode}");
            return false;
        }

        _settings.ResultsServerToken = payload.Token.Trim();
        _onTokenReceived?.Invoke(_settings.ResultsServerToken);
        http.DefaultRequestHeaders.Remove(ResultsSyncProtocol.TokenHeader);
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            ResultsSyncProtocol.TokenHeader, _settings.ResultsServerToken);
        return true;
    }

    private void RecreateHttp()
    {
        _http?.Dispose();
        _http = null;
        if (TryCreateBaseUri(_settings.ResultsServerUrl, out var uri))
            _http = CreateClient(uri);
    }

    private HttpClient CreateClient(Uri baseUri)
    {
        var client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(_settings.ResultsServerToken))
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                ResultsSyncProtocol.TokenHeader, _settings.ResultsServerToken.Trim());
        return client;
    }

    private void SetStatus(bool connected, string message)
    {
        Status.Connected = connected;
        Status.Message = message;
    }

    private static bool TryCreateBaseUri(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var normalized = ResultsSyncProtocol.NormalizeServerUrl(url);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        uri = parsed;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _heartbeat.Stop();
        _debounce.Stop();
        _http?.Dispose();
    }
}
