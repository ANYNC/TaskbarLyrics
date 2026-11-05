using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void DetachingRetiringWindowKeepsItHiddenThroughReparentAndStyleRestore(bool savedStyles, bool dispose)
    {
        RunSta(() =>
        {
            var parent = CreateTaskbarSpaceWindow();
            var child = CreateTaskbarSpaceWindow();
            using var anchor = new EmbeddedTaskbarAnchor();
            try
            {
                var handle = EmbedForRetirement(anchor, child, parent, savedStyles);
                var exposed = false;
                HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message is 0x0047 or 0x007D &&
                        (TaskbarNativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64() & 0x40000000L) == 0 && IsRetiringWindowVisible(hwnd))
                        exposed = true;
                    return IntPtr.Zero;
                };
                var source = HwndSource.FromHwnd(handle);
                source.AddHook(hook);
                try
                {
                    if (dispose) anchor.Dispose();
                    else anchor.Detach(keepHidden: true);
                    PumpMessages(TimeSpan.FromMilliseconds(100));
                    // A WPF top-level popup may have an invisible owner HWND;
                    // GetParent includes owners, so check the child style as well.
                    Assert.NotEqual(new WindowInteropHelper(parent).Handle, GetRetiringWindowParent(handle));
                    Assert.Equal(0, TaskbarNativeMethods.GetWindowLongPtr(handle, -16).ToInt64() & 0x40000000L);
                    Assert.False(IsRetiringWindowVisible(handle));
                    Assert.Equal(0, TaskbarNativeMethods.GetWindowLongPtr(handle, -16).ToInt64() & 0x10000000L);
                    Assert.False(exposed, "The retiring HWND became visible while restoring top-level styles.");
                    Assert.False(anchor.IsAttached);
                }
                finally { source.RemoveHook(hook); }
            }
            finally { child.Close(); parent.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LyricsRecreationHidesOldMainAndMirrorBeforeNotifyingHost(bool mirror)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            Window window = mirror
                ? new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { }, composition)
                : new MainWindow(offsets, composition, () => { }, () => { });
            var type = window.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            window.Loaded -= type.GetMethod("OnLoaded", flags)!.CreateDelegate<RoutedEventHandler>(window);
            var settings = new AppSettings { UseFloatingWindow = true };
            if (window is MainWindow floatingMain)
            {
                floatingMain.ApplySettings(settings);
                floatingMain.SetDisplayMonitor(display);
            }
            else ((LyricsMirrorWindow)window).ApplySettings(settings);
            window.Left = 700;
            window.Top = 700;
            window.Show();
            var parent = CreateTaskbarSpaceWindow();
            var anchor = (EmbeddedTaskbarAnchor)type.GetField("_embeddedTaskbarAnchor", flags)!.GetValue(window)!;
            try
            {
                var handle = EmbedForRetirement(anchor, window, parent, savedStyles: true);
                var requested = 0;
                void OnRequested(object? sender, EventArgs args)
                {
                    requested++;
                    Assert.False(window.IsVisible);
                    Assert.False(IsRetiringWindowVisible(handle));
                    Assert.Equal(IntPtr.Zero, GetRetiringWindowParent(handle));
                }
                if (window is MainWindow main) main.RecreateWindowRequested += OnRequested;
                else ((LyricsMirrorWindow)window).RecreateWindowRequested += OnRequested;
                var request = type.GetMethod("OnEmbeddedAttachmentLost", flags)!;
                request.Invoke(window, [window, EventArgs.Empty]);
                request.Invoke(window, [window, EventArgs.Empty]);
                PumpMessages(TimeSpan.FromMilliseconds(100));
                Assert.Equal(1, requested);
                Assert.False(window.IsVisible);
                Assert.False(IsRetiringWindowVisible(handle));
            }
            finally { window.Close(); parent.Close(); }
        });
    }

    private static IntPtr EmbedForRetirement(EmbeddedTaskbarAnchor anchor, Window child, Window parent, bool savedStyles)
    {
        var handle = new WindowInteropHelper(child).Handle;
        var parentHandle = new WindowInteropHelper(parent).Handle;
        var style = TaskbarNativeMethods.GetWindowLongPtr(handle, -16).ToInt64();
        var extendedStyle = TaskbarNativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
        SetTaskbarSpaceParent(handle, parentHandle);
        TaskbarNativeMethods.SetWindowLongPtr(handle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L | 0x10000000L));
        TaskbarNativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 2, 420, 44,
            TaskbarNativeMethods.SWP_NOZORDER | TaskbarNativeMethods.SWP_NOACTIVATE);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var pair in new Dictionary<string, object>
        {
            ["_windowHandle"] = handle, ["_parentHandle"] = parentHandle,
            ["_hasOriginalStyles"] = savedStyles, ["_originalStyle"] = style | 0x10000000L,
            ["_originalExtendedStyle"] = extendedStyle
        }) typeof(EmbeddedTaskbarAnchor).GetField(pair.Key, flags)!.SetValue(anchor, pair.Value);
        Assert.True(IsRetiringWindowVisible(handle));
        return handle;
    }

    [DllImport("user32.dll", EntryPoint = "GetParent")]
    private static extern IntPtr GetRetiringWindowParent(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsRetiringWindowVisible(IntPtr hwnd);
}
