using System.IO;
using System.Text.Json;

namespace GroupedAudioMixer.Core.Mapping;

/// <summary>Persists the executable-&gt;group map to a JSON file. Missing or malformed file yields
/// an empty map rather than crashing startup.</summary>
public sealed class JsonExecutableGroupMapStore : IExecutableGroupMapStore
{
    private readonly string _filePath;

    public JsonExecutableGroupMapStore(string filePath)
    {
        _filePath = filePath;
    }

    public JsonExecutableGroupMapStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GroupedAudioMixer", "executable-group-map.json"))
    {
    }

    public IReadOnlyDictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(_filePath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return raw is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(IReadOnlyDictionary<string, string> mappings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(mappings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
