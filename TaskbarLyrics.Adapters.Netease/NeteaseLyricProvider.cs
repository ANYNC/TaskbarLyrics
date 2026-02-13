using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Adapters.Netease;

public sealed class NeteaseLyricProvider : ILyricProvider
{
    public string SourceApp => "Netease";

    public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(track.SourceApp, SourceApp, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<LyricDocument?>(null);
        }

        var lines = new[]
        {
            new LyricLine(TimeSpan.FromSeconds(0), "TaskbarLyrics MVP - 网易云适配器"),
            new LyricLine(TimeSpan.FromSeconds(5), "先跑通显示链路，再替换真实歌词抓取"),
            new LyricLine(TimeSpan.FromSeconds(10), "下一步可接入 SMTC + 歌词接口"),
            new LyricLine(TimeSpan.FromSeconds(15), "支持歌词偏移与逐行同步"),
            new LyricLine(TimeSpan.FromSeconds(20), "后续可扩展更多音乐平台")
        };

        return Task.FromResult<LyricDocument?>(new LyricDocument(lines));
    }
}
