; F1 Chronos — Inno Setup installer
; Requires Inno Setup 6: https://jrsoftware.org/isinfo.php
; Build payload first:  .\build.ps1
; Then compile:         iscc installer\MT_F1Chronos.iss
;
; VERSION: keep in sync with Directory.Build.props

#define MyAppName "F1 Chronos"
#define MyAppVersion "0.7.0"
#define MyAppPublisher "MT_F1Chronos"
#define MyAppExeName "MT_F1Chronos.exe"
#define MyAppId "{{8F3C2A91-6B4E-4D7A-9C1F-2E8A5B0D4F73}}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\F1Chronos
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=F1Chronos-Setup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
CloseApplicationsFilter=*.exe
RestartApplications=no
; Same AppId + higher AppVersion = in-place upgrade
; Settings/sessions stay in %LOCALAPPDATA%\MT_F1Chronos\ (separate from install dir)
UsePreviousAppDir=yes
DisableDirPage=auto

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis:"; Flags: checkedonce
Name: "startupicon"; Description: "Lancer au démarrage de Windows"; GroupDescription: "Raccourcis:"; Flags: checkedonce

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Lancer {#MyAppName}"; Flags: nowait postinstall skipifsilent
