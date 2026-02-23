# Multi-Drive Storage Plan for Storarr

## Current Architecture Analysis

### How Storage Works Now
1. **Single Media Library Path**: Config has one `MediaLibraryPath` (default: `/media`)
2. **File Discovery**: LibraryScanner scans this single path for all media files
3. **Path Validation**: FileManagementService validates all paths are within MediaLibraryPath
4. **State Transitions**: Files are deleted and re-downloaded to the same location
5. **Arr Integration**: Sonarr/Radarr manage the actual file paths based on their own root folder configuration

### Current Flow
```
MediaLibraryPath (/media)
├── TV/
│   └── Series Name/
│       └── Season 01/
│           ├── S01E01.strm  (symlink state)
│           └── S01E02.mkv   (mkv state)
└── Movies/
    └── Movie Name (Year)/
        └── Movie Name.mkv
```

## The Challenge

Users want:
- **Symlinks (.strm)** on a fast/small drive (e.g., NVMe SSD) for quick streaming
- **MKV files** on a large/slow drive (e.g., HDD array) for bulk storage

### Constraints
1. **Sonarr/Radarr own the paths**: They decide where files are stored based on root folders
2. **Jellyfin needs consistent paths**: Library paths must remain stable for metadata
3. **Transitions must be atomic**: Moving files between drives must not break *arr state

## Proposed Solution: Storage Tiers with Path Mapping

### Approach
Instead of physically moving files, Storarr will:
1. Configure **separate root folders** in Sonarr/Radarr for each storage tier
2. Use **symlinks at the Jellyfin library level** to present a unified view
3. Orchestrate **re-downloads to different root folders** during transitions

### Architecture Changes

#### 1. New Configuration Model
```csharp
public class StorageTier
{
    public int Id { get; set; }
    public string Name { get; set; }  // "Fast", "Bulk", etc.
    public string Path { get; set; }  // "/fast-media" or "/bulk-media"
    public StorageTierType Type { get; set; }  // Symlink, Mkv, Both
    public bool IsDefault { get; set; }
}

public enum StorageTierType
{
    SymlinkOnly,  // For .strm files only
    MkvOnly,      // For full quality files only
    Both          // Can contain either
}
```

#### 2. Updated Config
```csharp
// Replace single MediaLibraryPath with:
public string? SymlinkStoragePath { get; set; }  // e.g., /mnt/nvme/media
public string? MkvStoragePath { get; set; }      // e.g., /mnt/hdd-array/media
public string JellyfinLibraryPath { get; set; } = "/media";  // Unified view for Jellyfin
```

### Implementation Options

#### Option A: Jellyfin Library Symlinks (Recommended)
```
JellyfinLibraryPath (/media - unified view via symlinks)
├── TV/ -> /mnt/nvme/media/TV/ (symlinks stored here)
└── Movies/ -> /mnt/hdd-array/media/Movies/ (MKVs stored here)
```

**How it works:**
1. Jellyfin sees a unified library at `/media`
2. Actual files are on different drives
3. Storarr manages which drive holds what based on state

**Pros:**
- Jellyfin sees consistent paths
- No *arr reconfiguration needed
- Simple to implement

**Cons:**
- Requires careful symlink management
- Need to maintain directory structure across drives

#### Option B: Arr Root Folder Coordination
```
Sonarr Root Folders:
- /mnt/nvme/media/TV (for symlinks)
- /mnt/hdd-array/media/TV (for MKVs)

Radarr Root Folders:
- /mnt/nvme/media/Movies (for symlinks)
- /mnt/hdd-array/media/Movies (for MKVs)
```

**How it works:**
1. Storarr tells Sonarr/Radarr to move content between root folders
2. *arr handles the actual file movement
3. Jellyfin libraries updated to point to both locations

**Pros:**
- *arr handles all file management
- Native quality profile integration

**Cons:**
- Requires *arr API support for moving content
- Jellyfin needs multiple library paths
- More complex coordination

#### Option C: Hybrid Approach (Best)
```
Physical Layout:
/mnt/nvme/media/          (Symlink tier - fast storage)
├── TV/
│   └── Show Name/
│       └── S01E01.strm
└── Movies/
    └── Movie.strm

/mnt/hdd-array/media/     (Mkv tier - bulk storage)
├── TV/
│   └── Show Name/
│       └── S01E01.mkv
└── Movies/
    └── Movie.mkv

Jellyfin View (via union mount or Storarr-managed symlinks):
/media/
├── TV/ (merged view)
└── Movies/ (merged view)
```

## Recommended Implementation Plan

### Phase 1: Configuration Update
1. Add `SymlinkStoragePath` and `MkvStoragePath` to Config model
2. Create database migration
3. Add UI for configuring storage paths
4. Validate paths exist and are writable

### Phase 2: Library Scanner Update
1. Scan both storage paths
2. Track which storage tier each file is on
3. Add `StorageTier` property to MediaItem model

### Phase 3: Transition Logic Update
1. When transitioning Symlink → MKV:
   - Delete from SymlinkStoragePath
   - Trigger download (Sonarr/Radarr downloads to MkvStoragePath)
2. When transitioning MKV → Symlink:
   - Delete from MkvStoragePath
   - Request symlink via Jellyseerr (downloads to SymlinkStoragePath)

### Phase 4: Jellyfin Integration
1. Either:
   - Configure Jellyfin with multiple library paths
   - OR use mergerfs/unionfs to present unified view
   - OR create symlinks in a unified directory

### Phase 5: Sonarr/Radarr Coordination
**Critical**: Sonarr/Radarr need to download to the correct path.

**Solution**: Configure multiple root folders in Sonarr/Radarr:
- `/mnt/nvme/media/TV` - for symlink-tier content
- `/mnt/hdd-array/media/TV` - for mkv-tier content

When transitioning:
1. Update series/movie in Sonarr/Radarr to use different root folder
2. Trigger search/redownload
3. Files arrive in correct location

## API Changes Required

### New Endpoints
```
GET  /api/v1/storage-tiers       - List configured storage tiers
POST /api/v1/storage-tiers       - Add storage tier
PUT  /api/v1/storage-tiers/{id}  - Update storage tier
DELETE /api/v1/storage-tiers/{id} - Remove storage tier
```

### Updated Endpoints
```
GET/PUT /api/v1/config - Include symlinkPath and mkvPath fields
```

## Sonarr/Radarr API Requirements

### Sonarr API v3
- `GET /api/v3/series/{id}` - Get series with root folder path
- `PUT /api/v3/series/{id}` - Update series (can change rootFolderPath)
- `POST /api/v3/command` - Trigger "RenameSeries" to move files

### Radarr API v3
- `GET /api/v3/movie/{id}` - Get movie with root folder path
- `PUT /api/v3/movie/{id}` - Update movie (can change path)
- `POST /api/v3/command` - Trigger "RenameMovie" to move files

### Important Note
Sonarr/Radarr **can** move files between root folders, but it requires:
1. Updating the series/movie record with new `rootFolderPath`
2. Triggering a rename/move command

This is the cleanest approach as *arr maintains full control over files.

## Risk Assessment

### Low Risk
- Adding configuration fields
- Scanning multiple paths
- UI changes

### Medium Risk
- Path validation across multiple drives
- Ensuring Jellyfin can find content after transitions

### High Risk
- Changing Sonarr/Radarr root folders programmatically
- Potential for broken *arr state if moves fail

## Mitigation Strategies

1. **Atomic Operations**: Only change one thing at a time
2. **Rollback Support**: Keep metadata about original locations
3. **Dry Run Mode**: Preview what would happen before executing
4. *Arr Backup**: Recommend users backup before enabling multi-drive

## Alternative: Simpler Approach

If full multi-drive support is too complex, a simpler option:

### Per-Library Paths
Just allow configuring different paths for TV vs Movies:

```csharp
public string? TvLibraryPath { get; set; }    // /mnt/nvme/tv
public string? MovieLibraryPath { get; set; } // /mnt/hdd-array/movies
```

This allows:
- TV on fast storage (more likely to rewatch)
- Movies on bulk storage (larger files, watch once)

**Much simpler to implement** - just scan two paths instead of one.

## Conclusion

Multi-drive storage is **possible** but requires careful coordination with:
1. Sonarr/Radarr (for download destinations)
2. Jellyfin (for library paths)
3. File system (for path management)

**Recommended First Step**: Implement the simpler "Per-Library Paths" approach to validate the architecture before full tier-based storage.

---

## Questions to Resolve

1. **How is Jellyfin configured today?** Single library path or multiple?
2. **Do you want TV and Movies on different drives, or symlink vs MKV on different drives?**
3. **Is mergerfs/unionfs available in your environment?**
4. **Can Sonarr/Radarr have multiple root folders configured?**
