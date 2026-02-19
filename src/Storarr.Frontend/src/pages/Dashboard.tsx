import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Link2, HardDrive, Download, Clock } from 'lucide-react'
import { getDashboard } from '../api/client'
import StatCard from '../components/StatCard'
import TransitionRow from '../components/TransitionRow'
import { useAppStore, Transition } from '../stores/appStore'

interface DashboardData {
  totalItems: number
  symlinkCount: number
  mkvCount: number
  downloadingCount: number
  pendingSymlinkCount: number
  totalSizeBytes: number
  upcomingTransitions: Transition[]
}

export default function Dashboard() {
  const [data, setData] = useState<DashboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const { setDashboardData } = useAppStore()

  useEffect(() => {
    const fetchData = async () => {
      try {
        const response = await getDashboard()
        setData(response.data)
        setDashboardData({
          symlinkCount: response.data.symlinkCount,
          mkvCount: response.data.mkvCount,
          downloadingCount: response.data.downloadingCount,
          pendingSymlinkCount: response.data.pendingSymlinkCount,
          upcomingTransitions: response.data.upcomingTransitions,
        })
      } catch (error) {
        console.error('Failed to fetch dashboard data:', error)
      } finally {
        setLoading(false)
      }
    }

    fetchData()
  }, [setDashboardData])

  const formatSize = (bytes: number) => {
    if (bytes === 0) return '0 B'
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
      <h2 className="text-2xl font-bold">Dashboard</h2>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        <StatCard
          title="SYMLINK"
          value={data?.symlinkCount ?? 0}
          icon={<Link2 size={48} />}
          color="bg-arr-card"
        />
        <StatCard
          title="MKV"
          value={data?.mkvCount ?? 0}
          icon={<HardDrive size={48} />}
          color="bg-arr-card"
        />
        <StatCard
          title="DOWNLOADING"
          value={data?.downloadingCount ?? 0}
          icon={<Download size={48} />}
          color="bg-arr-card"
        />
        <StatCard
          title="PENDING"
          value={data?.pendingSymlinkCount ?? 0}
          icon={<Clock size={48} />}
          color="bg-arr-card"
        />
      </div>

      {/* Total Size */}
      <div className="bg-arr-card rounded-lg p-6">
        <h3 className="text-lg font-semibold mb-2">Total Storage</h3>
        <p className="text-3xl font-bold text-arr-accent">
          {formatSize(data?.totalSizeBytes ?? 0)}
        </p>
      </div>

      {/* Upcoming Transitions */}
      <div className="bg-arr-card rounded-lg p-6">
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-semibold">Upcoming Transitions</h3>
          <Link to="/activity" className="text-arr-accent hover:underline text-sm">
            View All
          </Link>
        </div>
        {data?.upcomingTransitions && data.upcomingTransitions.length > 0 ? (
          <div>
            {data.upcomingTransitions.map((transition, index) => (
              <TransitionRow
                key={index}
                title={transition.title}
                currentState={transition.currentState}
                targetState={transition.targetState}
                daysUntilTransition={transition.daysUntilTransition}
                transitionDate={transition.transitionDate}
              />
            ))}
          </div>
        ) : (
          <p className="text-arr-muted text-center py-4">No upcoming transitions</p>
        )}
      </div>
    </div>
  )
}
