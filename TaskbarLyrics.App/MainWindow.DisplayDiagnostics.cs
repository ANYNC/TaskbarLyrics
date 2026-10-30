using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public partial class MainWindow
{
    private readonly LyricsDisplayDiagnostics _displayDiagnostics;
    internal int DisplayDiagnosticId => _displayDiagnostics.Id;

    internal void RecordDisplayDiagnostics(string stage, bool capture = false)
    {
        if (!LyricsDisplayDiagnostics.Enabled) return;
        _displayDiagnostics.Record(stage, ReadDisplayDiagnosticState());
        if (capture)
            TaskObserver.Observe(_displayDiagnostics.CaptureAsync(stage, () => new LyricsDisplaySnapshot(
                RootBorder, _lyricsWebViewElement, _lyricsCoreWebView2, _lyricsWebViewController,
                ReadDisplayDiagnosticState())), "lyrics display diagnostic snapshot");
    }

    private string ReadDisplayDiagnosticState() =>
        $"Target='{_displayMonitor?.Id}' Bounds={_displayMonitor?.Bounds} Dpi={_displayMonitor?.PixelsPerDip} " +
        $"Disposed={_isDisposed} RecreateRequested={_windowRecreationRequested} " +
        $"WebReady={_isWebViewReady} DocumentReady={_isWebDocumentReady} Initializing={_isWebViewInitializing} " +
        $"ContentVisible={_isLyricsContentVisible} Region={_regionVisibility.Phase} " +
        $"SurfaceHidden={_embeddedTaskbarAnchor.IsSurfaceHidden}";
}
