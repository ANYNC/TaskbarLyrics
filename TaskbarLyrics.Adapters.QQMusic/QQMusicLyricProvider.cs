using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Adapters.QQMusic;

public sealed class QQMusicLyricProvider : ILyricProvider
{
    public string SourceApp => "QQMusic";

    public Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(track.SourceApp, SourceApp, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<LyricDocument?>(null);
        }

        var lines = new[]
        {
            new LyricLine(TimeSpan.FromSeconds(0), "TaskbarLyrics MVP - QQ音乐适配器"),
            new LyricLine(TimeSpan.FromSeconds(5), "后续替换为真实歌词来源"),
            new LyricLine(TimeSpan.FromSeconds(10), "统一接口已支持插件化扩展")
        };

        return Task.FromResult<LyricDocument?>(new LyricDocument(lines));
    }
}
