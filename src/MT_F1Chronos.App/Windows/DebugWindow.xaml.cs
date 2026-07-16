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
    private static readonly (string Title, Func<TelemetryDebugSnapshot, string> Builder)[] Sections =
    [
        ("Connexion", BuildConnectionText),
        ("Session", BuildSessionText),
        ("Voiture active (Lap Data)", BuildActiveCarText),
        ("Lap Data — toutes les voitures", BuildCarsText),
        ("Time Trial", BuildTimeTrialText),
        ("Événements / dernier update", BuildEventsText),
        ("État parsé (TelemetryState)", BuildParsedStateText),
        ("SessionStore", BuildStoreText),
        ("Compteurs paquets", BuildPacketCountsText),
        ("Log — 20 derniers paquets", BuildPacketLogText),
    ];

    private readonly AppController _controller;
    private readonly DispatcherTimer _refreshTimer;
    private readonly TextBlock[] _bodyBlocks;

    public DebugWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        _bodyBlocks = new TextBlock[Sections.Length];
        for (var i = 0; i < Sections.Length; i++)
            SectionsPanel.Children.Add(CreateSection(Sections[i].Title, out _bodyBlocks[i]));

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        Closed += (_, _) => _refreshTimer.Stop();
        Refresh();
    }

    private void Refresh()
    {
        var snapshot = _controller.BuildDebugSnapshot();
        for (var i = 0; i < Sections.Length; i++)
            _bodyBlocks[i].Text = Sections[i].Builder(snapshot);
    }

    private static UIElement CreateSection(string title, out TextBlock body)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x16, 0x1B, 0x22)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xCA, 0xD3)),
            Margin = new Thickness(0, 0, 0, 6),
        });

        body = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(body);
        border.Child = panel;
        return border;
    }

    private static string BuildConnectionText(TelemetryDebugSnapshot s) =>
        $"""
        cfg {s.ConfiguredFormat} · rx {s.ReceivedFormat}
        état : {(s.IsConnected ? "connecté" : "déconnecté")}
        débit : {s.PacketsPerSecond} pkt/s
        dernier paquet : il y a {s.SecondsSinceLastPacket:F1}s
        """;

    private static string BuildSessionText(TelemetryDebugSnapshot s) =>
        $"""
        trackId brut : {s.RawTrackId} ({F1UdpConstants.GetTrackName(s.RawTrackId)})
        trackId résolu : {s.ResolvedTrackId} ({F1UdpConstants.GetTrackName(s.ResolvedTrackId)})
        longueur circuit : {s.TrackLengthMeters} m
        sessionType : {s.SessionType} {(s.IsTimeTrial ? "(Time Trial)" : "")}
        gameMode : {s.GameMode}
        sessionUid : {s.SessionUid}
        """;

    private static string BuildActiveCarText(TelemetryDebugSnapshot s) =>
        $"""
        playerCarIndex : {s.PlayerCarIndex}
        resolvedCarIndex : {s.ResolvedCarIndex}
        lapData offset : {s.LapDataOffset} (taille {s.LapDataSize} o/voiture)
        lastLap brut : {s.RawLastLapMs} ms ({FormatMs(s.RawLastLapMs)})
        currentLap brut : {s.RawCurrentLapMs} ms ({FormatMs(s.RawCurrentLapMs)})
                driverStatus brut : {s.DriverStatus} ({DriverStatusLabel(s.DriverStatus)})
        currentLapInvalid : {s.CurrentLapInvalid} {(s.CurrentLapInvalid == 1 ? "(cut — non enregistré)" : "")}
        """;

    private static string BuildCarsText(TelemetryDebugSnapshot s)
    {
        if (s.Cars.Count == 0)
            return "Ouvre Debug pendant que des Lap Data arrivent, ou aucun paquet encore.";

        var sb = new StringBuilder();
        sb.AppendLine("car | lastLap      | currentLap   | drv | flags");
        sb.AppendLine("----+--------------+--------------+-----+------");

        foreach (var car in s.Cars)
        {
            if (car.LastLapMs == 0 && car.CurrentLapMs == 0 && car.DriverStatus == 0)
                continue;

            var flags = new List<string>(2);
            if (car.IsPlayerCar) flags.Add("P");
            if (car.IsResolvedCar) flags.Add("R");

            sb.AppendLine(
                $"{car.CarIndex,3} | {FormatMs(car.LastLapMs),12} | {FormatMs(car.CurrentLapMs),12} | {car.DriverStatus,3} | {string.Join(',', flags)}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildTimeTrialText(TelemetryDebugSnapshot s) =>
        $"""
        session best (brut) : {s.TimeTrialSessionBestMs} ms ({FormatMs(s.TimeTrialSessionBestMs)})
        personal best (brut) : {s.TimeTrialPersonalBestMs} ms ({FormatMs(s.TimeTrialPersonalBestMs)})
        """;

    private static string BuildEventsText(TelemetryDebugSnapshot s) =>
        $"""
        dernier event : {s.LastEventCode}
        lapCompleted : {BoolLabel(s.LastLapCompleted)}
        completedLapMs : {FormatNullableMs(s.LastCompletedLapMs)}
        sessionStarted : {BoolLabel(s.LastSessionStarted)}
        sessionEnded : {BoolLabel(s.LastSessionEnded)}
        trackChanged : {BoolLabel(s.LastTrackChanged)}
        """;

    private static string BuildParsedStateText(TelemetryDebugSnapshot s) =>
        $"""
        sessionBest : {FormatNullableMs(s.ParsedSessionBestMs)}
        personalBest : {FormatNullableMs(s.ParsedPersonalBestMs)}
        currentLastLap : {FormatNullableMs(s.ParsedCurrentLastLapMs)}
        currentLap : {FormatNullableMs(s.ParsedCurrentLapMs)}
        """;

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
                circuit : {store.ActiveTrackId} ({store.ActiveTrackName})
                dernier tour : {FormatNullableMs(store.ActiveBestLapMs)}
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

        foreach (var (title, builder) in Sections)
        {
            content.AppendLine($"[{title}]");
            content.AppendLine(builder(snapshot));
            content.AppendLine();
        }

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
        ms is > 0 ? $"{ms.Value} ms ({LapTimeFormatter.Format(ms.Value)})" : "—";

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
