using KineticAudio.Core.Volume;

namespace KineticAudio.Tests.Core.Volume;

public class FaderScaleTests
{
    [Fact]
    public void Top_IsFullVolume_AndReadsHundredPercent()
    {
        Assert.Equal(1f, FaderScale.LinearFromDb(FaderScale.MaxDb));
        Assert.Equal(100, FaderScale.PercentFromDb(FaderScale.MaxDb));
    }

    [Fact]
    public void Bottom_IsSilent_AndReadsZeroPercent()
    {
        Assert.Equal(0f, FaderScale.LinearFromDb(FaderScale.MinDb));
        Assert.Equal(0, FaderScale.PercentFromDb(FaderScale.MinDb));
    }

    [Fact]
    public void EveryStepBelowTop_IsQuieterThanTheOneAbove()
    {
        // Regression: the old -60..+12 dB range clamped everything above 0 dB (~83%) to 1.0,
        // so the top of the fader did nothing.
        for (var db = FaderScale.MaxDb - 0.5; db >= FaderScale.MinDb; db -= 0.5)
            Assert.True(FaderScale.LinearFromDb(db) < FaderScale.LinearFromDb(db + 0.5), $"{db} dB");
    }

    [Theory]
    [InlineData(1f, 0.0)]
    [InlineData(0.5f, -6.0206)]
    [InlineData(0f, -60.0)]
    public void DbFromLinear_MapsStateVolumeToFaderPosition(float linear, double expectedDb)
    {
        Assert.Equal(expectedDb, FaderScale.DbFromLinear(linear), precision: 3);
    }

    [Fact]
    public void DbFromLinear_BelowFaderRange_SitsAtBottom()
    {
        Assert.Equal(FaderScale.MinDb, FaderScale.DbFromLinear(0.0001f));
    }
}
