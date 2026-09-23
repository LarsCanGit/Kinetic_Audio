using GroupedAudioMixer.Core.Mapping;
using Xunit;

namespace GroupedAudioMixer.Tests.Core.Mapping;

public class JsonExecutableGroupMapStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"gam-map-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyMap()
    {
        var store = new JsonExecutableGroupMapStore(_filePath);

        var mappings = store.Load();

        Assert.Empty(mappings);
    }

    [Fact]
    public void Load_MalformedFile_ReturnsEmptyMapRatherThanThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, "{ not valid json");
        var store = new JsonExecutableGroupMapStore(_filePath);

        var mappings = store.Load();

        Assert.Empty(mappings);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsMappings()
    {
        var store = new JsonExecutableGroupMapStore(_filePath);
        var mappings = new Dictionary<string, string>
        {
            ["C:\\Games\\game.exe"] = "group1",
            ["C:\\Chat\\voice.exe"] = "mic",
        };

        store.Save(mappings);
        var loaded = store.Load();

        Assert.Equal("group1", loaded["C:\\Games\\game.exe"]);
        Assert.Equal("mic", loaded["C:\\Chat\\voice.exe"]);
    }

    [Fact]
    public void Load_IsCaseInsensitiveOnExecutablePath()
    {
        var store = new JsonExecutableGroupMapStore(_filePath);
        store.Save(new Dictionary<string, string> { ["C:\\Games\\Game.exe"] = "group1" });

        var loaded = store.Load();

        Assert.True(loaded.ContainsKey("c:\\games\\game.exe"));
    }
}
