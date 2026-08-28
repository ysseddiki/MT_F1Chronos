using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using Xunit;

namespace MT_F1Chronos.Tests;

public class ResultsSyncTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("classement.exemple.com", "https://classement.exemple.com")]
    [InlineData("https://classement.exemple.com", "https://classement.exemple.com")]
    [InlineData("https://classement.exemple.com:443", "https://classement.exemple.com")]
    [InlineData("http://classement.exemple.com:8080", "https://classement.exemple.com")]
    [InlineData("http://127.0.0.1:8080", "https://127.0.0.1")]
    [InlineData("https://", "")]
    [InlineData("https://classement.exemple.com:8443", "https://classement.exemple.com:8443")]
    public void NormalizeServerUrl_UsesHttpsOn443(string? input, string expected)
    {
        Assert.Equal(expected, ResultsSyncProtocol.NormalizeServerUrl(input));
    }

    [Fact]
    public void SnapshotBuilder_IncludesGlobalAndContests()
    {
        var root = NewTempRoot();
        try
        {
            var store = new SessionStore(root);
            store.Load();
            store.RecordCompletedLap("Ada", 1, "Melbourne", 80_000);

            var contests = new ContestStore(root);
            contests.Load();
            var contest = contests.Create("Soirée");
            contests.RecordCompletedLap("Ada", 1, "Melbourne", 80_000);

            var snap = ResultsSnapshotBuilder.Build(
                "sim1",
                "Box 1",
                "Ada",
                1,
                "Melbourne",
                120,
                store,
                contests);

            Assert.Equal(ResultsSyncProtocol.Version, snap.ProtocolVersion);
            Assert.Equal("sim1", snap.SimulatorId);
            Assert.Equal("Box 1", snap.SimulatorLabel);
            Assert.Equal(120, snap.SyncIntervalSeconds);
            Assert.Single(snap.Global.Tracks);
            Assert.Equal(80_000u, snap.Global.Tracks[0].Entries[0].BestLapMs);
            Assert.Single(snap.Contests);
            Assert.Equal(contest.Id, snap.Contests[0].Id);
            Assert.Equal(ContestStatus.Active, snap.Contests[0].Status);
            Assert.Single(snap.Contests[0].Tracks);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CommandApplier_DeletesLocalEntry_Idempotent()
    {
        var root = NewTempRoot();
        try
        {
            var store = new SessionStore(root);
            store.Load();
            store.RecordCompletedLap("Ada", 1, "Melbourne", 80_000);
            var contests = new ContestStore(root);
            contests.Load();

            var entry = store.GetAllScoredEntries().Single();
            var command = new ResultsCommand
            {
                Id = "cmd1",
                Type = ResultsCommandTypes.DeleteEntry,
                EntryId = entry.Id,
            };

            var applied = ResultsCommandApplier.Apply(store, contests, [command, command]);
            Assert.Equal(["cmd1", "cmd1"], applied);
            Assert.Empty(store.GetAllScoredEntries());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CommandApplier_IgnoresUnknownType()
    {
        var root = NewTempRoot();
        try
        {
            var store = new SessionStore(root);
            store.Load();
            store.RecordCompletedLap("Ada", 1, "Melbourne", 80_000);
            var contests = new ContestStore(root);
            contests.Load();

            var applied = ResultsCommandApplier.Apply(store, contests,
            [
                new ResultsCommand { Id = "x", Type = "nope" },
            ]);

            Assert.Empty(applied);
            Assert.Single(store.GetAllScoredEntries());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void CommandApplier_RenameAndRestore()
    {
        var root = NewTempRoot();
        try
        {
            var store = new SessionStore(root);
            store.Load();
            store.RecordCompletedLap("Ada", 1, "Melbourne", 80_000);
            var contests = new ContestStore(root);
            contests.Load();
            var entry = store.GetAllScoredEntries().Single();

            ResultsCommandApplier.Apply(store, contests,
            [
                new ResultsCommand
                {
                    Id = "r1",
                    Type = ResultsCommandTypes.RenameEntry,
                    EntryId = entry.Id,
                    NewName = "Ada2",
                },
            ]);
            Assert.Equal("Ada2", store.GetAllScoredEntries().Single().Name);

            store.DeleteEntry(entry.Id);
            ResultsCommandApplier.Apply(store, contests,
            [
                new ResultsCommand
                {
                    Id = "rst",
                    Type = ResultsCommandTypes.RestoreEntry,
                    EntryId = entry.Id,
                    PlayerName = "Ada2",
                    TrackId = 1,
                    TrackName = "Melbourne",
                    BestLapMs = 80_000,
                    StartedAt = entry.StartedAt,
                },
            ]);
            Assert.Single(store.GetAllScoredEntries());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string NewTempRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MT_F1Chronos_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best effort */ }
    }
}
