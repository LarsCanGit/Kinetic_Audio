using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using KineticAudio.Core.Audio.Interop;

namespace KineticAudio.Core.Audio;

/// <summary>
/// The real <see cref="IAudioSessionProvider"/> implementation: a thin COM-interop adapter over
/// Windows Core Audio (<c>IAudioSessionManager2</c> / <c>IAudioSessionControl2</c> /
/// <c>ISimpleAudioVolume</c> / <c>IAudioSessionNotification</c>), per binding decision D5. It has
/// no group assignment, volume math, or mute/unmute-restore logic of its own; that all lives above
/// this seam and unit-tests against <c>FakeAudioSessionProvider</c> instead.
///
/// Session creation is event-driven via <c>IAudioSessionNotification</c> (D8); session removal is
/// detected via each session's own <c>IAudioSessionEvents.OnStateChanged</c> reaching
/// <c>AudioSessionStateExpired</c>, since <c>IAudioSessionNotification</c> itself has no
/// "session removed" callback.
///
/// Not unit-tested; per the design doc's test-coverage model this class is covered by the D6
/// manual/E2E regression pass (device-switch hotkey, mute/unmute-restore), not by xUnit against a
/// fake, since it IS the real Core Audio implementation.
/// </summary>
public sealed class CoreAudioSessionProvider : IAudioSessionProvider, IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly IMMDeviceEnumerator _deviceEnumerator;
    private readonly IAudioSessionManager2 _sessionManager;
    private readonly SessionNotificationSink _notificationSink;
    private bool _disposed;

    public event EventHandler<AudioSessionEventArgs>? SessionCreated;
    public event EventHandler<AudioSessionEventArgs>? SessionRemoved;

    public CoreAudioSessionProvider()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        ThrowIfFailed(_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device));

        var sessionManagerIid = typeof(IAudioSessionManager2).GUID;
        ThrowIfFailed(device.Activate(ref sessionManagerIid, 0, IntPtr.Zero, out var sessionManagerObj));
        _sessionManager = (IAudioSessionManager2)sessionManagerObj;

        _notificationSink = new SessionNotificationSink(this);
        ThrowIfFailed(_sessionManager.RegisterSessionNotification(_notificationSink));

        ThrowIfFailed(_sessionManager.GetSessionEnumerator(out var enumerator));
        ThrowIfFailed(enumerator.GetCount(out var count));
        for (var i = 0; i < count; i++)
        {
            ThrowIfFailed(enumerator.GetSession(i, out var control));
            TryTrackSession(control);
        }
        Marshal.ReleaseComObject(enumerator);
    }

    public IReadOnlyList<AudioSessionInfo> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.Select(entry => entry.Info).ToList();
        }
    }

    public bool TryGetSessionVolume(string sessionKey, out float volume)
    {
        volume = 0f;
        if (!TryGetEntry(sessionKey, out var entry))
            return false;

        try
        {
            return entry.Volume.GetMasterVolume(out volume) >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            // Session's process exited or its device was disconnected before the removal
            // notification arrived.
            return false;
        }
    }

    public bool TrySetSessionVolume(string sessionKey, float volume)
    {
        if (!TryGetEntry(sessionKey, out var entry))
            return false;

        try
        {
            var eventContext = Guid.Empty;
            return entry.Volume.SetMasterVolume(volume, ref eventContext) >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            return false;
        }
    }

    private bool TryGetEntry(string sessionKey, out SessionEntry entry)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionKey, out entry!);
        }
    }

    /// <summary>Called from <see cref="SessionNotificationSink.OnSessionCreated"/> (D8).</summary>
    private void HandleSessionCreated(IAudioSessionControl newSession)
    {
        AudioSessionInfo? info = null;
        lock (_lock)
        {
            info = TryTrackSession(newSession);
        }
        if (info is not null)
            SessionCreated?.Invoke(this, new AudioSessionEventArgs(info));
    }

    /// <summary>
    /// Attempts to add <paramref name="control"/> to <see cref="_sessions"/>. Must be called with
    /// <see cref="_lock"/> held. Skips the system sounds session, which has no owning process, and
    /// any session that dies while it is being read (a failed HRESULT here means the process
    /// exited or the device went away, which is expected rather than fatal).
    /// </summary>
    private AudioSessionInfo? TryTrackSession(IAudioSessionControl control)
    {
        if (control is not IAudioSessionControl2 control2)
        {
            control2 = (IAudioSessionControl2)control;
        }

        if (control2.IsSystemSoundsSession() == 0)
            return null;

        if (control2.GetProcessId(out var processId) < 0 || processId == 0)
            return null;

        string executablePath;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            executablePath = process.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process already exited between session creation and enumeration.
            return null;
        }

        if (control2.GetSessionInstanceIdentifier(out var sessionKey) < 0 || string.IsNullOrEmpty(sessionKey))
            return null;

        if (control2.GetDisplayName(out var displayName) < 0 || string.IsNullOrWhiteSpace(displayName))
            displayName = Path.GetFileNameWithoutExtension(executablePath);

        var volume = (ISimpleAudioVolume)control2;
        var eventSink = new SessionEventSink(this, sessionKey);
        // Without the event sink the session's removal would never be seen, so don't track it.
        if (control2.RegisterAudioSessionNotification(eventSink) < 0)
            return null;

        var info = new AudioSessionInfo(sessionKey, executablePath, displayName);
        _sessions[sessionKey] = new SessionEntry(info, control2, volume, eventSink);
        return info;
    }

    /// <summary>Called from a <see cref="SessionEventSink"/> when its session state expires.</summary>
    private void HandleSessionExpired(string sessionKey)
    {
        SessionEntry? removed = null;
        lock (_lock)
        {
            if (_sessions.Remove(sessionKey, out var entry))
                removed = entry;
        }
        if (removed is not null)
        {
            removed.Control.UnregisterAudioSessionNotification(removed.EventSink);
            SessionRemoved?.Invoke(this, new AudioSessionEventArgs(removed.Info));
        }
    }

    private static int ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
        return hresult;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionManager.UnregisterSessionNotification(_notificationSink);

        lock (_lock)
        {
            foreach (var entry in _sessions.Values)
                entry.Control.UnregisterAudioSessionNotification(entry.EventSink);
            _sessions.Clear();
        }

        Marshal.ReleaseComObject(_sessionManager);
        Marshal.ReleaseComObject(_deviceEnumerator);
    }

    private sealed record SessionEntry(
        AudioSessionInfo Info,
        IAudioSessionControl2 Control,
        ISimpleAudioVolume Volume,
        IAudioSessionEvents EventSink);

    /// <summary>
    /// Registered once with <see cref="IAudioSessionManager2.RegisterSessionNotification"/> to
    /// receive new-session callbacks (D8). Delegates back to the owning provider.
    /// </summary>
    private sealed class SessionNotificationSink(CoreAudioSessionProvider owner) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            owner.HandleSessionCreated(newSession);
            return 0;
        }
    }

    /// <summary>
    /// Registered per-session to detect removal: <c>IAudioSessionNotification</c> has no
    /// "session removed" callback, so removal is inferred from a session's own state reaching
    /// <see cref="AudioSessionState.AudioSessionStateExpired"/>.
    /// </summary>
    private sealed class SessionEventSink(CoreAudioSessionProvider owner, string sessionKey) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string displayName, ref Guid eventContext) => 0;

        public int OnIconPathChanged(string iconPath, ref Guid eventContext) => 0;

        public int OnSimpleVolumeChanged(float volume, bool mute, ref Guid eventContext) => 0;

        public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, ref Guid eventContext) => 0;

        public int OnGroupingParamChanged(ref Guid groupingParam, ref Guid eventContext) => 0;

        public int OnStateChanged(AudioSessionState state)
        {
            if (state == AudioSessionState.AudioSessionStateExpired)
                owner.HandleSessionExpired(sessionKey);
            return 0;
        }

        public int OnSessionDisconnected(int disconnectReason)
        {
            owner.HandleSessionExpired(sessionKey);
            return 0;
        }
    }
}
