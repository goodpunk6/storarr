import { useEffect, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { ArrowLeft, Download, Link2, Trash2, Ban, Play, Pause } from 'lucide-react'
import { getMediaItem, forceDownload, forceSymlink, deleteMedia, toggleExcluded, excludeByArrId } from '../api/client'
import { MediaItem } from '../stores/appStore'

export default function MediaDetail() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [item, setItem] = useState<MediaItem | null>(null)
  const [loading, setLoading] = useState(true)
  const [actionLoading, setActionLoading] = useState(false)

  useEffect(() => {
    const fetchItem = async () => {
      if (!id) return
      try {
        const response = await getMediaItem(parseInt(id))
        setItem(response.data)
      } catch (error) {
        console.error('Failed to fetch media item:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchItem()
  }, [id])

  const handleForceDownload = async () => {
    if (!item || actionLoading) return
    setActionLoading(true)
    try {
      await forceDownload(item.id)
      const response = await getMediaItem(item.id)
      setItem(response.data)
    } catch (error) {
      console.error('Failed to force download:', error)
    } finally {
      setActionLoading(false)
    }
  }

  const handleForceSymlink = async () => {
    if (!item || actionLoading) return
    setActionLoading(true)
    try {
      await forceSymlink(item.id)
      const response = await getMediaItem(item.id)
      setItem(response.data)
    } catch (error) {
      console.error('Failed to force symlink:', error)
    } finally {
      setActionLoading(false)
    }
  }

  const handleDelete = async () => {
    if (!item || actionLoading) return
    if (!confirm('Are you sure you want to remove this item from tracking?')) return
    setActionLoading(true)
    try {
      await deleteMedia(item.id)
      navigate('/media')
    } catch (error) {
      console.error('Failed to delete:', error)
    } finally {
      setActionLoading(false)
    }
  }

  const handleToggleExcluded = async () => {
    if (!item || actionLoading) return
    setActionLoading(true)
    try {
      await toggleExcluded(item.id)
      const response = await getMediaItem(item.id)
      setItem(response.data)
    } catch (error) {
      console.error('Failed to toggle exclusion:', error)
    } finally {
      setActionLoading(false)
    }
  }

  const handleExcludeEntireSeries = async () => {
    if (!item || actionLoading) return
    const confirmMsg = item.type === 'Movie'
      ? `Exclude "${item.title}" from the entire Storarr process?\n\nThis will remove all tracked files for this movie and prevent any future tracking.`
      : `Exclude the entire "${item.title}" series from the Storarr process?\n\nThis will remove all tracked episodes and prevent any future tracking.`
    if (!confirm(confirmMsg)) return

    setActionLoading(true)
    try {
      await excludeByArrId({
        sonarrId: item.sonarrId,
        radarrId: item.radarrId,
        tmdbId: item.tmdbId,
        tvdbId: item.tvdbId,
        title: item.title,
        type: item.type,
        reason: 'Excluded via media detail page'
      })
      navigate('/media')
    } catch (error: any) {
      console.error('Failed to exclude:', error)
      alert(error.response?.data?.error || 'Failed to create exclusion')
    } finally {
      setActionLoading(false)
    }
  }

  const formatSize = (bytes?: number) => {
    if (!bytes) return '-'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  const formatDate = (date?: string) => {
    if (!date) return '-'
    return new Date(date).toLocaleString()
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  if (!item) {
    return (
      <div className="text-center py-8">
        <p className="text-arr-muted">Media item not found</p>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <button
          onClick={() => navigate('/media')}
          className="p-2 hover:bg-arr-primary rounded-lg transition-colors"
        >
          <ArrowLeft size={24} />
        </button>
        <h2 className="text-2xl font-bold">{item.title}</h2>
      </div>

      {/* Details */}
      <div className="bg-arr-card rounded-lg p-6">
        <h3 className="text-lg font-semibold mb-4">Details</h3>
        <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <dt className="text-arr-muted text-sm">Type</dt>
            <dd className="text-lg">{item.type}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">Current State</dt>
            <dd className="text-lg">{item.currentState}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">File Path</dt>
            <dd className="text-lg font-mono text-sm break-all">{item.filePath}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">File Size</dt>
            <dd className="text-lg">{formatSize(item.fileSize)}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">Created</dt>
            <dd className="text-lg">{formatDate(item.createdAt)}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">Last Watched</dt>
            <dd className="text-lg">{formatDate(item.lastWatchedAt)}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">State Changed</dt>
            <dd className="text-lg">{formatDate(item.stateChangedAt)}</dd>
          </div>
          <div>
            <dt className="text-arr-muted text-sm">Days Until Transition</dt>
            <dd className="text-lg">{item.daysUntilTransition ?? '-'}</dd>
          </div>
        </dl>
      </div>

      {/* Actions */}
      <div className="bg-arr-card rounded-lg p-6">
        <h3 className="text-lg font-semibold mb-4">Actions</h3>
        <div className="flex flex-wrap gap-4">
          {(item.currentState === 'Symlink' || item.currentState === 'PendingSymlink') && (
            <button
              onClick={handleForceDownload}
              disabled={actionLoading}
              className="flex items-center gap-2 px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg transition-colors disabled:opacity-50"
            >
              <Download size={20} />
              Force Download
            </button>
          )}
          {(item.currentState === 'Mkv' || item.currentState === 'Downloading') && (
            <button
              onClick={handleForceSymlink}
              disabled={actionLoading}
              className="flex items-center gap-2 px-4 py-2 bg-arr-success/20 text-arr-success hover:bg-arr-success/30 rounded-lg transition-colors disabled:opacity-50"
            >
              <Link2 size={20} />
              Restore Symlink
            </button>
          )}
          <button
            onClick={handleToggleExcluded}
            disabled={actionLoading}
            className={`flex items-center gap-2 px-4 py-2 rounded-lg transition-colors disabled:opacity-50 ${
              item.isExcluded
                ? 'bg-arr-success/20 text-arr-success hover:bg-arr-success/30'
                : 'bg-yellow-500/20 text-yellow-400 hover:bg-yellow-500/30'
            }`}
          >
            {item.isExcluded ? <Play size={20} /> : <Pause size={20} />}
            {item.isExcluded ? 'Resume Transitions' : 'Pause Transitions'}
          </button>
          <button
            onClick={handleDelete}
            disabled={actionLoading}
            className="flex items-center gap-2 px-4 py-2 bg-arr-danger/20 text-arr-danger hover:bg-arr-danger/30 rounded-lg transition-colors disabled:opacity-50"
          >
            <Trash2 size={20} />
            Remove from Tracking
          </button>
        </div>
      </div>

      {/* Exclude Entire Series/Movie */}
      <div className="bg-red-500/10 border border-red-500/30 rounded-lg p-6">
        <h3 className="text-lg font-semibold mb-2 text-red-400">Danger Zone</h3>
        <p className="text-sm text-arr-muted mb-4">
          Completely exclude this {item.type === 'Movie' ? 'movie' : 'series'} from Storarr.
          All files will be removed from tracking and future library scans will skip this content.
        </p>
        <button
          onClick={handleExcludeEntireSeries}
          disabled={actionLoading}
          className="flex items-center gap-2 px-4 py-2 bg-red-500/20 text-red-400 hover:bg-red-500/30 rounded-lg transition-colors disabled:opacity-50"
        >
          <Ban size={20} />
          Exclude Entire {item.type === 'Movie' ? 'Movie' : 'Series'}
        </button>
      </div>
    </div>
  )
}
