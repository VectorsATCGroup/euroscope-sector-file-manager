; Inno Setup script - Vectors ATC Group EuroScope Sector File Manager
; Per-user install (no admin), into %LOCALAPPDATA%\Programs. Ensures the WebView2 runtime is present.
;
; Build:
;   1) pwsh build/publish.ps1 -SelfContained     (produces build/publish)
;   2) (optional) place MicrosoftEdgeWebview2Setup.exe in installer/redist for offline WebView2 install
;   3) iscc installer/setup.iss                   (produces installer/Output/VectorsEuroScopeSectorFileManager-Setup.exe)

#define AppName "Vectors ATC Group EuroScope Sector File Manager"
#define AppShortName "EuroScope Sector File Manager"
#define AppPublisher "Vectors ATC Group"
; AppVersion may be overridden by the release pipeline: iscc /DAppVersion=1.2.3 setup.iss
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "VectorsEuroScopeSectorFileManager.exe"

[Setup]
AppId={{A1F5C7D3-2E48-4B6A-9C1D-7F3E5A2B8C40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppSupportURL=https://vectorsatcgroup.com/
DefaultDirName={localappdata}\Programs\Vectors ATC Group\EuroScope Sector File Manager
DefaultGroupName=Vectors ATC Group
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=Output
OutputBaseFilename=VectorsEuroScopeSectorFileManager-Setup
SetupIconFile=..\src\Vectors.EuroScopeUpdater.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\TERMS.txt
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Published app output (self-contained; produced by build/publish.ps1 -SelfContained).
Source: "..\build\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; Usage/privacy terms and code license, installed alongside the app.
Source: "..\TERMS.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Bundled offline WebView2 bootstrapper (installed only if the runtime is missing).
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{group}\{#AppShortName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppShortName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppShortName}"; Flags: nowait postinstall skipifsilent

[Code]
// Detect the Evergreen WebView2 runtime (per-machine or per-user).
function WebView2Installed: Boolean;
var
  v: string;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v) or
    RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', v);
  if Result then Result := (v <> '') and (v <> '0.0.0.0');
end;

procedure InstallWebView2Runtime;
var
  code: Integer;
  bootstrapper: string;
begin
  if WebView2Installed then exit;

  // Bundled bootstrapper — extract and run the official Microsoft installer silently.
  ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
  bootstrapper := ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe');
  Exec(bootstrapper, '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, code);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallWebView2Runtime;
end;
