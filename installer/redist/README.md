# installer/redist

The installer bundles the official Microsoft WebView2 **Evergreen Bootstrapper** so it can install the runtime on machines that do not already have it.

The binary itself is **not committed** to this repository (it is a Microsoft file, fetched at build time). To build the installer locally, download it here first:

- File name expected by `setup.iss`: `MicrosoftEdgeWebview2Setup.exe`
- Official download: https://developer.microsoft.com/microsoft-edge/webview2/ (Evergreen Bootstrapper)
- Direct link used by CI: https://go.microsoft.com/fwlink/p/?LinkId=2124703

The release pipeline downloads it automatically, so this step is only needed for local installer builds.
