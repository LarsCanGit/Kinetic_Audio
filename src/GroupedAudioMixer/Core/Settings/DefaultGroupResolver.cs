namespace KineticAudio.Core.Settings;

/// <summary>
/// Resolves which group new/unmapped audio sessions default into. Group 4 ("Aux") is preferred
/// when present, matching the Sonar-style catch-all convention; otherwise falls back to the
/// first configured group so the app still works with a non-standard group list.
/// </summary>
public static class DefaultGroupResolver
{
    public static string ResolveDefaultGroupId(IReadOnlyList<GroupDefinition> groups)
    {
        if (groups.Count == 0)
            throw new ArgumentException("At least one group is required.", nameof(groups));

        var group4 = groups.FirstOrDefault(g => g.Id == WellKnownGroupIds.Group4Id);
        return group4?.Id ?? groups[0].Id;
    }
}
