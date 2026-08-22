using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Vectors.EuroScopeUpdater.App.Services;
using Vectors.EuroScopeUpdater.Core.Abstractions;
using Vectors.EuroScopeUpdater.Core.Updates;

namespace Vectors.EuroScopeUpdater.App.ViewModels;

/// <summary>
/// "A new version is available" dialog: shows what is new, and either updates in place (download →
/// verify → installer → restart) or lets the user postpone. Closing is requested through
/// <see cref="RequestClose"/> so the view stays passive.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly ILogger _log;
    private CancellationTokenSource? _cts;

    public AppRelease Release { get; }

    public UpdateViewModel(AppRelease release, IUpdateService updates, ILogger log)
    {
        Release = release;
        _updates = updates;
        _log = log;
        NotesText = CleanNotes(release.Notes);
    }

    private static Localization Loc => Localization.Instance;

    public string LatestVersionText => Release.VersionText;
    public string CurrentVersionText => AppVersions.Format(_updates.CurrentVersion);
    public string BodyText => string.Format(Loc.T("Upd_Body"), LatestVersionText, CurrentVersionText);
    public string NotesText { get; }
    public bool HasNotes => !string.IsNullOrWhiteSpace(NotesText);
    public bool HasInstaller => Release.HasInstaller;
    public string ReleaseUrl => Release.HtmlUrl;

    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _indeterminate = true;
    [ObservableProperty] private string? _statusMessage;

    public string LaterButtonText => Busy ? Loc.T("Common_Cancel") : Loc.T("Upd_Later");

    partial void OnBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(LaterButtonText));
        InstallNowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event EventHandler? RequestClose;

    private bool CanInstallNow() => HasInstaller && !Busy;

    [RelayCommand(CanExecute = nameof(CanInstallNow))]
    private async Task InstallNowAsync()
    {
        if (!HasInstaller)
        {
            StatusMessage = Loc.T("Upd_NoInstaller");
            return;
        }
        Busy = true;
        Indeterminate = true;
        Percent = 0;
        StatusMessage = string.Format(Loc.T("Upd_Downloading"), "");
        _cts = new CancellationTokenSource();
        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.Fraction is { } f)
            {
                Indeterminate = false;
                Percent = f * 100;
                StatusMessage = string.Format(Loc.T("Upd_Downloading"), $"{FormatMb(p.BytesReceived)} / {FormatMb(p.TotalBytes ?? 0)} ({f:P0})");
            }
            else
            {
                StatusMessage = string.Format(Loc.T("Upd_Downloading"), FormatMb(p.BytesReceived));
            }
            if (p.TotalBytes is { } total && p.BytesReceived >= total)
                StatusMessage = Loc.T("Upd_Verifying");
        });

        try
        {
            await _updates.DownloadAndInstallAsync(Release, progress, _cts.Token);
            // If we get here the installer was launched and the app is shutting down.
            Indeterminate = false;
            Percent = 100;
            StatusMessage = Loc.T("Upd_Launching");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.T("Upd_Cancelled");
            Busy = false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "update download/launch failed");
            StatusMessage = string.Format(Loc.T("Upd_Failed"), ex.Message);
            Busy = false;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Later()
    {
        if (Busy) { _cts?.Cancel(); return; }
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        try { Process.Start(new ProcessStartInfo(ReleaseUrl) { UseShellExecute = true }); }
        catch (Exception ex) { _log.LogWarning(ex, "could not open release page"); }
    }

    private static string FormatMb(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";

    /// <summary>
    /// Release notes are GitHub-flavoured Markdown (auto-generated). Render a plain, readable version:
    /// strip heading markers, turn list markers into bullets, drop bare "Full Changelog" compare links
    /// and collapse blank lines.
    /// </summary>
    internal static string CleanNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        var lines = new List<string>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("**Full Changelog**", StringComparison.OrdinalIgnoreCase)) continue;
            line = Regex.Replace(line, @"^\s{0,3}#{1,6}\s*", "");                  // headings
            line = Regex.Replace(line, @"^\s*[-*+]\s+", "• ");                      // bullets
            line = Regex.Replace(line, @"\s+by\s+@[\w-]+\s+in\s+https?://\S+$", ""); // "by @user in <pr url>"
            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");                     // bold
            line = Regex.Replace(line, @"`([^`]*)`", "$1");                         // code spans
            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0)) continue;
            lines.Add(line);
        }
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join(Environment.NewLine, lines);
    }
}
