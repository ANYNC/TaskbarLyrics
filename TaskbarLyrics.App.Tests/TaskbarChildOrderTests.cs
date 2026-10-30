using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void TaskbarAvoidanceDisabledSkipsReadsAndReenablingRestoresAvoidance()
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: true);
            var reads = 0;
            using var provider = new TaskbarOccupiedSpaceProvider((_, bounds, _) =>
            {
                reads++;
                return [new(bounds.Left + 300, bounds.Right)];
            }, TimeProvider.System);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            var layout = new Window { Width = 420, Height = 44 };
            var settings = new AppSettings { TaskbarAvoidanceEnabled = false, XOffset = 80 };
            try
            {
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var configured);
                Assert.Equal(420, configured.Right - configured.Left);
                Assert.Equal(0, reads);
                settings.TaskbarAvoidanceEnabled = true;
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var avoided);
                Assert.Equal(292, avoided.Right - avoided.Left);
                settings.TaskbarAvoidanceEnabled = false;
                var before = reads;
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var restored);
                Assert.Equal(configured, restored);
                Assert.Equal(before, reads);
                Assert.Equal(native.Parent, GetAncestor(native.Lyrics, 1));
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Lyrics, 3));
            }
            finally { layout.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarAvoidanceUnknownAndFullSpaceUseDisabledBoundsAndRecover(bool throwRead)
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: true);
            var clock = new TaskbarSpaceClock();
            var phase = 0;
            using var provider = new TaskbarOccupiedSpaceProvider((_, bounds, _) => phase switch
            {
                0 when throwRead => throw new InvalidOperationException("Test UIA failure"),
                0 => null,
                2 => [new(bounds.Left, bounds.Right)],
                _ => [new(bounds.Left + 300, bounds.Right)]
            }, clock);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            var layout = new Window { Width = 420, Height = 44 };
            var settings = new AppSettings { TaskbarAvoidanceEnabled = false, XOffset = 80 };
            try
            {
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var configured);
                settings.TaskbarAvoidanceEnabled = true;
                Refresh(0);
                AssertBounds(configured);
                Refresh(1);
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var avoided);
                Assert.Equal(292, avoided.Right - avoided.Left);
                Refresh(2);
                AssertBounds(configured);
                Refresh(3);
                AssertBounds(avoided);
                Assert.Equal(420, settings.WindowWidth);
                Assert.Equal(80, settings.XOffset);
                Assert.True(settings.TaskbarAvoidanceEnabled);

                void Refresh(int next)
                {
                    phase = next;
                    clock.Advance();
                    Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                    Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                    Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                }
                void AssertBounds(TaskbarNativeMethods.NativeRect expected)
                {
                    ChildOrderWindows.GetWindowRect(native.Lyrics, out var actual);
                    Assert.Equal(expected, actual);
                    Assert.Equal(native.Parent, GetAncestor(native.Lyrics, 1));
                }
            }
            finally { layout.Close(); }
        });
    }

    [Fact]
    public void TaskbarAvoidanceBlockedReadFallsBackAfterCacheExpiryWithoutParallelQueries()
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: true);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var clock = new TaskbarSpaceClock();
            var reads = 0;
            using var provider = new TaskbarOccupiedSpaceProvider((_, bounds, token) =>
            {
                if (Interlocked.Increment(ref reads) == 2)
                {
                    entered.Set();
                    release.Wait(token);
                }
                return [new(bounds.Left + 300, bounds.Right)];
            }, clock);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            var layout = new Window { Width = 420, Height = 44 };
            var settings = new AppSettings { XOffset = 80 };
            try
            {
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var avoided);
                Assert.Equal(292, avoided.Right - avoided.Left);
                clock.Advance();
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                for (var pass = 0; pass < 5; pass++) clock.Advance();
                Assert.False(provider.PendingRead.IsCompleted);
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var fallback);
                Assert.Equal(420, fallback.Right - fallback.Left);
                settings.TaskbarAvoidanceEnabled = false;
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var disabled);
                Assert.Equal(disabled, fallback);
                Assert.Equal(2, reads);
                release.Set();
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                settings.TaskbarAvoidanceEnabled = true;
                clock.Advance();
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var recovered);
                Assert.Equal(avoided, recovered);
            }
            finally
            {
                release.Set();
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                layout.Close();
            }
        });
    }

    [Fact]
    public void TaskbarChildOrderRecoversAfterLateContentWithoutMovingOrActivating()
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: true);
            using var provider = new TaskbarOccupiedSpaceProvider((_, bounds, _) => [new TaskbarInterval(bounds.Left + 800, bounds.Right)], TimeProvider.System);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            var layout = new Window { Width = 420, Height = 44 };
            try
            {
                var active = ChildOrderWindows.GetForegroundWindow();
                var settings = new AppSettings { HorizontalAnchor = LyricsHorizontalAnchor.Left, WindowWidth = 420 };
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Lyrics, 3));
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Parent, 5));

                ChildOrderWindows.GetWindowRect(native.Lyrics, out var before);
                native.RecreateContent();
                Assert.Equal(native.Content, ChildOrderWindows.GetWindow(native.Lyrics, 3));
                // Geometry is already correct. The old early return skipped this repair.
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var after);
                Assert.Equal(before, after);
                Assert.Equal(native.Parent, GetAncestor(native.Lyrics, 1));
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Lyrics, 3));
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Parent, 5));
                Assert.Equal(active, ChildOrderWindows.GetForegroundWindow());
            }
            finally { layout.Close(); }
        });
    }

    [Fact]
    public void TaskbarChildOrderLeavesUnrelatedSiblingsAndWrongParentsAlone()
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: false);
            var before = ChildOrderWindows.GetWindow(native.Lyrics, 3);
            Assert.True(EmbeddedTaskbarAnchor.RestoreTaskbarChildOrder(native.Lyrics, native.Parent));
            Assert.Equal(before, ChildOrderWindows.GetWindow(native.Lyrics, 3));
            Assert.True(EmbeddedTaskbarAnchor.RestoreTaskbarChildOrder(native.Lyrics, native.Foreground));
            Assert.Equal(native.Parent, GetAncestor(native.Lyrics, 1));
            Assert.Equal(before, ChildOrderWindows.GetWindow(native.Lyrics, 3));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarChildOrderRestoresVisibleFallbackWithoutKnownFreeSpace(bool confirmedFull)
    {
        RunSta(() =>
        {
            using var native = new ChildOrderWindows(contentBridge: true);
            using var provider = new TaskbarOccupiedSpaceProvider((_, bounds, _) => [new TaskbarInterval(bounds.Left, bounds.Right)], TimeProvider.System);
            using var anchor = new EmbeddedTaskbarAnchor(provider);
            var layout = new Window { Width = 420, Height = 44 };
            try
            {
                var settings = new AppSettings { HorizontalAnchor = LyricsHorizontalAnchor.Left };
                Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                if (confirmedFull)
                {
                    Assert.True(provider.PendingRead.Wait(TimeSpan.FromSeconds(5)));
                    Assert.True(anchor.Position(native.Lyrics, native.Parent, layout, settings, null));
                }
                Assert.Equal(native.Foreground, ChildOrderWindows.GetWindow(native.Lyrics, 3));
                ChildOrderWindows.GetWindowRect(native.Lyrics, out var bounds);
                Assert.Equal(420, bounds.Right - bounds.Left);
                Assert.Equal(44, bounds.Bottom - bounds.Top);
            }
            finally { layout.Close(); }
        });
    }

    // Own all native windows and the local class registration; never alter Explorer.
    private sealed class ChildOrderWindows : IDisposable
    {
        private const string BridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";
        private static readonly WindowProcedure Procedure = DefWindowProc;
        private readonly IntPtr _module = GetModuleHandle(null);
        private readonly bool _contentBridge;
        private bool _registered;
        internal IntPtr Parent { get; private set; }
        internal IntPtr Lyrics { get; private set; }
        internal IntPtr Content { get; private set; }
        internal IntPtr Foreground { get; private set; }

        internal ChildOrderWindows(bool contentBridge)
        {
            _contentBridge = contentBridge;
            try
            {
                if (contentBridge)
                {
                    var definition = new WindowClass
                    {
                        Procedure = Marshal.GetFunctionPointerForDelegate(Procedure),
                        Instance = _module,
                        ClassName = BridgeClass
                    };
                    if (RegisterClass(ref definition) == 0) throw new Win32Exception();
                    _registered = true;
                }
                Parent = Create("Static", 0x90000000, -10000, -10000, 1000, 48, IntPtr.Zero);
                Lyrics = Create("Static", 0x50000000, 0, 2, 420, 44, Parent);
                Content = Create(contentBridge ? BridgeClass : "Static", 0x50000000, 0, 0, 1000, 48, Parent);
                Foreground = Create("Static", 0x50000000, 900, 0, 40, 48, Parent);
                Raise(Content);
                Raise(Foreground);
            }
            catch { Dispose(); throw; }
        }

        internal void RecreateContent()
        {
            Assert.True(DestroyWindow(Content));
            Content = Create(_contentBridge ? BridgeClass : "Static", 0x50000000, 0, 0, 1000, 48, Parent);
            Raise(Content);
            Raise(Foreground);
        }

        private static void Raise(IntPtr hwnd) =>
            Assert.True(TaskbarNativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                TaskbarNativeMethods.SWP_NOMOVE | TaskbarNativeMethods.SWP_NOSIZE | TaskbarNativeMethods.SWP_NOACTIVATE));

        private IntPtr Create(string className, uint style, int x, int y, int width, int height, IntPtr parent)
        {
            var hwnd = CreateWindowEx(0x08000080, className, "", style, x, y, width, height, parent,
                IntPtr.Zero, _module, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new Win32Exception();
            return hwnd;
        }

        public void Dispose()
        {
            if (Parent != IntPtr.Zero) { _ = DestroyWindow(Parent); Parent = IntPtr.Zero; }
            if (_registered) { _ = UnregisterClass(BridgeClass, _module); _registered = false; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Style;
            internal IntPtr Procedure;
            internal int ClassExtra, WindowExtra;
            internal IntPtr Instance, Icon, Cursor, Background;
            [MarshalAs(UnmanagedType.LPWStr)] internal string? MenuName;
            [MarshalAs(UnmanagedType.LPWStr)] internal string ClassName;
        }
        private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass definition);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out TaskbarNativeMethods.NativeRect rect);
    }
}
