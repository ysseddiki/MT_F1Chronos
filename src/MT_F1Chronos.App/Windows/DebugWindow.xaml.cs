using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.Core.Services;
using MT_F1Chronos.Core.Telemetry;

namespace MT_F1Chronos.App.Windows;

public partial class DebugWindow : Window
{
    private readonly AppController _controller;
    private readonly DispatcherTimer _refreshTimer;

    public DebugWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Closed += (_, _) => _refreshTimer.Stop();
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _controller.BuildDebugSnapshot();
        SectionsPanel.Children.Clear();

        SectionsPanel.Children.Add(CreateSection("Connexion", BuildConnectionText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Session", BuildSessionText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Voiture active (Lap Data)", BuildActiveCarText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Lap Data — toutes les voitures", BuildCarsText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Time Trial", BuildTimeTrialText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Événements / dernier update", BuildEventsText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("État parsé (TelemetryState)", BuildParsedStateText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("SessionStore", BuildStoreText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Compteurs paquets", BuildPacketCountsText(snapshot)));
        SectionsPanel.Children.Add(CreateSection("Log — 20 derniers paquets", BuildPacketLogText(snapshot)));
    }

    private static UIElement CreateSection(string title, string content)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x44, 0x1A, 0x1A, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = content,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
        });

        border.Child = panel;
        return border;
    }

    private static string BuildConnectionText(TelemetryDebugSnapshot s)
    {
        var status = s.IsConnected ? "connecté" : "déconnecté";
        return $"""
                cfg {s.ConfiguredFormat} · rx {s.ReceivedFormat}
                état : {status}
                débit : {s.PacketsPerSecond} pkt/s
                dernier paquet : il y a {s.SecondsSinceLastPacket:F1}s
                """;
    }

    private static string BuildSessionText(TelemetryDebugSnapshot s)
    {
        var rawName = F1UdpConstants.GetTrackName(s.RawTrackId);
        var resolvedName = F1UdpConstants.GetTrackName(s.ResolvedTrackId);
        return $"""
                trackId brut : {s.RawTrackId} ({rawName})
                trackId résolu : {s.ResolvedTrackId} ({resolvedName})
                longueur circuit : {s.TrackLengthMeters} m
                sessionType : {s.SessionType} {(s.IsTimeTrial ? "(Time Trial)" : "")}
                gameMode : {s.GameMode}
                sessionUid : {s.SessionUid}
                """;
    }

    private static string BuildActiveCarText(TelemetryDebugSnapshot s)
    {
        return $"""
                playerCarIndex : {s.PlayerCarIndex}
                resolvedCarIndex : {s.ResolvedCarIndex}
                lapData offset : {s.LapDataOffset} (taille {s.LapDataSize} o/voiture)
                lastLap brut : {s.RawLastLapMs} ms ({FormatMs(s.RawLastLapMs)})
                currentLap brut : {s.RawCurrentLapMs} ms ({FormatMs(s.RawCurrentLapMs)})
                driverStatus brut : {s.DriverStatus} ({DriverStatusLabel(s.DriverStatus)})
                """;
    }

    private static string BuildCarsText(TelemetryDebugSnapshot s)
    {
        if (s.Cars.Count == 0)
            return "Aucun paquet Lap Data reçu.";

        var sb = new StringBuilder();
        sb.AppendLine("car | lastLap      | currentLap   | drv | flags");
        sb.AppendLine("----+--------------+--------------+-----+------");

        foreach (var car in s.Cars)
        {
            if (car.LastLapMs == 0 && car.CurrentLapMs == 0 && car.DriverStatus == 0)
                continue;

            var flags = new List<string>();
            if (car.IsPlayerCar) flags.Add("P");
            if (car.IsResolvedCar) flags.Add("R");

            sb.AppendLine(
                $"{car.CarIndex,3} | {FormatMs(car.LastLapMs),12} | {FormatMs(car.CurrentLapMs),12} | {car.DriverStatus,3} | {string.Join(",", flags)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildTimeTrialText(TelemetryDebugSnapshot s)
    {
        return $"""
                session best (brut) : {s.TimeTrialSessionBestMs} ms ({FormatMs(s.TimeTrialSessionBestMs)})
                personal best (brut) : {s.TimeTrialPersonalBestMs} ms ({FormatMs(s.TimeTrialPersonalBestMs)})
                """;
    }

    private static string BuildEventsText(TelemetryDebugSnapshot s)
    {
        return $"""
                dernier event : {s.LastEventCode}
                lapCompleted : {BoolLabel(s.LastLapCompleted)}
                completedLapMs : {FormatNullableMs(s.LastCompletedLapMs)}
                sessionStarted : {BoolLabel(s.LastSessionStarted)}
                sessionEnded : {BoolLabel(s.LastSessionEnded)}
                trackChanged : {BoolLabel(s.LastTrackChanged)}
                """;
    }

    private static string BuildParsedStateText(TelemetryDebugSnapshot s)
    {
        return $"""
                sessionBest : {FormatNullableMs(s.ParsedSessionBestMs)}
                personalBest : {FormatNullableMs(s.ParsedPersonalBestMs)}
                currentLastLap : {FormatNullableMs(s.ParsedCurrentLastLapMs)}
                currentLap : {FormatNullableMs(s.ParsedCurrentLapMs)}
                """;
    }

    private static string BuildStoreText(TelemetryDebugSnapshot s)
    {
        var store = s.Store;
        if (!store.HasActiveSession)
        {
            return $"""
                    session active : non
                    fichier : {store.SessionsFilePath}
                    total sessions : {store.TotalSessions} ({store.ScoredSessions} avec chrono)
                    """;
        }

        return $"""
                session active : oui
                nom : {store.ActiveSessionName}
                circuit : {store.ActiveTrackId} ({store.ActiveTrackName})
                bestLapMs : {FormatNullableMs(store.ActiveBestLapMs)}
                fichier : {store.SessionsFilePath}
                total sessions : {store.TotalSessions} ({store.ScoredSessions} avec chrono)
                """;
    }

    private static string BuildPacketCountsText(TelemetryDebugSnapshot s)
    {
        if (s.PacketCounts.Count == 0)
            return "Aucun paquet reçu.";

        var sb = new StringBuilder();
        foreach (var (packetId, count) in s.PacketCounts.OrderBy(kv => kv.Key))
            sb.AppendLine($"{PacketName(packetId),-18} ({packetId,2}) : {count}");

        return sb.ToString().TrimEnd();
    }

    private static string BuildPacketLogText(TelemetryDebugSnapshot s)
    {
        if (s.PacketLog.Count == 0)
            return "Aucun paquet enregistré.";

        var sb = new StringBuilder();
        foreach (var entry in s.PacketLog)
        {
            sb.AppendLine(
                $"{entry.Timestamp:HH:mm:ss.fff}  {PacketName(entry.PacketId),-14} ({entry.PacketId})  {entry.BufferLength,5}o  {entry.Summary}");
        }

        return sb.ToString().TrimEnd();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var snapshot = _controller.BuildDebugSnapshot();
        var content = new StringBuilder();
        content.AppendLine("=== MT_F1Chronos Debug UDP ===");
        content.AppendLine($"Exporté le {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        content.AppendLine();
        content.AppendLine("[Connexion]");
        content.AppendLine(BuildConnectionText(snapshot));
        content.AppendLine();
        content.AppendLine("[Session]");
        content.AppendLine(BuildSessionText(snapshot));
        content.AppendLine();
        content.AppendLine("[Voiture active]");
        content.AppendLine(BuildActiveCarText(snapshot));
        content.AppendLine();
        content.AppendLine("[Lap Data — voitures]");
        content.AppendLine(BuildCarsText(snapshot));
        content.AppendLine();
        content.AppendLine("[Time Trial]");
        content.AppendLine(BuildTimeTrialText(snapshot));
        content.AppendLine();
        content.AppendLine("[Événements]");
        content.AppendLine(BuildEventsText(snapshot));
        content.AppendLine();
        content.AppendLine("[État parsé]");
        content.AppendLine(BuildParsedStateText(snapshot));
        content.AppendLine();
        content.AppendLine("[SessionStore]");
        content.AppendLine(BuildStoreText(snapshot));
        content.AppendLine();
        content.AppendLine("[Compteurs]");
        content.AppendLine(BuildPacketCountsText(snapshot));
        content.AppendLine();
        content.AppendLine("[Log 20 derniers paquets]");
        content.AppendLine(BuildPacketLogText(snapshot));

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MT_F1Chronos",
            $"debug-udp-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ToString());

        MessageBox.Show(this, $"Log exporté :\n{path}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static string FormatMs(uint ms) => ms == 0 ? "—" : LapTimeFormatter.Format(ms);

    private static string FormatNullableMs(uint? ms) =>
        ms is > 0 value ? $"{value} ms ({LapTimeFormatter.Format(value)})" : "—";

    private static string BoolLabel(bool value) => value ? "oui" : "non";

    private static string DriverStatusLabel(byte status) => status switch
    {
        0 => "in garage",
        1 => "flying lap",
        2 => "in lap",
        3 => "out lap",
        4 => "on track",
        _ => $"inconnu ({status})",
    };

    private static string PacketName(byte id) => id switch
    {
        0 => "Motion",
        1 => "Session",
        2 => "Lap Data",
        3 => "Event",
        4 => "Participants",
        5 => "Car Setups",
        6 => "Car Telemetry",
        7 => "Car Status",
        8 => "Final Classification",
        9 => "Lobby Info",
        10 => "Car Damage",
        11 => "Session History",
        12 => "Tyre Sets",
        13 => "Motion Ex",
        14 => "Time Trial",
        15 => "Lap Positions",
        _ => $"Packet {id}",
    };
}
