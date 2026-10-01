using System.Runtime.InteropServices;

namespace TaskbarLyrics.App;

internal static class AudioSessionProcessResolver
{
    private const int AudioSessionStateActive = 1;
    private const int RenderDataFlow = 0;
    private const int MultimediaRole = 1;
    private const int ClassContextAll = 0x17;

    private static readonly Guid AudioSessionManager2InterfaceId = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    public static string? TryResolveSingleActiveSessionExecutablePath() =>
        SelectSingleSessionExecutablePath(
            EnumerateActiveSessionProcessIds(),
            ProcessExecutablePathResolver.TryResolveByProcessId);

    internal static string? SelectSingleSessionExecutablePath(
        IReadOnlyList<int> activeProcessIds,
        Func<int, string?> resolveExecutablePath)
    {
        var distinctProcessIds = activeProcessIds.Distinct().ToArray();
        return distinctProcessIds.Length == 1 ? resolveExecutablePath(distinctProcessIds[0]) : null;
    }

    internal static IReadOnlyList<int> EnumerateActiveSessionProcessIds()
    {
        var processIds = new List<int>();
        IMMDeviceEnumerator? deviceEnumerator = null;
        IMMDevice? device = null;
        IAudioSessionManager2? sessionManager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        try
        {
            deviceEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumerator();
            if (deviceEnumerator.GetDefaultAudioEndpoint(RenderDataFlow, MultimediaRole, out device) != 0 ||
                device is null)
            {
                return processIds;
            }

            var interfaceId = AudioSessionManager2InterfaceId;
            if (device.Activate(ref interfaceId, ClassContextAll, IntPtr.Zero, out var sessionManagerObject) != 0 ||
                sessionManagerObject is not IAudioSessionManager2 manager ||
                manager.GetSessionEnumerator(out sessionEnumerator) != 0 ||
                sessionEnumerator is null ||
                sessionEnumerator.GetCount(out var sessionCount) != 0)
            {
                return processIds;
            }

            for (var index = 0; index < sessionCount; index++)
            {
                IAudioSessionControl2? sessionControl = null;
                try
                {
                    if (sessionEnumerator.GetSession(index, out sessionControl) != 0 ||
                        sessionControl is null ||
                        sessionControl.GetState(out var state) != 0 ||
                        state != AudioSessionStateActive ||
                        sessionControl.GetProcessId(out var processId) != 0 ||
                        processId <= 0)
                    {
                        continue;
                    }

                    processIds.Add(processId);
                }
                finally
                {
                    ReleaseComObject(sessionControl);
                }
            }
        }
        catch (Exception)
        {
            processIds.Clear();
        }
        finally
        {
            ReleaseComObject(sessionEnumerator);
            ReleaseComObject(sessionManager);
            ReleaseComObject(device);
            ReleaseComObject(deviceEnumerator);
        }

        return processIds;
    }

    private static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid interfaceId, int classContext, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
        int OpenPropertyStore(int access, out IntPtr properties);
        int GetId(out IntPtr id);
        int GetState(out int state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr audioVolume);
        int GetSessionEnumerator(out IAudioSessionEnumerator? sessionEnumerator);
        int RegisterSessionNotification(IntPtr notification);
        int UnregisterSessionNotification(IntPtr notification);
        int RegisterDuckNotification(IntPtr sessionId, IntPtr notification);
        int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int sessionCount);
        int GetSession(int sessionIndex, out IAudioSessionControl2? session);
    }

    // 必须扁平声明全部 14 个方法：继承式 [ComImport] 声明不会展开父接口方法，导致 GetProcessId 落到错误的 vtable 槽位。
    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        int GetState(out int state);
        int GetDisplayName(out IntPtr displayName);
        int SetDisplayName(string displayName, IntPtr eventContext);
        int GetIconPath(out IntPtr iconPath);
        int SetIconPath(string iconPath, IntPtr eventContext);
        int GetGroupingParam(out Guid groupingParam);
        int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
        int RegisterAudioSessionNotification(IntPtr notification);
        int UnregisterAudioSessionNotification(IntPtr notification);
        int GetSessionIdentifier(out IntPtr sessionIdentifier);
        int GetSessionInstanceIdentifier(out IntPtr sessionInstanceIdentifier);
        int GetProcessId(out int processId);
        int IsSystemSoundsSession();
        int SetDuckingPreference(bool optOut);
    }
}
