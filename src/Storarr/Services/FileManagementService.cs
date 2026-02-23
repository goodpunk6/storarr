using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;

namespace Storarr.Services
{
    public class FileManagementService : IFileManagementService
    {
        private readonly ILogger<FileManagementService> _logger;
        private readonly StorarrDbContext _dbContext;

        public FileManagementService(ILogger<FileManagementService> logger, StorarrDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Validates that the given path is within the configured media library paths to prevent path traversal attacks.
        /// When multi-drive is enabled, allows both symlink and MKV storage paths.
        /// </summary>
        public async Task ValidatePath(string path)
        {
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            var fullPath = Path.GetFullPath(path);

            // Collect all allowed base paths
            var allowedBases = new List<string>();

            // Primary media library path (always allowed)
            var primaryPath = config?.MediaLibraryPath ?? "/media";
            allowedBases.Add(Path.GetFullPath(primaryPath));

            // Multi-drive paths (if enabled)
            if (config?.MultiDriveEnabled == true)
            {
                if (!string.IsNullOrWhiteSpace(config.SymlinkStoragePath))
                    allowedBases.Add(Path.GetFullPath(config.SymlinkStoragePath));
                if (!string.IsNullOrWhiteSpace(config.MkvStoragePath))
                    allowedBases.Add(Path.GetFullPath(config.MkvStoragePath));
            }

            // Check if the path is within any allowed base
            foreach (var allowedBase in allowedBases)
            {
                if (fullPath.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase))
                    return; // Path is valid
            }

            throw new UnauthorizedAccessException($"Path '{path}' is outside the allowed media directories.");
        }

        /// <summary>
        /// Gets the appropriate storage path for a given file state.
        /// </summary>
        public async Task<string> GetStoragePathForState(FileState state)
        {
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            if (config == null)
                return "/media";

            if (!config.MultiDriveEnabled)
                return config.MediaLibraryPath;

            return state switch
            {
                FileState.Symlink or FileState.PendingSymlink =>
                    config.SymlinkStoragePath ?? config.MediaLibraryPath,
                FileState.Mkv =>
                    config.MkvStoragePath ?? config.MediaLibraryPath,
                _ => config.MediaLibraryPath
            };
        }

        /// <summary>
        /// Gets all configured storage paths for scanning.
        /// </summary>
        public async Task<List<string>> GetAllStoragePaths()
        {
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            var paths = new List<string>();

            if (config == null)
            {
                paths.Add("/media");
                return paths;
            }

            if (config.MultiDriveEnabled)
            {
                if (!string.IsNullOrWhiteSpace(config.SymlinkStoragePath))
                    paths.Add(config.SymlinkStoragePath);
                if (!string.IsNullOrWhiteSpace(config.MkvStoragePath))
                    paths.Add(config.MkvStoragePath);

                // Fallback to primary path if multi-drive paths aren't set
                if (paths.Count == 0 && !string.IsNullOrWhiteSpace(config.MediaLibraryPath))
                    paths.Add(config.MediaLibraryPath);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(config.MediaLibraryPath))
                    paths.Add(config.MediaLibraryPath);
            }

            return paths;
        }

        public Task<bool> IsSymlink(string path)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await ValidatePath(path);

                    if (!File.Exists(path) && !Directory.Exists(path))
                        return false;

                    var attributes = File.GetAttributes(path);
                    return attributes.HasFlag(FileAttributes.ReparsePoint);
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
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
            return await Task.Run(async () =>
            {
                try
                {
                    await ValidatePath(path);

                    // .NET 6+: FileInfo.LinkTarget resolves symlinks natively
                    var info = new FileInfo(path);
                    return info.LinkTarget;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
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
            return Task.Run(async () =>
            {
                try
                {
                    await ValidatePath(path);

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        _logger.LogInformation("Deleted file {Path}", path);
                    }
                    else if (Directory.Exists(path))
                    {
                        // Do NOT delete recursively — only delete an empty directory
                        Directory.Delete(path, recursive: false);
                        _logger.LogInformation("Deleted directory {Path}", path);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
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
            return Task.Run(async () =>
            {
                try
                {
                    await ValidatePath(path);

                    if (!File.Exists(path))
                        return 0L;

                    var fileInfo = new FileInfo(path);
                    return fileInfo.Length;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get file size for {Path}", path);
                    return 0L;
                }
            });
        }

        public async Task<bool> FileExists(string path)
        {
            try
            {
                await ValidatePath(path);
                return File.Exists(path);
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch
            {
                return false;
            }
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
                        return result.AsEnumerable();
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
                            var attributes = fileInfo.Attributes;
                            var isSymlink = extension == ".strm" || attributes.HasFlag(FileAttributes.ReparsePoint);

                            // Use .NET 6 FileInfo.LinkTarget directly (no async .Result needed)
                            string? symlinkTarget = isSymlink ? fileInfo.LinkTarget : null;

                            result.Add(new MediaFileInfo
                            {
                                Path = file,
                                Name = fileInfo.Name,
                                IsSymlink = isSymlink,
                                SymlinkTarget = symlinkTarget,
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
