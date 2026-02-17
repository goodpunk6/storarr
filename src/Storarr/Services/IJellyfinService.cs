using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Storarr.Models;

namespace Storarr.Services
{
    public interface IJellyfinService
    {
        Task<IEnumerable<WatchHistoryEntry>> GetWatchHistory(string itemId);
        Task<DateTime?> GetLastPlayedDate(string filePath);
        Task ScanLibrary();
        Task<MediaItemInfo?> GetItemByPath(string filePath);
        Task<List<MediaItemInfo>> GetAllMediaItems();
        Task TestConnection();
    }

    public class WatchHistoryEntry
    {
        public string ItemId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public DateTime PlayedAt { get; set; }
        public bool Completed { get; set; }
    }

    public class MediaItemInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public long? Size { get; set; }
    }
}
