namespace KineticAudio.Core.Settings;

/// <summary>Merges a persisted device order against the devices actually present on the system.
/// Endpoint IDs can change on driver update/USB replug, so stale saved IDs are dropped from the
/// effective order and newly-seen devices are appended to the bottom rather than silently
/// discarded.</summary>
public static class DeviceOrderReconciler
{
    public static List<string> Reconcile(IReadOnlyList<string> storedOrder, IReadOnlyList<string> currentDeviceIds)
    {
        var currentSet = new HashSet<string>(currentDeviceIds, StringComparer.OrdinalIgnoreCase);
        var result = storedOrder.Where(id => currentSet.Contains(id)).ToList();

        var known = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
        result.AddRange(currentDeviceIds.Where(id => !known.Contains(id)));

        return result;
    }
}
