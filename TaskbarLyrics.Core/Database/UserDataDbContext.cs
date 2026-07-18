using Microsoft.EntityFrameworkCore;

namespace TaskbarLyrics.Core.Database;

public sealed class UserDataDbContext : DbContext
{
    public DbSet<TrackLyricOffset> TrackLyricOffsets => Set<TrackLyricOffset>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbDirectory = Path.Combine(appData, "TaskbarLyrics", "database");
        Directory.CreateDirectory(dbDirectory);
        optionsBuilder.UseSqlite($"Data Source={Path.Combine(dbDirectory, "user_data.db")}");
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
