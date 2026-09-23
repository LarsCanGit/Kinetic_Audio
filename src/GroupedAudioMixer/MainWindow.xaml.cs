using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GroupedAudioMixer.Core.Volume;
using Forms = System.Windows.Forms;

namespace GroupedAudioMixer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public const string Group1Id = "group1";
    public const string MicId = "mic";

    private const double FaderMinDb = -60.0;
    private const double FaderMaxDb = 12.0;
    private const int MeterSegmentCount = 14;
    private static readonly double[] TickValuesDb = { 12, 0, -12, -24, -36, -48, -60 };

    private readonly VolumeStateManager _volumeState;
    private bool _isInitializing = true;
    private Forms.NotifyIcon? _trayIcon;

    public MainWindow(VolumeStateManager volumeState)
    {
        InitializeComponent();

        _volumeState = volumeState;

        Width = SystemParameters.PrimaryScreenWidth / 2;
        Height = SystemParameters.PrimaryScreenHeight / 2;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;

        BuildTicks(MasterTicks);
        BuildTicks(Group1Ticks);
        BuildTicks(MicTicks);

        BuildMeter(MasterMeter);
        BuildMeter(Group1Meter);
        BuildMeter(MicMeter);

        MasterFader.Value = 0.0;
        Group1Fader.Value = -3.0;
        MicFader.Value = -6.0;

        _volumeState.SetMainVolume(LinearFromDb(MasterFader.Value));
        _volumeState.SetGroupVolume(Group1Id, LinearFromDb(Group1Fader.Value));
        _volumeState.SetGroupVolume(MicId, LinearFromDb(MicFader.Value));

        _isInitializing = false;
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
                Text = "Grouped Audio Mixer",
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

    private static void BuildTicks(System.Windows.Controls.Grid tickGrid)
    {
        for (var i = 0; i < TickValuesDb.Length; i++)
        {
            tickGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var db = TickValuesDb[i];
            var label = new TextBlock
            {
                Text = db.ToString("0"),
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

    private static float LinearFromDb(double db) => (float)Math.Pow(10, db / 20.0);

    private static double PercentOfRange(double db) => (db - FaderMinDb) / (FaderMaxDb - FaderMinDb);

    private void MasterFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MasterDbText.Text = e.NewValue.ToString("0.0");
        UpdateMeter(MasterMeter, PercentOfRange(e.NewValue));
        if (_isInitializing)
            return;

        _volumeState.SetMainVolume(LinearFromDb(e.NewValue));
    }

    private void Group1Fader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Group1DbText.Text = e.NewValue.ToString("0.0");
        UpdateMeter(Group1Meter, PercentOfRange(e.NewValue));
        if (_isInitializing)
            return;

        _volumeState.SetGroupVolume(Group1Id, LinearFromDb(e.NewValue));
    }

    private void MicFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MicDbText.Text = e.NewValue.ToString("0.0");
        UpdateMeter(MicMeter, PercentOfRange(e.NewValue));
        if (_isInitializing)
            return;

        _volumeState.SetGroupVolume(MicId, LinearFromDb(e.NewValue));
    }

    private void MasterMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (MasterMuteButton.IsChecked == true)
            _volumeState.MuteMain();
        else
            _volumeState.UnmuteMain();
    }

    private void Group1MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Group1MuteButton.IsChecked == true)
            _volumeState.MuteGroup(Group1Id);
        else
            _volumeState.UnmuteGroup(Group1Id);
    }

    private void MicMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (MicMuteButton.IsChecked == true)
            _volumeState.MuteGroup(MicId);
        else
            _volumeState.UnmuteGroup(MicId);
    }
}
