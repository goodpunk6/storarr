using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Storarr.Models;

namespace Storarr.Services
{
    public interface IJellyfinService
    {
        Task<DateTime?> GetLastPlayedDate(string filePath);
        Task ScanLibrary();
        Task<MediaItemInfo?> GetItemByPath(string filePath);
        Task<List<MediaItemInfo>> GetAllMediaItems();
        Task TestConnection();
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
