import { type ElementType } from 'react'
import { CheckCircle2, Info } from 'lucide-react'

interface EmptyStateProps {
  title: string
  description?: string
  tone?: 'default' | 'good'
  icon?: ElementType
}

export function EmptyState({ title, description, tone = 'default', icon: Icon }: EmptyStateProps) {
  const ResolvedIcon = Icon ?? (tone === 'good' ? CheckCircle2 : Info)
  return (
    <div className="flex flex-col items-center justify-center py-10 gap-3 text-center px-4">
      <ResolvedIcon className={['h-8 w-8', tone === 'good' ? 'text-good-fg' : 'text-ink-soft'].join(' ')} />
      <p className="text-sm font-medium text-ink">{title}</p>
      {description && <p className="text-xs text-ink-soft max-w-sm">{description}</p>}
    </div>
  )
}
