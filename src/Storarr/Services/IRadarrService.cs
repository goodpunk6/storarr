using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storarr.Services
{
    public interface IRadarrService
    {
        Task<IEnumerable<Movie>> GetMovies();
        Task<Movie?> GetMovie(int radarrId);
        Task<MovieFile?> FindMovieFileByPath(int movieId, string filePath);
        Task TriggerSearch(int movieId);
        Task<MovieFile?> GetMovieFile(int movieFileId);
        Task DeleteMovieFile(int movieFileId);
        Task<bool> DeleteMovieFileByPath(int movieId, string filePath);
        Task<List<RadarrQueueItem>> GetQueue();
        Task TestConnection();
    }

    public class Movie
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int TmdbId { get; set; }
        public string? Path { get; set; }
        public int QualityProfileId { get; set; }
        public int? MovieFileId { get; set; }
    }

    public class MovieFile
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Quality { get; set; } = string.Empty;
    }

    public class RadarrQueueItem
    {
        public string DownloadId { get; set; } = string.Empty;
        public int MovieId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
