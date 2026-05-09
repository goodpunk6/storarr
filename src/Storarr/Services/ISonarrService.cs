using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storarr.Services
{
    public class ReleaseResult
    {
        public string Guid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long Size { get; set; }
        public int IndexerId { get; set; }
        public bool DownloadAllowed { get; set; }
        public string? Protocol { get; set; }
        public int QualityWeight { get; set; }
        public int CustomFormatScore { get; set; }
        public int? Seeders { get; set; }
    }

    public class GrabResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadClientInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Implementation { get; set; } = string.Empty;
        public bool Enable { get; set; }
    }

    public interface ISonarrService
    {
        Task<IEnumerable<Series>> GetSeries();
        Task<Series?> GetSeries(int sonarrId);
        Task<Series?> LookupSeriesByTitle(string title);
        Task<IEnumerable<SonarrEpisodeFile>> GetEpisodeFiles(int seriesId);
        Task<SonarrEpisodeFile?> FindEpisodeFileByPath(int seriesId, string filePath);
        Task TriggerSearch(int seriesId, int[]? episodeIds = null);
        Task<SonarrEpisodeFile?> GetEpisodeFile(int episodeFileId);
        Task DeleteEpisodeFile(int episodeFileId);
        Task<bool> DeleteEpisodeFileByPath(int seriesId, string filePath);
        Task<List<SonarrQueueItem>> GetQueue();
        Task TestConnection();
        Task<IEnumerable<ReleaseResult>> SearchReleases(int seriesId, int[] episodeIds);
        Task<GrabResult> GrabRelease(string guid, int indexerId, int? downloadClientId = null, int? seriesId = null, int[]? episodeIds = null);
        Task<IEnumerable<DownloadClientInfo>> GetDownloadClients();
        Task<int?> GetEpisodeId(int seriesId, int seasonNumber, int episodeNumber);
        Task<HashSet<string>> GetBlocklistedTitles();
        Task DeleteSeries(int seriesId, bool deleteFiles = false);
        Task SetSeriesMonitorState(int seriesId, bool monitored);
    }

    public class Series
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TvdbId { get; set; }
        public string? Path { get; set; }
        public int QualityProfileId { get; set; }
        public int? TmdbId { get; set; }
        public List<SonarrImage>? Images { get; set; }
        public bool Monitored { get; set; }
    }

    public class SonarrEpisodeFile
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Quality { get; set; } = string.Empty;
    }

    public class SonarrQueueItem
    {
        public string DownloadId { get; set; } = string.Empty;
        public int SeriesId { get; set; }
        public int EpisodeId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class SonarrImage
    {
        public string? CoverType { get; set; }
        public string? Url { get; set; }
        public string? RemoteUrl { get; set; }
    }
}
