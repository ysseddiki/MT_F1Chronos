using System.Net;
using System.Net.Sockets;

namespace MT_F1Chronos.Core.Telemetry;

public sealed class UdpTelemetryListener : IDisposable
{
    private readonly TelemetryState _state = new();
    private readonly F1UdpPacketParser _parser = new();
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action<TelemetryUpdate>? UpdateReceived;
    public event Action<Exception>? ErrorOccurred;

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

        _cts = new CancellationTokenSource();
        _client = new UdpClient(port);
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
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
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex);
            }
        }
    }

    public void Dispose() => Stop();
}
