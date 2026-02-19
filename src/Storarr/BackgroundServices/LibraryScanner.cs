using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.BackgroundServices
{
    public class LibraryScanner : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<LibraryScanner> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

        public LibraryScanner(IServiceProvider serviceProvider, ILogger<LibraryScanner> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LibraryScanner started");

            // Initial delay to let other services start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await BackgroundServiceLock.GlobalLock.WaitAsync(stoppingToken);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<StorarrDbContext>();
                    var fileService = scope.ServiceProvider.GetRequiredService<IFileManagementService>();
                    var sonarrService = scope.ServiceProvider.GetRequiredService<ISonarrService>();
                    var radarrService = scope.ServiceProvider.GetRequiredService<IRadarrService>();

                    await ScanLibrary(dbContext, fileService, sonarrService, radarrService);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in LibraryScanner");
                }
                finally
                {
                    BackgroundServiceLock.GlobalLock.Release();
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task ScanLibrary(
            StorarrDbContext dbContext,
            IFileManagementService fileService,
            ISonarrService sonarrService,
            IRadarrService radarrService)
        {
            var config = await dbContext.Configs.FindAsync(Config.SingletonId);
            if (string.IsNullOrEmpty(config?.MediaLibraryPath) || !Directory.Exists(config.MediaLibraryPath))
            {
                _logger.LogDebug("Media library path not configured or doesn't exist");
                return;
            }

            _logger.LogInformation("Scanning media library at {Path}", config.MediaLibraryPath);

            // Fetch all series from Sonarr and movies from Radarr for ID matching
            // Key is the relative path within the media library
            Dictionary<string, SeriesInfo> seriesByPath = new();
            Dictionary<string, MovieInfo> moviesByPath = new();

            try
            {
                var series = await sonarrService.GetSeries();
                foreach (var s in series)
                {
                    if (!string.IsNullOrEmpty(s.Path))
                    {
                        var relativePath = ExtractRelativePath(s.Path, config.MediaLibraryPath);
                        seriesByPath[relativePath] = new SeriesInfo
                        {
                            Id = s.Id,
                            Title = s.Title,
                            TvdbId = s.TvdbId,
                            Path = relativePath
                        };
                        _logger.LogDebug("[LibraryScanner] Sonarr series: {Title} -> {Path} (relative: {RelPath})",
                            s.Title, s.Path, relativePath);
                    }
                }
                _logger.LogInformation("[LibraryScanner] Loaded {Count} series from Sonarr", seriesByPath.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LibraryScanner] Failed to fetch series from Sonarr");
            }

            try
            {
                var movies = await radarrService.GetMovies();
                foreach (var m in movies)
                {
                    if (!string.IsNullOrEmpty(m.Path))
                    {
                        var relativePath = ExtractRelativePath(m.Path, config.MediaLibraryPath);
                        moviesByPath[relativePath] = new MovieInfo
                        {
                            Id = m.Id,
                            Title = m.Title,
                            TmdbId = m.TmdbId,
                            Path = relativePath
                        };
                        _logger.LogDebug("[LibraryScanner] Radarr movie: {Title} -> {Path} (relative: {RelPath})",
                            m.Title, m.Path, relativePath);
                    }
                }
                _logger.LogInformation("[LibraryScanner] Loaded {Count} movies from Radarr", moviesByPath.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LibraryScanner] Failed to fetch movies from Radarr");
            }

            var files = await fileService.ScanDirectory(config.MediaLibraryPath);

            // Load all existing items into a dictionary to avoid N+1 queries
            var existingItems = await dbContext.MediaItems.ToListAsync();
            var existingItemsByPath = existingItems.ToDictionary(
                m => m.FilePath,
                StringComparer.OrdinalIgnoreCase);
            var existingPaths = existingItemsByPath.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Find new files
            var newFiles = files.Where(f => !existingPaths.Contains(f.Path)).ToList();
            foreach (var file in newFiles)
            {
                var mediaType = DetermineMediaType(file.Path);
                var (sonarrId, radarrId, tvdbId, tmdbId, title) = MatchToArrService(file.Path, config.MediaLibraryPath, mediaType, seriesByPath, moviesByPath);

                var mediaItem = new MediaItem
                {
                    Title = title ?? Path.GetFileNameWithoutExtension(file.Name),
                    FilePath = file.Path,
                    CurrentState = file.IsSymlink ? FileState.Symlink : FileState.Mkv,
                    FileSize = file.Size,
                    CreatedAt = DateTime.UtcNow,
                    StateChangedAt = DateTime.UtcNow,
                    Type = mediaType,
                    SonarrId = sonarrId,
                    RadarrId = radarrId,
                    TvdbId = tvdbId,
                    TmdbId = tmdbId
                };

                // Extract season/episode from path for TV shows
                if (mediaType == MediaType.Series || mediaType == MediaType.Anime)
                {
                    ExtractSeasonEpisode(file.Path, mediaItem);
                }

                dbContext.MediaItems.Add(mediaItem);
                _logger.LogInformation("[LibraryScanner] Added new media item: {Title} (SonarrId={SonarrId}, RadarrId={RadarrId}, TmdbId={TmdbId}, TvdbId={TvdbId})",
                    mediaItem.Title, mediaItem.SonarrId, mediaItem.RadarrId, mediaItem.TmdbId, mediaItem.TvdbId);
            }

            // Update existing files' states if changed AND link to Arr services if not already linked
            var existingFiles = files.Where(f => existingPaths.Contains(f.Path)).ToList();
            foreach (var file in existingFiles)
            {
                if (!existingItemsByPath.TryGetValue(file.Path, out var item))
                    continue;

                var expectedState = file.IsSymlink ? FileState.Symlink : FileState.Mkv;

                if (item.CurrentState != expectedState &&
                    item.CurrentState != FileState.Downloading &&
                    item.CurrentState != FileState.PendingSymlink)
                {
                    _logger.LogInformation("[LibraryScanner] State changed for {Title}: {OldState} -> {NewState}",
                        item.Title, item.CurrentState, expectedState);
                    item.CurrentState = expectedState;
                    item.StateChangedAt = DateTime.UtcNow;
                }

                // Link to Arr services if not already linked
                if (!item.SonarrId.HasValue && !item.RadarrId.HasValue)
                {
                    var (sonarrId, radarrId, tvdbId, tmdbId, title) = MatchToArrService(file.Path, config.MediaLibraryPath, item.Type, seriesByPath, moviesByPath);
                    if (sonarrId.HasValue || radarrId.HasValue)
                    {
                        item.SonarrId = sonarrId;
                        item.RadarrId = radarrId;
                        item.TvdbId = tvdbId;
                        item.TmdbId = tmdbId;
                        if (!string.IsNullOrEmpty(title) && item.Title != title)
                        {
                            item.Title = title;
                        }
                        _logger.LogInformation("[LibraryScanner] Linked existing item {Title} to Arr service (SonarrId={SonarrId}, RadarrId={RadarrId})",
                            item.Title, item.SonarrId, item.RadarrId);
                    }
                }

                item.FileSize = file.Size;
            }

            // Find deleted files
            var filePaths = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deletedPaths = existingPaths.Where(p => !filePaths.Contains(p)).ToList();
            foreach (var path in deletedPaths)
            {
                if (!existingItemsByPath.TryGetValue(path, out var item))
                    continue;

                // If file is gone and we were downloading, mark as pending symlink
                if (item.CurrentState == FileState.Downloading)
                {
                    item.CurrentState = FileState.PendingSymlink;
                }
                _logger.LogWarning("[LibraryScanner] Media file no longer exists: {Path}", path);
            }

            await dbContext.SaveChangesAsync();
            _logger.LogInformation("[LibraryScanner] Library scan complete. Found {New} new, {Updated} existing, {Deleted} missing",
                newFiles.Count, existingFiles.Count, deletedPaths.Count);
        }

        /// <summary>
        /// Extracts the relative path within the media library by stripping the configured
        /// media library path prefix. Falls back to the last two path components.
        /// </summary>
        private string ExtractRelativePath(string absolutePath, string mediaLibraryPath)
        {
            var normalizedPath = absolutePath.Replace('\\', '/').TrimEnd('/');
            var normalizedBase = mediaLibraryPath.Replace('\\', '/').TrimEnd('/');

            if (normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return normalizedPath.Substring(normalizedBase.Length).TrimStart('/').ToLowerInvariant();

            // Fallback: return the last two directory components
            var parts = normalizedPath.ToLowerInvariant().Split('/');
            return string.Join("/", parts.TakeLast(2));
        }

        /// <summary>
        /// Extract the relative path from a file path within the media library.
        /// </summary>
        private string GetRelativeFilePath(string filePath, string mediaLibraryPath)
        {
            var normalizedFile = filePath.Replace('\\', '/').TrimEnd('/');
            var normalizedLibrary = mediaLibraryPath.Replace('\\', '/').TrimEnd('/');

            if (normalizedFile.StartsWith(normalizedLibrary, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFile.Substring(normalizedLibrary.Length).TrimStart('/').ToLowerInvariant();
            }

            return normalizedFile.ToLowerInvariant();
        }

        private (int? sonarrId, int? radarrId, int? tvdbId, int? tmdbId, string? title) MatchToArrService(
            string filePath,
            string mediaLibraryPath,
            MediaType mediaType,
            Dictionary<string, SeriesInfo> seriesByPath,
            Dictionary<string, MovieInfo> moviesByPath)
        {
            var relativeFilePath = GetRelativeFilePath(filePath, mediaLibraryPath);
            _logger.LogDebug("[LibraryScanner] Matching file: {FilePath} -> relative: {RelPath}", filePath, relativeFilePath);

            if (mediaType == MediaType.Series || mediaType == MediaType.Anime)
            {
                // Find the series whose path is a prefix of this file path
                var match = seriesByPath
                    .Where(kvp => relativeFilePath.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kvp => kvp.Key.Length) // Longest match first
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(match.Key))
                {
                    _logger.LogDebug("[LibraryScanner] Matched file {FilePath} to series {Title} (ID={Id})",
                        filePath, match.Value.Title, match.Value.Id);
                    return (match.Value.Id, null, match.Value.TvdbId, null, match.Value.Title);
                }
            }
            else if (mediaType == MediaType.Movie)
            {
                // Find the movie whose path is a prefix of this file path
                var match = moviesByPath
                    .Where(kvp => relativeFilePath.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kvp => kvp.Key.Length) // Longest match first
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(match.Key))
                {
                    _logger.LogDebug("[LibraryScanner] Matched file {FilePath} to movie {Title} (ID={Id})",
                        filePath, match.Value.Title, match.Value.Id);
                    return (null, match.Value.Id, null, match.Value.TmdbId, match.Value.Title);
                }
            }

            return (null, null, null, null, null);
        }

        private MediaType DetermineMediaType(string path)
        {
            var pathLower = path.ToLowerInvariant();

            if (pathLower.Contains("/anime/") || pathLower.Contains("\\anime\\"))
                return MediaType.Anime;

            if (pathLower.Contains("/tv/") || pathLower.Contains("\\tv\\") ||
                pathLower.Contains("/series/") || pathLower.Contains("\\series\\"))
                return MediaType.Series;

            return MediaType.Movie;
        }

        private void ExtractSeasonEpisode(string path, MediaItem item)
        {
            // Try to extract season/episode from path like "Series Name/Season 01/S01E01.mkv"
            var fileName = Path.GetFileNameWithoutExtension(path);

            // Match patterns like S01E01, S1E1, etc.
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"S(\d{1,2})E(\d{1,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var season))
                    item.SeasonNumber = season;
                if (int.TryParse(match.Groups[2].Value, out var episode))
                    item.EpisodeNumber = episode;
            }
        }

        private class SeriesInfo
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public int TvdbId { get; set; }
            public string Path { get; set; } = string.Empty;
        }

        private class MovieInfo
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public int TmdbId { get; set; }
            public string Path { get; set; } = string.Empty;
        }
    }
}
