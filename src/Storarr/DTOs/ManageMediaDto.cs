using System.Collections.Generic;

namespace Storarr.DTOs
{
    public class ManageMediaRequestDto
    {
        public List<int> ItemIds { get; set; } = new();
        public bool DeleteFiles { get; set; }
        public bool RemoveFromArr { get; set; }
        public bool Unmonitor { get; set; }
        public bool ReMonitor { get; set; }
    }

    public class ManageMediaResultDto
    {
        public List<ManageMediaItemResult> Results { get; set; } = new();
    }

    public class ManageMediaItemResult
    {
        public int ItemId { get; set; }
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Success { get; set; }
        public List<string> Actions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
