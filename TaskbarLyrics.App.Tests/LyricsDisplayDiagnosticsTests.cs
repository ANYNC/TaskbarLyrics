using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed partial class FloatingPanelInputTests
{
    [Fact]
    public void DisplayDiagnosticsCoalesceWithoutChangingOrCreatingTheNativeWindow()
    {
        RunSta(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new Window { Width = 420, Height = 44, Opacity = 0.8 };
            var surface = new Border { Opacity = 0.5 };
            using var diagnostics = new LyricsDisplayDiagnostics(window, enabled: true, captureImages: false);
            try
            {
                var reads = 0;
                LyricsDisplaySnapshot Read()
                {
                    window.Dispatcher.VerifyAccess();
                    reads++;
                    return new(surface, null, null, null, "Test");
                }
                var capture = diagnostics.CaptureAsync("First", Read);
                Assert.Same(capture, diagnostics.CaptureAsync("Burst", Read));
                PumpDisplayEventsUntil(() => capture.IsCompleted, TimeSpan.FromSeconds(4));
                capture.GetAwaiter().GetResult();
                Assert.Equal(2, reads);
                Assert.Equal(IntPtr.Zero, new WindowInteropHelper(window).Handle);
                Assert.False(window.IsVisible);
                Assert.Equal(0.8, window.Opacity);
                Assert.Equal(0.5, surface.Opacity);
                Assert.Equal(Visibility.Visible, surface.Visibility);
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        });
    }

    [Fact]
    public void DisplayDiagnosticsDisposeCancelsLateReadersAndRejectsLaterCaptures()
    {
        RunSta(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new Window();
            using var diagnostics = new LyricsDisplayDiagnostics(window, enabled: true, captureImages: false);
            try
            {
                LyricsDisplaySnapshot Read() => throw new InvalidOperationException("Disposed window must not be read.");
                var capture = diagnostics.CaptureAsync("Closing", Read);
                diagnostics.Dispose();
                PumpDisplayEventsUntil(() => capture.IsCompleted, TimeSpan.FromSeconds(2));
                capture.GetAwaiter().GetResult();
                Assert.True(diagnostics.CaptureAsync("Late", Read).IsCompletedSuccessfully);
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(previous); }
        });
    }

    [Fact]
    public void DisabledDisplayDiagnosticsNeverInvokeReaders()
    {
        RunSta(() =>
        {
            var window = new Window();
            using var diagnostics = new LyricsDisplayDiagnostics(window, enabled: false);
            try
            {
                var capture = diagnostics.CaptureAsync("Disabled", () => throw new InvalidOperationException("Disabled reader"));
                Assert.True(capture.IsCompletedSuccessfully);
            }
            finally { window.Close(); }
        });
    }
}
