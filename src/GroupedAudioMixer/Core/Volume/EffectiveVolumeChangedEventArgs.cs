namespace GroupedAudioMixer.Core.Volume;

/// <summary>
/// Raised whenever the state managed by <see cref="VolumeStateManager"/> changes in a way that
/// requires the writer layer to push fresh volumes to Core Audio. Carries only the sessions
/// affected, not the whole session set, so both the Phase 1 per-session writer and any future
/// Phase 3 mix engine can react without recomputing everything.
/// </summary>
public sealed class EffectiveVolumeChangedEventArgs(IReadOnlyList<(string SessionKey, float Volume)> sessions)
    : EventArgs
{
    public IReadOnlyList<(string SessionKey, float Volume)> Sessions { get; } = sessions;
}
