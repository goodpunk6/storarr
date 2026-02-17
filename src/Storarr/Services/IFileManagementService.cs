using System.Collections.Generic;
using System.Threading.Tasks;

namespace Storarr.Services
{
    public interface IFileManagementService
    {
        Task<bool> IsSymlink(string path);
        Task<string?> GetSymlinkTarget(string path);
        Task DeleteFile(string path);
        Task<long> GetFileSize(string path);
        Task<bool> FileExists(string path);
        Task<IEnumerable<MediaFileInfo>> ScanDirectory(string path, bool recursive = true);
    }

    public class MediaFileInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsSymlink { get; set; }
        public string? SymlinkTarget { get; set; }
        public long Size { get; set; }
        public System.DateTime LastModified { get; set; }
    }
}
