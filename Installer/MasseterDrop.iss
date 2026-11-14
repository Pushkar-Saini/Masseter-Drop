; Masseter Drop — Inno Setup Script
; Generates a professional Windows installer executable (MasseterDrop-Setup.exe)

#define MyAppName "Masseter Drop"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Masseter"
#define MyAppExeName "MasseterDrop.exe"

[Setup]
AppId={{D37E6145-7B38-4A59-8664-90DEE5C8D4A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Masseter Drop
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\publish\Setup
OutputBaseFilename=MasseterDrop-Setup
SetupIconFile=..\Assets\Icons\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "&Start Masseter Drop automatically when Windows starts"; GroupDescription: "Windows Integration:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Assets\Icons\app.ico"; DestDir: "{app}\Assets\Icons"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Icons\app.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\Icons\app.ico"

[Registry]
; Register automatic startup with --minimized flag so it starts directly in the system tray
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MasseterDrop"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Tasks: startup; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; ValueName: "MasseterDrop"; ValueData: "02 00 00 00 00 00 00 00 00 00 00 00"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Description: "Launch Masseter Drop in system tray"; Flags: nowait postinstall
