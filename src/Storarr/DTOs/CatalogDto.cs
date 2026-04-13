using System.Collections.Generic;
using Storarr.Models;

namespace Storarr.DTOs
{
    public class CatalogGroupDto
    {
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string? PosterUrl { get; set; }
        public int TotalEpisodes { get; set; }
        public int TrackedEpisodes { get; set; }
        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public Dictionary<string, int> StateBreakdown { get; set; } = new();
        public bool IsExcluded { get; set; }
        public List<CatalogEpisodeDto> Episodes { get; set; } = new();
    }

    public class CatalogEpisodeDto
    {
        public int? MediaItemId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public long? FileSize { get; set; }
        public bool IsExcluded { get; set; }
        public string? FilePath { get; set; }
    }

    public class EnsureTrackedRequestDto
    {
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public MediaType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public int? TmdbId { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    public class EnsureTrackedResponseDto
    {
        public int MediaItemId { get; set; }
        public bool Created { get; set; }
    }
}
