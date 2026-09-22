using GroupedAudioMixer.Core.Audio;

namespace GroupedAudioMixer.Tests.Core.Audio;

/// <summary>
/// In-memory <see cref="IAudioSessionProvider"/> for exercising logic-layer code without real
/// Core Audio, per the D5 testable-architecture seam.
/// </summary>
public sealed class FakeAudioSessionProvider : IAudioSessionProvider
{
    private readonly Dictionary<string, AudioSessionInfo> _sessions = new();
    private readonly Dictionary<string, float> _volumes = new();

    public event EventHandler<AudioSessionEventArgs>? SessionCreated;
    public event EventHandler<AudioSessionEventArgs>? SessionRemoved;

    public IReadOnlyList<AudioSessionInfo> GetActiveSessions() => _sessions.Values.ToList();

    public float GetSessionVolume(string sessionKey) => _volumes[sessionKey];

    public void SetSessionVolume(string sessionKey, float volume) => _volumes[sessionKey] = volume;

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
        SessionRemoved?.Invoke(this, new AudioSessionEventArgs(session));
    }
}
