using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSyncServiceTests
{
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
