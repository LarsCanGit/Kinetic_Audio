using System.Windows.Input;

namespace GroupedAudioMixer.Core.Settings;

/// <summary>Persisted user configuration: device order/hotkey-swap membership, cycle hotkey,
/// general options, and mixer groups.</summary>
public sealed class AppSettings
{
    public List<string> PlaybackDeviceOrder { get; set; } = new();
    public int PlaybackSwitchCount { get; set; } = 2;
    public List<string> RecordingDeviceOrder { get; set; } = new();

    public ModifierKeys CycleHotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
    public Key CycleHotkeyKey { get; set; } = Key.F11;

    public bool RunAtStartup { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool NotifyOnSwitch { get; set; }

    public List<GroupDefinition> Groups { get; set; } = new()
    {
        new GroupDefinition("group1", "Group 1"),
        new GroupDefinition("mic", "Mic"),
    };
}
