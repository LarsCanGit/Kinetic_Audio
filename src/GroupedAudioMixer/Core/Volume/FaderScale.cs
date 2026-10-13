namespace KineticAudio.Core.Volume;

/// <summary>
/// Maps a fader's position to the linear 0..1 volume held by <see cref="VolumeStateManager"/>
/// and back. The mapping is linear, so the fader sits at the same position as the Windows volume
/// mixer slider (which is linear in the Core Audio scalar) and the percent readout is the real
/// volume. A dB-scaled fader packed everything under about -24 dB (6%) into the bottom third of
/// the travel. Positions are expressed in the slider's -60..0 units (the names keep "Db" for
/// historical reasons); the top is 0 because Core Audio session volume can't exceed 1.0, and the
/// bottom stop is silence.
/// </summary>
public static class FaderScale
{
    public const double MinDb = -60.0;
    public const double MaxDb = 0.0;

    public static float LinearFromDb(double db) => (float)FractionFromDb(db);

    public static double DbFromLinear(float linear) => MinDb + Math.Clamp(linear, 0f, 1f) * (MaxDb - MinDb);

    /// <summary>Fader position as a fraction of its travel, 0 at the bottom, 1 at the top.</summary>
    public static double FractionFromDb(double db) => (Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb);

    public static int PercentFromDb(double db) => (int)Math.Round(FractionFromDb(db) * 100);
}
