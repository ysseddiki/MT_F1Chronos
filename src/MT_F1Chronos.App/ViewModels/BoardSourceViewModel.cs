using System.ComponentModel;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.ViewModels;

/// <summary>Light VM for score-board source selection (global vs contest).</summary>
public sealed class BoardSourceViewModel : INotifyPropertyChanged
{
    private readonly SessionStore _global;
    private readonly ContestStore _contests;
    private IScoreBoardView _board;
    private string? _contestId;

    public BoardSourceViewModel(SessionStore global, ContestStore contests, string? initialContestId = null)
    {
        _global = global;
        _contests = contests;
        _board = global;
        if (!string.IsNullOrWhiteSpace(initialContestId) && contests.Get(initialContestId) is not null)
            SelectContest(initialContestId);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IScoreBoardView Board => _board;

    public IScoreBoardQuery Query => _board;

    public string? ContestId => _contestId;

    public string BoardLabel => _board.BoardLabel;

    public IReadOnlyList<BoardSourceOption> ListSources()
    {
        var list = new List<BoardSourceOption>
        {
            new(null, "Global"),
        };

        foreach (var contest in _contests.List())
            list.Add(new BoardSourceOption(contest.Id, $"Concours — {contest.Name}"));

        return list;
    }

    public void SelectGlobal()
    {
        _contestId = null;
        _board = _global;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Board)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoardLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContestId)));
    }

    public void SelectContest(string contestId)
    {
        if (_contests.Get(contestId) is null)
        {
            SelectGlobal();
            return;
        }

        _contestId = contestId;
        _board = _contests.AsScoreBoard(contestId);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Board)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoardLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ContestId)));
    }

    public void Select(string? contestId)
    {
        if (string.IsNullOrWhiteSpace(contestId))
            SelectGlobal();
        else
            SelectContest(contestId);
    }
}

public sealed record BoardSourceOption(string? ContestId, string Label)
{
    public override string ToString() => Label;
}
