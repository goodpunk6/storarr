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
        /// Stages a new ExcludedItem and removes matching MediaItems. Does NOT call SaveChangesAsync
        /// (caller controls the transaction).
        /// </summary>
        Task<ExcludeByArrIdResult> ExcludeByArrIdAsync(ExcludeByArrIdDto dto, CancellationToken ct = default);

        /// <summary>
        /// Stage tracked MediaItems matching the exclusion for deletion. Does NOT save. Returns count staged.
        /// </summary>
        Task<int> RemoveMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default);

        /// <summary>
        /// Count tracked MediaItems matching the exclusion.
        /// </summary>
        Task<int> CountMatchingMediaItemsAsync(ExcludedItem exclusion, CancellationToken ct = default);
    }
}
