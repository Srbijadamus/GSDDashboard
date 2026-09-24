import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { apiFetch } from '../api/client'
import { useAuth } from '../auth/AuthContext'

// Read-only own-data view for AGENT sessions. Every call below is scoped to the
// session's own EmployeeId and enforced again server-side (RbacMiddleware).

interface EmployeeDto {
  employeeId: string
  fullName: string | null
  primaryRole: string | null
  teamLeadName: string | null
  engagement: string | null
}

interface ShiftTimelineItem {
  shiftDate: string
  shiftType: string
  shiftStart: string | null
  shiftEnd: string | null
  isWicDuty: boolean
}
interface SickLeaveItem { firstDay: string; lastDay: string; durationDays: number; leaveType: string | null }
interface VacationItem { firstDay: string; lastDay: string; workDaysNet: number; comments: string | null }

interface TimelineDto {
  employeeId: string
  fullName: string | null
  teamLeadName: string | null
  shifts: ShiftTimelineItem[]
  sickLeaves: SickLeaveItem[]
  vacations: VacationItem[]
}

interface ALBalanceDto {
  year: number
  eligibleDays: number
  plannedTakenAL: number
  remainingAL: number
}

const today = () => new Date().toISOString().split('T')[0]

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-xl border border-line-subtle bg-raised p-5">
      <h2 className="text-sm font-semibold text-ink mb-3">{title}</h2>
      {children}
    </section>
  )
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <span className="text-ink-muted">{label}</span>
      <span className="text-ink font-medium text-right">{value ?? '—'}</span>
    </div>
  )
}

export default function AgentHome() {
  const { t } = useTranslation()
  const { session } = useAuth()
  const ownId = session?.employeeId ?? ''

  const [employee, setEmployee] = useState<EmployeeDto | null>(null)
  const [timeline, setTimeline] = useState<TimelineDto | null>(null)
  const [al, setAl] = useState<ALBalanceDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!ownId) return
    let cancelled = false
    const id = encodeURIComponent(ownId)
    Promise.allSettled([
      apiFetch<EmployeeDto>(`/api/employees/${id}`),
      apiFetch<TimelineDto>(`/api/employees/${id}/timeline`),
      apiFetch<ALBalanceDto>(`/api/albalance/${id}`),
    ]).then(([emp, tl, bal]) => {
      if (cancelled) return
      if (emp.status === 'fulfilled') setEmployee(emp.value)
      if (tl.status === 'fulfilled') setTimeline(tl.value)
      if (bal.status === 'fulfilled') setAl(bal.value)
      if (emp.status === 'rejected' && tl.status === 'rejected') {
        const reason = emp.reason
        setError(reason instanceof Error ? reason.message : String(reason))
      }
    })
    return () => { cancelled = true }
  }, [ownId])

  if (error) {
    return <p role="alert" className="text-sm text-red-600 dark:text-red-400">{error}</p>
  }

  const upcoming = (timeline?.shifts ?? [])
    .filter(s => s.shiftDate >= today())
    .slice(0, 14)

  return (
    <div className="space-y-4 max-w-4xl">
      <Card title={t('my.profile')}>
        <Row label={t('my.name')} value={employee?.fullName ?? session?.name} />
        <Row label={t('my.employeeId')} value={ownId} />
        <Row label={t('my.role')} value={employee?.primaryRole} />
        <Row label={t('my.teamLead')} value={employee?.teamLeadName} />
        <Row label={t('my.engagement')} value={employee?.engagement} />
      </Card>

      {al && (
        <Card title={`${t('my.alBalance')} ${al.year}`}>
          <Row label={t('my.alEligible')} value={al.eligibleDays} />
          <Row label={t('my.alTaken')} value={al.plannedTakenAL} />
          <Row label={t('my.alRemaining')} value={al.remainingAL} />
        </Card>
      )}

      <Card title={t('my.upcomingShifts')}>
        {upcoming.length === 0 ? (
          <p className="text-sm text-ink-soft">{t('my.noShifts')}</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-2xs uppercase tracking-wide text-ink-soft border-b border-line-subtle">
                <th className="py-1.5 pr-4">{t('my.date')}</th>
                <th className="py-1.5 pr-4">{t('my.shiftType')}</th>
                <th className="py-1.5 pr-4">{t('my.time')}</th>
                <th className="py-1.5">{t('my.wicDuty')}</th>
              </tr>
            </thead>
            <tbody>
              {upcoming.map(s => (
                <tr key={s.shiftDate} className="border-b border-line-subtle last:border-0">
                  <td className="py-1.5 pr-4 tnum">{s.shiftDate}</td>
                  <td className="py-1.5 pr-4">{s.shiftType}</td>
                  <td className="py-1.5 pr-4 tnum">
                    {s.shiftStart && s.shiftEnd ? `${s.shiftStart}–${s.shiftEnd}` : '—'}
                  </td>
                  <td className="py-1.5">{s.isWicDuty ? '✓' : ''}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <div className="grid gap-4 md:grid-cols-2">
        <Card title={t('my.sickLeaves')}>
          {(timeline?.sickLeaves?.length ?? 0) === 0 ? (
            <p className="text-sm text-ink-soft">{t('my.none')}</p>
          ) : (
            <ul className="text-sm space-y-1">
              {timeline!.sickLeaves.slice(0, 8).map((s, i) => (
                <li key={i} className="tnum">
                  {s.firstDay} → {s.lastDay} · {s.durationDays}d{s.leaveType ? ` · ${s.leaveType}` : ''}
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title={t('my.vacations')}>
          {(timeline?.vacations?.length ?? 0) === 0 ? (
            <p className="text-sm text-ink-soft">{t('my.none')}</p>
          ) : (
            <ul className="text-sm space-y-1">
              {timeline!.vacations.slice(0, 8).map((v, i) => (
                <li key={i} className="tnum">
                  {v.firstDay} → {v.lastDay} · {v.workDaysNet}d{v.comments ? ` · ${v.comments}` : ''}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </div>
  )
}
