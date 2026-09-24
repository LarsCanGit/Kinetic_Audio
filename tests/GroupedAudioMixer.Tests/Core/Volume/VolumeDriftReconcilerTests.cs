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

    [Fact]
    public void PollOnce_SessionNotYetLinkedToGroup_LeavesDefaultGroupUntouched()
    {
        // The provider lists a new session before SessionCreated reaches the linker. A poll in
        // that window must not read the session's pre-link volume (Windows' persisted value) as
        // drift on the default group.
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        manager.SetGroupVolume("group-1", 0.5f);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"), initialVolume: 1.0f);
        var reconciler = new VolumeDriftReconciler(provider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, 3);
    }

    [Fact]
    public void PollOnce_FaderMovedBetweenReadAndReconcile_DoesNotRevertFader()
    {
        // The poll reads the session's volume outside the state lock. If the user moves the fader
        // (which writes the new volume) right after that read, the stale reading must not be
        // treated as drift and snap the group back to the old value.
        var provider = new FakeAudioSessionProvider();
        var manager = CreateManager();
        using var writer = new SessionVolumeWriter(provider, manager);
        provider.RaiseSessionCreated(new AudioSessionInfo("session-1", @"C:\Apps\discord.exe", "Discord"));
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);
        // The read returns 0.5; the fader then moves to 0.8 before the reconcile runs.
        var racingProvider = new ReadThenActProvider(provider, () => manager.SetGroupVolume("group-1", 0.8f));
        var reconciler = new VolumeDriftReconciler(racingProvider, manager);

        reconciler.PollOnce();

        Assert.Equal(0.8f, manager.GetGroup("group-1").Volume, 3);
    }

    /// <summary>Wraps a provider so an action runs right after each volume read returns.</summary>
    private sealed class ReadThenActProvider(FakeAudioSessionProvider inner, Action afterRead) : IAudioSessionProvider
    {
        public event EventHandler<AudioSessionEventArgs>? SessionCreated { add => inner.SessionCreated += value; remove => inner.SessionCreated -= value; }
        public event EventHandler<AudioSessionEventArgs>? SessionRemoved { add => inner.SessionRemoved += value; remove => inner.SessionRemoved -= value; }
        public IReadOnlyList<AudioSessionInfo> GetActiveSessions() => inner.GetActiveSessions();
        public bool TrySetSessionVolume(string sessionKey, float volume) => inner.TrySetSessionVolume(sessionKey, volume);

        public bool TryGetSessionVolume(string sessionKey, out float volume)
        {
            var ok = inner.TryGetSessionVolume(sessionKey, out volume);
            afterRead();
            return ok;
        }
    }
}
