namespace MT_F1Chronos.App;

public static class OverlaySizes
{
    /// <summary>Base "Grand" width — narrow by default, expands up to <see cref="Max"/> if needed.</summary>
    public const double Default = 300;

    /// <summary>Upper bound when long names force a wider overlay.</summary>
    public const double Max = 440;

    public const int MaxPlayerNameLength = 20;
}
