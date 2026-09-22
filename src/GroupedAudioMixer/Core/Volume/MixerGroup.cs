namespace GroupedAudioMixer.Core.Volume;

/// <summary>
/// A user-defined volume group (e.g. "Game", "Chat", "Music"). Owns its own fader position
/// and mute flag; muting never mutates <see cref="Volume"/>, so unmuting restores the exact
/// pre-mute level.
/// </summary>
public sealed class MixerGroup(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; set; } = name;

    /// <summary>Fader position, 0.0-1.0. Unaffected by <see cref="IsMuted"/>.</summary>
    public float Volume { get; set; } = 1.0f;

    public bool IsMuted { get; set; }
}
