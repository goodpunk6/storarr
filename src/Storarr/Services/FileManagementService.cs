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
        /// Validates that the given path is within the configured media library to prevent path traversal attacks.
        /// </summary>
        public async Task ValidatePath(string path)
        {
            var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
            var allowedBase = Path.GetFullPath(config?.MediaLibraryPath ?? "/media");
            var fullPath = Path.GetFullPath(path);

            if (!fullPath.StartsWith(allowedBase, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Path '{path}' is outside the allowed media directory.");
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
