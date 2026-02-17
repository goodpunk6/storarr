using System;
using Storarr.Models;

namespace Storarr.DTOs
{
    public class ActivityLogDto
    {
        public int Id { get; set; }
        public int MediaItemId { get; set; }
        public string? MediaTitle { get; set; }
        public string Action { get; set; } = string.Empty;
        public string FromState { get; set; } = string.Empty;
        public string ToState { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
    }

    // Jellyseerr webhook payload
    public class JellyseerrWebhookPayload
    {
        public string EventType { get; set; } = string.Empty;
        public JellyseerrMedia? Media { get; set; }
        public JellyseerrRequest? Request { get; set; }
    }

    public class JellyseerrMedia
    {
        public int TmdbId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int? TvdbId { get; set; }
    }

    public class JellyseerrRequest
    {
        public int RequestId { get; set; }
        public int MediaId { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    // Sonarr webhook payload
    public class SonarrWebhookPayload
    {
        public string EventType { get; set; } = string.Empty;
        public SonarrSeries? Series { get; set; }
        public SonarrEpisode? Episode { get; set; }
        public SonarrEpisodeFile? EpisodeFile { get; set; }
    }

    public class SonarrSeries
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TvdbId { get; set; }
    }

    public class SonarrEpisode
    {
        public int Id { get; set; }
        public int EpisodeNumber { get; set; }
        public int SeasonNumber { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    public class SonarrEpisodeFile
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
    }

    // Radarr webhook payload
    public class RadarrWebhookPayload
    {
        public string EventType { get; set; } = string.Empty;
        public RadarrMovie? Movie { get; set; }
        public RadarrMovieFile? MovieFile { get; set; }
    }

    public class RadarrMovie
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TmdbId { get; set; }
    }

    public class RadarrMovieFile
    {
        public int Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
