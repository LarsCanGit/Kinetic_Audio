using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KineticAudio;

/// <summary>
/// Bottom-center, non-activating pop-up that briefly names the device the cycle-switch hotkey just
/// switched to (design doc D6: hotkey-switch UX). One instance is reused across switches so rapid
/// hotkey presses reset the dismiss timer instead of stacking windows.
/// </summary>
public partial class DeviceSwitchOsd : Window
{
    private const double DisplaySeconds = 1.8;
    private const double FadeSeconds = 0.3;
    private const double BottomMargin = 96;

    private readonly DispatcherTimer _dismissTimer;

    public DeviceSwitchOsd()
    {
        InitializeComponent();

        // Re-centers whenever the popup's measured size changes: on first Show() (size goes from
        // 0x0 to its real content size) and whenever a longer/shorter device name reflows it.
        SizeChanged += (_, _) => PositionBottomCenter();

        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DisplaySeconds) };
        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            FadeOutAndHide();
        };
    }

    public void ShowSwitchedTo(string deviceName)
    {
        DeviceNameText.Text = deviceName;

        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1.0;

        if (!IsVisible)
            Show();

        _dismissTimer.Start();
    }

    private void FadeOutAndHide()
    {
        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(FadeSeconds));
        fadeOut.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void PositionBottomCenter()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - BottomMargin;
    }
}
