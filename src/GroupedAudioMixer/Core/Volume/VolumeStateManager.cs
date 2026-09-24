namespace KineticAudio.Core.Volume;

/// <summary>
/// Owns group volume, Main volume, and mute state, and computes effective per-session volume
/// as <c>group.Volume * main.Volume</c>, except for main-independent groups (the Mic strip), which
/// Main Out does not scale or mute. This is pure logic with no Core Audio dependency, so it
/// unit-tests in isolation.
///
/// Shared by design (binding decision D4): both the Phase 1 per-session writer and any future
/// Phase 3 mix engine consume this same module rather than each reimplementing volume math.
/// Consumers subscribe to <see cref="EffectiveVolumeChanged"/> to learn which sessions need a
/// fresh volume written after any mutation.
///
/// Thread-safe: Core Audio raises session create/remove on background threads while the UI
/// thread moves faders, so every member takes <see cref="_lock"/>. <see cref="EffectiveVolumeChanged"/>
/// is raised while the lock is held, so subscribers see changes in the order they were made and
/// never write a stale volume over a newer one. Handlers must therefore not block on another
/// thread that could be waiting for this lock (use <c>Dispatcher.BeginInvoke</c>, not <c>Invoke</c>).
/// </summary>
public sealed class VolumeStateManager
{
    private const float ReconcileEpsilon = 0.005f;

    private readonly object _lock = new();
    private readonly Dictionary<string, MixerGroup> _groups;
    private readonly Dictionary<string, string> _sessionToGroup = new();
    private readonly string _defaultGroupId;
    private readonly HashSet<string> _mainIndependentGroupIds;
    private long _version;

    public MainChannel Main { get; } = new();

    public event EventHandler<EffectiveVolumeChangedEventArgs>? EffectiveVolumeChanged;

    /// <param name="mainIndependentGroupIds">Groups Main volume and mute don't apply to. Main Out is
    /// the playback master, so the Mic strip (capture sessions) is listed here and stays live when
    /// Main is muted.</param>
    public VolumeStateManager(
        IEnumerable<MixerGroup> initialGroups,
        string defaultGroupId,
        IEnumerable<string>? mainIndependentGroupIds = null)
    {
        _groups = initialGroups.ToDictionary(g => g.Id);
        if (_groups.Count == 0)
            throw new ArgumentException("At least one group is required.", nameof(initialGroups));
        if (!_groups.ContainsKey(defaultGroupId))
            throw new ArgumentException($"Default group '{defaultGroupId}' is not among the initial groups.", nameof(defaultGroupId));

        _defaultGroupId = defaultGroupId;
        _mainIndependentGroupIds = new HashSet<string>(mainIndependentGroupIds ?? []);
    }

    /// <summary>A snapshot of the groups, safe to enumerate while other threads mutate state.</summary>
    public IReadOnlyCollection<MixerGroup> Groups
    {
        get { lock (_lock) return _groups.Values.ToList(); }
    }

    /// <summary>
    /// Increments on every change that affects effective volumes. A caller that reads a session's
    /// actual volume outside the lock captures this first and passes it to
    /// <see cref="ReconcileExternalVolume"/>, so a reading taken before a fader move isn't mistaken
    /// for drift afterwards.
    /// </summary>
    public long Version
    {
        get { lock (_lock) return _version; }
    }

    public string DefaultGroupId
    {
        get { lock (_lock) return _defaultGroupId; }
    }

    public bool HasGroup(string groupId)
    {
        lock (_lock) return _groups.ContainsKey(groupId);
    }

    public MixerGroup GetGroup(string groupId)
    {
        lock (_lock) return _groups[groupId];
    }

    /// <summary>
    /// Links a session to a group (auto-link on session creation, or manual drag-to-group).
    /// Falls back to the default group if <paramref name="groupId"/> does not exist.
    /// </summary>
    public void AssignSessionToGroup(string sessionKey, string groupId)
    {
        lock (_lock)
        {
            var resolvedGroupId = _groups.ContainsKey(groupId) ? groupId : _defaultGroupId;
            _sessionToGroup[sessionKey] = resolvedGroupId;
            RaiseEffectiveVolumeChanged([sessionKey]);
        }
    }

    public void RemoveSession(string sessionKey)
    {
        lock (_lock) _sessionToGroup.Remove(sessionKey);
    }

    public bool IsSessionLinked(string sessionKey)
    {
        lock (_lock) return _sessionToGroup.ContainsKey(sessionKey);
    }

    public string GetGroupIdForSession(string sessionKey)
    {
        lock (_lock) return _sessionToGroup.TryGetValue(sessionKey, out var groupId) ? groupId : _defaultGroupId;
    }

    public void SetGroupVolume(string groupId, float volume)
    {
        lock (_lock)
        {
            var group = _groups[groupId];
            group.Volume = Math.Clamp(volume, 0f, 1f);
            RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
        }
    }

    public void MuteGroup(string groupId)
    {
        lock (_lock)
        {
            _groups[groupId].IsMuted = true;
            RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
        }
    }

    /// <summary>Restores the group's exact pre-mute volume (Volume was never touched by mute).</summary>
    public void UnmuteGroup(string groupId)
    {
        lock (_lock)
        {
            _groups[groupId].IsMuted = false;
            RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
        }
    }

    public void SetMainVolume(float volume)
    {
        lock (_lock)
        {
            Main.Volume = Math.Clamp(volume, 0f, 1f);
            RaiseEffectiveVolumeChanged(GetSessionsFollowingMain());
        }
    }

    public void MuteMain()
    {
        lock (_lock)
        {
            Main.IsMuted = true;
            RaiseEffectiveVolumeChanged(GetSessionsFollowingMain());
        }
    }

    public void UnmuteMain()
    {
        lock (_lock)
        {
            Main.IsMuted = false;
            RaiseEffectiveVolumeChanged(GetSessionsFollowingMain());
        }
    }

    /// <summary>
    /// Flips Main mute (the Main mute hotkey). Unmuting restores the exact prior level, as with
    /// <see cref="UnmuteMain"/>. Returns the new muted state.
    /// </summary>
    public bool ToggleMainMute()
    {
        lock (_lock)
        {
            if (Main.IsMuted) UnmuteMain(); else MuteMain();
            return Main.IsMuted;
        }
    }

    /// <summary>
    /// Effective volume to write to Core Audio for this session: group.Volume * main.Volume, or
    /// just group.Volume for a main-independent group.
    /// </summary>
    public float GetEffectiveVolume(string sessionKey)
    {
        lock (_lock)
        {
            var groupId = GetGroupIdForSession(sessionKey);
            var group = _groups[groupId];
            var groupFactor = group.IsMuted ? 0f : group.Volume;
            return groupFactor * GetMainFactor(groupId);
        }
    }

    /// <summary>
    /// Reconciles a volume change made outside the app (e.g. via the Windows volume mixer)
    /// against the current computed value. A mismatch is treated as a manual override: the
    /// group's fader is back-calculated from the actual volume and Main, and every other session
    /// sharing that group is notified so its own written volume stays in sync (binding decision:
    /// external drift gets reconciled, not ignored or silently overwritten).
    /// </summary>
    /// <param name="observedAtVersion">The <see cref="Version"/> captured before
    /// <paramref name="actualVolume"/> was read. If state has changed since, the reading is stale
    /// and is ignored; the next poll will see the freshly written volume.</param>
    /// <returns>true if the group's stored volume was updated.</returns>
    public bool ReconcileExternalVolume(string sessionKey, float actualVolume, long? observedAtVersion = null)
    {
        lock (_lock)
        {
            if (observedAtVersion is { } observed && observed != _version)
                return false;

            // A session the provider lists but that isn't linked yet still carries Windows'
            // persisted volume, not ours; treating it as drift would move the default group.
            if (!_sessionToGroup.ContainsKey(sessionKey))
                return false;

            var expected = GetEffectiveVolume(sessionKey);
            if (MathF.Abs(actualVolume - expected) <= ReconcileEpsilon)
                return false;

            var groupId = GetGroupIdForSession(sessionKey);
            var group = _groups[groupId];
            var mainFactor = GetMainFactor(groupId);

            // Can't reliably back-calculate a group fader from a volume that's forced to zero
            // by mute state on either channel.
            if (mainFactor <= ReconcileEpsilon || group.IsMuted)
                return false;

            group.Volume = Math.Clamp(actualVolume / mainFactor, 0f, 1f);
            RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
            return true;
        }
    }

    private float GetMainFactor(string groupId) =>
        _mainIndependentGroupIds.Contains(groupId) ? 1f : Main.IsMuted ? 0f : Main.Volume;

    private List<string> GetSessionsFollowingMain() =>
        _sessionToGroup.Where(kv => !_mainIndependentGroupIds.Contains(kv.Value)).Select(kv => kv.Key).ToList();

    private List<string> GetSessionsInGroup(string groupId) =>
        _sessionToGroup.Where(kv => kv.Value == groupId).Select(kv => kv.Key).ToList();

    private void RaiseEffectiveVolumeChanged(IReadOnlyList<string> sessionKeys)
    {
        _version++;
        if (sessionKeys.Count == 0) return;

        var payload = sessionKeys.Select(key => (key, GetEffectiveVolume(key))).ToList();
        EffectiveVolumeChanged?.Invoke(this, new EffectiveVolumeChangedEventArgs(payload));
    }
}
