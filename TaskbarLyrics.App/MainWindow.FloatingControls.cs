using System.Windows;
using System.Windows.Media;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Utilities;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace TaskbarLyrics.App;

public partial class MainWindow
{
    private FloatingLyricsPanelController? _floatingControls;

    private bool FloatingControlsLight => (_primaryTextColor.R + _primaryTextColor.G + _primaryTextColor.B) / 3 < 128;

    private void InitializeFloatingControls()
    {
        _floatingControls = new FloatingLyricsPanelController(Dispatcher, CreateFloatingControls);
        _floatingControls.Register(this, () => _displayMonitor, () => _isLyricsContentVisible && _isWebDocumentReady,
            SetFloatingControlSurface);
    }

    internal void RegisterFloatingLyricsHost(LyricsMirrorWindow mirror)
    {
        _floatingControls?.Register(mirror, () => mirror.DisplayMonitor, () => mirror.IsContentVisible,
            (open, above) => mirror.SetFloatingControlSurface(open, above, FloatingControlsLight));
    }

    private FloatingLyricsControlWindow CreateFloatingControls()
    {
        var panel = new FloatingLyricsControlWindow(
            action => TaskObserver.Observe(ExecuteMediaHotkeyAsync(action, CancellationToken.None), "floating control action"),
            position => TaskObserver.Observe(_mediaPlaybackController.SeekToAsync(position, CancellationToken.None), "floating control seek"));
        panel.Dismissed += (_, _) => _floatingControls?.Close();
        UpdateFloatingControls(panel);
        return panel;
    }

    private void UpdateFloatingControls(FloatingLyricsControlWindow? panel)
    {
        panel?.Update(_currentTrack, _isCurrentPlaybackPlaying, _currentPlaybackPosition, _canSeek,
            _currentTrack is null ? default : PlayerSourceBadgeResolver.Resolve(_currentTrack.SourceApp, _currentSettings),
            FloatingControlsLight);
    }

    private void SetFloatingControlSurface(bool open, bool above)
    {
        if (Volatile.Read(ref _isDisposed) != 0) return;
        RootBorder.Background = _currentSettings.UseFloatingWindow && _currentSettings.EnableControlPanel
            ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) : Brushes.Transparent;
        RootBorder.CornerRadius = new CornerRadius(8);
        if (_isWebDocumentReady) TaskObserver.Observe(ExecuteWebScriptAsync(
            LyricsWebViewScriptFactory.SetFloatingControlSurface(open, FloatingControlsLight)), "floating lyric surface");
        SetPrimaryControlPanelOpen(open);
    }
}
