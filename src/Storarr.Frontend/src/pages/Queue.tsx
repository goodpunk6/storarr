import { useEffect, useState } from 'react'
import { Download, Server, RefreshCw } from 'lucide-react'
import { getQueue, getDownloadClientQueues } from '../api/client'
import { QueueItem } from '../stores/appStore'

interface DownloadClientItem {
  id: string
  name: string
  size: number
  sizeRemaining: number
  progress: number
  status: string
  errorMessage?: string
}

interface DownloadClientQueue {
  clientType: string
  clientUrl: string
  items: DownloadClientItem[]
}

export default function Queue() {
  const [items, setItems] = useState<QueueItem[]>([])
  const [clientQueues, setClientQueues] = useState<DownloadClientQueue[]>([])
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)

  const fetchData = async (showRefreshing = false) => {
    if (showRefreshing) setRefreshing(true)
    try {
      const [queueResponse, clientsResponse] = await Promise.all([
        getQueue(),
        getDownloadClientQueues()
      ])
      setItems(queueResponse.data.items)
      setClientQueues(clientsResponse.data.clients)
    } catch (error) {
      console.error('Failed to fetch queue:', error)
    } finally {
      setLoading(false)
      setRefreshing(false)
    }
  }

  useEffect(() => {
    fetchData()
    const interval = setInterval(() => fetchData(), 10000) // Refresh every 10 seconds
    return () => clearInterval(interval)
  }, [])

  const formatSize = (bytes: number) => {
    if (bytes === 0) return '0 B'
    const k = 1024
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
    const i = Math.floor(Math.log(bytes) / Math.log(k))
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
  }

  const getStatusColor = (status: string) => {
    switch (status.toLowerCase()) {
      case 'completed':
        return 'text-arr-success'
      case 'downloading':
        return 'text-blue-400'
      case 'failed':
        return 'text-arr-danger'
      case 'paused':
        return 'text-arr-warning'
      case 'stalled':
        return 'text-arr-warning'
      case 'queued':
        return 'text-arr-muted'
      default:
        return 'text-arr-muted'
    }
  }

  const getClientIcon = (type: string) => {
    switch (type.toLowerCase()) {
      case 'qbittorrent':
        return '🐚'
      case 'transmission':
        return '📡'
      case 'sabnzbd':
        return '📦'
      default:
        return '⬇️'
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-12 w-12 border-t-2 border-b-2 border-arr-accent"></div>
      </div>
    )
  }

  const totalDownloads = items.length + clientQueues.reduce((sum, c) => sum + c.items.length, 0)

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold">Download Queue</h2>
        <button
          onClick={() => fetchData(true)}
          disabled={refreshing}
          className="flex items-center gap-2 px-3 py-1.5 bg-arr-card hover:bg-arr-primary rounded-lg text-sm transition-colors disabled:opacity-50"
        >
          <RefreshCw size={16} className={refreshing ? 'animate-spin' : ''} />
          Refresh
        </button>
      </div>

      {totalDownloads === 0 ? (
        <div className="bg-arr-card rounded-lg p-8 text-center">
          <Download className="mx-auto text-arr-muted mb-4" size={48} />
          <p className="text-arr-muted">No active downloads</p>
        </div>
      ) : (
        <>
          {/* Download Client Queues */}
          {clientQueues.some(c => c.items.length > 0) && (
            <div className="space-y-4">
              <h3 className="text-lg font-semibold flex items-center gap-2">
                <Server size={20} />
                Download Clients
              </h3>
              {clientQueues.map((client, index) => (
                client.items.length > 0 && (
                  <div key={index} className="bg-arr-card rounded-lg overflow-hidden">
                    <div className="px-4 py-3 bg-arr-primary/50 flex items-center gap-2">
                      <span>{getClientIcon(client.clientType)}</span>
                      <span className="font-medium">{client.clientType}</span>
                      <span className="text-arr-muted text-sm">({client.clientUrl})</span>
                      <span className="ml-auto text-sm text-arr-muted">
                        {client.items.length} active
                      </span>
                    </div>
                    <div className="divide-y divide-arr-primary">
                      {client.items.map((item) => (
                        <div key={item.id} className="p-4">
                          <div className="flex items-center justify-between mb-2">
                            <h4 className="font-medium text-sm truncate max-w-md" title={item.name}>
                              {item.name}
                            </h4>
                            <span className={`text-sm ${getStatusColor(item.status)}`}>
                              {item.status}
                            </span>
                          </div>

                          {/* Progress bar */}
                          <div className="relative h-2 bg-arr-bg rounded-full overflow-hidden mb-2">
                            <div
                              className="absolute inset-y-0 left-0 bg-blue-500 rounded-full transition-all"
                              style={{ width: `${item.progress}%` }}
                            />
                          </div>

                          <div className="flex items-center justify-between text-sm text-arr-muted">
                            <span>
                              {formatSize(item.size - item.sizeRemaining)} / {formatSize(item.size)}
                            </span>
                            <span>{item.progress.toFixed(1)}%</span>
                          </div>

                          {item.errorMessage && (
                            <p className="mt-2 text-sm text-arr-danger">{item.errorMessage}</p>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )
              ))}
            </div>
          )}

          {/* Sonarr/Radarr Queue */}
          {items.length > 0 && (
            <div className="space-y-4">
              <h3 className="text-lg font-semibold flex items-center gap-2">
                <Download size={20} />
                Arr Queue (Tracked Media)
              </h3>
              <div className="bg-arr-card rounded-lg overflow-hidden">
                <div className="divide-y divide-arr-primary">
                  {items.map((item) => (
                    <div key={item.downloadId} className="p-4">
                      <div className="flex items-center justify-between mb-2">
                        <div>
                          <h4 className="font-medium">{item.title}</h4>
                          <p className="text-sm text-arr-muted">
                            via {item.source}
                          </p>
                        </div>
                        <span className={`text-sm ${getStatusColor(item.status)}`}>
                          {item.status}
                        </span>
                      </div>

                      {/* Progress bar */}
                      <div className="relative h-2 bg-arr-bg rounded-full overflow-hidden mb-2">
                        <div
                          className="absolute inset-y-0 left-0 bg-arr-accent rounded-full transition-all"
                          style={{ width: `${item.progress}%` }}
                        />
                      </div>

                      <div className="flex items-center justify-between text-sm text-arr-muted">
                        <span>
                          {formatSize(item.size - item.sizeLeft)} / {formatSize(item.size)}
                        </span>
                        <span>{item.progress.toFixed(1)}%</span>
                      </div>

                      {item.errorMessage && (
                        <p className="mt-2 text-sm text-arr-danger">{item.errorMessage}</p>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
