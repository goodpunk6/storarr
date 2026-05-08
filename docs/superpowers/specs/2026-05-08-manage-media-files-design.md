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

## API Design

### `POST /api/v1/media/manage`

**Request:**

```json
{
  "itemIds": [1199, 1488],
  "deleteFiles": true,
  "removeFromArr": false,
  "unmonitor": false,
  "reMonitor": false
}
```

**Validation:**
- At least one action must be true
- `removeFromArr` cannot be combined with `unmonitor` or `reMonitor`
- `unmonitor` and `reMonitor` cannot both be true
- All item IDs must exist in the DB

**Response (200):**

```json
{
  "results": [
    {
      "itemId": 1199,
      "title": "Wicked",
      "success": true,
      "actions": ["deleteFiles", "unmonitor"],
      "errors": []
    },
    {
      "itemId": 1488,
      "title": "Wicked: For Good",
      "success": false,
      "actions": [],
      "errors": ["Failed to delete movie file: 404 Not Found"]
    }
  ]
}
```

**Execution order per item:**
1. `deleteFiles` → call arr file deletion API
2. `unmonitor` or `reMonitor` → call arr series/movie update API
3. `removeFromArr` → call arr series/movie deletion API
4. Update Storarr DB: remove MediaItem if (files deleted AND NOT kept due to remove-from-arr-without-file-delete)

## New Service Methods

### ISonarrService additions

```csharp
Task DeleteSeries(int seriesId);
Task SetSeriesMonitorState(int seriesId, bool monitored);
```

### IRadarrService additions

```csharp
Task DeleteMovie(int movieId);
Task SetMovieMonitorState(int movieId, bool monitored);
```

### Sonarr API calls

- `DELETE /api/v3/series/{id}?deleteFiles=false` — remove series without deleting files
- `PUT /api/v3/series/{id}` with `{"monitored": false}` — unmonitor (must send full series object or partial update)

### Radarr API calls

- `DELETE /api/v3/movie/{id}?deleteFiles=false` — remove movie without deleting files
- `PUT /api/v3/movie/{id}` with `{"monitored": false}` — unmonitor (must send full movie object or partial update)

## Frontend Design

### Manage Button

A "Manage" button (gear/wrench icon) added to the existing `BulkActionBar`. Always visible when items are selected, alongside the existing Convert buttons.

### ManageModal Component

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
- Zustand store updates for modal open/close state and results
- Toast notifications for completion

## Error Handling

- Per-item errors are collected, not thrown — the batch continues even if some items fail
- Each item's result includes success/failure and specific error messages
- If file deletion fails but arr removal succeeds, the item is still removed from arr (best-effort)
- Results are shown in the modal so the user can see exactly what happened

## Future Scope (Not Implemented Now)

- Jellyseerr/Overseerr integration: when removing from arr, also mark as un-requested in Jellyseerr
- Re-add to arr: when converting items that were removed from arr, automatically re-add them
- Per-episode monitor toggling for series (currently series-level only)
