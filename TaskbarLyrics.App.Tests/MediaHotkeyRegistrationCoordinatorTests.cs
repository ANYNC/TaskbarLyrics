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
        Assert.Equal(6, registrar.RegistrationAttempts.Count);
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
        Assert.Equal(8, registrar.RegistrationAttempts.Count);
    }

    [Fact]
    public void ApplyWhenSettingsAreReplacedUnregistersExistingBindingsBeforeApplyingNewOnes()
    {
        var registrar = new FakeRegistrar();
        using var coordinator = new MediaHotkeyRegistrationCoordinator(registrar);

        coordinator.Apply(new GlobalMediaHotkeySettings());
        coordinator.Apply(new GlobalMediaHotkeySettings { Enabled = false });

        Assert.Equal(8, registrar.UnregisteredIds.Count);
        Assert.All(coordinator.GetStatusSnapshot().Values, status =>
            Assert.Equal(MediaHotkeyRegistrationStatus.Disabled, status));
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
