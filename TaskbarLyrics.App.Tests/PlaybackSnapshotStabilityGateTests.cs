using TaskbarLyrics.Core.Models;
using Xunit;

namespace TaskbarLyrics.App.Tests;

public sealed class PlaybackSnapshotStabilityGateTests
{
    [Fact]
    public void FirstValidSnapshotIsAcceptedWithoutHolding()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        var snapshot = CreateSnapshot("Owl City", isPlaying: true);

        var decision = Observe(gate, snapshot, At(0));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.InitialSnapshot, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
        Assert.Same(snapshot, decision.Snapshot);
    }

    [Fact]
    public void SameStableIdentityIsAcceptedImmediately()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City", isPlaying: true), At(0));

        var snapshot = CreateSnapshot("Owl City", isPlaying: false);
        var decision = Observe(gate, snapshot, At(60));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.StableIdentity, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
    }

    [Fact]
    public void ArtistFormattingChangeIsHeldAndNoPlaybackClearsPendingState()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City/Lindsey Stirling", isPlaying: true), At(0));

        var weakChange = CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: false);
        var held = Observe(gate, weakChange, At(60));
        var pendingState = gate.State;
        var noPlayback = CreateSnapshot(null, isPlaying: false);
        var accepted = Observe(gate, noPlayback, At(120));

        Assert.Equal(PlaybackSnapshotGateAction.Hold, held.Action);
        Assert.Equal(PlaybackSnapshotGateReason.WeakMetadataChange, held.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.PendingWeakChange, pendingState);
        Assert.Equal(PlaybackSnapshotGateAction.Accept, accepted.Action);
        Assert.Equal(PlaybackSnapshotGateReason.NoPlayback, accepted.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Empty, gate.State);
    }

    [Fact]
    public void ArtistSubsetChangeIsHeldAndNoPlaybackIsAcceptedImmediately()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));

        var held = Observe(gate, CreateSnapshot("Owl City", isPlaying: false), At(60));
        var accepted = Observe(gate, CreateSnapshot(null, isPlaying: false), At(120));

        Assert.Equal(PlaybackSnapshotGateAction.Hold, held.Action);
        Assert.Equal(PlaybackSnapshotGateAction.Accept, accepted.Action);
        Assert.Equal(PlaybackSnapshotGateReason.NoPlayback, accepted.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown Artist")]
    public void MissingArtistMetadataIsHeldAsWeakChange(string degradedArtist)
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));

        var candidate = CreateSnapshot(degradedArtist, isPlaying: false);
        var decision = Observe(gate, candidate, At(60));

        Assert.Equal(PlaybackSnapshotGateAction.Hold, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.WeakMetadataChange, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.PendingWeakChange, gate.State);
    }

    [Fact]
    public void RestoredStableIdentityDiscardsPendingCandidate()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        var stable = CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true);
        Observe(gate, stable, At(0));
        Observe(gate, CreateSnapshot("Owl City", isPlaying: false), At(60));

        var restored = CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: false);
        var decision = Observe(gate, restored, At(120));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.StableIdentityRestored, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
        Assert.Same(restored, decision.Snapshot);
    }

    [Fact]
    public void WeakCandidateCommitsAfterQuietPeriod()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));
        var candidate = CreateSnapshot("Owl City", isPlaying: false);

        var held = Observe(gate, candidate, At(1));
        var beforeDeadline = Observe(gate, candidate, At(750));
        var committed = Observe(gate, candidate, At(751));

        Assert.Equal(PlaybackSnapshotGateAction.Hold, held.Action);
        Assert.Equal(PlaybackSnapshotGateAction.Hold, beforeDeadline.Action);
        Assert.Equal(PlaybackSnapshotGateAction.Accept, committed.Action);
        Assert.Equal(PlaybackSnapshotGateReason.QuietPeriodElapsed, committed.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
        Assert.Same(candidate, committed.Snapshot);
    }

    [Fact]
    public void CandidateReplacementResetsQuietPeriodButMaximumDeadlineCommitsLatest()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling / Carly Rae", isPlaying: true), At(0));

        var firstCandidate = CreateSnapshot("Owl City", isPlaying: false);
        var secondCandidate = CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: false);
        var thirdCandidate = CreateSnapshot("Owl City / Carly Rae", isPlaying: false);
        var latestCandidate = CreateSnapshot("Owl City/Lindsey Stirling", isPlaying: false);

        Assert.Equal(
            PlaybackSnapshotGateAction.Hold,
            Observe(gate, firstCandidate, At(1)).Action);
        Assert.Equal(
            PlaybackSnapshotGateReason.WeakMetadataChangeReplaced,
            Observe(gate, secondCandidate, At(600)).Reason);
        Assert.Equal(
            PlaybackSnapshotGateReason.WeakMetadataChangeReplaced,
            Observe(gate, thirdCandidate, At(1200)).Reason);
        Assert.Equal(
            PlaybackSnapshotGateReason.WeakMetadataChangeReplaced,
            Observe(gate, latestCandidate, At(1499)).Reason);

        var committed = Observe(gate, latestCandidate, At(1501));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, committed.Action);
        Assert.Equal(PlaybackSnapshotGateReason.MaximumHoldElapsed, committed.Reason);
        Assert.Same(latestCandidate, committed.Snapshot);
    }

    [Theory]
    [InlineData("Other Title", "Netease")]
    [InlineData("Beautiful Times", "Spotify")]
    public void TitleOrSourceChangeIsAcceptedImmediately(string title, string sourceApp)
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));

        var changed = CreateSnapshot("Owl City", title: title, sourceApp: sourceApp, isPlaying: false);
        var decision = Observe(gate, changed, At(60));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.StrongMetadataChange, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
    }

    [Fact]
    public void DifferentReliableSongIdIsAcceptedImmediately()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(
            gate,
            CreateSnapshot("Owl City / Lindsey Stirling", songId: "song-a", isPlaying: true),
            At(0));

        var changed = CreateSnapshot("Owl City", songId: "song-b", isPlaying: false);
        var decision = Observe(gate, changed, At(60));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.StrongMetadataChange, decision.Reason);
    }

    [Fact]
    public void PlayingSameTitleWithDifferentArtistIsAcceptedImmediately()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));

        var changed = CreateSnapshot("Different Artist", isPlaying: true);
        var decision = Observe(gate, changed, At(60));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.StrongMetadataChange, decision.Reason);
    }

    [Fact]
    public void NoPlaybackClearsIdentityAndNextTrackIsAcceptedAsInitial()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        Observe(gate, CreateSnapshot("Owl City / Lindsey Stirling", isPlaying: true), At(0));
        Observe(gate, CreateSnapshot(null, isPlaying: false), At(60));

        var nextTrack = CreateSnapshot("Owl City", isPlaying: false);
        var decision = Observe(gate, nextTrack, At(120));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.InitialSnapshot, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Stable, gate.State);
    }

    [Fact]
    public void InitialNoPlaybackIsAcceptedWithoutHolding()
    {
        var gate = new PlaybackSnapshotStabilityGate();
        var snapshot = CreateSnapshot(null, isPlaying: false);

        var decision = Observe(gate, snapshot, At(0));

        Assert.Equal(PlaybackSnapshotGateAction.Accept, decision.Action);
        Assert.Equal(PlaybackSnapshotGateReason.NoPlayback, decision.Reason);
        Assert.Equal(PlaybackSnapshotStabilityState.Empty, gate.State);
    }

    private static PlaybackSnapshotGateDecision Observe(
        PlaybackSnapshotStabilityGate gate,
        PlaybackSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        return gate.Observe(snapshot, PlaybackInputPolicy.Classify(snapshot), nowUtc);
    }

    private static PlaybackSnapshot CreateSnapshot(
        string? artist,
        string title = "Beautiful Times",
        string sourceApp = "Netease",
        string? songId = null,
        bool isPlaying = true)
    {
        var track = artist is null
            ? null
            : new TrackInfo(
                $"{sourceApp}|{title}|{artist}",
                title,
                artist,
                "Album",
                sourceApp,
                TimeSpan.FromMinutes(3),
                songId);
        return new PlaybackSnapshot(isPlaying, TimeSpan.Zero, track);
    }

    private static DateTimeOffset At(int milliseconds)
    {
        return DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds);
    }
}
