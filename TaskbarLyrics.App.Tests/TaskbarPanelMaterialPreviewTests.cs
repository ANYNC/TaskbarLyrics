using System.Reflection;
using System.Windows;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void TaskbarMaterialUsesSavedSettingsAndPreviewsWithoutClosingOrMovingPanel()
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var main = new MainWindow(offsets, composition, () => { }, () => { });
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var settings = new AppSettings { EmbeddedPanelBrightness = 0.7, EmbeddedPanelTintOpacity = 0.3, EmbeddedPanelBlurAmount = 40 };
            typeof(MainWindow).GetField("_currentSettings", flags)!.SetValue(main, settings);
            typeof(MainWindow).GetField("_hasAppliedSettings", flags)!.SetValue(main, true);
            var panel = (LyricsControlPanelWindow)typeof(MainWindow).GetMethod("CreateControlPanelWindow", flags)!.Invoke(main, null)!;
            var material = new RecordingTaskbarMaterial();
            typeof(LyricsControlPanelWindow).GetField("_material", flags)!.SetValue(panel, material);
            typeof(MainWindow).GetField("_controlPanelWindow", flags)!.SetValue(main, panel);
            try
            {
                ShowNativePanel(panel);
                Assert.Equal((0.7, 0.3, 40.0), material.LastConfiguration);
                var before = FloatingLyricsPanelController.GetBounds(panel);
                var next = settings.Clone();
                next.EmbeddedPanelBrightness = 0.2;
                next.EmbeddedPanelTintOpacity = 0.8;
                next.EmbeddedPanelBlurAmount = 8;
                main.ApplySettings(next, keepControlPanelOpen: true);
                Assert.True(panel.IsVisible);
                Assert.Equal(before, FloatingLyricsPanelController.GetBounds(panel));
                Assert.Equal((0.2, 0.8, 8.0), material.LastConfiguration);
                typeof(LyricsControlPanelWindow).GetMethod("ApplyTheme", flags)!.Invoke(panel, null);
                Assert.Equal((0.2, 0.8, 8.0), material.LastConfiguration);
                panel.HidePanel(immediate: true);
                ShowNativePanel(panel);
                Assert.Equal((0.2, 0.8, 8.0), material.LastConfiguration);
            }
            finally { main.Close(); }
            Assert.True(material.Disposed);
        });
    }

    private sealed class RecordingTaskbarMaterial : IFloatingPanelMaterial
    {
        public IntPtr Handle => IntPtr.Zero;
        internal (double Brightness, double TintOpacity, double BlurAmount) LastConfiguration { get; private set; }
        internal bool Disposed { get; private set; }
        public void Position(Rect cardBounds, double scale, IntPtr lyricsHandle) { }
        public bool EnsureBelow(IntPtr lyricsHandle) => true;
        public void Configure(bool light, double brightness, double tintOpacity, double blurAmount) => LastConfiguration = (brightness, tintOpacity, blurAmount);
        public void SetVisible(bool visible, bool immediate = false) { }
        public void Dispose() => Disposed = true;
    }
}
