using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class QqMusicContentParityTests
{
    public static TheoryData<string> XmlCases => new()
    {
        "<QrcInfos><Lyric_1 LyricType=\"1\" LyricContent=\"[1000,1000]A & B(1000,1000)\"/></QrcInfos>",
        "<QrcInfos><Lyric_1 LyricType=\"1\" LyricContent=\"[1000,1000]He said \"go\"(1000,1000)\"/></QrcInfos>",
        "<QrcInfos><=bad/><Lyric_1 LyricType=\"1\" LyricContent=\"[1000,1000]A(1000,1000)\"/></QrcInfos>",
        "<QrcInfos><Lyric_1 LyricContent='[1000,1000]A &amp; B(1000,1000)&#10;[2000,1000]C(2000,1000)' LyricType='1'/></QrcInfos>",
        "<QrcInfos><Lyric_1 LyricType=\"1\" LyricContent=\"[1000,1000]A(1000,1000)\r\n[2000,1000]B(2000,1000)\"/></QrcInfos>"
    };

    [Theory]
    [MemberData(nameof(XmlCases))]
    public async Task XmlExtractionAndBusinessProjectionMatchPinnedLyricify(string xml)
    {
        var nativeText = Lyricify.Lyrics.Decrypter.Qrc.XmlUtils.Create(xml)
            .SelectSingleNode("//Lyric_1")!.Attributes!["LyricContent"]!.Value;
        var extracted = QqMusicResponseMapper.UnwrapQrcContent(xml);
        Assert.Equal(nativeText, extracted);
        var native = Lyricify.Lyrics.Parsers.QrcParser.Parse(nativeText);
        Assert.NotNull(native.Lines);
        var parsed = await new LyricifyPayloadParser().ParseAsync(new DecodedLyricPayload(
            KnownLyricProviders.QQMusic, "parity", LyricPayloadFormat.Qrc, extracted, null, false, new Dictionary<string, string>()));
        Assert.Equal(native.Lines.Count, parsed.Lines.Count);
        foreach (var (line, actual) in native.Lines.Zip(parsed.Lines))
        {
            Assert.Equal(line.Text, actual.Text);
            Assert.Equal(TimeSpan.FromMilliseconds(line.StartTime!.Value), actual.StartTime);
            Assert.Equal(TimeSpan.FromMilliseconds(line.EndTime!.Value), actual.EndTime);
            var syllables = Assert.IsType<Lyricify.Lyrics.Models.SyllableLineInfo>(line).Syllables;
            Assert.Equal(syllables.Count, actual.Segments.Count);
            foreach (var (expected, segment) in syllables.Zip(actual.Segments))
            {
                Assert.Equal(expected.Text, segment.Text);
                Assert.Equal(TimeSpan.FromMilliseconds(expected.StartTime), segment.StartTime);
                Assert.Equal(TimeSpan.FromMilliseconds(expected.EndTime), segment.EndTime);
            }
        }
    }

    [Theory]
    [InlineData("[1000,1000]A(1000,500)!(1500,0)B(1500,500)")]
    [InlineData("[1000,1000]A(1000,1000)!(2000,0)")]
    public async Task InstantaneousQqMarkersRetainTextAndOrderingWithoutInventingVisibleDuration(string qrc)
    {
        var native = Lyricify.Lyrics.Parsers.QrcParser.Parse(qrc);
        Assert.NotNull(native.Lines);
        var parsed = await new LyricifyPayloadParser().ParseAsync(new DecodedLyricPayload(
            KnownLyricProviders.QQMusic, "instant", LyricPayloadFormat.Qrc, qrc, null, false, new Dictionary<string, string>()));
        var nativeLine = Assert.IsType<Lyricify.Lyrics.Models.SyllableLineInfo>(Assert.Single(native.Lines));
        var actual = Assert.Single(parsed.Lines);
        Assert.Equal(nativeLine.Text, actual.Text);
        Assert.Equal(nativeLine.Syllables.Count, actual.Segments.Count);
        foreach (var (expected, segment) in nativeLine.Syllables.Zip(actual.Segments))
        {
            Assert.Equal(expected.Text, segment.Text);
            Assert.Equal(TimeSpan.FromMilliseconds(expected.StartTime), segment.StartTime);
            Assert.Equal(expected.StartTime == expected.EndTime ? TimeSpan.FromTicks(1)
                : TimeSpan.FromMilliseconds(expected.EndTime - expected.StartTime), segment.EndTime - segment.StartTime);
        }
        Assert.True(actual.EndTime >= actual.Segments.Max(segment => segment.EndTime));
    }
}
