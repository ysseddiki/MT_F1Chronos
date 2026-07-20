namespace MT_F1Chronos.Core.Models;

/// <summary>Read-only score-board queries used by Scores / overlay.</summary>
public interface IScoreBoardQuery
{
    string BoardLabel { get; }
    IReadOnlyList<TrackSummary> GetTracksWithScores();
    IReadOnlyList<LeaderboardRow> GetScoresForTrack(int trackId, bool bestPerPlayer = false, string? playerName = null);
    IReadOnlyList<string> GetPlayerNamesForTrack(int trackId);
}

/// <summary>Destructive score-board mutations used by ManageScores.</summary>
public interface IScoreBoardMutator
{
    bool DeleteEntry(string entryId);
    int DeletePlayerOnTrack(string playerName, int trackId);
    int ClearTrack(int trackId);
    int ClearAll();
}

/// <summary>Full score-board view (query + mutations).</summary>
public interface IScoreBoardView : IScoreBoardQuery, IScoreBoardMutator;
