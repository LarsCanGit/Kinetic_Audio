namespace GroupedAudioMixer.Core.Devices;

/// <summary>
/// Seam over Windows default-device enumeration and switching (<c>IMMDeviceEnumerator</c> for
/// reads, the undocumented <c>IPolicyConfig</c> for <c>SetDefaultEndpoint</c> writes, since Windows
/// has no public API for changing the default endpoint). Per binding decision D5, the real
/// implementation is a thin COM-interop adapter with no logic of its own; switch-list cycling logic
/// lives above this seam in <see cref="DeviceSwitcher"/> and unit-tests against a fake.
/// </summary>
public interface IAudioDeviceProvider
{
    /// <summary>All active endpoint devices of the given kind, in enumeration order.</summary>
    IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceKind kind);

    /// <summary>The current default device id for the given kind, or null if none is set.</summary>
    string? GetDefaultDeviceId(AudioDeviceKind kind);

    /// <summary>Makes <paramref name="deviceId"/> the default device for the given kind.</summary>
    void SetDefaultDevice(AudioDeviceKind kind, string deviceId);
}
