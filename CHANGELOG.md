# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/tag/v1.0.0
