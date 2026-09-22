namespace GroupedAudioMixer.Core.Mapping;

/// <summary>
/// Persistence seam for the executable-to-group map. Storage format/location (local file vs.
/// registry) is still an open decision (see CLAUDE.md "Open / not yet decided"), so
/// <see cref="ExecutableGroupMap"/> is written against this interface rather than a concrete
/// format, the same way group/volume logic is written against <c>IAudioSessionProvider</c>
/// rather than real Core Audio.
/// </summary>
public interface IExecutableGroupMapStore
{
    /// <summary>Loads the persisted executable path -> group id mappings, if any.</summary>
    IReadOnlyDictionary<string, string> Load();

    /// <summary>Persists the full set of executable path -> group id mappings.</summary>
    void Save(IReadOnlyDictionary<string, string> mappings);
}
