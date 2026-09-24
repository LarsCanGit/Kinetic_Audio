namespace KineticAudio.Core.Mapping;

/// <summary>
/// Non-persisted <see cref="IExecutableGroupMapStore"/>: mappings live only for the process
/// lifetime. Placeholder until config storage format/location (D2, still open per project
/// CLAUDE.md) is decided; swap for a file- or registry-backed store without touching
/// <see cref="ExecutableGroupMap"/> or its callers.
/// </summary>
public sealed class InMemoryExecutableGroupMapStore : IExecutableGroupMapStore
{
    private IReadOnlyDictionary<string, string> _mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Load() => _mappings;

    public void Save(IReadOnlyDictionary<string, string> mappings) => _mappings = mappings;
}
