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
/// Sessions belong to one endpoint, and apps aren't all on the default device (a call app on the
/// communications device, a game pinned to a headset in Windows' per-app routing), so the provider
/// binds a session manager on every active render endpoint, and on every active capture endpoint
/// so recording apps can be driven by the Mic strip. When an endpoint is added, removed,
/// enabled or disabled, the endpoint set is re-synced: a lost device's sessions are reported
/// removed and a new device's sessions are reported created, which lets the linker/writer apply
/// group x main volume to them straight away. Session keys come from
/// <c>GetSessionInstanceIdentifier</c>, which embeds the device id, so the same app on two devices
/// is two distinct sessions.
///
/// Not unit-tested; per the design doc's test-coverage model this class is covered by the D6
/// manual/E2E regression pass (device-switch hotkey, mute/unmute-restore), not by xUnit against a
/// fake, since it IS the real Core Audio implementation.
/// </summary>
public sealed class CoreAudioSessionProvider : IAudioSessionProvider, IDisposable
{
    private readonly object _lock = new();
    // Serializes endpoint syncs so the removed/created events of two quick device changes can't
    // interleave. Never taken by event subscribers, so raising events under it is safe.
    private readonly object _bindLock = new();
    private readonly Dictionary<string, SessionEntry> _sessions = new();
    // Keyed by device id. Read under _lock (session callbacks check it); only changed under _bindLock.
    private readonly Dictionary<string, EndpointBinding> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly IMMDeviceEnumerator _deviceEnumerator;
    private readonly EndpointNotificationSink _endpointSink;
    private bool _disposed;

    public event EventHandler<AudioSessionEventArgs>? SessionCreated;
    public event EventHandler<AudioSessionEventArgs>? SessionRemoved;

    public CoreAudioSessionProvider()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        // Startup: consumers read GetActiveSessions() after construction, so no events here.
        SyncEndpoints(raiseEvents: false);

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
    /// Binds every active render and capture endpoint not yet bound and unbinds any bound endpoint that is no
    /// longer active. Idempotent, so redundant device notifications are harmless. No active
    /// endpoints (everything unplugged or disabled) leaves the provider with no sessions until
    /// one appears.
    /// </summary>
    private void SyncEndpoints(bool raiseEvents)
    {
        lock (_bindLock)
        {
            if (_disposed)
                return;

            var active = EnumerateActiveDevices();
            try
            {
                List<string> stale;
                lock (_lock)
                {
                    stale = _endpoints.Keys.Where(id => !active.ContainsKey(id)).ToList();
                }

                foreach (var deviceId in stale)
                {
                    var removed = Unbind(deviceId);
                    if (raiseEvents)
                    {
                        foreach (var entry in removed)
                            SessionRemoved?.Invoke(this, new AudioSessionEventArgs(entry.Info));
                    }
                }

                foreach (var (deviceId, (device, flow)) in active)
                {
                    bool alreadyBound;
                    lock (_lock)
                    {
                        alreadyBound = _endpoints.ContainsKey(deviceId);
                    }
                    if (alreadyBound)
                        continue;

                    var created = Bind(deviceId, device, flow);
                    if (raiseEvents)
                    {
                        foreach (var info in created)
                            SessionCreated?.Invoke(this, new AudioSessionEventArgs(info));
                    }
                }
            }
            finally
            {
                // The session manager Bind activated holds its own reference, so the device
                // objects themselves can go.
                foreach (var (device, _) in active.Values)
                    Marshal.ReleaseComObject(device);
            }
        }
    }

    private Dictionary<string, (IMMDevice Device, AudioSessionFlow Flow)> EnumerateActiveDevices()
    {
        var devices = new Dictionary<string, (IMMDevice, AudioSessionFlow)>(StringComparer.OrdinalIgnoreCase);
        AddActiveDevices(EDataFlow.eRender, AudioSessionFlow.Render, devices);
        AddActiveDevices(EDataFlow.eCapture, AudioSessionFlow.Capture, devices);
        return devices;
    }

    private void AddActiveDevices(
        EDataFlow dataFlow,
        AudioSessionFlow flow,
        Dictionary<string, (IMMDevice, AudioSessionFlow)> devices)
    {
        const uint DEVICE_STATE_ACTIVE = 0x1;

        if (_deviceEnumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var collectionPtr) < 0)
            return;
        var collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collectionPtr);
        Marshal.Release(collectionPtr);

        try
        {
            if (collection.GetCount(out var count) < 0)
                return;
            for (var i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0)
                    continue;
                if (device.GetId(out var id) >= 0 && !string.IsNullOrEmpty(id) && !devices.ContainsKey(id))
                    devices[id] = (device, flow);
                else
                    Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    /// <summary>
    /// Activates the session manager on <paramref name="device"/> and tracks its current sessions.
    /// Caller holds <see cref="_bindLock"/>.
    /// </summary>
    private List<AudioSessionInfo> Bind(string deviceId, IMMDevice device, AudioSessionFlow flow)
    {
        var created = new List<AudioSessionInfo>();

        var sessionManagerIid = typeof(IAudioSessionManager2).GUID;
        if (device.Activate(ref sessionManagerIid, 0, IntPtr.Zero, out var sessionManagerObj) < 0)
            return created;
        var sessionManager = (IAudioSessionManager2)sessionManagerObj;
        var notificationSink = new SessionNotificationSink(this, deviceId);

        lock (_lock)
        {
            _endpoints[deviceId] = new EndpointBinding(sessionManager, notificationSink, flow);
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
                    var info = TryTrackSession(deviceId, control);
                    if (info is not null)
                        created.Add(info);
                }
            }
        }
        Marshal.ReleaseComObject(enumerator);
        return created;
    }

    /// <summary>
    /// Releases one endpoint's session manager and sessions, returning the sessions dropped.
    /// Caller holds <see cref="_bindLock"/>.
    /// </summary>
    private List<SessionEntry> Unbind(string deviceId)
    {
        EndpointBinding? binding;
        List<SessionEntry> removed;
        lock (_lock)
        {
            _endpoints.Remove(deviceId, out binding);
            removed = _sessions.Values
                .Where(entry => string.Equals(entry.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in removed)
                _sessions.Remove(entry.Info.SessionKey);
        }

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

        if (binding is not null)
        {
            try
            {
                binding.SessionManager.UnregisterSessionNotification(binding.NotificationSink);
                Marshal.ReleaseComObject(binding.SessionManager);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException)
            {
                // Device was removed; its session manager is already dead.
            }
        }
        return removed;
    }

    /// <summary>
    /// Called from <see cref="EndpointNotificationSink"/> on any endpoint add/remove/state change.
    /// Core Audio forbids blocking or re-registering inside its callbacks, so the sync runs on the
    /// thread pool.
    /// </summary>
    private void HandleEndpointsChanged()
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                SyncEndpoints(raiseEvents: true);
            }
            catch (Exception ex) when (ex is COMException or InvalidComObjectException or InvalidCastException)
            {
                Debug.WriteLine($"Re-syncing audio endpoints failed: {ex}");
            }
        });
    }

    /// <summary>Called from <see cref="SessionNotificationSink.OnSessionCreated"/> (D8).</summary>
    private void HandleSessionCreated(SessionNotificationSink sink, IAudioSessionControl newSession)
    {
        AudioSessionInfo? info = null;
        lock (_lock)
        {
            // A late callback from an endpoint the provider has already unbound.
            if (!_endpoints.TryGetValue(sink.DeviceId, out var binding) || !ReferenceEquals(sink, binding.NotificationSink))
                return;
            info = TryTrackSession(sink.DeviceId, newSession);
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
    private AudioSessionInfo? TryTrackSession(string deviceId, IAudioSessionControl control)
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

        var flow = _endpoints.TryGetValue(deviceId, out var binding) ? binding.Flow : AudioSessionFlow.Render;
        var info = new AudioSessionInfo(sessionKey, executablePath, displayName, flow);
        _sessions[sessionKey] = new SessionEntry(deviceId, info, control2, volume, eventSink);
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
            List<string> deviceIds;
            lock (_lock)
            {
                deviceIds = _endpoints.Keys.ToList();
            }
            foreach (var deviceId in deviceIds)
                Unbind(deviceId);
        }

        Marshal.ReleaseComObject(_deviceEnumerator);
    }

    private sealed record SessionEntry(
        string DeviceId,
        AudioSessionInfo Info,
        IAudioSessionControl2 Control,
        ISimpleAudioVolume Volume,
        IAudioSessionEvents EventSink);

    private sealed record EndpointBinding(
        IAudioSessionManager2 SessionManager,
        SessionNotificationSink NotificationSink,
        AudioSessionFlow Flow);

    /// <summary>
    /// Registered once per bound endpoint with
    /// <see cref="IAudioSessionManager2.RegisterSessionNotification"/> to receive new-session
    /// callbacks (D8). Delegates back to the owning provider.
    /// </summary>
    private sealed class SessionNotificationSink(CoreAudioSessionProvider owner, string deviceId) : IAudioSessionNotification
    {
        public string DeviceId { get; } = deviceId;

        public int OnSessionCreated(IAudioSessionControl newSession)
        {
            owner.HandleSessionCreated(this, newSession);
            return 0;
        }
    }

    /// <summary>
    /// Registered with <see cref="IMMDeviceEnumerator.RegisterEndpointNotificationCallback"/> to
    /// keep the bound endpoint set in step with the active render and capture devices. A
    /// default-device change needs nothing: every active endpoint is already bound.
    /// </summary>
    private sealed class EndpointNotificationSink(CoreAudioSessionProvider owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState)
        {
            owner.HandleEndpointsChanged();
            return 0;
        }

        public int OnDeviceAdded(string deviceId)
        {
            owner.HandleEndpointsChanged();
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            owner.HandleEndpointsChanged();
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => 0;

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
