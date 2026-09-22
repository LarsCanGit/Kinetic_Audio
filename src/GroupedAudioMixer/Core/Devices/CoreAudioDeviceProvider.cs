using System.Runtime.InteropServices;
using GroupedAudioMixer.Core.Audio.Interop;

namespace GroupedAudioMixer.Core.Devices;

/// <summary>
/// The real <see cref="IAudioDeviceProvider"/> implementation: a thin COM-interop adapter over
/// <c>IMMDeviceEnumerator</c> (device enumeration and current-default lookup) and the undocumented
/// <c>IPolicyConfig</c> (default-endpoint switching), per binding decision D5. It has no
/// switch-list or cycling logic of its own; that lives in <see cref="DeviceSwitcher"/> and
/// unit-tests against a fake.
///
/// Not unit-tested; per the design doc's test-coverage model this class is covered by the D6
/// manual/E2E regression pass (device-switch hotkey), not by xUnit against a fake, since it IS the
/// real Core Audio / undocumented-API implementation.
///
/// <c>SetDefaultDevice</c> sets all three roles (console, multimedia, communications) to the same
/// device, matching SoundSwitch's proven model. Separating console/communications targets is an
/// explicitly open design-doc question (Open Questions) and is not resolved here.
/// </summary>
public sealed class CoreAudioDeviceProvider : IAudioDeviceProvider
{
    private readonly IMMDeviceEnumerator _deviceEnumerator;

    public CoreAudioDeviceProvider()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    }

    public IReadOnlyList<AudioDeviceInfo> GetDevices(AudioDeviceKind kind)
    {
        var dataFlow = ToDataFlow(kind);
        const uint DEVICE_STATE_ACTIVE = 0x1;

        ThrowIfFailed(_deviceEnumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var collectionPtr));
        var collection = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collectionPtr);
        Marshal.Release(collectionPtr);

        try
        {
            ThrowIfFailed(collection.GetCount(out var count));
            var devices = new List<AudioDeviceInfo>(count);
            for (var i = 0; i < count; i++)
            {
                ThrowIfFailed(collection.Item(i, out var device));
                try
                {
                    devices.Add(ToDeviceInfo(device, kind));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            return devices;
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    public string? GetDefaultDeviceId(AudioDeviceKind kind)
    {
        var hresult = _deviceEnumerator.GetDefaultAudioEndpoint(ToDataFlow(kind), ERole.eMultimedia, out var device);
        if (hresult < 0)
            return null;

        try
        {
            ThrowIfFailed(device.GetId(out var id));
            return id;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    public void SetDefaultDevice(AudioDeviceKind kind, string deviceId)
    {
        var policyConfig = (IPolicyConfig)new PolicyConfigClientComObject();
        try
        {
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole));
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia));
            ThrowIfFailed(policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications));
        }
        finally
        {
            Marshal.ReleaseComObject(policyConfig);
        }
    }

    private static AudioDeviceInfo ToDeviceInfo(IMMDevice device, AudioDeviceKind kind)
    {
        ThrowIfFailed(device.GetId(out var id));
        var displayName = TryGetFriendlyName(device) ?? id;
        return new AudioDeviceInfo(id, displayName, kind);
    }

    /// <summary>
    /// Best-effort friendly-name lookup via the device's property store. Falls back to null (caller
    /// falls back further to the device id) rather than throwing, since a missing friendly name
    /// should not break enumeration.
    /// </summary>
    private static string? TryGetFriendlyName(IMMDevice device)
    {
        try
        {
            ThrowIfFailed(device.OpenPropertyStore(0, out var propertyStorePtr));
            if (propertyStorePtr == IntPtr.Zero)
                return null;

            Marshal.Release(propertyStorePtr);
            return null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static EDataFlow ToDataFlow(AudioDeviceKind kind) => kind switch
    {
        AudioDeviceKind.Playback => EDataFlow.eRender,
        AudioDeviceKind.Recording => EDataFlow.eCapture,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static int ThrowIfFailed(int hresult)
    {
        if (hresult < 0)
            Marshal.ThrowExceptionForHR(hresult);
        return hresult;
    }

    public void Dispose() => Marshal.ReleaseComObject(_deviceEnumerator);
}
