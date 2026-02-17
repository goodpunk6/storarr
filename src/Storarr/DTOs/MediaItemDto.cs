using System;
using Storarr.Models;

namespace Storarr.DTOs
{
    public class MediaItemDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public string? JellyfinId { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public FileState CurrentState { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastWatchedAt { get; set; }
        public DateTime? StateChangedAt { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public long? FileSize { get; set; }

        // Computed fields
        public int? DaysUntilTransition { get; set; }
        public string? TransitionType { get; set; }
    }

    public class CreateMediaItemDto
    {
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public string? JellyfinId { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
    }

    public class MediaItemListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public FileState CurrentState { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public DateTime? LastWatchedAt { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public long? FileSize { get; set; }
        public int? DaysUntilTransition { get; set; }
    }
}
