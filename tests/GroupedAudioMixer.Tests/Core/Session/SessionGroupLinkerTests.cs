using KineticAudio.Core.Audio;
using KineticAudio.Core.Mapping;
using KineticAudio.Core.Session;
using KineticAudio.Core.Volume;
using KineticAudio.Tests.Core.Audio;
using KineticAudio.Tests.Core.Mapping;

namespace KineticAudio.Tests.Core.Session;

public class SessionGroupLinkerTests
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
        using var linker = new SessionGroupLinker(provider, map, manager);

        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void SessionCreated_WithUnmappedExecutable_FallsBackToDefaultGroup()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);

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

        using var linker = new SessionGroupLinker(provider, map, manager);

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void SessionRemoved_DropsSessionFromLiveTracking()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        map.SetMapping(@"C:\Apps\discord.exe", "group-2");
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);
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
        var linker = new SessionGroupLinker(provider, map, manager);
        var raised = false;
        manager.EffectiveVolumeChanged += (_, _) => raised = true;

        linker.Dispose();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));

        Assert.False(raised);
    }

    [Fact]
    public void AssignExecutableToGroup_PersistsMapping_ThroughExecutableGroupMap()
    {
        var provider = new FakeAudioSessionProvider();
        var store = new FakeExecutableGroupMapStore();
        var map = new ExecutableGroupMap(store);
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);

        linker.AssignExecutableToGroup(@"C:\Apps\discord.exe", "group-2");

        Assert.Equal("group-2", map.TryGetGroupId(@"C:\Apps\discord.exe"));
        Assert.Equal("group-2", store.Load()[@"C:\Apps\discord.exe"]);
    }

    [Fact]
    public void AssignExecutableToGroup_ReassignsLiveSession_Immediately()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        Assert.Equal("group-1", manager.GetGroupIdForSession("session-1"));

        linker.AssignExecutableToGroup(@"C:\Apps\discord.exe", "group-2");

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void AssignExecutableToGroup_ReassignsAllLiveSessionsSharingExecutable()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\discord.exe", "Discord (helper)"));

        linker.AssignExecutableToGroup(@"C:\Apps\discord.exe", "group-2");

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
        Assert.Equal("group-2", manager.GetGroupIdForSession("session-2"));
    }

    [Fact]
    public void AssignExecutableToGroup_DoesNotAffectSessionsForOtherExecutables()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"));

        linker.AssignExecutableToGroup(@"C:\Apps\discord.exe", "group-2");

        Assert.Equal("group-1", manager.GetGroupIdForSession("session-2"));
    }

    [Fact]
    public void AssignExecutableToGroup_WithNoLiveSession_StillPersistsMapping_ForFutureAutoLink()
    {
        var provider = new FakeAudioSessionProvider();
        var map = new ExecutableGroupMap(new FakeExecutableGroupMapStore());
        var manager = CreateManager();
        using var linker = new SessionGroupLinker(provider, map, manager);

        linker.AssignExecutableToGroup(@"C:\Apps\discord.exe", "group-2");
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));

        Assert.Equal("group-2", manager.GetGroupIdForSession("session-1"));
    }
}
