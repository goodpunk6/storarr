namespace Storarr.Models
{
    /// <summary>
    /// Preferred initial format for newly-added content.
    /// StrmFirst (default) = land as .strm, materialize to MKV only if watched past the symlink->mkv threshold.
    /// MkvFirst = land as .mkv, reduce to symlink only if inactive past the mkv->symlink threshold.
    /// </summary>
    public enum DownloadOrder
    {
        StrmFirst = 0,
        MkvFirst = 1
    }
}
