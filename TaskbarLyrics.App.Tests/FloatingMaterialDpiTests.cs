using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(1.5, 1)]
    [InlineData(1, 1.5)]
    [InlineData(1.25, 2)]
    [InlineData(2, 1.25)]
    public void MaterialDpiNotificationPreservesExplicitPhysicalBounds(double oldScale, double newScale)
    {
        RunSta(() =>
        {
            using var compositor = new FloatingMaterialCompositor();
            using var material = new FloatingMaterialWindow(compositor);
            var owner = CreateOwner();
            try
            {
                var card = new Rect(100, 200, 360 * newScale, 124 * newScale);
                material.Position(card, newScale, new WindowInteropHelper(owner).Handle);
                var source = HwndSource.FromHwnd(material.Handle);
                // WPF receives this suggestion after Position has already supplied target-screen pixels.
                var suggested = new Rect(card.Location, new Size(card.Width * newScale / oldScale,
                    card.Height * newScale / oldScale));
                var notification = (HwndDpiChangedEventArgs)Activator.CreateInstance(typeof(HwndDpiChangedEventArgs),
                    BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                    [new DpiScale(oldScale, oldScale), new DpiScale(newScale, newScale), suggested], culture: null)!;
                typeof(HwndSource).GetMethod("OnDpiChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(source, [notification]);
                PumpMessages();

                Assert.True(GetMaterialWindowRect(material.Handle, out var actual));
                Assert.Equal(card, new Rect(actual.Left, actual.Top,
                    actual.Right - actual.Left, actual.Bottom - actual.Top));
            }
            finally { owner.Close(); }
        });
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMaterialWindowRect(IntPtr window, out TaskbarNativeMethods.NativeRect bounds);
}
