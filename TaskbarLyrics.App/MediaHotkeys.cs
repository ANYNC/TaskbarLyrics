using System.Runtime.InteropServices;
using System.Windows.Interop;

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

    public string TogglePlayPause { get; set; } = "Ctrl+Shift+P";

    public string PreviousTrack { get; set; } = "Ctrl+Shift+Left";

    public string NextTrack { get; set; } = "Ctrl+Shift+Right";

    public string SeekBackward { get; set; } = "Ctrl+Alt+Shift+Left";

    public string SeekForward { get; set; } = "Ctrl+Alt+Shift+Right";

    public string ToggleLyricsVisibility { get; set; } = "Ctrl+Shift+D";

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

    public string GetBinding(MediaHotkeyAction action) => action switch
    {
        MediaHotkeyAction.TogglePlayPause => TogglePlayPause,
        MediaHotkeyAction.PreviousTrack => PreviousTrack,
        MediaHotkeyAction.NextTrack => NextTrack,
        MediaHotkeyAction.SeekBackward => SeekBackward,
        MediaHotkeyAction.SeekForward => SeekForward,
        MediaHotkeyAction.ToggleLyricsVisibility => ToggleLyricsVisibility,
        _ => string.Empty
    };

    public void ResetBinding(MediaHotkeyAction action)
    {
        switch (action)
        {
            case MediaHotkeyAction.TogglePlayPause:
                TogglePlayPause = "Ctrl+Shift+P";
                break;
            case MediaHotkeyAction.PreviousTrack:
                PreviousTrack = "Ctrl+Shift+Left";
                break;
            case MediaHotkeyAction.NextTrack:
                NextTrack = "Ctrl+Shift+Right";
                break;
            case MediaHotkeyAction.SeekBackward:
                SeekBackward = "Ctrl+Alt+Shift+Left";
                break;
            case MediaHotkeyAction.SeekForward:
                SeekForward = "Ctrl+Alt+Shift+Right";
                break;
            case MediaHotkeyAction.ToggleLyricsVisibility:
                ToggleLyricsVisibility = "Ctrl+Shift+D";
                break;
        }
    }
}

internal sealed class GlobalMediaHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ModAlt = 0x0001;
    private const int ModControl = 0x0002;
    private const int ModShift = 0x0004;
    private const int ModNoRepeat = 0x4000;
    private const int HotkeyIdBase = 0x5A00;

    private static readonly MediaHotkeyAction[] Actions =
    {
        MediaHotkeyAction.TogglePlayPause,
        MediaHotkeyAction.PreviousTrack,
        MediaHotkeyAction.NextTrack,
        MediaHotkeyAction.SeekBackward,
        MediaHotkeyAction.SeekForward,
        MediaHotkeyAction.ToggleLyricsVisibility
    };

    private readonly Func<MediaHotkeyAction, Task> _executeActionAsync;
    private readonly HwndSource _messageSource;
    private readonly Dictionary<MediaHotkeyAction, string> _statuses = new();
    private readonly HashSet<int> _registeredIds = new();
    private bool _disposed;

    public GlobalMediaHotkeyService(Func<MediaHotkeyAction, Task> executeActionAsync)
    {
        _executeActionAsync = executeActionAsync;
        _messageSource = new HwndSource(new HwndSourceParameters("TaskbarLyrics.MediaHotkeys")
        {
            ParentWindow = new IntPtr(-3),
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _messageSource.AddHook(WndProc);
        SetAllStatuses("已关闭");
    }

    public void Apply(GlobalMediaHotkeySettings? settings)
    {
        if (_disposed)
        {
            return;
        }

        UnregisterAll();
        var snapshot = settings?.Clone() ?? new GlobalMediaHotkeySettings();
        if (!snapshot.Enabled)
        {
            SetAllStatuses("已关闭");
            return;
        }

        var parsedBindings = new Dictionary<MediaHotkeyAction, HotkeyBinding>();
        var duplicateActions = new HashSet<MediaHotkeyAction>();
        var seenBindings = new Dictionary<HotkeyBinding, MediaHotkeyAction>();
        foreach (var action in Actions)
        {
            if (!TryParseBinding(snapshot.GetBinding(action), out var binding))
            {
                _statuses[action] = "组合无效";
                continue;
            }

            if (seenBindings.ContainsKey(binding))
            {
                duplicateActions.Add(action);
                _statuses[action] = "与其他快捷键重复";
                continue;
            }

            seenBindings[binding] = action;
            parsedBindings[action] = binding;
        }

        foreach (var (action, binding) in parsedBindings)
        {
            var id = GetHotkeyId(action);
            if (RegisterHotKey(_messageSource.Handle, id, binding.Modifiers | ModNoRepeat, binding.VirtualKey))
            {
                _registeredIds.Add(id);
                _statuses[action] = "已注册";
            }
            else
            {
                _statuses[action] = "已被系统或其他应用占用";
            }
        }

        foreach (var action in Actions.Where(action => !parsedBindings.ContainsKey(action) && !duplicateActions.Contains(action)))
        {
            _statuses.TryAdd(action, "组合无效");
        }
    }

    public IReadOnlyDictionary<string, string> GetStatusSnapshot()
    {
        return Actions.ToDictionary(
            GetActionKey,
            action => _statuses.TryGetValue(action, out var status) ? status : "未注册",
            StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _messageSource.RemoveHook(WndProc);
        _messageSource.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey)
        {
            return IntPtr.Zero;
        }

        var id = wParam.ToInt32();
        var action = Actions.FirstOrDefault(candidate => GetHotkeyId(candidate) == id);
        if (!_registeredIds.Contains(id) || GetHotkeyId(action) != id)
        {
            return IntPtr.Zero;
        }

        handled = true;
        _ = ExecuteActionSilentlyAsync(action);
        return IntPtr.Zero;
    }

    private async Task ExecuteActionSilentlyAsync(MediaHotkeyAction action)
    {
        try
        {
            await _executeActionAsync(action);
        }
        catch
        {
            // Global shortcuts must never surface a UI error.
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _registeredIds)
        {
            _ = UnregisterHotKey(_messageSource.Handle, id);
        }

        _registeredIds.Clear();
    }

    private void SetAllStatuses(string status)
    {
        foreach (var action in Actions)
        {
            _statuses[action] = status;
        }
    }

    private static int GetHotkeyId(MediaHotkeyAction action) => HotkeyIdBase + (int)action;

    private static string GetActionKey(MediaHotkeyAction action) => action switch
    {
        MediaHotkeyAction.TogglePlayPause => "togglePlayPause",
        MediaHotkeyAction.PreviousTrack => "previousTrack",
        MediaHotkeyAction.NextTrack => "nextTrack",
        MediaHotkeyAction.SeekBackward => "seekBackward",
        MediaHotkeyAction.SeekForward => "seekForward",
        MediaHotkeyAction.ToggleLyricsVisibility => "toggleLyricsVisibility",
        _ => string.Empty
    };

    private static bool TryParseBinding(string? value, out HotkeyBinding binding)
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
                modifiers |= ModControl;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
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

        binding = new HotkeyBinding(modifiers, virtualKey.Value);
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

    private readonly record struct HotkeyBinding(int Modifiers, int VirtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
