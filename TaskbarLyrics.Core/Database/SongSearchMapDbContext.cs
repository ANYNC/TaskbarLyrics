using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace TaskbarLyrics.Core.Database;

public class SongSearchMapDbContext : DbContext
{
    public DbSet<SongSearchMap> SongSearchMaps => Set<SongSearchMap>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbDirectory = Path.Combine(appData, "TaskbarLyrics", "database");
        
        try
        {
            Directory.CreateDirectory(dbDirectory);
        }
        catch
        {
            // Ignore folder creation errors, EF will throw during use if directory is inaccessible.
        }

        var dbPath = Path.Combine(dbDirectory, "song_maps.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 为原始元数据字段建立复合唯一索引，保障高频查询效率
        modelBuilder.Entity<SongSearchMap>()
            .HasIndex(s => new { s.OriginalTitle, s.OriginalArtist, s.OriginalAlbum })
            .IsUnique();
    }

    public static void InitializeDatabase()
    {
        try
        {
            using var context = new SongSearchMapDbContext();
            context.Database.EnsureCreated();
        }
        catch
        {
            // Ignore database initialization failures to prevent app crashing on startup.
        }
    }
}
