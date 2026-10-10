using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class SystemMasterVolumeTests
{
    [Theory]
    [InlineData(typeof(SystemAudioSpectrumService))]
    [InlineData(typeof(AudioSessionProcessResolver))]
    public void ReadWorksWhenAnotherAudioFeatureCreatedTheDeviceEnumerator(Type owner)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            object? competingEnumerator = null;
            IntPtr device = IntPtr.Zero;
            try
            {
                // Reproduce the application's competing managed coclass for the same CLSID.
                var coclass = owner.GetNestedType("MMDeviceEnumerator", BindingFlags.NonPublic)!;
                competingEnumerator = Activator.CreateInstance(coclass, nonPublic: true);
                var enumerator = (ITestDeviceEnumerator)competingEnumerator!;
                var hasEndpoint = enumerator.GetDefaultAudioEndpoint(0, 1, out device) == 0;

                var available = SystemMasterVolume.TryRead(out var level, out _);

                Assert.Equal(hasEndpoint, available);
                Assert.InRange(level, 0, 100);
                // Repeated reads must not release the other feature's live wrapper.
                Assert.Equal(hasEndpoint, SystemMasterVolume.TryRead(out _, out _));
                Assert.Equal(hasEndpoint, enumerator.GetDefaultAudioEndpoint(0, 1, out var nextDevice) == 0);
                if (nextDevice != IntPtr.Zero) Marshal.Release(nextDevice);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (competingEnumerator is not null && Marshal.IsComObject(competingEnumerator))
                    Marshal.ReleaseComObject(competingEnumerator);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Audio endpoint read did not finish.");
        Assert.Null(failure);
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITestDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr device);
    }
}
