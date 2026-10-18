using System.Windows.Threading;

namespace TaskbarLyrics.App;

internal enum LyricsRegionVisibilityPhase { Visible, Hidden, Preparing, Entering, Exiting }

// Coordinates native visibility with a separately animated WebView surface.
// All calls and completion messages belong to the lyrics window's dispatcher.
internal sealed class LyricsRegionVisibilityController : IDisposable
{
    private readonly Action<int, LyricsRegionVisibilityPhase> _send;
    private readonly Action<bool, bool> _setSurface;
    private readonly DispatcherTimer _fallback;
    private bool _documentReady;
    private bool _targetVisible = true;
    private bool _surfaceVisible = true;
    private bool _disposed;
    private int _generation;

    public LyricsRegionVisibilityPhase Phase { get; private set; } = LyricsRegionVisibilityPhase.Visible;
    public bool IsInteractive => Phase == LyricsRegionVisibilityPhase.Visible;

    public LyricsRegionVisibilityController(Dispatcher dispatcher,
        Action<int, LyricsRegionVisibilityPhase> send, Action<bool, bool> setSurface)
    {
        _send = send;
        _setSurface = setSurface;
        _fallback = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _fallback.Tick += OnFallback;
    }

    public void SetVisible(bool visible)
    {
        if (_disposed || _targetVisible == visible) return;
        _targetVisible = visible;
        ++_generation;
        _fallback.Stop();
        if (!_documentReady || (!visible && !_surfaceVisible))
        {
            Phase = visible ? LyricsRegionVisibilityPhase.Preparing : LyricsRegionVisibilityPhase.Hidden;
            SetSurface(false, false);
            if (_documentReady) _send(_generation, Phase);
            return;
        }

        Phase = visible ? LyricsRegionVisibilityPhase.Preparing : LyricsRegionVisibilityPhase.Exiting;
        SetSurface(_surfaceVisible, false);
        if (!visible) _fallback.Start();
        _send(_generation, Phase);
    }

    public void OnDocumentReady()
    {
        if (_disposed) return;
        _documentReady = true;
        ++_generation;
        _fallback.Stop();
        Phase = _targetVisible ? LyricsRegionVisibilityPhase.Preparing : LyricsRegionVisibilityPhase.Hidden;
        SetSurface(false, false);
        _send(_generation, Phase);
    }

    // Called after the new lyric frame is dispatched, so stale waiting/countdown text
    // cannot become the first frame of an entrance.
    public void OnFrameDispatched()
    {
        if (_disposed || !_documentReady || !_targetVisible || Phase != LyricsRegionVisibilityPhase.Preparing) return;
        Phase = LyricsRegionVisibilityPhase.Entering;
        SetSurface(true, false);
        _fallback.Start();
        _send(_generation, Phase);
    }

    public void Complete(int generation, bool visible)
    {
        if (_disposed || generation != _generation || visible != _targetVisible ||
            Phase is not (LyricsRegionVisibilityPhase.Entering or LyricsRegionVisibilityPhase.Exiting)) return;
        _fallback.Stop();
        Phase = visible ? LyricsRegionVisibilityPhase.Visible : LyricsRegionVisibilityPhase.Hidden;
        SetSurface(visible, visible);
    }

    private void SetSurface(bool visible, bool interactive)
    {
        _surfaceVisible = visible;
        _setSurface(visible, interactive);
    }

    private void OnFallback(object? sender, EventArgs e)
    {
        if (_disposed || Phase is not (LyricsRegionVisibilityPhase.Entering or LyricsRegionVisibilityPhase.Exiting)) return;
        Complete(_generation, _targetVisible);
        // Recover the page as well if its completion event was lost.
        _send(_generation, Phase);
    }

    public void Dispose()
    {
        _disposed = true;
        ++_generation;
        _fallback.Stop();
        _fallback.Tick -= OnFallback;
    }
}
