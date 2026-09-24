using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KineticAudio.Core.Audio;
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
    private bool _isInitializing = true;
    private bool _isSyncingFromState;
    private Forms.NotifyIcon? _trayIcon;

    public ObservableCollection<GroupStripViewModel> Strips { get; } = new();

    public MainWindow(VolumeStateManager volumeState, IAudioSessionProvider sessionProvider)
    {
        InitializeComponent();

        _volumeState = volumeState;
        _sessionProvider = sessionProvider;

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
            foreach (var session in sessions)
            {
                if (_volumeState.GetGroupIdForSession(session.SessionKey) != strip.GroupId)
                    continue;

                strip.SessionChips.Add(new SessionChipViewModel(session.SessionKey, session.DisplayName));
            }

            strip.HasNoSessions = strip.SessionChips.Count == 0;
        }
    }
}
