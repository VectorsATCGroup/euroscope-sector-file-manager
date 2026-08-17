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

[UninstallDelete]
; Fully remove the program folder (the uninstaller runs from inside it) and its parent if empty.
Type: filesandordirs; Name: "{app}"
Type: dirifempty; Name: "{localappdata}\Programs\Vectors ATC Group"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppShortName}"; Flags: nowait postinstall skipifsilent

[Code]
// The per-user (and, if ever elevated, per-machine) uninstall registry key Inno writes for this AppId.
const
  UninstallRegKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1F5C7D3-2E48-4B6A-9C1D-7F3E5A2B8C40}_is1';

var
  ExistingInstall: Boolean;
  ExistingVersion: String;
  ExistingUninstaller: String;
  ActionPage: TInputOptionWizardPage;
  UninstallAndExit: Boolean;

// ── WebView2 runtime ─────────────────────────────────────────────────────────
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

// ── Existing-installation detection ──────────────────────────────────────────
function ReadExisting(RootKey: Integer): Boolean;
begin
  Result := RegQueryStringValue(RootKey, UninstallRegKey, 'DisplayVersion', ExistingVersion);
  if Result then
    if not RegQueryStringValue(RootKey, UninstallRegKey, 'QuietUninstallString', ExistingUninstaller) then
      RegQueryStringValue(RootKey, UninstallRegKey, 'UninstallString', ExistingUninstaller);
end;

function DetectExistingInstall: Boolean;
begin
  ExistingVersion := '';
  ExistingUninstaller := '';
  Result := ReadExisting(HKCU) or ReadExisting(HKLM);
end;

// Split an uninstall command ("C:\path\unins000.exe" /SILENT) into exe and params.
procedure SplitCommand(const S: String; var Exe, Params: String);
var
  p: Integer;
begin
  Exe := '';
  Params := '';
  if (Length(S) > 0) and (S[1] = '"') then
  begin
    p := Pos('"', Copy(S, 2, Length(S)));
    if p > 0 then
    begin
      Exe := Copy(S, 2, p - 1);
      Params := Trim(Copy(S, p + 2, Length(S)));
    end;
  end
  else
  begin
    p := Pos(' ', S);
    if p > 0 then
    begin
      Exe := Copy(S, 1, p - 1);
      Params := Trim(Copy(S, p + 1, Length(S)));
    end
    else
      Exe := S;
  end;
end;

procedure InitializeWizard;
begin
  ExistingInstall := DetectExistingInstall;
  if ExistingInstall then
  begin
    ActionPage := CreateInputOptionPage(wpWelcome,
      'Existing installation found',
      'Version ' + ExistingVersion + ' of {#AppShortName} is already installed on this computer.',
      'Choose what you would like to do, then click Next.',
      True, False);
    ActionPage.Add('Update to version {#AppVersion} (recommended). Keeps your settings and sign-in.');
    ActionPage.Add('Repair. Reinstall all program files to fix a broken installation.');
    ActionPage.Add('Uninstall. Remove {#AppShortName} and all of its data from this computer.');
    ActionPage.SelectedValueIndex := 0;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  exe, params: String;
  code: Integer;
begin
  Result := True;
  if ExistingInstall and (ActionPage <> nil) and (CurPageID = ActionPage.ID) then
  begin
    // 0 = Update, 1 = Repair: both proceed to install over the existing files.
    // 2 = Uninstall: run the existing uninstaller (which also clears app data) and exit setup.
    if ActionPage.SelectedValueIndex = 2 then
    begin
      if ExistingUninstaller <> '' then
      begin
        SplitCommand(ExistingUninstaller, exe, params);
        if params = '' then params := '/SILENT';
        Exec(exe, params, '', SW_SHOW, ewWaitUntilTerminated, code);
      end;
      UninstallAndExit := True;
      Result := False;
      WizardForm.Close;
    end;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  // Do not prompt "Exit Setup?" when we are closing because the user chose Uninstall.
  if UninstallAndExit then
    Confirm := False;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallWebView2Runtime;
end;

// ── Uninstall removes everything, including the local app data folder ─────────
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  dataDir, legacyDir, parentDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // config.json, state manifests, backups, logs, operations and the WebView2 session profile.
    dataDir := ExpandConstant('{localappdata}\VectorsATCGroup\EuroScopeSectorFileManager');
    if DirExists(dataDir) then
      DelTree(dataDir, True, True, True);
    // Legacy data folder from the pre-rename "EuroScope Updater" identity, if present.
    legacyDir := ExpandConstant('{localappdata}\VectorsATCGroup\EuroScopeUpdater');
    if DirExists(legacyDir) then
      DelTree(legacyDir, True, True, True);
    // Remove the VectorsATCGroup parent folder only if nothing else remains in it.
    parentDir := ExpandConstant('{localappdata}\VectorsATCGroup');
    if DirExists(parentDir) then
      RemoveDir(parentDir);
  end;
end;
