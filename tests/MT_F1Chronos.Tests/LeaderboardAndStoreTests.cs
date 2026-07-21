using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;
using Xunit;

namespace MT_F1Chronos.Tests;

public class LeaderboardQueryTests
{
    [Fact]
    public void Filter_BestPerPlayer_KeepsFastestPerName()
    {
        var entries = new[]
        {
            Entry("A", 90_000),
            Entry("B", 88_000),
            Entry("A", 85_000),
            Entry("B", 91_000),
        };

        var rows = LeaderboardQuery.ToRows(LeaderboardQuery.Filter(entries, bestPerPlayer: true));

        Assert.Equal(2, rows.Count);
        Assert.Equal("A", rows[0].Name);
        Assert.Equal(85_000u, rows[0].BestLapMs);
        Assert.Equal("B", rows[1].Name);
        Assert.Equal(88_000u, rows[1].BestLapMs);
    }

    [Fact]
    public void Filter_PlayerName_IsCaseInsensitive()
    {
        var entries = new[]
        {
            Entry("Alice", 80_000),
            Entry("Bob", 81_000),
            Entry("alice", 79_000),
        };

        var filtered = LeaderboardQuery.Filter(entries, playerName: "ALICE").ToList();
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, e => Assert.Equal("alice", e.Name, ignoreCase: true));
    }

    private static ChronoEntry Entry(string name, uint ms) => new()
    {
        Name = name,
        TrackId = 1,
        TrackName = "Monza",
        BestLapMs = ms,
        StartedAt = DateTime.UtcNow,
    };
}

public class TrackScoreBoardTests
{
    [Fact]
    public void Record_And_Leaderboard_PersistRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MT_F1Chronos_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var board = new TrackScoreBoard();
            board.Record("Pilot", 7, "Spa", 95_000);
            board.Record("Pilot", 7, "Spa", 93_000);
            board.PersistDirty(dir);

            var loaded = new TrackScoreBoard();
            loaded.LoadFromDirectory(dir);
            var rows = loaded.GetLeaderboard(7, count: 5, bestPerPlayer: false);

            Assert.Equal(2, rows.Count);
            Assert.Equal(93_000u, rows[0].BestLapMs);
            Assert.Equal(95_000u, rows[1].BestLapMs);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public void DeleteEntry_RemovesAndMarksDirty()
    {
        var board = new TrackScoreBoard();
        board.Record("A", 1, "Track", 80_000);
        var id = board.GetScoresForTrack(1).Single().EntryId;

        Assert.True(board.DeleteEntry(id));
        Assert.Empty(board.GetScoresForTrack(1));
        Assert.True(board.HasDirty);
    }
}

public class SessionStoreTests
{
    [Fact]
    public void RecordCompletedLap_AppearsInLeaderboard()
    {
        var root = Path.Combine(Path.GetTempPath(), "MT_F1Chronos_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            using var store = new SessionStore(root);
            store.Load();
            store.RecordCompletedLap("Youssef", 3, "Silverstone", 82_500);
            store.Save();

            var rows = store.GetLeaderboard(3, count: 5);
            Assert.Single(rows);
            Assert.Equal("Youssef", rows[0].Name);
            Assert.Equal(82_500u, rows[0].BestLapMs);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { /* ignore */ }
        }
    }
}

public class TelemetryStateTests
{
    [Fact]
    public void Clone_IsIndependentSnapshot()
    {
        var original = new TelemetryState
        {
            TrackId = 5,
            SessionBestLapMs = 70_000,
            CurrentLapTimeMs = 12_000,
            IsReceiving = true,
        };

        var clone = original.Clone();
        original.TrackId = 99;
        original.SessionBestLapMs = 1;

        Assert.Equal(5, clone.TrackId);
        Assert.Equal(70_000u, clone.SessionBestLapMs);
        Assert.Equal(12_000u, clone.CurrentLapTimeMs);
        Assert.True(clone.IsReceiving);
    }
}

public class ScoreExporterTests
{
    [Theory]
    [InlineData("=CMD", "'=CMD")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@SUM", "'@SUM")]
    [InlineData("Normal", "Normal")]
    public void NeutralizeFormula_PrefixesDangerousValues(string input, string expected) =>
        Assert.Equal(expected, ScoreExporter.NeutralizeFormula(input));
}
