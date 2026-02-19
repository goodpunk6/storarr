using System;
using System.Collections.Generic;

namespace Storarr.DTOs
{
    public class DashboardDto
    {
        public int TotalItems { get; set; }
        public int SymlinkCount { get; set; }
        public int MkvCount { get; set; }
        public int DownloadingCount { get; set; }
        public int PendingSymlinkCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public List<TransitionDto> UpcomingTransitions { get; set; } = new List<TransitionDto>();
    }

    public class TransitionDto
    {
        public int MediaItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public string TargetState { get; set; } = string.Empty;
        public int DaysUntilTransition { get; set; }
        public DateTime? TransitionDate { get; set; }
    }
}
