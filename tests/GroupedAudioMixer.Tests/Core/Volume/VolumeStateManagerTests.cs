using KineticAudio.Core.Volume;

namespace KineticAudio.Tests.Core.Volume;

public class VolumeStateManagerTests
{
    private static VolumeStateManager CreateManager(params string[] extraGroupIds)
    {
        var groups = new List<MixerGroup> { new("group-1", "Group 1") };
        groups.AddRange(extraGroupIds.Select(id => new MixerGroup(id, id)));
        return new VolumeStateManager(groups, "group-1");
    }

    [Fact]
    public void EffectiveVolume_IsGroupTimesMain()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);
        manager.SetMainVolume(0.5f);

        Assert.Equal(0.25f, manager.GetEffectiveVolume("session-1"), precision: 4);
    }

    [Fact]
    public void MutingGroup_DoesNotChangeStoredVolume_AndUnmuteRestoresExactLevel()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.73f);

        manager.MuteGroup("group-1");
        Assert.Equal(0f, manager.GetEffectiveVolume("session-1"));
        Assert.Equal(0.73f, manager.GetGroup("group-1").Volume, precision: 4);

        manager.UnmuteGroup("group-1");
        Assert.Equal(0.73f, manager.GetEffectiveVolume("session-1"), precision: 4);
    }

    [Fact]
    public void MutingMain_SilencesAllSessionsRegardlessOfGroup()
    {
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-2");
        manager.SetGroupVolume("group-1", 1.0f);
        manager.SetGroupVolume("group-2", 1.0f);

        manager.MuteMain();

        Assert.Equal(0f, manager.GetEffectiveVolume("session-1"));
        Assert.Equal(0f, manager.GetEffectiveVolume("session-2"));
    }

    [Fact]
    public void ToggleMainMute_MutesThenRestoresExactPriorEffectiveVolume()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.8f);
        manager.SetMainVolume(0.6f);

        Assert.True(manager.ToggleMainMute());
        Assert.True(manager.Main.IsMuted);
        Assert.Equal(0f, manager.GetEffectiveVolume("session-1"));

        Assert.False(manager.ToggleMainMute());
        Assert.False(manager.Main.IsMuted);
        Assert.Equal(0.48f, manager.GetEffectiveVolume("session-1"), precision: 4);
    }

    [Fact]
    public void ToggleMainMute_NotifiesEverySession()
    {
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-2");
        var notified = new List<string>();
        manager.EffectiveVolumeChanged += (_, e) => notified.AddRange(e.Sessions.Select(s => s.SessionKey));

        manager.ToggleMainMute();

        Assert.Equal(new[] { "session-1", "session-2" }, notified.Order());
    }

    private static VolumeStateManager CreateManagerWithMic() =>
        new(new[] { new MixerGroup("group-1", "Group 1"), new MixerGroup("mic", "Mic") }, "group-1",
            mainIndependentGroupIds: ["mic"]);

    [Fact]
    public void MainIndependentGroup_IgnoresMainVolumeAndMute()
    {
        // Main Out is the output master; muting it (e.g. with the Main mute hotkey) must not
        // silence or turn down the microphone.
        var manager = CreateManagerWithMic();
        manager.AssignSessionToGroup("mic-session", "mic");
        manager.AssignSessionToGroup("playback", "group-1");
        manager.SetGroupVolume("mic", 0.7f);
        manager.SetMainVolume(0.5f);

        Assert.Equal(0.7f, manager.GetEffectiveVolume("mic-session"), precision: 4);
        Assert.Equal(0.5f, manager.GetEffectiveVolume("playback"), precision: 4);

        manager.MuteMain();

        Assert.Equal(0.7f, manager.GetEffectiveVolume("mic-session"), precision: 4);
        Assert.Equal(0f, manager.GetEffectiveVolume("playback"));
    }

    [Fact]
    public void MainChanges_DoNotNotifyMainIndependentSessions()
    {
        var manager = CreateManagerWithMic();
        manager.AssignSessionToGroup("mic-session", "mic");
        manager.AssignSessionToGroup("playback", "group-1");
        var notified = new List<string>();
        manager.EffectiveVolumeChanged += (_, e) => notified.AddRange(e.Sessions.Select(s => s.SessionKey));

        manager.SetMainVolume(0.3f);
        manager.ToggleMainMute();

        Assert.DoesNotContain("mic-session", notified);
        Assert.Contains("playback", notified);
    }

    [Fact]
    public void MainIndependentGroup_StillMutesAndRestoresOnItsOwn()
    {
        var manager = CreateManagerWithMic();
        manager.AssignSessionToGroup("mic-session", "mic");
        manager.SetGroupVolume("mic", 0.6f);

        manager.MuteGroup("mic");
        Assert.Equal(0f, manager.GetEffectiveVolume("mic-session"));

        manager.UnmuteGroup("mic");
        Assert.Equal(0.6f, manager.GetEffectiveVolume("mic-session"), precision: 4);
    }

    [Fact]
    public void ReconcileExternalVolume_MainIndependentGroup_TakesActualVolumeAsIs_EvenWhileMainMuted()
    {
        // An external change to an app's mic level (e.g. Discord's input slider writing the
        // capture session volume) is the new Mic fader value; Main plays no part in it.
        var manager = CreateManagerWithMic();
        manager.AssignSessionToGroup("mic-session", "mic");
        manager.SetGroupVolume("mic", 1.0f);
        manager.SetMainVolume(0.5f);
        manager.MuteMain();

        var changed = manager.ReconcileExternalVolume("mic-session", 0.3f);

        Assert.True(changed);
        Assert.Equal(0.3f, manager.GetGroup("mic").Volume, precision: 4);
    }

    [Fact]
    public void AssignSessionToGroup_FallsBackToDefaultGroup_WhenGroupDoesNotExist()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "no-such-group");

        Assert.Equal(manager.DefaultGroupId, manager.GetGroupIdForSession("session-1"));
    }

    [Fact]
    public void UnmappedSession_FallsBackToDefaultGroup()
    {
        var manager = CreateManager("group-2");

        Assert.Equal("group-1", manager.GetGroupIdForSession("never-assigned"));
    }

    [Fact]
    public void EffectiveVolumeChanged_FiresWithAffectedSessions_OnGroupVolumeChange()
    {
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-1");
        manager.AssignSessionToGroup("session-3", "group-2");

        EffectiveVolumeChangedEventArgs? captured = null;
        manager.EffectiveVolumeChanged += (_, args) => captured = args;

        manager.SetGroupVolume("group-1", 0.6f);

        Assert.NotNull(captured);
        var keys = captured!.Sessions.Select(s => s.SessionKey).ToList();
        Assert.Contains("session-1", keys);
        Assert.Contains("session-2", keys);
        Assert.DoesNotContain("session-3", keys);
    }

    [Fact]
    public void ReconcileExternalVolume_BackCalculatesGroupVolume_AndSyncsOtherSessionsInGroup()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.AssignSessionToGroup("session-2", "group-1");
        manager.SetMainVolume(0.5f);
        manager.SetGroupVolume("group-1", 1.0f);

        EffectiveVolumeChangedEventArgs? captured = null;
        manager.EffectiveVolumeChanged += (_, args) => captured = args;

        // External tool set session-1's actual volume to 0.25 while Main is 0.5,
        // implying the user dragged the group fader down to ~0.5.
        var changed = manager.ReconcileExternalVolume("session-1", 0.25f);

        Assert.True(changed);
        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, precision: 4);
        var keys = captured!.Sessions.Select(s => s.SessionKey).ToList();
        Assert.Contains("session-2", keys);
    }

    [Fact]
    public void ReconcileExternalVolume_NoOp_WhenWithinEpsilonOfExpected()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);

        var changed = manager.ReconcileExternalVolume("session-1", 0.5005f);

        Assert.False(changed);
        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, precision: 4);
    }

    [Fact]
    public void ReconcileExternalVolume_NoOp_WhenMainIsMuted()
    {
        var manager = CreateManager();
        manager.AssignSessionToGroup("session-1", "group-1");
        manager.SetGroupVolume("group-1", 0.5f);
        manager.MuteMain();

        var changed = manager.ReconcileExternalVolume("session-1", 0.9f);

        Assert.False(changed);
        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, precision: 4);
    }

    [Fact]
    public void SetGroupVolume_ClampsToValidRange()
    {
        var manager = CreateManager();

        manager.SetGroupVolume("group-1", 1.5f);
        Assert.Equal(1.0f, manager.GetGroup("group-1").Volume, precision: 4);

        manager.SetGroupVolume("group-1", -0.5f);
        Assert.Equal(0f, manager.GetGroup("group-1").Volume, precision: 4);
    }
    [Fact]
    public async Task ConcurrentSessionChurn_WhileMovingMainAndReading_DoesNotThrow()
    {
        // Regression: Core Audio raises session create/remove on background threads while the
        // UI thread moves faders and reads the session table. Unsynchronized dictionary access
        // threw "Collection was modified" or corrupted the table.
        var manager = CreateManager("group-2");
        var observedSessions = 0;
        manager.EffectiveVolumeChanged += (_, e) => observedSessions += e.Sessions.Count;
        const int iterations = 20_000;
        var start = new ManualResetEventSlim();

        var writers = Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < iterations; i++)
            {
                var key = $"t{t}-s{i % 50}";
                manager.AssignSessionToGroup(key, i % 2 == 0 ? "group-1" : "group-2");
                if (i % 3 == 0) manager.RemoveSession(key);
            }
        })).ToList();

        var ui = Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < iterations; i++)
            {
                manager.SetMainVolume((i % 100) / 100f);
                manager.SetGroupVolume("group-2", (i % 10) / 10f);
                _ = manager.GetGroupIdForSession($"t0-s{i % 50}");
            }
        });

        start.Set();
        var all = Task.WhenAll(writers.Append(ui));
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(30))) == all;
        Assert.True(finished, "Concurrent operations did not finish (deadlock?).");
        await all; // Rethrows any exception from the worker tasks.
    }

    [Fact]
    public void ReconcileExternalVolume_UnassignedSession_IsIgnored()
    {
        var manager = CreateManager();
        manager.SetGroupVolume("group-1", 0.5f);

        var changed = manager.ReconcileExternalVolume("never-assigned", 1.0f);

        Assert.False(changed);
        Assert.Equal(0.5f, manager.GetGroup("group-1").Volume, 3);
    }
}
