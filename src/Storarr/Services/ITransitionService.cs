using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Storarr.Models;

namespace Storarr.Services
{
    public interface ITransitionService
    {
        Task TransitionToMkv(MediaItem item);
        Task TransitionToSymlink(MediaItem item);
        Task<SeasonConversionResult> TransitionSeasonToMkv(int seriesId, int seasonNumber);
        Task CheckAndProcessTransitions();
        Task<IEnumerable<TransitionCandidate>> GetUpcomingTransitions(int count = 10);
    }

    public class SeasonConversionResult
    {
        public int ConvertedCount { get; set; }
        public string? Tier { get; set; }
        public string? ChosenRelease { get; set; }
        public string? Message { get; set; }
    }

    public class TransitionCandidate
    {
        public int MediaItemId { get; set; }
        public string Title { get; set; } = string.Empty;
        public FileState CurrentState { get; set; }
        public FileState TargetState { get; set; }
        public DateTime? TransitionDate { get; set; }
        public int DaysUntilTransition { get; set; }
    }
}
