namespace Storarr.Models
{
    public enum FileState
    {
        Symlink,        // Streaming via NZB-Dav
        Mkv,            // Local file
        Downloading,    // Transition in progress
        PendingSymlink  // Waiting for Jellyseerr to create symlink
    }
}
