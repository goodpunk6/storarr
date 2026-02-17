namespace Storarr.Models
{
    public enum LibraryMode
    {
        /// <summary>
        /// Only track content added after Storarr is installed (safest)
        /// </summary>
        NewContentOnly = 0,

        /// <summary>
        /// Scan existing library but don't auto-transition
        /// </summary>
        TrackExisting = 1,

        /// <summary>
        /// Scan and auto-transition existing content (use with caution)
        /// </summary>
        FullAutomation = 2
    }
}
