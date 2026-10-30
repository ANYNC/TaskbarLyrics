using System.Reflection;
using System.Windows.Interop;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void LostNativeAttachmentRequestsOneRecreationAndNeverReattachesTheOldWindow()
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var window = new MainWindow(offsets, composition, () => { }, () => { });
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var anchor = (EmbeddedTaskbarAnchor)typeof(MainWindow).GetField("_embeddedTaskbarAnchor", flags)!.GetValue(window)!;
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            // A valid old parent that is no longer the actual parent models Windows
            // moving/unparenting the child during display reconfiguration.
            using var oldParent = new HwndSource(new HwndSourceParameters("Old display taskbar")
            {
                WindowStyle = unchecked((int)0x80000000),
                Width = 0,
                Height = 0
            });
            typeof(EmbeddedTaskbarAnchor).GetField("_windowHandle", flags)!.SetValue(anchor, hwnd);
            typeof(EmbeddedTaskbarAnchor).GetField("_parentHandle", flags)!.SetValue(anchor, oldParent.Handle);
            typeof(EmbeddedTaskbarAnchor).GetField("_taskbarHandle", flags)!.SetValue(anchor, oldParent.Handle);
            typeof(EmbeddedTaskbarAnchor).GetField("_attachedDisplayTarget", flags)!.SetValue(anchor, EmbeddedTaskbarDisplayTarget.Create(null));
            var requests = 0;
            window.RecreateWindowRequested += (_, _) => requests++;
            try
            {
                var refresh = typeof(EmbeddedTaskbarAnchor).GetMethod("RefreshAvailableSpace", flags)!.CreateDelegate<Action>(anchor);
                refresh();
                refresh();
                typeof(MainWindow).GetMethod("AnchorToTaskbar", flags)!.Invoke(window, null);
                Assert.Equal(1, requests);
                Assert.False(anchor.IsAttached);
                Assert.NotEqual(oldParent.Handle, GetAncestor(hwnd, 1)); // GA_PARENT
            }
            finally { window.CloseFromHost(); }
        });
    }
}
