namespace MT_F1Chronos.Core.Models;

/// <summary>
/// Optional VPS sync: the simulator always initiates HTTP (NAT).
/// Jobs ride on the sync response. Wiping the server DB never creates jobs.
/// </summary>
public static class ResultsSyncProtocol
{
    public const int Version = 1;
    public const string HealthPath = "/api/v1/health";
    public const string SyncPath = "/api/v1/sync";
    public const string TokenHeader = "X-Results-Token";
    public const int MaxPlayerNameLength = 20;
    public const int MinSyncIntervalSeconds = 15;
    public const int DefaultSyncIntervalSeconds = 120;
    public const int MaxSyncIntervalSeconds = 600;
    public static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);

    public static int NormalizeSyncInterval(int seconds)
    {
        if (seconds < MinSyncIntervalSeconds)
            return DefaultSyncIntervalSeconds;
        return Math.Clamp(seconds, MinSyncIntervalSeconds, MaxSyncIntervalSeconds);
    }
}

public sealed class ResultsSyncRequest
{
    public int ProtocolVersion { get; set; } = ResultsSyncProtocol.Version;
    public string SimulatorId { get; set; } = string.Empty;
    public string SimulatorLabel { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public int SyncIntervalSeconds { get; set; } = ResultsSyncProtocol.DefaultSyncIntervalSeconds;
    public string PlayerName { get; set; } = string.Empty;
    public int CurrentTrackId { get; set; } = -1;
    public string CurrentTrackName { get; set; } = string.Empty;
    public List<string> AppliedCommandIds { get; set; } = [];
    public List<string> DeletedEntryIds { get; set; } = [];
    public ResultsBoardSnapshot Global { get; set; } = new();
    public List<ResultsContestSnapshot> Contests { get; set; } = [];
}

public sealed class ResultsSyncResponse
{
    public bool Ok { get; set; } = true;
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;
    public string? Message { get; set; }
    public List<ResultsCommand> Commands { get; set; } = [];
}

public sealed class ResultsHealthResponse
{
    public bool Ok { get; set; } = true;
    public int ProtocolVersion { get; set; } = ResultsSyncProtocol.Version;
    public bool AuthRequired { get; set; } = true;
}

public sealed class ResultsBoardSnapshot
{
    public List<ResultsTrackSnapshot> Tracks { get; set; } = [];
}

public sealed class ResultsTrackSnapshot
{
    public int TrackId { get; set; }
    public string TrackName { get; set; } = string.Empty;
    public List<ResultsEntrySnapshot> Entries { get; set; } = [];
}

public sealed class ResultsEntrySnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint BestLapMs { get; set; }
    public DateTime StartedAt { get; set; }
}

public sealed class ResultsContestSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ContestStatus Status { get; set; }
    public int? TrackFilter { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public List<ResultsTrackSnapshot> Tracks { get; set; } = [];
}

public static class ResultsCommandTypes
{
    public const string DeleteEntry = "deleteEntry";
    public const string DeletePlayerOnTrack = "deletePlayerOnTrack";
    public const string RenameEntry = "renameEntry";
    public const string RenamePlayer = "renamePlayer";
    public const string RestoreEntry = "restoreEntry";
}

public sealed class ResultsCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public string? ContestId { get; set; }
    public string? EntryId { get; set; }
    public string? PlayerName { get; set; }
    public string? NewName { get; set; }
    public string? TrackName { get; set; }
    public int? TrackId { get; set; }
    public uint? BestLapMs { get; set; }
    public DateTime? StartedAt { get; set; }
}
