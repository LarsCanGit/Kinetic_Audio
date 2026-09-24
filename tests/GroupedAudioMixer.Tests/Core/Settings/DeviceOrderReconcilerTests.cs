using KineticAudio.Core.Settings;

namespace KineticAudio.Tests.Core.Settings;

public class DeviceOrderReconcilerTests
{
    [Fact]
    public void Reconcile_KeepsAbsentDevicesInPlace_AndAppendsNewOnesToBottom()
    {
        var stored = new List<string> { "headset", "speakers", "monitor" };
        var current = new List<string> { "speakers", "monitor", "new-device" };

        var result = DeviceOrderReconciler.Reconcile(stored, current);

        Assert.Equal(new[] { "headset", "speakers", "monitor", "new-device" }, result);
    }

    [Fact]
    public void Reconcile_UnplugThenReplug_RestoresOriginalPosition()
    {
        var stored = new List<string> { "headset", "speakers", "monitor" };

        var whileUnplugged = DeviceOrderReconciler.Reconcile(stored, new[] { "speakers", "monitor" });
        var afterReplug = DeviceOrderReconciler.Reconcile(whileUnplugged, new[] { "monitor", "speakers", "headset" });

        Assert.Equal(new[] { "headset", "speakers", "monitor" }, afterReplug);
    }

    [Fact]
    public void Reconcile_MatchesIdsCaseInsensitively()
    {
        var result = DeviceOrderReconciler.Reconcile(new[] { "ABC" }, new[] { "abc" });

        Assert.Equal(new[] { "ABC" }, result);
    }

    [Fact]
    public void MergeVisibleOrder_ReorderingPresentDevices_KeepsAbsentDeviceSlots()
    {
        // Full order: headset(absent), speakers, | divider after 2 | monitor.
        var full = new List<string> { "headset", "speakers", "monitor" };
        var present = new HashSet<string> { "speakers", "monitor" };

        // User drags monitor above speakers, with the divider still after the first visible device.
        var (order, switchCount) = DeviceOrderReconciler.MergeVisibleOrder(
            full, fullSwitchCount: 2, present, visibleOrder: new[] { "monitor", "speakers" }, visibleSwitchCount: 1);

        Assert.Equal(new[] { "headset", "monitor", "speakers" }, order);
        Assert.Equal(2, switchCount);
    }

    [Fact]
    public void MergeVisibleOrder_MovingDividerPastAbsentDevice_LeavesAbsentDeviceOutOfSwitchList()
    {
        var full = new List<string> { "speakers", "headset", "monitor" };
        var present = new HashSet<string> { "speakers", "monitor" };

        // Divider was at 1 (only speakers in the switch-list); user moves it below monitor.
        var (order, switchCount) = DeviceOrderReconciler.MergeVisibleOrder(
            full, fullSwitchCount: 1, present, visibleOrder: new[] { "speakers", "monitor" }, visibleSwitchCount: 2);

        // The divider and monitor take the present slots; headset keeps its slot below the divider
        // since the user never moved it into the switch-list.
        Assert.Equal(new[] { "speakers", "monitor", "headset" }, order);
        Assert.Equal(2, switchCount);
    }

    [Fact]
    public void VisibleSwitchCount_CountsOnlyPresentDevicesAboveTheDivider()
    {
        var full = new List<string> { "headset", "speakers", "monitor" };
        var present = new HashSet<string> { "speakers", "monitor" };

        Assert.Equal(1, DeviceOrderReconciler.VisibleSwitchCount(full, fullSwitchCount: 2, present));
    }
}
