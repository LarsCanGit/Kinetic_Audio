using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace KineticAudio;

/// <summary>
/// UI-shaping view-model for one mixer strip (Main Out, a group, or Mic), rendered by the single
/// shared strip <c>DataTemplate</c> in an <c>ItemsControl</c> rather than hand-duplicated XAML.
/// </summary>
public sealed class GroupStripViewModel(string? groupId, bool isMasterStrip, string title) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Null for the Main Out strip, which writes to <c>VolumeStateManager.Main</c> instead.</summary>
    public string? GroupId { get; } = groupId;

    public bool IsMasterStrip { get; } = isMasterStrip;

    public bool ShowSessionCard => !IsMasterStrip;

    /// <summary>The Main Out strip shows the active playback device where group strips show apps.</summary>
    public bool ShowDeviceCard => IsMasterStrip;

    private string _playbackDeviceName = string.Empty;
    public string PlaybackDeviceName
    {
        get => _playbackDeviceName;
        set => SetField(ref _playbackDeviceName, value);
    }

    /// <summary>Playback apps can be dragged onto any group strip except Main Out and Mic.</summary>
    public bool AcceptsSessionDrop => !IsMasterStrip && GroupId != Core.Settings.WellKnownGroupIds.MicId;

    private bool _isDropTarget;
    /// <summary>True while a chip is dragged over this strip, for the highlight.</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetField(ref _isDropTarget, value);
    }

    /// <summary>Stashed here (not bound) so the shared fader handler can update the right strip's
    /// VU meter, since there's no bindable audio-level source yet.</summary>
    public Grid? MeterGridElement { get; set; }

    /// <summary>Stashed here (not bound) so the shared handler can build tick labels once per strip.</summary>
    public Grid? TicksGridElement { get; set; }

    private string _title = title;
    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    private double _faderValueDb;
    public double FaderValueDb
    {
        get => _faderValueDb;
        set => SetField(ref _faderValueDb, value);
    }

    private string _percentText = string.Empty;
    public string PercentText
    {
        get => _percentText;
        set => SetField(ref _percentText, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    private bool _hasNoSessions = true;
    public bool HasNoSessions
    {
        get => _hasNoSessions;
        set => SetField(ref _hasNoSessions, value);
    }

    public ObservableCollection<SessionChipViewModel> SessionChips { get; } = new();

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
