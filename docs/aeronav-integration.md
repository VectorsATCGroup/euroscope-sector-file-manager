# AeroNav Integration

How the updater discovers and downloads packages from AeroNav, and — importantly — which parts are
**confirmed** versus **inferred**. AeroNav download endpoints require authentication that cannot be
exercised in an offline/dev environment, so the live contract is deliberately isolated behind one thin
layer and paired with an offline fixture implementation so the whole application is testable without
touching AeroNav.

## Guiding principles (non-negotiable)

- Authentication happens **only** on the official AeroNav / VATSIM / Navigraph pages, inside a dedicated,
  **isolated WebView2 profile**. The app never renders its own username/password fields, never receives
  the user's password, never reads external browser cookies, and never copies cookies out to another
  browser.
- The app **does not redistribute** sector files, does not proxy downloads through any Vectors server,
  and hosts nothing. Files always come straight from the official origin.
- Nothing sensitive is ever logged: no cookies, `Authorization` headers, tokens, or signed URLs.
- The Sector File Provider mechanism is intentionally **not** used in this version (see README), but the
  source is abstracted behind `ISectorPackageSource` so a `AeroNavProviderPackageSource` can replace the
  web source later with no change to the UI, installer, or file engine.

## The abstraction seam

```
ISectorPackageSource
├── GetCatalogAsync(division)          → RemoteCatalog { Airac, packages[] }
└── DownloadAsync(package, dest, progress, ct)
```

Two implementations ship:

| Implementation | Use | Status |
| --- | --- | --- |
| `FixturePackageSource` | Offline dev/demo/tests. Serves a catalog + synthetic `.7z` packages from a local folder. | Fully working. |
| `AeroNavWebPackageSource` | Real AeroNav via the authenticated WebView2 session. | Boundary implemented; live selectors/endpoints must be confirmed against the authenticated site (see below). |

`AeroNavWebPackageSource` is composed of:
`AeroNavSession` (owns the isolated WebView2 environment + session lifetime) · `AeroNavCatalogSource`
(drives navigation to `https://files.aero-nav.com/SBXX` and reads the rendered listing) ·
`AeroNavParser` (pure HTML→model, unit-testable against saved snapshots) · `AeroNavDownloadService`
(captures the browser-initiated download and redirects it to our staging path).

## Authentication flow — **Inferred contract**

Observed indirectly (a real installation and packages exist, and the public page is
`https://files.aero-nav.com/SBXX`); the exact auth handshake was **not** exercised here.

1. Navigate the WebView2 to `https://files.aero-nav.com/SBXX`.
2. If unauthenticated, AeroNav redirects to its own login (VATSIM Connect / Navigraph). The user
   authenticates **on those official pages**; the app only displays them and watches the URL/host.
3. On success the browser returns to the SBXX listing. The app detects arrival on the listing host and
   transitions to the native catalog UI.
4. The session (cookies) lives only inside the isolated WebView2 `UserDataFolder` for the lifetime of
   the session and is cleared on logout / exit (`CoreWebView2.Profile.ClearBrowsingDataAsync`).

**Session expiry:** any catalog/download call that lands back on a login host (or returns 401/403) is
surfaced as `AeroNavAuthRequired`, which the UI turns into a friendly "session expired → Authenticate"
prompt rather than a technical error, then resumes the pending action.

**How the live implementation judges the session (confirmed in production):**

- Being on an `aero-nav.com` URL proves nothing: the pre-login page lives at the same host and path
  (`files.aero-nav.com/SBXX`). The only reliable "authenticated" signal is the package listing actually
  rendering in the DOM (links matching `<FIR>/(Install|Update)-Package_….7z`), which AeroNav injects via
  JavaScript after navigation completes. Every check therefore polls the DOM instead of reading it once.
- "Logged out" is recognised three ways: a redirect to a sign-in host (VATSIM Connect / Navigraph /
  `auth`/`sso`/`login` hosts), the AeroNav page showing a sign-in link/form and no packages after a short
  grace period, or no listing at all within the timeout. The silent startup check is bounded (~15 s) and
  the dashboard shows a "checking saved session" banner with **Authenticate** available meanwhile.
- `IsAuthenticated` is re-validated, not trusted forever: the listing is reloaded on every refresh
  (reused only for a minute right after sign-in), and a download that never starts after clicking its
  link is treated as an expired session.
- The session persists across launches only because the WebView2 user-data folder is a stable
  per-user folder and the app shuts down cleanly (`ShutdownMode=OnMainWindowClose`, browser disposed on
  exit, single instance). A process left running invisibly would keep that profile open and make
  persistence unreliable, which is exactly the bug fixed in the release after 1.0.3.

## Catalog discovery — **Inferred, must be confirmed against the live page**

The parser is written against a stable, *semantic* reading of the listing rather than brittle generated
class names:

- Each FIR/package is a link whose `href`/text matches the package grammar
  `` <FIR>-(Install|Update)-Package_<TS>-<AIRAC>-<REV>.7z `` (see `docs/package-analysis.md`).
- From that single string the parser derives FIR, package **type** (Install/Update), **AIRAC** cycle,
  build timestamp and revision — no dependence on surrounding layout.
- The catalog AIRAC is the max cycle across discovered packages.

Because the parser keys off the **file-name contract** (which is embedded in the artifacts themselves
and therefore stable) rather than CSS structure, it degrades gracefully: if AeroNav restyles the page,
parsing still works as long as the download links still carry these file names. If the contract ever
changes, `AeroNavParser` is the single file to update, and its behavior is pinned by snapshot tests.

> The concrete DOM selectors, login host names, and whether the listing is server-rendered vs. requires
> a click to reveal links are **Unknown** until run against the authenticated site. These are localized
> to `AeroNavCatalogSource`/`AeroNavParser` and flagged with `// TODO(confirm-live)` in code.

## Download mechanism — **Inferred**

The safe assumption (matching how the manual flow works today) is that the download is a normal
authenticated GET that the **browser session** is authorized for. Two supported strategies, chosen at
runtime:

1. **Browser-initiated capture (preferred):** trigger the official download inside the WebView2 and
   handle `CoreWebView2.DownloadStarting` — cancel the browser's own save, take the `ResultFilePath`
   into our staging directory, and drive progress from `DownloadOperation.BytesReceived/TotalBytes`.
   This keeps AeroNav's exact request semantics (cookies, redirects) intact.
2. **Session-scoped GET:** if a direct package URL is exposed, issue the GET through an `HttpClient`
   seeded *only* with the WebView2 session cookies for the AeroNav host, writing to staging with
   progress. Cookies are never persisted or logged.

Either way the destination is our controlled **staging** path; the file engine then validates and
installs it (see the transactional pipeline in the README).

## Failure behavior

| Condition | Surfaced as |
| --- | --- |
| Not logged in / redirected to login | `AeroNavAuthRequired` → "Authenticate" prompt |
| Network / host unreachable | `AeroNavUnavailable` → retry option |
| Package link missing for a FIR | `PackageUnavailable` → FIR shown without an action |
| Download interrupted | resumable/retry; partial file discarded from staging |

## Future migration path

Replacing the web source with the EuroScope/AeroNav **Sector File Provider** (or a first-party AeroNav
API) means implementing one new `ISectorPackageSource` (`AeroNavProviderPackageSource`) and registering
it in DI. The catalog model, download manager, file engine, backup/rollback, manifest, UI and installer
are all unaffected — they depend only on the abstraction.
