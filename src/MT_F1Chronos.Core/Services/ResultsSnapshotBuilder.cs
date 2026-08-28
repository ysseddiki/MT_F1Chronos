using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

public static class ResultsSnapshotBuilder
{
    public static ResultsSyncRequest Build(
        string simulatorId,
        string simulatorLabel,
        string playerName,
        int currentTrackId,
        string currentTrackName,
        int syncIntervalSeconds,
        SessionStore store,
        ContestStore contests,
        IReadOnlyList<string>? appliedCommandIds = null)
    {
        var deleted = store.PeekDeletedIds()
            .Concat(contests.PeekDeletedIds())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ResultsSyncRequest
        {
            ProtocolVersion = ResultsSyncProtocol.Version,
            SimulatorId = simulatorId,
            SimulatorLabel = string.IsNullOrWhiteSpace(simulatorLabel) ? "Simulateur" : simulatorLabel.Trim(),
            SentAt = DateTime.UtcNow,
            SyncIntervalSeconds = ResultsSyncProtocol.NormalizeSyncInterval(syncIntervalSeconds),
            PlayerName = playerName ?? string.Empty,
            CurrentTrackId = currentTrackId,
            CurrentTrackName = currentTrackName ?? string.Empty,
            AppliedCommandIds = appliedCommandIds?.ToList() ?? [],
            DeletedEntryIds = deleted,
            Global = ToBoard(store.GetAllScoredEntries()),
            Contests = contests.List().Select(c => new ResultsContestSnapshot
            {
                Id = c.Id,
                Name = c.Name,
                Status = c.Status,
                TrackFilter = c.TrackFilter,
                CreatedAt = c.CreatedAt,
                StartedAt = c.StartedAt,
                StoppedAt = c.StoppedAt,
                Tracks = ToBoard(contests.GetAllScoredEntries(c.Id)).Tracks,
            }).ToList(),
        };
    }

    public static void AcknowledgeDeleted(SessionStore store, ContestStore contests, IEnumerable<string> ids)
    {
        var list = ids.ToList();
        store.AcknowledgeDeletedIds(list);
        contests.AcknowledgeDeletedIds(list);
    }

    public static ResultsBoardSnapshot ToBoard(IEnumerable<ChronoEntry> entries)
    {
        var tracks = entries
            .Where(e => e.TrackId >= 0 && e.BestLapMs is > 0)
            .GroupBy(e => e.TrackId)
            .OrderBy(g => g.First().TrackName)
            .Select(g => new ResultsTrackSnapshot
            {
                TrackId = g.Key,
                TrackName = g.First().TrackName,
                Entries = g
                    .OrderBy(e => e.BestLapMs)
                    .ThenBy(e => e.StartedAt)
                    .Select(ToEntry)
                    .ToList(),
            })
            .ToList();

        return new ResultsBoardSnapshot { Tracks = tracks };
    }

    private static ResultsEntrySnapshot ToEntry(ChronoEntry entry) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        BestLapMs = entry.BestLapMs ?? 0,
        StartedAt = entry.StartedAt,
    };
}
