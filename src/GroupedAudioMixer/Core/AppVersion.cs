using System.Reflection;

namespace KineticAudio.Core;

/// <summary>The app's version, taken from the assembly (set by <c>Directory.Build.props</c>).</summary>
public static class AppVersion
{
    /// <summary>E.g. "0.1.0". Falls back to "unknown" if the assembly carries no version.</summary>
    public static string Current { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Drops the "+commit" build metadata the SDK appends to the informational version, so the
    /// UI shows "0.1.0" rather than "0.1.0+abc1234".
    /// </summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return "unknown";

        var plus = informationalVersion.IndexOf('+');
        return plus < 0 ? informationalVersion : informationalVersion[..plus];
    }
}
