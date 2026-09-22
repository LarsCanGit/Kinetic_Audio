using GroupedAudioMixer.Core.Audio;
using GroupedAudioMixer.Core.Mapping;
using GroupedAudioMixer.Core.Session;
using GroupedAudioMixer.Core.Volume;
using GroupedAudioMixer.Tests.Core.Audio;
using GroupedAudioMixer.Tests.Core.Mapping;

namespace GroupedAudioMixer.Tests.Core.Session;

public class SessionAutoLinkerTests
{
    private static VolumeStateManager CreateManager() =>
        new(new[] { new MixerGroup("group-1", "Group 1"), new MixerGroup("group-2", "Group 2") }, "group-1");

    [Fact]
    public void SessionCreated_WithMappedExecutable_AssignsSessionToMappedGroup()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-2");
        var manager = CreateManager();
        using var linker = new SessionAutoLinker(provider, map, manager);

        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void SessionCreated_WithUnmappedExecutable_FallsBackToDefaultGroup()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionAutoLinker(provider, map, manager);

        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\unknown.exe", "Unknown"));

        Assert.Equal(manager.DefaultGroupId, manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void Construction_LinksSessionsAlreadyActive()
    {
        var provider = new FakeAudioSessionProvider();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-2");
        var manager = CreateManager();

        using var linker = new SessionAutoLinker(provider, map, manager);

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void SessionRemoved_DropsSessionFromLiveTracking()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-2");
        var manager = CreateManager();
        using var linker = new SessionAutoLinker(provider, map, manager);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\discord.exe", "Discord"));

        provider.RaiseSessionRemoved("session-1");

        EffectiveVolumeChangedEventArgs? captured = null;
        manager.EffectiveVolumeChanged += (_, args) => captured = args;
        manager.SetGroupVolume("group-2", 0.3f);

        Assert.DoesNotContain(captured!.Sessions, s => s.SessionKey == "session-1");
    }

    [Fact]
    public void Dispose_UnsubscribesFromProvider_SoFurtherSessionsAreNotLinked()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-2");
        var manager = CreateManager();
        var linker = new SessionAutoLinker(provider, map, manager);
        var raised = false;
        manager.EffectiveVolumeChanged += (_, _) => raised = true;

        linker.Dispose();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));

        Assert.False(raised);
    }
}
