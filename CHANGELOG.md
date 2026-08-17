# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- Bump `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging`.
- Bump `Microsoft.Extensions.Logging.Abstractions` to 10.0.11.
- Bump `CommunityToolkit.Mvvm` to 8.4.2.
- Bump `Microsoft.Web.WebView2` to 1.0.4129.50.
- Bump `Microsoft.NET.Test.Sdk` to 18.9.0.
- Bump GitHub Actions: `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact` (removes the Node 20 deprecation warning).

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

[Unreleased]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/VectorsATCGroup/euroscope-sector-file-manager/releases/tag/v1.0.0
