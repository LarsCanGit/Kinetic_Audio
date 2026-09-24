; Inno Setup script for Kinetic Audio.
; Build with: dotnet publish first (see installer/README.md), then compile this script
; with ISCC.exe (from the Inno Setup install) or the Inno Setup IDE.

#define MyAppName "Kinetic Audio"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Lars Hansen"
#define MyAppExeName "KineticAudio.exe"
#define PublishDir "..\src\GroupedAudioMixer\bin\Release\net9.0-windows\win-x64\publish"

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
; deliberately does not touch that key, to avoid a second, conflicting entry.

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
