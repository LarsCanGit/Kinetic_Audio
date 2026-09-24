using Microsoft.Win32;

namespace KineticAudio.Core.Devices;

/// <summary>Wraps the HKCU Run-key registration used for the "run at Windows startup" toggle.
/// Kept separate from JSON settings since it's OS integration, not app config.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KineticAudio";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
            key.SetValue(ValueName, $"\"{executablePath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
