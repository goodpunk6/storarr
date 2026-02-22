using System;
using System.ComponentModel.DataAnnotations;

namespace Storarr.Models
{
    /// <summary>
    /// Represents a TV series or movie that is completely excluded from the Storarr process.
    /// Excluded items will not be:
    /// - Added to the library during scanning
    /// - Tracked for watch status
    /// - Processed for any automatic transitions
    /// </summary>
    public class ExcludedItem
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Display title of the excluded item
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Type of media being excluded (Movie, Series, or Anime)
        /// </summary>
        [Required]
        public MediaType Type { get; set; }

        /// <summary>
        /// The Movie Database ID (for movies and series)
        /// </summary>
        public int? TmdbId { get; set; }

        /// <summary>
        /// The TV Database ID (for series/anime)
        /// </summary>
        public int? TvdbId { get; set; }

        /// <summary>
        /// Sonarr series ID (if this is a TV series/anime)
        /// </summary>
        public int? SonarrId { get; set; }

        /// <summary>
        /// Radarr movie ID (if this is a movie)
        /// </summary>
        public int? RadarrId { get; set; }

        /// <summary>
        /// Optional reason for exclusion
        /// </summary>
        [MaxLength(500)]
        public string? Reason { get; set; }

        /// <summary>
        /// When this item was excluded
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
