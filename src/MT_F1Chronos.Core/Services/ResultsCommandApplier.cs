using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

/// <summary>
/// Applies server jobs pulled by the simulator. Recording new laps stays local.
/// </summary>
public static class ResultsCommandApplier
{
    /// <param name="onSetPlayerName">
    /// Called when the server asks to rename the current session pseudo (setPlayerName).
    /// Without a handler the command stays pending (not ACKed).
    /// </param>
    public static IReadOnlyList<string> Apply(
        SessionStore store,
        ContestStore contests,
        IEnumerable<ResultsCommand> commands,
        Action<string>? onSetPlayerName = null)
    {
        var applied = new List<string>();

        foreach (var command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.Type))
                continue;

            switch (command.Type)
            {
                case ResultsCommandTypes.DeleteEntry:
                    if (string.IsNullOrWhiteSpace(command.EntryId))
                        break;
                    if (string.IsNullOrWhiteSpace(command.ContestId))
                        store.DeleteEntry(command.EntryId);
                    else
                        contests.DeleteEntry(command.ContestId, command.EntryId);
                    applied.Add(command.Id);
                    break;

                case ResultsCommandTypes.DeletePlayerOnTrack:
                    if (string.IsNullOrWhiteSpace(command.PlayerName) || command.TrackId is not >= 0)
                        break;
                    if (string.IsNullOrWhiteSpace(command.ContestId))
                        store.DeletePlayerOnTrack(command.PlayerName, command.TrackId.Value);
                    else
                        contests.DeletePlayerOnTrack(command.ContestId, command.PlayerName, command.TrackId.Value);
                    applied.Add(command.Id);
                    break;

                case ResultsCommandTypes.RenameEntry:
                    if (string.IsNullOrWhiteSpace(command.EntryId) || string.IsNullOrWhiteSpace(command.NewName))
                        break;
                    if (string.IsNullOrWhiteSpace(command.ContestId))
                        store.RenameEntry(command.EntryId, command.NewName);
                    else
                        contests.RenameEntry(command.ContestId, command.EntryId, command.NewName);
                    applied.Add(command.Id);
                    break;

                case ResultsCommandTypes.RenamePlayer:
                    if (string.IsNullOrWhiteSpace(command.PlayerName) || string.IsNullOrWhiteSpace(command.NewName))
                        break;
                    if (string.IsNullOrWhiteSpace(command.ContestId))
                        store.RenamePlayer(command.PlayerName, command.NewName);
                    else
                        contests.RenamePlayer(command.ContestId, command.PlayerName, command.NewName);
                    applied.Add(command.Id);
                    break;

                case ResultsCommandTypes.SetPlayerName:
                    if (string.IsNullOrWhiteSpace(command.NewName) || onSetPlayerName is null)
                        break;
                    var sessionName = command.NewName.Trim();
                    if (sessionName.Length > ResultsSyncProtocol.MaxPlayerNameLength)
                        sessionName = sessionName[..ResultsSyncProtocol.MaxPlayerNameLength];
                    onSetPlayerName(sessionName);
                    applied.Add(command.Id);
                    break;

                case ResultsCommandTypes.RestoreEntry:
                    if (string.IsNullOrWhiteSpace(command.EntryId) ||
                        command.TrackId is not >= 0 ||
                        command.BestLapMs is not > 0)
                        break;

                    var restored = new ChronoEntry
                    {
                        Id = command.EntryId,
                        Name = command.PlayerName ?? string.Empty,
                        TrackId = command.TrackId.Value,
                        TrackName = command.TrackName ?? "Inconnu",
                        BestLapMs = command.BestLapMs,
                        StartedAt = command.StartedAt ?? DateTime.UtcNow,
                        EndedAt = command.StartedAt ?? DateTime.UtcNow,
                    };

                    if (string.IsNullOrWhiteSpace(command.ContestId))
                        store.RestoreEntry(restored);
                    else
                        contests.RestoreEntry(command.ContestId, restored);
                    applied.Add(command.Id);
                    break;
            }
        }

        return applied;
    }
}
