import { useTranslation } from 'react-i18next'

interface DayForecast { date: string; status: string }
interface LocationForecast { locationCode: string; forecast?: DayForecast[] }

interface DaySummary {
  date: string
  uncoveredCount: number
  partialCount: number
  worstTone: 'crit' | 'warn' | 'good'
}

interface DayStripProps {
  forecast: LocationForecast[]
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

export function DayStrip({ forecast, dates, today, selectedDay, onSelectDay }: DayStripProps) {
  const { t, i18n } = useTranslation()

  const summaries: DaySummary[] = dates.map(date => {
    let uncoveredCount = 0
    let partialCount = 0
    for (const lf of forecast) {
      const df = lf.forecast?.find(d => d.date === date)
      if (!df || df.status === 'CLOSED') continue
      if (df.status === 'UNCOVERED') uncoveredCount++
      else if (df.status === 'PARTIAL') partialCount++
    }
    const worstTone: DaySummary['worstTone'] =
      uncoveredCount > 0 ? 'crit' : partialCount > 0 ? 'warn' : 'good'
    return { date, uncoveredCount, partialCount, worstTone }
  })

  return (
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
          <li key={date} role="listitem" className={['flex-1 min-w-0', isWe ? 'opacity-60' : ''].join(' ')}>
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
  )
}
