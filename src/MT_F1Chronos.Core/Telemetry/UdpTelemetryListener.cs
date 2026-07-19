using System.Net.Sockets;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class UdpTelemetryListener : IDisposable
{
    private readonly TelemetryState _state = new();
    private readonly F1UdpPacketParser _parser = new();
    private UdpClient? _client;
    private CancellationTokenSource? _cts;

    public event Action<TelemetryUpdate>? UpdateReceived;

    public TelemetryState State => _state;
    public F1UdpPacketParser Parser => _parser;

    public void SetFormat(ushort format)
    {
        _parser.SetFormat(format);
        _state.ConfiguredFormat = format;
    }

    public void Start(int port = F1UdpConstants.DefaultPort)
    {
        Stop();

        var cts = new CancellationTokenSource();
        UdpClient client;
        try
        {
            client = new UdpClient(port);
        }
        catch
        {
            // Port already in use (another instance/tool) or unavailable.
            // Leave the listener stopped and let the caller surface the error.
            cts.Dispose();
            throw;
        }

        _cts = cts;
        _client = client;
        _ = Task.Run(() => ListenLoop(cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _client?.Close();
        _client?.Dispose();
        _client = null;
        _cts?.Dispose();
        _cts = null;
        _state.IsReceiving = false;
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client is not null)
        {
            try
            {
                var result = await _client.ReceiveAsync(token);
                if (_parser.TryParse(result.Buffer, _state, out var update) && update is not null)
                    UpdateReceived?.Invoke(update);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore transient socket errors while listening.
            }
        }
    }

    public void Dispose() => Stop();
}
