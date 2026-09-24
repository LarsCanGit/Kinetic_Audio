using KineticAudio.Core.Audio;
using KineticAudio.Core.Volume;
using KineticAudio.Tests.Core.Audio;

namespace KineticAudio.Tests.Core.Volume;

public class SessionVolumeWriterTests
{
    private static VolumeStateManager CreateManager(params string[] extraGroupIds)
    {
        var groups = new List<MixerGroup> { new("group-1", "Group 1") };
        groups.AddRange(extraGroupIds.Select(id => new MixerGroup(id, id)));
        return new VolumeStateManager(groups, "group-1");
    }

    [Fact]
    public void SetGroupVolume_WritesEffectiveVolume_ToEverySessionInGroup()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\discord.exe", "Discord (helper)"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-1");
        using var writer = new SessionVolumeWriter(provider, manager);

        manager.SetGroupVolume("group-1", 0.4f);

        Assert.Equal(0.4f, provider.GetSessionVolume("session-1"), 3);
        Assert.Equal(0.4f, provider.GetSessionVolume("session-2"), 3);
    }

    [Fact]
    public void SetMainVolume_WritesEffectiveVolume_ToAllTrackedSessions()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager("group-2");
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-2");
        manager.SetGroupVolume("group-1", 0.5f);
        manager.SetGroupVolume("group-2", 0.8f);
        using var writer = new SessionVolumeWriter(provider, manager);

        manager.SetMainVolume(0.5f);

        Assert.Equal(0.25f, provider.GetSessionVolume("session-1"), 3);
        Assert.Equal(0.4f, provider.GetSessionVolume("session-2"), 3);
    }

    [Fact]
    public void AssignSessionToGroup_WritesNewEffectiveVolume_ForReassignedSession()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager("group-2");
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-2", 0.7f);
        using var writer = new SessionVolumeWriter(provider, manager);

        manager.AssignSessionToGroup("session-1", "group-2");

        Assert.Equal(0.7f, provider.GetSessionVolume("session-1"), 3);
    }

    [Fact]
    public void MuteThenUnmuteGroup_WritesZeroThenRestoredVolume()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.6f);
        using var writer = new SessionVolumeWriter(provider, manager);

        manager.MuteGroup("group-1");
        Assert.Equal(0f, provider.GetSessionVolume("session-1"), 3);

        manager.UnmuteGroup("group-1");
        Assert.Equal(0.6f, provider.GetSessionVolume("session-1"), 3);
    }

    [Fact]
    public void Dispose_UnsubscribesFromManager_SoFurtherChangesAreNotWritten()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        manager.AssignSessionToGroup("session-1", "group-1");
        var writer = new SessionVolumeWriter(provider, manager);

        writer.Dispose();
        manager.SetGroupVolume("group-1", 0.1f);

        Assert.Equal(1.0f, provider.GetSessionVolume("session-1"), 3);
    }
    [Fact]
    public void SetGroupVolume_SessionAlreadyRemovedFromProvider_DoesNotThrowAndWritesOthers()
    {
        // Regression: an app closing drops its session from the provider before the
        // SessionRemoved event reaches VolumeStateManager. A fader move in that gap used to
        // throw KeyNotFoundException out of the fader handler and crash the app.
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-1");
        using var writer = new SessionVolumeWriter(provider, manager);
        provider.RaiseSessionRemoved("session-1");

        manager.SetGroupVolume("group-1", 0.3f);

        Assert.Equal(0.3f, provider.GetSessionVolume("session-2"), 3);
    }

    [Fact]
    public void MuteGroup_SessionComObjectDead_DoesNotThrowAndWritesOthers()
    {
        // Regression: a disconnected device makes SetMasterVolume fail with a COM error, which
        // used to propagate out of the mute click and crash the app.
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-1");
        using var writer = new SessionVolumeWriter(provider, manager);
        provider.KillSession("session-1");

        manager.MuteGroup("group-1");

        Assert.Equal(0f, provider.GetSessionVolume("session-2"), 3);
    }
}
