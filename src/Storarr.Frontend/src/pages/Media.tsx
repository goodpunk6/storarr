import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Search, Link2, HardDrive, Download, Clock, Play, Pause, RefreshCw } from 'lucide-react'
import { getMedia, forceDownload, forceSymlink, toggleExcluded } from '../api/client'
import { MediaItem } from '../stores/appStore'

export default function Media() {
  const [items, setItems] = useState<MediaItem[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [stateFilter, setStateFilter] = useState<string>('')
  const [typeFilter, setTypeFilter] = useState<string>('')
  const [actionLoading, setActionLoading] = useState<number | null>(null)

  const fetchMedia = async () => {
    try {
      const response = await getMedia({
        search: search || undefined,
        state: stateFilter || undefined,
        type: typeFilter || undefined,
      })
      setItems(response.data)
    } catch (error) {
      console.error('Failed to fetch media:', error)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    fetchMedia()
  }, [search, stateFilter, typeFilter])

  const handleForceDownload = async (id: number) => {
    setActionLoading(id)
    try {
      await forceDownload(id)
      await fetchMedia()
    } catch (error) {
      console.error('Failed to force download:', error)
      alert('Failed to trigger download')
    } finally {
      setActionLoading(null)
    }
  }

  const handleForceSymlink = async (id: number) => {
    setActionLoading(id)
    try {
      await forceSymlink(id)
      await fetchMedia()
    } catch (error) {
      console.error('Failed to force symlink:', error)
      alert('Failed to trigger symlink restoration')
    } finally {
      setActionLoading(null)
    }
  }

  const handleToggleExcluded = async (id: number, _currentExcluded: boolean) => {
    setActionLoading(id)
    try {
      await toggleExcluded(id)
      await fetchMedia()
    } catch (error) {
      console.error('Failed to toggle exclusion:', error)
      alert('Failed to update exclusion status')
    } finally {
      setActionLoading(null)
    }
  }

  const getStateIcon = (state: string) => {
    switch (state.toLowerCase()) {
      case 'symlink':
        return <Link2 size={16} className="text-arr-success" />
      case 'mkv':
        return <HardDrive size={16} className="text-arr-warning" />
      case 'downloading':
        return <Download size={16} className="text-blue-400" />
      default:
        return <Clock size={16} className="text-arr-muted" />
    }
  }

  const getStateColor = (state: string) => {
    switch (state.toLowerCase()) {
      case 'symlink':
        return 'bg-arr-success/20 text-arr-success'
      case 'mkv':
        return 'bg-arr-warning/20 text-arr-warning'
      case 'downloading':
        return 'bg-blue-500/20 text-blue-400'
      default:
        return 'bg-arr-primary text-arr-muted'
    }
  }

  const formatSize = (bytes?: number) => {
    if (!bytes) return '-'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <h2 className="text-2xl font-bold">Media</h2>

      {/* Filters */}
      <div className="bg-arr-card rounded-lg p-4 flex flex-wrap gap-4">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-arr-muted" size={20} />
          <input
            type="text"
            placeholder="Search media..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full bg-arr-bg border border-arr-primary rounded-lg pl-10 pr-4 py-2 focus:outline-none focus:border-arr-accent"
          />
        </div>
        <select
          value={stateFilter}
          onChange={(e) => setStateFilter(e.target.value)}
          className="bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
        >
          <option value="">All States</option>
          <option value="Symlink">Symlink</option>
          <option value="Mkv">MKV</option>
          <option value="Downloading">Downloading</option>
          <option value="PendingSymlink">Pending Symlink</option>
        </select>
        <select
          value={typeFilter}
          onChange={(e) => setTypeFilter(e.target.value)}
          className="bg-arr-bg border border-arr-primary rounded-lg px-4 py-2 focus:outline-none focus:border-arr-accent"
        >
          <option value="">All Types</option>
          <option value="Movie">Movies</option>
          <option value="Series">Series</option>
          <option value="Anime">Anime</option>
        </select>
      </div>

      {/* Media List */}
      <div className="bg-arr-card rounded-lg overflow-hidden">
        <table className="w-full">
          <thead>
            <tr className="border-b border-arr-primary">
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Title</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Type</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">State</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Size</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Transition</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr
                key={item.id}
                className={`border-b border-arr-primary last:border-0 hover:bg-arr-primary/50 transition-colors ${item.isExcluded ? 'opacity-60' : ''}`}
              >
                <td className="px-4 py-3">
                  <Link to={`/media/${item.id}`} className="hover:text-arr-accent">
                    {item.title}
                    {item.seasonNumber && item.episodeNumber && (
                      <span className="text-arr-muted ml-2">
                        S{item.seasonNumber.toString().padStart(2, '0')}E{item.episodeNumber.toString().padStart(2, '0')}
                      </span>
                    )}
                    {item.isExcluded && (
                      <span className="ml-2 px-1 py-0.5 bg-red-500/20 text-red-400 text-xs rounded">
                        EXCLUDED
                      </span>
                    )}
                  </Link>
                </td>
                <td className="px-4 py-3">
                  <span className="px-2 py-1 bg-arr-primary rounded text-sm">{item.type}</span>
                </td>
                <td className="px-4 py-3">
                  <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-sm ${getStateColor(item.currentState)}`}>
                    {getStateIcon(item.currentState)}
                    {item.currentState}
                  </span>
                </td>
                <td className="px-4 py-3 text-arr-muted">{formatSize(item.fileSize)}</td>
                <td className="px-4 py-3 text-arr-muted">
                  {item.isExcluded ? (
                    <span className="text-red-400">Paused</span>
                  ) : item.daysUntilTransition !== null && item.daysUntilTransition !== undefined ? (
                    item.daysUntilTransition === 0 ? 'Now' :
                    item.daysUntilTransition === 1 ? '1 day' :
                    `${item.daysUntilTransition} days`
                  ) : '-'}
                </td>
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    {/* Force Download Button - for symlinks */}
                    {(item.currentState === 'Symlink' || item.currentState === 'PendingSymlink') && (
                      <button
                        onClick={() => handleForceDownload(item.id)}
                        disabled={actionLoading === item.id}
                        className="p-1.5 bg-arr-success/20 hover:bg-arr-success/30 rounded text-arr-success disabled:opacity-50"
                        title="Force Download (convert to MKV)"
                      >
                        {actionLoading === item.id ? (
                          <RefreshCw size={16} className="animate-spin" />
                        ) : (
                          <Download size={16} />
                        )}
                      </button>
                    )}

                    {/* Force Symlink Button - for MKVs */}
                    {(item.currentState === 'Mkv' || item.currentState === 'Downloading') && (
                      <button
                        onClick={() => handleForceSymlink(item.id)}
                        disabled={actionLoading === item.id}
                        className="p-1.5 bg-blue-500/20 hover:bg-blue-500/30 rounded text-blue-400 disabled:opacity-50"
                        title="Force Symlink (convert to streaming)"
                      >
                        {actionLoading === item.id ? (
                          <RefreshCw size={16} className="animate-spin" />
                        ) : (
                          <Link2 size={16} />
                        )}
                      </button>
                    )}

                    {/* Exclude/Include Toggle */}
                    <button
                      onClick={() => handleToggleExcluded(item.id, item.isExcluded)}
                      disabled={actionLoading === item.id}
                      className={`p-1.5 rounded disabled:opacity-50 ${
                        item.isExcluded
                          ? 'bg-arr-success/20 hover:bg-arr-success/30 text-arr-success'
                          : 'bg-red-500/20 hover:bg-red-500/30 text-red-400'
                      }`}
                      title={item.isExcluded ? 'Include in auto-transitions' : 'Exclude from auto-transitions'}
                    >
                      {actionLoading === item.id ? (
                        <RefreshCw size={16} className="animate-spin" />
                      ) : item.isExcluded ? (
                        <Play size={16} />
                      ) : (
                        <Pause size={16} />
                      )}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {items.length === 0 && (
          <div className="text-center py-8 text-arr-muted">No media items found</div>
        )}
      </div>
    </div>
  )
}
