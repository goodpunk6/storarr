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

        // Exclude from automatic transitions (master pause - disables BOTH directions)
        public bool IsExcluded { get; set; } = false;

        // Per-direction auto-transition disables (granular; compose with IsExcluded via AND)
        public bool DisableAutoToMkv { get; set; } = false;     // gates strm->mkv auto only
        public bool DisableAutoToSymlink { get; set; } = false; // gates mkv->strm auto only

        // One-shot guard: the download-order reactive trigger has fired for this item
        public bool DownloadOrderApplied { get; set; } = false;

        // Timestamp when item entered PendingSymlink state (for stale detection)
        public DateTime? PendingSymlinkAt { get; set; }

        // Error state tracking
        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime? ErrorAt { get; set; }

        /// <summary>
        /// Anchor date for auto-transition countdowns = the most recent of CreatedAt,
        /// StateChangedAt, LastWatchedAt. Any deliberate transition (which sets StateChangedAt=now)
        /// or a watch (LastWatchedAt) resets the clock, preventing oscillation between directions.
        /// </summary>
        public DateTime GetTransitionAnchor()
        {
            var best = CreatedAt;
            if (StateChangedAt.HasValue && StateChangedAt.Value > best) best = StateChangedAt.Value;
            if (LastWatchedAt.HasValue && LastWatchedAt.Value > best) best = LastWatchedAt.Value;
            return best;
        }
    }
}
