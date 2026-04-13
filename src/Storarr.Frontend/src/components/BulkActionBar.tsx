import { Download, Link2 } from 'lucide-react'

interface BulkActionBarProps {
  selectedCount: number
  hasEligibleForMkv: boolean
  hasEligibleForSymlink: boolean
  onConvertToMkv: () => void
  onConvertToSymlink: () => void
}

export default function BulkActionBar({
  selectedCount,
  hasEligibleForMkv,
  hasEligibleForSymlink,
  onConvertToMkv,
  onConvertToSymlink,
}: BulkActionBarProps) {
  if (selectedCount === 0) return null

  return (
    <div className="fixed bottom-0 left-0 right-0 z-40 bg-arr-card border-t border-arr-primary">
      <div className="flex items-center justify-between px-6 py-3">
        <span className="text-arr-muted">
          {selectedCount} item{selectedCount !== 1 ? 's' : ''} selected
        </span>
        <div className="flex items-center gap-3">
          <button
            onClick={onConvertToMkv}
            disabled={!hasEligibleForMkv}
            className="bg-arr-success/20 hover:bg-arr-success/30 text-arr-success disabled:opacity-50 px-4 py-2 rounded-lg flex items-center gap-2 transition-colors"
          >
            <Download className="w-4 h-4" />
            Convert to MKV
          </button>
          <button
            onClick={onConvertToSymlink}
            disabled={!hasEligibleForSymlink}
            className="bg-blue-500/20 hover:bg-blue-500/30 text-blue-400 disabled:opacity-50 px-4 py-2 rounded-lg flex items-center gap-2 transition-colors"
          >
            <Link2 className="w-4 h-4" />
            Convert to Symlink
          </button>
        </div>
      </div>
    </div>
  )
}
