using System.Collections.Generic;

namespace Storarr.DTOs
{
    public class QueueItemDto
    {
        public string DownloadId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long Size { get; set; }
        public long SizeLeft { get; set; }
        public double Progress { get; set; }
        public string? ErrorMessage { get; set; }
        public string Source { get; set; } = string.Empty;
        public int MediaItemId { get; set; }
    }

    public class QueueResponse
    {
        public List<QueueItemDto> Items { get; set; } = new List<QueueItemDto>();
        public int TotalCount { get; set; }
    }
}
