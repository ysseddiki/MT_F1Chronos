using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using MT_F1Chronos.Core.Models;
using MT_F1Chronos.Core.Services;

namespace MT_F1Chronos.App.Services;

/// <summary>File-dialog + ScoreExporter orchestration for admin export.</summary>
public sealed class ScoreExportService
{
    public void Export(
        Window owner,
        IReadOnlyList<ChronoEntry> entries,
        string format,
        string filePrefix)
    {
        if (entries.Count == 0)
        {
            MessageBox.Show(owner, "Aucun score à exporter.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dialog = new SaveFileDialog
        {
            Title = "Exporter les scores",
            FileName = $"MT_F1Chronos-{filePrefix}-{stamp}",
            Filter = format switch
            {
                "csv" => "CSV (*.csv)|*.csv",
                "json" => "JSON (*.json)|*.json",
                _ => "HTML (*.html)|*.html",
            },
            DefaultExt = format,
            AddExtension = true,
        };

        if (dialog.ShowDialog(owner) != true)
            return;

        try
        {
            switch (format)
            {
                case "csv":
                    ScoreExporter.ExportCsv(entries, dialog.FileName);
                    break;
                case "json":
                    ScoreExporter.ExportJson(entries, dialog.FileName);
                    break;
                default:
                    ScoreExporter.ExportHtml(entries, dialog.FileName);
                    break;
            }

            var open = MessageBox.Show(
                owner,
                $"Export terminé :\n{dialog.FileName}\n\nOuvrir le fichier ?",
                "Export",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (open == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Échec de l'export :\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
