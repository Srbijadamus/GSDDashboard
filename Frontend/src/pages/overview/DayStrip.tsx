import { useTranslation } from 'react-i18next'
import { isWideHorizon } from '../../utils/wicHorizon'

// One gap day from GET /api/wic/coverage-gaps (the same source as the banner and the
// list below). `present` decides crit (0 agents) vs warn (fewer than required).
interface CoverageGapRow {
  date: string
  present: number
  required: number
}

interface DaySummary {
  date: string
  uncoveredCount: number
  partialCount: number
  worstTone: 'crit' | 'warn' | 'good'
}

interface DayStripProps {
  gaps: CoverageGapRow[]
  dates: string[]
  today: string
  selectedDay: string | null
  onSelectDay: (day: string | null) => void
}

function dayMeta(d: string) {
  const dt = new Date(d + 'T12:00:00')
  const dow = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'][dt.getDay()]
  const day = String(dt.getDate()).padStart(2, '0')
  const isWe = dt.getDay() === 0 || dt.getDay() === 6
  return { dow, day, isWe }
}

export function DayStrip({ gaps, dates, today, selectedDay, onSelectDay }: DayStripProps) {
  const { t, i18n } = useTranslation()

  const summaries: DaySummary[] = dates.map(date => {
    let uncoveredCount = 0
    let partialCount = 0
    for (const g of gaps) {
      if (g.date !== date) continue
      if (g.present <= 0) uncoveredCount++
      else partialCount++
    }
    const worstTone: DaySummary['worstTone'] =
      uncoveredCount > 0 ? 'crit' : partialCount > 0 ? 'warn' : 'good'
    return { date, uncoveredCount, partialCount, worstTone }
  })

  // A 90-day window does not fit the day-by-day row the 7/14/28-day views use. The
  // strip switches to a fixed-width, horizontally scrollable band (no cell is ever
  // squeezed below a legible size, so nothing wraps or breaks), prefixed with the
  // window total. The row still reads left-to-right by date and every cell is still a
  // real, clickable day — a weekly roll-up would stop being day-selectable.
  const wide = isWideHorizon(dates.length)
  const gapDayCount = summaries.filter(s => s.worstTone !== 'good').length

  return (
    <div>
      {wide && (
        <div className="px-3 pt-3 -mb-1 flex items-center justify-between gap-3 flex-wrap">
          <span className="text-2xs text-ink-muted tnum">
            {t('overview.dayStrip.window', { n: dates.length })}
          </span>
          <span
            className="text-2xs font-semibold tnum"
            style={{ color: gapDayCount > 0 ? 'rgb(var(--st-crit-solid))' : 'rgb(var(--st-good-solid))' }}
          >
            {gapDayCount > 0
              ? t('overview.dayStrip.gapDayCount', { count: gapDayCount })
              : t('overview.dayStrip.noGapDays')}
          </span>
        </div>
      )}
      <div className="overflow-x-auto">
        <ul role="list" className="flex gap-1 px-3 pt-3 pb-2">
          {summaries.map(({ date, uncoveredCount, partialCount, worstTone }) => {
            const { dow, day, isWe } = dayMeta(date)
            const isToday = date === today
            const isSelected = selectedDay === date
            const problemCount = worstTone === 'crit' ? uncoveredCount : partialCount

            const borderClass =
              isSelected
                ? worstTone === 'crit' ? 'border-crit-bd'
                : worstTone === 'warn' ? 'border-warn-bd'
                : 'border-line-strong'
              : isToday ? 'border-line-strong'
              : worstTone === 'crit' ? 'border-crit-bd'
              : worstTone === 'warn' ? 'border-warn-bd'
              : 'border-line-subtle'

            const bgClass =
              worstTone === 'crit' ? 'bg-crit-bg' :
              worstTone === 'warn' ? 'bg-warn-bg' : 'bg-raised'

            const heightClass = worstTone === 'good' ? 'h-8' : 'h-12'

            const lang = i18n.language === 'de' ? 'de-DE' : 'en-GB'
            const fullDate = new Date(date + 'T12:00:00').toLocaleDateString(lang, {
              weekday: 'long', day: 'numeric', month: 'long',
            })
            const statusLabel =
              worstTone === 'crit' ? t('attendance.status.uncovered') :
              worstTone === 'warn' ? t('attendance.status.partial') :
              t('attendance.status.covered')
            const ariaLabel = worstTone === 'good'
              ? `${fullDate}: ${statusLabel}`
              : `${fullDate}: ${problemCount} ${statusLabel}`

            return (
              <li
                key={date}
                role="listitem"
                className={[
                  wide ? 'w-10 shrink-0' : 'flex-1 min-w-0',
                  isWe ? 'opacity-60' : '',
                ].join(' ')}
              >
                <button
                  aria-label={ariaLabel}
                  aria-pressed={isSelected}
                  onClick={() => onSelectDay(isSelected ? null : date)}
                  className={[
                    'w-full rounded-md border flex flex-col items-center justify-center gap-0.5',
                    'cursor-pointer transition-colors duration-fast focus-visible:outline-2',
                    'hover:border-line-strong',
                    bgClass, borderClass, heightClass,
                  ].join(' ')}
                >
                  <span className={[
                    'text-2xs leading-none',
                    isToday ? 'text-ink font-semibold' : 'text-ink-soft',
                  ].join(' ')}>
                    {dow}
                  </span>
                  <span className={[
                    'text-2xs leading-none tnum',
                    isToday ? 'text-ink font-semibold' : 'text-ink-muted',
                  ].join(' ')}>
                    {day}
                  </span>
                  {worstTone !== 'good' && (
                    <span className={[
                      'text-sm font-semibold leading-none tnum',
                      worstTone === 'crit' ? 'text-crit-fg' : 'text-warn-fg',
                    ].join(' ')}>
                      {problemCount}
                    </span>
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}
