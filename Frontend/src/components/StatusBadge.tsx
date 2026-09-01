import { type ReactNode } from 'react'

export type StatusTone =
  | 'good' | 'warn' | 'crit' | 'info'
  | 'wic' | 'learn' | 'holiday'
  | 'neutralst' | 'mutedst'

export type StatusVariant = 'solid' | 'subtle' | 'outline'

const TONE_CLASSES: Record<StatusTone, Record<StatusVariant, string>> = {
  good:      { solid: 'bg-good-solid text-ink-inverse',      subtle: 'bg-good-bg border border-good-bd text-good-fg',                 outline: 'border border-good-bd text-good-fg' },
  warn:      { solid: 'bg-warn-solid text-ink-inverse',      subtle: 'bg-warn-bg border border-warn-bd text-warn-fg',                 outline: 'border border-warn-bd text-warn-fg' },
  crit:      { solid: 'bg-crit-solid text-ink-inverse',      subtle: 'bg-crit-bg border border-crit-bd text-crit-fg',                 outline: 'border border-crit-bd text-crit-fg' },
  info:      { solid: 'bg-info-solid text-ink-inverse',      subtle: 'bg-info-bg border border-info-bd text-info-fg',                 outline: 'border border-info-bd text-info-fg' },
  wic:       { solid: 'bg-wic-solid text-ink-inverse',       subtle: 'bg-wic-bg border border-wic-bd text-wic-fg',                   outline: 'border border-wic-bd text-wic-fg' },
  learn:     { solid: 'bg-learn-solid text-ink-inverse',     subtle: 'bg-learn-bg border border-learn-bd text-learn-fg',             outline: 'border border-learn-bd text-learn-fg' },
  holiday:   { solid: 'bg-holiday-solid text-ink-inverse',   subtle: 'bg-holiday-bg border border-holiday-bd text-holiday-fg',       outline: 'border border-holiday-bd text-holiday-fg' },
  neutralst: { solid: 'bg-neutralst-solid text-ink-inverse', subtle: 'bg-neutralst-bg border border-neutralst-bd text-neutralst-fg', outline: 'border border-neutralst-bd text-neutralst-fg' },
  mutedst:   { solid: 'bg-mutedst-solid text-ink-inverse',   subtle: 'bg-mutedst-bg border border-mutedst-bd text-mutedst-fg',       outline: 'border border-mutedst-bd text-mutedst-fg' },
}

interface StatusBadgeProps {
  tone: StatusTone
  variant?: StatusVariant
  children: ReactNode
  className?: string
}

export function StatusBadge({ tone, variant = 'subtle', children, className }: StatusBadgeProps) {
  return (
    <span className={[
      'inline-flex items-center px-1.5 py-0.5 rounded-xs text-2xs font-semibold font-mono leading-none',
      TONE_CLASSES[tone][variant],
      className ?? '',
    ].filter(Boolean).join(' ')}>
      {children}
    </span>
  )
}
