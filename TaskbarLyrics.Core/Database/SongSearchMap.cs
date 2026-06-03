using System.ComponentModel.DataAnnotations;

namespace TaskbarLyrics.Core.Database;

public class SongSearchMap
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string OriginalTitle { get; set; } = string.Empty;

    [Required]
    public string OriginalArtist { get; set; } = string.Empty;

    [Required]
    public string OriginalAlbum { get; set; } = string.Empty;

    public string MappedTitle { get; set; } = string.Empty;
    public string MappedArtist { get; set; } = string.Empty;
    public string MappedAlbum { get; set; } = string.Empty;

    public bool IsMarkedAsPureMusic { get; set; }
    public string? PreferredProvider { get; set; }
}
