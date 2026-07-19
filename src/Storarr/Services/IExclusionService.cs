using System.Threading;
using System.Threading.Tasks;
using Storarr.DTOs;
using Storarr.Models;

namespace Storarr.Services
{
    public interface IExclusionService
    {
        /// <summary>
        /// Exclude all tracked media for a series (by SonarrId) or movie (by RadarrId).
        /// Stages a new ExcludedItem and soft-pauses matching MediaItems (sets IsExcluded=true; rows are kept
        /// so manual conversion still works). Does NOT call SaveChangesAsync (caller controls the transaction).
        /// </summary>
        Task<ExcludeByArrIdResult> ExcludeByArrIdAsync(ExcludeByArrIdDto dto, CancellationToken ct = default);

        /// <summary>
        /// Soft-pause tracked MediaItems matching the exclusion (IsExcluded=true). Does NOT save. Returns count.
        /// </summary>
        Task<int> PauseMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default);

        /// <summary>
        /// Resume tracked MediaItems matching the exclusion (IsExcluded=false). Does NOT save. Returns count.
        /// </summary>
        Task<int> UnpauseMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default);

        /// <summary>
        /// Count tracked MediaItems matching the exclusion.
        /// </summary>
        Task<int> CountMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default);
    }
}
