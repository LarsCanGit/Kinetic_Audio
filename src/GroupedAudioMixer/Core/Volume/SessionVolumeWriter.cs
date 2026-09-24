using KineticAudio.Core.Audio;

namespace KineticAudio.Core.Volume;

/// <summary>
/// The Phase 1 per-session writer (binding decision D4): subscribes to
/// <see cref="VolumeStateManager.EffectiveVolumeChanged"/> and pushes each affected session's
/// freshly computed volume to Core Audio via <see cref="IAudioSessionProvider.TrySetSessionVolume"/>.
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
        // A false result means the session is gone (app closed or device disconnected) but its
        // SessionRemoved notification hasn't reached VolumeStateManager yet. Skip it; removal
        // will follow.
        foreach (var (sessionKey, volume) in e.Sessions)
            _sessionProvider.TrySetSessionVolume(sessionKey, volume);
    }

    public void Dispose() => _volumeStateManager.EffectiveVolumeChanged -= OnEffectiveVolumeChanged;
}
