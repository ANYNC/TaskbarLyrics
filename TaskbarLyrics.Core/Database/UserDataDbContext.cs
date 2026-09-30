using Microsoft.EntityFrameworkCore;

namespace TaskbarLyrics.Core.Database;

public sealed class UserDataDbContext : DbContext
{
    private readonly string? _databasePath;

    public UserDataDbContext()
    {
    }

    internal UserDataDbContext(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public DbSet<TrackLyricOffset> TrackLyricOffsets => Set<TrackLyricOffset>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var databasePath = _databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarLyrics",
            "database",
            "user_data.db");
        var dbDirectory = Path.GetDirectoryName(databasePath)!;
        Directory.CreateDirectory(dbDirectory);
        optionsBuilder.UseSqlite($"Data Source={databasePath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrackLyricOffset>()
            .HasIndex(offset => new
            {
                offset.NormalizedTitle,
                offset.NormalizedArtist,
                offset.NormalizedLyricSource,
                offset.DurationBucketSeconds
            })
            .IsUnique();
        modelBuilder.Entity<TrackLyricOffset>()
            .HasIndex(offset => offset.UpdatedAtUtcStorage)
            .HasDatabaseName("IX_TrackLyricOffsets_UpdatedAtUtc");
        modelBuilder.Entity<TrackLyricOffset>()
            .HasIndex(offset => new { offset.LyricSource, offset.UpdatedAtUtcStorage })
            .HasDatabaseName("IX_TrackLyricOffsets_LyricSource_UpdatedAtUtc");
    }
}
