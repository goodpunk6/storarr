# Storarr Fallback Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Jellyseerr fallback in MKV→symlink transitions with a usenet-availability check followed by a Sonarr/Radarr search trigger, preventing unwanted full MKV re-downloads.

**Architecture:** Before deleting any MKV, check if usenet releases exist via Sonarr/Radarr release search APIs. If usenet releases are available, delete the MKV and trigger an Arr search command — the Arr stack will naturally pick NZBdav (no priority = first choice). If no usenet releases exist, skip the transition entirely and keep the MKV.

**Tech Stack:** C# / ASP.NET Core / Entity Framework Core / Sonarr & Radarr REST APIs

**Spec:** `docs/superpowers/specs/2026-04-26-storarr-fallback-fix-design.md`

---

### Task 1: Add `CheckUsenetAvailable` method to TransitionService

**Files:**
- Modify: `src/Storarr/Services/TransitionService.cs:355` (add new method after `FilterRelease`)

This method searches releases via the Arr API and checks if any `downloadAllowed` release has `protocol: "usenet"`. It reuses the same episode-ID resolution logic already in `TryDirectReleaseGrab`.

- [ ] **Step 1: Add `CheckUsenetAvailable` method**

Add the following private method to `TransitionService` class (after `FilterRelease` at line 463):

```csharp
private async Task<bool> CheckUsenetAvailable(MediaItem item)
{
    try
    {
        if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
        {
            if (!item.SeasonNumber.HasValue || !item.EpisodeNumber.HasValue)
            {
                _logger.LogDebug("[TransitionService] Cannot check usenet: missing season/episode for {Title}", item.Title);
                return false;
            }

            var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
            if (!episodeId.HasValue)
            {
                _logger.LogDebug("[TransitionService] Cannot check usenet: episode not found for {Title}", item.Title);
                return false;
            }

            var releases = await _sonarrService.SearchReleases(item.SonarrId.Value, new[] { episodeId.Value });
            return releases.Any(r => r.DownloadAllowed && r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
        }
        else if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
        {
            var releases = await _radarrService.SearchReleases(item.RadarrId.Value);
            return releases.Any(r => r.DownloadAllowed && r.Protocol?.Equals("usenet", StringComparison.OrdinalIgnoreCase) == true);
        }
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "[TransitionService] Failed to check usenet availability for {Title}", item.Title);
    }

    return false;
}
```

- [ ] **Step 2: Verify it compiles**

Run: `cd /path/to/storarr/src && dotnet build`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add src/Storarr/Services/TransitionService.cs
git commit -m "feat: add CheckUsenetAvailable method to TransitionService"
```

---

### Task 2: Modify `TransitionToSymlink` to use new flow

**Files:**
- Modify: `src/Storarr/Services/TransitionService.cs:155-216` (the `TransitionToSymlink` method)

Replace the current flow (delete → try direct grab → Jellyseerr fallback) with: check usenet → if yes, delete → trigger search → if no, skip.

- [ ] **Step 1: Replace the grab/fallback block in `TransitionToSymlink`**

Replace lines 185-198 in `TransitionToSymlink`:

**Old code (lines 185-198):**
```csharp
                // Try direct release grab via Sonarr/Radarr with specific download client
                var config = await _dbContext.Configs.FindAsync(Config.SingletonId);
                bool grabbed = false;

                if (config != null)
                {
                    grabbed = await TryDirectReleaseGrab(item, config);
                }

                if (!grabbed)
                {
                    _logger.LogInformation("[TransitionService] Direct grab not available, falling back to Jellyseerr for {Title}", item.Title);
                    await _jellyseerrService.CreateRequest(item.TmdbId.Value, item.Type, item.TvdbId);
                }
```

**New code:**
```csharp
                // Check if usenet releases are available before proceeding
                var hasUsenet = await CheckUsenetAvailable(item);
                if (!hasUsenet)
                {
                    _logger.LogInformation("[TransitionService] No usenet releases available for {Title}, skipping symlink transition", item.Title);
                    return;
                }

                // Trigger Sonarr/Radarr search — they will pick NZBdav for usenet releases
                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    await _radarrService.TriggerSearch(item.RadarrId.Value);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                    {
                        var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                        if (episodeId.HasValue)
                        {
                            await _sonarrService.TriggerSearch(item.SonarrId.Value, new[] { episodeId.Value });
                        }
                        else
                        {
                            _logger.LogWarning("[TransitionService] Could not resolve episode ID for {Title}, triggering series search", item.Title);
                            await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        }
                    }
                    else
                    {
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                    }
                }
```

Note: The file deletion code (lines 161-183) stays unchanged — it runs before this block. The usenet check happens after deletion but the spec was updated to do check-first. We need to **move the usenet check BEFORE the deletion block** (before line 161).

- [ ] **Step 2: Move usenet check before file deletion**

The usenet check must happen before any file deletion. Restructure `TransitionToSymlink` so the order is:

1. Usenet availability check (new, at the top of the try block)
2. If no usenet: return early, no deletion
3. Delete file via Arr API / disk (existing code, lines 161-183)
4. Trigger Arr search (new code from step 1)
5. Update state to PendingSymlink (existing code, lines 200-215)

The full restructured `TransitionToSymlink` method body should be:

```csharp
        public async Task TransitionToSymlink(MediaItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (item.TmdbId == null) throw new InvalidOperationException("Item must have a TMDb ID");

            try
            {
                // Check if usenet releases are available before doing anything
                var hasUsenet = await CheckUsenetAvailable(item);
                if (!hasUsenet)
                {
                    _logger.LogInformation("[TransitionService] No usenet releases available for {Title}, skipping symlink transition", item.Title);
                    return;
                }

                // Delete the file via Arr API and/or disk
                bool apiDeleted = false;
                var arrFilePath = RemapToArrPath(item.FilePath);

                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting movie file via Radarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, item.FilePath)
                        || await _radarrService.DeleteMovieFileByPath(item.RadarrId.Value, arrFilePath);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    _logger.LogDebug("[TransitionService] Deleting episode file via Sonarr API: {Path} (arr: {ArrPath})", item.FilePath, arrFilePath);
                    apiDeleted = await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, item.FilePath)
                        || await _sonarrService.DeleteEpisodeFileByPath(item.SonarrId.Value, arrFilePath);
                }

                if (await _fileService.FileExists(item.FilePath))
                {
                    _logger.LogDebug("[TransitionService] Deleting file from disk: {Path}", item.FilePath);
                    await _fileService.DeleteFile(item.FilePath);
                }

                // Trigger Sonarr/Radarr search — they will pick NZBdav for usenet releases
                if (item.Type == MediaType.Movie && item.RadarrId.HasValue)
                {
                    await _radarrService.TriggerSearch(item.RadarrId.Value);
                }
                else if ((item.Type == MediaType.Series || item.Type == MediaType.Anime) && item.SonarrId.HasValue)
                {
                    if (item.SeasonNumber.HasValue && item.EpisodeNumber.HasValue)
                    {
                        var episodeId = await _sonarrService.GetEpisodeId(item.SonarrId.Value, item.SeasonNumber.Value, item.EpisodeNumber.Value);
                        if (episodeId.HasValue)
                        {
                            await _sonarrService.TriggerSearch(item.SonarrId.Value, new[] { episodeId.Value });
                        }
                        else
                        {
                            _logger.LogWarning("[TransitionService] Could not resolve episode ID for {Title}, triggering series search", item.Title);
                            await _sonarrService.TriggerSearch(item.SonarrId.Value);
                        }
                    }
                    else
                    {
                        await _sonarrService.TriggerSearch(item.SonarrId.Value);
                    }
                }

                var previousState = item.CurrentState;
                item.CurrentState = FileState.PendingSymlink;
                item.StateChangedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                await LogActivity(item.Id, "TransitionToSymlink", previousState, FileState.PendingSymlink,
                    apiDeleted ? "Deleted via Arr API" : "Deleted from disk");
                await _hubContext.Clients.All.SendAsync("MediaUpdated", item.Id, item.CurrentState.ToString());

                _logger.LogInformation("[TransitionService] Successfully transitioned {Title} to PendingSymlink state (API deleted: {ApiDeleted})",
                    item.Title, apiDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TransitionService] Failed to transition {Title} to symlink", item.Title);
                throw;
            }
        }
```

- [ ] **Step 3: Verify it compiles**

Run: `cd /path/to/storarr/src && dotnet build`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add src/Storarr/Services/TransitionService.cs
git commit -m "feat: replace Jellyseerr fallback with Arr search trigger in TransitionToSymlink"
```

---

### Task 3: Build Docker image and test

**Files:** None (testing only)

- [ ] **Step 1: Build Docker image**

Run: `cd /path/to/storarr && docker compose build storarr`
Expected: Build succeeds.

- [ ] **Step 2: Deploy updated container**

Run: `cd /path/to/storarr && docker compose up -d storarr`
Expected: Container starts without errors.

- [ ] **Step 3: Verify Storarr is healthy**

Run: `curl -s http://localhost:8687/api/v1/config | jq '.libraryMode'`
Expected: `"TrackExisting"`

- [ ] **Step 4: Check logs for startup errors**

Run: `docker logs storarr --tail 20 2>&1 | grep -i "error\|fail\|exception" | head -5`
Expected: No errors.

- [ ] **Step 5: Commit final state**

```bash
git add -A
git commit -m "chore: build and deploy fallback fix"
```

---

## Summary of changes

| File | Change |
|------|--------|
| `TransitionService.cs` | Add `CheckUsenetAvailable` method; rewrite `TransitionToSymlink` to check usenet before deleting, trigger Arr search instead of Jellyseerr fallback |

No interface changes needed — `TriggerSearch`, `SearchReleases`, `GetEpisodeId`, and `GetDownloadClients` already exist in both `ISonarrService` and `IRadarrService`.

`TryDirectReleaseGrab` and `FilterRelease` are kept unchanged — they're still used by the symlink → MKV manual transition flow.
