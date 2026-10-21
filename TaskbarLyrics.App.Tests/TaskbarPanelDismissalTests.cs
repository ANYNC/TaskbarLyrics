using System.Reflection;
using System.Windows;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void TaskbarPanelReopeningFromPartialOpacityDoesNotFlashTheEndpoint()
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var hidden = 0;
            panel.Hidden += (_, _) => hidden++;
            try
            {
                ShowNativePanel(panel);
                var visual = (FrameworkElement)panel.FindName("VisualRoot");
                PumpMessages(TimeSpan.FromMilliseconds(250));
                panel.HidePanel();
                Assert.False(visual.IsHitTestVisible);
                PumpMessages(TimeSpan.FromMilliseconds(60));
                if (FloatingPanelAnimation.IsEnabled) Assert.InRange(visual.Opacity, 0.001, 0.60);
                var hiddenBeforeReopening = hidden;
                ShowNativePanel(panel);
                if (FloatingPanelAnimation.IsEnabled) Assert.InRange(visual.Opacity, 0, 0.95);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.True(panel.IsVisible);
                Assert.False(panel.IsClosing);
                Assert.True(visual.IsHitTestVisible);
                Assert.Equal(1, visual.Opacity);
                Assert.Equal(hiddenBeforeReopening, hidden);
            }
            finally { panel.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarPanelPartialFadeFinishesOnceAndLeavesNoBackground(bool immediate)
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var hidden = 0;
            panel.Hidden += (_, _) => hidden++;
            try
            {
                ShowNativePanel(panel);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                var visual = (FrameworkElement)panel.FindName("VisualRoot");
                var material = Assert.IsAssignableFrom<IFloatingPanelMaterial>(GetStartupMaterial(panel));
                panel.HidePanel();
                PumpMessages(TimeSpan.FromMilliseconds(60));
                if (immediate) panel.HidePanel(immediate: true);
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.False(panel.IsVisible);
                Assert.False(panel.IsClosing);
                Assert.Equal(0, visual.Opacity);
                Assert.False(IsWindowVisible(material.Handle));
                Assert.Equal(1, hidden);
            }
            finally { panel.Close(); }
        });
    }

    [Fact]
    public void ClosingTaskbarPanelCancelsItsPendingFadeCompletion()
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var hidden = 0;
            panel.Hidden += (_, _) => hidden++;
            try
            {
                ShowNativePanel(panel);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                panel.HidePanel();
                panel.Close();
                var hiddenWhenClosed = hidden;
                PumpMessages(TimeSpan.FromMilliseconds(300));
                Assert.Equal(hiddenWhenClosed, hidden);
                Assert.False(panel.IsVisible);
            }
            finally { panel.Dispose(); }
        });
    }

    [Fact]
    public void TaskbarPanelReopeningCancelsTheOldDismissalCompletion()
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var hidingCount = 0;
            var hiddenCount = 0;
            panel.Hiding += (_, _) => hidingCount++;
            panel.Hidden += (_, _) => hiddenCount++;
            try
            {
                ShowNativePanel(panel);
                panel.HidePanel();
                Assert.Equal(1, hidingCount);
                var completedBeforeReopen = hiddenCount;
                ShowNativePanel(panel);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                Assert.True(panel.IsVisible);
                Assert.False(panel.IsClosing);
                Assert.Equal(completedBeforeReopen, hiddenCount);
                panel.HidePanel(immediate: true);
                Assert.Equal(2, hidingCount);
                Assert.Equal(completedBeforeReopen + 1, hiddenCount);
            }
            finally { panel.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarPanelClearsSourceAffordanceWhenDismissalStarts(bool immediate)
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            var sourceStates = new List<bool>();
            var expandedField = typeof(MainWindow).GetField("_setControlPanelOpen", flags)!;
            var events = new List<string>();
            panel.Hiding += (_, _) =>
            {
                events.Add("hiding");
                Assert.True(panel.IsVisible);
                Assert.True(panel.IsClosing);
                Assert.False(Assert.Single(sourceStates));
            };
            panel.Hidden += (_, _) => events.Add("hidden");
            try
            {
                ShowNativePanel(panel);
                expandedField.SetValue(main, (Action<bool>)sourceStates.Add);
                panel.HidePanel(immediate);
                Assert.False(Assert.Single(sourceStates));
                Assert.Null(expandedField.GetValue(main));
                Assert.Equal("hiding", events[0]);
                panel.HidePanel(immediate);
                PumpMessages(TimeSpan.FromMilliseconds(250));
                Assert.Collection(events,
                    item => Assert.Equal("hiding", item),
                    item => Assert.Equal("hidden", item));
                Assert.False(panel.IsVisible);
            }
            finally { main.Close(); }
        });
    }
}
