namespace KineticAudio;

/// <summary>A single "app assigned to this group" chip in a strip's session-card blob.</summary>
public sealed class SessionChipViewModel(string sessionKey, string displayName)
{
    public string SessionKey { get; } = sessionKey;
    public string DisplayName { get; } = displayName;
}
