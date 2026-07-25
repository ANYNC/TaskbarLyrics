using TaskbarLyrics.Core.Models;
using TaskbarLyrics.Core.Services;
using Xunit;

namespace TaskbarLyrics.Core.Tests;

public sealed class LyricProviderBaseTests
{
    [Fact]
    public void ParseLrc_AppliesOffsetAndExpandsMultipleTimestamps()
    {
        var provider = new ParserProvider();

        var lines = provider.Parse("[offset:+500]\n[00:01.20][00:02.00]Hello world");

        Assert.Collection(
            lines,
            first =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1700), first.Timestamp);
                Assert.Equal("Hello world", first.Text);
            },
            second =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(2500), second.Timestamp);
                Assert.Equal("Hello world", second.Text);
            });
    }

    [Fact]
    public void ParseLrc_ClampsNegativeTimestampAfterOffset()
    {
        var provider = new ParserProvider();

        var line = Assert.Single(provider.Parse("[offset:-1500]\n[00:01.00]Opening"));

        Assert.Equal(TimeSpan.Zero, line.Timestamp);
        Assert.Equal("Opening", line.Text);
    }

    private sealed class ParserProvider : LyricProviderBase
    {
        private static readonly HttpClient HttpClient = new();

        public ParserProvider()
            : base(HttpClient)
        {
        }

        public override string SourceApp => "Test";

        public List<LyricLine> Parse(string content) => ParseLrc(content);

        protected override Task<LyricDocument?> ResolveRemoteAsync(
            TrackInfo track,
            CancellationToken cancellationToken) => Task.FromResult<LyricDocument?>(null);
    }
}
