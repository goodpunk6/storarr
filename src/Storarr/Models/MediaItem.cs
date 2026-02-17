using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Storarr.Models
{
    public class MediaItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public MediaType Type { get; set; }

        [MaxLength(100)]
        public string? JellyfinId { get; set; }

        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public int? JellyseerrRequestId { get; set; }

        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }

        // For TV shows - specific episode file IDs
        public int? SonarrFileId { get; set; }
        public int? RadarrFileId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public FileState CurrentState { get; set; } = FileState.Symlink;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastWatchedAt { get; set; }
        public DateTime? StateChangedAt { get; set; }

        // For series - track season and episode
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }

        // File size in bytes
        public long? FileSize { get; set; }

        // Exclude from automatic transitions
        public bool IsExcluded { get; set; } = false;
    }
}
