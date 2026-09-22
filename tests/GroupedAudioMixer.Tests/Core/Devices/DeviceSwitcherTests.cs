using GroupedAudioMixer.Core.Devices;
using Xunit;

namespace GroupedAudioMixer.Tests.Core.Devices;

public class DeviceSwitcherTests
{
    private static AudioDeviceInfo Playback(string id) => new(id, id, AudioDeviceKind.Playback);

    [Fact]
    public void CycleNext_AdvancesToNextDeviceInSwitchList()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("a"));
        provider.AddDevice(Playback("b"));
        provider.AddDevice(Playback("c"));
        provider.SetDefaultDevice(AudioDeviceKind.Playback, "a");

        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b", "c" });

        var next = switcher.CycleNext();

        Assert.Equal("b", next);
        Assert.Equal("b", provider.GetDefaultDeviceId(AudioDeviceKind.Playback));
    }

    [Fact]
    public void CycleNext_WrapsAroundAtEndOfSwitchList()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("a"));
        provider.AddDevice(Playback("b"));
        provider.SetDefaultDevice(AudioDeviceKind.Playback, "b");

        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b" });

        var next = switcher.CycleNext();

        Assert.Equal("a", next);
    }

    [Fact]
    public void CycleNext_StartsFromFirstEntry_WhenCurrentDefaultIsNotInSwitchList()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("a"));
        provider.AddDevice(Playback("b"));
        provider.AddDevice(Playback("other"));
        provider.SetDefaultDevice(AudioDeviceKind.Playback, "other");

        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b" });

        var next = switcher.CycleNext();

        Assert.Equal("a", next);
    }

    [Fact]
    public void CycleNext_SkipsSwitchListEntriesForDevicesNoLongerPresent()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("a"));
        provider.AddDevice(Playback("c"));
        provider.SetDefaultDevice(AudioDeviceKind.Playback, "a");

        // "b" is in the switch-list but not currently present (unplugged / driver update).
        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b", "c" });

        var next = switcher.CycleNext();

        Assert.Equal("c", next);
    }

    [Fact]
    public void CycleNext_Throws_WhenNoSwitchListDeviceIsPresent()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("other"));

        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b" });

        Assert.Throws<InvalidOperationException>(() => switcher.CycleNext());
    }

    [Fact]
    public void SwitchTo_SetsDefaultDeviceDirectly()
    {
        var provider = new FakeAudioDeviceProvider();
        provider.AddDevice(Playback("a"));
        provider.AddDevice(Playback("b"));

        var switcher = new DeviceSwitcher(provider, AudioDeviceKind.Playback, new[] { "a", "b" });
        switcher.SwitchTo("b");

        Assert.Equal("b", provider.GetDefaultDeviceId(AudioDeviceKind.Playback));
    }
}
