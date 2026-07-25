using System.Windows.Interop;
using TaskbarLyrics.Core.Utilities;

namespace TaskbarLyrics.App;

public enum MediaHotkeyAction
{
    TogglePlayPause,
    PreviousTrack,
    NextTrack,
    SeekBackward,
    SeekForward,
    ToggleLyricsVisibility
}

public sealed class GlobalMediaHotkeySettings
{
    public bool Enabled { get; set; } = true;

    public string TogglePlayPause { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.TogglePlayPause).DefaultBinding;

    public string PreviousTrack { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.PreviousTrack).DefaultBinding;

    public string NextTrack { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.NextTrack).DefaultBinding;

    public string SeekBackward { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.SeekBackward).DefaultBinding;

    public string SeekForward { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.SeekForward).DefaultBinding;

    public string ToggleLyricsVisibility { get; set; } = MediaHotkeyCatalog.Get(MediaHotkeyAction.ToggleLyricsVisibility).DefaultBinding;

    public GlobalMediaHotkeySettings Clone() => new()
    {
        Enabled = Enabled,
        TogglePlayPause = TogglePlayPause,
        PreviousTrack = PreviousTrack,
        NextTrack = NextTrack,
        SeekBackward = SeekBackward,
        SeekForward = SeekForward,
        ToggleLyricsVisibility = ToggleLyricsVisibility
    };

    public string GetBinding(MediaHotkeyAction action) => MediaHotkeyCatalog.Get(action).ReadBinding(this);

    public void ResetBinding(MediaHotkeyAction action)
    {
        var definition = MediaHotkeyCatalog.Get(action);
        definition.WriteBinding(this, definition.DefaultBinding);
    }
}

internal sealed class GlobalMediaHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly SerialCommandQueue<MediaHotkeyAction> _commandQueue;
    private readonly HwndSource _messageSource;
    private readonly MediaHotkeyRegistrationCoordinator _registrationCoordinator;
    private bool _stopping;
    private bool _messageSourceDisposed;
    private bool _disposed;

    public GlobalMediaHotkeyService(Func<MediaHotkeyAction, CancellationToken, Task> executeActionAsync)
    {
        _commandQueue = new SerialCommandQueue<MediaHotkeyAction>(
            executeActionAsync,
            exception => Log.Warn($"Global media hotkey command failed: {exception}"));
        _messageSource = new HwndSource(new HwndSourceParameters("TaskbarLyrics.MediaHotkeys")
        {
            ParentWindow = new IntPtr(-3),
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _messageSource.AddHook(WndProc);
        _registrationCoordinator = new MediaHotkeyRegistrationCoordinator(
            new WindowsMediaHotkeyRegistrar(_messageSource.Handle));
    }

    public void Apply(GlobalMediaHotkeySettings? settings)
    {
        if (_disposed || _stopping)
        {
            return;
        }

        _registrationCoordinator.Apply(settings);
    }

    public IReadOnlyDictionary<string, string> GetStatusSnapshot()
    {
        return _registrationCoordinator.GetStatusSnapshot();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        BeginStopping();
        _registrationCoordinator.Dispose();
        DisposeMessageSource();
        _commandQueue.Dispose();
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        BeginStopping();
        try
        {
            await _commandQueue.StopAsync(timeout);
        }
        catch (TimeoutException exception)
        {
            Log.Warn($"Global media hotkey commands did not stop within {timeout.TotalMilliseconds:0} ms: {exception.Message}");
        }
        finally
        {
            DisposeMessageSource();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey)
        {
            return IntPtr.Zero;
        }

        if (!_registrationCoordinator.TryGetRegisteredAction(wParam.ToInt32(), out var action))
        {
            return IntPtr.Zero;
        }

        handled = true;
        _commandQueue.TryEnqueue(action);
        return IntPtr.Zero;
    }

    private void BeginStopping()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        _registrationCoordinator.UnregisterAll();
        _messageSource.RemoveHook(WndProc);
    }

    private void DisposeMessageSource()
    {
        if (_messageSourceDisposed)
        {
            return;
        }

        _messageSourceDisposed = true;
        _messageSource.Dispose();
    }

}

internal readonly record struct MediaHotkeyBinding(int Modifiers, int VirtualKey);

internal static class MediaHotkeyBindingParser
{
    internal const int ModifierAlt = 0x0001;
    internal const int ModifierControl = 0x0002;
    internal const int ModifierShift = 0x0004;

    public static bool TryParse(string? value, out MediaHotkeyBinding binding)
    {
        binding = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var modifiers = 0;
        int? virtualKey = null;
        foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierControl;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierAlt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierShift;
                continue;
            }

            if (virtualKey is not null || !TryGetVirtualKey(part, out var key))
            {
                return false;
            }

            virtualKey = key;
        }

        if (modifiers == 0 || virtualKey is null)
        {
            return false;
        }

        binding = new MediaHotkeyBinding(modifiers, virtualKey.Value);
        return true;
    }

    private static bool TryGetVirtualKey(string key, out int virtualKey)
    {
        virtualKey = key.ToUpperInvariant() switch
        {
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "INSERT" => 0x2D,
            "DELETE" => 0x2E,
            "SPACE" => 0x20,
            _ => 0
        };

        if (virtualKey != 0)
        {
            return true;
        }

        if (key.Length == 1 && char.IsLetter(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        if (key.Length == 1 && char.IsDigit(key[0]))
        {
            virtualKey = key[0];
            return true;
        }

        if (key.Length is 2 or 3 && key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = 0x70 + functionKey - 1;
            return true;
        }

        return false;
    }

}
