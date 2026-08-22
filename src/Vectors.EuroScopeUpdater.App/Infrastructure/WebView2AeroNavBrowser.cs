using System.Diagnostics;
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
/// <see cref="IAeroNavBrowser"/> implemented with an isolated WebView2 profile (a stable folder under the
/// app's data directory, never the machine Edge profile) so the AeroNav session survives app restarts.
/// No external cookies are imported; the profile is cleared only on explicit logout. Authentication
/// happens on AeroNav's own pages; this class only observes navigation/DOM to know when the session is
/// ready. Nothing sensitive (cookies, tokens, signed URLs) is ever read out or logged.
///
/// Session validity is always judged by the ONLY reliable signal, the authenticated package listing
/// actually rendering in the DOM (AeroNav injects it via JavaScript after navigation), never by merely
/// being on an aero-nav.com URL, because the pre-login page lives on the same host and path.
/// </summary>
public sealed class WebView2AeroNavBrowser : IAeroNavBrowser
{
    private const string ListingUrl = "https://files.aero-nav.com/SBXX";

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ListingTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan LoggedOutGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ListingReuseWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PersistedSessionLifetime = TimeSpan.FromDays(30);
    private static readonly string[] AeroNavCookieOrigins = { "https://files.aero-nav.com", "https://aero-nav.com" };

    private readonly Dispatcher _dispatcher;
    private readonly ILogger<WebView2AeroNavBrowser> _log;
    private readonly string _userDataFolder;

    private Window? _window;
    private WebView2? _web;
    private TextBlock? _domainLabel;
    private TextBlock? _hintLabel;
    private TaskCompletionSource? _authTcs;
    private TaskCompletionSource? _downloadTcs;
    private bool _initialized;
    private bool _shuttingDown;
    private DateTime? _listingLoadedUtc;

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

    private static bool IsAeroNavHost(Uri uri) => uri.Host.EndsWith("aero-nav.com", StringComparison.OrdinalIgnoreCase);

    // VATSIM Connect / Navigraph / generic SSO hosts, or an explicit login path.
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
    /// to a login host, or the AeroNav page itself shows a sign-in link instead of packages, we are not.
    /// Bounded by <see cref="RestoreTimeout"/> so the UI never looks hung. Cancellation (the user chose to
    /// authenticate right away) surfaces as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        if (IsAuthenticated) return true;
        await InitAsync();
        var sw = Stopwatch.StartNew();
        try { await NavigateAsync(ListingUrl, ct, NavigationTimeout).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogInformation("Session restore: listing navigation failed ({Reason})", ex.GetType().Name);
            return false;
        }

        while (sw.Elapsed < RestoreTimeout)
        {
            ct.ThrowIfCancellationRequested();
            if (await PageHasPackagesAsync().ConfigureAwait(false))
            {
                MarkListingLoaded();
                _log.LogInformation("Session restore: package listing visible after {Ms} ms, session valid", sw.ElapsedMilliseconds);
                return true;
            }
            if (await IsOnLoginAsync().ConfigureAwait(false))
            {
                _log.LogInformation("Session restore: redirected to a sign-in host after {Ms} ms, login required", sw.ElapsedMilliseconds);
                return false;
            }
            if (sw.Elapsed > LoggedOutGrace && await PageLooksLoggedOutAsync().ConfigureAwait(false))
            {
                _log.LogInformation("Session restore: AeroNav shows a sign-in link and no packages after {Ms} ms, login required", sw.ElapsedMilliseconds);
                return false;
            }
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
        _log.LogInformation("Session restore: no package listing after {Ms} ms, treating as login required", sw.ElapsedMilliseconds);
        return false;
    }

    private void MarkListingLoaded()
    {
        IsAuthenticated = true;
        _listingLoadedUtc = DateTime.UtcNow;
        _ = PersistAeroNavSessionAsync();
    }

    /// <summary>
    /// Make the AeroNav sign-in survive app restarts. AeroNav issues its session as a browser-session
    /// cookie, which Chromium discards when the browser process ends, so with a clean shutdown the user
    /// would be asked to sign in on every launch (the old version only "remembered" the session because
    /// its process never actually exited). Give those cookies a bounded lifetime instead: they stay
    /// exactly where they already live, inside this app's isolated WebView2 profile, are never read out
    /// for any other purpose, never logged, and are cleared by Logout. The server still expires the
    /// session on its own terms; when that happens the dashboard gates and asks to sign in again.
    /// </summary>
    private Task PersistAeroNavSessionAsync() => _dispatcher.InvokeAsync(async () =>
    {
        if (_web?.CoreWebView2 is null) return;
        try
        {
            var manager = _web.CoreWebView2.CookieManager;
            var expires = DateTime.Now.Add(PersistedSessionLifetime);
            var persisted = 0;
            foreach (var origin in AeroNavCookieOrigins)
            {
                var cookies = await manager.GetCookiesAsync(origin);
                foreach (var cookie in cookies)
                {
                    if (!cookie.IsSession) continue;
                    cookie.Expires = expires;
                    manager.AddOrUpdateCookie(cookie);
                    persisted++;
                }
            }
            if (persisted > 0)
                _log.LogInformation("AeroNav session: {Count} session cookie(s) given a {Days}-day lifetime in the isolated profile",
                    persisted, (int)PersistedSessionLifetime.TotalDays);
        }
        catch (Exception ex)
        {
            _log.LogWarning("AeroNav session could not be persisted: {Message}", ex.Message);
        }
    }).Task.Unwrap();

    private async Task InitAsync()
    {
        if (_initialized) return;
        await RunOnUiAsync(async () =>
        {
            if (_initialized) return;
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
                Text = "Faça login. Assim que a lista de pacotes aparecer, esta janela fecha sozinha.",
                Foreground = Brush("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            };
            var continueBtn = new Button
            {
                Content = "Já estou conectado, continuar",
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

            // Installing more than one FIR in a single session triggers Chromium's "this site wants to
            // download multiple files" prompt. If the user picks "Block", every further download in the
            // session is silently denied and the only recovery is restarting the app. Auto-allow just this
            // one permission kind (downloads the user explicitly initiated from AeroNav) so a second, third
            // package installs without any prompt; all other permission kinds keep their default handling.
            _web.CoreWebView2.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind == CoreWebView2PermissionKind.MultipleAutomaticDownloads)
                {
                    e.State = CoreWebView2PermissionState.Allow;
                    e.Handled = true;
                }
            };

            _web.CoreWebView2.SourceChanged += (_, _) => UpdateDomainLabel();
            _web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                UpdateDomainLabel();
                // Auto-complete ONLY when the authenticated package listing is actually visible —
                // never merely because we are on an aero-nav URL (the pre-login page is one too).
                if (_authTcs is { Task.IsCompleted: false }) await TryCompleteAsync(fromUser: false);
            };

            // The user closing the auth/download window means "cancel what you were doing", NOT "destroy
            // the browser": keep the WebView2 alive (hidden) so the next interaction reuses the same
            // profile and environment. It is only truly closed when the application shuts down.
            _window.Closing += (_, e) =>
            {
                if (_shuttingDown) return;
                e.Cancel = true;
                _window.Hide();
                _window.ShowInTaskbar = false;
                _authTcs?.TrySetException(new AeroNavAuthRequiredException("Autenticação cancelada."));
                _downloadTcs?.TrySetException(new OperationCanceledException("Download cancelado pelo usuário."));
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

    // A sign-in affordance on the AeroNav page itself (a link/form to a login or SSO endpoint). Used
    // only AFTER a grace period and only when no packages rendered, so a footer link can at worst cost
    // the user one extra click, never a wrong "authenticated" verdict.
    private const string LoggedOutProbeJs =
        "(function(){var h=document.documentElement.outerHTML;" +
        "return /href=[\"'][^\"']*(?:\\/login|login\\.|auth\\.vatsim\\.net|navigraph\\.com\\/|\\/oauth|\\/sso)[^\"']*[\"']/i.test(h)" +
        "||/<form[^>]*action=[\"'][^\"']*(?:login|auth|sso)[^\"']*[\"']/i.test(h);})()";

    /// <summary>True when the current page actually shows the AeroNav package listing (download links). UI-safe.</summary>
    private Task<bool> PageHasPackagesAsync() => ProbeAsync(PackageProbeJs);

    /// <summary>True when the current AeroNav page shows a sign-in link/form instead of packages. UI-safe.</summary>
    private async Task<bool> PageLooksLoggedOutAsync() =>
        await IsOnAeroNavAsync().ConfigureAwait(false)
        && !await PageHasPackagesAsync().ConfigureAwait(false)
        && await ProbeAsync(LoggedOutProbeJs).ConfigureAwait(false);

    private Task<bool> ProbeAsync(string js) => _dispatcher.InvokeAsync(async () =>
    {
        if (_web?.CoreWebView2 is null) return false;
        try
        {
            var r = await _web.CoreWebView2.ExecuteScriptAsync(js);
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
    /// Must run on the UI thread.
    /// </summary>
    private async Task TryCompleteAsync(bool fromUser)
    {
        if (_authTcs is not { Task.IsCompleted: false }) return;

        var hasUri = Uri.TryCreate(_web?.Source?.ToString(), UriKind.Absolute, out var uri);
        var onAeroNav = hasUri && IsAeroNavHost(uri!) && !IsLoginHost(uri!);
        var hasPackages = onAeroNav && await PageHasPackagesAsync();

        if (hasPackages || (fromUser && onAeroNav))
        {
            MarkListingLoaded();
            _log.LogInformation("Authentication completed ({How})", fromUser ? "confirmed by user" : "package listing detected");
            _window?.Hide();
            if (_window is not null) _window.ShowInTaskbar = false;
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

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _authTcs = tcs;
        await _dispatcher.InvokeAsync(() =>
        {
            if (_hintLabel is not null)
                _hintLabel.Text = "Faça login. Assim que a lista de pacotes aparecer, esta janela fecha sozinha.";
            _web!.CoreWebView2.Navigate(ListingUrl);
            ShowForInteraction();
        });
        _log.LogInformation("Authentication window opened");

        // AeroNav injects the package list via JavaScript AFTER the navigation completes, so a single
        // check at NavigationCompleted usually misses it. Poll the DOM while the window is open and
        // auto-complete the moment the listing renders, so the user never needs the manual button.
        _ = PollForCompletionAsync(tcs, ct);

        using (ct.Register(() => tcs.TrySetException(new OperationCanceledException(ct))))
            await tcs.Task.ConfigureAwait(false);
    }

    private async Task PollForCompletionAsync(TaskCompletionSource tcs, CancellationToken ct)
    {
        try
        {
            while (!tcs.Task.IsCompleted && !ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                if (tcs.Task.IsCompleted) return;
                await _dispatcher.InvokeAsync(() => TryCompleteAsync(fromUser: false)).Task.Unwrap().ConfigureAwait(false);
            }
        }
        catch { /* polling is best effort; the window's own events and the manual button remain */ }
    }

    public async Task<string> GetListingHtmlAsync(string url, CancellationToken ct = default)
    {
        await InitAsync();

        // Right after authentication the WebView is already sitting on the freshly rendered listing;
        // reuse it briefly. Otherwise ALWAYS reload the listing so an expired session is detected here,
        // on refresh, instead of much later when a download silently fails to start.
        var fresh = _listingLoadedUtc is { } t && DateTime.UtcNow - t < ListingReuseWindow
                    && await IsOnAeroNavAsync().ConfigureAwait(false)
                    && await PageHasPackagesAsync().ConfigureAwait(false);
        if (!fresh)
            await NavigateAsync(url, ct, NavigationTimeout).ConfigureAwait(false);

        // The package list can be injected by JavaScript AFTER navigation completes, so poll the DOM
        // until the packages appear (or a timeout), rather than reading outerHTML a single time.
        var sw = Stopwatch.StartNew();
        var html = "";
        while (sw.Elapsed < ListingTimeout)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsOnLoginAsync().ConfigureAwait(false))
            {
                IsAuthenticated = false;
                _log.LogInformation("Listing: redirected to a sign-in host, session expired");
                throw new AeroNavAuthRequiredException();
            }
            html = await GetHtmlAsync().ConfigureAwait(false);
            if (await PageHasPackagesAsync().ConfigureAwait(false))
            {
                MarkListingLoaded();
                return html;
            }
            if (sw.Elapsed > LoggedOutGrace && await PageLooksLoggedOutAsync().ConfigureAwait(false))
            {
                IsAuthenticated = false;
                _log.LogInformation("Listing: AeroNav shows a sign-in link and no packages, session expired");
                throw new AeroNavAuthRequiredException();
            }
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }
        _log.LogWarning("Listing: no package links rendered within {Sec}s", (int)ListingTimeout.TotalSeconds);
        return html; // timeout — return whatever we have; the parser will extract what it can
    }

    private async Task NavigateAsync(string url, CancellationToken ct, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            if (timeout is null)
            {
                await tcs.Task.ConfigureAwait(false);
                return;
            }
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout.Value, ct)).ConfigureAwait(false);
            if (done != tcs.Task)
            {
                ct.ThrowIfCancellationRequested();
                throw new TimeoutException($"A navegação não foi concluída em {(int)timeout.Value.TotalSeconds}s.");
            }
            await tcs.Task.ConfigureAwait(false);
        }
    }

    private Task HideWindowAsync() => _dispatcher.InvokeAsync(() =>
    {
        _window?.Hide();
        if (_window is not null) _window.ShowInTaskbar = false;
    }).Task;

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
            await NavigateAsync(ListingUrl, ct, NavigationTimeout).ConfigureAwait(false);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < ListingTimeout)
            {
                ct.ThrowIfCancellationRequested();
                if (await IsOnLoginAsync().ConfigureAwait(false)
                    || (sw.Elapsed > LoggedOutGrace && await PageLooksLoggedOutAsync().ConfigureAwait(false)))
                {
                    IsAuthenticated = false;
                    await HideWindowAsync().ConfigureAwait(false);
                    _log.LogInformation("Download: session expired before the listing rendered");
                    throw new AeroNavAuthRequiredException();
                }
                if (await PageHasPackagesAsync().ConfigureAwait(false)) break;
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }

        var startedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _downloadTcs = doneTcs;
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
                if (_window is not null) _window.ShowInTaskbar = false;

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
            _downloadTcs = null;
            await HideWindowAsync().ConfigureAwait(false);
            throw new PackageUnavailableException("Não foi possível localizar o link de download na página do AeroNav.");
        }

        try
        {
            using (ct.Register(() => { try { op?.Cancel(); } catch { } startedTcs.TrySetCanceled(); doneTcs.TrySetCanceled(); }))
            {
                var started = await Task.WhenAny(startedTcs.Task, doneTcs.Task, Task.Delay(TimeSpan.FromSeconds(90), ct)).ConfigureAwait(false);
                if (started == doneTcs.Task)
                {
                    await doneTcs.Task.ConfigureAwait(false); // surfaces a cancellation from the closed window
                }
                else if (started != startedTcs.Task)
                {
                    await HideWindowAsync().ConfigureAwait(false);
                    // A download that never begins after a click almost always means the session is gone
                    // (AeroNav bounced the click to its sign-in page). Mark it so the UI asks to re-authenticate.
                    IsAuthenticated = false;
                    _log.LogInformation("Download: did not start within 90s after clicking the link, treating as session expired");
                    throw new AeroNavAuthRequiredException("O download não iniciou. Talvez seja necessário autenticar novamente.");
                }
                try { await doneTcs.Task.ConfigureAwait(false); }
                catch { TryDelete(destinationFile); throw; }
            }
        }
        finally
        {
            _downloadTcs = null;
            await HideWindowAsync().ConfigureAwait(false);
        }
    }

    public async Task LogoutAsync()
    {
        IsAuthenticated = false;
        _listingLoadedUtc = null;
        if (_web is null) return;
        await RunOnUiAsync(async () =>
        {
            try { await _web.CoreWebView2.Profile.ClearBrowsingDataAsync(); } catch { /* best effort */ }
            try { _web.CoreWebView2.Navigate("about:blank"); } catch { /* best effort */ }
        });
        _log.LogInformation("AeroNav session cleared (logout)");
    }

    public async ValueTask DisposeAsync()
    {
        _shuttingDown = true;
        try
        {
            // Called from Application.OnExit, i.e. ON the dispatcher thread: run inline there, because
            // posting to the dispatcher and waiting would never complete during shutdown.
            if (_dispatcher.CheckAccess()) DisposeCore();
            else await _dispatcher.InvokeAsync(DisposeCore);
        }
        catch { /* shutting down */ }
        // NOTE: the profile is intentionally NOT deleted here — it persists the session across launches.
        // It is cleared only on explicit logout (LogoutAsync).
    }

    private void DisposeCore()
    {
        _authTcs?.TrySetException(new AeroNavAuthRequiredException("Aplicativo encerrado."));
        _downloadTcs?.TrySetException(new OperationCanceledException("Aplicativo encerrado."));
        try { _web?.Dispose(); } catch { /* already gone */ }
        try { _window?.Close(); } catch { /* already gone */ }
        _web = null;
        _window = null;
        _initialized = false;
    }

    private static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
