using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

/// <summary>
/// One-shot import of legacy JSON boards (<c>sessions/</c>, <c>contests/</c>) into <see cref="LocalChronosDb"/>.
/// Safe to call on every startup — no-op when already migrated.
/// </summary>
public static class LocalChronosMigrator
{
    public const string ArchiveFolderName = "archive-json";

    /// <summary>
    /// Returns a short FR status line for logs / docs (e.g. "déjà migré", "importé N chronos").
    /// </summary>
    public static string MigrateIfNeeded(string dataDirectory, LocalChronosDb db)
    {
        if (db.IsJsonMigrationCompleted())
            return "déjà migré (chronos.db à jour)";

        Directory.CreateDirectory(dataDirectory);
        var sessionsDir = Path.Combine(dataDirectory, "sessions");
        var contestsDir = Path.Combine(dataDirectory, "contests");
        var legacySessions = Path.Combine(dataDirectory, "sessions.json");

        var importedLaps = 0;
        var importedContests = 0;

        // Legacy monolith sessions.json → per-track then into DB
        if (File.Exists(legacySessions))
        {
            try
            {
                var json = File.ReadAllText(legacySessions);
                var legacy = JsonSerializer.Deserialize<ChronoDatabase>(json, LocalChronosDb.SharedJsonOptions);
                if (legacy?.Sessions is { Count: > 0 })
                {
                    Directory.CreateDirectory(sessionsDir);
                    foreach (var group in legacy.Sessions.GroupBy(s => s.TrackId))
                    {
                        if (group.Key < 0)
                            continue;
                        var capped = TrackScoreBoard.CapEntries(group.ToList());
                        TrackScoreBoard.PersistTrack(sessionsDir, group.Key, capped);
                    }
                }

                TryMove(legacySessions, legacySessions + ".bak");
            }
            catch
            {
                // Continue with whatever track files exist.
            }
        }

        if (Directory.Exists(sessionsDir))
        {
            foreach (var path in Directory.EnumerateFiles(sessionsDir, "track-*.json"))
            {
                if (!TrackScoreBoard.TryParseTrackId(path, out var trackId))
                    continue;

                var entries = TrackScoreBoard.CapEntries(TrackScoreBoard.ReadTrackFile(path));
                if (entries.Count == 0)
                    continue;

                foreach (var e in entries)
                {
                    e.TrackId = trackId;
                    if (string.IsNullOrWhiteSpace(e.TrackName))
                        e.TrackName = $"Circuit {trackId}";
                }

                db.ReplaceTrackLaps(contestId: null, trackId, entries);
                importedLaps += entries.Count;
            }
        }

        var indexPath = Path.Combine(contestsDir, "index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                var json = File.ReadAllText(indexPath);
                var index = JsonSerializer.Deserialize<ContestIndex>(json, LocalChronosDb.SharedJsonOptions);
                if (index?.Contests is { Count: > 0 })
                {
                    db.ReplaceContests(index.Contests);
                    importedContests = index.Contests.Count;

                    foreach (var contest in index.Contests)
                    {
                        var dir = Path.Combine(contestsDir, contest.Id);
                        if (!Directory.Exists(dir))
                            continue;

                        foreach (var path in Directory.EnumerateFiles(dir, "track-*.json"))
                        {
                            if (!TrackScoreBoard.TryParseTrackId(path, out var trackId))
                                continue;

                            var entries = TrackScoreBoard.CapEntries(TrackScoreBoard.ReadTrackFile(path));
                            if (entries.Count == 0)
                                continue;

                            foreach (var e in entries)
                            {
                                e.TrackId = trackId;
                                if (string.IsNullOrWhiteSpace(e.TrackName))
                                    e.TrackName = $"Circuit {trackId}";
                            }

                            db.ReplaceTrackLaps(contest.Id, trackId, entries);
                            importedLaps += entries.Count;
                        }
                    }
                }
            }
            catch
            {
                // Index corrupt — still mark migration to avoid loops; user can restore from archive.
            }
        }

        db.MarkJsonMigrationCompleted();
        ArchiveJsonFolders(dataDirectory, sessionsDir, contestsDir);

        if (importedLaps == 0 && importedContests == 0)
            return "chronos.db initialisé (aucune donnée JSON à importer)";

        return $"importé {importedLaps} chrono(s), {importedContests} concours → chronos.db";
    }

    private static void ArchiveJsonFolders(string dataDirectory, string sessionsDir, string contestsDir)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var archiveRoot = Path.Combine(dataDirectory, ArchiveFolderName, stamp);
        try
        {
            Directory.CreateDirectory(archiveRoot);
            if (Directory.Exists(sessionsDir))
                TryMoveDirectory(sessionsDir, Path.Combine(archiveRoot, "sessions"));
            if (Directory.Exists(contestsDir))
                TryMoveDirectory(contestsDir, Path.Combine(archiveRoot, "contests"));
        }
        catch
        {
            // Archive best-effort; DB already has the data.
        }
    }

    private static void TryMoveDirectory(string source, string dest)
    {
        try
        {
            if (Directory.Exists(dest))
                return;
            Directory.Move(source, dest);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryMove(string source, string dest)
    {
        try
        {
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(source, dest);
        }
        catch
        {
            // ignore
        }
    }
}
