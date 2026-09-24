namespace KineticAudio;

/// <summary>
/// One "app assigned to this group" chip in a strip's session card. One chip per executable, not
/// per session: dragging a chip moves the executable's mapping, which covers all of its sessions.
/// </summary>
public sealed class SessionChipViewModel(string executablePath, string displayName, string? groupId, bool isCapture = false)
{
    public string ExecutablePath { get; } = executablePath;
    public string DisplayName { get; } = displayName;

    /// <summary>The group this chip is currently shown under; dropping it back there is a no-op.</summary>
    public string? GroupId { get; } = groupId;

    /// <summary>
    /// Sessions with no resolvable executable (e.g. system sounds) have nothing to map, and a
    /// recording chip on the Mic strip always stays there (the map only places playback).
    /// </summary>
    public bool CanDrag => !isCapture && !string.IsNullOrEmpty(ExecutablePath);
}
