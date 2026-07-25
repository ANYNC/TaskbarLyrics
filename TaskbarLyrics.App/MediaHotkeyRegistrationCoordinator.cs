namespace TaskbarLyrics.App;

internal interface IGlobalHotkeyRegistrar
{
    bool TryRegister(int id, int modifiers, int virtualKey);
    void Unregister(int id);
}

internal static class MediaHotkeyRegistrationStatus
{
    public const string Disabled = "disabled";
    public const string Invalid = "invalid";
    public const string Duplicate = "duplicate";
    public const string Registered = "registered";
    public const string Occupied = "occupied";
    public const string NotRegistered = "notRegistered";
}

internal sealed class MediaHotkeyRegistrationCoordinator : IDisposable
{
    private const int ModNoRepeat = 0x4000;
    private readonly IGlobalHotkeyRegistrar _registrar;
    private readonly Dictionary<MediaHotkeyAction, string> _statuses = new();
    private readonly HashSet<int> _registeredIds = [];
    private bool _disposed;

    public MediaHotkeyRegistrationCoordinator(IGlobalHotkeyRegistrar registrar)
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
        foreach (var definition in MediaHotkeyCatalog.Definitions)
        {
            if (!MediaHotkeyBindingParser.TryParse(definition.ReadBinding(snapshot), out var binding))
            {
                _statuses[definition.Action] = MediaHotkeyRegistrationStatus.Invalid;
                continue;
            }

            if (seenBindings.TryGetValue(binding, out var originalAction))
            {
                duplicateActions.Add(originalAction);
                duplicateActions.Add(definition.Action);
                parsedBindings.Remove(originalAction);
                _statuses[originalAction] = MediaHotkeyRegistrationStatus.Duplicate;
                _statuses[definition.Action] = MediaHotkeyRegistrationStatus.Duplicate;
                continue;
            }

            seenBindings[binding] = definition.Action;
            parsedBindings[definition.Action] = binding;
        }

        foreach (var (action, binding) in parsedBindings)
        {
            var id = MediaHotkeyCatalog.Get(action).RegistrationId;
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

        foreach (var action in MediaHotkeyCatalog.Definitions
                     .Select(definition => definition.Action)
                     .Where(action => !parsedBindings.ContainsKey(action) && !duplicateActions.Contains(action)))
        {
            _statuses.TryAdd(action, MediaHotkeyRegistrationStatus.Invalid);
        }
    }

    public IReadOnlyDictionary<string, string> GetStatusSnapshot() => MediaHotkeyCatalog.Definitions.ToDictionary(
        definition => definition.StatusKey,
        definition => _statuses.TryGetValue(definition.Action, out var status) ? status : MediaHotkeyRegistrationStatus.NotRegistered,
        StringComparer.Ordinal);

    public bool TryGetRegisteredAction(int id, out MediaHotkeyAction action)
    {
        foreach (var definition in MediaHotkeyCatalog.Definitions)
        {
            if (definition.RegistrationId == id && _registeredIds.Contains(id))
            {
                action = definition.Action;
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

    internal static int GetHotkeyId(MediaHotkeyAction action) => MediaHotkeyCatalog.Get(action).RegistrationId;

    private void SetAllStatuses(string status)
    {
        foreach (var action in MediaHotkeyCatalog.Definitions.Select(definition => definition.Action))
        {
            _statuses[action] = status;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
