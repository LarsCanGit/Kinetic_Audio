using System.Runtime.InteropServices;

namespace GroupedAudioMixer.Core.Audio.Interop;

/// <summary>
/// Raw Windows Core Audio COM interop declarations (mmdeviceapi.h / audiopolicy.h). Hand-rolled
/// because the project takes no COM-interop helper dependency; vtable order must match the
/// Windows SDK headers exactly, so nothing here is reordered or trimmed even when a member goes
/// unused. This file has no logic of its own (binding decision D5) and is not unit-tested; it is
/// covered by the D6 manual/E2E regression pass, same as the adapter that consumes it.
/// </summary>
internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll,
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications,
}

internal enum AudioSessionState
{
    AudioSessionStateInactive,
    AudioSessionStateActive,
    AudioSessionStateExpired,
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr devices);

    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);

    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    int RegisterEndpointNotificationCallback(IntPtr client);

    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

/// <summary>
/// Trimmed PROPVARIANT: only <c>vt</c> and the pointer-sized union slot are read, which is all
/// <c>PKEY_Device_FriendlyName</c> (VT_LPWSTR) needs; other variant types are not interpreted.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pointerValue;
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out uint count);

    int GetAt(uint index, out PROPERTYKEY key);

    int GetValue([In] ref PROPERTYKEY key, out PROPVARIANT value);

    int SetValue([In] ref PROPERTYKEY key, [In] ref PROPVARIANT value);

    int Commit();
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(
        [In] ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    int OpenPropertyStore(uint stgmAccess, out IntPtr propertyStore);

    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    int GetState(out uint state);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out int count);

    int Item(int index, out IMMDevice device);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int GetAudioSessionControl([In] ref Guid audioSessionGuid, int streamFlags, out IAudioSessionControl sessionControl);

    int GetSimpleAudioVolume([In] ref Guid audioSessionGuid, int streamFlags, out ISimpleAudioVolume audioVolume);

    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    int RegisterSessionNotification(IAudioSessionNotification sessionNotification);

    int UnregisterSessionNotification(IAudioSessionNotification sessionNotification);

    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

    int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    int GetCount(out int count);

    int GetSession(int sessionIndex, out IAudioSessionControl session);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    int GetState(out AudioSessionState state);

    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, [In] ref Guid eventContext);

    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, [In] ref Guid eventContext);

    int GetGroupingParam(out Guid groupingParam);

    int SetGroupingParam([In] ref Guid groupingParam, [In] ref Guid eventContext);

    int RegisterAudioSessionNotification(IAudioSessionEvents client);

    int UnregisterAudioSessionNotification(IAudioSessionEvents client);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl members, repeated so this interface can be QueryInterface'd
    // directly from IAudioSessionControl without losing vtable alignment.
    int GetState(out AudioSessionState state);

    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string displayName);

    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string displayName, [In] ref Guid eventContext);

    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string iconPath);

    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string iconPath, [In] ref Guid eventContext);

    int GetGroupingParam(out Guid groupingParam);

    int SetGroupingParam([In] ref Guid groupingParam, [In] ref Guid eventContext);

    int RegisterAudioSessionNotification(IAudioSessionEvents client);

    int UnregisterAudioSessionNotification(IAudioSessionEvents client);

    // IAudioSessionControl2 additions
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionId);

    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string sessionInstanceId);

    int GetProcessId(out uint processId);

    [PreserveSig]
    int IsSystemSoundsSession();

    int SetDuckingPreference(bool optOut);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    int SetMasterVolume(float level, [In] ref Guid eventContext);

    int GetMasterVolume(out float level);

    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [In] ref Guid eventContext);

    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport]
[Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig]
    int OnSessionCreated(IAudioSessionControl newSession);
}

[ComImport]
[Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEvents
{
    [PreserveSig]
    int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string displayName, [In] ref Guid eventContext);

    [PreserveSig]
    int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string iconPath, [In] ref Guid eventContext);

    [PreserveSig]
    int OnSimpleVolumeChanged(float volume, [MarshalAs(UnmanagedType.Bool)] bool mute, [In] ref Guid eventContext);

    [PreserveSig]
    int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumeArray, uint changedChannel, [In] ref Guid eventContext);

    [PreserveSig]
    int OnGroupingParamChanged([In] ref Guid groupingParam, [In] ref Guid eventContext);

    [PreserveSig]
    int OnStateChanged(AudioSessionState state);

    [PreserveSig]
    int OnSessionDisconnected(int disconnectReason);
}

/// <summary>
/// Undocumented interface used to change the default endpoint (<c>SetDefaultEndpoint</c>) since
/// Windows exposes no public API for it. This is the same mechanism SoundSwitch and similar
/// switcher tools use. The GUIDs and vtable order below are the community-established values for
/// Windows 7 and later; unused members are declared only to keep vtable slot order correct and are
/// never invoked, so their parameter marshaling is not exercised.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

    int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool useDefault, out IntPtr format);

    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

    int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool useDefault, out long defaultPeriod, out long minimumPeriod);

    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, long period);

    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr shareMode);

    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr shareMode);

    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, out IntPtr value);

    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);

    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);

    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClientComObject
{
}

internal static class NativeMethods
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PROPVARIANT pvar);
}
