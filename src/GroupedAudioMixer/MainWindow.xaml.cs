using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using KineticAudio.Core.Audio;
using KineticAudio.Core.Session;
using KineticAudio.Core.Settings;
using KineticAudio.Core.Volume;
using Forms = System.Windows.Forms;

namespace KineticAudio;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const int MeterSegmentCount = 14;
    private static readonly double[] TickValuesDb = { 0, -12, -24, -36, -48, -60 };

    private readonly VolumeStateManager _volumeState;
    private readonly IAudioSessionProvider _sessionProvider;
    private readonly SessionGroupLinker _sessionGroupLinker;
    private DispatcherTimer? _noticeTimer;
    private Point _chipDragStart;
    private bool _isInitializing = true;
    private bool _isSyncingFromState;
    private Forms.NotifyIcon? _trayIcon;

    public ObservableCollection<GroupStripViewModel> Strips { get; } = new();

    public MainWindow(VolumeStateManager volumeState, IAudioSessionProvider sessionProvider, SessionGroupLinker sessionGroupLinker)
    {
        InitializeComponent();

        _volumeState = volumeState;
        _sessionProvider = sessionProvider;
        _sessionGroupLinker = sessionGroupLinker;

        Width = SystemParameters.PrimaryScreenWidth / 2;
        Height = SystemParameters.PrimaryScreenHeight / 2;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;

        var mainStrip = new GroupStripViewModel(groupId: null, isMasterStrip: true, title: "MAIN OUT");
        Strips.Add(mainStrip);
        foreach (var group in _volumeState.Groups)
        {
            var strip = new GroupStripViewModel(group.Id, isMasterStrip: false, title: group.Name.ToUpperInvariant());
            Strips.Add(strip);
        }

        foreach (var strip in Strips)
        {
            var db = strip.IsMasterStrip
                ? DbFromLinear(_volumeState.Main.Volume)
                : DbFromLinear(_volumeState.GetGroup(strip.GroupId!).Volume);
            strip.FaderValueDb = db;
            strip.PercentText = FormatPercent(db);
            strip.IsMuted = strip.IsMasterStrip ? _volumeState.Main.IsMuted : _volumeState.GetGroup(strip.GroupId!).IsMuted;
        }

        // Core Audio session notifications can arrive on a non-UI thread, so marshal back to the
        // dispatcher before touching the bound SessionChips collections. BeginInvoke, not Invoke:
        // EffectiveVolumeChanged is raised under VolumeStateManager's lock, and blocking that
        // thread on the UI thread (which may itself be waiting for the lock) would deadlock.
        _volumeState.EffectiveVolumeChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            SyncFadersFromState();
            RebuildAllSessionCards();
        });
        _sessionProvider.SessionRemoved += (_, _) => Dispatcher.BeginInvoke(RebuildAllSessionCards);
        RebuildAllSessionCards();

        _isInitializing = false;
    }

    /// <summary>Re-reads group names into the strip headers (after a rename in Settings).</summary>
    public void RefreshStripTitles()
    {
        foreach (var strip in Strips)
            if (!strip.IsMasterStrip)
                strip.Title = _volumeState.GetGroup(strip.GroupId!).Name.ToUpperInvariant();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).Settings.MinimizeToTray)
            HideToTray();
        else
            WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SettingsCog_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Marked handled so the click doesn't bubble to the header's DragMove handler,
        // which would otherwise capture the mouse and swallow the button-up.
        e.Handled = true;

        var settingsWindow = new SettingsWindow((App)Application.Current) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void HideToTray()
    {
        Hide();

        if (_trayIcon == null)
        {
            _trayIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "Kinetic Audio",
                Visible = true,
            };
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (_, _) => RestoreFromTray());
            contextMenu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
            _trayIcon.ContextMenuStrip = contextMenu;
        }
        else
        {
            _trayIcon.Visible = true;
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();

        if (_trayIcon != null)
            _trayIcon.Visible = false;
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnClosed(e);
    }

    private void Ticks_Loaded(object sender, RoutedEventArgs e)
    {
        var tickGrid = (System.Windows.Controls.Grid)sender;
        if (tickGrid.DataContext is GroupStripViewModel strip)
            strip.TicksGridElement = tickGrid;

        if (tickGrid.Children.Count > 0)
            return; // Loaded can re-fire (e.g. tab/visibility changes); build once.

        BuildTicks(tickGrid);
    }

    private void Meter_Loaded(object sender, RoutedEventArgs e)
    {
        var meterGrid = (System.Windows.Controls.Grid)sender;
        if (meterGrid.DataContext is not GroupStripViewModel strip)
            return;

        strip.MeterGridElement = meterGrid;

        if (meterGrid.Children.Count == 0)
            BuildMeter(meterGrid);

        UpdateMeter(meterGrid, PercentOfRange(strip.FaderValueDb));
    }

    private static void BuildTicks(System.Windows.Controls.Grid tickGrid)
    {
        for (var i = 0; i < TickValuesDb.Length; i++)
        {
            tickGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var db = TickValuesDb[i];
            var label = new TextBlock
            {
                // Bottom stop is silence, not -60 dB.
                Text = db <= FaderScale.MinDb ? "-∞" : db.ToString("0"),
                Style = (Style)tickGrid.FindResource("TickLabelStyle"),
                Foreground = db == 0
                    ? (Brush)tickGrid.FindResource("AmberBrush")
                    : (Brush)tickGrid.FindResource("TickBrush"),
                VerticalAlignment = db == TickValuesDb[0]
                    ? VerticalAlignment.Top
                    : db == TickValuesDb[^1]
                        ? VerticalAlignment.Bottom
                        : VerticalAlignment.Center,
            };
            System.Windows.Controls.Grid.SetRow(label, i);
            tickGrid.Children.Add(label);
        }
    }

    // Segment n (0 = top/loudest) lights when the fader's percent-of-range exceeds n / 14, per the
    // design spec; there is no live audio-level source yet, so the meter reflects fader position.
    private static void BuildMeter(System.Windows.Controls.Grid meterGrid)
    {
        var litBrushes = new[]
        {
            (Brush)meterGrid.FindResource("MeterRedLitBrush"),
            (Brush)meterGrid.FindResource("MeterYellowLitBrush"),
            (Brush)meterGrid.FindResource("MeterGreenLitBrush"),
        };
        var unlitBrushes = new[]
        {
            (Brush)meterGrid.FindResource("MeterRedUnlitBrush"),
            (Brush)meterGrid.FindResource("MeterYellowUnlitBrush"),
            (Brush)meterGrid.FindResource("MeterGreenUnlitBrush"),
        };
        var style = (Style)meterGrid.FindResource("MeterSegmentStyle");

        for (var i = 0; i < MeterSegmentCount; i++)
        {
            meterGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top 2 rows red, next 3 yellow, remaining 9 green; row 0 is the top of the Grid, so the
            // loudest (red) tier occupies the first rows to sit at the top of the meter.
            var tier = i < 2 ? 0 : i < 5 ? 1 : 2;
            var rectangle = new Rectangle { Style = style, Fill = unlitBrushes[tier], Tag = (litBrushes[tier], unlitBrushes[tier]) };
            System.Windows.Controls.Grid.SetRow(rectangle, i);
            meterGrid.Children.Add(rectangle);
        }

        UpdateMeter(meterGrid, 0.0);
    }

    private static void UpdateMeter(System.Windows.Controls.Grid meterGrid, double percentOfRange)
    {
        foreach (var child in meterGrid.Children)
        {
            if (child is not Rectangle rectangle)
                continue;

            // Row 0 is the top (loudest) segment, so its threshold is the highest.
            var row = System.Windows.Controls.Grid.GetRow(rectangle);
            var threshold = (double)(MeterSegmentCount - row) / MeterSegmentCount;
            var (litBrush, unlitBrush) = ((Brush, Brush))rectangle.Tag;
            rectangle.Fill = percentOfRange >= threshold ? litBrush : unlitBrush;
        }
    }

    private static float LinearFromDb(double db) => FaderScale.LinearFromDb(db);

    private static double DbFromLinear(float linear) => FaderScale.DbFromLinear(linear);

    private static double PercentOfRange(double db) => FaderScale.FractionFromDb(db);

    private static string FormatPercent(double db) => $"{FaderScale.PercentFromDb(db)}%";

    private void Fader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var slider = (Slider)sender;
        if (slider.DataContext is not GroupStripViewModel strip)
            return;

        strip.PercentText = FormatPercent(e.NewValue);
        if (strip.MeterGridElement != null)
            UpdateMeter(strip.MeterGridElement, PercentOfRange(e.NewValue));

        if (_isInitializing || _isSyncingFromState)
            return;

        if (strip.IsMasterStrip)
            _volumeState.SetMainVolume(LinearFromDb(e.NewValue));
        else
            _volumeState.SetGroupVolume(strip.GroupId!, LinearFromDb(e.NewValue));
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Primitives.ToggleButton)sender;
        if (button.DataContext is not GroupStripViewModel strip)
            return;

        var isMuted = button.IsChecked == true;
        if (strip.IsMasterStrip)
        {
            if (isMuted) _volumeState.MuteMain(); else _volumeState.UnmuteMain();
        }
        else
        {
            if (isMuted) _volumeState.MuteGroup(strip.GroupId!); else _volumeState.UnmuteGroup(strip.GroupId!);
        }
    }

    /// <summary>
    /// Updates every strip's mute button from state, for mutes that didn't come from a button
    /// click (the Main mute hotkey). Setting IsMuted doesn't raise Click, so this never re-mutes.
    /// </summary>
    public void SyncMuteStatesFromState()
    {
        foreach (var strip in Strips)
            strip.IsMuted = strip.IsMasterStrip ? _volumeState.Main.IsMuted : _volumeState.GetGroup(strip.GroupId!).IsMuted;
    }

    /// <summary>
    /// Moves group faders to match state changed from outside the UI (external volume drift
    /// reconciled by <see cref="VolumeDriftReconciler"/>). Faders already matching state are left
    /// alone so a sync never nudges a fader the user just set.
    /// </summary>
    private void SyncFadersFromState()
    {
        _isSyncingFromState = true;
        try
        {
            foreach (var strip in Strips)
            {
                if (strip.IsMasterStrip)
                    continue;

                var stateVolume = _volumeState.GetGroup(strip.GroupId!).Volume;
                var faderVolume = LinearFromDb(strip.FaderValueDb);
                if (Math.Abs(faderVolume - stateVolume) <= 0.005f)
                    continue;

                strip.FaderValueDb = DbFromLinear(stateVolume);
            }
        }
        finally
        {
            _isSyncingFromState = false;
        }
    }

    private void RebuildAllSessionCards()
    {
        var sessions = _sessionProvider.GetActiveSessions();

        foreach (var strip in Strips)
        {
            if (!strip.ShowSessionCard)
                continue;

            strip.SessionChips.Clear();
            var seenExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions)
            {
                // An unlinked recording session (no Mic strip) would otherwise report the default
                // group and show up as playback.
                var isCapture = session.Flow == AudioSessionFlow.Capture;
                if (isCapture && !_volumeState.IsSessionLinked(session.SessionKey))
                    continue;
                if (_volumeState.GetGroupIdForSession(session.SessionKey) != strip.GroupId)
                    continue;

                // One chip per executable (an app can open several sessions); a session with no
                // executable path keeps its own chip keyed by session so it still shows up.
                if (!seenExecutables.Add(string.IsNullOrEmpty(session.ExecutablePath) ? session.SessionKey : session.ExecutablePath))
                    continue;

                strip.SessionChips.Add(new SessionChipViewModel(session.ExecutablePath, session.DisplayName, strip.GroupId, isCapture));
            }

            strip.HasNoSessions = strip.SessionChips.Count == 0;
        }
    }

    /// <summary>
    /// Surfaces a stale executable-to-group mapping that was moved to the default group (#13), so
    /// the fallback isn't silent. Must be called on the UI thread.
    /// </summary>
    public void ShowMappingFallback(MappingFallbackEventArgs e)
    {
        var appName = System.IO.Path.GetFileNameWithoutExtension(e.ExecutablePath);
        var fallbackName = _volumeState.HasGroup(e.FallbackGroupId) ? _volumeState.GetGroup(e.FallbackGroupId).Name : e.FallbackGroupId;
        // The group can still exist when it's the Mic strip, which never holds playback.
        ShowNotice(_volumeState.HasGroup(e.MissingGroupId)
            ? $"{appName}'s playback can't go to {_volumeState.GetGroup(e.MissingGroupId).Name}. Moved to {fallbackName}."
            : $"{appName}'s saved group no longer exists. Moved to {fallbackName}.");
    }

    private void ShowNotice(string text)
    {
        NoticeText.Text = text;
        NoticeText.Visibility = Visibility.Visible;

        _noticeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _noticeTimer.Stop();
        _noticeTimer.Tick -= NoticeTimer_Tick;
        _noticeTimer.Tick += NoticeTimer_Tick;
        _noticeTimer.Start();
    }

    private void NoticeTimer_Tick(object? sender, EventArgs e) => HideNotice();

    private void NoticeText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideNotice();
        e.Handled = true; // don't start a window drag from the header
    }

    private void HideNotice()
    {
        _noticeTimer?.Stop();
        NoticeText.Visibility = Visibility.Collapsed;
    }

    // Drag-to-group (#3): drag an app chip from one strip's session card onto another group strip.

    private void Chip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _chipDragStart = e.GetPosition(this);

    private void Chip_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement { DataContext: SessionChipViewModel chip } element || !chip.CanDrag) return;

        var delta = e.GetPosition(this) - _chipDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Outline just the chip being dragged, for the duration of the drag.
        var chipBorder = element as Border;
        if (chipBorder is not null)
        {
            chipBorder.BorderBrush = (Brush)FindResource("AmberBrush");
        }

        try
        {
            DragDrop.DoDragDrop(element, new DataObject(typeof(SessionChipViewModel), chip), DragDropEffects.Move);
        }
        finally
        {
            chipBorder?.ClearValue(Border.BorderBrushProperty);
            foreach (var strip in Strips) strip.IsDropTarget = false;
        }
    }

    private static bool TryGetDrop(object sender, DragEventArgs e, out GroupStripViewModel strip, out SessionChipViewModel chip)
    {
        strip = null!;
        chip = null!;
        if (sender is not FrameworkElement { DataContext: GroupStripViewModel target } || !target.AcceptsSessionDrop) return false;
        if (e.Data.GetData(typeof(SessionChipViewModel)) is not SessionChipViewModel dragged) return false;
        // The drag starts over the chip's own strip; don't highlight or accept a no-op move.
        if (dragged.GroupId == target.GroupId) return false;
        strip = target;
        chip = dragged;
        return true;
    }

    private void Strip_DragOver(object sender, DragEventArgs e)
    {
        var accepts = TryGetDrop(sender, e, out var strip, out _);
        e.Effects = accepts ? DragDropEffects.Move : DragDropEffects.None;
        if (accepts) strip.IsDropTarget = true;
        e.Handled = true;
    }

    private void Strip_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupStripViewModel strip })
            strip.IsDropTarget = false;
    }

    private void Strip_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDrop(sender, e, out var strip, out var chip)) return;
        strip.IsDropTarget = false;
        e.Handled = true;

        // Persists through ExecutableGroupMap's single writer and reassigns live sessions; the
        // resulting EffectiveVolumeChanged rebuilds the session cards.
        _sessionGroupLinker.AssignExecutableToGroup(chip.ExecutablePath, strip.GroupId!);
    }
}
