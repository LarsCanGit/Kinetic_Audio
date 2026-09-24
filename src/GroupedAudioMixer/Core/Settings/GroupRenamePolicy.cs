namespace KineticAudio.Core.Settings;

/// <summary>Which of the fixed mixer strips the user may rename. Mic is a dedicated recording strip
/// with a fixed identity, so it keeps its name; the four playback groups are user-nameable.</summary>
public static class GroupRenamePolicy
{
    public static bool CanRename(string groupId) =>
        !string.Equals(groupId, WellKnownGroupIds.MicId, StringComparison.OrdinalIgnoreCase);
}
