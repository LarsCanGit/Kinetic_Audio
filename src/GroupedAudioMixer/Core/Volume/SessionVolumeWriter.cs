using GroupedAudioMixer.Core.Audio;

namespace GroupedAudioMixer.Core.Volume;

/// <summary>
/// The Phase 1 per-session writer (binding decision D4): subscribes to
/// <see cref="VolumeStateManager.EffectiveVolumeChanged"/> and pushes each affected session's
/// freshly computed volume to Core Audio via <see cref="IAudioSessionProvider.SetSessionVolume"/>.
/// This is the only consumer of that event today; a future Phase 3 mix engine would subscribe
/// the same way rather than duplicating volume math.
/// </summary>
public sealed class SessionVolumeWriter : IDisposable
{
    private readonly IAudioSessionProvider _sessionProvider;
    private readonly VolumeStateManager _volumeStateManager;

    public SessionVolumeWriter(IAudioSessionProvider sessionProvider, VolumeStateManager volumeStateManager)
    {
        _sessionProvider = sessionProvider;
        _volumeStateManager = volumeStateManager;

        _volumeStateManager.EffectiveVolumeChanged += OnEffectiveVolumeChanged;
    }

    private void OnEffectiveVolumeChanged(object? sender, EffectiveVolumeChangedEventArgs e)
    {
        foreach (var (sessionKey, volume) in e.Sessions)
            _sessionProvider.SetSessionVolume(sessionKey, volume);
    }

    public void Dispose() => _volumeStateManager.EffectiveVolumeChanged -= OnEffectiveVolumeChanged;
}
