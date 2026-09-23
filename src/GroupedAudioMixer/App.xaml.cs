using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Input;
using GroupedAudioMixer.Core.Audio;
using GroupedAudioMixer.Core.Devices;
using GroupedAudioMixer.Core.Mapping;
using GroupedAudioMixer.Core.Session;
using GroupedAudioMixer.Core.Settings;
using GroupedAudioMixer.Core.Volume;

namespace GroupedAudioMixer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private CoreAudioDeviceProvider? _deviceProvider;
    private GlobalHotkeyListener? _hotkeyListener;
    private IDisposable? _hotkeyRegistration;
    private DeviceSwitcher? _playbackDeviceSwitcher;
    private DeviceSwitchOsd? _deviceSwitchOsd;

    private CoreAudioSessionProvider? _sessionProvider;
    private SessionGroupLinker? _sessionGroupLinker;
    private SessionVolumeWriter? _sessionVolumeWriter;

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

        _hotkeyListener = new GlobalHotkeyListener();
        TryRegisterHotkey(_settings.CycleHotkeyModifiers, _settings.CycleHotkeyKey);

        if (_settings.Groups.Count == 0)
            _settings.Groups.Add(new GroupDefinition(GroupedAudioMixer.MainWindow.Group1Id, "Group 1"));

        var defaultGroupId = _settings.Groups.Any(g => g.Id == GroupedAudioMixer.MainWindow.Group1Id)
            ? GroupedAudioMixer.MainWindow.Group1Id
            : _settings.Groups[0].Id;

        _volumeState = new VolumeStateManager(
            _settings.Groups.Select(g => new MixerGroup(g.Id, g.Name)),
            defaultGroupId);

        _executableGroupMap = new ExecutableGroupMap(new JsonExecutableGroupMapStore());

        _sessionProvider = new CoreAudioSessionProvider();
        _sessionGroupLinker = new SessionGroupLinker(_sessionProvider, _executableGroupMap, _volumeState);
        _sessionVolumeWriter = new SessionVolumeWriter(_sessionProvider, _volumeState);

        if (_settings.RunAtStartup)
            StartupRegistration.SetEnabled(true, Environment.ProcessPath ?? string.Empty);

        var mainWindow = new GroupedAudioMixer.MainWindow(_volumeState);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>Disposes the current hotkey registration and registers the new binding. Returns
    /// null on success, or an error message if the new binding is already claimed (the old
    /// binding is re-registered in that case so the hotkey isn't left silently unbound).</summary>
    public string? TryRebindCycleHotkey(ModifierKeys modifiers, Key key)
    {
        var previousModifiers = _settings!.CycleHotkeyModifiers;
        var previousKey = _settings.CycleHotkeyKey;

        _hotkeyRegistration?.Dispose();
        _hotkeyRegistration = null;

        if (TryRegisterHotkey(modifiers, key))
        {
            _settings.CycleHotkeyModifiers = modifiers;
            _settings.CycleHotkeyKey = key;
            _settingsStore!.Save(_settings);
            return null;
        }

        TryRegisterHotkey(previousModifiers, previousKey);
        return $"Failed to register {modifiers}+{key}. It may already be in use by another application.";
    }

    private bool TryRegisterHotkey(ModifierKeys modifiers, Key key)
    {
        try
        {
            _hotkeyRegistration = _hotkeyListener!.Register(modifiers, key, OnCycleHotkeyPressed);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Hotkey already claimed by another application; the app still runs, just without the
            // global cycle-switch shortcut until the user picks a free one in Settings.
            return false;
        }
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
        _sessionVolumeWriter?.Dispose();
        _sessionGroupLinker?.Dispose();
        _sessionProvider?.Dispose();
        _hotkeyRegistration?.Dispose();
        _hotkeyListener?.Dispose();
        _deviceProvider?.Dispose();
        base.OnExit(e);
    }
}

