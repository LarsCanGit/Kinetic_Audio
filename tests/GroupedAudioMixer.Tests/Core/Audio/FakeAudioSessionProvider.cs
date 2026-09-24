using KineticAudio.Core.Audio;

namespace KineticAudio.Tests.Core.Audio;

/// <summary>
/// In-memory <see cref="IAudioSessionProvider"/> for exercising logic-layer code without real
/// Core Audio, per the D5 testable-architecture seam.
/// </summary>
public sealed class FakeAudioSessionProvider : IAudioSessionProvider
{
    private readonly Dictionary<string, AudioSessionInfo> _sessions = new();
    private readonly Dictionary<string, float> _volumes = new();
    private readonly HashSet<string> _deadSessions = new();

    public event EventHandler<AudioSessionEventArgs>? SessionCreated;
    public event EventHandler<AudioSessionEventArgs>? SessionRemoved;

    public IReadOnlyList<AudioSessionInfo> GetActiveSessions() => _sessions.Values.ToList();

    public bool TryGetSessionVolume(string sessionKey, out float volume)
    {
        volume = 0f;
        if (!IsAvailable(sessionKey)) return false;
        volume = _volumes[sessionKey];
        return true;
    }

    public bool TrySetSessionVolume(string sessionKey, float volume)
    {
        if (!IsAvailable(sessionKey)) return false;
        _volumes[sessionKey] = volume;
        return true;
    }

    /// <summary>Test helper: reads a session's volume, throwing if it isn't available.</summary>
    public float GetSessionVolume(string sessionKey) =>
        TryGetSessionVolume(sessionKey, out var volume) ? volume : throw new KeyNotFoundException(sessionKey);

    /// <summary>Test helper: simulates an external volume change (e.g. the Windows mixer).</summary>
    public void SetSessionVolume(string sessionKey, float volume)
    {
        if (!TrySetSessionVolume(sessionKey, volume)) throw new KeyNotFoundException(sessionKey);
    }

    /// <summary>Test helper: simulates a new process opening an audio session.</summary>
    public void RaiseSessionCreated(AudioSessionInfo session, float initialVolume = 1.0f)
    {
        _sessions[session.SessionKey] = session;
        _volumes[session.SessionKey] = initialVolume;
        SessionCreated?.Invoke(this, new AudioSessionEventArgs(session));
    }

    /// <summary>Test helper: simulates a process exiting or closing its audio stream.</summary>
    public void RaiseSessionRemoved(string sessionKey)
    {
        if (!_sessions.Remove(sessionKey, out var session)) return;
        _volumes.Remove(sessionKey);
        _deadSessions.Remove(sessionKey);
        SessionRemoved?.Invoke(this, new AudioSessionEventArgs(session));
    }

    /// <summary>
    /// Test helper: simulates a session that is still listed but whose COM object no longer works
    /// (process exited or device disconnected, removal notification not yet delivered).
    /// </summary>
    public void KillSession(string sessionKey) => _deadSessions.Add(sessionKey);

    private bool IsAvailable(string sessionKey) =>
        _sessions.ContainsKey(sessionKey) && !_deadSessions.Contains(sessionKey);
}
