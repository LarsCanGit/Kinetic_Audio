namespace GroupedAudioMixer.Core.Volume;

/// <summary>
/// Owns group volume, Main volume, and mute state, and computes effective per-session volume
/// as <c>group.Volume * main.Volume</c>. This is pure logic with no Core Audio dependency, so it
/// unit-tests in isolation.
///
/// Shared by design (binding decision D4): both the Phase 1 per-session writer and any future
/// Phase 3 mix engine consume this same module rather than each reimplementing volume math.
/// Consumers subscribe to <see cref="EffectiveVolumeChanged"/> to learn which sessions need a
/// fresh volume written after any mutation.
/// </summary>
public sealed class VolumeStateManager
{
    private const float ReconcileEpsilon = 0.005f;

    private readonly Dictionary<string, MixerGroup> _groups;
    private readonly Dictionary<string, string> _sessionToGroup = new();
    private string _defaultGroupId;

    public MainChannel Main { get; } = new();

    public event EventHandler<EffectiveVolumeChangedEventArgs>? EffectiveVolumeChanged;

    public VolumeStateManager(IEnumerable<MixerGroup> initialGroups, string defaultGroupId)
    {
        _groups = initialGroups.ToDictionary(g => g.Id);
        if (_groups.Count == 0)
            throw new ArgumentException("At least one group is required.", nameof(initialGroups));
        if (!_groups.ContainsKey(defaultGroupId))
            throw new ArgumentException($"Default group '{defaultGroupId}' is not among the initial groups.", nameof(defaultGroupId));

        _defaultGroupId = defaultGroupId;
    }

    public IReadOnlyCollection<MixerGroup> Groups => _groups.Values;

    public string DefaultGroupId => _defaultGroupId;

    public MixerGroup GetGroup(string groupId) => _groups[groupId];

    public void AddGroup(MixerGroup group) => _groups.Add(group.Id, group);

    /// <summary>
    /// Removes a group, immediately reassigning any live sessions linked to it to the default
    /// group (binding decision: group deletion reassigns live sessions immediately, not just
    /// future ones via the exe map). Returns the reassigned session keys so the writer layer
    /// can push fresh volumes for them.
    /// </summary>
    public IReadOnlyList<string> RemoveGroup(string groupId)
    {
        if (!_groups.ContainsKey(groupId))
            throw new KeyNotFoundException($"Group '{groupId}' does not exist.");
        if (_groups.Count == 1)
            throw new InvalidOperationException("Cannot remove the last remaining group.");

        _groups.Remove(groupId);

        if (_defaultGroupId == groupId)
            _defaultGroupId = _groups.Keys.First();

        var reassigned = new List<string>();
        foreach (var (sessionKey, sessionGroupId) in _sessionToGroup)
        {
            if (sessionGroupId != groupId) continue;
            _sessionToGroup[sessionKey] = _defaultGroupId;
            reassigned.Add(sessionKey);
        }

        RaiseEffectiveVolumeChanged(reassigned);
        return reassigned;
    }

    /// <summary>
    /// Links a session to a group (auto-link on session creation, or manual drag-to-group).
    /// Falls back to the default group if <paramref name="groupId"/> does not exist.
    /// </summary>
    public void AssignSessionToGroup(string sessionKey, string groupId)
    {
        var resolvedGroupId = _groups.ContainsKey(groupId) ? groupId : _defaultGroupId;
        _sessionToGroup[sessionKey] = resolvedGroupId;
        RaiseEffectiveVolumeChanged([sessionKey]);
    }

    public void RemoveSession(string sessionKey) => _sessionToGroup.Remove(sessionKey);

    public string GetGroupIdForSession(string sessionKey) =>
        _sessionToGroup.TryGetValue(sessionKey, out var groupId) ? groupId : _defaultGroupId;

    public void SetGroupVolume(string groupId, float volume)
    {
        var group = _groups[groupId];
        group.Volume = Math.Clamp(volume, 0f, 1f);
        RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
    }

    public void MuteGroup(string groupId)
    {
        _groups[groupId].IsMuted = true;
        RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
    }

    /// <summary>Restores the group's exact pre-mute volume (Volume was never touched by mute).</summary>
    public void UnmuteGroup(string groupId)
    {
        _groups[groupId].IsMuted = false;
        RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
    }

    public void SetMainVolume(float volume)
    {
        Main.Volume = Math.Clamp(volume, 0f, 1f);
        RaiseEffectiveVolumeChanged(_sessionToGroup.Keys.ToList());
    }

    public void MuteMain()
    {
        Main.IsMuted = true;
        RaiseEffectiveVolumeChanged(_sessionToGroup.Keys.ToList());
    }

    public void UnmuteMain()
    {
        Main.IsMuted = false;
        RaiseEffectiveVolumeChanged(_sessionToGroup.Keys.ToList());
    }

    /// <summary>Effective volume to write to Core Audio for this session: group.Volume * main.Volume.</summary>
    public float GetEffectiveVolume(string sessionKey)
    {
        var group = _groups[GetGroupIdForSession(sessionKey)];
        var groupFactor = group.IsMuted ? 0f : group.Volume;
        var mainFactor = Main.IsMuted ? 0f : Main.Volume;
        return groupFactor * mainFactor;
    }

    /// <summary>
    /// Reconciles a volume change made outside the app (e.g. via the Windows volume mixer)
    /// against the current computed value. A mismatch is treated as a manual override: the
    /// group's fader is back-calculated from the actual volume and Main, and every other session
    /// sharing that group is notified so its own written volume stays in sync (binding decision:
    /// external drift gets reconciled, not ignored or silently overwritten).
    /// </summary>
    /// <returns>true if the group's stored volume was updated.</returns>
    public bool ReconcileExternalVolume(string sessionKey, float actualVolume)
    {
        var expected = GetEffectiveVolume(sessionKey);
        if (MathF.Abs(actualVolume - expected) <= ReconcileEpsilon)
            return false;

        var groupId = GetGroupIdForSession(sessionKey);
        var group = _groups[groupId];
        var mainFactor = Main.IsMuted ? 0f : Main.Volume;

        // Can't reliably back-calculate a group fader from a volume that's forced to zero
        // by mute state on either channel.
        if (mainFactor <= ReconcileEpsilon || group.IsMuted)
            return false;

        group.Volume = Math.Clamp(actualVolume / mainFactor, 0f, 1f);
        RaiseEffectiveVolumeChanged(GetSessionsInGroup(groupId));
        return true;
    }

    private List<string> GetSessionsInGroup(string groupId) =>
        _sessionToGroup.Where(kv => kv.Value == groupId).Select(kv => kv.Key).ToList();

    private void RaiseEffectiveVolumeChanged(IReadOnlyList<string> sessionKeys)
    {
        if (sessionKeys.Count == 0) return;

        var payload = sessionKeys.Select(key => (key, GetEffectiveVolume(key))).ToList();
        EffectiveVolumeChanged?.Invoke(this, new EffectiveVolumeChangedEventArgs(payload));
    }
}
