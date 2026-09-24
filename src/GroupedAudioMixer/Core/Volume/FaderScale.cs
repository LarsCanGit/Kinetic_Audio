namespace KineticAudio.Core.Volume;

/// <summary>
/// Maps a fader's dB position to the linear 0..1 volume held by <see cref="VolumeStateManager"/>
/// and back. The range tops out at 0 dB because Core Audio session volume can't exceed 1.0, so any
/// travel above it would do nothing. The bottom stop is silence rather than -60 dB, so 0% really
/// is off and 100% is full volume.
/// </summary>
public static class FaderScale
{
    public const double MinDb = -60.0;
    public const double MaxDb = 0.0;

    public static float LinearFromDb(double db) =>
        db <= MinDb ? 0f : (float)Math.Pow(10, Math.Min(db, MaxDb) / 20.0);

    public static double DbFromLinear(float linear) =>
        linear <= 0f ? MinDb : Math.Clamp(20.0 * Math.Log10(linear), MinDb, MaxDb);

    /// <summary>Fader position as a fraction of its travel, 0 at the bottom, 1 at the top.</summary>
    public static double FractionFromDb(double db) => (Math.Clamp(db, MinDb, MaxDb) - MinDb) / (MaxDb - MinDb);

    public static int PercentFromDb(double db) => (int)Math.Round(FractionFromDb(db) * 100);
}
