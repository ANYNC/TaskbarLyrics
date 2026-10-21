using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class MediaHotkeyRegistrationCoordinatorTests
{
    [Fact]
    public void ApplyWhenBindingsAreDuplicatedMarksEveryConflictingActionAndRegistersNeither()
    {
        var registrar = new FakeRegistrar();
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);
        var settings = new GlobalMediaHotkeySettings
        {
            NextTrack = MediaHotkeyCatalog.Get(MediaHotkeyAction.PreviousTrack).DefaultBinding
        };

        coordinator.Apply(settings);

        var statuses = coordinator.GetStatusSnapshot();
        Assert.Equal(MediaHotkeyRegistrationStatus.Duplicate, statuses["previousTrack"]);
        Assert.Equal(MediaHotkeyRegistrationStatus.Duplicate, statuses["nextTrack"]);
        Assert.Equal(MediaHotkeyCatalog.Definitions.Count - 2, registrar.RegistrationAttempts.Count);
        Assert.False(coordinator.TryGetRegisteredAction(
            MediaHotkeyRegistrationCoordinator.GetHotkeyId(MediaHotkeyAction.PreviousTrack),
            out _));
        Assert.False(coordinator.TryGetRegisteredAction(
            MediaHotkeyRegistrationCoordinator.GetHotkeyId(MediaHotkeyAction.NextTrack),
            out _));
    }

    [Fact]
    public void ApplyWhenSystemRejectsBindingReportsOccupiedAndDoesNotDispatchIt()
    {
        var rejectedId = MediaHotkeyRegistrationCoordinator.GetHotkeyId(MediaHotkeyAction.TogglePlayPause);
        var registrar = new FakeRegistrar(new HashSet<int> { rejectedId });
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);

        coordinator.Apply(new GlobalMediaHotkeySettings());

        var statuses = coordinator.GetStatusSnapshot();
        Assert.Equal(MediaHotkeyRegistrationStatus.Occupied, statuses["togglePlayPause"]);
        Assert.False(coordinator.TryGetRegisteredAction(rejectedId, out _));
        Assert.Equal(MediaHotkeyCatalog.Definitions.Count, registrar.RegistrationAttempts.Count);
    }

    [Fact]
    public void ApplyWhenSettingsAreReplacedUnregistersExistingBindingsBeforeApplyingNewOnes()
    {
        var registrar = new FakeRegistrar();
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);

        coordinator.Apply(new GlobalMediaHotkeySettings());
        coordinator.Apply(new GlobalMediaHotkeySettings { Enabled = false });

        Assert.Equal(MediaHotkeyCatalog.Definitions.Count, registrar.UnregisteredIds.Count);
        Assert.All(coordinator.GetStatusSnapshot().Values, status =>
            Assert.Equal(MediaHotkeyRegistrationStatus.Disabled, status));
    }

    [Fact]
    public void ModeHotkeyRegistersDefaultBindingAndUnregistersWhenDisabledOrCleared()
    {
        var registrar = new FakeRegistrar();
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);
        var settings = new GlobalMediaHotkeySettings();
        var action = MediaHotkeyAction.ToggleDisplayMode;
        var id = MediaHotkeyRegistrationCoordinator.GetHotkeyId(action);
        coordinator.Apply(settings);
        Assert.Contains((id, 0x4000 | 0x0001 | 0x0004, 0x43), registrar.RegistrationAttempts);
        Assert.True(coordinator.TryGetRegisteredAction(id, out var registered));
        Assert.Equal(action, registered);

        settings.SetActionEnabled(action, false);
        coordinator.Apply(settings);
        Assert.Equal("Alt+Shift+C", settings.ToggleDisplayMode);
        Assert.Contains(id, registrar.UnregisteredIds);
        Assert.False(coordinator.TryGetRegisteredAction(id, out _));
        Assert.Equal(MediaHotkeyRegistrationStatus.Disabled, coordinator.GetStatusSnapshot()["toggleDisplayMode"]);

        settings.SetActionEnabled(action, true);
        settings.ToggleDisplayMode = "";
        coordinator.Apply(settings);
        Assert.False(coordinator.TryGetRegisteredAction(id, out _));
        Assert.Equal(MediaHotkeyRegistrationStatus.Unassigned, coordinator.GetStatusSnapshot()["toggleDisplayMode"]);
        settings.ResetBinding(action);
        coordinator.Apply(settings);
        Assert.True(coordinator.TryGetRegisteredAction(id, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledAndEmptyBindingsDoNotConflictWithActiveBindings(bool clear)
    {
        var registrar = new FakeRegistrar();
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);
        var settings = new GlobalMediaHotkeySettings { ToggleDisplayMode = "Alt+Shift+P" };
        if (clear) settings.ToggleDisplayMode = " ";
        else settings.SetActionEnabled(MediaHotkeyAction.ToggleDisplayMode, false);
        coordinator.Apply(settings);
        Assert.Equal(MediaHotkeyRegistrationStatus.Registered, coordinator.GetStatusSnapshot()["togglePlayPause"]);
        Assert.Equal(MediaHotkeyCatalog.Definitions.Count - 1, registrar.RegistrationAttempts.Count);
        Assert.False(coordinator.TryGetRegisteredAction(
            MediaHotkeyRegistrationCoordinator.GetHotkeyId(MediaHotkeyAction.ToggleDisplayMode), out _));
    }

    private sealed class FakeRegistrar(IReadOnlySet<int>? rejectedIds = null) : IGlobalHotkeyRegistrar
    {
        private readonly IReadOnlySet<int> _rejectedIds = rejectedIds ?? new HashSet<int>();

        public List<(int Id, int Modifiers, int VirtualKey)> RegistrationAttempts { get; } = [];
        public List<int> UnregisteredIds { get; } = [];

        public bool TryRegister(int id, int modifiers, int virtualKey)
        {
            RegistrationAttempts.Add((id, modifiers, virtualKey));
            return !_rejectedIds.Contains(id);
        }

        public void Unregister(int id)
        {
            UnregisteredIds.Add(id);
        }
    }
}
