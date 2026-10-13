using System.Runtime.InteropServices;
using KineticAudio.Core.Audio.Interop;

namespace KineticAudio.Core.Devices;

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
    private readonly DefaultDeviceNotificationSink _notificationSink;

    public event EventHandler<AudioDeviceKind>? DefaultDeviceChanged;

    public CoreAudioDeviceProvider()
    {
        _deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _notificationSink = new DefaultDeviceNotificationSink(this);
        ThrowIfFailed(_deviceEnumerator.RegisterEndpointNotificationCallback(_notificationSink));
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

    // PKEY_Device_FriendlyName {a45c254e-df1c-4efd-8020-67d146a850e0}, pid 14.
    private static readonly PROPERTYKEY PKeyDeviceFriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid = 14,
    };

    private const ushort VT_LPWSTR = 31;

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

            try
            {
                var propertyStore = (IPropertyStore)Marshal.GetObjectForIUnknown(propertyStorePtr);
                try
                {
                    var key = PKeyDeviceFriendlyName;
                    ThrowIfFailed(propertyStore.GetValue(ref key, out var value));
                    try
                    {
                        return value.vt == VT_LPWSTR && value.pointerValue != IntPtr.Zero
                            ? Marshal.PtrToStringUni(value.pointerValue)
                            : null;
                    }
                    finally
                    {
                        NativeMethods.PropVariantClear(ref value);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(propertyStore);
                }
            }
            finally
            {
                Marshal.Release(propertyStorePtr);
            }
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

    public void Dispose()
    {
        _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationSink);
        Marshal.ReleaseComObject(_deviceEnumerator);
    }

    /// <summary>
    /// Forwards default-device changes. Only the multimedia role is reported, matching
    /// <see cref="GetDefaultDeviceId"/>; <see cref="SetDefaultDevice"/> changes all three roles at
    /// once, so reporting every role would raise the event three times per switch.
    /// </summary>
    private sealed class DefaultDeviceNotificationSink(CoreAudioDeviceProvider owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            if (role == ERole.eMultimedia)
                owner.DefaultDeviceChanged?.Invoke(owner, flow == EDataFlow.eCapture ? AudioDeviceKind.Recording : AudioDeviceKind.Playback);
            return 0;
        }

        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
    }
}
