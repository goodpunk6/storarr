import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Clock } from 'lucide-react'
import { getActivity } from '../api/client'
import { ActivityLog } from '../stores/appStore'

const PAGE_SIZE = 50

export default function Activity() {
  const [logs, setLogs] = useState<ActivityLog[]>([])
  const [loading, setLoading] = useState(true)
  const [page, setPage] = useState(1)
  const [totalCount, setTotalCount] = useState(0)

  useEffect(() => {
    const fetchActivity = async () => {
      try {
        const response = await getActivity({ page, pageSize: PAGE_SIZE })
        setLogs(response.data.items)
        setTotalCount(response.data.totalCount)
      } catch (error) {
        console.error('Failed to fetch activity:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchActivity()
  }, [page])

  const formatDate = (date: string) => {
    const d = new Date(date)
    return d.toLocaleString()
  }

  const getActionColor = (action: string) => {
    switch (action.toLowerCase()) {
      case 'transitiontomkv':
        return 'bg-arr-warning/20 text-arr-warning'
      case 'transitiontosymlink':
        return 'bg-arr-success/20 text-arr-success'
      case 'downloadcomplete':
        return 'bg-blue-500/20 text-blue-400'
      default:
        return 'bg-arr-primary text-arr-muted'
    }
  }

  const totalPages = Math.ceil(totalCount / PAGE_SIZE)

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <h2 className="text-2xl font-bold">Activity Log</h2>

      <div className="bg-arr-card rounded-lg overflow-hidden">
        {logs.length === 0 ? (
          <div className="p-8 text-center">
            <Clock className="mx-auto text-arr-muted mb-4" size={48} />
            <p className="text-arr-muted">No activity recorded yet</p>
          </div>
        ) : (
          <div className="divide-y divide-arr-primary">
            {logs.map((log) => (
              <div key={log.id} className="p-4">
                <div className="flex items-start justify-between mb-2">
                  <div>
                    {log.mediaTitle && (
                      <Link
                        to={`/media/${log.mediaItemId}`}
                        className="font-medium hover:text-arr-accent"
                      >
                        {log.mediaTitle}
                      </Link>
                    )}
                    <div className="flex items-center gap-2 mt-1">
                      <span className={`px-2 py-0.5 rounded text-xs ${getActionColor(log.action)}`}>
                        {log.action}
                      </span>
                      <span className="text-sm text-arr-muted">
                        {log.fromState} → {log.toState}
                      </span>
                    </div>
                  </div>
                  <span className="text-sm text-arr-muted">{formatDate(log.timestamp)}</span>
                </div>
                {log.details && (
                  <p className="text-sm text-arr-muted mt-2">{log.details}</p>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Pagination */}
      <div className="flex justify-center gap-4">
        <button
          onClick={() => setPage(p => Math.max(1, p - 1))}
          disabled={page === 1}
          className="px-4 py-2 bg-arr-card rounded-lg disabled:opacity-50 hover:bg-arr-primary transition-colors"
        >
          Previous
        </button>
        <span className="px-4 py-2 text-arr-muted">
          Page {page}{totalPages > 0 ? ` of ${totalPages}` : ''}
        </span>
        <button
          onClick={() => setPage(p => p + 1)}
          disabled={page >= totalPages}
          className="px-4 py-2 bg-arr-card rounded-lg disabled:opacity-50 hover:bg-arr-primary transition-colors"
        >
          Next
        </button>
      </div>
    </div>
  )
}
