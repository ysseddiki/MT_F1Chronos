namespace MT_F1Chronos.Tests;

/// <summary>Minimal <see cref="TimeProvider"/> for deterministic Core tests (no extra NuGet).</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public MutableTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
