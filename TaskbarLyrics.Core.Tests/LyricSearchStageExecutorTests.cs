using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricSearchStageExecutorTests
{
    [Fact]
    public async Task ExecuteAsyncStopsAfterFirstStageWithAdmittedCandidate()
    {
        var plan = CreatePlan("exact", "relaxed", "fallback");
        var calls = new List<string>();

        var result = await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            (variant, _) =>
            {
                calls.Add(variant.Id);
                return Task.FromResult<IReadOnlyList<SourceTrackCandidate>>(variant.Id switch
                {
                    "exact" =>
                    [
                        CreateCandidate("rejected", "Run Away With Me (Simlish Version)", variant.Id),
                        CreateCandidate("rejected", "another rejected title", variant.Id)
                    ],
                    "relaxed" =>
                    [
                        CreateCandidate("admitted", "Run Away With Me", variant.Id),
                        CreateCandidate("admitted", "Run Away With Me", variant.Id),
                        CreateCandidate("second", "Another song", variant.Id)
                    ],
                    _ => throw new InvalidOperationException("A later variant must not execute.")
                });
            });

        Assert.Equal(["exact", "relaxed"], calls);
        Assert.Equal(["admitted", "second"], result.Select(candidate => candidate.CandidateId));
        Assert.All(result, candidate => Assert.Equal("relaxed", candidate.QueryVariantId));
    }

    [Fact]
    public async Task ExecuteAsyncContinuesAfterStageWhoseCandidatesAreAllRejected()
    {
        var plan = CreatePlan("exact", "relaxed");
        var calls = new List<string>();

        var result = await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            (variant, _) =>
            {
                calls.Add(variant.Id);
                return Task.FromResult<IReadOnlyList<SourceTrackCandidate>>(
                    variant.Id == "exact"
                        ? [CreateCandidate("rejected", "Run Away With Me (Simlish Version)", variant.Id)]
                        : [CreateCandidate("admitted", "Run Away With Me", variant.Id)]);
            });

        Assert.Equal(["exact", "relaxed"], calls);
        Assert.Equal(["admitted"], result.Select(candidate => candidate.CandidateId));
    }

    [Fact]
    public async Task ExecuteAsyncReturnsAllStagesWhenEveryCandidateIsRejected()
    {
        var plan = CreatePlan("exact", "relaxed", "fallback");
        var sharedFromFirstStage = CreateCandidate(
            "shared",
            "Run Away With Me (Simlish Version)",
            "exact");
        var sharedFromLaterStage = CreateCandidate(
            "shared",
            "another rejected title",
            "relaxed");

        var result = await LyricSearchStageExecutor.ExecuteAsync(
            plan,
            (variant, _) => Task.FromResult<IReadOnlyList<SourceTrackCandidate>>(variant.Id switch
            {
                "exact" => [sharedFromFirstStage, CreateCandidate("first", "another rejected title", variant.Id)],
                "relaxed" => [sharedFromLaterStage, CreateCandidate("second", "still rejected", variant.Id)],
                "fallback" => [CreateCandidate("first", "a different rejected title", variant.Id)],
                _ => []
            }));

        Assert.Equal(["shared", "first", "second"], result.Select(candidate => candidate.CandidateId));
        Assert.Same(sharedFromFirstStage, result[0]);
        Assert.Equal("exact", result[0].QueryVariantId);
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesRetrievalExceptions()
    {
        var plan = CreatePlan("exact");
        var expected = new InvalidOperationException("search failed");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LyricSearchStageExecutor.ExecuteAsync(
                plan,
                (_, _) => Task.FromException<IReadOnlyList<SourceTrackCandidate>>(expected)));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesCancellationBeforeRetrieval()
    {
        var plan = CreatePlan("exact");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LyricSearchStageExecutor.ExecuteAsync(
                plan,
                (_, _) =>
                {
                    invoked = true;
                    return Task.FromResult<IReadOnlyList<SourceTrackCandidate>>([]);
                },
                cancellation.Token));

        Assert.False(invoked);
    }

    [Fact]
    public async Task ExecuteAsyncPropagatesCancellationFromRetrieval()
    {
        var plan = CreatePlan("exact");
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var execution = LyricSearchStageExecutor.ExecuteAsync(
            plan,
            async (_, token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return (IReadOnlyList<SourceTrackCandidate>)[];
            },
            cancellation.Token);

        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    private static LyricSearchPlan CreatePlan(params string[] variantIds)
    {
        var identity = TrackIdentity.FromTrackInfo(new TrackInfo(
            "executor-track",
            "Run Away With Me",
            "Carly Rae Jepsen",
            "Emotion",
            "Spotify",
            TimeSpan.FromSeconds(210)));
        var variants = variantIds
            .Select(id => new SearchQueryVariant(
                id,
                identity.Title,
                identity.Artists,
                identity.Album,
                identity.Duration,
                []))
            .ToArray();
        return new LyricSearchPlan(identity, variants);
    }

    private static SourceTrackCandidate CreateCandidate(
        string candidateId,
        string title,
        string variantId) => new(
        KnownLyricProviders.QQMusic,
        candidateId,
        title,
        ["Carly Rae Jepsen"],
        "Emotion",
        TimeSpan.FromSeconds(210),
        variantId,
        new Dictionary<string, string>());
}
