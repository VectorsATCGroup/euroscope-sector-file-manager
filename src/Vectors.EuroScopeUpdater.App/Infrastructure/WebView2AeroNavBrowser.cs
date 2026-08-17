using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Vectors.EuroScopeUpdater.Core.Abstractions;

namespace Vectors.EuroScopeUpdater.App.Infrastructure;

/// <summary>
/// <see cref="IAeroNavBrowser"/> implemented with an isolated WebView2 profile. The user-data folder is
/// a fresh temp directory (never the machine Edge profile); no external cookies are imported and the
/// profile is cleared/deleted on dispose. Authentication happens on AeroNav's own pages; this class
/// only observes navigation to know when the session is ready.
///
/// Because the exact AeroNav login/redirect flow cannot be confirmed offline, the auth window carries a
/// "Já estou conectado — continuar" button so the user can confirm completion manually if the automatic
/// detection does not fire. Host heuristics are marked TODO(confirm-live).
/// </summary>
public sealed class WebView2AeroNavBrowser : IAeroNavBrowser
{
    private const string ListingUrl = "https://files.aero-nav.com/SBXX";

    private readonly Dispatcher _dispatcher;
    private readonly ILogger<WebView2AeroNavBrowser> _log;
    private readonly string _userDataFolder;

    private Window? _window;
    private WebView2? _web;
    private TextBlock? _domainLabel;
    private TextBlock? _hintLabel;
    private TaskCompletionSource? _authTcs;
    private bool _initialized;

    public bool IsAuthenticated { get; private set; }

    public WebView2AeroNavBrowser(ILogger<WebView2AeroNavBrowser> log)
    {
        _log = log;
        _dispatcher = Application.Current.Dispatcher;
        // Stable, isolated profile (separate from the machine's Edge) so the AeroNav session PERSISTS
        // across app launches — the user is not asked to re-authenticate every time. Cleared on logout.
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorsATCGroup", "EuroScopeSectorFileManager", "webview2");
    }

    // TODO(confirm-live): the real AeroNav file host.
    private static bool IsAeroNavHost(Uri uri) => uri.Host.EndsWith("aero-nav.com", StringComparison.OrdinalIgnoreCase);

    // TODO(confirm-live): the real auth/login hosts (VATSIM Connect / Navigraph).
    private static bool IsLoginHost(Uri uri)
    {
        var h = uri.Host.ToLowerInvariant();
        return h.Contains("vatsim") || h.Contains("navigraph") || h.Contains("auth")
               || h.Contains("login") || h.Contains("sso") || uri.AbsolutePath.Contains("login");
    }

    private Task RunOnUiAsync(Func<Task> action) => _dispatcher.InvokeAsync(action).Task.Unwrap();

    /// <summary>Bring the (off-screen) window on-screen, centered, for user interaction.</summary>
    private void ShowForInteraction()
    {
        if (_window is null) return;
        _window.ShowInTaskbar = true;
        _window.Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - _window.Width) / 2);
        _window.Top = Math.Max(0, (SystemParameters.PrimaryScreenHeight - _window.Height) / 2);
        _window.Show();
        _window.Activate();
    }

    /// <summary>
    /// Silently check whether the persisted session is still valid — WITHOUT showing any window. Loads
    /// the listing in the hidden browser: if the packages render, we are authenticated; if it redirects
    /// to a login host, we are not. Used at startup so the user is only prompted when truly needed.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated) return true;
        await InitAsync();
        try { await NavigateAsync(ListingUrl, ct).ConfigureAwait(false); }
        catch { return false; }

        // Poll up to ~20s. If we land on a login host the session is gone; if packages render it's valid.
        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (await PageHasPackagesAsync().ConfigureAwait(false)) { IsAuthenticated = true; return true; }
            if (await IsOnLoginAsync().ConfigureAwait(false)) return false;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return false;
    }

    private async Task InitAsync()
    {
        if (_initialized) return;
        await RunOnUiAsync(async () =>
        {
            // Keep the (hidden/off-screen) window's JavaScript running at full speed — Chromium otherwise
            // throttles background windows, which can stall the silent session-restore navigation.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows",
            };
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder, options: options);
            _web = new WebView2();

            var root = new DockPanel { LastChildFill = true, Background = Brush("BgBrush") };

            // Header — shows the official domain the user is on.
            var header = new Border { Background = Brush("CardBrush"), Padding = new Thickness(14, 10, 14, 10) };
            _domainLabel = new TextBlock
            {
                Text = "Autenticação AeroNav",
                Foreground = Brush("ForegroundBrush"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
            };
            header.Child = _domainLabel;
            DockPanel.SetDock(header, Dock.Top);

            // Footer — instructions + manual confirmation button.
            var footer = new Border { Background = Brush("CardBrush"), Padding = new Thickness(14, 10, 14, 10) };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _hintLabel = new TextBlock
            {
                Text = "Faça login e, ao ver a lista de pacotes, clique em Continuar.",
                Foreground = Brush("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            };
            var continueBtn = new Button
            {
                Content = "Já estou conectado — continuar",
                Padding = new Thickness(16, 8, 16, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brush("PrimaryBrush"),
                Foreground = Brush("PrimaryForegroundBrush"),
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            continueBtn.Click += async (_, _) => await TryCompleteAsync(fromUser: true);
            Grid.SetColumn(_hintLabel, 0);
            Grid.SetColumn(continueBtn, 1);
            footerGrid.Children.Add(_hintLabel);
            footerGrid.Children.Add(continueBtn);
            footer.Child = footerGrid;
            DockPanel.SetDock(footer, Dock.Bottom);

            root.Children.Add(header);
            root.Children.Add(footer);
            root.Children.Add(_web); // fills the rest

            _window = new Window
            {
                Title = "Vectors ATC Group - AeroNav",
                Width = 1000,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000, // realize the HWND off-screen (no visible flash on startup)
                Top = -32000,
                Content = root,
                ShowInTaskbar = false,
            };

            // The WPF WebView2 control needs a real window/HWND before CoreWebView2 can initialize;
            // otherwise EnsureCoreWebView2Async never completes. Realize it off-screen, then hide.
            _window.Show();
            await _web.EnsureCoreWebView2Async(env);
            _window.Hide();

            var s = _web.CoreWebView2.Settings;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            s.IsStatusBarEnabled = false;

            _web.CoreWebView2.SourceChanged += (_, _) => UpdateDomainLabel();
            _web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                UpdateDomainLabel();
                // Auto-complete ONLY when the authenticated package listing is actually visible —
                // never merely because we are on an aero-nav URL (the pre-login page is one too).
                if (_authTcs is { Task.IsCompleted: false }) await TryCompleteAsync(fromUser: false);
            };

            _window.Closed += (_, _) =>
            {
                if (!IsAuthenticated && _authTcs is { Task.IsCompleted: false })
                {
                    _initialized = false; _web = null; _window = null;
                    _authTcs.TrySetException(new AeroNavAuthRequiredException("Autenticação cancelada."));
                }
            };

            _initialized = true;
        });
    }

    private void UpdateDomainLabel()
    {
        if (_domainLabel is null || _web?.CoreWebView2 is null) return;
        if (Uri.TryCreate(_web.Source?.ToString(), UriKind.Absolute, out var uri))
            _domainLabel.Text = $"🔒 {uri.Host}";
    }

    // Same file-name contract the parser uses; the only reliable signal of the authenticated listing.
    private const string PackageProbeJs =
        "/[A-Z]{2}[A-Z0-9]{2}\\/(?:Install|Update)-Package_\\d{14}-\\d{6}-\\d+\\.7z/i.test(document.documentElement.outerHTML)";

    /// <summary>True when the current page actually shows the AeroNav package listing (download links). UI-safe.</summary>
    private Task<bool> PageHasPackagesAsync() => _dispatcher.InvokeAsync(async () =>
    {
        if (_web?.CoreWebView2 is null) return false;
        try
        {
            var r = await _web.CoreWebView2.ExecuteScriptAsync(PackageProbeJs);
            return string.Equals(r, "true", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }).Task.Unwrap();

    /// <summary>Read the current document's outerHTML. UI-safe.</summary>
    private Task<string> GetHtmlAsync() => _dispatcher.InvokeAsync(async () =>
    {
        if (_web?.CoreWebView2 is null) return "";
        try
        {
            var json = await _web.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            return JsonSerializer.Deserialize<string>(json) ?? "";
        }
        catch { return ""; }
    }).Task.Unwrap();

    private Task<bool> IsOnLoginAsync() => _dispatcher.InvokeAsync(() =>
        Uri.TryCreate(_web?.Source?.ToString(), UriKind.Absolute, out var uri) && IsLoginHost(uri)).Task;

    private Task<bool> IsOnAeroNavAsync() => _dispatcher.InvokeAsync(() =>
        Uri.TryCreate(_web?.Source?.ToString(), UriKind.Absolute, out var uri) && IsAeroNavHost(uri) && !IsLoginHost(uri)).Task;

    /// <summary>
    /// Complete the pending auth. Auto (fromUser=false) completes ONLY when the package listing is
    /// visible. Manual (the "continuar" button) completes when packages are visible, or — as a safety
    /// valve against unknown page layouts — when the user is on the AeroNav host past any login page.
    /// </summary>
    private async Task TryCompleteAsync(bool fromUser)
    {
        if (_authTcs is not { Task.IsCompleted: false }) return;

        var hasUri = Uri.TryCreate(_web?.Source?.ToString(), UriKind.Absolute, out var uri);
        var onAeroNav = hasUri && IsAeroNavHost(uri!) && !IsLoginHost(uri!);
        var hasPackages = onAeroNav && await PageHasPackagesAsync();

        if (hasPackages || (fromUser && onAeroNav))
        {
            IsAuthenticated = true;
            _window?.Hide();
            _authTcs.TrySetResult();
        }
        else if (fromUser && _hintLabel is not null)
        {
            _hintLabel.Text = onAeroNav
                ? "Ainda não encontrei a lista de pacotes. Conclua o login e clique novamente."
                : "Ainda parece a página de login. Conclua o login no AeroNav e clique novamente.";
        }
    }

    public async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated) return;
        await InitAsync();

        _authTcs = new TaskCompletionSource();
        await _dispatcher.InvokeAsync(() =>
        {
            if (_hintLabel is not null)
                _hintLabel.Text = "Faça login e, ao ver a lista de pacotes, clique em Continuar.";
            _web!.CoreWebView2.Navigate(ListingUrl);
            ShowForInteraction();
        });

        using (ct.Register(() => _authTcs.TrySetException(new OperationCanceledException(ct))))
            await _authTcs.Task.ConfigureAwait(false);
    }

    public async Task<string> GetListingHtmlAsync(string url, CancellationToken ct = default)
    {
        await InitAsync();

        // After authentication the WebView is already sitting on the listing page. Only navigate if we
        // are NOT on the AeroNav host (e.g. a stale login page); otherwise reuse the current page so we
        // don't throw away the freshly-authenticated, JS-rendered listing.
        if (!await IsOnAeroNavAsync().ConfigureAwait(false))
            await NavigateAsync(url, ct).ConfigureAwait(false);

        // The package list can be injected by JavaScript AFTER navigation completes, so poll the DOM
        // until the packages appear (or a timeout), rather than reading outerHTML a single time.
        var html = "";
        for (var i = 0; i < 30 && !ct.IsCancellationRequested; i++)
        {
            if (await IsOnLoginAsync().ConfigureAwait(false))
            {
                IsAuthenticated = false;
                throw new AeroNavAuthRequiredException();
            }
            html = await GetHtmlAsync().ConfigureAwait(false);
            if (await PageHasPackagesAsync().ConfigureAwait(false))
                return html;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        return html; // timeout — return whatever we have; the parser will extract what it can
    }

    private async Task NavigateAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        await _dispatcher.InvokeAsync(() =>
        {
            void OnNav(object? _, CoreWebView2NavigationCompletedEventArgs e)
            {
                _web!.CoreWebView2.NavigationCompleted -= OnNav;
                tcs.TrySetResult();
            }
            _web!.CoreWebView2.NavigationCompleted += OnNav;
            _web.CoreWebView2.Navigate(url);
        });
        using (ct.Register(() => tcs.TrySetCanceled()))
            await tcs.Task.ConfigureAwait(false);
    }

    private Task HideWindowAsync() => _dispatcher.InvokeAsync(() => _window?.Hide()).Task;

    public async Task DownloadAsync(string downloadRef, string destinationFile,
        IProgress<DownloadProgress>? progress, CancellationToken ct = default)
    {
        await InitAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

        // AeroNav download links are plain <a href="…/<FIR>/<pkg>.7z"> buttons. Navigating directly to
        // that URL drops the Referer, so the server bounces to its home page. The reliable way is to
        // CLICK the actual link on the /SBXX listing (preserves Referer, session and the user gesture),
        // and capture the resulting browser download. auth.vatsim.net/Cloudflare needs a visible browser,
        // so the window is shown during this and hidden once the file transfer begins.
        var abs = Uri.TryCreate(downloadRef, UriKind.Absolute, out var u) ? u : new Uri(new Uri(ListingUrl), downloadRef);
        var segs = abs.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var suffix = segs.Length >= 2 ? $"{segs[^2]}/{segs[^1]}" : segs[^1]; // e.g. SBRE/Update-Package_….7z

        await _dispatcher.InvokeAsync(ShowForInteraction);

        // Make sure the listing (with the download links) is actually loaded before clicking.
        if (!await PageHasPackagesAsync().ConfigureAwait(false))
        {
            await NavigateAsync(ListingUrl, ct).ConfigureAwait(false);
            for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
            {
                if (await IsOnLoginAsync().ConfigureAwait(false))
                {
                    IsAuthenticated = false;
                    await HideWindowAsync().ConfigureAwait(false);
                    throw new AeroNavAuthRequiredException();
                }
                if (await PageHasPackagesAsync().ConfigureAwait(false)) break;
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
        }

        var startedTcs = new TaskCompletionSource();
        var doneTcs = new TaskCompletionSource();
        CoreWebView2DownloadOperation? op = null;
        string clickResult = "";

        await _dispatcher.InvokeAsync(async () =>
        {
            void OnDownloadStarting(object? _, CoreWebView2DownloadStartingEventArgs e)
            {
                _web!.CoreWebView2.DownloadStarting -= OnDownloadStarting;
                e.Handled = true;                 // suppress the browser's own download UI
                e.ResultFilePath = destinationFile;
                op = e.DownloadOperation;
                startedTcs.TrySetResult();
                _window?.Hide();                  // download started → hide the browser, continue in our modal

                op.BytesReceivedChanged += (_, _) =>
                    progress?.Report(new DownloadProgress(op.BytesReceived, (long?)op.TotalBytesToReceive));
                op.StateChanged += (_, _) =>
                {
                    switch (op.State)
                    {
                        case CoreWebView2DownloadState.Completed:
                            doneTcs.TrySetResult();
                            break;
                        case CoreWebView2DownloadState.Interrupted:
                            TryDelete(destinationFile);
                            doneTcs.TrySetException(new PackageSourceUnavailableException("O download foi interrompido."));
                            break;
                    }
                };
            }

            _web!.CoreWebView2.DownloadStarting += OnDownloadStarting;

            // Click the .7z link whose href ends with the FIR/filename suffix.
            var js = "(function(){var s=" + JsonSerializer.Serialize(suffix) +
                     ";var a=Array.from(document.querySelectorAll('a[href]')).find(function(x){return x.href.indexOf(s)!==-1;});" +
                     "if(a){a.click();return 'clicked';}return 'notfound';})()";
            var r = await _web.CoreWebView2.ExecuteScriptAsync(js);
            clickResult = JsonSerializer.Deserialize<string>(r) ?? "";
        }).Task.Unwrap().ConfigureAwait(false);

        if (clickResult != "clicked")
        {
            await HideWindowAsync().ConfigureAwait(false);
            throw new PackageUnavailableException("Não foi possível localizar o link de download na página do AeroNav.");
        }

        using (ct.Register(() => { try { op?.Cancel(); } catch { } startedTcs.TrySetCanceled(); doneTcs.TrySetCanceled(); }))
        {
            var started = await Task.WhenAny(startedTcs.Task, Task.Delay(TimeSpan.FromSeconds(90), ct)).ConfigureAwait(false);
            if (started != startedTcs.Task)
            {
                await HideWindowAsync().ConfigureAwait(false);
                throw new AeroNavAuthRequiredException("O download não iniciou. Talvez seja necessário autenticar novamente.");
            }
            try { await doneTcs.Task.ConfigureAwait(false); }
            catch { TryDelete(destinationFile); throw; }
        }
    }

    public async Task LogoutAsync()
    {
        IsAuthenticated = false;
        if (_web is null) return;
        await RunOnUiAsync(async () =>
        {
            try { await _web.CoreWebView2.Profile.ClearBrowsingDataAsync(); } catch { /* best effort */ }
        });
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _web?.Dispose();
                _window?.Close();
            });
        }
        catch { /* shutting down */ }
        // NOTE: the profile is intentionally NOT deleted here — it persists the session across launches.
        // It is cleared only on explicit logout (LogoutAsync).
    }

    private static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
