using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterialCommitKeepsNativeWindowHiddenUntilExplicitShow(bool light)
    {
        RunSta(() =>
        {
            using var compositor = new FloatingMaterialCompositor();
            using var material = new FloatingMaterialWindow(compositor);
            var owner = CreateOwner();
            try
            {
                material.Configure(light, 0.5, 0.7, 24);
                material.Position(new Rect(100, 200, 360, 124), 1,
                    new System.Windows.Interop.WindowInteropHelper(owner).Handle);
                Assert.False(IsWindowVisible(material.Handle));
                CompleteStartupPanel(material.PrepareAsync(CancellationToken.None));
                Assert.False(IsWindowVisible(material.Handle));
                material.SetVisible(true, immediate: true);
                Assert.True(IsWindowVisible(material.Handle));
                material.SetVisible(false, immediate: true);
                Assert.False(IsWindowVisible(material.Handle));
            }
            finally { owner.Close(); }
        });
    }

    [Fact]
    public void TaskbarPanelWaitsForMaterialAndRechecksThemeBeforeShowing()
    {
        RunSta(() =>
        {
            NativeWindowTheme.SetMode(ToolWindowTheme.Light);
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var material = new DelayedStartupMaterial();
            SetStartupMaterial(panel, material);
            try
            {
                var opening = BeginStartupPanel(panel);
                PumpMessages(TimeSpan.FromMilliseconds(20));
                Assert.True(panel.IsOpening);
                Assert.False(material.Visible);
                Assert.Equal(0, ((Grid)panel.FindName("VisualRoot")).Opacity);
                Assert.True(material.Light);

                NativeWindowTheme.SetMode(ToolWindowTheme.Dark);
                material.Ready.SetResult();
                CompleteStartupPanel(opening);
                Assert.False(panel.IsOpening);
                Assert.True(material.Visible);
                Assert.False(material.Light);
                Assert.Equal(2, material.PreparationCount);

                panel.HidePanel(immediate: true);
                NativeWindowTheme.SetMode(ToolWindowTheme.Light);
                ShowNativePanel(panel);
                Assert.True(material.Visible);
                Assert.True(material.Light);
                Assert.False(material.Disposed);
            }
            finally { panel.Close(); NativeWindowTheme.SetMode(ToolWindowTheme.System); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaskbarPanelCancellationDuringMaterialPreparationCannotShowLate(bool close)
    {
        RunSta(() =>
        {
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var material = new DelayedStartupMaterial();
            SetStartupMaterial(panel, material);
            try
            {
                var opening = BeginStartupPanel(panel);
                PumpMessages(TimeSpan.FromMilliseconds(20));
                Assert.True(panel.IsOpening);
                if (close) panel.Dispose();
                else panel.HidePanel(immediate: true);
                CompleteStartupPanel(opening);
                material.Ready.SetResult();
                PumpMessages(TimeSpan.FromMilliseconds(20));
                Assert.False(panel.IsVisible);
                Assert.False(material.Visible);
                Assert.Equal(close, material.Disposed);
                if (!close)
                {
                    ShowNativePanel(panel);
                    Assert.True(material.Visible);
                }
            }
            finally { panel.Close(); }
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TaskbarPanelUsesThemedFallbackWhenMaterialFailsOrTimesOut(bool timeout, bool light)
    {
        RunSta(() =>
        {
            NativeWindowTheme.SetMode(light ? ToolWindowTheme.Light : ToolWindowTheme.Dark);
            var panel = new LyricsControlPanelWindow(_ => { }, _ => { });
            var material = new DelayedStartupMaterial();
            if (!timeout) material.Ready.SetException(new InvalidOperationException("Unavailable compositor"));
            SetStartupMaterial(panel, material);
            try
            {
                ShowNativePanel(panel);
                Assert.True(panel.IsVisible);
                Assert.False(panel.IsOpening);
                Assert.True(material.Disposed);
                var fallback = Assert.IsType<FloatingMaterialFallbackWindow>(GetStartupMaterial(panel));
                var surface = (Border)typeof(FloatingMaterialFallbackWindow)
                    .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fallback)!;
                var brush = Assert.IsType<SolidColorBrush>(surface.Background);
                Assert.Equal(light, brush.Color.R >= 180);
                Assert.True(((Grid)panel.FindName("VisualRoot")).IsHitTestVisible);
                panel.HidePanel(immediate: true);
                ShowNativePanel(panel);
                if (timeout) Assert.NotSame(fallback, GetStartupMaterial(panel));
                else Assert.Same(fallback, GetStartupMaterial(panel));
            }
            finally { panel.Close(); NativeWindowTheme.SetMode(ToolWindowTheme.System); }
        });
    }

    private static Task BeginStartupPanel(LyricsControlPanelWindow panel) => panel.Dispatcher.InvokeAsync(() =>
        panel.ShowNearAsync(new Rect(200, 400, 34, 34), DisplayMonitorService.GetDisplays().First(d => d.IsPrimary),
            CoverPosition.Left, activate: false)).Task.Unwrap();

    private static void CompleteStartupPanel(Task opening)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!opening.IsCompleted && DateTime.UtcNow < deadline) PumpMessages(TimeSpan.FromMilliseconds(10));
        Assert.True(opening.IsCompleted);
        opening.GetAwaiter().GetResult();
    }

    private static FieldInfo StartupMaterialField => typeof(LyricsControlPanelWindow)
        .GetField("_material", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void SetStartupMaterial(LyricsControlPanelWindow panel, IFloatingPanelMaterial material) =>
        StartupMaterialField.SetValue(panel, material);
    private static object? GetStartupMaterial(LyricsControlPanelWindow panel) => StartupMaterialField.GetValue(panel);

    private sealed class DelayedStartupMaterial : IFloatingPanelMaterial
    {
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Visible { get; private set; }
        internal bool Light { get; private set; }
        internal bool Disposed { get; private set; }
        internal int PreparationCount { get; private set; }
        public IntPtr Handle => IntPtr.Zero;
        public void Position(Rect bounds, double scale, IntPtr lyricsHandle) { }
        public bool EnsureBelow(IntPtr lyricsHandle) => true;
        public void Configure(bool light, double brightness, double tintOpacity, double blurAmount) => Light = light;
        public Task PrepareAsync(CancellationToken cancellationToken)
        {
            PreparationCount++;
            return PreparationCount == 1 ? Ready.Task.WaitAsync(cancellationToken) : Task.CompletedTask;
        }
        public void SetVisible(bool visible, bool immediate = false) => Visible = visible;
        public void Dispose() => Disposed = true;
    }
}
