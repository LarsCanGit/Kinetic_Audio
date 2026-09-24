namespace KineticAudio.Core.Settings;

/// <summary>
/// Ids for the fixed set of 6 mixer strips (Main Out, Group 1-4, Mic). The group count is fixed
/// (Sonar's model), not dynamic, so these are well-known constants rather than data.
/// </summary>
public static class WellKnownGroupIds
{
    public const string Group1Id = "group1";
    public const string Group2Id = "group2";
    public const string Group3Id = "group3";
    public const string Group4Id = "group4";
    public const string MicId = "mic";
}
