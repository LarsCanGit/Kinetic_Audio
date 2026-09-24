using KineticAudio.Core.Settings;

namespace KineticAudio.Tests.Core.Settings;

public class GroupRenamePolicyTests
{
    [Theory]
    [InlineData(WellKnownGroupIds.Group1Id)]
    [InlineData(WellKnownGroupIds.Group2Id)]
    [InlineData(WellKnownGroupIds.Group3Id)]
    [InlineData(WellKnownGroupIds.Group4Id)]
    public void CanRename_PlaybackGroups_IsAllowed(string groupId) =>
        Assert.True(GroupRenamePolicy.CanRename(groupId));

    [Fact]
    public void CanRename_Mic_IsNotAllowed() =>
        Assert.False(GroupRenamePolicy.CanRename(WellKnownGroupIds.MicId));
}
