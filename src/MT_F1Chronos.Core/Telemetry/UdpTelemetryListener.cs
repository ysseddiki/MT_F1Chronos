using System.Net.Sockets;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class UdpTelemetryListener : IDisposable
{
    private readonly TelemetryState _working = new();
    private readonly F1UdpPacketParser _parser = new();
    private readonly object _snapshotGate = new();
    private TelemetryState _latestSnapshot = new();
    private UdpClient? _client;
    private CancellationTokenSource? _cts;

    public event Action<TelemetryUpdate>? UpdateReceived;

    /// <summary>Last published immutable snapshot (safe to read from the UI thread).</summary>
    public TelemetryState State
    {
        get
        {
            lock (_snapshotGate)
                return _latestSnapshot;
        }
    }

    public F1UdpPacketParser Parser => _parser;

    public void SetFormat(ushort format)
    {
        _parser.SetFormat(format);
        _working.ConfiguredFormat = format;
        PublishSnapshot(_working.Clone());
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
        _working.IsReceiving = false;
        PublishSnapshot(_working.Clone());
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _client is not null)
        {
            try
            {
                var result = await _client.ReceiveAsync(token);
                if (_parser.TryParse(result.Buffer, _working, out var update) && update is not null)
                {
                    PublishSnapshot(update.State);
                    UpdateReceived?.Invoke(update);
                }
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

    private void PublishSnapshot(TelemetryState snapshot)
    {
        lock (_snapshotGate)
            _latestSnapshot = snapshot;
    }

    public void Dispose() => Stop();
}
