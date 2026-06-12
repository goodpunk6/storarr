# Stale PendingSymlink Auto-Revert

**Date:** 2026-06-12
**Status:** Draft

## Problem

When Storarr transitions an item from Mkv to PendingSymlink, it deletes the .mkv file and requests a replacement .strm/symlink from the download client. If the replacement never arrives (download fails, wrong file type delivered, client error), the item stays in PendingSymlink indefinitely. The reaper gives up after 2 hours but takes no action — it just logs a warning every minute forever.

Result: 82 items stuck in "pending" on the dashboard. The user's expectation: **if nothing is happening, pending should be 0.**

## Solution

Modify the existing stale PendingSymlink reaper in `TransitionService.CheckAndProcessTransitions()` to automatically revert items that have been pending for 2+ hours with no resolution. Reverted items enter a new `Error` state visible on the dashboard.

## Changes

### 1. Model: Add `Error` to `FileState` enum

**File:** `src/Storarr/Models/FileState.cs`

```csharp
public enum FileState
{
    Symlink,         // 0 - Streaming via symlink/strm
    Mkv,             // 1 - Local file
    Downloading,     // 2 - Transition in progress
    PendingSymlink,  // 3 - Waiting for symlink replacement
    Error            // 4 - Transition failed, needs attention
}
```

### 2. Model: Add error fields to `MediaItem`

**File:** `src/Storarr/Models/MediaItem.cs`

Add two properties:

```csharp
[MaxLength(500)]
public string? ErrorMessage { get; set; }

public DateTime? ErrorAt { get; set; }
```

- `ErrorMessage`: human-readable reason (e.g. "PendingSymlink timed out after 2h - no symlink appeared")
- `ErrorAt`: timestamp for when the error was set (for UI sorting/filtering)

### 3. DB migration: Use `AddColumnIfNotExists` pattern

**File:** `src/Storarr/Program.cs`

This project does NOT use EF Core migrations (no `Migrations/` folder exists). It uses a three-way strategy in `Program.cs`:
1. Fresh DB: `EnsureCreated()`
2. Legacy DB: manual `ALTER TABLE` via `AddColumnIfNotExists()` helper
3. Migrated DB: `Database.Migrate()`

For the legacy path, add after line 85 (alongside existing `AddColumnIfNotExists` calls):

```csharp
AddColumnIfNotExists(dbContext, "MediaItems", "ErrorMessage", "TEXT NULL");
AddColumnIfNotExists(dbContext, "MediaItems", "ErrorAt", "TEXT NULL");
```

For fresh databases, EF Core will include the new properties automatically via `EnsureCreated()`.

### 4. DTOs: Add error fields to DTOs

**File:** `src/Storarr/DTOs/MediaItemDto.cs`

Add to `MediaItemDto`:
```csharp
public string? ErrorMessage { get; set; }
public DateTime? ErrorAt { get; set; }
```

Add to `MediaItemListDto`:
```csharp
public string? ErrorMessage { get; set; }
```

**File:** `src/Storarr/DTOs/DashboardDto.cs`

Add to `DashboardDto`:
```csharp
public int ErrorCount { get; set; }
```

### 5. Reaper: Auto-revert at 2-hour mark

**File:** `src/Storarr/Services/TransitionService.cs`
**Location:** `CheckAndProcessTransitions()`, the `else if (pendingAge.TotalHours >= 2)` block (currently lines 639-643)

**Current behavior:**
```csharp
else if (pendingAge.TotalHours >= 2)
{
    _logger.LogWarning("... giving up on automatic retry");
    // Does nothing
}
```

**New behavior:**
```csharp
else if (pendingAge.TotalHours >= 2)
{
    _logger.LogWarning("[TransitionService] PendingSymlink item {Title} has been pending for {Hours:F1} hours - auto-reverting to Error state",
        pendingItem.Title, pendingAge.TotalHours);

    pendingItem.CurrentState = FileState.Error;
    pendingItem.ErrorMessage = $"PendingSymlink timed out after {pendingAge.TotalHours:F1} hours - no symlink appeared at {pendingItem.FilePath}";
    pendingItem.ErrorAt = DateTime.UtcNow;
    pendingItem.PendingSymlinkAt = null;
    pendingItem.StateChangedAt = DateTime.UtcNow;

    try
    {
        await _dbContext.SaveChangesAsync();
        await LogActivity(pendingItem.Id, "StaleReaper", FileState.PendingSymlink, FileState.Error,
            $"Auto-reverted: {pendingItem.ErrorMessage}");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[TransitionService] Failed to save error revert for {Title}", pendingItem.Title);
        _dbContext.Entry(pendingItem).State = EntityState.Unchanged;
    }
}
```

**Key points:**
- Only triggers once (at the 2-hour mark, then state changes away from PendingSymlink so it won't match the query again)
- `PendingSymlinkAt` is cleared to prevent re-processing
- Activity is logged for audit trail
- SaveChanges is called immediately per-item (same pattern as the existing symlink resolution code above it)
- Note: `SaveChangesAsync` is called twice (once explicitly, once inside `LogActivity`) — this matches the existing pattern in the symlink resolution block

### 6. Dashboard: Show Error count

**File:** `src/Storarr/Controllers/DashboardController.cs`

Add error count query (after line 44):
```csharp
var errorCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.Error);
```

Fix total items calculation (line 45) to include errors:
```csharp
var totalItems = symlinkCount + mkvCount + downloadingCount + pendingSymlinkCount + errorCount;
```

Add to dashboard DTO construction:
```csharp
ErrorCount = errorCount,
```

### 7. Frontend: Add Error state support

**File:** `src/Storarr.Frontend/src/stores/appStore.ts`

- Line 7: Add `'Error'` to the `currentState` union type:
  ```typescript
  currentState: 'Symlink' | 'Mkv' | 'Downloading' | 'PendingSymlink' | 'Error'
  ```
- Lines 169-172: Add `errorCount: number` to the dashboard data interface
- Lines 175-178: Add `errorCount: number` to the setDashboardData interface
- Lines 209-212: Add `errorCount: 0` to initial state

**File:** `src/Storarr.Frontend/src/pages/Dashboard.tsx`

- Add `errorCount` to the `fetchDashboardData` response destructuring (after line 33)
- Add a 5th stats card for "Errors" with error/warning styling, using `data?.errorCount ?? 0`

**File:** `src/Storarr.Frontend/src/pages/Media.tsx`

- Add `<option value="Error">Error</option>` to the state filter dropdown (around line 31-35)

**File:** `src/Storarr.Frontend/src/components/CatalogView.tsx`

- Add `Error` case to `getStateColor()` — use a distinct error color (e.g. red/destructive)
- Ensure Error-state items are batch-selectable for bulk actions

### 8. API: Allow resolving Error-state items

**File:** `src/Storarr/Controllers/MediaController.cs`

**Retry endpoint:** `POST /api/v1/media/{id}/retry-transition`

The original .mkv file was already deleted during the initial transition, so retrying `TransitionToSymlink` directly would fail trying to delete a nonexistent file. Instead:

1. Set state to `PendingSymlink`, clear `PendingSymlinkAt` to now (reset the 2-hour timer)
2. Clear `ErrorMessage` and `ErrorAt`
3. Call `TriggerSearch` on Sonarr/Radarr (skip file deletion phase)
4. The existing reaper will then handle the 30-min retry cycle

Only available for `FileState.Error` items. Returns 400 for other states.

**Clear all errors:** `POST /api/v1/media/clear-errors`

Accepts a JSON body with a `mode` field: `"retry"` or `"delete"`.
- `"retry"`: resets all Error items to PendingSymlink with fresh timers and triggers searches
- `"delete"`: removes all Error items from the database

Modeled after the existing `clear-ghost-pending` endpoint.

**ForceSymlink:** Add `FileState.Error` to the allowed states in the `ForceSymlink` endpoint (line 200), so users can manually retry from the item detail view.

**GetTransitionType:** Add explicit `FileState.Error => "Error"` arm to the switch (around line 660) so the UI shows "Error" as the transition type.

**clear-ghost-pending:** Update the query to also check `FileState.Error` items alongside `FileState.PendingSymlink` (so the existing cleanup endpoint also handles Error items with missing files).

### 9. Self-healing interaction with LibraryScanner

The `LibraryScanner` (every 15 minutes) does NOT skip Error-state items. If it finds a real file on disk for an Error item, it will change the state back to `Mkv` or `Symlink`. This is desirable behavior — self-healing. No code change needed, just acknowledging the interaction.

## Flow

```
Mkv --[TransitionToSymlink]--> PendingSymlink
                                    |
                                    |-- .strm appears --> Symlink (existing)
                                    |-- 30min: retry search (existing)
                                    |-- 2h: auto-revert --> Error (NEW)
                                                            |
                                                            |-- retry --> PendingSymlink (fresh timer)
                                                            |-- delete --> removed from DB
                                                            |-- file reappears --> LibraryScanner auto-heals
```

## Dashboard appearance

Before:
```
3790 symlink | 705 mkv | 0 downloading | 82 pending
```

After:
```
3790 symlink | 705 mkv | 0 downloading | 0 pending | 82 errors
```

The "errors" card is clickable and filters the media list to Error-state items. Pending is always 0 when nothing is actively transitioning.

## Files changed

| File | Change |
|------|--------|
| `Models/FileState.cs` | Add `Error = 4` |
| `Models/MediaItem.cs` | Add `ErrorMessage`, `ErrorAt` |
| `Program.cs` | `AddColumnIfNotExists` calls for new columns |
| `DTOs/MediaItemDto.cs` | Add error fields to `MediaItemDto` and `MediaItemListDto` |
| `DTOs/DashboardDto.cs` | Add `ErrorCount` |
| `Services/TransitionService.cs` | Auto-revert logic in reaper at 2-hour mark |
| `Controllers/DashboardController.cs` | Add `errorCount` query, fix `totalItems` |
| `Controllers/MediaController.cs` | `retry-transition`, `clear-errors` endpoints; update `ForceSymlink`, `GetTransitionType`, `clear-ghost-pending` |
| `Storarr.Frontend/src/stores/appStore.ts` | Add `'Error'` to union type, `errorCount` to dashboard state |
| `Storarr.Frontend/src/pages/Dashboard.tsx` | 5th stats card for errors |
| `Storarr.Frontend/src/pages/Media.tsx` | Error option in state filter dropdown |
| `Storarr.Frontend/src/components/CatalogView.tsx` | Error color in `getStateColor()`, batch eligibility |

## Out of scope

- Changing the `TransitionToSymlink` logic to prevent the issue at the source (separate concern)
- Changing how the download client delivers files
- Notification/alerting for errors (future enhancement)
