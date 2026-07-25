namespace TaskbarLyrics.App;

internal interface IMediaHotkeyRegistrar
{
    bool TryRegister(int id, int modifiers, int virtualKey);
    void Unregister(int id);
}

internal static class MediaHotkeyRegistrationStatus
{
    public const string Disabled = "已关闭";
    public const string Invalid = "组合无效";
    public const string Duplicate = "与其他快捷键重复";
    public const string Registered = "已注册";
    public const string Occupied = "已被系统或其他应用占用";
    public const string NotRegistered = "未注册";
}

internal sealed class MediaHotkeyRegistrationCoordinator : IDisposable
{
    private const int ModNoRepeat = 0x4000;
    private const int HotkeyIdBase = 0x5A00;

    private static readonly MediaHotkeyAction[] Actions =
    [
        MediaHotkeyAction.TogglePlayPause,
        MediaHotkeyAction.PreviousTrack,
        MediaHotkeyAction.NextTrack,
        MediaHotkeyAction.SeekBackward,
        MediaHotkeyAction.SeekForward,
        MediaHotkeyAction.ToggleLyricsVisibility
    ];

    private readonly IMediaHotkeyRegistrar _registrar;
    private readonly Dictionary<MediaHotkeyAction, string> _statuses = new();
    private readonly HashSet<int> _registeredIds = [];
    private bool _disposed;

    public MediaHotkeyRegistrationCoordinator(IMediaHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        SetAllStatuses(MediaHotkeyRegistrationStatus.Disabled);
    }

    public void Apply(GlobalMediaHotkeySettings? settings)
    {
        ThrowIfDisposed();

        UnregisterAll();
        var snapshot = settings?.Clone() ?? new GlobalMediaHotkeySettings();
        if (!snapshot.Enabled)
        {
            SetAllStatuses(MediaHotkeyRegistrationStatus.Disabled);
            return;
        }

        var parsedBindings = new Dictionary<MediaHotkeyAction, MediaHotkeyBinding>();
        var duplicateActions = new HashSet<MediaHotkeyAction>();
        var seenBindings = new Dictionary<MediaHotkeyBinding, MediaHotkeyAction>();
        foreach (var action in Actions)
        {
            if (!MediaHotkeyBindingParser.TryParse(snapshot.GetBinding(action), out var binding))
            {
                _statuses[action] = MediaHotkeyRegistrationStatus.Invalid;
                continue;
            }

            if (seenBindings.ContainsKey(binding))
            {
                duplicateActions.Add(action);
                _statuses[action] = MediaHotkeyRegistrationStatus.Duplicate;
                continue;
            }

            seenBindings[binding] = action;
            parsedBindings[action] = binding;
        }

        foreach (var (action, binding) in parsedBindings)
        {
            var id = GetHotkeyId(action);
            if (_registrar.TryRegister(id, binding.Modifiers | ModNoRepeat, binding.VirtualKey))
            {
                _registeredIds.Add(id);
                _statuses[action] = MediaHotkeyRegistrationStatus.Registered;
            }
            else
            {
                _statuses[action] = MediaHotkeyRegistrationStatus.Occupied;
            }
        }

        foreach (var action in Actions.Where(action => !parsedBindings.ContainsKey(action) && !duplicateActions.Contains(action)))
        {
            _statuses.TryAdd(action, MediaHotkeyRegistrationStatus.Invalid);
        }
    }

    public IReadOnlyDictionary<string, string> GetStatusSnapshot() => Actions.ToDictionary(
        GetActionKey,
        action => _statuses.TryGetValue(action, out var status) ? status : MediaHotkeyRegistrationStatus.NotRegistered,
        StringComparer.Ordinal);

    public bool TryGetRegisteredAction(int id, out MediaHotkeyAction action)
    {
        foreach (var candidate in Actions)
        {
            if (GetHotkeyId(candidate) == id && _registeredIds.Contains(id))
            {
                action = candidate;
                return true;
            }
        }

        action = default;
        return false;
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds)
        {
            _registrar.Unregister(id);
        }

        _registeredIds.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
    }

    internal static int GetHotkeyId(MediaHotkeyAction action) => HotkeyIdBase + (int)action;

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

    private void SetAllStatuses(string status)
    {
        foreach (var action in Actions)
        {
            _statuses[action] = status;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
