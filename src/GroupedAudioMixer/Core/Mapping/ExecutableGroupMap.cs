namespace KineticAudio.Core.Mapping;

/// <summary>
/// Durable executable path -> group id map used to auto-link a newly created audio session to
/// its group (Windows audio sessions are ephemeral/process-tied, so the session key can't be
/// the persistence key; the owning executable's path is).
///
/// All writes go through <see cref="SetMapping"/>/<see cref="RemoveMapping"/>, which serialize
/// on a single lock (binding decision D9). Auto-link (on session creation, from the writer
/// layer) and manual drag-to-group (from the UI) both funnel through this same path, so a burst
/// of sessions created concurrently by one app launching several helper processes can't race
/// and corrupt the map.
///
/// A lookup miss is not resolved here: the caller (see <c>VolumeStateManager.AssignSessionToGroup</c>)
/// falls back to the default group, and a mapping that points at a since-deleted group falls
/// back the same way, so this class never needs to know which groups currently exist.
/// </summary>
public sealed class ExecutableGroupMap
{
    private readonly IExecutableGroupMapStore _store;
    private readonly object _writeLock = new();
    private readonly Dictionary<string, string> _mappings = new(StringComparer.OrdinalIgnoreCase);

    public ExecutableGroupMap(IExecutableGroupMapStore store)
    {
        _store = store;
        foreach (var (executablePath, groupId) in store.Load())
            _mappings[executablePath] = groupId;
    }

    /// <summary>Returns the mapped group id for this executable, or null if unmapped.</summary>
    public string? TryGetGroupId(string executablePath)
    {
        lock (_writeLock)
            return _mappings.TryGetValue(executablePath, out var groupId) ? groupId : null;
    }

    /// <summary>
    /// Maps (or remaps) an executable to a group and persists immediately. Serialized: concurrent
    /// callers block on this method rather than racing on the underlying dictionary or store.
    /// </summary>
    public void SetMapping(string executablePath, string groupId)
    {
        lock (_writeLock)
        {
            _mappings[executablePath] = groupId;
            Persist();
        }
    }

    /// <summary>Removes an executable's mapping (it falls back to the default group thereafter).</summary>
    public void RemoveMapping(string executablePath)
    {
        lock (_writeLock)
        {
            _mappings.Remove(executablePath);
            Persist();
        }
    }

    /// <summary>Snapshot of all current mappings, e.g. for a settings-screen listing.</summary>
    public IReadOnlyDictionary<string, string> GetAllMappings()
    {
        lock (_writeLock)
            return new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase);
    }

    // Must be called while holding _writeLock.
    private void Persist() =>
        _store.Save(new Dictionary<string, string>(_mappings, StringComparer.OrdinalIgnoreCase));
}
