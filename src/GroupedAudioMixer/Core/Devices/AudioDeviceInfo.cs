namespace KineticAudio.Core.Devices;

/// <summary>
/// A playback or recording endpoint device, as reported by an <see cref="IAudioDeviceProvider"/>.
/// </summary>
/// <param name="DeviceId">
/// Windows endpoint ID. Not guaranteed stable across a driver update or USB replug (design doc,
/// Open Questions: "Device ID stability") — callers that persist a switch-list need to degrade
/// gracefully rather than assume this never changes.
/// </param>
/// <param name="DisplayName">Human-readable name for UI display.</param>
/// <param name="Kind">Playback or Recording.</param>
public sealed record AudioDeviceInfo(string DeviceId, string DisplayName, AudioDeviceKind Kind);
