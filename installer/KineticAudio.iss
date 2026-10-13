; Inno Setup script for Kinetic Audio.
; Build with: dotnet publish first (see installer/README.md), then compile this script
; with ISCC.exe (from the Inno Setup install) or the Inno Setup IDE.

#define MyAppName "Kinetic Audio"
#define MyAppPublisher "Lars Hansen"
#define MyAppExeName "KineticAudio.exe"
#define PublishDir "..\src\GroupedAudioMixer\bin\Release\net9.0-windows\win-x64\publish"
; The version comes from the published exe, which gets it from Directory.Build.props, so the
; installer can't drift from the app. The file version is 4-part ("0.1.0.0"); drop the last part.
#define FullVersion GetVersionNumbersString(PublishDir + "\" + MyAppExeName)
#define MyAppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
AppId={{6E5A2C2B-6E3E-4A6F-9C6C-5B9C2E9C0E5A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=output
OutputBaseFilename=KineticAudioSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\GroupedAudioMixer\Assets\KineticAudio.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
; Per-user install (no UAC prompt): {autopf} resolves to %LOCALAPPDATA%\Programs, and the HKCU
; Run-key cleanup below targets the same account that installed.
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; "Run at Windows startup" is managed by the app itself (Settings > General), via
; Core/Devices/StartupRegistration.cs writing its own HKCU Run-key value. The installer
; deliberately does not create or modify that key during install, to avoid a second,
; conflicting entry, but does clean it up on uninstall below so a stale entry pointing
; at the now-deleted exe doesn't linger.

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "KineticAudio"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
