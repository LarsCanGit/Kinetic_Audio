using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using GroupedAudioMixer.Core.Devices;

namespace GroupedAudioMixer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Placeholder default until a settings UI exists to let the user pick and persist it (design
    /// doc decision D2, config storage, is still open per project CLAUDE.md).
    /// </summary>
    private static readonly (ModifierKeys Modifiers, Key Key) DefaultCycleHotkey = (ModifierKeys.Control | ModifierKeys.Alt, Key.F11);

    private CoreAudioDeviceProvider? _deviceProvider;
    private GlobalHotkeyListener? _hotkeyListener;
    private DeviceSwitcher? _playbackDeviceSwitcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _deviceProvider = new CoreAudioDeviceProvider();

        // No persisted switch-list yet (D2 open), so the switch-list starts as every currently
        // present playback device; a settings UI will let the user curate this once it exists.
        var initialSwitchList = _deviceProvider.GetDevices(AudioDeviceKind.Playback)
            .Select(d => d.DeviceId)
            .ToList();
        _playbackDeviceSwitcher = new DeviceSwitcher(_deviceProvider, AudioDeviceKind.Playback, initialSwitchList);

        _hotkeyListener = new GlobalHotkeyListener();
        try
        {
            _hotkeyListener.Register(DefaultCycleHotkey.Modifiers, DefaultCycleHotkey.Key, () => _playbackDeviceSwitcher.CycleNext());
        }
        catch (InvalidOperationException)
        {
            // Hotkey already claimed by another application; the app still runs, just without the
            // global cycle-switch shortcut until a settings UI lets the user pick a free one.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyListener?.Dispose();
        _deviceProvider?.Dispose();
        base.OnExit(e);
    }
}

