using System;
using Storarr.Models;

namespace Storarr.DTOs
{
    public class ExcludedItemDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Count of media items that were removed when this exclusion was created
        /// </summary>
        public int RemovedMediaCount { get; set; }
    }

    public class CreateExcludedItemDto
    {
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public string? Reason { get; set; }
    }

    public class ExcludedItemListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
