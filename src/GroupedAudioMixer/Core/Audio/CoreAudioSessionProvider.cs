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
/// Sessions belong to one endpoint, so the provider follows the default render (multimedia)
/// device: when it changes (hotkey switch or Windows' own sound settings) the old device's
/// sessions are reported removed and the new device's sessions are reported created, which lets
/// the linker/writer apply group x main volume to them straight away.
///
/// Not unit-tested; per the design doc's test-coverage model this class is covered by the D6
/// manual/E2E regression pass (device-switch hotkey, mute/unmute-restore), not by xUnit against a
/// fake, since it IS the real Core Audio implementation.
/// </summary>
public sealed class CoreAudioSessionProvider : IAudioSessionProvider, IDisposable
{
    private readonly object _lock = new();
    // Serializes binding to an endpoint so the removed/created events of two quick device
    // switches can't interleave. Never taken by event subscribers, so raising events under it is safe.
    private readonly object _bindLock = new();
    private readonly Dictionary<string, SessionEntry> _sessions = new();
    private readonly IMMDeviceEnumerator _deviceEnumerator;
    private readonly EndpointNotificationSink _endpointSink;
    private IAudioSessionManager2? _sessionManager;
    private SessionNotificationSink? _notificationSink;
    private string? _boundDeviceId;
    private bool _disposed;

    public event EventHandler<AudioSessionEventArgs>? SessionCreated;
    public event EventHandler<AudioSessionEventArgs>? SessionRemoved;

    public CoreAudioSessionProvider()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        // Startup: consumers read GetActiveSessions() after construction, so no events here.
        BindToDefaultDevice(raiseEvents: false);

        _endpointSink = new EndpointNotificationSink(this);
        ThrowIfFailed(_deviceEnumerator.RegisterEndpointNotificationCallback(_endpointSink));
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

    /// <summary>
    /// Binds to the current default render (multimedia) endpoint, releasing the previous one.
    /// No default device (everything unplugged or disabled) leaves the provider with no sessions
    /// until one appears.
    /// </summary>
    private void BindToDefaultDevice(bool raiseEvents)
    {
        lock (_bindLock)
        {
            if (_disposed)
                return;

            IMMDevice? device = null;
            string? deviceId = null;
            if (_deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDevice) >= 0)
            {
                device = defaultDevice;
                if (device.GetId(out var id) >= 0)
                    deviceId = id;
            }

            if (deviceId is not null && string.Equals(deviceId, _boundDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            var removed = Unbind();
            if (raiseEvents)
            {
                foreach (var entry in removed)
                    SessionRemoved?.Invoke(this, new AudioSessionEventArgs(entry.Info));
            }

            if (device is null || deviceId is null)
                return;

            var created = Bind(device);
            _boundDeviceId = deviceId;
            if (raiseEvents)
            {
                foreach (var info in created)
                    SessionCreated?.Invoke(this, new AudioSessionEventArgs(info));
            }
        }
    }

    /// <summary>
    /// Activates the session manager on <paramref name="device"/> and tracks its current sessions.
    /// Caller holds <see cref="_bindLock"/>.
    /// </summary>
    private List<AudioSessionInfo> Bind(IMMDevice device)
    {
        var created = new List<AudioSessionInfo>();

        var sessionManagerIid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref sessionManagerIid, 0, IntPtr.Zero, out var sessionManagerObj) < 0)
            return created;
        var sessionManager = (IAudioSessionManager2)sessionManagerObj;
        var notificationSink = new SessionNotificationSink(this);

        lock (_lock)
        {
            _sessionManager = sessionManager;
            _notificationSink = notificationSink;
        }

        // Registered before enumerating so a session created in between isn't missed;
        // TryTrackSession ignores the duplicate if it shows up in both.
        if (sessionManager.RegisterSessionNotification(notificationSink) < 0)
            return created;

        if (sessionManager.GetSessionEnumerator(out var enumerator) < 0)
            return created;
        if (enumerator.GetCount(out var count) >= 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (enumerator.GetSession(i, out var control) < 0)
                    continue;
                lock (_lock)
                {
                    var info = TryTrackSession(control);
                    if (info is not null)
                        created.Add(info);
                }
            }
        }
        Marshal.ReleaseComObject(enumerator);
        return created;
    }

    /// <summary>
    /// Releases the current endpoint's session manager and sessions, returning the sessions
    /// dropped. Caller holds <see cref="_bindLock"/>.
    /// </summary>
    private List<SessionEntry> Unbind()
    {
        IAudioSessionManager2? sessionManager;
        SessionNotificationSink? notificationSink;
        List<SessionEntry> removed;
        lock (_lock)
        {
            sessionManager = _sessionManager;
            notificationSink = _notificationSink;
            _sessionManager = null;
            _notificationSink = null;
            removed = _sessions.Values.ToList();
            _sessions.Clear();
        }
        _boundDeviceId = null;

        foreach (var entry in removed)
        {
            try
            {
                entry.Control.UnregisterAudioSessionNotification(entry.EventSink);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException)
            {
                // Session already gone; nothing to unregister from.
            }
        }

        if (sessionManager is not null)
        {
            try
            {
                if (notificationSink is not null)
                    sessionManager.UnregisterSessionNotification(notificationSink);
                Marshal.ReleaseComObject(sessionManager);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException)
            {
                // Device was removed; its session manager is already dead.
            }
        }
        return removed;
    }

    /// <summary>
    /// Called from <see cref="EndpointNotificationSink"/>. Core Audio forbids blocking or
    /// re-registering inside its callbacks, so the rebind runs on the thread pool.
    /// </summary>
    private void HandleDefaultDeviceChanged()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                BindToDefaultDevice(raiseEvents: true);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException)
            {
                Debug.WriteLine($"Rebinding to the new default device failed: {ex}");
            }
        });
    }

    /// <summary>Called from <see cref="SessionNotificationSink.OnSessionCreated"/> (D8).</summary>
    private void HandleSessionCreated(SessionNotificationSink sink, IAudioSessionControl newSession)
    {
        AudioSessionInfo? info = null;
        lock (_lock)
        {
            // A late callback from a device the provider has already switched away from.
            if (!ReferenceEquals(sink, _notificationSink))
                return;
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

        // Already tracked: seen by both the new-session callback and the bind-time enumeration.
        if (_sessions.ContainsKey(sessionKey))
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
        _deviceEnumerator.UnregisterEndpointNotificationCallback(_endpointSink);

        lock (_bindLock)
        {
            if (_disposed) return;
            _disposed = true;
            Unbind();
        }

        Marshal.ReleaseComObject(_deviceEnumerator);
    }

    private sealed record SessionEntry(
        AudioSessionInfo Info,
        IAudioSessionControl2 Control,
        ISimpleAudioVolume Volume,
        IAudioSessionEvents EventSink);

    /// <summary>
    /// Registered once per bound endpoint with
    /// <see cref="IAudioSessionManager2.RegisterSessionNotification"/> to receive new-session
    /// callbacks (D8). Delegates back to the owning provider.
    /// </summary>
    private sealed class SessionNotificationSink(CoreAudioSessionProvider owner) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            owner.HandleSessionCreated(this, newSession);
            return 0;
        }
    }

    /// <summary>
    /// Registered with <see cref="IMMDeviceEnumerator.RegisterEndpointNotificationCallback"/> to
    /// follow default render device changes. Only the multimedia role is acted on; Windows raises
    /// one callback per role for each switch.
    /// </summary>
    private sealed class EndpointNotificationSink(CoreAudioSessionProvider owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (flow == EDataFlow.eRender && role == ERole.eMultimedia)
                owner.HandleDefaultDeviceChanged();
            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
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
