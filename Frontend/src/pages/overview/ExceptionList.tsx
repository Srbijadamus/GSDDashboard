import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { X } from 'lucide-react'
import { StatusBadge } from '../../components/StatusBadge'
import { Button } from '../../components/Button'
import { EmptyState } from '../../components/EmptyState'
import { DataTable, type DataColumn } from '../../components/DataTable'
import type { WicCoverageBannerState } from '../../utils/wicCoverageState'

// One gap day, exactly as GET /api/wic/coverage-gaps returns it. The Coverage Risk
// list is driven by that same response as the banner above it, so the two can never
// disagree about which day is short of agents.
export interface CoverageGapRow {
  date: string; locationCode: string; displayName: string; city: string
  present: number; required: number
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
  gaps: CoverageGapRow[]
  dates: string[]
  briefingGaps: BriefingGap[]
  today: string
  selectedDay: string | null
  onClearDayFilter: () => void
  onOpenSub: (args: { locationCode: string; date: string; displayName: string }) => void
  horizon: number
  state: WicCoverageBannerState
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
  gaps, dates, briefingGaps, today, selectedDay,
  onClearDayFilter, onOpenSub, horizon, state,
}: ExceptionListProps) {
  const { t, i18n } = useTranslation()
  const [showAll, setShowAll] = useState(false)
  const lang = i18n.language === 'de' ? 'de-DE' : 'en-GB'

  // Built from the coverage-gaps response, filtered to the visible day strip. The
  // forecast is deliberately NOT a second source here: two readings of the same day
  // is what put the banner and this list out of step on 2026-10-13.
  const visibleDates = new Set(dates)
  const allExceptions: ExceptionRow[] = []
  for (const g of gaps) {
    if (!visibleDates.has(g.date)) continue
    const briefingGap = g.date === today
      ? briefingGaps.find(x => x.locationCode === g.locationCode)
      : undefined
    allExceptions.push({
      date: g.date, locationCode: g.locationCode, displayName: g.displayName, city: g.city,
      effectiveCoverage: g.present, minRequired: g.required,
      status: g.present <= 0 ? 'UNCOVERED' : 'PARTIAL',
      bestSub: briefingGap?.bestSubstituteName ?? null,
    })
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

  // A failed / 401 / 403 coverage query must never fall through to the green
  // "All locations covered" empty state below.
  if (state === 'disabled') return null
  if (state === 'loading') {
    return (
      <div>
        {filterChip}
        <div style={{ padding: 16, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {[1, 2, 3].map(i => <div key={i} className="skeleton" style={{ height: 24 }} />)}
        </div>
      </div>
    )
  }
  if (state === 'error') {
    return (
      <div>
        {filterChip}
        <EmptyState
          title={t('overview.coverageBanner.couldNotLoad')}
          description={t('overview.coverageBanner.couldNotLoadDetail')}
        />
      </div>
    )
  }

  if (filtered.length === 0) {
    // "All covered" only holds when nothing is filtered — filtering to one day and
    // finding it clear does NOT mean the other days in the strip are clear too, and
    // the strip itself still shows their red/amber cells unfiltered.
    // escapeValue: false — i18next HTML-escapes interpolated values by default
    // (e.g. "/" -> "&#x2F;"), which then shows up literally since React already
    // escapes text content itself. Only needed here: allCovered's {{n}} is a plain
    // number, nothing to escape either way.
    const title = selectedDay
      ? t('overview.exceptionList.dayCovered', { date: fmtDate(selectedDay, lang), interpolation: { escapeValue: false } })
      : t('overview.exceptionList.allCovered', { n: horizon })
    return (
      <div>
        {filterChip}
        <EmptyState tone="good" title={title} />
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
