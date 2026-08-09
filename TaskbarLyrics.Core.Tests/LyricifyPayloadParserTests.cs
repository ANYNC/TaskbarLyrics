using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricifyPayloadParserTests
{
    [Fact]
    public async Task ParseLrcFixtureAppliesOffsetExpandsTimestampsAndAlignsTranslation()
    {
        var parser = new LyricifyPayloadParser();
        var payload = CreatePayload(
            LyricPayloadFormat.Lrc,
            ReadFixture("baseline.lrc"),
            "[offset:+250]\n[00:01.00]translated fixture line\n[00:03.00]//");

        var parsed = await parser.ParseAsync(payload);

        Assert.Equal(LyricPayloadFormat.Lrc, parsed.Format);
        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        Assert.Equal(LyricTimingKind.LineTimed, parsed.TimingKind);
        Assert.Equal(3, parsed.Lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), parsed.Lines[0].StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(3250), parsed.Lines[1].StartTime);
        Assert.Equal(TimeSpan.FromMilliseconds(4250), parsed.Lines[2].StartTime);
        Assert.Equal("alpha fixture line", parsed.Lines[0].Text);
        Assert.Equal("translated fixture line", parsed.Lines[0].Translation);
        Assert.Null(parsed.Lines[1].Translation);
        Assert.All(parsed.Lines, line => Assert.Empty(line.Segments));
    }

    [Fact]
    public async Task ParseQrcFixtureRetainsAbsoluteLineAndSegmentTiming()
    {
        var parsed = await ParseFixtureAsync("qrc-provider-supplied.qrc", LyricPayloadFormat.Qrc);

        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        var line = Assert.Single(parsed.Lines, candidate => candidate.StartTime == TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMilliseconds(2800), line.EndTime);
        Assert.NotEmpty(line.Segments);
        Assert.Collection(
            line.Segments,
            segment =>
            {
                Assert.Equal(TimeSpan.FromSeconds(1), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.EndTime);
            },
            segment =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(2800), segment.EndTime);
            });
    }

    [Fact]
    public async Task ParseKrcFixtureRetainsAbsoluteLineAndSegmentTiming()
    {
        var parsed = await ParseFixtureAsync("krc-provider-supplied.krc", LyricPayloadFormat.Krc);

        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        var line = Assert.Single(parsed.Lines, candidate => candidate.StartTime == TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMilliseconds(2800), line.EndTime);
        Assert.NotEmpty(line.Segments);
        Assert.Collection(
            line.Segments,
            segment =>
            {
                Assert.Equal(TimeSpan.FromSeconds(1), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.EndTime);
            },
            segment =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(2800), segment.EndTime);
            });
    }

    [Fact]
    public async Task ParseYrcFixtureRetainsAbsoluteLineAndSegmentTiming()
    {
        var parsed = await ParseFixtureAsync("yrc-provider-supplied.yrc", LyricPayloadFormat.Yrc);

        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        var line = Assert.Single(parsed.Lines, candidate => candidate.StartTime == TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromMilliseconds(2800), line.EndTime);
        Assert.NotEmpty(line.Segments);
        Assert.Collection(
            line.Segments,
            segment =>
            {
                Assert.Equal(TimeSpan.FromSeconds(1), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.EndTime);
            },
            segment =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1300), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(2800), segment.EndTime);
            });
    }

    [Fact]
    public async Task ParseTtmlFixtureUsesExplicitTtmlParser()
    {
        var parsed = await ParseFixtureAsync("ttml-provider-supplied.ttml", LyricPayloadFormat.Ttml);

        Assert.Equal(LyricPayloadFormat.Ttml, parsed.Format);
        Assert.Equal(LyricTimingProvenance.ProviderSupplied, parsed.TimingProvenance);
        Assert.Contains(parsed.Lines, line => line.Text == "ttml zero fixture");
        Assert.Contains(parsed.Lines, line => line.Text == "metadata line");
    }

    [Fact]
    public async Task ParsePlainTextReturnsUnsyncedLines()
    {
        var parser = new LyricifyPayloadParser();
        var parsed = await parser.ParseAsync(CreatePayload(
            LyricPayloadFormat.PlainText,
            "plain first\n\nplain second"));

        Assert.Equal(LyricTimingKind.Unsynced, parsed.TimingKind);
        Assert.Equal(LyricTimingProvenance.Unknown, parsed.TimingProvenance);
        Assert.Equal(["plain first", "plain second"], parsed.Lines.Select(line => line.Text));
        Assert.All(parsed.Lines, line => Assert.Null(line.EndTime));
    }

    [Fact]
    public async Task ExplicitInformationLineMarkerIsPreservedWithoutTextHeuristics()
    {
        var parser = new LyricifyPayloadParser();
        var parsed = await parser.ParseAsync(CreatePayload(
            LyricPayloadFormat.Lrc,
            "[00:01.00]metadata line\n[00:02.00]Composer: The Band",
            diagnostics: new Dictionary<string, string>
            {
                [LyricifyPayloadParser.InformationLineStartTimesDiagnostic] = "1000"
            }));

        Assert.True(parsed.Lines[0].IsInformationLine);
        Assert.False(parsed.Lines[1].IsInformationLine);
    }

    [Fact]
    public async Task ParseEmptyOrInvalidPayloadFailsExplicitly()
    {
        var parser = new LyricifyPayloadParser();

        await Assert.ThrowsAsync<FormatException>(() => parser.ParseAsync(
            CreatePayload(LyricPayloadFormat.Lrc, string.Empty)));
        await Assert.ThrowsAsync<FormatException>(() => parser.ParseAsync(
            CreatePayload(LyricPayloadFormat.Lrc, "[not-a-time]invalid")));
        await Assert.ThrowsAsync<FormatException>(() => parser.ParseAsync(
            CreatePayload(LyricPayloadFormat.PlainText, "\n  \r\n")));
    }

    [Fact]
    public void ParserResultsExposeOnlyTaskbarLyricsCoreModels()
    {
        Assert.DoesNotContain(
            typeof(LyricifyPayloadParser).Assembly.GetTypes(),
            type => type.FullName?.StartsWith("Lyricify.", StringComparison.Ordinal) == true);
    }

    private static async Task<ParsedLyrics> ParseFixtureAsync(
        string fixtureName,
        LyricPayloadFormat format)
    {
        var parser = new LyricifyPayloadParser();
        return await parser.ParseAsync(CreatePayload(format, ReadFixture(fixtureName)));
    }

    private static DecodedLyricPayload CreatePayload(
        LyricPayloadFormat format,
        string? originalLyrics,
        string? translationLyrics = null,
        IReadOnlyDictionary<string, string>? diagnostics = null)
    {
        return new DecodedLyricPayload(
            KnownLyricProviders.QQMusic,
            $"a2-{format}",
            format,
            originalLyrics,
            translationLyrics,
            false,
            diagnostics ?? new Dictionary<string, string>());
    }

    private static string ReadFixture(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TaskbarLyrics.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory!.FullName,
            "TaskbarLyrics.Core.Tests",
            "TestData",
            "Lyrics",
            fileName));
    }
}
