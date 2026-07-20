namespace MT_F1Chronos.Core.Services;

/// <summary>Shared deferred-flush timer used by SessionStore and ContestStore.</summary>
internal sealed class DeferredFlush : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Action _flush;
    private readonly Timer _timer;
    private bool _disposed;

    public DeferredFlush(TimeSpan delay, Action flush)
    {
        _delay = delay;
        _flush = flush;
        _timer = new Timer(_ =>
        {
            try { _flush(); }
            catch { /* never throw from timer */ }
        }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Schedule()
    {
        if (_disposed)
            return;

        try
        {
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
    }
}
