import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ChevronDown, ChevronRight, Loader2, ArrowUpDown, Trash2 } from 'lucide-react'
import { getCatalog, getSeriesEpisodes, ensureTracked, forceDownload, forceSymlink, clearGhostPending } from '../api/client'
import type { CatalogEpisodeDto, CatalogGroupDto } from '../stores/appStore'
import BulkActionBar from './BulkActionBar'
import ConfirmationDialog from './ConfirmationDialog'
import ManageModal from './ManageModal'

interface CatalogViewProps {
  filters: {
    search: string
    stateFilter: string
    typeFilter: string
  }
}

function getEpisodeKey(ep: CatalogEpisodeDto, group: CatalogGroupDto): string {
  if (group.type === 'Movie') return `${group.radarrId ?? 'movie'}-${ep.mediaItemId ?? ep.filePath ?? Math.random()}`
  return `${group.sonarrId}-${ep.seasonNumber ?? 0}-${ep.episodeNumber ?? 0}${ep.mediaItemId ? `-${ep.mediaItemId}` : ''}`
}

function getStateColor(state: string): string {
  switch (state.toLowerCase()) {
    case 'symlink':
      return 'bg-arr-success/20 text-arr-success'
    case 'mkv':
      return 'bg-arr-warning/20 text-arr-warning'
    case 'downloading':
      return 'bg-blue-500/20 text-blue-400'
    case 'pendingsymlink':
      return 'bg-arr-primary text-arr-muted'
    default:
      return 'bg-arr-primary text-arr-muted'
  }
}

function formatSize(bytes?: number): string {
  if (!bytes) return '-'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

export default function CatalogView({ filters }: CatalogViewProps) {
  const [catalog, setCatalog] = useState<CatalogGroupDto[]>([])
  const [loading, setLoading] = useState(true)
  const [expandedGroups, setExpandedGroups] = useState<Set<number>>(new Set())
  const [loadingEpisodes, setLoadingEpisodes] = useState<Set<number>>(new Set())
  const [selectedEpisodes, setSelectedEpisodes] = useState<Map<string, { ep: CatalogEpisodeDto; group: CatalogGroupDto }>>(new Map())
  const [confirmDialog, setConfirmDialog] = useState<{ action: 'toMkv' | 'toSymlink'; items: CatalogEpisodeDto[] } | null>(null)
  const [batchProgress, setBatchProgress] = useState<{ completed: number; failed: number; total: number; running: boolean } | null>(null)
  const [sortBy, setSortBy] = useState<'title-asc' | 'title-desc' | 'size-desc' | 'size-asc'>('title-asc')
  const [batchResult, setBatchResult] = useState<{ completed: number; failed: number; total: number } | null>(null)
  const [ghostClearResult, setGhostClearResult] = useState<{ cleared: number; total: number } | null>(null)
  const [ghostClearing, setGhostClearing] = useState(false)
  const [manageModalOpen, setManageModalOpen] = useState(false)

  const batchResultTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Load catalog on mount and when filters change
  const loadCatalog = useCallback(async () => {
    setLoading(true)
    try {
      const response = await getCatalog({
        type: filters.typeFilter || undefined,
        search: filters.search || undefined,
      })
      setCatalog(response.data)
    } catch (error) {
      console.error('Failed to fetch catalog:', error)
    } finally {
      setLoading(false)
    }
  }, [filters.typeFilter, filters.search])

  useEffect(() => {
    loadCatalog()
  }, [loadCatalog])

  // Auto-dismiss batch result after 5 seconds
  useEffect(() => {
    if (batchResult && !batchProgress?.running) {
      if (batchResultTimer.current) clearTimeout(batchResultTimer.current)
      batchResultTimer.current = setTimeout(() => setBatchResult(null), 5000)
    }
    return () => {
      if (batchResultTimer.current) clearTimeout(batchResultTimer.current)
    }
  }, [batchResult, batchProgress?.running])

  // Expand/collapse series group or multi-file movie
  const toggleGroup = useCallback(async (id: number) => {
    setExpandedGroups((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
        return next
      }
      next.add(id)
      return next
    })

    // If expanding a series and episodes are empty, load them
    const group = catalog.find((g) => g.sonarrId === id)
    if (group && group.episodes.length === 0 && !expandedGroups.has(id)) {
      setLoadingEpisodes((prev) => new Set(prev).add(id))
      try {
        const response = await getSeriesEpisodes(id)
        setCatalog((prev) =>
          prev.map((g) =>
            g.sonarrId === id ? { ...g, episodes: response.data } : g
          )
        )
      } catch (error) {
        console.error(`Failed to load episodes for sonarrId=${id}:`, error)
      } finally {
        setLoadingEpisodes((prev) => {
          const next = new Set(prev)
          next.delete(id)
          return next
        })
      }
    }
  }, [catalog, expandedGroups])

  // Selection helpers
  const toggleEpisode = useCallback((ep: CatalogEpisodeDto, group: CatalogGroupDto) => {
    const key = getEpisodeKey(ep, group)
    setSelectedEpisodes((prev) => {
      const next = new Map(prev)
      if (next.has(key)) {
        next.delete(key)
      } else {
        next.set(key, { ep, group })
      }
      return next
    })
  }, [])

  const toggleAllInGroup = useCallback(async (group: CatalogGroupDto) => {
    let episodes = group.episodes

    // If episodes haven't been loaded yet, load them first
    if (episodes.length === 0 && group.type !== 'Movie' && group.sonarrId) {
      setLoadingEpisodes((prev) => new Set(prev).add(group.sonarrId!))
      try {
        const response = await getSeriesEpisodes(group.sonarrId)
        episodes = response.data
        // Update catalog state so the expansion also has episodes
        setCatalog((prev) =>
          prev.map((g) =>
            g.sonarrId === group.sonarrId ? { ...g, episodes } : g
          )
        )
        // Auto-expand so user can see what was selected
        setExpandedGroups((prev) => new Set(prev).add(group.sonarrId!))
      } catch (error) {
        console.error(`Failed to load episodes for sonarrId=${group.sonarrId}:`, error)
        return
      } finally {
        setLoadingEpisodes((prev) => {
          const next = new Set(prev)
          next.delete(group.sonarrId!)
          return next
        })
      }
    }

    if (episodes.length === 0) return

    const allSelected = episodes.every((ep) => selectedEpisodes.has(getEpisodeKey(ep, group)))

    setSelectedEpisodes((prev) => {
      const next = new Map(prev)
      for (const ep of episodes) {
        const key = getEpisodeKey(ep, group)
        if (allSelected) {
          next.delete(key)
        } else {
          next.set(key, { ep, group })
        }
      }
      return next
    })
  }, [selectedEpisodes])

  // Selected items for BulkActionBar
  const selectedItems = useMemo(() => Array.from(selectedEpisodes.values()), [selectedEpisodes])
  const selectedEpisodesList = useMemo(() => selectedItems.map((s) => s.ep), [selectedItems])

  const hasEligibleForMkv = useMemo(
    () =>
      selectedEpisodesList.some((item) =>
        ['Symlink', 'PendingSymlink', 'Untracked'].includes(item.currentState)
      ),
    [selectedEpisodesList]
  )

  const hasEligibleForSymlink = useMemo(
    () =>
      selectedEpisodesList.some((item) =>
        ['Mkv', 'Downloading'].includes(item.currentState)
      ),
    [selectedEpisodesList]
  )

  // Batch execution
  const executeBatch = useCallback(
    async (items: CatalogEpisodeDto[], action: 'toMkv' | 'toSymlink') => {
      const eligible = items.filter((item) => {
        if (action === 'toMkv') return ['Symlink', 'PendingSymlink', 'Untracked'].includes(item.currentState)
        return ['Mkv', 'Downloading'].includes(item.currentState)
      })

      let completed = 0
      let failed = 0
      setBatchProgress({ completed: 0, failed: 0, total: eligible.length, running: true })
      setConfirmDialog(null)

      for (const item of eligible) {
        try {
          let id = item.mediaItemId
          if (!id) {
            // Untracked -- look up the group info
            const entry = selectedEpisodes.get(
              Array.from(selectedEpisodes.keys()).find((key) => {
                const val = selectedEpisodes.get(key)
                return val && val.ep === item
              }) ?? ''
            )
            const group = entry?.group

            const resp = await ensureTracked({
              sonarrId: group?.sonarrId,
              radarrId: group?.radarrId,
              type: group?.type ?? 'Series',
              title: item.title,
              seasonNumber: item.seasonNumber,
              episodeNumber: item.episodeNumber,
              tmdbId: group?.tmdbId,
              filePath: item.filePath ?? '',
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
        setBatchProgress((prev) => (prev ? { ...prev, completed, failed } : null))
        await new Promise((r) => setTimeout(r, 200))
      }

      setBatchProgress((prev) => (prev ? { ...prev, running: false } : null))
      setBatchResult({ completed, failed, total: eligible.length })
      setSelectedEpisodes(new Map())
      // Reload catalog to reflect changes
      loadCatalog()
    },
    [selectedEpisodes, loadCatalog]
  )

  const handleConvertToMkv = useCallback(() => {
    setConfirmDialog({ action: 'toMkv', items: selectedEpisodesList })
  }, [selectedEpisodesList])

  const handleConvertToSymlink = useCallback(() => {
    setConfirmDialog({ action: 'toSymlink', items: selectedEpisodesList })
  }, [selectedEpisodesList])

  const handleManage = () => {
    setManageModalOpen(true)
  }

  const handleConfirm = useCallback(() => {
    if (confirmDialog) {
      executeBatch(confirmDialog.items, confirmDialog.action)
    }
  }, [confirmDialog, executeBatch])

  const handleClearGhostPending = useCallback(async () => {
    setGhostClearing(true)
    try {
      const resp = await clearGhostPending()
      setGhostClearResult(resp.data)
      loadCatalog()
    } catch (e) {
      console.error('Failed to clear ghost pending:', e)
      setGhostClearResult({ cleared: -1, total: 0 })
    } finally {
      setGhostClearing(false)
      setTimeout(() => setGhostClearResult(null), 5000)
    }
  }, [loadCatalog])

  // Filter catalog based on stateFilter
  const filteredCatalog = useMemo(() => {
    let result = filters.stateFilter
      ? (() => {
          const stateLower = filters.stateFilter.toLowerCase()
          return catalog
            .map((group) => {
              const filteredEpisodes = group.episodes.filter(
                (ep) => ep.currentState.toLowerCase() === stateLower || (stateLower === 'untracked' && !ep.mediaItemId)
              )
              if (group.type === 'Movie') {
                if (group.episodes.length > 0 && filteredEpisodes.length === 0) return null
              } else {
                if (expandedGroups.has(group.sonarrId ?? 0) && filteredEpisodes.length === 0 && group.episodes.length > 0) {
                  return null
                }
              }
              return { ...group, episodes: filteredEpisodes.length > 0 ? filteredEpisodes : group.episodes }
            })
            .filter((g): g is CatalogGroupDto => g !== null)
        })()
      : catalog

    // Sort
    result = [...result].sort((a, b) => {
      switch (sortBy) {
        case 'title-asc': return a.title.localeCompare(b.title)
        case 'title-desc': return b.title.localeCompare(a.title)
        case 'size-desc': return b.totalSizeBytes - a.totalSizeBytes
        case 'size-asc': return a.totalSizeBytes - b.totalSizeBytes
        default: return 0
      }
    })

    return result
  }, [catalog, filters.stateFilter, expandedGroups, sortBy])

  // Selection state per group
  const getGroupSelectionState = useCallback(
    (group: CatalogGroupDto): 'none' | 'some' | 'all' => {
      if (group.episodes.length === 0) return 'none'
      const selectedCount = group.episodes.filter((ep) =>
        selectedEpisodes.has(getEpisodeKey(ep, group))
      ).length
      if (selectedCount === 0) return 'none'
      if (selectedCount === group.episodes.length) return 'all'
      return 'some'
    },
    [selectedEpisodes]
  )

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  return (
    <div className="space-y-4 pb-20">
      {/* Batch progress bar */}
      {batchProgress && (
        <div className="bg-arr-card rounded-lg p-4">
          <div className="flex items-center justify-between mb-2">
            <span className="text-sm text-arr-muted">
              Converting {batchProgress.completed + batchProgress.failed}/{batchProgress.total}...
            </span>
            {batchProgress.running && (
              <Loader2 className="w-4 h-4 animate-spin text-arr-accent" />
            )}
          </div>
          <div className="w-full bg-arr-primary rounded-full h-2">
            <div
              className="bg-arr-accent h-2 rounded-full transition-all duration-300"
              style={{
                width: `${batchProgress.total > 0 ? ((batchProgress.completed + batchProgress.failed) / batchProgress.total) * 100 : 0}%`,
              }}
            />
          </div>
        </div>
      )}

      {/* Batch result toast */}
      {batchResult && !batchProgress?.running && (
        <div className="bg-arr-card rounded-lg p-4 border border-arr-primary">
          {batchResult.failed === 0 ? (
            <span className="text-arr-success">
              All {batchResult.completed} converted successfully
            </span>
          ) : (
            <span className="text-arr-text">
              {batchResult.completed}/{batchResult.total} converted,{' '}
              <span className="text-arr-danger">{batchResult.failed} failed</span>
            </span>
          )}
        </div>
      )}

      {/* Sort control */}
      <div className="flex items-center gap-2">
        <ArrowUpDown className="w-4 h-4 text-arr-muted" />
        <select
          value={sortBy}
          onChange={(e) => setSortBy(e.target.value as typeof sortBy)}
          className="bg-arr-card border border-arr-primary rounded px-2 py-1 text-sm text-arr-text focus:outline-none focus:border-arr-accent"
        >
          <option value="title-asc">Title A-Z</option>
          <option value="title-desc">Title Z-A</option>
          <option value="size-desc">Size (largest first)</option>
          <option value="size-asc">Size (smallest first)</option>
        </select>
        <span className="text-sm text-arr-muted">{filteredCatalog.length} items</span>
        <div className="ml-auto">
          <button
            onClick={handleClearGhostPending}
            disabled={ghostClearing}
            className="text-arr-muted hover:text-arr-danger disabled:opacity-50 px-3 py-1 rounded flex items-center gap-1.5 transition-colors text-sm border border-arr-primary hover:border-arr-danger"
            title="Clear items stuck in PendingSymlink where the file no longer exists"
          >
            {ghostClearing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
            Clear Ghost Pending
          </button>
          {ghostClearResult && ghostClearResult.cleared >= 0 && (
            <span className="ml-2 text-xs text-arr-success">
              Cleared {ghostClearResult.cleared}/{ghostClearResult.total}
            </span>
          )}
          {ghostClearResult && ghostClearResult.cleared < 0 && (
            <span className="ml-2 text-xs text-arr-danger">Failed</span>
          )}
        </div>
      </div>

      {/* Catalog table */}
      {filteredCatalog.length === 0 ? (
        <div className="text-center py-8 text-arr-muted">No catalog items found</div>
      ) : (
        <div className="bg-arr-card rounded-lg overflow-hidden">
          <table className="w-full">
            <thead>
              <tr className="border-b border-arr-primary">
                <th className="w-10 px-2 py-3"></th>
                <th className="w-10 px-2 py-3"></th>
                <th className="text-left px-4 py-3 text-arr-muted font-medium">Title</th>
                <th className="text-left px-4 py-3 text-arr-muted font-medium">State</th>
                <th className="text-right px-4 py-3 text-arr-muted font-medium">Size</th>
              </tr>
            </thead>
            <tbody>
              {filteredCatalog.map((group) => {
                const isSeries = group.type === 'Series' || group.type === 'Anime'
                const hasMultipleFiles = !isSeries && group.episodes.length > 1
                const expandableKey = isSeries ? (group.sonarrId ?? 0) : (group.radarrId ?? 0)
                const isExpanded = expandedGroups.has(expandableKey)
                const isLoadingEps = loadingEpisodes.has(expandableKey)
                const selState = getGroupSelectionState(group)

                return (
                  <CatalogGroupRow
                    key={group.sonarrId ?? group.radarrId ?? group.title}
                    group={group}
                    isSeries={isSeries}
                    hasMultipleFiles={hasMultipleFiles}
                    isExpanded={isExpanded}
                    isLoading={isLoadingEps}
                    selectionState={selState}
                    selectedEpisodes={selectedEpisodes}
                    onToggleGroup={toggleGroup}
                    onToggleAllInGroup={toggleAllInGroup}
                    onToggleEpisode={toggleEpisode}
                  />
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Bulk Action Bar */}
      <BulkActionBar
        selectedCount={selectedEpisodes.size}
        hasEligibleForMkv={hasEligibleForMkv}
        hasEligibleForSymlink={hasEligibleForSymlink}
        onConvertToMkv={handleConvertToMkv}
        onConvertToSymlink={handleConvertToSymlink}
        onManage={handleManage}
        onDeselectAll={() => setSelectedEpisodes(new Map())}
      />

      {/* Confirmation Dialog */}
      <ConfirmationDialog
        open={confirmDialog !== null}
        title={confirmDialog && confirmDialog.action === 'toMkv' ? `Convert ${confirmDialog.items.length} items to MKV` : `Convert ${confirmDialog?.items?.length ?? 0} items to Symlink`}
        items={confirmDialog?.items ?? []}
        action={confirmDialog?.action ?? 'toMkv'}
        onConfirm={handleConfirm}
        onCancel={() => setConfirmDialog(null)}
      />

      {/* Manage Modal */}
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
    </div>
  )
}

// --- Sub-component for group/episode rows ---

interface CatalogGroupRowProps {
  group: CatalogGroupDto
  isSeries: boolean
  hasMultipleFiles: boolean
  isExpanded: boolean
  isLoading: boolean
  selectionState: 'none' | 'some' | 'all'
  selectedEpisodes: Map<string, { ep: CatalogEpisodeDto; group: CatalogGroupDto }>
  onToggleGroup: (sonarrId: number) => void
  onToggleAllInGroup: (group: CatalogGroupDto) => void
  onToggleEpisode: (ep: CatalogEpisodeDto, group: CatalogGroupDto) => void
}

function CatalogGroupRow({
  group,
  isSeries,
  hasMultipleFiles,
  isExpanded,
  isLoading,
  selectionState,
  selectedEpisodes,
  onToggleGroup,
  onToggleAllInGroup,
  onToggleEpisode,
}: CatalogGroupRowProps) {
  // State breakdown as colored pills
  const statePills = Object.entries(group.stateBreakdown).map(([state, count]) => (
    <span
      key={state}
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs ${getStateColor(state)}`}
    >
      {state}: {count}
    </span>
  ))

  return (
    <>
      {/* Group header row */}
      <tr
        className={`border-b border-arr-primary hover:bg-arr-primary/50 transition-colors ${
          group.isExcluded ? 'opacity-60' : ''
        }`}
      >
        {/* Checkbox */}
        <td className="w-10 px-2 py-3 text-center">
          <input
            type="checkbox"
            checked={selectionState === 'all'}
            ref={(el) => {
              if (el) el.indeterminate = selectionState === 'some'
            }}
            onChange={() => onToggleAllInGroup(group)}
            disabled={group.type === 'Series' && group.totalEpisodes === 0 && group.episodes.length === 0}
            className="rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent cursor-pointer disabled:opacity-30"
          />
        </td>
        {/* Expand chevron */}
        <td className="w-10 px-2 py-3 text-center">
          {(isSeries || hasMultipleFiles) && (
            <button
              onClick={() => onToggleGroup(isSeries ? group.sonarrId! : group.radarrId!)}
              className="p-1 hover:bg-arr-primary rounded text-arr-muted transition-colors"
            >
              {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
            </button>
          )}
        </td>
        {/* Title */}
        <td className="px-4 py-3">
          <div className="flex items-center gap-3">
            <span className="font-medium truncate">{group.title}</span>
            {isSeries && (
              <span className="px-2 py-0.5 bg-arr-primary rounded text-xs text-arr-muted">
                {group.trackedEpisodes}/{group.totalEpisodes} eps
              </span>
            )}
            <span className="px-2 py-0.5 bg-arr-primary rounded text-xs text-arr-muted">
              {group.type}
            </span>
          </div>
        </td>
        {/* State breakdown */}
        <td className="px-4 py-3">
          <div className="flex items-center gap-1 flex-wrap">{statePills}</div>
        </td>
        {/* Size */}
        <td className="px-4 py-3 text-right text-arr-muted">{group.formattedSize}</td>
      </tr>

      {/* Expanded episode/file rows */}
      {(isSeries || hasMultipleFiles) &&
        isExpanded &&
        (isLoading ? (
          <tr className="border-b border-arr-primary/50">
            <td colSpan={5} className="px-4 py-6 text-center">
              <Loader2 className="w-5 h-5 animate-spin text-arr-accent mx-auto" />
              <span className="text-sm text-arr-muted mt-2 block">Loading episodes...</span>
            </td>
          </tr>
        ) : (
          group.episodes.map((ep) => {
            const epKey = getEpisodeKey(ep, group)
            const isSelected = selectedEpisodes.has(epKey)
            const fileName = ep.filePath ? ep.filePath.split('/').pop() : ep.title
            return (
              <tr
                key={epKey}
                className={`border-b border-arr-primary/50 hover:bg-arr-primary/30 transition-colors ${
                  ep.isExcluded ? 'opacity-60' : ''
                }`}
              >
                <td className="w-10 px-2 py-2 text-center">
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => onToggleEpisode(ep, group)}
                    className="rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent cursor-pointer"
                  />
                </td>
                <td className="w-10 px-2 py-2"></td>
                <td className="px-4 py-2 pl-10">
                  {isSeries ? (
                    <>
                      <span className="text-arr-muted mr-2">
                        S{(ep.seasonNumber ?? 0).toString().padStart(2, '0')}E
                        {(ep.episodeNumber ?? 0).toString().padStart(2, '0')}
                      </span>
                      <span className="truncate">{ep.title}</span>
                    </>
                  ) : (
                    <span className="truncate text-sm" title={ep.filePath}>{fileName}</span>
                  )}
                </td>
                <td className="px-4 py-2">
                  <span
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs ${getStateColor(
                      ep.currentState
                    )}`}
                  >
                    {ep.currentState}
                  </span>
                </td>
                <td className="px-4 py-2 text-right text-arr-muted">{formatSize(ep.fileSize)}</td>
              </tr>
            )
          })
        ))}
    </>
  )
}
