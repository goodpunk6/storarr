# Storarr Transition Fallback Fix

## Problem

When Storarr auto-transitions MKV to symlink, `TryDirectReleaseGrab` attempts to grab a release via NZBdav (download client 3). This fails in two cases:

1. **Magnet links** — NZBdav is a Usenet/SABnzbd client and cannot handle `magnet:` URIs
2. **No usenet releases** — some content only has torrent releases available

When the direct grab fails, Storarr falls back to Jellyseerr, which re-requests the content through Sonarr/Radarr's default download clients (SABnzbd/qBittorrent). This downloads full MKV files — defeating the purpose of converting to symlink/.strm.

The flood of Jellyseerr fallback requests also creates hundreds of unwanted downloads (observed: 358 Bold and Beautiful episodes, hundreds of Housewives/Hacks/Summer House episodes).

## Context

NZBdav is now enabled as a download client in both Sonarr and Radarr with no priority number set. SABnzbd is priority 49, qBittorrent is priority 50. Sonarr/Radarr will naturally prefer NZBdav (no priority = highest) for usenet releases.

## Design

### MKV → Symlink (auto-transition): Replace direct grab with Sonarr/Radarr search trigger

**Current flow:**
```
Delete MKV → TryDirectReleaseGrab(NZBdab) → fail → Jellyseerr → full MKV download
```

**New flow:**
```
Check if usenet releases available
  → If yes: Delete MKV → Trigger Sonarr/Radarr search command (they pick NZBdav)
  → If no: Skip transition, keep the MKV, log warning
```

Note: usenet check happens BEFORE deletion to avoid losing the file if the search trigger subsequently fails.

**Changes to `TransitionService.TransitionToSymlink` (lines ~185-198):**

1. Before deleting the MKV, call new `CheckUsenetAvailable` method
2. `CheckUsenetAvailable`: searches releases via Sonarr/Radarr API, returns true if any `downloadAllowed` release has `protocol: "usenet"`
   - For Series and Anime types: resolve episode ID (same logic as `TryDirectReleaseGrab` lines 379-389), then search releases
   - For Movie type: search releases by movieId
3. If no usenet releases: abort the transition, log warning, don't change state, don't delete the file
4. If usenet releases exist: delete MKV, then trigger Sonarr/Radarr search command using existing methods:
   - Sonarr: `TriggerSearch(seriesId, episodeIds)` — already exists in `ISonarrService`
   - Radarr: `TriggerSearch(movieId)` — already exists in `IRadarrService`
5. Sonarr/Radarr handle release selection — NZBdav is picked first (no priority)
6. Remove the Jellyseerr fallback entirely from this path

### Symlink → MKV (manual): Keep existing direct grab

No changes. `TryDirectReleaseGrab` continues to use a specific `downloadClientId` to bypass NZBdav and route to SABnzbd/qBittorrent for real MKV downloads.

### Files changed

- `src/Storarr/Services/TransitionService.cs` — modify `TransitionToSymlink`, add `CheckUsenetAvailable`
- `ISonarrService.cs` / `IRadarrService.cs` / `SonarrService.cs` / `RadarrService.cs` — no changes needed, `TriggerSearch` and `SearchReleases` already exist

### What stays the same

- `TryDirectReleaseGrab` — used only for symlink → MKV manual transitions
- `FilterRelease` — used only by `TryDirectReleaseGrab`
- All webhook/state machine logic in `WebhookController`
- `TransitionToMkv` flow — unchanged
