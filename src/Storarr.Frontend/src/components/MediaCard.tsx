import { format } from 'date-fns'

interface TransitionRowProps {
  title: string
  currentState: string
  targetState: string
  daysUntilTransition: number
  transitionDate?: string
}

export default function TransitionRow({
  title,
  currentState,
  targetState,
  daysUntilTransition,
  transitionDate,
}: TransitionRowProps) {
  const getStateColor = (state: string) => {
    switch (state.toLowerCase()) {
      case 'symlink':
        return 'text-arr-success'
      case 'mkv':
        return 'text-arr-warning'
      case 'downloading':
        return 'text-blue-400'
      default:
        return 'text-arr-muted'
    }
  }

  const getTimeDisplay = () => {
    if (daysUntilTransition === 0) return 'Now'
    if (daysUntilTransition === 1) return 'in 1 day'
    if (daysUntilTransition < 7) return `in ${daysUntilTransition} days`
    if (transitionDate) return format(new Date(transitionDate), 'MMM d')
    return `${daysUntilTransition} days`
  }

  return (
    <div className="flex items-center justify-between py-3 border-b border-arr-primary last:border-0">
      <div className="flex-1">
        <p className="font-medium">{title}</p>
        <p className="text-sm text-arr-muted">
          <span className={getStateColor(currentState)}>{currentState}</span>
          {' → '}
          <span className={getStateColor(targetState)}>{targetState}</span>
        </p>
      </div>
      <div className="text-sm text-arr-muted">
        {getTimeDisplay()}
      </div>
    </div>
  )
}
