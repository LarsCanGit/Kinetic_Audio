# Kinetic Audio

A Windows desktop app for grouped per-app volume mixing and fast favorite-device switching, built directly on Windows Core Audio APIs. No drivers, no virtual audio devices.

Kinetic Audio combines the two features that no single existing tool covers well:

- **Grouped volume control** (Sonar's model) — assign app audio sessions to one of four fixed groups plus Mic, and control each group's volume with a single fader instead of juggling per-app sliders.
- **Favorite-device switching** (SoundSwitch's model) — a curated list of playback/recording devices with one-click or hotkey-driven switching.

## Status

Work in progress, Phase 1 (no EQ). Not yet released.

## Installing

Run `KineticAudioSetup-<version>.exe`. It installs per user (no administrator prompt) to `%LOCALAPPDATA%\Programs\Kinetic Audio`. See [installer/README.md](installer/README.md) to build it.

> **Upgrading from v0.1.0:** uninstall v0.1.0 before installing v0.2.0 or later. v0.1.0 was installed machine-wide (Program Files, administrator), and v0.2.0 installs per user, so the new installer will not replace it and you would end up with two copies side by side. Your settings are kept.

## Features (Phase 1)

- Main fader plus four renamable groups and a dedicated Mic strip
- Drag-to-group app assignment, persisted by executable name/path
- Mute per group with exact-level restore on unmute
- Playback/recording device reordering and a switch-list of favorites
- Global hotkey to cycle the switch-list, with on-switch toast notification
- Drift reconciliation: an app's own volume changes are detected and treated as a manual override rather than being silently overwritten

## Stack

- .NET 9, WPF (`src/GroupedAudioMixer`)
- xUnit (`tests/GroupedAudioMixer.Tests`)
- Solution: `KineticAudio.sln`

## Building

```bash
dotnet build
dotnet test
dotnet run --project src/GroupedAudioMixer
```

Requires the .NET 9 SDK and Windows (Core Audio APIs are Windows-only).

## Architecture

Core Audio access lives behind an `IAudioSessionProvider`-style adapter so group assignment, volume math, and mute/unmute logic can be unit-tested against a fake instead of real Core Audio. Session detection is event-driven, not polled, and executable-to-group map writes are serialized through a single-writer path to avoid races when several sessions for one app start at once.

## License

GPL-3.0. See [LICENSE](LICENSE).
