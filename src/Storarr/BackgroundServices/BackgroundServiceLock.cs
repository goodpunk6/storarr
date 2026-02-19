using System.Threading;

namespace Storarr.BackgroundServices
{
    /// <summary>
    /// Shared semaphore to serialize execution across all background services,
    /// preventing concurrent database writes from TransitionScheduler, DownloadMonitor,
    /// WatchStatusMonitor, and LibraryScanner.
    /// </summary>
    internal static class BackgroundServiceLock
    {
        internal static readonly SemaphoreSlim GlobalLock = new SemaphoreSlim(1, 1);
    }
}
