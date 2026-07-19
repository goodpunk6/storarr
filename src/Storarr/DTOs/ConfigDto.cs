using System;
using System.Collections.Generic;

namespace Storarr.DTOs
{
    public class ConfigDto
    {
        // First-run setup
        public bool FirstRunComplete { get; set; }
        public string LibraryMode { get; set; } = "NewContentOnly";

        // Transition thresholds
        public int SymlinkToMkvValue { get; set; }
        public string SymlinkToMkvUnit { get; set; } = "Days";
        public int MkvToSymlinkValue { get; set; }
        public string MkvToSymlinkUnit { get; set; } = "Days";
        public string PreferredDownloadOrder { get; set; } = "StrmFirst";

        public string MediaLibraryPath { get; set; } = string.Empty;

        // Multi-drive storage paths
        public bool MultiDriveEnabled { get; set; }
        public string? SymlinkStoragePath { get; set; }
        public string? MkvStoragePath { get; set; }

        // Sonarr root folders for multi-drive
        public string? SonarrSymlinkRootFolder { get; set; }
        public string? SonarrMkvRootFolder { get; set; }

        // Radarr root folders for multi-drive
        public string? RadarrSymlinkRootFolder { get; set; }
        public string? RadarrMkvRootFolder { get; set; }

        // Jellyfin
        public string? JellyfinUrl { get; set; }
        public string? JellyfinApiKey { get; set; }

        // Jellyseerr
        public string? JellyseerrUrl { get; set; }
        public string? JellyseerrApiKey { get; set; }

        // Sonarr
        public string? SonarrUrl { get; set; }
        public string? SonarrApiKey { get; set; }

        // Radarr
        public string? RadarrUrl { get; set; }
        public string? RadarrApiKey { get; set; }

        // Download Client 1
        public bool DownloadClient1Enabled { get; set; }
        public string DownloadClient1Type { get; set; } = "QBittorrent";
        public string? DownloadClient1Url { get; set; }
        public string? DownloadClient1Username { get; set; }
        public string? DownloadClient1Password { get; set; }
        public string? DownloadClient1ApiKey { get; set; }

        // Download Client 2
        public bool DownloadClient2Enabled { get; set; }
        public string DownloadClient2Type { get; set; } = "Transmission";
        public string? DownloadClient2Url { get; set; }
        public string? DownloadClient2Username { get; set; }
        public string? DownloadClient2Password { get; set; }
        public string? DownloadClient2ApiKey { get; set; }

        // Download Client 3
        public bool DownloadClient3Enabled { get; set; }
        public string DownloadClient3Type { get; set; } = "Sabnzbd";
        public string? DownloadClient3Url { get; set; }
        public string? DownloadClient3ApiKey { get; set; }

        // Download client IDs (Sonarr/Radarr internal IDs for redirecting grabs)
        public int? SonarrSymlinkDownloadClientId { get; set; }
        public int? RadarrSymlinkDownloadClientId { get; set; }
        public int? SonarrMkvDownloadClientId { get; set; }
        public int? RadarrMkvDownloadClientId { get; set; }

        // STRM Refresh Schedule
        public bool StrmRefreshEnabled { get; set; } = true;
        public int StrmRefreshHour { get; set; } = 4;
        public int StrmRefreshMinute { get; set; } = 0;
        public string StrmRefreshDayOfWeek { get; set; } = "Monday";
        public string StrmRefreshInterval { get; set; } = "Weekly";
        public DateTime? StrmRefreshLastRun { get; set; }
        public DateTime? StrmRefreshNextRun { get; set; }
    }

    public class UpdateConfigDto
    {
        // First-run setup
        public bool? FirstRunComplete { get; set; }
        public string? LibraryMode { get; set; }

        // Transition thresholds
        public int? SymlinkToMkvValue { get; set; }
        public string? SymlinkToMkvUnit { get; set; }
        public int? MkvToSymlinkValue { get; set; }
        public string? MkvToSymlinkUnit { get; set; }
        public string? PreferredDownloadOrder { get; set; }

        public string? MediaLibraryPath { get; set; }

        // Multi-drive storage paths
        public bool? MultiDriveEnabled { get; set; }
        public string? SymlinkStoragePath { get; set; }
        public string? MkvStoragePath { get; set; }

        // Sonarr root folders for multi-drive
        public string? SonarrSymlinkRootFolder { get; set; }
        public string? SonarrMkvRootFolder { get; set; }

        // Radarr root folders for multi-drive
        public string? RadarrSymlinkRootFolder { get; set; }
        public string? RadarrMkvRootFolder { get; set; }

        // Jellyfin
        public string? JellyfinUrl { get; set; }
        public string? JellyfinApiKey { get; set; }

        // Jellyseerr
        public string? JellyseerrUrl { get; set; }
        public string? JellyseerrApiKey { get; set; }

        // Sonarr
        public string? SonarrUrl { get; set; }
        public string? SonarrApiKey { get; set; }

        // Radarr
        public string? RadarrUrl { get; set; }
        public string? RadarrApiKey { get; set; }

        // Download Client 1
        public bool? DownloadClient1Enabled { get; set; }
        public string? DownloadClient1Type { get; set; }
        public string? DownloadClient1Url { get; set; }
        public string? DownloadClient1Username { get; set; }
        public string? DownloadClient1Password { get; set; }
        public string? DownloadClient1ApiKey { get; set; }

        // Download Client 2
        public bool? DownloadClient2Enabled { get; set; }
        public string? DownloadClient2Type { get; set; }
        public string? DownloadClient2Url { get; set; }
        public string? DownloadClient2Username { get; set; }
        public string? DownloadClient2Password { get; set; }
        public string? DownloadClient2ApiKey { get; set; }

        // Download Client 3
        public bool? DownloadClient3Enabled { get; set; }
        public string? DownloadClient3Type { get; set; }
        public string? DownloadClient3Url { get; set; }
        public string? DownloadClient3ApiKey { get; set; }

        // Download client IDs (Sonarr/Radarr internal IDs for redirecting grabs)
        public int? SonarrSymlinkDownloadClientId { get; set; }
        public int? RadarrSymlinkDownloadClientId { get; set; }
        public int? SonarrMkvDownloadClientId { get; set; }
        public int? RadarrMkvDownloadClientId { get; set; }

        // STRM Refresh Schedule
        public bool? StrmRefreshEnabled { get; set; }
        public int? StrmRefreshHour { get; set; }
        public int? StrmRefreshMinute { get; set; }
        public string? StrmRefreshDayOfWeek { get; set; }
        public string? StrmRefreshInterval { get; set; }
    }

    public class ConnectionTestResult
    {
        public string Service { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Version { get; set; }
    }

    public class TestConnectionsResponse
    {
        public List<ConnectionTestResult> Results { get; set; } = new List<ConnectionTestResult>();
    }

    public class DownloadClientsResponse
    {
        public List<DownloadClientOption> SonarrClients { get; set; } = new();
        public List<DownloadClientOption> RadarrClients { get; set; } = new();
    }

    public class DownloadClientOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Implementation { get; set; } = string.Empty;
        public bool Enable { get; set; }
    }

    public class FirstRunSetupDto
    {
        public string LibraryMode { get; set; } = "NewContentOnly";
        public string? JellyfinUrl { get; set; }
        public string? JellyfinApiKey { get; set; }
        public string? JellyseerrUrl { get; set; }
        public string? JellyseerrApiKey { get; set; }
        public string? SonarrUrl { get; set; }
        public string? SonarrApiKey { get; set; }
        public string? RadarrUrl { get; set; }
        public string? RadarrApiKey { get; set; }
        public string? MediaLibraryPath { get; set; }
        // Multi-drive support (optional)
        public bool MultiDriveEnabled { get; set; }
        public string? SymlinkStoragePath { get; set; }
        public string? MkvStoragePath { get; set; }
    }
}
