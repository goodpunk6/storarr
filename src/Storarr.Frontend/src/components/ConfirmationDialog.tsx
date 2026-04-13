import { X, AlertTriangle, Link2, HardDrive, Download, Clock } from 'lucide-react'
import { CatalogEpisodeDto } from '../stores/appStore'

interface ConfirmationDialogProps {
  open: boolean
  title: string
  items: CatalogEpisodeDto[]
  action: 'toMkv' | 'toSymlink'
  onConfirm: () => void
  onCancel: () => void
}

function isEligible(item: CatalogEpisodeDto, action: 'toMkv' | 'toSymlink'): boolean {
  const state = item.currentState.toLowerCase()
  if (action === 'toMkv') {
    return state === 'symlink' || state === 'pendingsymlink' || state === 'untracked'
  }
  return state === 'mkv' || state === 'downloading'
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

function getStateIcon(state: string) {
  switch (state.toLowerCase()) {
    case 'symlink':
      return <Link2 size={14} className="text-arr-success" />
    case 'mkv':
      return <HardDrive size={14} className="text-arr-warning" />
    case 'downloading':
      return <Download size={14} className="text-blue-400" />
    default:
      return <Clock size={14} className="text-arr-muted" />
  }
}

export default function ConfirmationDialog({
  open,
  title,
  items,
  action,
  onConfirm,
  onCancel,
}: ConfirmationDialogProps) {
  if (!open) return null

  const eligible = items.filter((item) => isEligible(item, action))
  const ineligible = items.filter((item) => !isEligible(item, action))

  return (
    <div
      className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4"
      onClick={onCancel}
    >
      <div
        className="bg-arr-card rounded-lg max-w-lg w-full shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-arr-primary">
          <h3 className="text-lg font-semibold">{title}</h3>
          <button
            onClick={onCancel}
            className="p-1 hover:bg-arr-primary rounded text-arr-muted"
          >
            <X size={20} />
          </button>
        </div>

        {/* Summary */}
        <div className="px-6 py-3 text-sm text-arr-muted">
          <span className="text-arr-text font-medium">{eligible.length}</span> eligible
          {ineligible.length > 0 && (
            <>
              {' / '}
              <span className="text-arr-muted">{ineligible.length}</span> not eligible
            </>
          )}
        </div>

        {/* Scrollable item list */}
        <div className="max-h-64 overflow-y-auto px-6">
          {items.map((item, index) => {
            const eligibleItem = isEligible(item, action)
            return (
              <div
                key={item.mediaItemId ?? index}
                className={`flex items-center justify-between py-2 border-b border-arr-primary/50 last:border-0 ${
                  !eligibleItem ? 'opacity-40' : ''
                }`}
              >
                <div className="flex items-center gap-2 min-w-0 flex-1">
                  {!eligibleItem && (
                    <AlertTriangle size={14} className="text-arr-muted shrink-0" />
                  )}
                  <span className="truncate">{item.title}</span>
                </div>
                <div className="flex items-center gap-2 shrink-0 ml-3">
                  <span
                    className={`inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs ${getStateColor(item.currentState)}`}
                  >
                    {getStateIcon(item.currentState)}
                    {item.currentState}
                  </span>
                  {!eligibleItem && (
                    <span className="text-xs text-arr-muted">(not eligible)</span>
                  )}
                </div>
              </div>
            )
          })}
        </div>

        {/* Footer with buttons */}
        <div className="flex items-center justify-end gap-3 px-6 py-4 border-t border-arr-primary">
          <button
            onClick={onCancel}
            className="px-4 py-2 bg-arr-primary hover:bg-arr-primary/80 rounded-lg text-arr-muted transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={eligible.length === 0}
            className="px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg text-white transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Confirm
          </button>
        </div>
      </div>
    </div>
  )
}
