using KineticAudio.Core.Settings;
using Xunit;

namespace KineticAudio.Tests.Core.Settings;

public class DefaultGroupResolverTests
{
    [Fact]
    public void ResolveDefaultGroupId_Group4Present_ReturnsGroup4Id()
    {
        var groups = new List<GroupDefinition>
        {
            new(WellKnownGroupIds.Group1Id, "Group 1"),
            new(WellKnownGroupIds.Group4Id, "Group 4"),
            new(WellKnownGroupIds.MicId, "Mic"),
        };

        var result = DefaultGroupResolver.ResolveDefaultGroupId(groups);

        Assert.Equal(WellKnownGroupIds.Group4Id, result);
    }

    [Fact]
    public void ResolveDefaultGroupId_Group4Absent_ReturnsFirstGroupId()
    {
        var groups = new List<GroupDefinition>
        {
            new(WellKnownGroupIds.Group1Id, "Group 1"),
            new(WellKnownGroupIds.MicId, "Mic"),
        };

        var result = DefaultGroupResolver.ResolveDefaultGroupId(groups);

        Assert.Equal(WellKnownGroupIds.Group1Id, result);
    }

    [Fact]
    public void ResolveDefaultGroupId_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => DefaultGroupResolver.ResolveDefaultGroupId(new List<GroupDefinition>()));
    }
}
