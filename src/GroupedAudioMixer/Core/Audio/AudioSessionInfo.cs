namespace KineticAudio.Core.Audio;

/// <summary>
/// A live Windows audio session, as reported by an <see cref="IAudioSessionProvider"/>.
/// </summary>
/// <param name="SessionKey">
/// Identifies this specific live session instance. Ephemeral: a new session key is issued
/// each time the owning process opens a new audio stream, even for the same executable.
/// </param>
/// <param name="ExecutablePath">
/// The owning process's executable path. Durable across process restarts, so this is the key
/// used for exe-to-group persistence (a session key is not, since sessions are not persistent).
/// </param>
/// <param name="DisplayName">Human-readable name for UI display.</param>
/// <param name="Flow">
/// Whether the session plays audio (render) or records it (capture). Capture sessions belong to
/// the Mic strip regardless of the executable's playback group.
/// </param>
public sealed record AudioSessionInfo(
    string SessionKey,
    string ExecutablePath,
    string DisplayName,
    AudioSessionFlow Flow = AudioSessionFlow.Render)
{
    /// <summary>
    /// Stand-in <see cref="ExecutablePath"/> for the Windows system sounds session, which has no
    /// owning process. Lets it use the same exe-to-group map as any app.
    /// </summary>
    public const string SystemSoundsKey = "system-sounds";

    public const string SystemSoundsDisplayName = "System Sounds";

    /// <summary>Human-readable name for a mapped executable key (path or <see cref="SystemSoundsKey"/>).</summary>
    public static string FriendlyNameForExecutable(string executablePath) =>
        string.Equals(executablePath, SystemSoundsKey, StringComparison.OrdinalIgnoreCase)
            ? SystemSoundsDisplayName
            : System.IO.Path.GetFileNameWithoutExtension(executablePath);
}

public enum AudioSessionFlow
{
    Render,
    Capture,
}
