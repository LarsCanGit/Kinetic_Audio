# Building the installer

Produces `KineticAudioSetup-<version>.exe`, a self-contained installer (bundles the
.NET runtime, so users don't need the .NET runtime installed separately).

## Prerequisites

- [Inno Setup 6](https://jrsoftware.org/isdl.php) (adds `ISCC.exe` to your PATH by default
  under `C:\Program Files (x86)\Inno Setup 6\`)

## Steps

1. Publish a self-contained, single-file build from the repo root:

   ```powershell
   dotnet publish src/GroupedAudioMixer -c Release -r win-x64 --self-contained true `
     -p:PublishSingleFile=false
   ```

   This writes to `src/GroupedAudioMixer/bin/Release/net9.0-windows/win-x64/publish/`,
   which is what `installer/KineticAudio.iss` packages.

2. Compile the installer script:

   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\KineticAudio.iss
   ```

   Output lands in `installer/output/KineticAudioSetup-<version>.exe`.

3. Run the resulting `.exe` to test the install/uninstall flow.

## Notes

- Bump `<Version>` in `Directory.Build.props` (repo root) for each release. It is the only
  place the version lives: the installer reads it from the published exe (so re-run step 1
  before step 2), and the Settings window shows it.
- `installer/output/` is gitignored; it's build output, not source.
- Code-signing isn't set up yet (unsigned installers trigger a SmartScreen warning on
  first run). Revisit once Phase 2 bundles the third-party VST3 fork binary, per CLAUDE.md.
