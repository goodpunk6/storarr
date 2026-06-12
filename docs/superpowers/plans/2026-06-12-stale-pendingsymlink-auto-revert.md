# Stale PendingSymlink Auto-Revert Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automatically revert PendingSymlink items that have been stuck for 2+ hours into a new Error state, so "pending" is always 0 when nothing is actively transitioning.

**Architecture:** Add an `Error` state to the existing FileState enum, modify the reaper in TransitionService to auto-revert stale items, add retry/delete API endpoints, and update the frontend dashboard and catalog to display errors.

**Tech Stack:** C# / .NET (backend), React + TypeScript + Zustand (frontend), SQLite (EF Core), Tailwind CSS

**Build/deploy:** `docker compose build && docker compose up -d` from `/home/dobbie/storarr/`

**Spec:** `docs/superpowers/specs/2026-06-12-stale-pendingsymlink-auto-revert-design.md`

---

## Task 1: Add Error to FileState enum

**Files:**
- Modify: `src/Storarr/Models/FileState.cs`

- [ ] **Step 1: Add Error to the enum**

In `src/Storarr/Models/FileState.cs`, add `Error` after `PendingSymlink`:

```csharp
namespace Storarr.Models
{
    public enum FileState
    {
        Symlink,        // 0 - Streaming via NZB-Dav
        Mkv,            // 1 - Local file
        Downloading,    // 2 - Transition in progress
        PendingSymlink, // 3 - Waiting for symlink replacement
        Error           // 4 - Transition failed, needs attention
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Storarr/Models/FileState.cs
git commit -m "feat: add Error state to FileState enum"
```

---

## Task 2: Add error fields to MediaItem model

**Files:**
- Modify: `src/Storarr/Models/MediaItem.cs`

- [ ] **Step 1: Add ErrorMessage and ErrorAt properties**

In `src/Storarr/Models/MediaItem.cs`, add after the `PendingSymlinkAt` property (after line 55):

```csharp
        // Error state tracking
        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime? ErrorAt { get; set; }
```

- [ ] **Step 2: Commit**

```bash
git add src/Storarr/Models/MediaItem.cs
git commit -m "feat: add ErrorMessage and ErrorAt fields to MediaItem"
```

---

## Task 3: Add DB column migration

**Files:**
- Modify: `src/Storarr/Program.cs`

- [ ] **Step 1: Add AddColumnIfNotExists calls**

In `src/Storarr/Program.cs`, add after line 85 (after the existing `AddColumnIfNotExists` calls for download client IDs, inside the `using` block):

```csharp
                    AddColumnIfNotExists(dbContext, "MediaItems", "ErrorMessage", "TEXT NULL");
                    AddColumnIfNotExists(dbContext, "MediaItems", "ErrorAt", "TEXT NULL");
```

Also add the same calls after line 83 (in the "Always ensure new columns exist" section), so they run regardless of migration path:

```csharp
                AddColumnIfNotExists(dbContext, "MediaItems", "ErrorMessage", "TEXT NULL");
                AddColumnIfNotExists(dbContext, "MediaItems", "ErrorAt", "TEXT NULL");
```

- [ ] **Step 2: Commit**

```bash
git add src/Storarr/Program.cs
git commit -m "feat: add DB migration for ErrorMessage and ErrorAt columns"
```

---

## Task 4: Update DTOs

**Files:**
- Modify: `src/Storarr/DTOs/MediaItemDto.cs`
- Modify: `src/Storarr/DTOs/DashboardDto.cs`

- [ ] **Step 1: Add error fields to MediaItemDto and MediaItemListDto**

In `src/Storarr/DTOs/MediaItemDto.cs`, add to `MediaItemDto` (after line 29, before the closing `}`):

```csharp
        public string? ErrorMessage { get; set; }
        public DateTime? ErrorAt { get; set; }
```

Add to `MediaItemListDto` (after line 59, before the closing `}`):

```csharp
        public string? ErrorMessage { get; set; }
```

- [ ] **Step 2: Add ErrorCount to DashboardDto**

In `src/Storarr/DTOs/DashboardDto.cs`, add after `PendingSymlinkCount` (line 8):

```csharp
        public int ErrorCount { get; set; }
```

- [ ] **Step 3: Commit**

```bash
git add src/Storarr/DTOs/MediaItemDto.cs src/Storarr/DTOs/DashboardDto.cs
git commit -m "feat: add error fields to DTOs and ErrorCount to DashboardDto"
```

---

## Task 5: Update DTO mapping in MediaController

**Files:**
- Modify: `src/Storarr/Controllers/MediaController.cs`

- [ ] **Step 1: Add error fields to MediaItemDto mapping**

In the `GetMedia(int id)` method, in the `new MediaItemDto` block (around line 145), add after `TransitionType`:

```csharp
                    ErrorMessage = item.ErrorMessage,
                    ErrorAt = item.ErrorAt,
```

- [ ] **Step 2: Add error field to MediaItemListDto mapping**

In the `GetMedia` list endpoint, in the `new MediaItemListDto` block (around line 93), add after `IsOverdue`:

```csharp
                        ErrorMessage = m.ErrorMessage,
```

- [ ] **Step 3: Add Error to GetTransitionType switch**

In `GetTransitionType` (around line 660), add a new arm before `_ => null`:

```csharp
                FileState.Error => "Error",
```

- [ ] **Step 4: Add Error to ForceSymlink allowed states**

In `ForceSymlink` (line 200), change the condition:

```csharp
            if (item.CurrentState != FileState.Mkv && item.CurrentState != FileState.Downloading && item.CurrentState != FileState.PendingSymlink && item.CurrentState != FileState.Error)
```

And update the error message (line 203):

```csharp
                return BadRequest("Can only force symlink for items in MKV, downloading, pending, or error state");
```

- [ ] **Step 5: Update clear-ghost-pending to also handle Error state**

In `ClearGhostPending` (line 513), change the query to include Error:

```csharp
            var pendingItems = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.PendingSymlink || m.CurrentState == FileState.Downloading || m.CurrentState == FileState.Error)
                .ToListAsync();
```

- [ ] **Step 6: Commit**

```bash
git add src/Storarr/Controllers/MediaController.cs
git commit -m "feat: map error fields in DTOs, add Error to ForceSymlink and clear-ghost-pending"
```

---

## Task 6: Add auto-revert logic to the reaper

**Files:**
- Modify: `src/Storarr/Services/TransitionService.cs`

- [ ] **Step 1: Replace the 2-hour "give up" block with auto-revert**

In `CheckAndProcessTransitions()`, replace lines 639-643 (the `else if (pendingAge.TotalHours >= 2)` block):

**Before:**
```csharp
                else if (pendingAge.TotalHours >= 2)
                {
                    _logger.LogWarning("[TransitionService] PendingSymlink item {Title} has been pending for {Hours:F1} hours - giving up on automatic retry",
                        pendingItem.Title, pendingAge.TotalHours);
                }
```

**After:**
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
                        _dbContext.Entry(pendingItem).State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
                    }
                }
```

- [ ] **Step 2: Commit**

```bash
git add src/Storarr/Services/TransitionService.cs
git commit -m "feat: auto-revert stale PendingSymlink items to Error state after 2 hours"
```

---

## Task 7: Update DashboardController

**Files:**
- Modify: `src/Storarr/Controllers/DashboardController.cs`

- [ ] **Step 1: Add error count query and fix totalItems**

In `GetDashboard()`, after line 44 (`var pendingSymlinkCount = ...`), add:

```csharp
                var errorCount = await _dbContext.MediaItems.AsNoTracking().CountAsync(m => m.CurrentState == FileState.Error);
```

Change line 45 to include errors:

```csharp
                var totalItems = symlinkCount + mkvCount + downloadingCount + pendingSymlinkCount + errorCount;
```

- [ ] **Step 2: Add ErrorCount to the dashboard DTO**

In the `new DashboardDto` block (around line 60), add after `PendingSymlinkCount`:

```csharp
                    ErrorCount = errorCount,
```

- [ ] **Step 3: Update log message**

Update the debug log (line 48-49) to include errors:

```csharp
                _logger.LogDebug("[DashboardController] State breakdown - Symlinks: {Symlink}, MKVs: {Mkv}, Downloading: {Downloading}, Pending: {Pending}, Errors: {Errors}",
                    symlinkCount, mkvCount, downloadingCount, pendingSymlinkCount, errorCount);
```

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Controllers/DashboardController.cs
git commit -m "feat: add ErrorCount to dashboard endpoint"
```

---

## Task 8: Add retry-transition and clear-errors API endpoints

**Files:**
- Modify: `src/Storarr/Controllers/MediaController.cs`

- [ ] **Step 1: Add retry-transition endpoint**

Add this method to `MediaController`, after the `ForceSymlink` method (after line 217):

```csharp
        [HttpPost("{id}/retry-transition")]
        public async Task<ActionResult> RetryTransition(int id)
        {
            _logger.LogDebug("[MediaController] RetryTransition called for ID: {Id}", id);

            var item = await _dbContext.MediaItems.FindAsync(id);
            if (item == null)
            {
                return NotFound();
            }

            if (item.CurrentState != FileState.Error)
            {
                return BadRequest("Can only retry transition for items in Error state");
            }

            try
            {
                _logger.LogInformation("[MediaController] Retrying transition for {Title} - resetting to PendingSymlink", item.Title);

                var previousState = item.CurrentState;
                item.CurrentState = FileState.PendingSymlink;
                item.PendingSymlinkAt = DateTime.UtcNow;
                item.StateChangedAt = DateTime.UtcNow;
                item.ErrorMessage = null;
                item.ErrorAt = null;

                await _dbContext.SaveChangesAsync();

                await LogActivity(item.Id, "RetryTransition", previousState, FileState.PendingSymlink,
                    "Manually retried from Error state");

                // Trigger search in the arr to try to get a new download
                try
                {
                    if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                    {
                        await _radarrService.TriggerSearch(item.RadarrId.Value);
                    }
                    else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                    {
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[MediaController] TriggerSearch failed during retry for {Title}", item.Title);
                }

                return Ok(new { message = "Transition retried", item.Id, item.CurrentState });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MediaController] Error in RetryTransition for {Title}", item.Title);
                return StatusCode(500, new { error = ex.Message });
            }
        }
```

Also add this helper method (the controller already has `_sonarrService`, `_radarrService`, and `_dbContext`. Named `LogActivity` — no collision since the controller doesn't have one; `TransitionService` has its own `LogActivity` but that's a different class):

```csharp
        private async Task LogActivity(int mediaItemId, string action, FileState fromState, FileState toState, string? details = null)
        {
            _dbContext.ActivityLogs.Add(new ActivityLog
            {
                MediaItemId = mediaItemId,
                Action = action,
                FromState = fromState.ToString(),
                ToState = toState.ToString(),
                Details = details,
                Timestamp = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }
```

- [ ] **Step 2: Add clear-errors endpoint**

Add after the `ClearGhostPending` method (after line 571):

```csharp
        [HttpPost("clear-errors")]
        public async Task<ActionResult> ClearErrors([FromBody] ClearErrorsDto dto)
        {
            _logger.LogDebug("[MediaController] ClearErrors called with mode: {Mode}", dto.Mode);

            var errorItems = await _dbContext.MediaItems
                .Where(m => m.CurrentState == FileState.Error)
                .ToListAsync();

            _logger.LogDebug("[MediaController] Found {Count} Error items", errorItems.Count);

            if (dto.Mode == "delete")
            {
                _dbContext.MediaItems.RemoveRange(errorItems);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[MediaController] Deleted {Count} Error items", errorItems.Count);
                return Ok(new { mode = "delete", cleared = errorItems.Count });
            }
            else if (dto.Mode == "retry")
            {
                int retried = 0;
                foreach (var item in errorItems)
                {
                    item.CurrentState = FileState.PendingSymlink;
                    item.PendingSymlinkAt = DateTime.UtcNow;
                    item.StateChangedAt = DateTime.UtcNow;
                    item.ErrorMessage = null;
                    item.ErrorAt = null;

                    _dbContext.ActivityLogs.Add(new ActivityLog
                    {
                        MediaItemId = item.Id,
                        Action = "ClearErrors",
                        FromState = FileState.Error.ToString(),
                        ToState = FileState.PendingSymlink.ToString(),
                        Details = "Bulk retry from Error state",
                        Timestamp = DateTime.UtcNow
                    });

                    retried++;

                    try
                    {
                        if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                            await _radarrService.TriggerSearch(item.RadarrId.Value);
                        else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                            await _sonarrService.TriggerSearch(item.SonarrId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[MediaController] TriggerSearch failed for {Title} during clear-errors", item.Title);
                    }
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("[MediaController] Retried {Count} Error items", retried);
                return Ok(new { mode = "retry", cleared = retried });
            }

            return BadRequest("Invalid mode. Use 'retry' or 'delete'.");
        }
```

- [ ] **Step 3: Add ClearErrorsDto**

In `src/Storarr/DTOs/MediaItemDto.cs`, add at the end of the file:

```csharp
    public class ClearErrorsDto
    {
        public string Mode { get; set; } = "retry"; // "retry" or "delete"
    }
```

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Controllers/MediaController.cs src/Storarr/DTOs/MediaItemDto.cs
git commit -m "feat: add retry-transition and clear-errors API endpoints"
```

---

## Task 9: Update frontend TypeScript types

**Files:**
- Modify: `src/Storarr.Frontend/src/stores/appStore.ts`

- [ ] **Step 1: Add Error to currentState union**

In `src/Storarr.Frontend/src/stores/appStore.ts`, line 7, change:

```typescript
  currentState: 'Symlink' | 'Mkv' | 'Downloading' | 'PendingSymlink' | 'Error'
```

- [ ] **Step 2: Add errorCount to dashboard interfaces**

Find the interface with `symlinkCount`, `mkvCount`, `downloadingCount`, `pendingSymlinkCount` (around lines 169-172) and add:

```typescript
  errorCount: number
```

Do the same for the `setDashboardData` interface (around lines 175-178).

- [ ] **Step 3: Add errorCount to initial state**

Find the initial state with `symlinkCount: 0, mkvCount: 0` etc. (around lines 209-212) and add:

```typescript
  errorCount: 0,
```

- [ ] **Step 4: Commit**

```bash
git add src/Storarr.Frontend/src/stores/appStore.ts
git commit -m "feat: add Error to frontend TypeScript types and store"
```

---

## Task 10: Update Dashboard page

**Files:**
- Modify: `src/Storarr.Frontend/src/pages/Dashboard.tsx`

- [ ] **Step 1: Add errorCount to DashboardData interface**

In the `DashboardData` interface (line 9-17), add after `pendingSymlinkCount`:

```typescript
  errorCount: number
```

- [ ] **Step 2: Add errorCount to setDashboardData call**

In the `setDashboardData` call (around line 33), add after `pendingSymlinkCount`:

```typescript
          errorCount: response.data.errorCount,
```

- [ ] **Step 3: Add AlertTriangle import**

Change line 3 to include `AlertTriangle`:

```typescript
import { Link2, HardDrive, Download, Clock, AlertTriangle } from 'lucide-react'
```

- [ ] **Step 4: Change grid to 5 columns and add Error card**

Change line 67 from `lg:grid-cols-4` to `lg:grid-cols-5`, and add after the PENDING StatCard (after line 91):

```tsx
        <StatCard
          title="ERRORS"
          value={data?.errorCount ?? 0}
          icon={<AlertTriangle size={48} />}
          color="bg-arr-card"
        />
```

- [ ] **Step 5: Commit**

```bash
git add src/Storarr.Frontend/src/pages/Dashboard.tsx
git commit -m "feat: add Error count card to dashboard"
```

---

## Task 11: Add frontend API client functions

**Files:**
- Modify: `src/Storarr.Frontend/src/api/client.ts`

- [ ] **Step 1: Add retry-transition and clear-errors API functions**

In `src/Storarr.Frontend/src/api/client.ts`, add after the `clearGhostPending` function (after line 55):

```typescript
export const retryTransition = (id: number) => api.post(`/media/${id}/retry-transition`)
export const clearErrors = (mode: 'retry' | 'delete') => api.post('/media/clear-errors', { mode })
```

- [ ] **Step 2: Commit**

```bash
git add src/Storarr.Frontend/src/api/client.ts
git commit -m "feat: add retryTransition and clearErrors API client functions"
```

---

## Task 13: Update Media page filter and CatalogView

**Files:**
- Modify: `src/Storarr.Frontend/src/pages/Media.tsx`
- Modify: `src/Storarr.Frontend/src/components/CatalogView.tsx`

- [ ] **Step 1: Add Error option to state filter**

In `src/Storarr.Frontend/src/pages/Media.tsx`, add after the PendingSymlink option (after line 35):

```tsx
          <option value="Error">Error</option>
```

- [ ] **Step 2: Add Error case to getStateColor**

In `src/Storarr.Frontend/src/components/CatalogView.tsx`, in the `getStateColor` function (line 22), add before the `default` case:

```typescript
    case 'error':
      return 'bg-red-500/20 text-red-400'
```

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/pages/Media.tsx src/Storarr.Frontend/src/components/CatalogView.tsx
git commit -m "feat: add Error state to filter dropdown and catalog styling"
```

---

## Task 14: Build and deploy

**Files:** None (build only)

- [ ] **Step 1: Build the Docker image**

```bash
cd /home/dobbie/storarr && docker compose build
```

Expected: Build succeeds with no errors.

- [ ] **Step 2: Deploy**

```bash
cd /home/dobbie/storarr && docker compose up -d
```

- [ ] **Step 3: Wait for container to be healthy, then verify**

```bash
# Wait for container to be running and responsive
until curl -s -o /dev/null -w "%{http_code}" http://localhost:8687/api/v1/dashboard | grep -q "200"; do sleep 2; done

# Check the dashboard API returns errorCount
# Check container is running
docker ps --filter name=storarr --format '{{.Status}}'

# Check the dashboard API returns errorCount
curl -s http://localhost:8687/api/v1/dashboard | python3 -c "import json,sys; d=json.load(sys.stdin); print(f'Total: {d[\"totalItems\"]}, Symlinks: {d[\"symlinkCount\"]}, MKVs: {d[\"mkvCount\"]}, Pending: {d[\"pendingSymlinkCount\"]}, Errors: {d[\"errorCount\"]}')"
```

Expected: `Errors: 82` (the current stale items will be auto-reverted by the reaper within 2 hours, or immediately if they're already past 2 hours).

- [ ] **Step 4: Verify the reaper is working**

Check activity logs for auto-reverts (the reaper runs every 1 minute, stale items will be reverted on first cycle):

```bash
curl -s "http://localhost:8687/api/v1/activity?pageSize=10" | python3 -c "
import json, sys
data = json.load(sys.stdin)
for a in data[:5]:
    print(f'{a.get(\"timestamp\",\"?\")}: {a.get(\"action\",\"?\")} | {a.get(\"fromState\",\"?\")} -> {a.get(\"toState\",\"?\")} | {a.get(\"details\",\"\")[:80]}')
"
```

Expected: Recent entries with `action=StaleReaper`, `fromState=PendingSymlink`, `toState=Error`.

- [ ] **Step 5: Verify dashboard shows 0 pending**

```bash
curl -s http://localhost:8687/api/v1/dashboard | python3 -c "import json,sys; d=json.load(sys.stdin); print(f'Pending: {d[\"pendingSymlinkCount\"]}, Errors: {d[\"errorCount\"]}')"
```

Expected: `Pending: 0, Errors: 82`

---

## Task 15: Final commit with all changes

- [ ] **Step 1: Verify everything is committed**

```bash
cd /home/dobbie/storarr && git status
```

Expected: clean working tree.

- [ ] **Step 2: Push if desired**

Ask user if they want to push to remote.
