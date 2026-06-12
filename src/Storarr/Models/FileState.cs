namespace Storarr.Models
{
    public enum FileState
    {
        Symlink,        // 0 - Streaming via NZB-Dav
        Mkv,            // 1 - Local file
        Downloading,    // 2 - Transition in progress
        PendingSymlink, // 3 - Waiting for symlink replacement
        Error           // 4 - Transition failed, needs attention
    }
}
