# Bulk Catalog Conversion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a grouped catalog view to the Media tab that pulls the full library from Sonarr/Radarr, merges with Storarr tracked state, and enables bulk MKV/symlink conversion via checkboxes with confirmation.

**Architecture:** Frontend-orchestrated batch — new catalog API endpoint serves grouped show/movie data from Sonarr/Radarr merged with Storarr's DB. Frontend loops over existing per-item transition endpoints for batch operations. Lazy episode loading on expand.

**Tech Stack:** ASP.NET Core 6.0 (C#), EF Core + SQLite, React 18 + TypeScript + Vite + Tailwind + Zustand, SignalR, Sonarr/Radarr v3 REST APIs

**Spec:** `docs/superpowers/specs/2026-04-13-bulk-catalog-conversion-design.md`

---

## File Structure

### New files
| File | Responsibility |
|------|---------------|
| `src/Storarr/DTOs/CatalogDto.cs` | DTOs: `CatalogGroupDto`, `CatalogEpisodeDto`, `EnsureTrackedRequestDto`, `EnsureTrackedResponseDto` |
| `src/Storarr/Controllers/CatalogController.cs` | `GET /api/v1/catalog`, `GET /api/v1/catalog/{sonarrId}/episodes`, and `POST /api/v1/media/ensure-tracked` endpoints |
| `src/Storarr.Frontend/src/components/CatalogView.tsx` | Grouped catalog view with expandable shows and checkboxes |
| `src/Storarr.Frontend/src/components/BulkActionBar.tsx` | Sticky bottom action bar with convert buttons |
| `src/Storarr.Frontend/src/components/ConfirmationDialog.tsx` | Confirmation modal before batch conversion |

### Modified files
| File | Change |
|------|--------|
| `src/Storarr/Services/ISonarrService.cs:21-28` | Add `TmdbId`, `Images` to `Series` model |
| `src/Storarr/Services/SonarrService.cs:72-79` | Map `TmdbId`, `Images` from internal model |
| `src/Storarr/Services/SonarrService.cs:360-367` | Add `TmdbId`, `Images` to `SonarrSeries` internal model |
| `src/Storarr/Services/IRadarrService.cs:20-28` | Add `Images` to `Movie` model |
| `src/Storarr/Services/RadarrService.cs:72-80` | Map `Images` from internal model |
| `src/Storarr/Services/RadarrService.cs:325-333` | Add `Images` to `RadarrMovie` internal model |
| `src/Storarr.Frontend/src/api/client.ts` | Add `getCatalog()` and `ensureTracked()` functions |
| `src/Storarr.Frontend/src/stores/appStore.ts` | Add catalog DTO types |
| `src/Storarr.Frontend/src/pages/Media.tsx` | Add toggle button, conditionally render CatalogView |

---

## Task 1: Add TmdbId and Images to Sonarr Models

**Files:**
- Modify: `src/Storarr/Services/ISonarrService.cs:21-28` (Series model)
- Modify: `src/Storarr/Services/SonarrService.cs:72-79` (mapping) and `:360-367` (SonarrSeries)

- [ ] **Step 1: Update `Series` model in `ISonarrService.cs`**

After line 27 (`QualityProfileId`), add:

```csharp
public int? TmdbId { get; set; }
public List<SonarrImage>? Images { get; set; }
```

After the `SonarrQueueItem` class (line 51), add:

```csharp
public class SonarrImage
{
    public string? CoverType { get; set; }
    public string? Url { get; set; }
    public string? RemoteUrl { get; set; }
}
```

- [ ] **Step 2: Update `SonarrSeries` internal model in `SonarrService.cs`**

After line 366 (`QualityProfileId`), add:

```csharp
public int? TmdbId { get; set; }
public List<SonarrImage>? Images { get; set; }
```

- [ ] **Step 3: Update mapping in `SonarrService.GetSeries()` at line 72-79**

Add after `QualityProfileId = s.QualityProfileId`:

```csharp
TmdbId = s.TmdbId,
Images = s.Images
```

- [ ] **Step 4: Update mapping in `SonarrService.GetSeries(int)` around line 109**

Add `TmdbId` and `Images` to the mapping.

- [ ] **Step 5: Build and verify**

Run: `cd /path/to/storarr/src/Storarr && dotnet build`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add src/Storarr/Services/ISonarrService.cs src/Storarr/Services/SonarrService.cs
git commit -m "feat: add TmdbId and Images to Sonarr Series model"
```

---

## Task 2: Add Images to Radarr Models

**Files:**
- Modify: `src/Storarr/Services/IRadarrService.cs:20-28` (Movie model)
- Modify: `src/Storarr/Services/RadarrService.cs:72-80` (mapping) and `:325-333` (RadarrMovie)

- [ ] **Step 1: Update `Movie` model in `IRadarrService.cs`**

After line 27 (`MovieFileId`), add:

```csharp
public List<SonarrImage>? Images { get; set; }
```

No need to create a separate `RadarrImage` class — reuse `SonarrImage` from Task 1 (identical fields). Both Sonarr and Radarr return the same `images` JSON structure.

- [ ] **Step 2: Update `RadarrMovie` internal model in `RadarrService.cs`**

After line 332 (`MovieFileId`), add:

```csharp
public List<RadarrImage>? Images { get; set; }
```

- [ ] **Step 3: Update mapping in `RadarrService.GetMovies()` at line 72-80**

Add after `MovieFileId = m.MovieFileId`:

```csharp
Images = m.Images
```

- [ ] **Step 4: Update mapping in `RadarrService.GetMovie(int)` around line 109**

Add `Images` to the mapping.

- [ ] **Step 5: Build and verify**

Run: `cd /path/to/storarr/src/Storarr && dotnet build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Storarr/Services/IRadarrService.cs src/Storarr/Services/RadarrService.cs
git commit -m "feat: add Images to Radarr Movie model"
```

---

## Task 3: Create Catalog DTOs

**Files:**
- Create: `src/Storarr/DTOs/CatalogDto.cs`

- [ ] **Step 1: Create the DTO file**

```csharp
using System.Collections.Generic;
using Storarr.Models;

namespace Storarr.DTOs
{
    public class CatalogGroupDto
    {
        public string Title { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string? PosterUrl { get; set; }
        public int TotalEpisodes { get; set; }
        public int TrackedEpisodes { get; set; }
        public long TotalSizeBytes { get; set; }
        public string FormattedSize { get; set; } = string.Empty;
        public Dictionary<string, int> StateBreakdown { get; set; } = new();
        public bool IsExcluded { get; set; }
        public List<CatalogEpisodeDto> Episodes { get; set; } = new();
    }

    public class CatalogEpisodeDto
    {
        public int? MediaItemId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CurrentState { get; set; } = string.Empty;
        public long? FileSize { get; set; }
        public bool IsExcluded { get; set; }
        public string? FilePath { get; set; }
    }

    public class EnsureTrackedRequestDto
    {
        public int? SonarrId { get; set; }
        public int? RadarrId { get; set; }
        public MediaType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public int? TmdbId { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }

    public class EnsureTrackedResponseDto
    {
        public int MediaItemId { get; set; }
        public bool Created { get; set; }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr/DTOs/CatalogDto.cs
git commit -m "feat: add catalog DTOs for grouped library view"
```

---

## Task 4: Create Catalog Controller — Scaffold and GetCatalog

**Files:**
- Create: `src/Storarr/Controllers/CatalogController.cs`

- [ ] **Step 1: Create the controller with scaffold and `GetCatalog` endpoint**

The controller has three endpoints:
1. `GET /api/v1/catalog` — returns show/movie list **without episodes** (lazy loading)
2. `GET /api/v1/catalog/{sonarrId}/episodes` — returns episodes for a single show (called on expand)
3. `POST /api/v1/media/ensure-tracked` — creates a MediaItem if it doesn't exist

Key implementation details for `GetCatalog`:
- Inject `ISonarrService`, `IRadarrService`, `IMemoryCache`, `StorarrDbContext`, `IFileManagementService`, `ILogger<CatalogController>`
- **Do NOT load episodes in GetCatalog** — return groups with empty `Episodes` list. Compute `TotalEpisodes` and `StateBreakdown` from tracked items in Storarr DB only (no Sonarr episode API call).
- Cross-reference `MediaItems` table by `SonarrId`/`RadarrId` to count tracked episodes per show
- Cross-reference `ExcludedItems` table for show-level exclusion
- Cache the Sonarr series list and Radarr movie list in `IMemoryCache` for 5 minutes (key: `"sonarr_series"`, `"radarr_movies"`)
- `FormatSize` helper converts bytes to human-readable string
- `GetPosterUrl` extracts poster URL from `SonarrImage`/`RadarrImage` list (same shape, use `SonarrImage` for both since fields are identical)

For series groups without episode expansion, aggregate size from tracked `MediaItem.FileSize` where `SonarrId` matches.

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Storarr.Data;
using Storarr.DTOs;
using Storarr.Models;
using Storarr.Services;

namespace Storarr.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CatalogController : ControllerBase
    {
        private readonly ISonarrService _sonarrService;
        private readonly IRadarrService _radarrService;
        private readonly IMemoryCache _cache;
        private readonly StorarrDbContext _dbContext;
        private readonly IFileManagementService _fileService;
        private readonly ILogger<CatalogController> _logger;

        public CatalogController(
            ISonarrService sonarrService,
            IRadarrService radarrService,
            IMemoryCache cache,
            StorarrDbContext dbContext,
            IFileManagementService fileService,
            ILogger<CatalogController> logger)
        {
            _sonarrService = sonarrService;
            _radarrService = radarrService;
            _cache = cache;
            _dbContext = dbContext;
            _fileService = fileService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CatalogGroupDto>>> GetCatalog(
            [FromQuery] MediaType? type = null,
            [FromQuery] string? search = null)
        {
            try
            {
                var result = new List<CatalogGroupDto>();
                var trackedItems = await _dbContext.MediaItems.AsNoTracking().ToListAsync();
                var excludedItems = await _dbContext.ExcludedItems.AsNoTracking().ToListAsync();

                // Series (from Sonarr) — cached
                if (type == null || type == MediaType.Series || type == MediaType.Anime)
                {
                    var seriesList = await _cache.GetOrCreateAsync("sonarr_series", entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                        return _sonarrService.GetSeries();
                    }) ?? Enumerable.Empty<Series>();

                    foreach (var series in seriesList)
                    {
                        if (!string.IsNullOrEmpty(search) &&
                            !series.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var seriesTracked = trackedItems.Where(t => t.SonarrId == series.Id).ToList();
                        var stateBreakdown = seriesTracked
                            .GroupBy(t => t.CurrentState.ToString())
                            .ToDictionary(g => g.Key, g => g.Count());
                        var untrackedCount = 0; // Unknown without episode API call
                        var totalSize = seriesTracked.Sum(t => t.FileSize ?? 0);

                        result.Add(new CatalogGroupDto
                        {
                            Title = series.Title,
                            Type = MediaType.Series,
                            SonarrId = series.Id,
                            TmdbId = series.TmdbId,
                            TvdbId = series.TvdbId > 0 ? series.TvdbId : null,
                            PosterUrl = GetPosterUrl(series.Images),
                            TotalEpisodes = seriesTracked.Count,
                            TrackedEpisodes = seriesTracked.Count,
                            TotalSizeBytes = totalSize,
                            FormattedSize = FormatSize(totalSize),
                            StateBreakdown = stateBreakdown,
                            IsExcluded = excludedItems.Any(e => e.SonarrId == series.Id),
                            Episodes = new List<CatalogEpisodeDto>() // Lazy — loaded on expand
                        });
                    }
                }

                // Movies (from Radarr) — cached
                if (type == null || type == MediaType.Movie)
                {
                    var movies = await _cache.GetOrCreateAsync("radarr_movies", entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                        return _radarrService.GetMovies();
                    }) ?? Enumerable.Empty<Movie>();

                    foreach (var movie in movies)
                    {
                        if (!string.IsNullOrEmpty(search) &&
                            !movie.Title.Contains(search, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var tracked = trackedItems.FirstOrDefault(t => t.RadarrId == movie.Id);
                        var state = tracked?.CurrentState.ToString() ?? "Untracked";

                        result.Add(new CatalogGroupDto
                        {
                            Title = movie.Title,
                            Type = MediaType.Movie,
                            RadarrId = movie.Id,
                            TmdbId = movie.TmdbId,
                            PosterUrl = GetPosterUrl(movie.Images),
                            TotalEpisodes = 1,
                            TrackedEpisodes = tracked != null ? 1 : 0,
                            TotalSizeBytes = tracked?.FileSize ?? 0,
                            FormattedSize = FormatSize(tracked?.FileSize ?? 0),
                            StateBreakdown = new Dictionary<string, int> { { state, 1 } },
                            IsExcluded = excludedItems.Any(e => e.RadarrId == movie.Id),
                            Episodes = tracked != null ? new List<CatalogEpisodeDto>
                            {
                                new()
                                {
                                    MediaItemId = tracked.Id,
                                    Title = movie.Title,
                                    CurrentState = state,
                                    FileSize = tracked.FileSize,
                                    IsExcluded = tracked.IsExcluded,
                                    FilePath = tracked.FilePath
                                }
                            } : new List<CatalogEpisodeDto>()
                        });
                    }
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetCatalog");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private static string? GetPosterUrl(List<SonarrImage>? images)
        {
            if (images == null || images.Count == 0) return null;
            var poster = images.FirstOrDefault(i =>
                i.CoverType?.Equals("poster", StringComparison.OrdinalIgnoreCase) == true);
            return poster?.RemoteUrl ?? poster?.Url;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = (int)Math.Floor(Math.Log(bytes, 1024));
            return $"{bytes / Math.Pow(1024, order):F1} {sizes[order]}";
        }
    }
}
```

Note on poster URL typing: `SonarrImage` and `RadarrImage` have identical fields. In Task 1, use `SonarrImage` for the `Series.Images` type. In Task 2, also use `SonarrImage` for the `Movie.Images` type (instead of creating a separate `RadarrImage`). This way `GetPosterUrl` works for both without casting.

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr && dotnet build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr/Controllers/CatalogController.cs
git commit -m "feat: add catalog controller scaffold with lazy-loaded GetCatalog endpoint"
```

---

## Task 5: Add Episodes Endpoint and EnsureTracked

**Files:**
- Modify: `src/Storarr/Controllers/CatalogController.cs` — add two new endpoints

- [ ] **Step 1: Add `GET /api/v1/catalog/{sonarrId}/episodes` endpoint**

This endpoint is called by the frontend when a show row is expanded. Returns full episode data for a single series.

```csharp
[HttpGet("{sonarrId}/episodes")]
public async Task<ActionResult<IEnumerable<CatalogEpisodeDto>>> GetSeriesEpisodes(int sonarrId)
{
    try
    {
        var trackedItems = await _dbContext.MediaItems.AsNoTracking()
            .Where(m => m.SonarrId == sonarrId)
            .ToListAsync();

        var episodeFiles = await _sonarrService.GetEpisodeFiles(sonarrId);
        var series = await _sonarrService.GetSeries(sonarrId);
        var seriesTitle = series?.Title ?? $"Series {sonarrId}";

        var episodes = episodeFiles.Select(ep =>
        {
            var tracked = trackedItems.FirstOrDefault(t =>
                t.SeasonNumber == ep.SeasonNumber &&
                t.EpisodeNumber == ep.EpisodeNumber);

            return new CatalogEpisodeDto
            {
                MediaItemId = tracked?.Id,
                SeasonNumber = ep.SeasonNumber,
                EpisodeNumber = ep.EpisodeNumber,
                Title = $"{seriesTitle} S{ep.SeasonNumber:D2}E{ep.EpisodeNumber:D2}",
                CurrentState = tracked?.CurrentState.ToString() ?? "Untracked",
                FileSize = tracked?.FileSize ?? ep.Size,
                IsExcluded = tracked?.IsExcluded ?? false,
                FilePath = ep.Path
            };
        }).ToList();

        return Ok(episodes);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error getting episodes for series {SonarrId}", sonarrId);
        return StatusCode(500, new { error = ex.Message });
    }
}
```

- [ ] **Step 2: Add `POST /api/v1/media/ensure-tracked` endpoint**

```csharp
[HttpPost("/api/v1/media/ensure-tracked")]
public async Task<ActionResult<EnsureTrackedResponseDto>> EnsureTracked(
    [FromBody] EnsureTrackedRequestDto dto)
{
    try
    {
        // Idempotency: check if already tracked
        MediaItem? existing = null;
        if (dto.SonarrId.HasValue && dto.SeasonNumber.HasValue && dto.EpisodeNumber.HasValue)
        {
            existing = await _dbContext.MediaItems.FirstOrDefaultAsync(m =>
                m.SonarrId == dto.SonarrId &&
                m.SeasonNumber == dto.SeasonNumber &&
                m.EpisodeNumber == dto.EpisodeNumber);
        }
        else if (dto.RadarrId.HasValue)
        {
            existing = await _dbContext.MediaItems.FirstOrDefaultAsync(m =>
                m.RadarrId == dto.RadarrId);
        }

        if (existing != null)
        {
            return Ok(new EnsureTrackedResponseDto
            {
                MediaItemId = existing.Id,
                Created = false
            });
        }

        // Validate TmdbId (required for TransitionToSymlink)
        if (!dto.TmdbId.HasValue)
        {
            return BadRequest(new { error = "TmdbId is required for tracking." });
        }

        // Validate file path
        try
        {
            await _fileService.ValidatePath(dto.FilePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var item = new MediaItem
        {
            Title = dto.Title,
            Type = dto.Type,
            SonarrId = dto.SonarrId,
            RadarrId = dto.RadarrId,
            TmdbId = dto.TmdbId,
            FilePath = dto.FilePath,
            SeasonNumber = dto.SeasonNumber,
            EpisodeNumber = dto.EpisodeNumber,
            CurrentState = FileState.Symlink, // Must be Symlink so force-download state check passes
            CreatedAt = DateTime.UtcNow,
            StateChangedAt = DateTime.UtcNow,
            IsExcluded = false
        };

        _dbContext.MediaItems.Add(item);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created tracked media item: {Title} (ID: {Id})", item.Title, item.Id);

        return Ok(new EnsureTrackedResponseDto
        {
            MediaItemId = item.Id,
            Created = true
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error in EnsureTracked");
        return StatusCode(500, new { error = ex.Message });
    }
}
```

- [ ] **Step 3: Build and verify**

Run: `cd /path/to/storarr/src/Storarr && dotnet build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Controllers/CatalogController.cs
git commit -m "feat: add episodes endpoint and ensure-tracked to catalog controller"
```

---

## Task 6: Add Frontend Types and API Client

**Files:**
- Modify: `src/Storarr.Frontend/src/stores/appStore.ts` (add catalog types)
- Modify: `src/Storarr.Frontend/src/api/client.ts` (add API functions)

- [ ] **Step 1: Add catalog types to `appStore.ts`**

After the `ExcludedItem` interface (line 66), add:

```typescript
export interface CatalogGroupDto {
  title: string
  type: 'Movie' | 'Series' | 'Anime'
  sonarrId?: number
  radarrId?: number
  tmdbId?: number
  tvdbId?: number
  posterUrl?: string
  totalEpisodes: number
  trackedEpisodes: number
  totalSizeBytes: number
  formattedSize: string
  stateBreakdown: Record<string, number>
  isExcluded: boolean
  episodes: CatalogEpisodeDto[]
}

export interface CatalogEpisodeDto {
  mediaItemId?: number
  seasonNumber?: number
  episodeNumber?: number
  title: string
  currentState: string
  fileSize?: number
  isExcluded: boolean
  filePath?: string
}

export interface EnsureTrackedRequestDto {
  sonarrId?: number
  radarrId?: number
  type: 'Movie' | 'Series' | 'Anime'
  title: string
  seasonNumber?: number
  episodeNumber?: number
  tmdbId?: number
  filePath: string
}

export interface EnsureTrackedResponseDto {
  mediaItemId: number
  created: boolean
}
```

- [ ] **Step 2: Add API functions to `client.ts`**

After the exclusions section (line 108), add:

```typescript
// Catalog
export const getCatalog = (params?: { type?: string; search?: string }) =>
  api.get<CatalogGroupDto[]>('/catalog', { params })

export const getSeriesEpisodes = (sonarrId: number) =>
  api.get<CatalogEpisodeDto[]>(`/catalog/${sonarrId}/episodes`)

export const ensureTracked = (data: EnsureTrackedRequestDto) =>
  api.post<EnsureTrackedResponseDto>('/media/ensure-tracked', data)
```

Also add the imports at the top:

```typescript
import type { CatalogGroupDto, EnsureTrackedRequestDto, EnsureTrackedResponseDto } from '../stores/appStore'
```

- [ ] **Step 3: Build and verify**

Run: `cd /path/to/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr.Frontend/src/stores/appStore.ts src/Storarr.Frontend/src/api/client.ts
git commit -m "feat: add catalog types and API client functions"
```

---

## Task 7: Create ConfirmationDialog Component

**Files:**
- Create: `src/Storarr.Frontend/src/components/ConfirmationDialog.tsx`

- [ ] **Step 1: Create the component**

Props:
- `open: boolean` — whether dialog is visible
- `title: string` — dialog title (e.g. "Convert 47 items to Symlink")
- `items: CatalogEpisodeDto[]` — selected items to display
- `action: 'toMkv' | 'toSymlink'` — which conversion direction
- `onConfirm: () => void`
- `onCancel: () => void`

Behavior:
- Show scrollable list of items with title, state badge, and whether eligible
- Items in wrong state shown greyed out with "(not eligible)" note
- For `toMkv`: eligible if state is `Symlink`, `PendingSymlink`, or `Untracked`
- For `toSymlink`: eligible if state is `Mkv` or `Downloading`
- Show count of ineligible items if any
- Cancel and Confirm buttons (Confirm disabled if 0 eligible items)

Use existing Tailwind classes and Lucide icons matching the project style.

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/components/ConfirmationDialog.tsx
git commit -m "feat: add ConfirmationDialog for bulk conversion preview"
```

---

## Task 8: Create BulkActionBar Component

**Files:**
- Create: `src/Storarr.Frontend/src/components/BulkActionBar.tsx`

- [ ] **Step 1: Create the component**

Props:
- `selectedCount: number`
- `hasEligibleForMkv: boolean` — any selected item that is Symlink/PendingSymlink/Untracked
- `hasEligibleForSymlink: boolean` — any selected item that is Mkv/Downloading
- `onConvertToMkv: () => void`
- `onConvertToSymlink: () => void`

Behavior:
- Fixed to bottom of viewport with `fixed bottom-0 left-0 right-0 z-50`
- Only visible when `selectedCount > 0`
- Shows "{N} items selected" text
- Two buttons: "Convert to MKV" and "Convert to Symlink"
- Each button disabled if no eligible items for that action
- Use `Download` icon for MKV, `Link2` icon for Symlink (matching existing Media.tsx)
- Use project Tailwind classes

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/components/BulkActionBar.tsx
git commit -m "feat: add BulkActionBar with convert buttons"
```

---

## Task 9: Create CatalogView Component

**Files:**
- Create: `src/Storarr.Frontend/src/components/CatalogView.tsx`

This is the largest frontend task. The component renders the grouped library view.

- [ ] **Step 1: Create the component**

State:
- `catalog: CatalogGroupDto[]` — loaded catalog data (groups loaded, episodes empty for series)
- `loading: boolean`
- `expandedGroups: Set<number>` — which series are expanded (keyed by sonarrId)
- `loadingEpisodes: Set<number>` — which series are currently loading episodes
- `selectedEpisodes: Map<string, CatalogEpisodeDto>` — selected episodes (keyed by `${sonarrId}-${season}-${episode}` or `${radarrId}`)
- `confirmDialog: { open, action, items } | null`
- `batchProgress: { completed: number, failed: number, total: number, running: boolean } | null`
- `batchResult: { completed: number, failed: number, total: number } | null` — shown as toast when batch completes

Behavior:
- On mount, call `getCatalog()` and populate `catalog` (series groups have empty Episodes list)
- Render show rows: checkbox, expand chevron, title, tracked episode count, size, state badges
- **Lazy episode loading**: When a show row is expanded, call `getSeriesEpisodes(sonarrId)` to fetch episodes. Cache loaded episodes in the `catalog` state. Show spinner while loading.
- Expanded shows display per-episode rows with checkboxes
- Movies display as single rows with checkboxes (episodes already populated from catalog)
- Show header checkbox selects/deselects all loaded episodes in that show
- Selection changes propagate to `BulkActionBar` props
- Clicking a convert button in `BulkActionBar` opens `ConfirmationDialog`
- On confirm: execute batch conversion loop

Progress UI during batch:
- When `batchProgress.running` is true, show a progress bar below the filter bar: "Converting 23/47..."
- When batch completes, show a result toast (auto-dismiss after 5 seconds): "45/47 converted successfully, 2 failed"

Batch execution logic (in a helper function):
```
async function executeBatch(items, action) {
  let completed = 0, failed = 0
  setBatchProgress({ completed: 0, failed: 0, total: items.length, running: true })

  for (const item of items) {
    try {
      let id = item.mediaItemId
      if (!id) {
        const resp = await ensureTracked({
          sonarrId: item.sonarrId,
          radarrId: item.radarrId,
          type: item.type,
          title: item.title,
          seasonNumber: item.seasonNumber,
          episodeNumber: item.episodeNumber,
          tmdbId: item.tmdbId,
          filePath: item.filePath ?? ''
        })
        id = resp.data.mediaItemId
      }
      if (action === 'toMkv') await forceDownload(id)
      else await forceSymlink(id)
      completed++
    } catch (e) {
      failed++
      console.error(`Failed for ${item.title}:`, e)
    }
    setBatchProgress(prev => ({ ...prev, completed, failed }))
    await new Promise(r => setTimeout(r, 200)) // Rate limit
  }
  setBatchProgress({ completed, failed, total: items.length, running: false })
  setBatchResult({ completed, failed, total: items.length })
}
```

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/components/CatalogView.tsx
git commit -m "feat: add CatalogView with grouped library, selection, and batch conversion"
```

---

## Task 10: Integrate CatalogView into Media Page

**Files:**
- Modify: `src/Storarr.Frontend/src/pages/Media.tsx`

- [ ] **Step 1: Add toggle button and conditional rendering**

In the filter bar section (after line 153, before the media list table), add a toggle button:

```tsx
<button
  onClick={() => setViewMode(viewMode === 'flat' ? 'grouped' : 'flat')}
  className={`px-4 py-2 rounded-lg ${viewMode === 'grouped' ? 'bg-arr-accent text-white' : 'bg-arr-bg border border-arr-primary'}`}
>
  {viewMode === 'flat' ? 'Grouped' : 'Flat'}
</button>
```

Add state: `const [viewMode, setViewMode] = useState<'flat' | 'grouped'>('flat')`

Conditionally render:
```tsx
{viewMode === 'flat' ? (
  /* existing table */
) : (
  <CatalogView filters={{ search, stateFilter, typeFilter }} />
)}
```

Import `CatalogView` at the top.

- [ ] **Step 2: Build and verify**

Run: `cd /path/to/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/pages/Media.tsx
git commit -m "feat: add flat/grouped toggle to Media page with CatalogView integration"
```

---

## Task 11: Build Docker Image and Smoke Test

**Files:**
- No new files

- [ ] **Step 1: Build the Docker image**

Run: `cd /path/to/storarr && docker compose build`
Expected: Build succeeds (multi-stage: node frontend + .NET backend).

- [ ] **Step 2: Start the container**

Run: `cd /path/to/storarr && docker compose up -d`

- [ ] **Step 3: Verify the catalog API**

Run: `curl -s http://localhost:8686/api/v1/catalog | head -c 500`
Expected: JSON array of catalog groups.

- [ ] **Step 4: Verify the frontend loads**

Open `http://localhost:8686` in a browser, navigate to Media tab, verify the toggle appears.

- [ ] **Step 5: Commit any fixes**

If any issues were found, fix and commit.

---

## Task 12: Final Commit and Tag

- [ ] **Step 1: Ensure clean working tree**

Run: `cd /path/to/storarr && git status`
Expected: Clean working tree or only untracked files.

- [ ] **Step 2: Tag the release**

```bash
git tag -a v1.1.0 -m "feat: bulk catalog conversion with grouped library view"
```
