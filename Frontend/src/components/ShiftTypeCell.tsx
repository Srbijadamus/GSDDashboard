import { useTranslation } from 'react-i18next'
import { ShiftBadge, type ShiftStatus } from './ShiftBadge'

// Types for which a shift time is never meaningful (full-day absence / closure)
const NO_TIME = new Set<ShiftStatus>([
  'OFF', 'OFF_WEEKEND', 'AL', 'PH', 'LPH', 'CD', 'CO', 'OL', 'UL', 'RESIGNED',
])

interface ShiftTypeCellProps {
  status: ShiftStatus
  timeStart?: string   // e.g. "06:00"
  timeEnd?: string     // e.g. "14:00"
  compact?: boolean    // h-9 row, badge only — use when row height budget is tight
  isWeekend?: boolean  // applies bg-sunken to column; badge contents unchanged
}

export function ShiftTypeCell({
  status, timeStart, timeEnd,
  compact = false, isWeekend = false,
}: ShiftTypeCellProps) {
  const showTime = !compact && !NO_TIME.has(status) && !!timeStart && !!timeEnd

  if (compact) {
    return (
      <div className={['h-9 px-1 flex items-center justify-center border-r border-line-subtle', isWeekend ? 'bg-sunken' : ''].filter(Boolean).join(' ')}>
        <ShiftBadge status={status} />
      </div>
    )
  }

  return (
    <div className={['h-14 px-2 flex flex-col items-center justify-center gap-0.5 border-r border-line-subtle', isWeekend ? 'bg-sunken' : ''].filter(Boolean).join(' ')}>
      <ShiftBadge status={status} />
      {showTime && (
        <span className="font-mono text-[10px] text-ink-soft leading-none">
          {timeStart}–{timeEnd}
        </span>
      )}
    </div>
  )
}

// ── Legend ────────────────────────────────────────────────────────────────────
// Uses real badge components at actual size — not colour squares.
// Render at the bottom of any shift grid that uses ShiftTypeCell.

const LEGEND_ITEMS: Array<{ status: ShiftStatus; labelKey: string }> = [
  { status: 'WORKING',     labelKey: 'status.working' },
  { status: 'WIC_DUTY',    labelKey: 'status.wicDuty' },
  { status: 'AL',          labelKey: 'status.annualLeave' },
  { status: 'HALF_AL',     labelKey: 'status.halfAl' },
  { status: 'SL',          labelKey: 'status.sickLeave' },
  { status: 'TRAINING',    labelKey: 'status.training' },
  { status: 'OFF',         labelKey: 'status.off' },
  { status: 'OFF_WEEKEND', labelKey: 'status.offWeekend' },
  { status: 'PH',          labelKey: 'status.publicHoliday' },
  { status: 'LPH',         labelKey: 'status.lph' },
  { status: 'UL',          labelKey: 'status.otherLeave' },
  { status: 'CD',          labelKey: 'status.cd' },
  { status: 'CO',          labelKey: 'status.co' },
  { status: 'OL',          labelKey: 'status.ol' },
]

export function ShiftLegend({ className }: { className?: string }) {
  const { t } = useTranslation()
  return (
    <div className={['flex flex-wrap gap-x-4 gap-y-2 pt-3 border-t border-line-subtle', className ?? ''].filter(Boolean).join(' ')}>
      {LEGEND_ITEMS.map(({ status, labelKey }) => (
        <span key={status} className="flex items-center gap-1.5">
          <ShiftBadge status={status} />
          <span className="text-xs text-ink-muted">{t(labelKey, { defaultValue: status })}</span>
        </span>
      ))}
    </div>
  )
}

// ── Agent column cell ─────────────────────────────────────────────────────────
// Use this for the sticky-left agent name cell in every shift grid.
// role + engagement joined by a middot: "WIC · GSD-DE"

interface AgentCellProps {
  name: string
  role?: string
  engagement?: string
  resigned?: boolean
}

export function AgentCell({ name, role, engagement, resigned = false }: AgentCellProps) {
  const sub = [role, engagement].filter(Boolean).join(' · ')
  return (
    <td
      className="px-3 py-0 align-middle sticky left-0 bg-raised border-r border-line z-[1] min-w-[200px]"
      style={{ height: 'inherit' }}
    >
      <div className="flex flex-col justify-center gap-0.5 h-full py-2">
        <span className={['text-sm font-medium text-ink leading-snug', resigned ? 'line-through text-ink-muted' : ''].filter(Boolean).join(' ')}>
          {name}
        </span>
        {sub && (
          <span className="text-xs text-ink-soft leading-snug">{sub}</span>
        )}
      </div>
    </td>
  )
}
