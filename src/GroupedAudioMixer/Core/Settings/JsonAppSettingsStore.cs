using System.IO;
using System.Text.Json;

namespace KineticAudio.Core.Settings;

/// <summary>Persists <see cref="AppSettings"/> to a JSON file. Missing file on first run yields
/// defaults; a malformed file is treated the same way rather than crashing startup.</summary>
public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _filePath;

    public JsonAppSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public JsonAppSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KineticAudio", "settings.json"))
    {
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
