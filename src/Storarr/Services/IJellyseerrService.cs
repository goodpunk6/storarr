using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Storarr.Models;

namespace Storarr.Services
{
    public interface IJellyseerrService
    {
        Task<IEnumerable<MediaRequest>> GetRecentRequests(int limit = 50);
        Task<MediaRequest?> GetRequest(int requestId);
        Task<MediaRequest> CreateRequest(int tmdbId, MediaType type, int? tvdbId = null);
        Task TestConnection();
    }

    public class MediaRequest
    {
        public int RequestId { get; set; }
        public int MediaId { get; set; }
        public int TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public MediaType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
