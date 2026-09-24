using KineticAudio.Core.Audio;
using KineticAudio.Core.Volume;
using KineticAudio.Tests.Core.Audio;

namespace KineticAudio.Tests.Core.Volume;

public class VolumeDriftReconcilerTests
{
    private static VolumeStateManager CreateManager(params string[] extraGroupIds)
    {
        var groups = new List<MixerGroup> { new("group-1", "Group 1") };
        groups.AddRange(extraGroupIds.Select(id => new MixerGroup(id, id)));
        return new VolumeStateManager(groups, "group-1");
    }

    [Fact]
    public void PollOnce_DriftedSession_ReconcilesGroupVolume()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"), initialVolume: 0.5f);
        provider.SetSessionVolume("session-1", 0.9f);
        var reconciler = new VolumeDriftReconciler(provider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.9f, manager.GetGroup("group-1").Volume, 3);
    }

    [Fact]
    public void PollOnce_SessionWithinEpsilon_LeavesGroupVolumeUnchanged()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"), initialVolume: 0.5f);
        var reconciler = new VolumeDriftReconciler(provider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, 3);
    }

    [Fact]
    public void PollOnce_MultipleSessionsAcrossGroups_EachReconciledIndependently()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-2");
        manager.SetGroupVolume("group-1", 0.5f);
        manager.SetGroupVolume("group-2", 0.5f);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"), initialVolume: 0.5f);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"), initialVolume: 0.5f);
        provider.SetSessionVolume("session-1", 0.8f);
        provider.SetSessionVolume("session-2", 0.2f);
        var reconciler = new VolumeDriftReconciler(provider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.8f, manager.GetGroup("group-1").Volume, 3);
        Assert.Equal(0.2f, manager.GetGroup("group-2").Volume, 3);
    }

    [Fact]
    public void PollOnce_NoLiveSessions_IsNoOp()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        var reconciler = new VolumeDriftReconciler(provider, manager);

        var exception = Record.Exception(() => reconciler.PollOnce());

        Assert.Null(exception);
    }

    [Fact]
    public void StartAndStop_DoNotThrow_AndDisposeIsSafe()
    {
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        var reconciler = new VolumeDriftReconciler(provider, manager, TimeSpan.FromMilliseconds(50));

        reconciler.Start();
        reconciler.Stop();
        reconciler.Dispose();
    }
    [Fact]
    public void PollOnce_SessionDiesMidPoll_DoesNotThrowAndReconcilesOthers()
    {
        // Regression: PollOnce runs on a timer thread, where an unhandled exception kills the
        // process. A session that expires between GetActiveSessions and the volume read used to
        // throw out of PollOnce.
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-2");
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        provider.RaiseSessionCreated(new AudioSessionInfo("session-2", @"C:\Apps\spotify.exe", "Spotify"));
        provider.SetSessionVolume("session-2", 0.4f);
        provider.KillSession("session-1");
        var reconciler = new VolumeDriftReconciler(provider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.4f, manager.GetGroup("group-2").Volume, 3);
    }
}
