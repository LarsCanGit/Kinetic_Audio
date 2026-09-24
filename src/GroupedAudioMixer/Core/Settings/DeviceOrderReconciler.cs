namespace KineticAudio.Core.Settings;

/// <summary>Merges a persisted device order against the devices actually present on the system.
/// Devices that are currently absent (unplugged, powered off) keep their saved slot so they return
/// to the same position when reconnected; newly-seen devices are appended to the bottom rather than
/// silently discarded. The Settings UI only shows present devices, so
/// <see cref="VisibleSwitchCount"/> and <see cref="MergeVisibleOrder"/> translate between the full
/// persisted order and that visible subset without losing the absent devices' slots.</summary>
public static class DeviceOrderReconciler
{
    public static List<string> Reconcile(IReadOnlyList<string> storedOrder, IReadOnlyList<string> currentDeviceIds)
    {
        var result = storedOrder.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var known = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        result.AddRange(currentDeviceIds.Where(id => known.Add(id)));

        return result;
    }

    /// <summary>How many present devices sit above the divider, given the divider position in the
    /// full order (<paramref name="fullSwitchCount"/> entries, absent ones included).</summary>
    public static int VisibleSwitchCount(IReadOnlyList<string> fullOrder, int fullSwitchCount, IReadOnlySet<string> presentIds) =>
        fullOrder.Take(fullSwitchCount).Count(presentIds.Contains);

    /// <summary>Writes a reordered visible list (present devices plus the divider position) back
    /// into the full order. Absent devices keep their slot on the side of the divider they were
    /// saved on (the user can't see them, so they never moved them); the present devices fill the
    /// remaining slots of each side in their new visible order.</summary>
    public static (List<string> Order, int SwitchCount) MergeVisibleOrder(
        IReadOnlyList<string> fullOrder,
        int fullSwitchCount,
        IReadOnlySet<string> presentIds,
        IReadOnlyList<string> visibleOrder,
        int visibleSwitchCount)
    {
        fullSwitchCount = Math.Clamp(fullSwitchCount, 0, fullOrder.Count);
        visibleSwitchCount = Math.Clamp(visibleSwitchCount, 0, visibleOrder.Count);

        var above = MergeSide(fullOrder.Take(fullSwitchCount), visibleOrder.Take(visibleSwitchCount), presentIds);
        var below = MergeSide(fullOrder.Skip(fullSwitchCount), visibleOrder.Skip(visibleSwitchCount), presentIds);

        return ([.. above, .. below], above.Count);
    }

    private static List<string> MergeSide(IEnumerable<string> savedSide, IEnumerable<string> visibleSide, IReadOnlySet<string> presentIds)
    {
        var incoming = new Queue<string>(visibleSide);
        var result = new List<string>();
        foreach (var id in savedSide)
        {
            if (!presentIds.Contains(id))
                result.Add(id);
            else if (incoming.Count > 0)
                result.Add(incoming.Dequeue());
            // else: this present device moved to the other side; its slot is dropped.
        }

        // Devices moved in from the other side of the divider.
        result.AddRange(incoming);
        return result;
    }
}
