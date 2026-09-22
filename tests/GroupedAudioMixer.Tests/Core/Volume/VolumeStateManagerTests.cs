using GroupedAudioMixer.Core.Volume;

namespace GroupedAudioMixer.Tests.Core.Volume;

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
    public void RemoveGroup_ReassignsLiveSessionsToDefaultGroup_Immediately()
    {
        var manager = CreateManager("group-2");
        manager.AssignSessionToGroup("session-1", "group-2");
        manager.SetGroupVolume("group-2", 0.4f);
        manager.SetGroupVolume("group-1", 0.9f);

        var reassigned = manager.RemoveGroup("group-2");

        Assert.Equal(["session-1"], reassigned);
        Assert.Equal("group-1", manager.GetGroupIdForSession("session-1"));
        Assert.Equal(0.9f, manager.GetEffectiveVolume("session-1"), precision: 4);
    }

    [Fact]
    public void RemoveGroup_PromotesAnotherGroupToDefault_WhenDefaultGroupIsRemoved()
    {
        var manager = CreateManager("group-2");

        manager.RemoveGroup("group-1");

        Assert.Equal("group-2", manager.DefaultGroupId);
    }

    [Fact]
    public void RemoveGroup_Throws_WhenOnlyOneGroupRemains()
    {
        var manager = CreateManager();

        Assert.Throws<InvalidOperationException>(() => manager.RemoveGroup("group-1"));
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
}
