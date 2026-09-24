using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KineticAudio.Core.Audio;
using KineticAudio.Core.Devices;
using KineticAudio.Core.Mapping;
using KineticAudio.Core.Settings;
using KineticAudio.Core.Volume;

namespace KineticAudio;

public partial class SettingsWindow : Window
{
    // Sentinel marking the divider's position within the playback reorder list.
    private sealed class DividerMarker { public static readonly DividerMarker Instance = new(); }

    private readonly App _app;
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAudioDeviceProvider _deviceProvider;
    private readonly DeviceSwitcher _playbackDeviceSwitcher;
    private readonly VolumeStateManager _volumeState;
    private readonly ExecutableGroupMap _executableGroupMap;

    private List<object> _playbackItems = new();
    private List<AudioDeviceInfo> _recordingItems = new();
    private HashSet<string> _presentPlaybackIds = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _presentRecordingIds = new(StringComparer.OrdinalIgnoreCase);
    private object? _dragSource;

    public SettingsWindow(App app)
    {
        InitializeComponent();

        _app = app;
        _settings = app.Settings;
        _settingsStore = app.SettingsStore;
        _deviceProvider = app.DeviceProvider;
        _playbackDeviceSwitcher = app.PlaybackDeviceSwitcher;
        _volumeState = app.VolumeState;
        _executableGroupMap = app.ExecutableGroupMap;

        LoadDevicesTab();
        LoadHotkeyAndGeneralTab();
        LoadGroupsTab();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ----- Devices tab -----

    private void LoadDevicesTab()
    {
        var playbackDevices = _deviceProvider.GetDevices(AudioDeviceKind.Playback);
        var byId = playbackDevices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        _presentPlaybackIds = new HashSet<string>(byId.Keys, StringComparer.OrdinalIgnoreCase);

        _playbackItems = new List<object>();
        foreach (var id in _settings.PlaybackDeviceOrder)
            if (byId.TryGetValue(id, out var device))
                _playbackItems.Add(device);

        // Unplugged devices aren't listed but keep their saved slot, so count only present ones.
        var dividerIndex = DeviceOrderReconciler.VisibleSwitchCount(
            _settings.PlaybackDeviceOrder, _settings.PlaybackSwitchCount, _presentPlaybackIds);
        _playbackItems.Insert(dividerIndex, DividerMarker.Instance);

        RenderPlaybackList();

        var recordingDevices = _deviceProvider.GetDevices(AudioDeviceKind.Recording);
        var recordingById = recordingDevices.ToDictionary(d => d.DeviceId, StringComparer.OrdinalIgnoreCase);
        _presentRecordingIds = new HashSet<string>(recordingById.Keys, StringComparer.OrdinalIgnoreCase);
        _recordingItems = _settings.RecordingDeviceOrder
            .Where(recordingById.ContainsKey)
            .Select(id => recordingById[id])
            .ToList();

        RenderRecordingList();
    }

    private void RenderPlaybackList()
    {
        PlaybackList.Children.Clear();
        foreach (var item in _playbackItems)
        {
            FrameworkElement tile = item is DividerMarker
                ? BuildDividerTile()
                : BuildDeviceTile((AudioDeviceInfo)item, PlaybackList);
            tile.Tag = item;
            WireDrag(tile, PlaybackList, _playbackItems, OnPlaybackReordered);
            PlaybackList.Children.Add(tile);
        }
    }

    private void RenderRecordingList()
    {
        RecordingList.Children.Clear();
        foreach (var device in _recordingItems)
        {
            var tile = BuildDeviceTile(device, RecordingList);
            tile.Tag = device;
            WireDrag(tile, RecordingList, _recordingItems.Cast<object>().ToList(), OnRecordingReordered);
            RecordingList.Children.Add(tile);
        }
    }

    private static Border BuildDeviceTile(AudioDeviceInfo device, Panel owner)
    {
        return new Border
        {
            Style = (Style)owner.FindResource("DeviceTileStyle"),
            Child = new TextBlock { Text = device.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis },
        };
    }

    private static Border BuildDividerTile()
    {
        return new Border
        {
            Height = 2,
            Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xA9, 0x3E)),
            Margin = new Thickness(0, 6, 0, 6),
        };
    }

    private void WireDrag(FrameworkElement tile, Panel owner, List<object> items, Action onReordered)
    {
        tile.PreviewMouseLeftButtonDown += (_, _) => _dragSource = tile.Tag;
        tile.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragSource != null)
                DragDrop.DoDragDrop(tile, tile.Tag!, DragDropEffects.Move);
        };
        tile.AllowDrop = true;
        tile.Drop += (_, e) =>
        {
            if (_dragSource == null || tile.Tag == null || ReferenceEquals(_dragSource, tile.Tag))
                return;

            var fromIndex = items.IndexOf(_dragSource);
            var toIndex = items.IndexOf(tile.Tag);
            if (fromIndex < 0 || toIndex < 0)
                return;

            items.RemoveAt(fromIndex);
            items.Insert(toIndex, _dragSource);
            _dragSource = null;
            onReordered();
        };
    }

    private void OnPlaybackReordered()
    {
        RenderPlaybackList();

        var dividerIndex = _playbackItems.IndexOf(DividerMarker.Instance);
        var deviceIds = _playbackItems.OfType<AudioDeviceInfo>().Select(d => d.DeviceId).ToList();

        // Devices before the divider marker in the combined list are the ones above the line.
        var aboveCount = _playbackItems.Take(dividerIndex).OfType<AudioDeviceInfo>().Count();

        // Merge back into the full order so unplugged devices keep their saved position (#10).
        (_settings.PlaybackDeviceOrder, _settings.PlaybackSwitchCount) = DeviceOrderReconciler.MergeVisibleOrder(
            _settings.PlaybackDeviceOrder, _settings.PlaybackSwitchCount, _presentPlaybackIds, deviceIds, aboveCount);
        _settingsStore.Save(_settings);

        // Absent switch-list devices stay in the list; DeviceSwitcher skips them until replugged.
        _playbackDeviceSwitcher.SetSwitchList(
            _settings.PlaybackDeviceOrder.Take(_settings.PlaybackSwitchCount).ToList());
    }

    private void OnRecordingReordered()
    {
        RenderRecordingList();
        (_settings.RecordingDeviceOrder, _) = DeviceOrderReconciler.MergeVisibleOrder(
            _settings.RecordingDeviceOrder, 0, _presentRecordingIds,
            _recordingItems.Select(d => d.DeviceId).ToList(), 0);
        _settingsStore.Save(_settings);
    }

    // ----- Hotkey & General tab -----

    private void LoadHotkeyAndGeneralTab()
    {
        HotkeyBox.Text = FormatHotkey(_settings.CycleHotkeyModifiers, _settings.CycleHotkeyKey);
        MuteMainHotkeyBox.Text = FormatHotkey(_settings.MuteMainHotkeyModifiers, _settings.MuteMainHotkeyKey);
        RunAtStartupCheck.IsChecked = _settings.RunAtStartup;
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        NotifyOnSwitchCheck.IsChecked = _settings.NotifyOnSwitch;
    }

    private static string FormatHotkey(ModifierKeys modifiers, Key key) =>
        modifiers == ModifierKeys.None ? key.ToString() : $"{modifiers}+{key}";

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e) => HotkeyErrorText.Text = string.Empty;

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        RebindFromKeyPress(e, HotkeyAction.CycleDevice, HotkeyBox, HotkeyErrorText);

    private void MuteMainHotkeyBox_GotFocus(object sender, RoutedEventArgs e) => MuteMainHotkeyErrorText.Text = string.Empty;

    private void MuteMainHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e) =>
        RebindFromKeyPress(e, HotkeyAction.MuteMain, MuteMainHotkeyBox, MuteMainHotkeyErrorText);

    private void RebindFromKeyPress(KeyEventArgs e, HotkeyAction action, TextBox box, TextBlock errorText)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        errorText.Text = _app.TryRebindHotkey(action, Keyboard.Modifiers, key) ?? string.Empty;
        var (modifiers, boundKey) = _app.GetHotkey(action);
        box.Text = FormatHotkey(modifiers, boundKey);
    }

    private void RunAtStartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = RunAtStartupCheck.IsChecked == true;
        _settings.RunAtStartup = enabled;
        StartupRegistration.SetEnabled(enabled, Environment.ProcessPath ?? string.Empty);
        _settingsStore.Save(_settings);
    }

    private void MinimizeToTrayCheck_Changed(object sender, RoutedEventArgs e)
    {
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _settingsStore.Save(_settings);
    }

    private void NotifyOnSwitchCheck_Changed(object sender, RoutedEventArgs e)
    {
        _settings.NotifyOnSwitch = NotifyOnSwitchCheck.IsChecked == true;
        _settingsStore.Save(_settings);
    }

    // ----- Groups tab -----

    private void LoadGroupsTab()
    {
        GroupsListBox.ItemsSource = null;
        GroupsListBox.DisplayMemberPath = "Name";
        GroupsListBox.ItemsSource = _settings.Groups;
        if (_settings.Groups.Count > 0)
            GroupsListBox.SelectedIndex = 0;
    }

    private void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenameGroupButton.IsEnabled = GroupsListBox.SelectedItem is GroupDefinition selected
            && GroupRenamePolicy.CanRename(selected.Id);
        RefreshMappingsList();
    }

    private void RefreshMappingsList()
    {
        MappingsListBox.ItemsSource = null;
        if (GroupsListBox.SelectedItem is not GroupDefinition group)
            return;

        var mapped = _executableGroupMap.GetAllMappings()
            .Where(kv => string.Equals(kv.Value, group.Id, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .OrderBy(x => x)
            .ToList();

        MappingsListBox.ItemsSource = mapped;
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsListBox.SelectedItem is not GroupDefinition group || !GroupRenamePolicy.CanRename(group.Id))
            return;

        var dialog = new TextInputDialog("Rename group:", group.Name) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.InputText))
            return;

        var newName = dialog.InputText.Trim();
        var index = _settings.Groups.FindIndex(g => g.Id == group.Id);
        if (index >= 0)
            _settings.Groups[index] = group with { Name = newName };

        _volumeState.GetGroup(group.Id).Name = newName;
        _settingsStore.Save(_settings);

        (Owner as MainWindow)?.RefreshStripTitles();
        LoadGroupsTab();
    }

    private void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingsListBox.SelectedItem is not string executablePath)
            return;

        _executableGroupMap.RemoveMapping(executablePath);
        RefreshMappingsList();
    }
}
