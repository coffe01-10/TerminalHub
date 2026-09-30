; Terminal Hub / 终端控制中心 — Inno Setup installer script
; Build:   scripts\publish-windows.ps1   (or: iscc packaging\TerminalHub.iss)
; Expects: artifacts\publish\win-x64\TerminalHub.exe + payload

#define AppName      "Terminal Hub"
#define AppNameZh    "终端控制中心"
#define AppVersion   "0.2.0"
#define AppPublisher "coffe01-10"
#define AppExe       "TerminalHub.exe"

[Setup]
AppId={{8F2E5B1A-7C3D-4E5F-9A1B-TERMHUB0001}}
AppName={#AppName} {#AppNameZh}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\TerminalHub
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=TerminalHub-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; single-instance friendly: no service, per-machine or per-user install
PrivilegesRequired=lowest
SetupIconFile=
UninstallDisplayIcon={app}\TerminalHub.exe
WizardStyle=modern

[Languages]
Name: "english";    MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut / 创建桌面快捷方式"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\artifacts\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";      Filename: "{app}\{#AppExe}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; kill running instance before uninstall
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "killapp"

[Code]
// Single-instance guard: refuse install while running (clean-exit friendly)
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if Exec('taskkill.exe', '/F /IM TerminalHub.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log('Terminated running TerminalHub instance (code ' + IntToStr(ResultCode) + ')');
end;
