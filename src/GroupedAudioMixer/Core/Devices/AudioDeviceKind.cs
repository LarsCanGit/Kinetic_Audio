namespace KineticAudio.Core.Devices;

/// <summary>
/// Which endpoint stack a device belongs to. Playback and Recording each get their own default
/// endpoint and switch-list (design doc: "Device switching reuses the same favorites/switch-list
/// mechanism as playback... for the Recording Devices stack").
/// </summary>
public enum AudioDeviceKind
{
    Playback,
    Recording,
}
