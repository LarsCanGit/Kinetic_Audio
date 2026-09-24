using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using KineticAudio.Core.Audio;
using KineticAudio.Core.Devices;
using KineticAudio.Core.Mapping;
using KineticAudio.Core.Session;
using KineticAudio.Core.Settings;
using KineticAudio.Core.Volume;

namespace KineticAudio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CoreAudioDeviceProvider? _deviceProvider;
    private GlobalHotkeyListener? _hotkeyListener;
    private readonly Dictionary<HotkeyAction, IDisposable> _hotkeyRegistrations = new();
    private DeviceSwitcher? _playbackDeviceSwitcher;
    private DeviceSwitchOsd? _deviceSwitchOsd;

    private CoreAudioSessionProvider? _sessionProvider;
    private SessionGroupLinker? _sessionGroupLinker;
    private SessionVolumeWriter? _sessionVolumeWriter;
    private VolumeDriftReconciler? _volumeDriftReconciler;

    private AppSettings? _settings;
    private IAppSettingsStore? _settingsStore;
    private VolumeStateManager? _volumeState;
    private ExecutableGroupMap? _executableGroupMap;

    public AppSettings Settings => _settings!;
    public IAppSettingsStore SettingsStore => _settingsStore!;
    public IAudioDeviceProvider DeviceProvider => _deviceProvider!;
    public DeviceSwitcher PlaybackDeviceSwitcher => _playbackDeviceSwitcher!;
    public VolumeStateManager VolumeState => _volumeState!;
    public ExecutableGroupMap ExecutableGroupMap => _executableGroupMap!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsStore = new JsonAppSettingsStore();
        _settings = _settingsStore.Load();

        _deviceProvider = new CoreAudioDeviceProvider();

        var currentPlaybackIds = _deviceProvider.GetDevices(AudioDeviceKind.Playback).Select(d => d.DeviceId).ToList();
        _settings.PlaybackDeviceOrder = DeviceOrderReconciler.Reconcile(_settings.PlaybackDeviceOrder, currentPlaybackIds);
        _settings.PlaybackSwitchCount = Math.Clamp(_settings.PlaybackSwitchCount, 0, _settings.PlaybackDeviceOrder.Count);

        var currentRecordingIds = _deviceProvider.GetDevices(AudioDeviceKind.Recording).Select(d => d.DeviceId).ToList();
        _settings.RecordingDeviceOrder = DeviceOrderReconciler.Reconcile(_settings.RecordingDeviceOrder, currentRecordingIds);

        _settingsStore.Save(_settings);

        var initialSwitchList = _settings.PlaybackDeviceOrder.Take(_settings.PlaybackSwitchCount).ToList();
        _playbackDeviceSwitcher = new DeviceSwitcher(_deviceProvider, AudioDeviceKind.Playback, initialSwitchList);

        if (_settings.Groups.Count == 0)
            _settings.Groups.Add(new GroupDefinition(WellKnownGroupIds.Group1Id, "Group 1"));

        var defaultGroupId = DefaultGroupResolver.ResolveDefaultGroupId(_settings.Groups);

        // The Mic strip drives recording sessions: Main Out (the playback master) doesn't scale
        // or mute it, and capture sessions always link to it.
        var micGroupId = _settings.Groups.Any(g => g.Id == WellKnownGroupIds.MicId) ? WellKnownGroupIds.MicId : null;

        _volumeState = new VolumeStateManager(
            _settings.Groups.Select(g => new MixerGroup(g.Id, g.Name)),
            defaultGroupId,
            micGroupId is null ? null : [micGroupId]);

        _executableGroupMap = new ExecutableGroupMap(new JsonExecutableGroupMapStore());

        _sessionProvider = new CoreAudioSessionProvider();
        // Fallback notices can fire from the constructor (sessions already running) before the
        // window exists, and later on Core Audio threads; BeginInvoke covers both.
        _sessionGroupLinker = new SessionGroupLinker(_sessionProvider, _executableGroupMap, _volumeState,
            (_, e) => Dispatcher.BeginInvoke(() => (MainWindow as KineticAudio.MainWindow)?.ShowMappingFallback(e)),
            captureGroupId: micGroupId);
        _sessionVolumeWriter = new SessionVolumeWriter(_sessionProvider, _volumeState);
        _volumeDriftReconciler = new VolumeDriftReconciler(_sessionProvider, _volumeState);
        _volumeDriftReconciler.Start();

        // After _volumeState exists: the Main mute hotkey acts on it.
        _hotkeyListener = new GlobalHotkeyListener();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var (modifiers, key) = GetHotkey(action);
            TryRegisterHotkey(action, modifiers, key);
        }

        if (_settings.RunAtStartup)
            StartupRegistration.SetEnabled(true, Environment.ProcessPath ?? string.Empty);

        var mainWindow = new KineticAudio.MainWindow(_volumeState, _sessionProvider, _sessionGroupLinker);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    public (ModifierKeys Modifiers, Key Key) GetHotkey(HotkeyAction action) => action switch
    {
        HotkeyAction.CycleDevice => (_settings!.CycleHotkeyModifiers, _settings.CycleHotkeyKey),
        HotkeyAction.MuteMain => (_settings!.MuteMainHotkeyModifiers, _settings.MuteMainHotkeyKey),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private void SetHotkey(HotkeyAction action, ModifierKeys modifiers, Key key)
    {
        switch (action)
        {
            case HotkeyAction.CycleDevice:
                _settings!.CycleHotkeyModifiers = modifiers;
                _settings.CycleHotkeyKey = key;
                break;
            case HotkeyAction.MuteMain:
                _settings!.MuteMainHotkeyModifiers = modifiers;
                _settings.MuteMainHotkeyKey = key;
                break;
        }
    }

    /// <summary>Disposes the action's current hotkey registration and registers the new binding.
    /// Returns null on success, or an error message if the new binding is taken by the other
    /// action or by another application (the old binding is re-registered in that case so the
    /// hotkey isn't left silently unbound).</summary>
    public string? TryRebindHotkey(HotkeyAction action, ModifierKeys modifiers, Key key)
    {
        foreach (var other in Enum.GetValues<HotkeyAction>())
        {
            if (other != action && GetHotkey(other) == (modifiers, key))
                return $"{modifiers}+{key} is already used by the {DescribeHotkey(other)} hotkey.";
        }

        var (previousModifiers, previousKey) = GetHotkey(action);

        if (_hotkeyRegistrations.Remove(action, out var registration))
            registration.Dispose();

        if (TryRegisterHotkey(action, modifiers, key))
        {
            SetHotkey(action, modifiers, key);
            _settingsStore!.Save(_settings!);
            return null;
        }

        TryRegisterHotkey(action, previousModifiers, previousKey);
        return $"Failed to register {modifiers}+{key}. It may already be in use by another application.";
    }

    private static string DescribeHotkey(HotkeyAction action) => action switch
    {
        HotkeyAction.CycleDevice => "cycle-switch",
        HotkeyAction.MuteMain => "Main mute",
        _ => action.ToString(),
    };

    private bool TryRegisterHotkey(HotkeyAction action, ModifierKeys modifiers, Key key)
    {
        Action callback = action switch
        {
            HotkeyAction.CycleDevice => OnCycleHotkeyPressed,
            HotkeyAction.MuteMain => OnMuteMainHotkeyPressed,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        try
        {
            _hotkeyRegistrations[action] = _hotkeyListener!.Register(modifiers, key, callback);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Hotkey already claimed by another application; the app still runs, just without
            // this global shortcut until the user picks a free one in Settings.
            return false;
        }
    }

    /// <summary>Runs on the dispatcher thread (see <see cref="GlobalHotkeyListener.Register"/>).</summary>
    private void OnMuteMainHotkeyPressed()
    {
        _volumeState!.ToggleMainMute();
        // EffectiveVolumeChanged doesn't fire when no sessions are live, so refresh the Main
        // strip's mute button directly.
        (MainWindow as KineticAudio.MainWindow)?.SyncMuteStatesFromState();
    }

    private void OnCycleHotkeyPressed()
    {
        string switchedToId;
        try
        {
            switchedToId = _playbackDeviceSwitcher!.CycleNext();
        }
        catch (InvalidOperationException)
        {
            // No switch-list device is currently present (e.g. all unplugged); no-op rather than
            // crashing the hotkey message loop.
            return;
        }

        if (!_settings!.NotifyOnSwitch)
            return;

        var deviceName = _deviceProvider!.GetDevices(AudioDeviceKind.Playback)
            .FirstOrDefault(d => d.DeviceId == switchedToId)?.DisplayName ?? switchedToId;

        _deviceSwitchOsd ??= new DeviceSwitchOsd();
        _deviceSwitchOsd.ShowSwitchedTo(deviceName);
    }

    public void SaveSettings() => _settingsStore!.Save(_settings!);

    protected override void OnExit(ExitEventArgs e)
    {
        _deviceSwitchOsd?.Close();
        _volumeDriftReconciler?.Dispose();
        _sessionVolumeWriter?.Dispose();
        _sessionGroupLinker?.Dispose();
        _sessionProvider?.Dispose();
        foreach (var registration in _hotkeyRegistrations.Values)
            registration.Dispose();
        _hotkeyListener?.Dispose();
        _deviceProvider?.Dispose();
        base.OnExit(e);
    }
}

public enum HotkeyAction
{
    CycleDevice,
    MuteMain,
}
