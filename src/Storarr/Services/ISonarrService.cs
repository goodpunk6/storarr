using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storarr.Services
{
    public interface ISonarrService
    {
        Task<IEnumerable<Series>> GetSeries();
        Task<Series?> GetSeries(int sonarrId);
        Task<IEnumerable<SonarrEpisodeFile>> GetEpisodeFiles(int seriesId);
        Task<SonarrEpisodeFile?> FindEpisodeFileByPath(int seriesId, string filePath);
        Task TriggerSearch(int seriesId, int[]? episodeIds = null);
        Task<SonarrEpisodeFile?> GetEpisodeFile(int episodeFileId);
        Task DeleteEpisodeFile(int episodeFileId);
        Task<bool> DeleteEpisodeFileByPath(int seriesId, string filePath);
        Task<List<SonarrQueueItem>> GetQueue();
        Task TestConnection();
    }

    public class Series
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TvdbId { get; set; }
        public string? Path { get; set; }
        public int QualityProfileId { get; set; }
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
}
