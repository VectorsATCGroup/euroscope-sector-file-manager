# Fixtures

**No real AeroNav files live here.** AeroNav packages carry a copyright notice forbidding
distribution outside AeroNav services (see `docs/package-analysis.md`), so they are `.gitignore`d and
must never be committed.

- `archives/` — **synthetic** `.7z` packages generated for tests. They reproduce only the *structure*
  observed in real packages (versioned `.sct/.ese`, nested FIR data folder, `Settings/RADAR`
  personalization, a placeholder plugin `.dll`). Their content is dummy text, not AeroNav data.
- Most engine tests do not need a real archive at all — they build synthetic content directories at
  runtime via `SyntheticPackages` and drive the engine through folder-based test doubles.

To run the app offline against synthetic packages, point `FixturePackageSource` at a folder of
`.7z` files whose names follow the AeroNav grammar
(`<FIR>-<Install|Update>-Package_<TS>-<AIRAC>-<REV>.7z`).
