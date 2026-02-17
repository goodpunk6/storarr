import { ReactNode } from 'react'

interface StatCardProps {
  title: string
  value: number | string
  icon: ReactNode
  color?: string
}

export default function StatCard({ title, value, icon, color = 'bg-arr-primary' }: StatCardProps) {
  return (
    <div className={`${color} rounded-lg p-6`}>
      <div className="flex items-center justify-between">
        <div>
          <p className="text-arr-muted text-sm uppercase tracking-wider">{title}</p>
          <p className="text-3xl font-bold mt-2">{value}</p>
        </div>
        <div className="text-arr-accent opacity-50">
          {icon}
        </div>
      </div>
    </div>
  )
}
