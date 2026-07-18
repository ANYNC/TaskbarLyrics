using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

namespace TaskbarLyrics.Core.Database;

public sealed class TrackLyricOffset
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string NormalizedTitle { get; set; } = string.Empty;

    [Required]
    public string NormalizedArtist { get; set; } = string.Empty;

    [Required]
    public string NormalizedLyricSource { get; set; } = string.Empty;

    public int DurationBucketSeconds { get; set; }
    public string DisplayTitle { get; set; } = string.Empty;
    public string DisplayArtist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string SourceApp { get; set; } = string.Empty;
    public string LyricSource { get; set; } = string.Empty;
    public string SongId { get; set; } = string.Empty;
    public int OffsetMilliseconds { get; set; }
    [Column("UpdatedAtUtc")]
    public string UpdatedAtUtcStorage { get; set; } = string.Empty;

    [NotMapped]
    public DateTimeOffset UpdatedAtUtc
    {
        get => DateTimeOffset.TryParse(
            UpdatedAtUtcStorage,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var value)
            ? value.ToUniversalTime()
            : DateTimeOffset.MinValue;
        set => UpdatedAtUtcStorage = value
            .ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
    }
}
