# Bulk Catalog Conversion Design

## Problem

The `/mnt/library` drive is 98% full (6.8TB / 7TB). Managing disk space requires converting individual episodes between MKV (local storage) and symlink (streaming) one at a time. A show like Breaking Bad (470GB, 62 episodes) requires 62 separate clicks. Users need a way to browse their entire library and perform bulk conversions.

## Solution

Add a grouped catalog view to the existing Media tab that pulls the full show/movie library from Sonarr and Radarr, merges it with Storarr's tracked state, and enables bulk conversion via checkboxes with a confirmation step.

## User Flow

1. User opens Media tab and toggles to "Grouped" view
2. Full library loads from Sonarr (series) and Radarr (movies)
3. Shows appear as collapsed rows with aggregated size and state breakdown
4. User expands a show to see per-episode details
5. User checks episodes (or the show header to select all)
6. Sticky bottom action bar appears with "Convert to MKV" / "Convert to Symlink"
7. User clicks a convert button
8. Confirmation dialog shows selected items and their current states
9. On confirm, frontend loops through eligible items calling existing per-item endpoints
10. Progress indicator updates as each item completes

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Data source | Sonarr/Radarr catalogs | Has arr IDs needed to trigger downloads/deletions; covers full library |
| Batch execution | Frontend loop over existing endpoints | No new batch API needed; reuses proven per-item transition logic |
| Grouping UX | Toggle between flat and grouped views | Preserves existing workflow; adds new capability without disrupting |
| Selection model | Per-episode checkboxes + show-level select-all | Flexible: cherry-pick episodes or convert entire shows |
| Untracked items | "Convert to MKV" only (triggers arr search) | No file exists to symlink from; can only download |
| Episode data source | Sonarr/Radarr file-level data only | Use existing `GetEpisodeFiles`/`GetMovieFile` — only show episodes that have files on disk, avoiding N+1 API calls |

## Architecture

### Frontend-orchestrated batch (Approach A)

The frontend handles grouping, selection, and confirmation. It loops through selected items calling the existing `POST /api/v1/media/{id}/force-download` and `POST /api/v1/media/{id}/force-symlink` endpoints for each one.

Trade-off: Many sequential API calls for large shows (e.g. 62 for Breaking Bad). Acceptable because transitions are already async (trigger search, monitor download). If performance becomes an issue, a batch API can be added later without changing the UI.

## Backend Changes

### Service model updates

**`SonarrService` / `ISonarrService`:**
- Add `TmdbId` and `Images` (poster URL) to the `Series` model (mapped from Sonarr v3 API response)
- Add `GetEpisodeFiles(int seriesId)` to `ISonarrService` (method exists in `SonarrService` but not in the interface — add it)
- Sonarr v3 `/api/v3/series` returns `tmdbId` and `images[]` — map these during deserialization

**`RadarrService` / `IRadarrService`:**
- Add `Images` (poster URL) to the `Movie` model (`TmdbId` already exists)
- Radarr v3 `/api/v3/movie` returns `images[]` — map during deserialization

**Poster URL extraction:** Both Sonarr and Radarr return images as `[{ coverType: "poster", url: "..." }, ...]`. Extract the first `coverType == "poster"` entry.

### New endpoint: `GET /api/v1/catalog`

Returns the full library merged with Storarr tracked state.

**Controller:** `CatalogController.cs` — injects `ISonarrService`, `IRadarrService`, `StorarrDbContext` (use interfaces, not concrete classes, per existing DI patterns).

**Query parameters:**
- `type` — `Series`, `Movie`, or `Anime` (optional, defaults to all)
- `search` — text filter (optional)

**Response shape (DTO: `CatalogGroupDto`):**
```json
[
  {
    "title": "Breaking Bad",
    "type": "Series",
    "sonarrId": 42,
    "radarrId": null,
    "tmdbId": 1396,
    "tvdbId": 81189,
    "posterUrl": "https://image.tmdb.org/...",
    "totalEpisodes": 62,
    "trackedEpisodes": 62,
    "totalSizeBytes": 504652656640,
    "formattedSize": "470 GB",
    "stateBreakdown": {
      "Mkv": 12,
      "Symlink": 45,
      "Downloading": 0,
      "PendingSymlink": 0,
      "Untracked": 5
    },
    "isExcluded": false,
    "episodes": [
      {
        "mediaItemId": 101,
        "seasonNumber": 1,
        "episodeNumber": 1,
        "title": "Pilot",
        "currentState": "Mkv",
        "fileSize": 4500000000,
        "isExcluded": false,
        "filePath": "/mnt/library/data/media/tv/Breaking Bad/Season 1/s01e01.mkv"
      }
    ]
  }
]
```

For movies (DTO: `CatalogGroupDto` — same type, no episodes to expand):
```json
[
  {
    "title": "Inception",
    "type": "Movie",
    "sonarrId": null,
    "radarrId": 15,
    "tmdbId": 27205,
    "tvdbId": null,
    "posterUrl": "https://image.tmdb.org/...",
    "totalEpisodes": 1,
    "trackedEpisodes": 1,
    "totalSizeBytes": 45000000000,
    "formattedSize": "45 GB",
    "stateBreakdown": { "Mkv": 1 },
    "isExcluded": false,
    "episodes": [
      {
        "mediaItemId": 205,
        "seasonNumber": null,
        "episodeNumber": null,
        "title": "Inception",
        "currentState": "Mkv",
        "fileSize": 45000000000,
        "isExcluded": false,
        "filePath": "/mnt/library/data/media/movies/Inception (2010)/Inception.mkv"
      }
    ]
  }
]
```

**DTO classes (in `CatalogDto.cs`):**
- `CatalogGroupDto` — show/movie level
- `CatalogEpisodeDto` — per-episode detail
- `EnsureTrackedRequestDto` — for ensure-tracked endpoint
- `EnsureTrackedResponseDto` — response with mediaItemId

**`stateBreakdown` keys:** Use string representations of `FileState` enum values (`"Mkv"`, `"Symlink"`, `"Downloading"`, `"PendingSymlink"`) plus `"Untracked"` for items in Sonarr/Radarr but not in Storarr's DB. `"Untracked"` is a virtual state not in the enum.

**`isExcluded` at group level:** Check both `ExcludedItems` table (per-show exclusion) and `MediaItem.IsExcluded` (per-episode). Show-level `isExcluded` is `true` if the show has an entry in `ExcludedItems`.

**`fileSize` source:** Use `MediaItem.FileSize` from Storarr's DB for tracked items. For untracked items, use the file size from Sonarr/Radarr episode file data. If neither is available, `null`.

**Implementation:**
1. Call `ISonarrService.GetSeries()` (existing method) to get all series
2. For each series, call `GetEpisodeFiles(seriesId)` to get episodes with files on disk
3. Call `IRadarrService.GetMovies()` (existing method) to get all movies
4. Cross-reference each item with `MediaItems` table by `SonarrId` / `RadarrId` + season/episode
5. Cross-reference `ExcludedItems` table for show-level exclusion status
6. Group episodes under their parent show
7. Return aggregated sizes and state counts

**Performance:** Two bulk calls (GetSeries, GetMovies) + N calls for episode files. Episode file data is cached by SonarrService with a 5-minute TTL to avoid N+1 on repeated requests. First load may be slow for large libraries; subsequent loads use cached data. Episodes are loaded lazily — the collapsed show list loads first, episodes load on expand.

### Existing endpoints (no changes)

- `POST /api/v1/media/{id}/force-download` — reused for symlink-to-MKV conversion. Accepts `Symlink` or `PendingSymlink` state.
- `POST /api/v1/media/{id}/force-symlink` — reused for MKV-to-symlink conversion. Accepts `Mkv` or `Downloading` state.

### Untracked item handling

Items in Sonarr/Radarr but not in Storarr's `MediaItems` table:
- `mediaItemId` is `null`
- `currentState` is `"Untracked"`
- `isExcluded` is `false` (no Storarr record to be excluded)
- "Convert to MKV" triggers Sonarr/Radarr series/movie search (the existing transition logic already does this)
- "Convert to Symlink" is disabled — no file exists to convert

When an untracked item is selected for "Convert to MKV":
1. Frontend checks if `mediaItemId` is null
2. If null, calls `POST /api/v1/media/ensure-tracked` which creates a `MediaItem` record in `Symlink` state, then calls `force-download` on it

**Preferred approach**: Lazy creation via `ensure-tracked` — only create records when the user actually acts on an item.

### New endpoint: `POST /api/v1/media/ensure-tracked`

Creates a `MediaItem` for a Sonarr/Radarr item if one doesn't exist, then returns the ID. **Idempotent** — if a `MediaItem` with matching `SonarrId`/`RadarrId` + `SeasonNumber`/`EpisodeNumber` already exists, returns its ID without creating a duplicate.

**Request DTO (`EnsureTrackedRequestDto`):**
```json
{
  "sonarrId": 42,
  "radarrId": null,
  "type": "Series",
  "title": "Breaking Bad - S01E01",
  "seasonNumber": 1,
  "episodeNumber": 1,
  "tmdbId": 1396,
  "filePath": "/mnt/library/data/media/tv/Breaking Bad/Season 1/s01e01.strm"
}
```

**`filePath` is required** — `MediaItem` has `[Required]` on `FilePath`. The frontend passes the `filePath` from the catalog response (which comes from Sonarr/Radarr episode file data). If no file exists on disk yet (truly untracked), use the expected path based on the series path convention from Sonarr.

**`tmdbId` is required for series** — `TransitionService.TransitionToSymlink` aborts if `TmdbId` is null. For series, the updated `Series` model now carries `TmdbId` (mapped from Sonarr API). For movies, `TmdbId` already exists.

**`CurrentState` is set to `Symlink`** — This ensures the subsequent `force-download` call passes the state check (`item.CurrentState != FileState.Symlink`).

**Idempotency check:** Query by `SonarrId` + `SeasonNumber` + `EpisodeNumber` (for series) or `RadarrId` (for movies). If found, return existing ID.

**Response DTO (`EnsureTrackedResponseDto`):**
```json
{
  "mediaItemId": 303,
  "created": true
}
```

### Anime handling

Anime is tracked as separate series in Sonarr (distinguished by quality profile or tags). The `MediaType` enum has `{ Movie, Series, Anime }`. The catalog endpoint accepts `type=Anime` as a filter and passes it through. No special handling needed — Sonarr returns anime series alongside regular series, and the existing `GetSeries()` method returns all of them.

## Frontend Changes

### Media.tsx modifications

- Add a toggle button in the filter bar: "Flat" / "Grouped"
- When "Grouped" is selected, render `CatalogView` instead of the flat table
- Pass through existing state/type/search filters

### CatalogView component

Renders grouped library:

```
+---------------------------------------------------------------------+
| [v] Breaking Bad          62 episodes | 470 GB | 12 MKV, 45 Symlink |
|   +---------------------------------------------------------------+
|   | [x] S01E01 Pilot           MKV      4.5 GB                    |
|   | [x] S01E02 Cat's in the Bag MKV      4.2 GB                    |
|   | [ ] S01E03 ...              Symlink  12 MB                    |
|   | ...                                                           |
|   +---------------------------------------------------------------+
+---------------------------------------------------------------------+
| [ ] For All Mankind        40 episodes | 433 GB | 40 Symlink       |
+---------------------------------------------------------------------+
| [ ] Inception              Movie      | 45 GB   | 1 MKV            |
+---------------------------------------------------------------------+
```

- Show header row: checkbox, expand/collapse chevron, title, episode count, total size, state breakdown badges
- Expanded episodes: checkbox, season/episode number, title, state badge, file size
- Movies: single row with checkbox, no expansion needed
- **No per-episode action buttons** — all actions go through the bulk action bar
- Existing state/type filters apply to both show-level and episode-level filtering

### BulkActionBar component

Sticky bar fixed to bottom of viewport when items are selected:

```
+---------------------------------------------------------------------+
| 5 items selected                              [To MKV] [To Symlink] |
+---------------------------------------------------------------------+
```

- Shows count of selected items
- "Convert to MKV" button: enabled when any selected item is `Symlink`, `PendingSymlink`, or `Untracked`
- "Convert to Symlink" button: enabled when any selected item is `Mkv` or `Downloading`
- Buttons disabled if no eligible items for that action

### ConfirmationDialog component

Modal overlay:

```
+--------------------------------------------------+
| Convert 47 items to Symlink                      |
|                                                  |
| Breaking Bad - S01E01 Pilot (MKV -> Symlink)    |
| Breaking Bad - S01E02 Cat's in the Bag (MKV)    |
| ...                                              |
| For All Mankind - S01E01 (MKV -> Symlink)       |
|                                                  |
| 5 items cannot be converted (wrong state)        |
|                                                  |
|                    [Cancel]  [Confirm Convert]   |
+--------------------------------------------------+
```

- Scrollable list of selected items with current state
- Items in wrong state shown greyed out with note
- Count of ineligible items if any
- Cancel and Confirm buttons
- On confirm: close dialog, show progress indicator

### Batch execution flow

After confirmation:
1. Filter to eligible items only (correct state for the action)
2. For untracked items, call `ensure-tracked` first to get a `mediaItemId`
3. Call `force-download` or `force-symlink` for each item sequentially
4. Add 200ms delay between calls to avoid overwhelming Sonarr/Radarr
5. Show progress: "Converting 23/47..."
6. Track success/failure per item:
   - Success: increment completed count
   - Failure: log the error, increment failed count, continue with next item
7. On completion, show toast with result: "45/47 converted successfully, 2 failed"
8. Refresh catalog data

### client.ts additions

```typescript
getCatalog(params?: { type?: string; search?: string }): Promise<CatalogGroupDto[]>
ensureTracked(data: EnsureTrackedRequestDto): Promise<EnsureTrackedResponseDto>
```

### appStore.ts additions

```typescript
interface CatalogGroupDto {
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

interface CatalogEpisodeDto {
  mediaItemId?: number
  seasonNumber?: number
  episodeNumber?: number
  title: string
  currentState: string  // FileState enum string or "Untracked"
  fileSize?: number
  isExcluded: boolean
  filePath?: string
}

interface EnsureTrackedRequestDto {
  sonarrId?: number
  radarrId?: number
  type: 'Movie' | 'Series' | 'Anime'
  title: string
  seasonNumber?: number
  episodeNumber?: number
  tmdbId?: number
  filePath: string
}

interface EnsureTrackedResponseDto {
  mediaItemId: number
  created: boolean
}
```

## File & Component Summary

### New files
| File | Purpose |
|------|---------|
| `src/Storarr/Controllers/CatalogController.cs` | `/api/v1/catalog` and `/api/v1/media/ensure-tracked` endpoints |
| `src/Storarr/DTOs/CatalogDto.cs` | `CatalogGroupDto`, `CatalogEpisodeDto`, `EnsureTrackedRequestDto`, `EnsureTrackedResponseDto` |
| `src/Storarr.Frontend/src/components/CatalogView.tsx` | Grouped catalog view |
| `src/Storarr.Frontend/src/components/BulkActionBar.tsx` | Sticky bottom action bar |
| `src/Storarr.Frontend/src/components/ConfirmationDialog.tsx` | Confirmation modal |

### Modified files
| File | Change |
|------|--------|
| `src/Storarr.Frontend/src/pages/Media.tsx` | Add toggle button, conditionally render CatalogView |
| `src/Storarr.Frontend/src/api/client.ts` | Add `getCatalog()` and `ensureTracked()` API calls |
| `src/Storarr.Frontend/src/stores/appStore.ts` | Add catalog DTO types |
| `src/Storarr/Services/ISonarrService.cs` | Add `TmdbId`, `Images` to `Series` model; add `GetEpisodeFiles()` to interface |
| `src/Storarr/Services/SonarrService.cs` | Map `TmdbId` and `Images` from Sonarr API response; expose `GetEpisodeFiles()` via interface |
| `src/Storarr/Services/IRadarrService.cs` | Add `Images` to `Movie` model |
| `src/Storarr/Services/RadarrService.cs` | Map `Images` from Radarr API response |

### Unchanged
| File | Why |
|------|-----|
| `TransitionService.cs` | Reuse existing per-item transition logic |
| `MediaController.cs` | Reuse existing `force-download`/`force-symlink` endpoints |
| Database schema | No new tables needed |

## Edge Cases

- **Partially downloaded show**: Episodes in `Downloading` state are visible but excluded from symlink conversion (already in progress). They can be selected for "Convert to Symlink" since `force-symlink` accepts `Downloading` state.
- **Excluded items (per-episode)**: `MediaItem.IsExcluded` only affects automatic transitions. Manual conversion via catalog still works. Show with an "EXCLUDED" badge.
- **Excluded items (per-show)**: `ExcludedItems` table marks whole shows. Show-level `isExcluded` flag reflects this. User can still manually convert — exclusion only affects automatic scheduling.
- **Very large libraries**: Catalog endpoint uses lazy episode loading. Show list loads first (2 API calls: GetSeries + GetMovies). Episodes load per-show on expand (1 API call each, cached for 5 min).
- **Browser close during batch**: Partial state is acceptable — each item transitions independently. Items already converted remain converted.
- **Rate limiting**: Frontend adds 200ms delay between sequential API calls to avoid overwhelming Sonarr/Radarr.
- **Mixed-state batch**: Confirmation dialog shows which items are eligible and which aren't. Only eligible items are processed. Final toast reports success/failure counts (e.g. "45/47 converted, 2 failed").
- **TmdbId null guard**: `ensure-tracked` requires `tmdbId` for series (now available from updated `Series` model) to prevent `TransitionToSymlink` from silently failing. If `tmdbId` is unavailable, `ensure-tracked` returns an error for that item.
- **FilePath for untracked items**: Sonarr/Radarr episode file data provides the file path. If no file exists yet, the path is derived from the series path convention. `ensure-tracked` validates the path via `FileManagementService.ValidatePath()`.
