using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSyncServiceTests
{
    [Fact]
    public async Task RetriesTransientFailureAfterTwoSecondsAndPublishesRecoveredLyrics()
    {
        var clock = new RetryTimeProvider();
        var coordinator = new SequenceCoordinator(
            new(null, LyricResolutionStatus.TransientFailure),
            LyricResolutionResult.FromLyrics(CreateResolved("recovered", "Recovered lyric")));
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        var snapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack());
        Assert.Equal(LyricSyncService.SearchingText, (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
        var retry = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), retry.Delay);
        Assert.Equal(1, coordinator.ResolveCallCount);
        retry.Fire();
        await WaitForAcquisitionAsync(service, LyricAcquisitionKind.Remote);
        Assert.Equal("Recovered lyric", (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
        Assert.Equal(2, coordinator.ResolveCallCount);
        Assert.False(clock.HasPendingTimer);
    }

    [Fact]
    public async Task ExhaustsTwoRetriesWithBackoffAndDoesNotRestartOnEveryTick()
    {
        var clock = new RetryTimeProvider();
        var coordinator = new SequenceCoordinator(new LyricResolutionResult(null, LyricResolutionStatus.TransientFailure));
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        var snapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack());
        await service.GetDisplayFrameAsync(snapshot);
        var first = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromSeconds(2), first.Delay);
        first.Fire();
        var second = await clock.NextTimerAsync();
        Assert.Equal(TimeSpan.FromSeconds(5), second.Delay);
        second.Fire();
        await WaitForAcquisitionAsync(service, LyricAcquisitionKind.NotFound);
        for (var tick = 0; tick < 10; tick++)
            Assert.Equal(LyricSyncService.NoLyricsText, (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
        Assert.Equal(3, coordinator.ResolveCallCount);
        Assert.False(clock.HasPendingTimer);
    }

    [Theory]
    [InlineData(LyricResolutionStatus.NotFound)]
    [InlineData(LyricResolutionStatus.Failed)]
    [InlineData(LyricResolutionStatus.Unavailable)]
    public async Task DoesNotRetryNonTransientOutcomes(LyricResolutionStatus status)
    {
        var clock = new RetryTimeProvider();
        var coordinator = new SequenceCoordinator(new LyricResolutionResult(null, status));
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        var snapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack());
        Assert.Equal(LyricSyncService.NoLyricsText, (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
        await service.GetDisplayFrameAsync(snapshot);
        Assert.Equal(1, coordinator.ResolveCallCount);
        Assert.False(clock.HasPendingTimer);
    }

    [Fact]
    public async Task StopsRetryingWhenTheNextAttemptFindsNoCandidates()
    {
        var clock = new RetryTimeProvider();
        var coordinator = new SequenceCoordinator(new(null, LyricResolutionStatus.TransientFailure), new(null, LyricResolutionStatus.NotFound));
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack()));
        (await clock.NextTimerAsync()).Fire();
        await WaitForAcquisitionAsync(service, LyricAcquisitionKind.NotFound);
        Assert.Equal(2, coordinator.ResolveCallCount);
        Assert.False(clock.HasPendingTimer);
    }

    [Theory]
    [InlineData("track-change")]
    [InlineData("no-track")]
    [InlineData("manual-selection")]
    [InlineData("dispose")]
    public async Task PendingRetryIsCanceledWhenItNoLongerOwnsTheTrack(string action)
    {
        var clock = new RetryTimeProvider();
        var coordinator = new SequenceCoordinator(
            new(null, LyricResolutionStatus.TransientFailure),
            LyricResolutionResult.FromLyrics(CreateResolved("new", "New lyric")));
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        var track = CreateTrack();
        var snapshot = new PlaybackSnapshot(true, TimeSpan.Zero, track);
        await service.GetDisplayFrameAsync(snapshot);
        var retry = await clock.NextTimerAsync();
        switch (action)
        {
            case "track-change":
                Assert.Equal("New lyric", (await service.GetDisplayFrameAsync(snapshot with { Track = CreateTrack("Next song") })).CurrentLine);
                break;
            case "no-track":
                await service.GetDisplayFrameAsync(snapshot with { Track = null });
                break;
            case "manual-selection":
                Assert.True(service.TryApplyResolvedLyrics(track, CreateResolved("manual", "Manual lyric")));
                break;
            default:
                service.Dispose();
                break;
        }
        Assert.True(retry.IsDisposed);
        retry.Fire();
        await Task.Yield();
        Assert.Equal(action == "track-change" ? 2 : 1, coordinator.ResolveCallCount);
        if (action == "manual-selection")
            Assert.Equal("Manual lyric", (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
    }

    private static async Task WaitForAcquisitionAsync(LyricSyncService service, LyricAcquisitionKind acquisition)
    {
        for (var attempt = 0; attempt < 100 && service.CurrentLyricAcquisition != acquisition; attempt++)
            await Task.Delay(5);
        Assert.Equal(acquisition, service.CurrentLyricAcquisition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateNetworkFailureCannotReplaceManuallySelectedLyricsOrScheduleARetry(bool throws)
    {
        var clock = new RetryTimeProvider();
        var coordinator = new LateFailureCoordinator();
        using var service = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero, timeProvider: clock);
        var track = CreateTrack();
        var snapshot = new PlaybackSnapshot(true, TimeSpan.Zero, track);
        await service.GetDisplayFrameAsync(snapshot);
        Assert.True(service.TryApplyResolvedLyrics(track, CreateResolved("manual", "Manual lyric")));
        if (throws) coordinator.Result.SetException(new System.Net.Http.HttpRequestException("late failure"));
        else coordinator.Result.SetResult(new(null, LyricResolutionStatus.TransientFailure));
        await Task.Delay(20);
        Assert.Equal(LyricAcquisitionKind.Remote, service.CurrentLyricAcquisition);
        Assert.Equal("Manual lyric", (await service.GetDisplayFrameAsync(snapshot)).CurrentLine);
        Assert.False(clock.HasPendingTimer);
    }

    private sealed class LateFailureCoordinator : ILyricResolutionCoordinator
    {
        public TaskCompletionSource<LyricResolutionResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LyricResolutionResult> ResolveWithResultAsync(TrackInfo track, CancellationToken cancellationToken = default) => Result.Task;
        public async Task<ResolvedLyrics?> ResolveAsync(TrackInfo track, CancellationToken cancellationToken = default) =>
            (await ResolveWithResultAsync(track, cancellationToken)).Lyrics;
        public void Dispose() { }
    }

    private sealed class SequenceCoordinator(params LyricResolutionResult[] results) : ILyricResolutionCoordinator
    {
        public int ResolveCallCount { get; private set; }
        public Task<LyricResolutionResult> ResolveWithResultAsync(TrackInfo track, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(ResolveCallCount++, results.Length - 1);
            return Task.FromResult(results[index]);
        }
        public async Task<ResolvedLyrics?> ResolveAsync(TrackInfo track, CancellationToken cancellationToken = default) =>
            (await ResolveWithResultAsync(track, cancellationToken)).Lyrics;
        public void Dispose() { }
    }

    private sealed class RetryTimeProvider : TimeProvider
    {
        private readonly System.Threading.Channels.Channel<RetryTimer> _timers =
            global::System.Threading.Channels.Channel.CreateUnbounded<RetryTimer>();
        public bool HasPendingTimer => _timers.Reader.TryPeek(out _);
        public async Task<RetryTimer> NextTimerAsync() =>
            await _timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new RetryTimer(callback, state, dueTime);
            _timers.Writer.TryWrite(timer);
            return timer;
        }
    }

    private sealed class RetryTimer(TimerCallback callback, object? state, TimeSpan delay) : ITimer
    {
        public TimeSpan Delay { get; } = delay;
        public bool IsDisposed { get; private set; }
        public void Fire() { if (!IsDisposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => IsDisposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task GetDisplayFrameAsyncAppliesPlayerAndTrackOffsetsBeforeSelectingTheLine()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "offsets",
            new ParsedLyricLine(TimeSpan.Zero, null, "Intro"),
            new ParsedLyricLine(TimeSpan.FromSeconds(10), null, "Verse", "主歌"),
            new ParsedLyricLine(TimeSpan.FromSeconds(20), null, "Outro")));
        using var service = new LyricSyncService(
            coordinator,
            getPlayerLeadTime: _ => TimeSpan.FromMilliseconds(500),
            getTrackLeadTime: (_, _) => TimeSpan.FromMilliseconds(500),
            metadataStabilizationDelay: TimeSpan.Zero);
        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: TimeSpan.FromSeconds(9),
            Track: CreateTrack());

        var frame = await service.GetDisplayFrameAsync(snapshot);

        Assert.Equal("Verse", frame.CurrentLine);
        Assert.Equal("主歌", frame.CurrentTranslation);
        Assert.Equal("Outro", frame.NextLine);
        Assert.Null(frame.NextTranslation);
        Assert.True(frame.HasTrackTranslation);
        Assert.Equal(1, frame.CurrentLineIndex);
        Assert.Equal(0, frame.LineProgress);
        Assert.Equal(1, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncUsesOneResolvedResultAndExcludesInformationLines()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "information-filter",
            new ParsedLyricLine(TimeSpan.Zero, null, "Opening"),
            new ParsedLyricLine(
                TimeSpan.FromSeconds(5),
                null,
                "Lyrics provided by Example",
                isInformationLine: true),
            new ParsedLyricLine(TimeSpan.FromSeconds(10), null, "Composer: The Band")));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var snapshot = new PlaybackSnapshot(true, TimeSpan.FromSeconds(10), CreateTrack());

        var frame = await service.GetDisplayFrameAsync(snapshot);

        Assert.Equal("Composer: The Band", frame.CurrentLine);
        Assert.Equal(1, frame.CurrentLineIndex);
        Assert.NotEqual("Lyrics provided by Example", frame.CurrentLine);
        Assert.NotEqual("Lyrics provided by Example", frame.NextLine);
        Assert.Equal(1, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncCalculatesContinuousWordScanProgressAndRecalculatesAfterSeek()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "word-scan",
            new ParsedLyricLine(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(3),
                "Hi there",
                segments:
                [
                    new ParsedLyricSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hi"),
                    new ParsedLyricSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "there")
                ])));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var firstSegmentHalf = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromMilliseconds(500), track));
        var secondSegmentHalf = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromSeconds(2), track));
        var seekBack = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(false, TimeSpan.FromMilliseconds(250), track));
        var completed = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromSeconds(3), track));

        Assert.Equal(1d / 8d, firstSegmentHalf.WordScanProgress!.Value, precision: 6);
        Assert.Equal(5.5d / 8d, secondSegmentHalf.WordScanProgress!.Value, precision: 6);
        Assert.Equal(0.5d / 8d, seekBack.WordScanProgress!.Value, precision: 6);
        Assert.Equal(1d, completed.WordScanProgress!.Value);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncLimitsWordScanProgressToOriginalTextWhenTranslationIsShown()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "word-scan-translation",
            new ParsedLyricLine(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                "Hi",
                translation: "TR",
                segments: [new ParsedLyricSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hi")])));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var half = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromMilliseconds(500), track));
        var completed = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromSeconds(1), track));

        Assert.Equal("Hi", half.CurrentLine);
        Assert.Equal("TR", half.CurrentTranslation);
        Assert.Equal(1d / 2d, half.WordScanProgress!.Value, precision: 6);
        Assert.True(half.HasTrackTranslation);
        Assert.Equal("TR", completed.CurrentTranslation);
        Assert.Equal(1d, completed.WordScanProgress!.Value);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncReturnsStructuredTranslationsAndStableTrackMarker()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "translation-structure",
            new ParsedLyricLine(TimeSpan.Zero, null, "One", translation: "一"),
            new ParsedLyricLine(TimeSpan.FromSeconds(10), null, "Two"),
            new ParsedLyricLine(TimeSpan.FromSeconds(20), null, "Three", translation: "三")));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var first = await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        var middle = await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.FromSeconds(10), track));
        var last = await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.FromSeconds(20), track));

        Assert.Equal("一", first.CurrentTranslation);
        Assert.Null(first.NextTranslation);
        Assert.True(first.HasTrackTranslation);
        Assert.Null(middle.CurrentTranslation);
        Assert.Equal("三", middle.NextTranslation);
        Assert.True(middle.HasTrackTranslation);
        Assert.Equal("三", last.CurrentTranslation);
        Assert.Null(last.NextTranslation);
        Assert.True(last.HasTrackTranslation);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncLeavesWordScanProgressNullWithoutSyllableData()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved(
            "word-scan-empty",
            new ParsedLyricLine(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Line without segments")));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var frame = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.FromMilliseconds(500), track));

        Assert.Null(frame.WordScanProgress);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncUsesCorrectedDurationAfterMetadataStabilizationWindow()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved("stabilized", "Stabilized lyric"));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.FromMilliseconds(50));
        var inheritedTrack = CreateTrack(duration: TimeSpan.FromSeconds(242));
        var correctedTrack = inheritedTrack with { Duration = TimeSpan.FromSeconds(389) };

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, inheritedTrack));
        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, correctedTrack));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        Assert.Equal(1, coordinator.ResolveCallCount);
        Assert.Equal(TimeSpan.FromSeconds(389), Assert.Single(coordinator.SearchDurations));
    }

    [Fact]
    public async Task GetDisplayFrameAsyncCorrectsMaterialDurationChangeOnceAndIgnoresCanceledLateResult()
    {
        var coordinator = new DurationCorrectionCoordinator();
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var inheritedTrack = CreateTrack(duration: TimeSpan.FromSeconds(242));
        var correctedTrack = inheritedTrack with { Duration = TimeSpan.FromSeconds(389) };
        var inheritedSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, inheritedTrack);
        var correctedSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, correctedTrack);

        await service.GetDisplayFrameAsync(inheritedSnapshot);
        await coordinator.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await service.GetDisplayFrameAsync(correctedSnapshot);
        await coordinator.FirstSearchCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await coordinator.SecondSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        coordinator.CompleteSecond(CreateResolved("corrected", "Corrected lyric"));
        await coordinator.SecondSearchReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        var correctedFrame = await service.GetDisplayFrameAsync(correctedSnapshot);

        Assert.Equal("Corrected lyric", correctedFrame.CurrentLine);
        Assert.Equal(2, coordinator.ResolveCallCount);
        Assert.Equal(
            [TimeSpan.FromSeconds(242), TimeSpan.FromSeconds(389)],
            coordinator.SearchDurations);

        coordinator.CompleteFirst(CreateResolved("stale", "Stale lyric"));
        await coordinator.FirstSearchReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        var latestFrame = await service.GetDisplayFrameAsync(correctedSnapshot);

        Assert.Equal("Corrected lyric", latestFrame.CurrentLine);
        Assert.Equal(2, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncDoesNotRepeatSearchForMinorDurationChanges()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved("duration-stable", "Stable lyric"));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var initialTrack = CreateTrack(duration: TimeSpan.FromSeconds(242));

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, initialTrack));
        await service.GetDisplayFrameAsync(new PlaybackSnapshot(
            true,
            TimeSpan.Zero,
            initialTrack with { Duration = TimeSpan.FromSeconds(250) }));
        Assert.Equal(1, coordinator.ResolveCallCount);

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(
            true,
            TimeSpan.Zero,
            initialTrack with { Duration = TimeSpan.FromSeconds(389) }));
        Assert.Equal(2, coordinator.ResolveCallCount);

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(
            true,
            TimeSpan.Zero,
            initialTrack with { Duration = TimeSpan.FromSeconds(420) }));
        Assert.Equal(2, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task GetDisplayFrameAsyncWhenPreviousSearchCompletesLateKeepsNewerTrackLyrics()
    {
        var coordinator = new OutOfOrderCoordinator();
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var firstSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack("First track"));
        var secondSnapshot = new PlaybackSnapshot(true, TimeSpan.Zero, CreateTrack("Second track"));

        await service.GetDisplayFrameAsync(firstSnapshot);
        await coordinator.FirstSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var secondFrame = await service.GetDisplayFrameAsync(secondSnapshot);
        Assert.Equal("Second lyric", secondFrame.CurrentLine);

        coordinator.CompleteFirstSearch();
        await coordinator.FirstSearchReturned.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        var latestFrame = await service.GetDisplayFrameAsync(secondSnapshot);
        Assert.True(coordinator.FirstRequestWasCanceled);
        Assert.Equal("Second lyric", latestFrame.CurrentLine);
    }

    [Fact]
    public async Task TryApplyResolvedLyricsForSameTrackCancelsSearchAndPublishesCandidate()
    {
        var coordinator = new BlockingCoordinator();
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var applied = service.TryApplyResolvedLyrics(track, CreateResolved("manual", "Manual lyric"));

        Assert.True(applied);
        await coordinator.SearchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var frame = await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        Assert.Equal("Manual lyric", frame.CurrentLine);
    }

    [Fact]
    public async Task TryApplyResolvedLyricsRejectsDifferentTrack()
    {
        var coordinator = new BlockingCoordinator();
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var track = CreateTrack();

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, track));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var applied = service.TryApplyResolvedLyrics(
            track with { Title = "A different song" },
            CreateResolved("manual", "Manual lyric"));

        Assert.False(applied);
        Assert.False(coordinator.SearchCancelled.Task.IsCompleted);
    }

    [Fact]
    public async Task TryApplyResolvedLyricsIgnoresLaterDurationCorrectionForTheSameTrack()
    {
        var coordinator = new ImmediateCoordinator(CreateResolved("automatic", "Automatic lyric"));
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var initialTrack = CreateTrack(duration: TimeSpan.FromSeconds(242));

        await service.GetDisplayFrameAsync(new PlaybackSnapshot(true, TimeSpan.Zero, initialTrack));
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        Assert.True(service.TryApplyResolvedLyrics(initialTrack, CreateResolved("manual", "Manual lyric")));

        var correctedTrack = initialTrack with { Duration = TimeSpan.FromSeconds(389) };
        var frame = await service.GetDisplayFrameAsync(
            new PlaybackSnapshot(true, TimeSpan.Zero, correctedTrack));

        Assert.Equal("Manual lyric", frame.CurrentLine);
        Assert.Equal(1, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task DisposeCancelsActiveSearchAndDisposesCoordinator()
    {
        var coordinator = new BlockingCoordinator();
        using var service = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero);
        var snapshot = new PlaybackSnapshot(
            IsPlaying: true,
            Position: TimeSpan.Zero,
            Track: CreateTrack());

        await service.GetDisplayFrameAsync(snapshot);
        await coordinator.SearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        service.Dispose();

        await coordinator.SearchCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(coordinator.IsDisposed);
    }

    private static ResolvedLyrics CreateResolved(string candidateId, params ParsedLyricLine[] lines) =>
        new(
            new ParsedLyrics(
                lines,
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.Lrc),
            new LyricProviderId("Test"),
            candidateId,
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>());

    private static ResolvedLyrics CreateResolved(string candidateId, string line) =>
        CreateResolved(candidateId, new ParsedLyricLine(TimeSpan.Zero, null, line));

    private static TrackInfo CreateTrack(
        string title = "Midnight City",
        TimeSpan? duration = null) => new(
        "track-id",
        title,
        "M83",
        "Hurry Up, We're Dreaming",
        "Spotify",
        duration ?? TimeSpan.FromSeconds(244));

    private sealed class BlockingCoordinator : ILyricResolutionCoordinator
    {
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SearchCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsDisposed { get; private set; }

        public async Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            SearchStarted.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SearchCancelled.TrySetResult();
                throw;
            }

            return null;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class ImmediateCoordinator(ResolvedLyrics resolved) : ILyricResolutionCoordinator
    {
        public TaskCompletionSource SearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<TimeSpan> SearchDurations { get; } = [];
        public int ResolveCallCount { get; private set; }

        public Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            SearchDurations.Add(track.Duration);
            SearchStarted.TrySetResult();
            return Task.FromResult<ResolvedLyrics?>(resolved);
        }

        public void Dispose()
        {
        }
    }

    private sealed class DurationCorrectionCoordinator : ILyricResolutionCoordinator
    {
        private readonly TaskCompletionSource<ResolvedLyrics?> _firstResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ResolvedLyrics?> _secondResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSearchCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSearchReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondSearchReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<TimeSpan> SearchDurations { get; } = [];
        public int ResolveCallCount { get; private set; }

        public Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            SearchDurations.Add(track.Duration);
            return ResolveCallCount switch
            {
                1 => WaitForResultAsync(
                    _firstResult,
                    FirstSearchStarted,
                    FirstSearchCanceled,
                    FirstSearchReturned,
                    cancellationToken),
                2 => WaitForResultAsync(
                    _secondResult,
                    SecondSearchStarted,
                    canceled: null,
                    returned: SecondSearchReturned,
                    cancellationToken: cancellationToken),
                _ => Task.FromResult<ResolvedLyrics?>(null)
            };
        }

        public void CompleteFirst(ResolvedLyrics resolved) => _firstResult.TrySetResult(resolved);

        public void CompleteSecond(ResolvedLyrics resolved) => _secondResult.TrySetResult(resolved);

        public void Dispose()
        {
        }

        private static async Task<ResolvedLyrics?> WaitForResultAsync(
            TaskCompletionSource<ResolvedLyrics?> result,
            TaskCompletionSource started,
            TaskCompletionSource? canceled,
            TaskCompletionSource? returned,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            using var registration = cancellationToken.Register(() => canceled?.TrySetResult());
            var resolved = await result.Task;
            returned?.TrySetResult();
            return resolved;
        }
    }

    private sealed class OutOfOrderCoordinator : ILyricResolutionCoordinator
    {
        private readonly TaskCompletionSource<ResolvedLyrics> _firstSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstSearchReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FirstRequestWasCanceled { get; private set; }

        public Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            return track.Title == "First track"
                ? ResolveFirstAsync(cancellationToken)
                : Task.FromResult<ResolvedLyrics?>(CreateResolved("second", "Second lyric"));
        }

        public void CompleteFirstSearch()
        {
            _firstSearch.TrySetResult(CreateResolved("first", "First lyric"));
        }

        public void Dispose()
        {
        }

        private async Task<ResolvedLyrics?> ResolveFirstAsync(CancellationToken cancellationToken)
        {
            FirstSearchStarted.TrySetResult();
            var result = await _firstSearch.Task;
            FirstRequestWasCanceled = cancellationToken.IsCancellationRequested;
            FirstSearchReturned.TrySetResult();
            return result;
        }
    }
}
