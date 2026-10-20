using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmbeddedSurfaceWithoutSpaceCancelsPanelPreparationAndRejectsLateClicks(bool useMirror)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TaskbarLyrics.Core.Services.TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            var display = DisplayMonitorService.GetDisplays().First(item => item.IsPrimary);
            var mirror = useMirror ? new LyricsMirrorWindow(display, (_, _, _, _, _, _) => { },
                composition, () => main.CloseControlPanel()) : null;
            var parent = new Window { Width = 1000, Height = 48, ShowInTaskbar = false, ShowActivated = false };
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            try
            {
                parent.Show();
                var source = (Window?)mirror ?? main;
                var sourceHandle = new WindowInteropHelper(source).EnsureHandle();
                var parentHandle = new WindowInteropHelper(parent).Handle;
                var anchor = (EmbeddedTaskbarAnchor)source.GetType().GetField("_embeddedTaskbarAnchor", flags)!.GetValue(source)!;
                anchor.Detach();
                SetTaskbarSpaceParent(sourceHandle, parentHandle);
                var style = TaskbarNativeMethods.GetWindowLongPtr(sourceHandle, -16).ToInt64();
                TaskbarNativeMethods.SetWindowLongPtr(sourceHandle, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
                var expanded = true;
                typeof(MainWindow).GetField("_setControlPanelOpen", flags)!.SetValue(main, new Action<bool>(value => expanded = value));
                typeof(LyricsControlPanelWindow).GetProperty("IsOpening", flags)!.SetValue(panel, true);

                Assert.True(anchor.Position(sourceHandle, parentHandle, source,
                    new AppSettings { UseFloatingWindow = false }, display));
                Assert.True(anchor.IsSurfaceHidden);
                Assert.False(panel.IsOpening);
                Assert.False(expanded);
                Assert.Equal(0, FloatingLyricsPanelController.GetBounds(source).Width);

                main.ToggleControlPanelNear(new Rect(100, 100, 34, 34), display, value => expanded = value, source);
                Assert.False(panel.IsOpening);
                Assert.False(expanded);
                anchor.Detach();
                Assert.False(anchor.IsSurfaceHidden);
            }
            finally
            {
                mirror?.Close();
                panel.Close();
                main.Close();
                parent.Close();
            }
        });
    }

    [DllImport("user32.dll", EntryPoint = "SetParent")]
    private static extern IntPtr SetTaskbarSpaceParent(IntPtr child, IntPtr parent);
}
