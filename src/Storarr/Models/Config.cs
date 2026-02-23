using System;
using System.ComponentModel.DataAnnotations;

namespace Storarr.Models
{
    public class Config
    {
        public const int SingletonId = 1;

        [Key]
        public int Id { get; set; } = 1;

        // First-run setup
        public bool FirstRunComplete { get; set; } = false;
        public LibraryMode LibraryMode { get; set; } = LibraryMode.NewContentOnly;

        // Transition thresholds - Symlink to MKV
        public int SymlinkToMkvValue { get; set; } = 7;
        public TimeUnit SymlinkToMkvUnit { get; set; } = TimeUnit.Days;

        // Transition thresholds - MKV to Symlink
        public int MkvToSymlinkValue { get; set; } = 30;
        public TimeUnit MkvToSymlinkUnit { get; set; } = TimeUnit.Days;

        // Paths - Legacy single path (fallback if tier paths not set)
        [MaxLength(500)]
        public string MediaLibraryPath { get; set; } = "/media";

        // Multi-drive storage paths (optional - if enabled, these override MediaLibraryPath)
        public bool MultiDriveEnabled { get; set; } = false;
        [MaxLength(500)]
        public string? SymlinkStoragePath { get; set; }  // Fast storage for .strm files
        [MaxLength(500)]
        public string? MkvStoragePath { get; set; }      // Bulk storage for .mkv files

        // Sonarr root folders for multi-drive (optional)
        [MaxLength(500)]
        public string? SonarrSymlinkRootFolder { get; set; }  // Root folder in Sonarr for symlink tier
        [MaxLength(500)]
        public string? SonarrMkvRootFolder { get; set; }      // Root folder in Sonarr for MKV tier

        // Radarr root folders for multi-drive (optional)
        [MaxLength(500)]
        public string? RadarrSymlinkRootFolder { get; set; }  // Root folder in Radarr for symlink tier
        [MaxLength(500)]
        public string? RadarrMkvRootFolder { get; set; }      // Root folder in Radarr for MKV tier

        // Jellyfin
        [MaxLength(200)]
        public string? JellyfinUrl { get; set; }
        [MaxLength(100)]
        public string? JellyfinApiKey { get; set; }

        // Jellyseerr
        [MaxLength(200)]
        public string? JellyseerrUrl { get; set; }
        [MaxLength(100)]
        public string? JellyseerrApiKey { get; set; }

        // Sonarr (TV shows + Anime)
        [MaxLength(200)]
        public string? SonarrUrl { get; set; }
        [MaxLength(100)]
        public string? SonarrApiKey { get; set; }

        // Radarr (Movies)
        [MaxLength(200)]
        public string? RadarrUrl { get; set; }
        [MaxLength(100)]
        public string? RadarrApiKey { get; set; }

        // Download Client 1
        public bool DownloadClient1Enabled { get; set; } = false;
        public DownloadClientType DownloadClient1Type { get; set; } = DownloadClientType.QBittorrent;
        [MaxLength(200)]
        public string? DownloadClient1Url { get; set; }
        [MaxLength(100)]
        public string? DownloadClient1Username { get; set; }
        [MaxLength(100)]
        public string? DownloadClient1Password { get; set; }
        [MaxLength(100)]
        public string? DownloadClient1ApiKey { get; set; }

        // Download Client 2
        public bool DownloadClient2Enabled { get; set; } = false;
        public DownloadClientType DownloadClient2Type { get; set; } = DownloadClientType.Transmission;
        [MaxLength(200)]
        public string? DownloadClient2Url { get; set; }
        [MaxLength(100)]
        public string? DownloadClient2Username { get; set; }
        [MaxLength(100)]
        public string? DownloadClient2Password { get; set; }
        [MaxLength(100)]
        public string? DownloadClient2ApiKey { get; set; }

        // Download Client 3
        public bool DownloadClient3Enabled { get; set; } = false;
        public DownloadClientType DownloadClient3Type { get; set; } = DownloadClientType.Sabnzbd;
        [MaxLength(200)]
        public string? DownloadClient3Url { get; set; }
        [MaxLength(100)]
        public string? DownloadClient3ApiKey { get; set; }

        // Helper methods
        public TimeSpan GetSymlinkToMkvTimeSpan()
        {
            return ConvertToTimeSpan(SymlinkToMkvValue, SymlinkToMkvUnit);
        }

        public TimeSpan GetMkvToSymlinkTimeSpan()
        {
            return ConvertToTimeSpan(MkvToSymlinkValue, MkvToSymlinkUnit);
        }

        private static TimeSpan ConvertToTimeSpan(int value, TimeUnit unit)
        {
            return unit switch
            {
                TimeUnit.Minutes => TimeSpan.FromMinutes(value),
                TimeUnit.Hours => TimeSpan.FromHours(value),
                TimeUnit.Days => TimeSpan.FromDays(value),
                TimeUnit.Weeks => TimeSpan.FromDays(value * 7),
                TimeUnit.Months => TimeSpan.FromDays(value * 30),
                _ => TimeSpan.FromDays(value)
            };
        }
    }
}
