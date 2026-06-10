namespace Storarr.Models
{
    /// <summary>
    /// Interval for automatic STRM file refresh.
    /// </summary>
    public enum StrmRefreshInterval
    {
        /// <summary>
        /// Refresh daily at the specified time.
        /// </summary>
        Daily = 0,

        /// <summary>
        /// Refresh weekly on the specified day of week at the specified time.
        /// </summary>
        Weekly = 1,

        /// <summary>
        /// Refresh monthly on the first occurrence of the specified day of week.
        /// </summary>
        Monthly = 2,

        /// <summary>
        /// Refresh yearly on January 1st at the specified time.
        /// </summary>
        Yearly = 3
    }
}
