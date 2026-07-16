using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MT_F1Chronos.Core.Models;

namespace MT_F1Chronos.Core.Services;

public static class ScoreExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void ExportCsv(IReadOnlyList<ChronoEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("rank,name,trackId,trackName,lapMs,lapTime,recordedAt");

        foreach (var group in GroupByTrack(entries))
        {
            var rank = 1;
            foreach (var entry in group.Entries)
            {
                sb.Append(rank++).Append(',')
                    .Append(Csv(entry.Name)).Append(',')
                    .Append(entry.TrackId).Append(',')
                    .Append(Csv(entry.TrackName)).Append(',')
                    .Append(entry.BestLapMs!.Value).Append(',')
                    .Append(LapTimeFormatter.Format(entry.BestLapMs.Value)).Append(',')
                    .Append(entry.StartedAt.ToString("o", CultureInfo.InvariantCulture))
                    .AppendLine();
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public static void ExportJson(IReadOnlyList<ChronoEntry> entries, string filePath)
    {
        var payload = GroupByTrack(entries).Select(g => new
        {
            trackId = g.TrackId,
            trackName = g.TrackName,
            scores = g.Entries.Select((e, i) => new
            {
                rank = i + 1,
                name = e.Name,
                lapMs = e.BestLapMs!.Value,
                lapTime = LapTimeFormatter.Format(e.BestLapMs.Value),
                recordedAt = e.StartedAt,
            }),
        });

        File.WriteAllText(filePath, JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
    }

    public static void ExportHtml(IReadOnlyList<ChronoEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <!DOCTYPE html>
            <html lang="fr">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>MT_F1Chronos — Scores</title>
              <style>
                :root {
                  --bg: #0d1117;
                  --panel: #161b22;
                  --text: #ffffff;
                  --muted: #c5cad3;
                  --red: #e10600;
                  --line: rgba(255,255,255,0.12);
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  font-family: "Segoe UI", system-ui, sans-serif;
                  background: linear-gradient(160deg, #0d1117 0%, #121820 100%);
                  color: var(--text);
                  min-height: 100vh;
                  padding: 32px 20px 48px;
                }
                .wrap { max-width: 860px; margin: 0 auto; }
                .hero {
                  display: flex;
                  align-items: stretch;
                  gap: 14px;
                  margin-bottom: 28px;
                }
                .accent {
                  width: 4px;
                  border-radius: 4px;
                  background: var(--red);
                }
                h1 {
                  margin: 0 0 6px;
                  font-size: 28px;
                  letter-spacing: 0.02em;
                }
                .meta { color: var(--muted); font-size: 13px; }
                .track {
                  background: rgba(22, 27, 34, 0.92);
                  border: 1px solid var(--line);
                  border-radius: 8px;
                  margin-bottom: 16px;
                  overflow: hidden;
                }
                .track-head {
                  display: flex;
                  justify-content: space-between;
                  align-items: baseline;
                  padding: 14px 16px;
                  border-bottom: 1px solid var(--line);
                }
                .track-head h2 {
                  margin: 0;
                  font-size: 16px;
                  text-transform: uppercase;
                  letter-spacing: 0.04em;
                }
                .track-head span { color: var(--muted); font-size: 12px; }
                table { width: 100%; border-collapse: collapse; }
                th, td {
                  padding: 10px 16px;
                  text-align: left;
                  font-size: 14px;
                }
                th {
                  color: var(--muted);
                  font-size: 11px;
                  text-transform: uppercase;
                  letter-spacing: 0.06em;
                  border-bottom: 1px solid var(--line);
                }
                td.rank {
                  color: var(--red);
                  font-weight: 700;
                  width: 56px;
                }
                td.time {
                  font-family: Consolas, "Courier New", monospace;
                  font-weight: 700;
                  text-align: right;
                }
                td.date {
                  color: var(--muted);
                  font-size: 12px;
                  text-align: right;
                  white-space: nowrap;
                }
                tr:nth-child(even) td { background: rgba(255,255,255,0.02); }
                .empty {
                  color: var(--muted);
                  padding: 24px;
                  text-align: center;
                }
                footer {
                  margin-top: 24px;
                  color: var(--muted);
                  font-size: 12px;
                }
              </style>
            </head>
            <body>
              <div class="wrap">
                <div class="hero">
                  <div class="accent"></div>
                  <div>
                    <h1>MT_F1Chronos</h1>
                    <div class="meta">Export scores · 
            """);
        sb.Append(WebUtility.HtmlEncode(DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"))));
        sb.AppendLine("""
            </div>
                  </div>
                </div>
            """);

        var groups = GroupByTrack(entries).ToList();
        if (groups.Count == 0)
        {
            sb.AppendLine("""<div class="track"><div class="empty">Aucun score enregistré.</div></div>""");
        }
        else
        {
            foreach (var group in groups)
            {
                sb.AppendLine("""
                    <section class="track">
                      <div class="track-head">
                        <h2>
                    """);
                sb.Append(WebUtility.HtmlEncode(group.TrackName.ToUpperInvariant()));
                sb.AppendLine("</h2>");
                sb.Append("                        <span>")
                    .Append(group.Entries.Count)
                    .AppendLine(" chrono(s)</span>")
                    .AppendLine("""
                      </div>
                      <table>
                        <thead>
                          <tr>
                            <th>Rang</th>
                            <th>Pilote</th>
                            <th style="text-align:right">Temps</th>
                            <th style="text-align:right">Date</th>
                          </tr>
                        </thead>
                        <tbody>
                    """);

                var rank = 1;
                foreach (var entry in group.Entries)
                {
                    sb.AppendLine("                          <tr>");
                    sb.Append("                            <td class=\"rank\">").Append(rank++).AppendLine(".</td>");
                    sb.Append("                            <td>").Append(WebUtility.HtmlEncode(entry.Name)).AppendLine("</td>");
                    sb.Append("                            <td class=\"time\">")
                        .Append(LapTimeFormatter.Format(entry.BestLapMs!.Value))
                        .AppendLine("</td>");
                    sb.Append("                            <td class=\"date\">")
                        .Append(WebUtility.HtmlEncode(entry.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"))))
                        .AppendLine("</td>");
                    sb.AppendLine("                          </tr>");
                }

                sb.AppendLine("""
                        </tbody>
                      </table>
                    </section>
                    """);
            }
        }

        sb.AppendLine("""
                <footer>Généré par MT_F1Chronos</footer>
              </div>
            </body>
            </html>
            """);

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static IEnumerable<(int TrackId, string TrackName, List<ChronoEntry> Entries)> GroupByTrack(
        IReadOnlyList<ChronoEntry> entries) =>
        entries
            .Where(e => e.BestLapMs is > 0)
            .GroupBy(e => e.TrackId)
            .Select(g =>
            {
                var ordered = g.OrderBy(e => e.BestLapMs).ThenBy(e => e.StartedAt).ToList();
                var name = ordered
                    .OrderByDescending(e => e.StartedAt)
                    .Select(e => e.TrackName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Inconnu";
                return (g.Key, name, ordered);
            })
            .OrderBy(g => g.name, StringComparer.OrdinalIgnoreCase);

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
