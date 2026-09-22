using GroupedAudioMixer.Core.Mapping;

namespace GroupedAudioMixer.Tests.Core.Mapping;

public class ExecutableGroupMapTests
{
    [Fact]
    public void TryGetGroupId_ReturnsNull_WhenUnmapped()
    {
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());

        Assert.Null(map.TryGetGroupId(@"C:\Apps\discord.exe"));
    }

    [Fact]
    public void SetMapping_ThenTryGetGroupId_ReturnsMappedGroup()
    {
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());

        map.SetMapping(@"C:\Apps\discord.exe", "group-voice");

        Assert.Equal("group-voice", map.TryGetGroupId(@"C:\Apps\discord.exe"));
    }

    [Fact]
    public void TryGetGroupId_IsCaseInsensitive()
    {
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());

        map.SetMapping(@"C:\Apps\Discord.exe", "group-voice");

        Assert.Equal("group-voice", map.TryGetGroupId(@"c:\apps\discord.exe"));
    }

    [Fact]
    public void SetMapping_Persists_ToStore()
    {
        var store = new FakeExecutableGroupMapStore();
        var map = new ExecutableGroupMap(store);

        map.SetMapping(@"C:\Apps\discord.exe", "group-voice");

        Assert.Equal("group-voice", store.Load()[@"C:\Apps\discord.exe"]);
    }

    [Fact]
    public void RemoveMapping_ClearsMapping_AndPersists()
    {
        var store = new FakeExecutableGroupMapStore();
        var map = new ExecutableGroupMap(store);
        map.SetMapping(@"C:\Apps\discord.exe", "group-voice");

        map.RemoveMapping(@"C:\Apps\discord.exe");

        Assert.Null(map.TryGetGroupId(@"C:\Apps\discord.exe"));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void GetAllMappings_ReturnsSnapshot_NotLiveReference()
    {
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-voice");

        var snapshot = map.GetAllMappings();
        map.SetMapping(@"C:\Apps\spotify.exe", "group-music");

        Assert.Single(snapshot);
        Assert.Equal(2, map.GetAllMappings().Count);
    }

    [Fact]
    public void Constructor_LoadsExistingMappings_FromStore()
    {
        var store = new FakeExecutableGroupMapStore();
        store.Save(new Dictionary<string, string> { [@"C:\Apps\discord.exe"] = "group-voice" });

        var map = new ExecutableGroupMap(store);

        Assert.Equal("group-voice", map.TryGetGroupId(@"C:\Apps\discord.exe"));
    }

    [Fact]
    public void ConcurrentSetMapping_FromManyThreads_AllLandWithoutLossOrCorruption()
    {
        // Simulates D9's target race: a burst of sessions (auto-link) plus manual drag-to-group
        // hitting the map at once from different threads.
        var store = new FakeExecutableGroupMapStore();
        var map = new ExecutableGroupMap(store);
        const int threadCount = 50;

        Parallel.For(0, threadCount, i =>
        {
            map.SetMapping($@"C:\Apps\app{i}.exe", $"group-{i}");
        });

        var all = map.GetAllMappings();
        Assert.Equal(threadCount, all.Count);
        for (var i = 0; i < threadCount; i++)
            Assert.Equal($"group-{i}", all[$@"C:\Apps\app{i}.exe"]);

        // Store's final persisted state must match too (no write clobbered by a race).
        var persisted = store.Load();
        Assert.Equal(threadCount, persisted.Count);
    }
}
