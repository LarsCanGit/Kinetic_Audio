namespace KineticAudio.Core.Volume;

/// <summary>
/// The Main channel: a global multiplier applied at write time on top of each group's fader.
/// It is not a group, has no session membership of its own, and never touches a stored group
/// fader value.
/// </summary>
public sealed class MainChannel
{
    /// <summary>Global multiplier, 0.0-1.0. Unaffected by <see cref="IsMuted"/>.</summary>
    public float Volume { get; set; } = 1.0f;

    public bool IsMuted { get; set; }
}
