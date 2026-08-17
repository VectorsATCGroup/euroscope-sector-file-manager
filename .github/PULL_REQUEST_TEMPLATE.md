<!-- Thanks for contributing. Please fill this in so reviewers can help quickly. -->

## Summary

<!-- What does this PR change and why. Link the issue it closes, e.g. Closes #123. -->

## How I tested

<!-- Commands you ran, scenarios you checked. -->

- [ ] `dotnet build -c Release` is clean (0 warnings, 0 errors)
- [ ] `dotnet test -c Release` passes

## Checklist

- [ ] I read [CONTRIBUTING.md](../CONTRIBUTING.md).
- [ ] No AeroNav files are committed. Any new fixtures are synthetic.
- [ ] No telemetry, analytics, or backend was added, and no passwords, cookies, tokens, or signed URLs are logged.
- [ ] User-facing strings were added in both languages (PT and EN) in `Localization.cs`.
- [ ] Operations that write to Sector Files remain transactional (stage, backup, commit, rollback).
- [ ] User-facing text uses commas rather than dashes as separators.
