using System.Collections.Generic;
using System.Threading.Tasks;
using Storarr.Models;

namespace Storarr.Services
{
    public interface IDownloadClientService
    {
        Task<bool> TestConnection(DownloadClientType type, string url, string? username, string? password, string? apiKey);
        Task<IEnumerable<DownloadQueueItem>> GetQueue(DownloadClientType type, string url, string? username, string? password, string? apiKey);
    }

    public class DownloadQueueItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeRemaining { get; set; }
        public double Progress { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DownloadClientType ClientType { get; set; }
    }
}
