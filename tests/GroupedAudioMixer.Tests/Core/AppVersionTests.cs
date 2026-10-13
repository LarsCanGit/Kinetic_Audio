using KineticAudio.Core;

namespace KineticAudio.Tests.Core;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+abc1234", "0.1.0")]
    [InlineData("1.2.3-beta.1+abc1234", "1.2.3-beta.1")]
    [InlineData(null, "unknown")]
    [InlineData("  ", "unknown")]
    public void Format_StripsBuildMetadata(string? input, string expected) =>
        Assert.Equal(expected, AppVersion.Format(input));

    [Fact]
    public void Current_IsNotUnknown() => Assert.NotEqual("unknown", AppVersion.Current);
}
