# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0] - 2026-08-22

### Added

- Update notification. At startup the app reads the project's public GitHub Releases feed and, when a newer version exists, shows a popup with the release notes and an **Update now** button. Updating downloads the official installer from the repository, verifies its SHA-256 digest and size, runs it silently (per user, no administrator rights) and restarts the app on the new version. A banner under the title bar stays visible until you update, and Settings gains an **Updates** card with an on/off switch for the automatic check and a **Check now** button. The check sends no personal data (only the app name and version as User-Agent) and can be turned off.

### Fixed

- Closing the main window now really exits the app. The AeroNav browser lives in a hidden helper window, and with the WPF default shutdown mode that hidden window kept the process (and its WebView2 browser processes) alive invisibly after the main window was closed. Those leftover instances held the browser profile open, so a later launch started a second browser on the same profile and the saved AeroNav session became unreliable, which is why the dashboard could come back without the "Up to date" statuses and sign-in would not validate. The app now uses `OnMainWindowClose`, disposes the browser on exit, runs as a single instance (a second launch just brings the existing window to the front), and retires any invisible leftover instance from an older version on startup.
- The AeroNav sign-in now really survives restarts. AeroNav issues its session as a browser-session cookie, which the browser discards when it exits, so with a proper shutdown the user would have been asked to sign in on every launch (the old version only appeared to remember it because its process never exited). The app now gives those cookies a bounded 30-day lifetime inside the same isolated profile; they are never read out for any other purpose, never logged, and Logout still clears them. When AeroNav expires the session on its side, the dashboard simply asks to sign in again.
- When the saved AeroNav session is gone, the app no longer looks frozen for 20 seconds. The silent session check recognises the AeroNav sign-in page (not only redirects to VATSIM/Navigraph hosts) and gives up within a few seconds; the dashboard shows a "Checking saved session…" banner right away, with the **Authenticate** button available so you can sign in immediately instead of waiting.
- Sign-in is detected automatically. AeroNav injects the package list via JavaScript after the page loads, so the old one-shot check at navigation end usually missed it and the user had to press the manual "I'm signed in" button. The sign-in window now polls the page and closes itself as soon as the listing appears.
- An expired session is detected on refresh and during operations. The listing is reloaded on every refresh (instead of reusing a stale page), a download that never starts is reported as "session expired" instead of a generic failure, the tools are gated again, and the app offers to sign in and retry the same operation.
- Closing the AeroNav sign-in/download window now just cancels that step; the browser stays alive (hidden) for the next interaction instead of being torn down.
- Authentication and session events are now logged (without any URLs, cookies or tokens) so issues like this can be diagnosed from `logs\app-*.log`.

## [1.0.3] - 2026-08-18

### Fixed

- Installing a second package in the same session no longer gets blocked. Chromium prompts "this site is trying to download multiple files" on the second download, and choosing "Block" silently denied every later download until the app was restarted. The app now automatically allows the multiple-downloads permission for the AeroNav session, so the user can install several FIRs in a row without any prompt or restart.

## [1.0.2] - 2026-08-17

### Changed

- The uninstaller now also removes the (now empty) program folder and its parent, and clears the legacy `EuroScopeUpdater` data folder from the pre-rename identity, so nothing is left behind. Validated end to end with a silent install, uninstall, and reinstall cycle.

## [1.0.1] - 2026-08-17

### Fixed

- Installed packages are now identified from the actual sector file on disk (the `.sct` EuroScope loads), not from a stored manifest. A manifest that disagrees with what is on disk (an interrupted install, or files changed outside the app) no longer causes a FIR to show a phantom version or a false "locally modified" state. The dashboard now shows the real installed AIRAC and offers the correct update.

### Changed

- The uninstaller now removes everything, including the local app data folder (`%LOCALAPPDATA%\VectorsATCGroup\EuroScopeSectorFileManager`): settings, installed-state manifests, backups, logs, and the WebView2 session profile. A later reinstall starts fresh and shows the first-run wizard.
- The installer detects an existing installation and offers three choices: Update, Repair, or Uninstall.
- Corrected the FIR names: SBAO is Atlântico FIR and SBAZ is Amazônica FIR (they were swapped).

### Dependencies

- Bump `CommunityToolkit.Mvvm` to 8.4.2, `Microsoft.Web.WebView2` to 1.0.4129.50, `Microsoft.NET.Test.Sdk` to 18.9.0, and `Microsoft.Extensions.*`.
- Bump GitHub Actions (`actions/setup-dotnet`, and the remaining `actions/checkout` and `actions/upload-artifact`), removing the Node 20 deprecation warning.

## [1.0.0] - 2026-08-17

First public release.

### Added

- One click install and update of EuroScope Sector Files for every VATSIM Brasil FIR (SBAO, SBAZ, SBBS, SBCW, SBRE).
- Downloads exclusively from the official AeroNav source, no files are redistributed by the project.
- Transactional install and update engine: changes are staged, an automatic backup is taken, and the operation is committed atomically with rollback on failure.
- Preservation of user personalization (the `Settings` folder and custom files) across updates.
- Dashboard that always shows the installed AIRAC per FIR and the available package, even before signing in.
- Privacy-first authentication: sign-in happens only on the official AeroNav, VATSIM, and Navigraph pages inside an isolated WebView2 profile. The app never sees, types, intercepts, stores, or logs passwords.
- No telemetry, no analytics, no backend.
- Portuguese and English localization with live switching.
- Light and dark themes.
- Per-user Windows installer (Inno Setup) that also installs the Microsoft WebView2 runtime when missing.

### Security

- Archive extraction protected against path traversal (zip slip).
- Cookies, tokens, authorization headers, and signed URLs are never logged.

[Unreleased]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.3...v1.1.0
[1.0.3]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/tag/v1.0.0
