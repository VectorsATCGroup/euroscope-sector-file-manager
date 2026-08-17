# Security Policy

Security and privacy are core goals of this project. If you find a vulnerability, a way to leak credentials, or any behavior that could expose user data, we want to hear from you.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through one of these channels:

- Use GitHub's **[Report a vulnerability](https://github.com/VectorsATCGroup/euroscope-sector-file-manager/security/advisories/new)** (Security tab, Private vulnerability reporting), or
- Email **security@vectorsatcgroup.com**.

Please include:

- A description of the issue and its impact.
- Steps to reproduce, or a proof of concept.
- Affected version (see Help, About in the app, or the release tag).

We aim to acknowledge reports within a few days. Since this is a volunteer project, please allow reasonable time for a fix before any public disclosure. We will credit reporters who wish to be named.

## Supported versions

As a small project, security fixes are made against the latest release. Please update to the newest version before reporting.

## Our security and privacy design

These are the guarantees the code is built to uphold. A report that shows any of them being violated is a valid security issue:

- The app never sees, types, intercepts, stores, or logs passwords. Authentication happens only on the official AeroNav, VATSIM, and Navigraph pages, inside an isolated WebView2 browser profile that is separate from the user's own browser.
- No telemetry, analytics, or backend. Nothing is sent to any Vectors ATC Group server.
- Cookies, tokens, authorization headers, and signed URLs are never logged.
- Archive extraction is protected against path traversal (zip slip).
- Writes to the user's Sector Files are transactional, with an automatic backup and rollback on failure.

Thank you for helping keep controllers safe.
