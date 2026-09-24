using System.Windows.Input;
using KineticAudio.Core.Settings;
using Xunit;

namespace KineticAudio.Tests.Core.Settings;

public class JsonAppSettingsStoreTests : IDisposable
{
    private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"gam-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new JsonAppSettingsStore(_filePath);

        var settings = store.Load();

        Assert.Equal(new AppSettings().PlaybackSwitchCount, settings.PlaybackSwitchCount);
        Assert.Equal(new AppSettings().CycleHotkeyKey, settings.CycleHotkeyKey);
    }

    [Fact]
    public void Load_MalformedFile_ReturnsDefaultsRatherThanThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, "{ not valid json");
        var store = new JsonAppSettingsStore(_filePath);

        var settings = store.Load();

        Assert.NotNull(settings);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new JsonAppSettingsStore(_filePath);
        var settings = new AppSettings
        {
            PlaybackDeviceOrder = new List<string> { "dev-a", "dev-b" },
            PlaybackSwitchCount = 1,
            RecordingDeviceOrder = new List<string> { "mic-a" },
            CycleHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Shift,
            CycleHotkeyKey = Key.F9,
            RunAtStartup = true,
            MinimizeToTray = true,
            NotifyOnSwitch = true,
            Groups = new List<GroupDefinition> { new("group1", "Group 1"), new("custom", "Custom") },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.PlaybackDeviceOrder, loaded.PlaybackDeviceOrder);
        Assert.Equal(settings.PlaybackSwitchCount, loaded.PlaybackSwitchCount);
        Assert.Equal(settings.RecordingDeviceOrder, loaded.RecordingDeviceOrder);
        Assert.Equal(settings.CycleHotkeyModifiers, loaded.CycleHotkeyModifiers);
        Assert.Equal(settings.CycleHotkeyKey, loaded.CycleHotkeyKey);
        Assert.Equal(settings.RunAtStartup, loaded.RunAtStartup);
        Assert.Equal(settings.MinimizeToTray, loaded.MinimizeToTray);
        Assert.Equal(settings.NotifyOnSwitch, loaded.NotifyOnSwitch);
        Assert.Equal(settings.Groups.Select(g => (g.Id, g.Name)), loaded.Groups.Select(g => (g.Id, g.Name)));
    }

    [Fact]
    public void Reconcile_DropsStaleIdsAndAppendsNewOnesToBottom()
    {
        var stored = new List<string> { "old-1", "still-here", "old-2" };
        var current = new List<string> { "still-here", "new-device" };

        var result = DeviceOrderReconciler.Reconcile(stored, current);

        Assert.Equal(new[] { "still-here", "new-device" }, result);
    }

    [Fact]
    public void DefaultGroups_HasFiveWellKnownGroups()
    {
        var settings = new AppSettings();

        Assert.Equal(
            new[] { "group1", "group2", "group3", "group4", "mic" },
            settings.Groups.Select(g => g.Id));
    }
}
