using System.Runtime.InteropServices;
using System.Windows;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeTaskbarQueryReadsVisibleInteractiveHostsAndDescendantsInPhysicalCoordinates(bool vertical)
    {
        RunSta(() =>
        {
            const uint visible = 0x10000000;
            const uint child = 0x40000000;
            var root = CreateAutomationTestWindow("Static", visible | 0x80000000, 100, 100,
                vertical ? 120 : 450, vertical ? 450 : 140, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, root);
            try
            {
                var host = CreateAutomationTestWindow("Static", visible | child, 5, 5, 100, 80, root);
                var nested = CreateAutomationTestWindow("Button", visible | child, 10, 10, 70, 25, host);
                var direct = CreateAutomationTestWindow("Button", visible | child | 2, 20, 100, 70, 25, root);
                var hidden = CreateAutomationTestWindow("Button", child, 90, 80, 25, 25, root);
                Assert.NotEqual(IntPtr.Zero, nested);
                Assert.NotEqual(IntPtr.Zero, direct);
                Assert.NotEqual(IntPtr.Zero, hidden);
                Assert.True(GetAutomationTestWindowRect(root, out var bounds));
                Assert.True(GetAutomationTestWindowRect(nested, out var nestedBounds));
                Assert.True(GetAutomationTestWindowRect(direct, out var directBounds));
                var queryBounds = new Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
                var query = Task.Run(() => NativeTaskbarAutomation.ReadOccupiedBounds(root, queryBounds, CancellationToken.None));
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!query.IsCompleted && DateTime.UtcNow < deadline) PumpMessages(TimeSpan.FromMilliseconds(10));
                Assert.True(query.IsCompleted, "Native UIA query did not complete.");
                var result = query.GetAwaiter().GetResult();
                Assert.NotNull(result);
                Assert.Equal(2, result.Length);
                Assert.Contains(Interval(nestedBounds), result);
                Assert.Contains(Interval(directBounds), result);

                TaskbarInterval Interval(AutomationTestRect rect) => vertical
                    ? new TaskbarInterval(rect.Top, rect.Bottom)
                    : new TaskbarInterval(rect.Left, rect.Right);
            }
            finally { DestroyAutomationTestWindow(root); }
        });
    }

    [Fact]
    public void NativeTaskbarQueryHonorsCancellationBeforeCreatingComObjects()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => NativeTaskbarAutomation.ReadOccupiedBounds(
            new IntPtr(42), new Rect(0, 0, 1000, 48), stop.Token));
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateAutomationTestWindow(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    private static IntPtr CreateAutomationTestWindow(string className, uint style, int x, int y, int width, int height, IntPtr parent) =>
        CreateAutomationTestWindow(0, className, string.Empty, style, x, y, width, height, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyAutomationTestWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAutomationTestWindowRect(IntPtr window, out AutomationTestRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct AutomationTestRect { public int Left, Top, Right, Bottom; }
}
