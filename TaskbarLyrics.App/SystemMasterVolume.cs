using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class SystemMasterVolume
{
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly Guid EventContext = new("73F989F3-78D2-4FF6-9953-DC4BA516F75F");

    internal static bool TryRead(out int level, out bool muted)
    {
        var resultLevel = 0;
        var resultMuted = false;
        var success = WithEndpoint(endpoint =>
        {
            if (endpoint.GetMasterVolumeLevelScalar(out var scalar) != 0 ||
                endpoint.GetMute(out resultMuted) != 0)
            {
                return false;
            }

            resultLevel = (int)Math.Round(Math.Clamp(scalar, 0, 1) * 100);
            return true;
        });
        level = resultLevel;
        muted = resultMuted;
        return success;
    }

    internal static bool TrySetLevel(int level) => WithEndpoint(endpoint =>
    {
        var context = EventContext;
        return endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 100) / 100f, ref context) == 0;
    });

    internal static bool TryToggleMute() => WithEndpoint(endpoint =>
    {
        var context = EventContext;
        return endpoint.GetMute(out var muted) == 0 && endpoint.SetMute(!muted, ref context) == 0;
    });

    private static bool WithEndpoint(Func<IAudioEndpointVolume, bool> operation)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? endpoint = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device is null)
            {
                return false;
            }

            var interfaceId = EndpointVolumeId;
            if (device.Activate(ref interfaceId, 0x17, IntPtr.Zero, out var endpointObject) != 0 ||
                endpointObject is not IAudioEndpointVolume volume)
            {
                return false;
            }

            endpoint = volume;
            return operation(endpoint);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Release(endpoint);
            Release(device);
            Release(enumerator);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid interfaceId, int context, IntPtr parameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId(out IntPtr id);
        [PreserveSig] int GetState(out int state);
    }

    // IAudioEndpointVolume 的 vtable 必须按 Windows SDK 的完整方法顺序声明。
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }
}
