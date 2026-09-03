using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using Xunit;

namespace MT_F1Chronos.Tests;

public class ContestStoreTests
{
    [Fact]
    public void Record_OnlyIntoActiveContests()
    {
        var root = NewTempRoot();
        try
        {
            using var contests = new ContestStore(root);
            contests.Load();

            var active = contests.Create("Actif", startImmediately: true);
            var draft = contests.Create("Brouillon", startImmediately: false);
            var stopped = contests.Create("Stoppé", startImmediately: true);
            contests.Stop(stopped.Id);

            contests.RecordCompletedLap("Ada", 7, "Silverstone", 82_000);

            Assert.Single(contests.GetLeaderboard(active.Id, 7));
            Assert.Empty(contests.GetLeaderboard(draft.Id, 7));
            Assert.Empty(contests.GetLeaderboard(stopped.Id, 7));
            Assert.Equal(ContestStatus.Draft, contests.Get(draft.Id)!.Status);
            Assert.Equal(ContestStatus.Stopped, contests.Get(stopped.Id)!.Status);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Record_RespectsTrackFilter()
    {
        var root = NewTempRoot();
        try
        {
            using var contests = new ContestStore(root);
            contests.Load();

            var spaOnly = contests.Create("Spa only");
            contests.Get(spaOnly.Id)!.TrackFilter = 10;

            var allTracks = contests.Create("All tracks");

            contests.RecordCompletedLap("Ada", 7, "Silverstone", 80_000);
            contests.RecordCompletedLap("Ada", 10, "Spa", 81_000);

            Assert.Empty(contests.GetLeaderboard(spaOnly.Id, 7));
            Assert.Single(contests.GetLeaderboard(spaOnly.Id, 10));
            Assert.Single(contests.GetLeaderboard(allTracks.Id, 7));
            Assert.Single(contests.GetLeaderboard(allTracks.Id, 10));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Start_MakesDraftAcceptLaps()
    {
        var root = NewTempRoot();
        try
        {
            using var contests = new ContestStore(root);
            contests.Load();
            var draft = contests.Create("Draft", startImmediately: false);

            contests.RecordCompletedLap("Ada", 1, "Melbourne", 90_000);
            Assert.Empty(contests.GetLeaderboard(draft.Id, 1));

            Assert.True(contests.Start(draft.Id));
            contests.RecordCompletedLap("Ada", 1, "Melbourne", 88_000);
            Assert.Single(contests.GetLeaderboard(draft.Id, 1));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Record_UsesInjectedTimeProvider()
    {
        var root = NewTempRoot();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        try
        {
            using var contests = new ContestStore(root, clock);
            contests.Load();
            var contest = contests.Create("Timed");
            Assert.Equal(clock.GetUtcNow().UtcDateTime, contests.Get(contest.Id)!.CreatedAt);

            clock.SetUtcNow(new DateTimeOffset(2026, 3, 1, 13, 30, 0, TimeSpan.Zero));
            contests.RecordCompletedLap("Ada", 1, "Melbourne", 77_000);

            var entries = contests.GetAllScoredEntries(contest.Id);
            Assert.Single(entries);
            Assert.Equal(clock.GetUtcNow().UtcDateTime, entries[0].StartedAt);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MT_F1Chronos_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch { /* best effort */ }
    }
}
