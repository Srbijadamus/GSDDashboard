import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { StatusBadge } from '../../components/StatusBadge'
import { Button } from '../../components/Button'
import { EmptyState } from '../../components/EmptyState'
import { DataTable, type DataColumn } from '../../components/DataTable'

interface DayForecast {
  date: string; status: string; effectiveCoverage: number; minRequired: number
}
interface LocationForecast {
  locationCode: string; displayName: string; city: string; forecast?: DayForecast[]
}
interface BriefingGap {
  locationCode: string; bestSubstituteName: string | null
  // no gapDate — briefing.gaps are always for today
}
interface ExceptionRow {
  date: string; locationCode: string; displayName: string; city: string
  effectiveCoverage: number; minRequired: number
  status: 'UNCOVERED' | 'PARTIAL'
  bestSub: string | null
}

interface ExceptionListProps {
  forecast: LocationForecast[]
  dates: string[]
  briefingGaps: BriefingGap[]
  today: string
  selectedDay: string | null
  onClearDayFilter: () => void
  onOpenSub: (args: { locationCode: string; date: string; displayName: string }) => void
  horizon: number
}

const CAP = 8

function cityCode(city: string): string {
  return city.replace(/[^A-Za-z]/g, '').slice(0, 3).toUpperCase()
}

function fmtDate(d: string, lang: string): string {
  return new Date(d + 'T12:00:00').toLocaleDateString(lang, {
    weekday: 'short', day: '2-digit', month: '2-digit',
  })
}

export function ExceptionList({
  forecast, dates, briefingGaps, today, selectedDay,
  onClearDayFilter, onOpenSub, horizon,
}: ExceptionListProps) {
  const { t, i18n } = useTranslation()
  const [showAll, setShowAll] = useState(false)
  const lang = i18n.language === 'de' ? 'de-DE' : 'en-GB'

  const allExceptions: ExceptionRow[] = []
  for (const date of dates) {
    for (const lf of forecast) {
      const df = lf.forecast?.find(d => d.date === date)
      if (!df || (df.status !== 'UNCOVERED' && df.status !== 'PARTIAL')) continue
      const gap = date === today
        ? briefingGaps.find(g => g.locationCode === lf.locationCode)
        : undefined
      allExceptions.push({
        date, locationCode: lf.locationCode, displayName: lf.displayName, city: lf.city,
        effectiveCoverage: df.effectiveCoverage, minRequired: df.minRequired,
        status: df.status as 'UNCOVERED' | 'PARTIAL',
        bestSub: gap?.bestSubstituteName ?? null,
      })
    }
  }

  allExceptions.sort((a, b) => {
    if (a.status !== b.status) return a.status === 'UNCOVERED' ? -1 : 1
    const dd = a.date.localeCompare(b.date)
    return dd !== 0 ? dd : a.displayName.localeCompare(b.displayName)
  })

  const filtered = selectedDay ? allExceptions.filter(r => r.date === selectedDay) : allExceptions
  const visible = showAll ? filtered : filtered.slice(0, CAP)

  const filterChip = selectedDay ? (
    <div className="px-3 pt-2 pb-1 flex items-center gap-2">
      <div className="h-6 px-2 rounded-sm bg-hovered border border-line-default flex items-center gap-1.5 text-2xs text-ink-muted">
        <span>{fmtDate(selectedDay, lang)}</span>
        <button
          onClick={onClearDayFilter}
          aria-label={t('overview.dayStrip.clearFilter')}
          className="text-ink-soft hover:text-ink transition-colors duration-fast"
        >
          <X className="h-3 w-3" />
        </button>
      </div>
    </div>
  ) : null

  if (filtered.length === 0) {
    return (
      <div>
        {filterChip}
        <EmptyState tone="good" title={t('overview.exceptionList.allCovered', { n: horizon })} />
      </div>
    )
  }

  const columns: DataColumn<ExceptionRow>[] = [
    {
      key: 'date',
      header: t('overview.exceptionList.columns.date'),
      render: row => <span className="font-mono text-xs text-ink-muted whitespace-nowrap">{fmtDate(row.date, lang)}</span>,
    },
    {
      key: 'code',
      header: t('overview.exceptionList.columns.code'),
      render: row => <span className="font-mono text-xs text-ink">{cityCode(row.city)}</span>,
    },
    {
      key: 'location',
      header: t('table.location'),
      render: row => (
        <div>
          <div className="text-sm text-ink">{row.displayName}</div>
          {row.bestSub && (
            <div className="text-xs text-ink-soft">{t('overview.exceptionList.bestSub', { name: row.bestSub })}</div>
          )}
        </div>
      ),
    },
    {
      key: 'presentMin',
      header: t('overview.exceptionList.columns.presentMin'),
      render: row => (
        <span className={[
          'font-mono text-xs',
          row.effectiveCoverage === 0 ? 'text-crit-fg' : 'text-warn-fg',
        ].join(' ')}>
          {row.effectiveCoverage} / {row.minRequired}
        </span>
      ),
    },
    {
      key: 'status',
      header: t('overview.exceptionList.columns.status'),
      render: row => (
        <StatusBadge tone={row.status === 'UNCOVERED' ? 'crit' : 'warn'} variant="subtle">
          {row.status === 'UNCOVERED' ? t('attendance.status.uncovered') : t('attendance.status.partial')}
        </StatusBadge>
      ),
    },
    {
      key: 'action',
      header: '',
      headerClassName: 'text-right',
      className: 'text-right',
      render: row => (
        <Button
          variant="ghost"
          size="sm"
          onClick={() => onOpenSub({ locationCode: row.locationCode, date: row.date, displayName: row.displayName })}
        >
          {t('attendance.substitute.find')}
        </Button>
      ),
    },
  ]

  return (
    <div>
      {filterChip}
      <DataTable columns={columns} data={visible} rowKey={row => `${row.locationCode}-${row.date}`} />
      {filtered.length > CAP && (
        <div className="px-3 py-2 border-t border-line-subtle">
          <Button variant="ghost" size="sm" onClick={() => setShowAll(s => !s)}>
            {showAll
              ? t('overview.exceptionList.collapse')
              : t('overview.exceptionList.showAll', { n: filtered.length })}
          </Button>
        </div>
      )}
    </div>
  )
}
