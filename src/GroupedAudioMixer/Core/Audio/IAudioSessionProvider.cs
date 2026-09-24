namespace KineticAudio.Core.Audio;

/// <summary>
/// Seam over Windows Core Audio session APIs (<c>IAudioSessionManager2</c> /
/// <c>IAudioSessionControl2</c> / <c>ISimpleAudioVolume</c> / <c>IAudioSessionNotification</c>).
///
/// The real implementation is a thin COM-interop adapter with no logic of its own. All group
/// assignment, volume math, and mute/unmute-restore logic lives above this seam and unit-tests
/// against a fake implementation — never against real Core Audio.
/// </summary>
public interface IAudioSessionProvider
{
    /// <summary>All currently active audio sessions on the render (output) device.</summary>
    IReadOnlyList<AudioSessionInfo> GetActiveSessions();

    /// <summary>
    /// Raised when a new audio session is created (backed by <c>IAudioSessionNotification</c>).
    /// Event-driven per binding decision D8 — never polled for the session-creation hot path.
    /// </summary>
    event EventHandler<AudioSessionEventArgs> SessionCreated;

    /// <summary>Raised when an existing audio session is removed (process exited or stream closed).</summary>
    event EventHandler<AudioSessionEventArgs> SessionRemoved;

    /// <summary>
    /// Reads the current per-session output volume (0.0-1.0) via <c>ISimpleAudioVolume</c>.
    /// Returns false, without throwing, if the session is no longer tracked or its COM object has
    /// stopped working (process exited or device disconnected). Callers can race session removal,
    /// so a gone session is an expected outcome, not an error.
    /// </summary>
    bool TryGetSessionVolume(string sessionKey, out float volume);

    /// <summary>
    /// Writes the per-session output volume (0.0-1.0) via <c>ISimpleAudioVolume</c>. Returns false,
    /// without throwing, if the session is gone; see <see cref="TryGetSessionVolume"/>.
    /// </summary>
    bool TrySetSessionVolume(string sessionKey, float volume);
}
