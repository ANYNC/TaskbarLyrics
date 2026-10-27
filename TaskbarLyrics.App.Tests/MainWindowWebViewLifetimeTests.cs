using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void ClosingMainWindowDisposesAndDetachesItsOwnedWebViewExactlyOnce()
    {
        RunSta(() =>
        {
            using var composition = new AppCompositionRoot();
            using var offsets = new TrackLyricOffsetStore();
            var window = new MainWindow(offsets, composition, () => { }, () => { });
            var control = new DisposableWebSurface();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var host = (Grid)window.FindName("LyricsWebHost");
            host.Children.Add(control);
            typeof(MainWindow).GetField("_lyricsWebViewControl", flags)!.SetValue(window, control);
            typeof(MainWindow).GetField("_lyricsWebViewElement", flags)!.SetValue(window, control);
            typeof(MainWindow).GetField("_isWebViewReady", flags)!.SetValue(window, true);
            typeof(MainWindow).GetField("_isWebDocumentReady", flags)!.SetValue(window, true);
            try
            {
                window.CloseFromHost();
                window.Dispose();

                Assert.Equal(1, control.DisposeCount);
                Assert.Empty(host.Children);
                Assert.Null(typeof(MainWindow).GetField("_lyricsWebViewControl", flags)!.GetValue(window));
                Assert.Null(typeof(MainWindow).GetField("_lyricsWebViewElement", flags)!.GetValue(window));
                Assert.False((bool)typeof(MainWindow).GetField("_isWebViewReady", flags)!.GetValue(window)!);
                Assert.False((bool)typeof(MainWindow).GetField("_isWebDocumentReady", flags)!.GetValue(window)!);

                // Late Loaded/initialization work must not restart the timer or
                // create a replacement WebView on the already-closed window.
                var initialize = typeof(MainWindow).GetMethod("InitializeLyricsWindowAsync", flags)!
                    .CreateDelegate<Func<Task>>(window);
                initialize().GetAwaiter().GetResult();
                Assert.False(((DispatcherTimer)typeof(MainWindow).GetField("_timer", flags)!.GetValue(window)!).IsEnabled);
                Assert.Empty(host.Children);
            }
            finally { window.Dispose(); }
        });
    }

    private sealed class DisposableWebSurface : FrameworkElement, IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
