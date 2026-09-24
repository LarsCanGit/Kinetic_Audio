using KineticAudio.Core.Devices;

namespace KineticAudio.Tests.Core.Devices;

/// <summary>
/// In-memory <see cref="IAudioDeviceProvider"/> fake for testing <see cref="DeviceSwitcher"/>
/// against, per binding decision D5.
/// </summary>
public sealed class FakeAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly List<AudioDeviceInfo> _devices = new();
    private readonly Dictionary<AudioDeviceKind, string?> _defaultIdByKind = new();

    public void AddDevice(AudioDeviceInfo device) => _devices.Add(device);

    public void RemoveDevice(string deviceId) => _devices.RemoveAll(d => d.DeviceId == deviceId);

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceKind kind) =>
        _devices.Where(d => d.Kind == kind).ToList();

    public string? GetDefaultDeviceId(AudioDeviceKind kind) =>
        _defaultIdByKind.GetValueOrDefault(kind);

    public void SetDefaultDevice(AudioDeviceKind kind, string deviceId) =>
        _defaultIdByKind[kind] = deviceId;
}
