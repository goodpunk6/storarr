# Manage Media Files Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Manage" button and modal to the media catalog that lets users batch-delete files, remove series/movies from Sonarr/Radarr, and toggle monitoring state — all via Arr APIs.

**Architecture:** New backend service methods for series/movie deletion and monitor toggling. New batch API endpoint in MediaController. New ManageModal React component integrated into the existing BulkActionBar. No tests exist in this project — we verify via `dotnet build` and manual testing.

**Tech Stack:** .NET 6 (C#), React + TypeScript, Zustand, Tailwind CSS, Axios

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/Storarr/Services/ISonarrService.cs` | Add `DeleteSeries`, `SetSeriesMonitorState` methods + `Monitored` to `Series` model |
| Modify | `src/Storarr/Services/IRadarrService.cs` | Add `DeleteMovie`, `SetMovieMonitorState` methods + `Monitored` to `Movie` model |
| Modify | `src/Storarr/Services/SonarrService.cs` | Implement `DeleteSeries`, `SetSeriesMonitorState` |
| Modify | `src/Storarr/Services/RadarrService.cs` | Implement `DeleteMovie`, `SetMovieMonitorState` |
| Create | `src/Storarr/DTOs/ManageMediaDto.cs` | Request/response DTOs |
| Modify | `src/Storarr/Controllers/MediaController.cs` | Add `POST /api/v1/media/manage` endpoint + inject ISonarrService/IRadarrService |
| Modify | `src/Storarr.Frontend/src/api/client.ts` | Add `manageMedia()` API function |
| Modify | `src/Storarr.Frontend/src/components/BulkActionBar.tsx` | Add Manage button + `onManage` prop |
| Create | `src/Storarr.Frontend/src/components/ManageModal.tsx` | Modal with 4 action checkboxes, conflict validation, progress, results |
| Modify | `src/Storarr.Frontend/src/components/CatalogView.tsx` | Wire ManageModal into CatalogView, pass selected items to modal |

---

### Task 1: Add DeleteSeries and SetSeriesMonitorState to Sonarr Service

**Files:**
- Modify: `src/Storarr/Services/ISonarrService.cs` (add methods to interface, add `Monitored` to `Series` class at line 53-62)
- Modify: `src/Storarr/Services/SonarrService.cs` (implement new methods after `DeleteEpisodeFileByPath` at line 330)

- [ ] **Step 1: Update ISonarrService interface and Series model**

In `ISonarrService.cs`, add `Monitored` property to the `Series` class (lines 53-62). **Only add the `Monitored` line — do not replace existing nullable annotations on other properties:**

```csharp
public bool Monitored { get; set; }   // ADD THIS LINE to the existing Series class
```

Add two new method signatures to `ISonarrService` interface (after `GetBlocklistedTitles`):

```csharp
Task DeleteSeries(int seriesId, bool deleteFiles = false);
Task SetSeriesMonitorState(int seriesId, bool monitored);
```

- [ ] **Step 2: Implement DeleteSeries and SetSeriesMonitorState in SonarrService**

In `SonarrService.cs`, add after the `DeleteEpisodeFileByPath` method (after line 330):

```csharp
public async Task DeleteSeries(int seriesId, bool deleteFiles = false)
{
    var request = await CreateRequest(HttpMethod.Delete, $"api/v3/series/{seriesId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");
    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();
}

public async Task SetSeriesMonitorState(int seriesId, bool monitored)
{
    // GET full series, modify monitored field, PUT back
    var getRequest = await CreateRequest(HttpMethod.Get, $"api/v3/series/{seriesId}");
    var getResponse = await _httpClient.SendAsync(getRequest);
    getResponse.EnsureSuccessStatusCode();
    var content = await getResponse.Content.ReadAsStringAsync();
    var series = JsonSerializer.Deserialize<Series>(content, _jsonOptions);
    if (series == null) throw new InvalidOperationException($"Failed to deserialize series {seriesId}");

    series.Monitored = monitored;
    var putRequest = await CreateRequest(HttpMethod.Put, $"api/v3/series/{seriesId}");
    putRequest.Content = new StringContent(JsonSerializer.Serialize(series, _jsonOptions), Encoding.UTF8, "application/json");
    var putResponse = await _httpClient.SendAsync(putRequest);
    putResponse.EnsureSuccessStatusCode();
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build /home/dobbie/storarr/Storarr.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Services/ISonarrService.cs src/Storarr/Services/SonarrService.cs
git commit -m "feat: add DeleteSeries and SetSeriesMonitorState to Sonarr service"
```

---

### Task 2: Add DeleteMovie and SetMovieMonitorState to Radarr Service

**Files:**
- Modify: `src/Storarr/Services/IRadarrService.cs` (add methods to interface, add `Monitored` to `Movie` class at line 24-33)
- Modify: `src/Storarr/Services/RadarrService.cs` (implement new methods after `DeleteMovieFileByPath` at line 294)

- [ ] **Step 1: Update IRadarrService interface and Movie model**

In `IRadarrService.cs`, add `Monitored` property to the `Movie` class (lines 24-33). **Only add the `Monitored` line — do not replace existing nullable annotations:**

```csharp
public bool Monitored { get; set; }   // ADD THIS LINE to the existing Movie class
```

Add two new method signatures to `IRadarrService` interface (after `GetBlocklistedTitles`):

```csharp
Task DeleteMovie(int movieId, bool deleteFiles = false);
Task SetMovieMonitorState(int movieId, bool monitored);
```

- [ ] **Step 2: Implement DeleteMovie and SetMovieMonitorState in RadarrService**

In `RadarrService.cs`, add after the `DeleteMovieFileByPath` method (after line 294):

```csharp
public async Task DeleteMovie(int movieId, bool deleteFiles = false)
{
    var request = await CreateRequest(HttpMethod.Delete, $"api/v3/movie/{movieId}?deleteFiles={deleteFiles.ToString().ToLowerInvariant()}");
    var response = await _httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();
}

public async Task SetMovieMonitorState(int movieId, bool monitored)
{
    var getRequest = await CreateRequest(HttpMethod.Get, $"api/v3/movie/{movieId}");
    var getResponse = await _httpClient.SendAsync(getRequest);
    getResponse.EnsureSuccessStatusCode();
    var content = await getResponse.Content.ReadAsStringAsync();
    var movie = JsonSerializer.Deserialize<Movie>(content, _jsonOptions);
    if (movie == null) throw new InvalidOperationException($"Failed to deserialize movie {movieId}");

    movie.Monitored = monitored;
    var putRequest = await CreateRequest(HttpMethod.Put, $"api/v3/movie/{movieId}");
    putRequest.Content = new StringContent(JsonSerializer.Serialize(movie, _jsonOptions), Encoding.UTF8, "application/json");
    var putResponse = await _httpClient.SendAsync(putRequest);
    putResponse.EnsureSuccessStatusCode();
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build /home/dobbie/storarr/Storarr.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Services/IRadarrService.cs src/Storarr/Services/RadarrService.cs
git commit -m "feat: add DeleteMovie and SetMovieMonitorState to Radarr service"
```

---

### Task 3: Create ManageMedia DTOs

**Files:**
- Create: `src/Storarr/DTOs/ManageMediaDto.cs`

- [ ] **Step 1: Create the DTO file**

Create `src/Storarr/DTOs/ManageMediaDto.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Storarr.DTOs
{
    public class ManageMediaRequestDto
    {
        public List<int> ItemIds { get; set; } = new();
        public bool DeleteFiles { get; set; }
        public bool RemoveFromArr { get; set; }
        public bool Unmonitor { get; set; }
        public bool ReMonitor { get; set; }
    }

    public class ManageMediaResultDto
    {
        public List<ManageMediaItemResult> Results { get; set; } = new();
    }

    public class ManageMediaItemResult
    {
        public int ItemId { get; set; }
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Success { get; set; }
        public List<string> Actions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build /home/dobbie/storarr/Storarr.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr/DTOs/ManageMediaDto.cs
git commit -m "feat: add ManageMedia request/response DTOs"
```

---

### Task 4: Add Manage Endpoint to MediaController

**Files:**
- Modify: `src/Storarr/Controllers/MediaController.cs` (add ISonarrService/IRadarrService injection, add ManageMedia endpoint)

- [ ] **Step 1: Add service injections to MediaController constructor**

In `MediaController.cs`, add `ISonarrService` and `IRadarrService` to the constructor. Current constructor (lines 24-34):

```csharp
private readonly StorarrDbContext _dbContext;
private readonly ITransitionService _transitionService;
private readonly IFileManagementService _fileService;
private readonly ILogger<MediaController> _logger;

public MediaController(StorarrDbContext dbContext, ITransitionService transitionService, IFileManagementService fileService, ILogger<MediaController> logger)
{
    _dbContext = dbContext;
    _transitionService = transitionService;
    _fileService = fileService;
    _logger = logger;
}
```

Change to:

```csharp
private readonly StorarrDbContext _dbContext;
private readonly ITransitionService _transitionService;
private readonly IFileManagementService _fileService;
private readonly ISonarrService _sonarrService;
private readonly IRadarrService _radarrService;
private readonly ILogger<MediaController> _logger;

public MediaController(StorarrDbContext dbContext, ITransitionService transitionService, IFileManagementService fileService, ISonarrService sonarrService, IRadarrService radarrService, ILogger<MediaController> logger)
{
    _dbContext = dbContext;
    _transitionService = transitionService;
    _fileService = fileService;
    _sonarrService = sonarrService;
    _radarrService = radarrService;
    _logger = logger;
}
```

Add the `using Storarr.Services;` import if not already present.

- [ ] **Step 2: Add the ManageMedia endpoint**

Add this endpoint after the existing `DeleteMedia` endpoint (after line 280). Place it before `ClearGhostPending`:

```csharp
[HttpPost("manage")]
public async Task<ActionResult<ManageMediaResultDto>> ManageMedia([FromBody] ManageMediaRequestDto request)
{
    // Validation
    if (request.ItemIds == null || request.ItemIds.Count == 0)
        return BadRequest("No items selected");

    if (!request.DeleteFiles && !request.RemoveFromArr && !request.Unmonitor && !request.ReMonitor)
        return BadRequest("At least one action must be selected");

    if (request.RemoveFromArr && (request.Unmonitor || request.ReMonitor))
        return BadRequest("Cannot combine 'remove from Sonarr/Radarr' with monitor state changes");

    if (request.Unmonitor && request.ReMonitor)
        return BadRequest("Cannot both unmonitor and re-monitor");

    var results = new List<ManageMediaItemResult>();

    foreach (var itemId in request.ItemIds)
    {
        var result = new ManageMediaItemResult { ItemId = itemId };
        var item = await _dbContext.MediaItems.FindAsync(itemId);

        if (item == null)
        {
            result.Errors.Add("Item not found in Storarr database");
            results.Add(result);
            continue;
        }

        result.Title = item.Title;
        result.Type = item.Type.ToString();

        try
        {
            var isSonarr = item.Type == MediaType.Series || item.Type == MediaType.Anime;

            // Step 1: Delete files
            if (request.DeleteFiles)
            {
                try
                {
                    if (isSonarr)
                    {
                        if (item.SonarrId.HasValue)
                        {
                            if (item.SonarrFileId.HasValue)
                                await _sonarrService.DeleteEpisodeFile(item.SonarrFileId.Value);
                            else
                                await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath);
                        }
                        else
                        {
                            result.Errors.Add("No Sonarr ID — cannot delete file");
                        }
                    }
                    else
                    {
                        if (item.RadarrId.HasValue)
                        {
                            if (item.RadarrFileId.HasValue)
                                await _radarrService.DeleteMovieFile(item.RadarrFileId.Value);
                            else
                                await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath);
                        }
                        else
                        {
                            result.Errors.Add("No Radarr ID — cannot delete file");
                        }
                    }

                    if (!result.Errors.Any())
                    {
                        result.Actions.Add("deleteFiles");
                        _logger.LogInformation("[MediaController] Deleted file for {Title} (ID={Id})", item.Title, item.Id);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to delete file: {ex.Message}");
                    _logger.LogWarning(ex, "[MediaController] Failed to delete file for {Title}", item.Title);
                }
            }

            // Step 2: Monitor state
            if (request.Unmonitor || request.ReMonitor)
            {
                var monitored = request.ReMonitor;
                try
                {
                    if (isSonarr && item.SonarrId.HasValue)
                    {
                        await _sonarrService.SetSeriesMonitorState(item.SonarrId.Value, monitored);
                        result.Actions.Add(monitored ? "reMonitor" : "unmonitor");
                    }
                    else if (!isSonarr && item.RadarrId.HasValue)
                    {
                        await _radarrService.SetMovieMonitorState(item.RadarrId.Value, monitored);
                        result.Actions.Add(monitored ? "reMonitor" : "unmonitor");
                    }
                    else
                    {
                        result.Errors.Add($"No {(isSonarr ? "Sonarr" : "Radarr")} ID — cannot change monitor state");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to change monitor state: {ex.Message}");
                    _logger.LogWarning(ex, "[MediaController] Failed to change monitor state for {Title}", item.Title);
                }
            }

            // Step 3: Remove from arr
            if (request.RemoveFromArr)
            {
                try
                {
                    // Files were already deleted in step 1, so don't double-delete
                    var shouldDeleteFiles = false;
                    if (isSonarr && item.SonarrId.HasValue)
                    {
                        await _sonarrService.DeleteSeries(item.SonarrId.Value, shouldDeleteFiles);
                        result.Actions.Add("removeFromArr");
                    }
                    else if (!isSonarr && item.RadarrId.HasValue)
                    {
                        await _radarrService.DeleteMovie(item.RadarrId.Value, shouldDeleteFiles);
                        result.Actions.Add("removeFromArr");
                    }
                    else
                    {
                        result.Errors.Add($"No {(isSonarr ? "Sonarr" : "Radarr")} ID — cannot remove from arr");
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to remove from arr: {ex.Message}");
                    _logger.LogWarning(ex, "[MediaController] Failed to remove {Title} from arr", item.Title);
                }
            }

            // Step 4: Update Storarr DB
            // Keep in DB only if: remove from arr but files NOT deleted
            var keepInDb = request.RemoveFromArr && !request.DeleteFiles;

            if (keepInDb)
            {
                // Clear arr IDs but keep the item
                item.SonarrId = null;
                item.RadarrId = null;
                item.SonarrFileId = null;
                item.RadarrFileId = null;
                item.CurrentState = FileState.Symlink;
                item.StateChangedAt = DateTime.UtcNow;
                _logger.LogInformation("[MediaController] Cleared arr IDs for {Title} — keeping in DB (files on disk)", item.Title);
            }
            else if (request.DeleteFiles || request.RemoveFromArr)
            {
                _dbContext.MediaItems.Remove(item);
                _logger.LogInformation("[MediaController] Removed {Title} from Storarr DB", item.Title);
            }

            // Step 5: Activity log
            foreach (var action in result.Actions)
            {
                _dbContext.ActivityLogs.Add(new ActivityLog
                {
                    MediaItemId = item.Id,
                    Action = $"Manage_{action}",
                    FromState = item.CurrentState.ToString(),
                    ToState = keepInDb ? "Cleared" : "Removed",
                    Details = $"Manage action: {action}",
                    Timestamp = DateTime.UtcNow
                });
            }

            result.Success = !result.Errors.Any() || result.Actions.Any();
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Unexpected error: {ex.Message}");
            _logger.LogError(ex, "[MediaController] Unexpected error managing {Title}", item.Title);
        }

        results.Add(result);
    }

    await _dbContext.SaveChangesAsync();
    return Ok(new ManageMediaResultDto { Results = results });
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build /home/dobbie/storarr/Storarr.sln`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Controllers/MediaController.cs src/Storarr/DTOs/ManageMediaDto.cs
git commit -m "feat: add POST /api/v1/media/manage endpoint with batch delete/monitor/remove"
```

---

### Task 5: Add manageMedia API Function to Frontend

**Files:**
- Modify: `src/Storarr.Frontend/src/api/client.ts` (add after existing media functions around line 42)

- [ ] **Step 1: Add the manageMedia function**

In `client.ts`, add after `clearGhostPending()` (line 42):

```typescript
export const manageMedia = async (
  itemIds: number[],
  options: { deleteFiles: boolean; removeFromArr: boolean; unmonitor: boolean; reMonitor: boolean }
) => {
  const response = await api.post('/media/manage', { itemIds, ...options })
  return response.data
}
```

Also add the TypeScript interface near the top of the file (after the existing imports):

```typescript
export interface ManageMediaResult {
  results: ManageMediaItemResult[]
}

export interface ManageMediaItemResult {
  itemId: number
  title: string
  type: string
  success: boolean
  actions: string[]
  errors: string[]
}
```

- [ ] **Step 2: Build and verify**

Run: `cd /home/dobbie/storarr/src/Storarr.Frontend && npx tsc --noEmit`
Expected: No type errors.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/api/client.ts
git commit -m "feat: add manageMedia API function and types"
```

---

### Task 6: Add Manage Button to BulkActionBar

**Files:**
- Modify: `src/Storarr.Frontend/src/components/BulkActionBar.tsx`

- [ ] **Step 1: Add onManage prop to BulkActionBarProps**

Update the props interface (lines 3-10):

```typescript
interface BulkActionBarProps {
  selectedCount: number
  hasEligibleForMkv: boolean
  hasEligibleForSymlink: boolean
  onConvertToMkv: () => void
  onConvertToSymlink: () => void
  onManage?: () => void
  onDeselectAll: () => void
}
```

- [ ] **Step 2: Add Manage button to the UI**

Inside the right-side button group (the `<div className="flex items-center gap-3">` container), add a Manage button before the Convert to MKV button. Use `lucide-react` `Settings` icon (consistent with existing icon imports):

```tsx
import { Settings } from 'lucide-react'

// In the button group:
{onManage && (
  <button
    onClick={onManage}
    className="flex items-center gap-2 px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 text-white rounded-md transition-colors text-sm font-medium"
  >
    <Settings className="w-4 h-4" />
    Manage
  </button>
)}
```

- [ ] **Step 3: Build and verify**

Run: `cd /home/dobbie/storarr/src/Storarr.Frontend && npx tsc --noEmit`
Expected: No type errors.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr.Frontend/src/components/BulkActionBar.tsx
git commit -m "feat: add Manage button to BulkActionBar"
```

---

### Task 7: Create ManageModal Component

**Files:**
- Create: `src/Storarr.Frontend/src/components/ManageModal.tsx`

- [ ] **Step 1: Create the ManageModal component**

Create `src/Storarr.Frontend/src/components/ManageModal.tsx`. This follows the existing `ConfirmationDialog.tsx` modal pattern (fixed overlay, `bg-arr-card`, `z-50`, backdrop click cancels):

```tsx
import { useState } from 'react'
import { CatalogEpisodeDto, CatalogGroupDto } from './CatalogView'
import { manageMedia, ManageMediaResult, ManageMediaItemResult } from '../api/client'

interface ManageModalProps {
  open: boolean
  selectedItems: Map<string, { ep: CatalogEpisodeDto; group: CatalogGroupDto }>
  onCancel: () => void
  onComplete: () => void
}

type ActionState = 'idle' | 'executing' | 'done'

export default function ManageModal({ open, selectedItems, onCancel, onComplete }: ManageModalProps) {
  const [deleteFiles, setDeleteFiles] = useState(false)
  const [removeFromArr, setRemoveFromArr] = useState(false)
  const [unmonitor, setUnmonitor] = useState(false)
  const [reMonitor, setReMonitor] = useState(false)
  const [actionState, setActionState] = useState<ActionState>('idle')
  const [results, setResults] = useState<ManageMediaItemResult[]>([])

  if (!open) return null

  const items = Array.from(selectedItems.values())
  const titles = items.map(v => v.group.title)
  const uniqueTitles = [...new Set(titles)]
  const displayTitles = uniqueTitles.slice(0, 5)
  const extraCount = uniqueTitles.length - 5

  const mediaItemIds = items
    .map(v => v.ep.mediaItemId)
    .filter((id): id is number => id != null)

  const hasAction = deleteFiles || removeFromArr || unmonitor || reMonitor

  // Conflict: remove from arr disables monitor options
  const monitorDisabled = removeFromArr

  // Mutual exclusion: unmonitor vs reMonitor
  const handleUnmonitor = (checked: boolean) => {
    setUnmonitor(checked)
    if (checked) setReMonitor(false)
  }
  const handleReMonitor = (checked: boolean) => {
    setReMonitor(checked)
    if (checked) setUnmonitor(false)
  }

  // Build confirmation button text
  const getButtonText = () => {
    const parts: string[] = []
    if (deleteFiles) parts.push(`Delete ${mediaItemIds.length} file${mediaItemIds.length !== 1 ? 's' : ''}`)
    if (removeFromArr) parts.push('remove from Sonarr/Radarr')
    if (unmonitor) parts.push('unmonitor')
    if (reMonitor) parts.push('set to monitored')
    if (parts.length === 0) return 'Select an action'
    if (parts.length === 1) return parts[0]
    const last = parts.pop()
    return `${parts.join(', ')} and ${last}`
  }

  const handleConfirm = async () => {
    setActionState('executing')
    try {
      const result: ManageMediaResult = await manageMedia(mediaItemIds, {
        deleteFiles,
        removeFromArr,
        unmonitor,
        reMonitor,
      })
      setResults(result.results)
      setActionState('done')
    } catch (err: any) {
      setResults([{ itemId: 0, title: 'Request failed', type: '', success: false, actions: [], errors: [err.message] }])
      setActionState('done')
    }
  }

  const handleClose = () => {
    setDeleteFiles(false)
    setRemoveFromArr(false)
    setUnmonitor(false)
    setReMonitor(false)
    setActionState('idle')
    setResults([])
    if (actionState === 'done') {
      onComplete()
    } else {
      onCancel()
    }
  }

  return (
    <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4" onClick={handleClose}>
      <div className="bg-arr-card rounded-lg max-w-lg w-full shadow-xl" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-arr-primary">
          <h2 className="text-lg font-semibold text-arr-text">Manage {mediaItemIds.length} Selected Item{mediaItemIds.length !== 1 ? 's' : ''}</h2>
          <button onClick={handleClose} className="text-arr-text/50 hover:text-arr-text transition-colors">
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Content */}
        <div className="px-6 py-4">
          {/* Selected items summary */}
          <div className="mb-4 text-sm text-arr-text/70">
            <span className="font-medium text-arr-text">Selected:</span>{' '}
            {displayTitles.map((t, i) => (
              <span key={i}>{i > 0 ? ', ' : ''}{t}</span>
            ))}
            {extraCount > 0 && <span> +{extraCount} more</span>}
          </div>

          {actionState === 'idle' && (
            <>
              {/* Action checkboxes */}
              <div className="space-y-3">
                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={deleteFiles} onChange={(e) => setDeleteFiles(e.target.checked)}
                    className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                  <span className="text-arr-text">Delete file(s) from disk</span>
                </label>

                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={removeFromArr} onChange={(e) => setRemoveFromArr(e.target.checked)}
                    className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                  <span className="text-arr-text">Remove from Sonarr/Radarr</span>
                </label>

                <label className={`flex items-center gap-3 ${monitorDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}>
                  <input type="checkbox" checked={unmonitor} onChange={(e) => handleUnmonitor(e.target.checked)}
                    disabled={monitorDisabled}
                    className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                  <span className="text-arr-text">Set to Unmonitored</span>
                  {monitorDisabled && <span className="text-xs text-arr-text/40">(not available when removing)</span>}
                </label>

                <label className={`flex items-center gap-3 ${monitorDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}>
                  <input type="checkbox" checked={reMonitor} onChange={(e) => handleReMonitor(e.target.checked)}
                    disabled={monitorDisabled}
                    className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                  <span className="text-arr-text">Set to Monitored</span>
                  {monitorDisabled && <span className="text-xs text-arr-text/40">(not available when removing)</span>}
                </label>
              </div>
            </>
          )}

          {actionState === 'executing' && (
            <div className="flex items-center justify-center py-8">
              <svg className="animate-spin w-8 h-8 text-arr-accent" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              <span className="ml-3 text-arr-text">Processing...</span>
            </div>
          )}

          {actionState === 'done' && (
            <div className="space-y-2 max-h-48 overflow-y-auto">
              {results.map((r, i) => (
                <div key={i} className={`text-sm p-2 rounded ${r.success ? 'bg-green-900/20 text-green-400' : 'bg-red-900/20 text-red-400'}`}>
                  <span className="font-medium">{r.title}</span>
                  {r.actions.length > 0 && <span className="text-arr-text/60 ml-2">({r.actions.join(', ')})</span>}
                  {r.errors.map((e, j) => (
                    <div key={j} className="text-red-400 text-xs mt-1">{e}</div>
                  ))}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-arr-primary">
          <button onClick={handleClose} className="px-4 py-2 text-arr-text/70 hover:text-arr-text transition-colors text-sm">
            {actionState === 'done' ? 'Close' : 'Cancel'}
          </button>
          {actionState === 'idle' && (
            <button onClick={handleConfirm} disabled={!hasAction || mediaItemIds.length === 0}
              className="px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 disabled:opacity-40 disabled:cursor-not-allowed text-white rounded-md transition-colors text-sm font-medium">
              {getButtonText()}
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
```

Note: The `CatalogEpisodeDto` and `CatalogGroupDto` types need to be exported from CatalogView.tsx (or from the store). See Task 8 for the export.

- [ ] **Step 2: Build and verify**

Run: `cd /home/dobbie/storarr/src/Storarr.Frontend && npx tsc --noEmit`
Expected: May have errors about unexported types — will fix in Task 8.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr.Frontend/src/components/ManageModal.tsx
git commit -m "feat: add ManageModal component with action checkboxes and results display"
```

---

### Task 8: Wire ManageModal into CatalogView

**Files:**
- Modify: `src/Storarr.Frontend/src/components/CatalogView.tsx`

- [ ] **Step 1: Import ManageModal and manageMedia**

Add to the imports at the top of CatalogView.tsx:

```typescript
import ManageModal from './ManageModal'
```

- [ ] **Step 2: Export the CatalogEpisodeDto and CatalogGroupDto types**

The `ManageModal` needs access to `CatalogEpisodeDto` and `CatalogGroupDto` types. These are imported from the API client store in CatalogView. Check if they're already defined in `appStore.ts` (they are — lines in the interfaces section). Ensure they're importable from the store. The ManageModal should import them from `'../stores/appStore'` instead of from CatalogView.

Update ManageModal.tsx imports accordingly:

```typescript
// In ManageModal.tsx, change the import:
import { CatalogEpisodeDto, CatalogGroupDto } from '../stores/appStore'
```

And remove the incorrect import from CatalogView.

- [ ] **Step 3: Add ManageModal state to CatalogView**

In CatalogView.tsx, add state for the manage modal:

```typescript
const [manageModalOpen, setManageModalOpen] = useState(false)
```

- [ ] **Step 4: Add onManage handler and pass to BulkActionBar**

Add a handler function (near the existing `handleConvertToMkv` and `handleConvertToSymlink` functions):

```typescript
const handleManage = () => {
  setManageModalOpen(true)
}
```

Update the BulkActionBar props (around line 473) to include `onManage`:

```tsx
<BulkActionBar
  selectedCount={selectedEpisodes.size}
  hasEligibleForMkv={hasEligibleForMkv}
  hasEligibleForSymlink={hasEligibleForSymlink}
  onConvertToMkv={handleConvertToMkv}
  onConvertToSymlink={handleConvertToSymlink}
  onManage={handleManage}
  onDeselectAll={() => setSelectedEpisodes(new Map())}
/>
```

- [ ] **Step 5: Render ManageModal**

Add the ManageModal component after the BulkActionBar, near the bottom of the CatalogView return JSX:

```tsx
<ManageModal
  open={manageModalOpen}
  selectedItems={selectedEpisodes}
  onCancel={() => setManageModalOpen(false)}
  onComplete={() => {
    setManageModalOpen(false)
    setSelectedEpisodes(new Map())
    loadCatalog()
  }}
/>
```

- [ ] **Step 6: Build and verify**

Run: `cd /home/dobbie/storarr/src/Storarr.Frontend && npx tsc --noEmit`
Expected: No type errors.

- [ ] **Step 7: Commit**

```bash
git add src/Storarr.Frontend/src/components/CatalogView.tsx src/Storarr.Frontend/src/components/ManageModal.tsx
git commit -m "feat: wire ManageModal into CatalogView with selection integration"
```

---

### Task 9: Full Build, Deploy, and Manual Test

**Files:** None (verification only)

- [ ] **Step 1: Full backend build**

Run: `dotnet build /home/dobbie/storarr/Storarr.sln`
Expected: Build succeeds.

- [ ] **Step 2: Full frontend build**

Run: `cd /home/dobbie/storarr/src/Storarr.Frontend && npm run build`
Expected: Build succeeds.

- [ ] **Step 3: Deploy and rebuild Docker container**

```bash
cd /home/dobbie/storarr
docker compose build storarr
docker compose up -d storarr
```

- [ ] **Step 4: Manual test — Manage button appears**

1. Open Storarr UI in browser
2. Navigate to Media page
3. Select one or more items using checkboxes
4. Verify the BulkActionBar appears with "Manage" button alongside Convert buttons

- [ ] **Step 5: Manual test — Manage modal opens and validates**

1. Click "Manage" button
2. Verify modal opens with 4 checkboxes
3. Verify "Select an action" button is disabled when no checkboxes are checked
4. Check "Remove from Sonarr/Radarr" → verify Unmonitor and Re-monitor checkboxes gray out
5. Check "Unmonitor" then check "Re-monitor" → verify they're mutually exclusive

- [ ] **Step 6: Manual test — Re-monitor action (non-destructive)**

1. Select a single tracked item with a Sonarr/Radarr ID
2. Click "Manage"
3. Check only "Set to Monitored"
4. Click confirm
5. Verify success result shown
6. Check Sonarr/Radarr UI to confirm the item is now monitored

- [ ] **Step 7: Manual test — Delete file action**

1. Select a single item to test file deletion
2. Click "Manage"
3. Check "Delete file(s) from disk"
4. Confirm and verify the file is removed and item disappears from catalog

- [ ] **Step 8: Commit any fixes from manual testing**

```bash
git add -A
git commit -m "fix: address issues found during manual testing"
```
