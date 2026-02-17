using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Storarr.Services
{
    public class FileManagementService : IFileManagementService
    {
        private readonly ILogger<FileManagementService> _logger;

        public FileManagementService(ILogger<FileManagementService> logger)
        {
            _logger = logger;
        }

        public Task<bool> IsSymlink(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path))
                        return false;

                    var attributes = File.GetAttributes(path);
                    return attributes.HasFlag(FileAttributes.ReparsePoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to check if {Path} is symlink", path);
                    return false;
                }
            });
        }

        public async Task<string?> GetSymlinkTarget(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Simplified symlink target resolution
                    // On Linux/Mac this would use readlink
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _logger.LogWarning("Symlink target resolution not fully supported on Windows");
                        return (string?)null;
                    }

                    // Basic implementation - in production would use native calls
                    return (string?)null;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get symlink target for {Path}", path);
                    return (string?)null;
                }
            });
        }

        public Task DeleteFile(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        _logger.LogInformation("Deleted file {Path}", path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                        _logger.LogInformation("Deleted directory {Path}", path);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete {Path}", path);
                    throw;
                }
            });
        }

        public Task<long> GetFileSize(string path)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path))
                        return 0L;

                    var fileInfo = new FileInfo(path);
                    return fileInfo.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get file size for {Path}", path);
                    return 0L;
                }
            });
        }

        public Task<bool> FileExists(string path)
        {
            return Task.FromResult(File.Exists(path));
        }

        public Task<IEnumerable<MediaFileInfo>> ScanDirectory(string path, bool recursive = true)
        {
            return Task.Run(() =>
            {
                var result = new List<MediaFileInfo>();

                try
                {
                    if (!Directory.Exists(path))
                    {
                        _logger.LogWarning("Directory {Path} does not exist", path);
                        return result;
                    }

                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var extensions = new[] { ".mkv", ".mp4", ".avi", ".wmv", ".strm" };

                    foreach (var file in Directory.EnumerateFiles(path, "*.*", searchOption))
                    {
                        try
                        {
                            var extension = Path.GetExtension(file).ToLowerInvariant();
                            if (!extensions.Contains(extension))
                                continue;

                            var fileInfo = new FileInfo(file);
                            var isSymlink = extension == ".strm" || IsSymlink(file).Result;

                            result.Add(new MediaFileInfo
                            {
                                Path = file,
                                Name = fileInfo.Name,
                                IsSymlink = isSymlink,
                                SymlinkTarget = isSymlink ? GetSymlinkTarget(file).Result : null,
                                Size = fileInfo.Length,
                                LastModified = fileInfo.LastWriteTimeUtc
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process file {File}", file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to scan directory {Path}", path);
                }

                return result.AsEnumerable();
            });
        }
    }
}
