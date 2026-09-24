namespace KineticAudio.Core.Devices;

/// <summary>
/// Cycle-switch hotkey logic (design doc: "SoundSwitch's actual killer feature is a global hotkey
/// to cycle through the switch-list"). Owns the curated switch-list for one <see cref="AudioDeviceKind"/>
/// and decides which device to switch to next; all Core Audio interaction goes through
/// <see cref="IAudioDeviceProvider"/> so this class unit-tests against a fake per binding decision D5.
///
/// The switch-list is supplied in memory today. Persisting it (device order, switch-list membership)
/// is design doc decision D2, still open per project CLAUDE.md; this class does not depend on how or
/// whether that persistence lands.
/// </summary>
public sealed class DeviceSwitcher
{
    private readonly IAudioDeviceProvider _deviceProvider;
    private readonly AudioDeviceKind _kind;
    private IReadOnlyList<string> _switchListDeviceIds;

    public DeviceSwitcher(IAudioDeviceProvider deviceProvider, AudioDeviceKind kind, IReadOnlyList<string>? switchListDeviceIds = null)
    {
        _deviceProvider = deviceProvider;
        _kind = kind;
        _switchListDeviceIds = switchListDeviceIds ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> SwitchListDeviceIds => _switchListDeviceIds;

    /// <summary>Replaces the curated switch-list (e.g. after the user edits it in Settings).</summary>
    public void SetSwitchList(IReadOnlyList<string> deviceIds) => _switchListDeviceIds = deviceIds;

    /// <summary>Switches directly to <paramref name="deviceId"/> (one-click switching from a list/menu).</summary>
    public void SwitchTo(string deviceId) => _deviceProvider.SetDefaultDevice(_kind, deviceId);

    /// <summary>
    /// Advances to the next device in the switch-list after the current default, wrapping around.
    /// Switch-list entries for devices that are no longer present (unplugged, driver update) are
    /// skipped rather than failing the whole cycle, per the design doc's "degrade gracefully" guidance
    /// on device ID stability. Returns the id switched to.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No switch-list entry currently resolves to a present device.
    /// </exception>
    public string CycleNext()
    {
        var presentIds = new HashSet<string>(_deviceProvider.GetDevices(_kind).Select(d => d.DeviceId));
        var presentSwitchList = _switchListDeviceIds.Where(presentIds.Contains).ToList();

        if (presentSwitchList.Count == 0)
            throw new InvalidOperationException("No switch-list device is currently present.");

        var currentId = _deviceProvider.GetDefaultDeviceId(_kind);
        var currentIndex = currentId is null ? -1 : presentSwitchList.IndexOf(currentId);
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % presentSwitchList.Count;
        var nextId = presentSwitchList[nextIndex];

        _deviceProvider.SetDefaultDevice(_kind, nextId);
        return nextId;
    }
}
