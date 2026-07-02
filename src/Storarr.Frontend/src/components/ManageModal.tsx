import { useState } from 'react'
import { CatalogEpisodeDto, CatalogGroupDto } from '../stores/appStore'
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
  const [addToExclusions, setAddToExclusions] = useState(false)
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

  const hasAction = deleteFiles || removeFromArr || unmonitor || reMonitor || addToExclusions
  const monitorDisabled = removeFromArr || addToExclusions

  const handleUnmonitor = (checked: boolean) => {
    setUnmonitor(checked)
    if (checked) setReMonitor(false)
  }
  const handleReMonitor = (checked: boolean) => {
    setReMonitor(checked)
    if (checked) setUnmonitor(false)
  }

  const getButtonText = () => {
    const parts: string[] = []
    if (deleteFiles) parts.push(`Delete ${mediaItemIds.length} file${mediaItemIds.length !== 1 ? 's' : ''}`)
    if (removeFromArr) parts.push('remove from Sonarr/Radarr')
    if (unmonitor) parts.push('unmonitor')
    if (reMonitor) parts.push('set to monitored')
    if (addToExclusions) parts.push('exclude from Storarr')
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
        addToExclusions,
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
    setAddToExclusions(false)
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
          <div className="mb-4 text-sm text-arr-text/70">
            <span className="font-medium text-arr-text">Selected:</span>{' '}
            {displayTitles.map((t, i) => (
              <span key={i}>{i > 0 ? ', ' : ''}{t}</span>
            ))}
            {extraCount > 0 && <span> +{extraCount} more</span>}
          </div>

          {actionState === 'idle' && (
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

              <label className="flex items-center gap-3 cursor-pointer">
                <input type="checkbox" checked={addToExclusions} onChange={(e) => setAddToExclusions(e.target.checked)}
                  className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                <span className="text-arr-text">Add to Exclusions</span>
                <span className="text-xs text-arr-text/40">(stop managing entirely — blocks future scans)</span>
              </label>

              <label className={`flex items-center gap-3 ${monitorDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}>
                <input type="checkbox" checked={unmonitor} onChange={(e) => handleUnmonitor(e.target.checked)}
                  disabled={monitorDisabled}
                  className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                <span className="text-arr-text">Set to Unmonitored</span>
                {monitorDisabled && <span className="text-xs text-arr-text/40">(not available when removing/excluding)</span>}
              </label>

              <label className={`flex items-center gap-3 ${monitorDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}>
                <input type="checkbox" checked={reMonitor} onChange={(e) => handleReMonitor(e.target.checked)}
                  disabled={monitorDisabled}
                  className="w-4 h-4 rounded border-arr-primary bg-arr-bg text-arr-accent focus:ring-arr-accent" />
                <span className="text-arr-text">Set to Monitored</span>
                {monitorDisabled && <span className="text-xs text-arr-text/40">(not available when removing/excluding)</span>}
              </label>
            </div>
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
