using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using GroupedAudioMixer.Core.Volume;

namespace GroupedAudioMixer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string Group1Id = "group1";
    private const string MicId = "mic";

    private readonly VolumeStateManager _volumeState;
    private bool _isInitializing = true;

    public MainWindow()
    {
        InitializeComponent();

        _volumeState = new VolumeStateManager(
            new[]
            {
                new MixerGroup(Group1Id, "Group 1"),
                new MixerGroup(MicId, "Mic"),
            },
            defaultGroupId: Group1Id);

        Width = SystemParameters.PrimaryScreenWidth / 2;
        Height = SystemParameters.PrimaryScreenHeight / 2;

        BuildMeter(MasterMeter);
        BuildMeter(Group1Meter);
        BuildMeter(MicMeter);

        MasterFader.Value = 1.0;
        Group1Fader.Value = LinearFromDb(-3.0);
        MicFader.Value = LinearFromDb(-6.0);

        _volumeState.SetGroupVolume(Group1Id, (float)Group1Fader.Value);
        _volumeState.SetGroupVolume(MicId, (float)MicFader.Value);
        _volumeState.SetMainVolume((float)MasterFader.Value);

        _isInitializing = false;
    }

    // The reference mockup's meter segments are a static illustration; no live audio-level metering
    // source exists in the codebase yet, so this builds a fixed decorative stack rather than pretending
    // to reflect real signal.
    private static void BuildMeter(System.Windows.Controls.Panel meterPanel)
    {
        var redBrush = (Brush)meterPanel.FindResource("MeterRedDimBrush");
        var yellowBrush = (Brush)meterPanel.FindResource("MeterYellowBrush");
        var greenBrush = (Brush)meterPanel.FindResource("MeterGreenBrush");
        var style = (Style)meterPanel.FindResource("MeterSegmentStyle");

        var segmentBrushes = new[]
        {
            redBrush, redBrush, redBrush,
            yellowBrush, yellowBrush, yellowBrush,
            greenBrush, greenBrush, greenBrush, greenBrush, greenBrush, greenBrush, greenBrush, greenBrush,
        };

        foreach (var brush in segmentBrushes)
        {
            meterPanel.Children.Add(new Rectangle { Style = style, Fill = brush });
        }
    }

    private static double LinearFromDb(double db) => Math.Pow(10, db / 20.0);

    private static string FormatDb(double linear)
    {
        var db = linear <= 0.0001 ? -60.0 : 20.0 * Math.Log10(linear);
        db = Math.Max(db, -60.0);
        return db.ToString("0.0");
    }

    private void MasterFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MasterDbText.Text = FormatDb(e.NewValue);
        if (_isInitializing)
            return;

        _volumeState.SetMainVolume((float)e.NewValue);
    }

    private void Group1Fader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Group1DbText.Text = FormatDb(e.NewValue);
        if (_isInitializing)
            return;

        _volumeState.SetGroupVolume(Group1Id, (float)e.NewValue);
    }

    private void MicFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MicDbText.Text = FormatDb(e.NewValue);
        if (_isInitializing)
            return;

        _volumeState.SetGroupVolume(MicId, (float)e.NewValue);
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
