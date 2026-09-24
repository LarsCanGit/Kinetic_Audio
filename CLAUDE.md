# Kinetic Audio

Windows desktop app: grouped per-app volume mixing (Sonar's model) + favorite-device switching (SoundSwitch's model), built on Windows Core Audio APIs. No drivers, no virtual devices in Phase 1.

Full product/architecture spec: [Kinetic Audio - Product Design Doc.md](Kinetic%20Audio%20-%20Product%20Design%20Doc.md), including the `## GSTACK REVIEW REPORT` section at the bottom — that section is binding on implementation, not just advisory notes.

## Stack

- .NET 9, WPF (`src/GroupedAudioMixer` — folder not yet renamed to match the project; a build-server lock blocked it, rename when convenient)
- xUnit (`tests/GroupedAudioMixer.Tests` — same pending rename)
- Solution: `KineticAudio.sln`

## Architecture decisions (binding — see GSTACK REVIEW REPORT for full rationale)

- **Core Audio is behind an interface.** All `IAudioSessionManager2`/`ISimpleAudioVolume`/`IAudioEndpointVolume` calls live in a thin adapter layer implementing an `IAudioSessionProvider`-style seam. Group assignment, volume math, conflict/fallback handling, and mute/unmute-restore are pure logic that unit-tests against a fake — never against real Core Audio.
- **One shared volume-state module** owns `group_volume` / `main_volume` / mute state and effective-volume computation (`group_volume × main_volume`). Both the Phase 1 per-session writer and any future Phase 3 mix engine consume it — this logic does not get duplicated per phase.
- **Session detection is event-driven** (`IAudioSessionNotification`), not polled. Polling is reserved only as a fallback for device-list/endpoint enumeration, never for the session-creation hot path.
- **Executable→group map writes are serialized** through a single-writer path. Auto-link (on session creation) and manual drag-to-group both funnel through it so concurrent session bursts (an app launching several helper processes at once) can't race.
- **External volume drift gets reconciled**, not ignored: poll each session's actual volume periodically (target interval: 250ms–1s) and treat a mismatch against `group_volume × main_volume` as a manual override — update the stored value and the UI rather than silently overwriting it on the next fader move.
- **Group deletion reassigns live sessions immediately**, not just future ones via the map.

## Testing

- **Unit tests** (xUnit, `tests/GroupedAudioMixer.Tests`): all logic-layer code — volume math, group assignment, map conflict/fallback, mute/unmute-restore, group-deletion reassignment. Run against the fake `IAudioSessionProvider`, never real Core Audio.
- **E2E/manual regression checklist**, run before every release: device-switch hotkey, mute → unmute restore. These are the two flows where a silent regression is a real-world audio problem (wrong device mid-call, volume not restored), not just a UI bug.
- **Mandatory regression rule:** every logic-layer bug fix gets a unit test reproducing it before the fix lands. Every bug fix touching hotkey-switch or mute/unmute gets added to the E2E checklist.

## Open / not yet decided

- Config storage format and location (local file vs. registry) — blocks several implementation tasks, see GSTACK REVIEW REPORT implementation task list.
- Distribution: installer, updater, code-signing (relevant once Phase 2 bundles the third-party VST3 fork binary).
- Perf budgets need concrete numbers once profiled: reconciliation poll interval, Phase 3 added-latency target (<20ms working target).

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/GroupedAudioMixer
```
