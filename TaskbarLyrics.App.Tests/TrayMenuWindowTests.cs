using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

/// <summary>
/// The floating-window input switch is a single two-way row, so its rendered state
/// has to collapse click-through and free dragging into exactly one active mode.
/// </summary>
public class TrayMenuWindowTests
{
    private const double FloatingMenuHeight = 322;
    private const double EmbeddedMenuHeight = 290;

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    public void FloatingInputSwitchShowsOnlyTheActiveMode(
        bool clickThrough,
        bool freeDrag,
        bool clickThroughActive,
        bool freeDragActive)
    {
        RunOnStaThread(() =>
        {
            var window = CreateWindow(isFloatingWindow: true, freeDrag, clickThrough);
            try
            {
                AssertSegmentState(window, "FloatingClickThroughSegment", "FloatingClickThroughLabel", clickThroughActive);
                AssertSegmentState(window, "FloatingFreeDragSegment", "FloatingFreeDragLabel", freeDragActive);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void FloatingInputRowCollapsesOutsideFloatingMode()
    {
        RunOnStaThread(() =>
        {
            var floating = CreateWindow(isFloatingWindow: true, freeDrag: true, clickThrough: false);
            var embedded = CreateWindow(isFloatingWindow: false, freeDrag: true, clickThrough: false);
            try
            {
                Assert.Equal(FloatingMenuHeight, floating.Height);
                Assert.Equal(Visibility.Visible, Find<Border>(floating, "FloatingInputModeSwitch").Visibility);
                Assert.Equal(32, Find<RowDefinition>(floating, "FloatingInputModeRowDefinition").Height.Value);

                Assert.Equal(EmbeddedMenuHeight, embedded.Height);
                Assert.Equal(Visibility.Collapsed, Find<Border>(embedded, "FloatingInputModeSwitch").Visibility);
                Assert.Equal(0, Find<RowDefinition>(embedded, "FloatingInputModeRowDefinition").Height.Value);
            }
            finally
            {
                floating.Close();
                embedded.Close();
            }
        });
    }

    private static void AssertSegmentState(Window window, string segmentName, string labelName, bool isActive)
    {
        var segment = Find<Button>(window, segmentName);
        var label = Find<TextBlock>(window, labelName);

        Assert.Same(
            isActive ? window.FindResource("TrayMenuSwitchOnBrush") : Brushes.Transparent,
            segment.Background);
        Assert.Same(
            window.FindResource(isActive ? "TrayMenuSwitchOnTextBrush" : "TrayMenuMutedTextBrush"),
            label.Foreground);
    }

    private static T Find<T>(Window window, string name)
        where T : class =>
        Assert.IsType<T>(window.FindName(name));

    private static TrayMenuWindow CreateWindow(bool isFloatingWindow, bool freeDrag, bool clickThrough) =>
        new(
            () => { },
            string.Empty,
            () => { },
            string.Empty,
            () => { },
            string.Empty,
            _ => { },
            SpectrumDisplayMode.Disabled,
            () => { },
            () => { },
            () => { },
            () => { },
            () => { },
            new TrayMenuFloatingWindowState(isFloatingWindow, freeDrag, clickThrough, _ => { }, _ => { }));

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Tray menu window test did not finish.");
        Assert.Null(failure);
    }
}
