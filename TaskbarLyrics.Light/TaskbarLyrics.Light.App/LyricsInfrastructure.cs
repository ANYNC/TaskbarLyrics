using TaskbarLyrics.Core.Database;

namespace TaskbarLyrics.Light.App;

/// <summary>
/// 延迟初始化歌词检索依赖（SQLite 映射库等），避免启动时即加载 EF Core。
/// </summary>
internal static class LyricsInfrastructure
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            return;
        }

        SongSearchMapDbContext.InitializeDatabase();
    }
}
