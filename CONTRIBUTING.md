# Contributing

Thanks for your interest in improving the **EuroScope Sector File Manager**. This is a free, community project by [Vectors ATC Group](https://vectorsatcgroup.com/), and contributions of all sizes are welcome, from typo fixes to new features.

By contributing, you agree that your contributions are licensed under the project's [Apache License 2.0](LICENSE).

## Ground rules (please read)

This project has a few hard rules that protect users and respect third parties. Pull requests that break any of these will not be merged.

1. **Never commit AeroNav files.** The Sector File packages are copyrighted and must not be distributed outside AeroNav. Test fixtures must be **synthetic** and contain no AeroNav content. The `.gitignore` already blocks `*.7z` except the synthetic fixtures under `fixtures/archives/`.
2. **Never weaken the privacy design.** Do not add telemetry, analytics, crash reporting to a remote server, or any network call that sends user data anywhere. Do not read, type, intercept, store, or log passwords, cookies, tokens, authorization headers, or signed URLs. Authentication must stay on the official AeroNav, VATSIM, and Navigraph pages inside the isolated WebView2 profile.
3. **Never modify a real user install destructively in tests.** Use temporary copies. Tests must not touch a real EuroScope installation.
4. **Keep changes transactional.** Any operation that writes to the user's Sector Files must stage, back up, and commit atomically, with rollback on failure.

## Getting started

**Prerequisites:** the [.NET SDK](https://dotnet.microsoft.com/download) 8.0 or newer, on **Windows** (the WPF app targets `net8.0-windows`).

```powershell
git clone https://github.com/VectorsATCGroup/euroscope-sector-file-manager.git
cd euroscope-sector-file-manager

dotnet build Vectors.EuroScopeUpdater.sln -c Release
dotnet test  Vectors.EuroScopeUpdater.sln -c Release
```

To run the app from source:

```powershell
dotnet run --project src/Vectors.EuroScopeUpdater.App -c Debug
```

To build the installer you also need [Inno Setup 6](https://jrsoftware.org/isdl.php):

```powershell
dotnet publish src/Vectors.EuroScopeUpdater.App/Vectors.EuroScopeUpdater.App.csproj -c Release -r win-x64 --self-contained -o build/app
iscc installer/setup.iss
```

## Project layout

| Path | Target | Responsibility |
|------|--------|----------------|
| `src/Vectors.EuroScopeUpdater.Core` | `net8.0` | Domain model, install engine, archive safety, local scanning. No UI, no I/O to remote services. |
| `src/Vectors.EuroScopeUpdater.Infrastructure` | `net8.0` | Package sources, AeroNav parsing. |
| `src/Vectors.EuroScopeUpdater.App` | `net8.0-windows` | WPF app, MVVM view models, WebView2, theming, localization. |
| `tests/Vectors.EuroScopeUpdater.Tests` | `net8.0` | xUnit tests. |

The source namespaces remain `Vectors.EuroScopeUpdater.*` for historical reasons, even though the product is now named "EuroScope Sector File Manager".

## Coding conventions

- Match the style of the surrounding code. The repo has an `.editorconfig`, please keep it green.
- MVVM in the app layer: use `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`). Keep logic testable in Core, not in code-behind.
- Keep Core free of Windows-only and UI dependencies so it stays unit-testable.
- User-facing strings must be added to both languages in `Services/Localization.cs` (PT and EN), referenced from XAML via the `{loc:Loc Key}` extension.
- Prefer commas over dashes as separators in user-facing text and documentation.
- Do not log secrets. When in doubt, log less.

## Making a change

1. Open an issue first for anything non-trivial, so we can agree on the approach. Issues labeled `good first issue` are a friendly place to start.
2. Create a branch from `main`.
3. Make your change with tests. All tests must pass (`dotnet test`), and the build must be clean (0 warnings, 0 errors).
4. Open a pull request against `main` and fill in the template. Link the issue it closes.
5. A maintainer will review. At least one approving review is required before merge. Please be patient, this is a volunteer project.

## Commit and PR style

- Write clear, imperative commit messages ("Add rollback for partial installs").
- Keep pull requests focused. Small PRs are reviewed faster.
- Describe user-visible changes and how you tested them.

Thank you for helping fellow controllers keep their sectors up to date.
