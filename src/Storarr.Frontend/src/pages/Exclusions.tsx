import { useCallback, useEffect, useState } from 'react'
import { Search, Trash2, Ban, Plus, X, AlertTriangle, RefreshCw } from 'lucide-react'
import { getExclusions, createExclusion, deleteExclusion } from '../api/client'
import { ExcludedItem } from '../stores/appStore'

export default function Exclusions() {
  const [items, setItems] = useState<ExcludedItem[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState<string>('')
  const [actionLoading, setActionLoading] = useState<number | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [addError, setAddError] = useState<string | null>(null)

  // Form state for adding new exclusion
  const [newExclusion, setNewExclusion] = useState({
    title: '',
    type: 'Series' as 'Movie' | 'Series' | 'Anime',
    sonarrId: '',
    radarrId: '',
    tmdbId: '',
    tvdbId: '',
    reason: ''
  })

  const fetchExclusions = useCallback(async () => {
    try {
      const response = await getExclusions({
        search: search || undefined,
        type: typeFilter || undefined,
      })
      setItems(response.data)
    } catch (error) {
      console.error('Failed to fetch exclusions:', error)
    } finally {
      setLoading(false)
    }
  }, [search, typeFilter])

  useEffect(() => {
    fetchExclusions()
  }, [fetchExclusions])

  const handleDelete = async (id: number, title: string) => {
    if (!confirm(`Remove exclusion for "${title}"? The item will be processed in the next library scan.`)) return

    setActionLoading(id)
    try {
      await deleteExclusion(id)
      await fetchExclusions()
    } catch (error) {
      console.error('Failed to delete exclusion:', error)
      alert('Failed to remove exclusion')
    } finally {
      setActionLoading(null)
    }
  }

  const handleAddExclusion = async (e: React.FormEvent) => {
    e.preventDefault()
    setAddError(null)

    if (!newExclusion.title.trim()) {
      setAddError('Title is required')
      return
    }

    try {
      const data: any = {
        title: newExclusion.title,
        type: newExclusion.type,
        reason: newExclusion.reason || undefined
      }

      // Parse IDs
      if (newExclusion.sonarrId) data.sonarrId = parseInt(newExclusion.sonarrId)
      if (newExclusion.radarrId) data.radarrId = parseInt(newExclusion.radarrId)
      if (newExclusion.tmdbId) data.tmdbId = parseInt(newExclusion.tmdbId)
      if (newExclusion.tvdbId) data.tvdbId = parseInt(newExclusion.tvdbId)

      await createExclusion(data)
      setShowAddModal(false)
      setNewExclusion({
        title: '',
        type: 'Series',
        sonarrId: '',
        radarrId: '',
        tmdbId: '',
        tvdbId: '',
        reason: ''
      })
      await fetchExclusions()
    } catch (error: any) {
      console.error('Failed to add exclusion:', error)
      const msg = error.response?.data?.error || 'Failed to add exclusion'
      setAddError(msg)
    }
  }

  const formatDate = (date: string) => {
    return new Date(date).toLocaleDateString()
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
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold">Exclusions</h2>
        <button
          onClick={() => setShowAddModal(true)}
          className="flex items-center gap-2 px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg transition-colors"
        >
          <Plus size={20} />
          Add Exclusion
        </button>
      </div>

      {/* Info Box */}
      <div className="bg-blue-500/10 border border-blue-500/30 rounded-lg p-4">
        <div className="flex items-start gap-3">
          <AlertTriangle className="text-blue-400 flex-shrink-0 mt-0.5" size={20} />
          <div className="text-sm text-blue-200">
            <p className="font-medium mb-1">About Exclusions</p>
            <p className="text-blue-300">
              Excluded series or movies are completely skipped by Storarr. They will not be tracked,
              monitored for watch status, or processed for any transitions. Existing media items
              matching an exclusion are automatically removed.
            </p>
          </div>
        </div>
      </div>

      {/* Filters */}
      <div className="bg-arr-card rounded-lg p-4 flex flex-wrap gap-4">
        <div className="relative flex-1 min-w-[200px]">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-arr-muted" size={20} />
          <input
            type="text"
            placeholder="Search exclusions..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full bg-arr-bg border border-arr-primary rounded-lg pl-10 pr-4 py-2 focus:outline-none focus:border-arr-accent"
          />
        </div>
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

      {/* Exclusions List */}
      <div className="bg-arr-card rounded-lg overflow-hidden">
        <table className="w-full">
          <thead>
            <tr className="border-b border-arr-primary">
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Title</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Type</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">IDs</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Reason</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Added</th>
              <th className="text-left px-4 py-3 text-arr-muted font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr
                key={item.id}
                className="border-b border-arr-primary last:border-0 hover:bg-arr-primary/50 transition-colors"
              >
                <td className="px-4 py-3">
                  <div className="flex items-center gap-2">
                    <Ban size={16} className="text-red-400" />
                    {item.title}
                  </div>
                </td>
                <td className="px-4 py-3">
                  <span className="px-2 py-1 bg-arr-primary rounded text-sm">{item.type}</span>
                </td>
                <td className="px-4 py-3 text-arr-muted text-sm font-mono">
                  {item.sonarrId && <span className="mr-2">Sonarr: {item.sonarrId}</span>}
                  {item.radarrId && <span className="mr-2">Radarr: {item.radarrId}</span>}
                  {item.tmdbId && <span className="mr-2">TMDB: {item.tmdbId}</span>}
                  {item.tvdbId && <span className="mr-2">TVDB: {item.tvdbId}</span>}
                  {!item.sonarrId && !item.radarrId && !item.tmdbId && !item.tvdbId && '-'}
                </td>
                <td className="px-4 py-3 text-arr-muted text-sm">
                  {item.reason || '-'}
                </td>
                <td className="px-4 py-3 text-arr-muted text-sm">
                  {formatDate(item.createdAt)}
                </td>
                <td className="px-4 py-3">
                  <button
                    onClick={() => handleDelete(item.id, item.title)}
                    disabled={actionLoading === item.id}
                    className="p-1.5 bg-arr-success/20 hover:bg-arr-success/30 rounded text-arr-success disabled:opacity-50"
                    title="Remove exclusion (allow processing)"
                  >
                    {actionLoading === item.id ? (
                      <RefreshCw size={16} className="animate-spin" />
                    ) : (
                      <Trash2 size={16} />
                    )}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {items.length === 0 && (
          <div className="text-center py-8 text-arr-muted">
            No exclusions configured. Add an exclusion to prevent Storarr from processing specific series or movies.
          </div>
        )}
      </div>

      {/* Add Exclusion Modal */}
      {showAddModal && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-arr-card rounded-lg p-6 w-full max-w-md mx-4">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold">Add Exclusion</h3>
              <button
                onClick={() => {
                  setShowAddModal(false)
                  setAddError(null)
                }}
                className="p-1 hover:bg-arr-primary rounded"
              >
                <X size={20} />
              </button>
            </div>

            <form onSubmit={handleAddExclusion} className="space-y-4">
              {addError && (
                <div className="bg-red-500/20 text-red-400 rounded p-3 text-sm">
                  {addError}
                </div>
              )}

              <div>
                <label className="block text-sm text-arr-muted mb-1">Title *</label>
                <input
                  type="text"
                  value={newExclusion.title}
                  onChange={(e) => setNewExclusion({ ...newExclusion, title: e.target.value })}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                  placeholder="Series or Movie title"
                  required
                />
              </div>

              <div>
                <label className="block text-sm text-arr-muted mb-1">Type</label>
                <select
                  value={newExclusion.type}
                  onChange={(e) => setNewExclusion({ ...newExclusion, type: e.target.value as any })}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                >
                  <option value="Series">Series</option>
                  <option value="Movie">Movie</option>
                  <option value="Anime">Anime</option>
                </select>
              </div>

              <div className="grid grid-cols-2 gap-4">
                {(newExclusion.type === 'Series' || newExclusion.type === 'Anime') && (
                  <div>
                    <label className="block text-sm text-arr-muted mb-1">Sonarr ID</label>
                    <input
                      type="number"
                      value={newExclusion.sonarrId}
                      onChange={(e) => setNewExclusion({ ...newExclusion, sonarrId: e.target.value })}
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                      placeholder="e.g. 42"
                    />
                  </div>
                )}
                {newExclusion.type === 'Movie' && (
                  <div>
                    <label className="block text-sm text-arr-muted mb-1">Radarr ID</label>
                    <input
                      type="number"
                      value={newExclusion.radarrId}
                      onChange={(e) => setNewExclusion({ ...newExclusion, radarrId: e.target.value })}
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                      placeholder="e.g. 42"
                    />
                  </div>
                )}
                <div>
                  <label className="block text-sm text-arr-muted mb-1">TMDB ID</label>
                  <input
                    type="number"
                    value={newExclusion.tmdbId}
                    onChange={(e) => setNewExclusion({ ...newExclusion, tmdbId: e.target.value })}
                    className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                    placeholder="e.g. 12345"
                  />
                </div>
                {(newExclusion.type === 'Series' || newExclusion.type === 'Anime') && (
                  <div>
                    <label className="block text-sm text-arr-muted mb-1">TVDB ID</label>
                    <input
                      type="number"
                      value={newExclusion.tvdbId}
                      onChange={(e) => setNewExclusion({ ...newExclusion, tvdbId: e.target.value })}
                      className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                      placeholder="e.g. 12345"
                    />
                  </div>
                )}
              </div>

              <div>
                <label className="block text-sm text-arr-muted mb-1">Reason (optional)</label>
                <input
                  type="text"
                  value={newExclusion.reason}
                  onChange={(e) => setNewExclusion({ ...newExclusion, reason: e.target.value })}
                  className="w-full bg-arr-bg border border-arr-primary rounded-lg px-3 py-2 focus:outline-none focus:border-arr-accent"
                  placeholder="Why is this excluded?"
                />
              </div>

              <div className="flex gap-3 pt-2">
                <button
                  type="button"
                  onClick={() => {
                    setShowAddModal(false)
                    setAddError(null)
                  }}
                  className="flex-1 px-4 py-2 bg-arr-primary hover:bg-arr-primary/80 rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="flex-1 px-4 py-2 bg-arr-accent hover:bg-arr-accent/80 rounded-lg transition-colors"
                >
                  Add Exclusion
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}
