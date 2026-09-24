using KineticAudio.Core.Mapping;

namespace KineticAudio.Tests.Core.Mapping;

/// <summary>In-memory <see cref="IExecutableGroupMapStore"/>, standing in for whatever real
/// storage format/location gets decided (see CLAUDE.md "Open / not yet decided").</summary>
public sealed class FakeExecutableGroupMapStore : IExecutableGroupMapStore
{
    private readonly object _lock = new();
    private Dictionary<string, string> _saved = new(StringComparer.OrdinalIgnoreCase);

    public int SaveCallCount { get; private set; }

    public IReadOnlyDictionary<string, string> Load()
    {
        lock (_lock)
            return new Dictionary<string, string>(_saved, StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IReadOnlyDictionary<string, string> mappings)
    {
        lock (_lock)
        {
            _saved = new Dictionary<string, string>(mappings, StringComparer.OrdinalIgnoreCase);
            SaveCallCount++;
        }
    }
}
