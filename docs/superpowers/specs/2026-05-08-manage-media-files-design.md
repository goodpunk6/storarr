# Manage Media Files Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a "Manage" button to the media catalog that lets users delete files, remove shows from Sonarr/Radarr, and toggle monitoring state — all via the Arr APIs, never via OS-level file deletion.

**Architecture:** A batch API endpoint accepts a set of item IDs plus chosen actions. The backend calls Sonarr/Radarr APIs in a specific order, then updates or removes Storarr DB records. A React modal component presents four combinable action checkboxes with conflict validation.

**Tech Stack:** .NET 6 backend (existing patterns), React + TypeScript frontend with Zustand state management and Tailwind CSS.

---

## User Actions

Four checkboxes, all combinable with one conflict rule:

| # | Action | API Used | Effect |
|---|--------|----------|--------|
| 1 | Delete file(s) from disk | Sonarr `DELETE /api/v3/episodefile/{id}`, Radarr `DELETE /api/v3/moviefile/{id}` | Removes media file. Sonarr/Radarr handle actual filesystem deletion. |
| 2 | Remove from Sonarr/Radarr | Sonarr `DELETE /api/v3/series/{id}`, Radarr `DELETE /api/v3/movie/{id}` | Removes series/movie from the arr service entirely. |
| 3 | Unmonitor | Sonarr `PUT /api/v3/series/{id}` (monitored=false), Radarr `PUT /api/v3/movie/{id}` (monitored=false) | Stops arr from auto-grabbing new releases. |
| 4 | Re-monitor | Same API, monitored=true | Re-enables auto-grabbing. |

**Conflict rule:** Checking (2) "Remove from Sonarr/Radarr" disables checkboxes (3) and (4) — you can't monitor something you're removing.

**Storarr DB behavior:**
- If files are deleted AND the show is removed from arr → remove MediaItem from Storarr DB
- If the show is removed from arr but files are NOT deleted → keep MediaItem in Storarr DB (files still exist on disk). The item remains visible in the catalog. Future conversion attempts would re-add to arr (future feature).
- If only files are deleted (show stays in arr) → remove MediaItem from Storarr DB (arr will manage re-downloads)

## Data Model Changes

### Series/Movie models — add `Monitored` property

The existing `Series` (ISonarrService.cs:53-62) and `Movie` (IRadarrService.cs:24-33) model classes lack a `Monitored` field. Add:

```csharp
// In Series class
public bool Monitored { get; set; }

// In Movie class
public bool Monitored { get; set; }
```

### Request/Response DTOs

**`ManageMediaRequestDto`** (new file: `DTOs/ManageMediaRequestDto.cs`):

```csharp
public class ManageMediaRequestDto
{
    public List<int> ItemIds { get; set; } = new();
    public bool DeleteFiles { get; set; }
    public bool RemoveFromArr { get; set; }
    public bool Unmonitor { get; set; }
    public bool ReMonitor { get; set; }
}
```

**`ManageMediaResultDto`** (new file: `DTOs/ManageMediaResultDto.cs`):

```csharp
public class ManageMediaResultDto
{
    public List<ManageMediaItemResult> Results { get; set; } = new();
}

public class ManageMediaItemResult
{
    public int ItemId { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";  // "Series", "Movie", "Anime"
    public bool Success { get; set; }
    public List<string> Actions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
```

## API Design

### `POST /api/v1/media/manage`

**Validation:**
- At least one action must be true
- `removeFromArr` cannot be combined with `unmonitor` or `reMonitor`
- `unmonitor` and `reMonitor` cannot both be true
- All item IDs must exist in the DB

**Execution order per item:**

Each item is processed independently. The `MediaItem.Type` field determines which service to call:
- `MediaType.Series` or `MediaType.Anime` → `ISonarrService` (using `SonarrId`, `SonarrFileId`)
- `MediaType.Movie` → `IRadarrService` (using `RadarrId`, `RadarrFileId`)

**Items without an Arr ID** (e.g., `RadarrId = null` for a movie) are skipped with an error in the results: "Item has no associated Sonarr/Radarr ID."

**Steps per item:**
1. **`deleteFiles`** → Look up `SonarrFileId` or `RadarrFileId` on the `MediaItem`. Call `DeleteEpisodeFile(sonarrFileId)` or `DeleteMovieFile(radarrFileId)`. If the file ID is null, fall back to `DeleteEpisodeFileByPath(sonarrId, filePath)` or `DeleteMovieFileByPath(radarrId, filePath)`.
2. **`unmonitor` or `reMonitor`** → Call `SetSeriesMonitorState(sonarrId, false/true)` or `SetMovieMonitorState(radarrId, false/true)`. These methods GET the full series/movie object, set `Monitored`, then PUT it back.
3. **`removeFromArr`** → When combined with `deleteFiles=true`, use `deleteFiles=true` on the series/movie DELETE call. When `deleteFiles=false`, use `deleteFiles=false`. Call `DeleteSeries(sonarrId, deleteFiles)` or `DeleteMovie(radarrId, deleteFiles)`.
4. **Storarr DB update** → If files were deleted AND show was NOT removed-with-keep-files, remove the `MediaItem`. If show removed but files kept, clear `SonarrId`/`RadarrId` on the `MediaItem` but keep the row. If only files deleted, remove the `MediaItem` (arr will manage re-downloads).
5. **Activity log** → Create an `ActivityLog` entry per action: `"Manage_DeleteFile"`, `"Manage_RemoveFromArr"`, `"Manage_Unmonitor"`, `"Manage_ReMonitor"`.

## New Service Methods

### ISonarrService additions

```csharp
Task DeleteSeries(int seriesId, bool deleteFiles = false);
Task SetSeriesMonitorState(int seriesId, bool monitored);
```

### IRadarrService additions

```csharp
Task DeleteMovie(int movieId, bool deleteFiles = false);
Task SetMovieMonitorState(int movieId, bool monitored);
```

### Implementation details

**`SetSeriesMonitorState` / `SetMovieMonitorState`:**
1. `GET /api/v3/series/{id}` (or `/api/v3/movie/{id}`) — fetch full object
2. Set `monitored` field on the response object
3. `PUT /api/v3/series/{id}` (or `/api/v3/movie/{id}`) with the modified object

**`DeleteSeries` / `DeleteMovie`:**
- `DELETE /api/v3/series/{id}?deleteFiles={deleteFiles}`
- `DELETE /api/v3/movie/{id}?deleteFiles={deleteFiles}`

## Frontend Design

### Manage Button

A "Manage" button (Settings/wrench icon) added to the existing `BulkActionBar`. Extend `BulkActionBarProps` with an `onManage?: () => void` prop. Always visible when items are selected, alongside the existing Convert buttons.

### ManageModal Component

New component: `src/Storarr.Frontend/src/components/ManageModal.tsx`

**Layout:**
- Header: "Manage {N} Selected Items" with item count
- Selected items summary (scrollable list of titles, max 5 shown with "+N more")
- Action section with four checkboxes:
  ```
  [ ] Delete file(s) from disk
  [ ] Remove from Sonarr/Radarr
  [ ] Set to Unmonitored
  [ ] Set to Monitored
  ```
- When "Remove from Sonarr/Radarr" is checked → "Set to Unmonitored" and "Set to Monitored" are disabled (grayed out, tooltip: "Not available when removing from Sonarr/Radarr")
- "Set to Unmonitored" and "Set to Monitored" are mutually exclusive (checking one unchecks the other)
- Dynamic confirmation button text reflecting chosen actions, e.g.:
  - "Delete 3 files"
  - "Delete 3 files and remove 2 series from Sonarr"
  - "Set 5 items to monitored"
  - "Delete 2 files and unmonitor 2 movies"
- Cancel button
- After confirmation: progress indicator, then results summary (successes and failures)
- After completion: refresh catalog, clear selection

### State Integration

- New `manageMedia(itemIds, actions)` function in the API client (`/src/Storarr.Frontend/src/api/client.ts`)
- Zustand store: add `manageModalOpen: boolean` and `manageResult: ManageMediaResultDto | null` to app store
- Toast notifications for completion

## Error Handling

- Per-item errors are collected, not thrown — the batch continues even if some items fail
- Each item's result includes success/failure and specific error messages
- If file deletion fails but arr removal succeeds, the item is still removed from arr (best-effort)
- Monitor toggle and arr removal are idempotent: unmonitoring an already-unmonitored series succeeds without error
- Results are shown in the modal so the user can see exactly what happened

## Future Scope (Not Implemented Now)

- Jellyseerr/Overseerr integration: when removing from arr, also mark as un-requested in Jellyseerr
- Re-add to arr: when converting items that were removed from arr, automatically re-add them
- Per-episode monitor toggling for series (currently series-level only)
- Secondary confirmation (e.g., typing title) for destructive combinations
