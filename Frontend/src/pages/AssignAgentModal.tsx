import { useState, useEffect, useRef } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Users, AlertTriangle } from "lucide-react"
import { NppBadge } from "../components/NppBadge"
import { MoveToGsdBacklogAction } from "../components/MoveToGsdBacklogAction"
import { SearchableSelect } from "../components/SearchableSelect"
import { apiFetch } from "../api/client"
import { resolveDayHours, type WicOpenDay } from "../utils/wicOpenHours"

interface Employee {
  employeeId: string
  fullName: string | null
}

interface WicLocation {
  locationCode: string
  displayName: string
  isNpp?: boolean
}

interface AssignAgentModalProps {
  isOpen: boolean
  onClose: () => void
  defaultLocationCode?: string | null
  defaultDate?: string
}

function addDays(dateStr: string, n: number): string {
  const d = new Date(dateStr)
  d.setDate(d.getDate() + n)
  return d.toISOString().split("T")[0]
}

function isWeekend(dateStr: string): boolean {
  const dow = new Date(dateStr).getDay()
  return dow === 0 || dow === 6
}

function buildDateRange(from: string, to: string, skipWeekends: boolean): string[] {
  const dates: string[] = []
  let cur = from
  while (cur <= to) {
    if (!skipWeekends || !isWeekend(cur)) dates.push(cur)
    cur = addDays(cur, 1)
  }
  return dates
}

// One day's answer from POST /api/wic/assignments. `saved` is the authoritative
// "a row was actually written" flag the server now always returns; `skipped` is
// kept only for older callers. requiresConfirmation means the existing row for
// that day is a human's decision and the RTM must confirm the replacement.
interface ManualConflict {
  location: string
  workingShift?: string | null
  actor?: string | null
  atBerlin?: string | null
}

interface DayOutcome {
  date: string
  saved: boolean
  reasonCode?: string
  reason?: string | null
  displayName?: string | null
  shiftType?: string | null
  requiresConfirmation?: boolean
  conflicts?: ManualConflict[]
  nppWarning?: string | null
  httpStatus?: number
}

type TFn = (key: string, opts?: Record<string, unknown>) => string

// Localizes the server's machine-readable reasonCode from its STRUCTURED fields.
// The server's own English `reason` is only the last-resort fallback, so the EN
// view never shows German and the DE view never shows English.
function reasonText(p: TFn, o: DayOutcome): string {
  switch (o.reasonCode) {
    case "CLOSED":          return p("reasons.closed")
    case "BLOCKING_SHIFT":  return p("reasons.blockingShift", { date: o.date, shiftType: o.shiftType ?? "" })
    case "TIME_CONFLICT": {
      const c = o.conflicts?.[0]
      return c
        ? p("reasons.timeConflict", { loc: c.location, shift: c.workingShift ?? "—", date: o.date })
        : p("reasons.other", { reason: o.reason ?? "" })
    }
    case "MANUAL_CONFLICT": return p("reasons.manualConflict")
    case "PROTECTED_MANUAL":return p("reasons.protectedManual")
    case "HTTP":            return p("reasons.http", { status: o.httpStatus ?? "" })
    default:                return o.reason ? String(o.reason) : p("reasons.other")
  }
}

export function AssignAgentModal({ isOpen, onClose, defaultLocationCode, defaultDate }: AssignAgentModalProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const p = (k: string, opts?: Record<string, unknown>): string =>
    String(t(`attendance.assignAgent.${k}`, opts as never))

  const today = new Date().toISOString().split("T")[0]

  const [employeeId, setEmployeeId]       = useState("")
  const [locationCode, setLocationCode]   = useState(defaultLocationCode ?? "")
  const [dateFrom, setDateFrom]           = useState(defaultDate ?? today)
  const [dateTo, setDateTo]               = useState(defaultDate ?? today)
  const [skipWeekends, setSkipWeekends]   = useState(true)
  const [shiftStart, setShiftStart]       = useState("")
  const [shiftEnd, setShiftEnd]           = useState("")
  const [submitting, setSubmitting]       = useState(false)
  const [progress, setProgress]           = useState<string | null>(null)
  const [success, setSuccess]             = useState<string | null>(null)
  const [nppWarn, setNppWarn]             = useState<string | null>(null)
  const [error, setError]                 = useState<string | null>(null)
  const [closedDay, setClosedDay]         = useState(false)
  const [outcome, setOutcome]             = useState<DayOutcome[] | null>(null)
  const [confirmDays, setConfirmDays]     = useState<DayOutcome[] | null>(null)
  const [uncoveredWarn, setUncoveredWarn] = useState<{ agent: string; location: string; date: string } | null>(null)
  const pendingProceed                    = useRef<(() => Promise<void>) | null>(null)

  useEffect(() => {
    if (isOpen) {
      setEmployeeId("")
      setLocationCode(defaultLocationCode ?? "")
      setDateFrom(defaultDate ?? today)
      setDateTo(defaultDate ?? today)
      setSkipWeekends(true)
      setShiftStart("")
      setShiftEnd("")
      setSuccess(null)
      setNppWarn(null)
      setError(null)
      setProgress(null)
      setClosedDay(false)
      setOutcome(null)
      setConfirmDays(null)
      setUncoveredWarn(null)
    }
  }, [isOpen, defaultLocationCode, defaultDate])

  const { data: employees = [] } = useQuery<Employee[]>({
    queryKey: ["employees-active"],
    queryFn: () => fetch("/api/employees/?active=true").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60 * 1000,
    enabled: isOpen,
  })

  const { data: locations = [] } = useQuery<WicLocation[]>({
    queryKey: ["wic-locations"],
    queryFn: () =>
      fetch("/api/wic/locations")
        .then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 10 * 60 * 1000,
    enabled: isOpen,
  })

  // Hours for the SELECTED DATE, from the resolver that honours ExceptionDate (manual
  // exception day / Rollout day) and public holidays: GET /api/wic/open?date=...&horizon=1
  // returns exactly one day with per-location isOpen and the intervals of THAT date.
  // Reading the weekly pattern here is what made an open exception show "closed" and a
  // closed exception show the pattern's hours with no banner.
  const { data: openDays = [] } = useQuery<WicOpenDay[]>({
    queryKey: ["wic-open", dateFrom],
    queryFn: () => apiFetch<WicOpenDay[]>(`/api/wic/open?date=${dateFrom}&horizon=1`),
    staleTime: 60 * 1000,
    enabled: isOpen && !!dateFrom,
  })

  // Auto-populate times and detect closed day whenever location or from-date changes.
  // Red banner only when the centre is really closed on that DATE; an open exception
  // pre-fills the exception's hours.
  useEffect(() => {
    if (!locationCode || !dateFrom || openDays.length === 0) { setClosedDay(false); return }
    const hours = resolveDayHours(openDays, dateFrom, locationCode)
    setClosedDay(hours.closed)
    setShiftStart(hours.start)
    setShiftEnd(hours.end)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [locationCode, dateFrom, openDays])

  const dates = buildDateRange(dateFrom, dateTo, skipWeekends)
  const isRange = dateFrom !== dateTo

  const inputCls = "bg-sunken border border-line-subtle text-ink"
  const inputStyle: React.CSSProperties = {
    width: "100%",
    padding: "6px 10px",
    borderRadius: 6,
    fontSize: 12,
    outline: "none",
    boxSizing: "border-box",
    marginTop: 4,
  }

  // Runs the whole date list (or a confirm re-run of the days that asked for one).
  // EVERY day's answer is kept, saved or not, so the summary can never call a
  // nothing-saved run a success.
  const runDays = async (runDates: string[], confirmManualReplace: boolean): Promise<DayOutcome[]> => {
    const results: DayOutcome[] = []
    for (let i = 0; i < runDates.length; i++) {
      const d = runDates[i]
      setProgress(p("progress", { current: i + 1, total: runDates.length, date: d }))
      try {
        const res = await fetch("/api/wic/assignments", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            employeeId,
            locationCode,
            date: d,
            shiftStart: shiftStart || null,
            shiftEnd: shiftEnd || null,
            confirmManualReplace,
          }),
        })
        if (!res.ok) {
          const body = await res.text().catch(() => "")
          results.push({ date: d, saved: false, reasonCode: "HTTP", httpStatus: res.status, reason: `HTTP ${res.status}${body ? ": " + body : ""}` })
          continue
        }
        const data = await res.json()
        results.push({
          date: d,
          saved: data.saved === true,
          reasonCode: data.reasonCode,
          reason: data.reason ?? null,
          displayName: data.displayName ?? null,
          shiftType: data.shiftType ?? null,
          requiresConfirmation: data.requiresConfirmation === true,
          conflicts: data.conflicts ?? undefined,
          nppWarning: data.nppWarning ?? null,
        })
      } catch (err) {
        results.push({ date: d, saved: false, reasonCode: "HTTP", reason: String(err) })
      }
    }
    return results
  }

  const finish = (results: DayOutcome[]) => {
    setSubmitting(false)
    setProgress(null)
    setConfirmDays(null)
    setOutcome(results)

    const saved = results.filter(r => r.saved)
    const notSaved = results.filter(r => !r.saved)
    const firstNpp = results.find(r => r.nppWarning)?.nppWarning ?? null
    if (firstNpp) setNppWarn(firstNpp)

    if (notSaved.length === 0) {
      const loc = saved[saved.length - 1]?.displayName ?? ""
      setSuccess(isRange
        ? p("summaryRange", { count: saved.length, loc })
        : p("success", { loc }))
      setTimeout(() => { setSuccess(null); setNppWarn(null); onClose() }, firstNpp ? 5000 : 1800)
    } else {
      // Success is only for "everything was saved". Anything else stays visible
      // with the exact reason per day.
      setSuccess(null)
    }

    queryClient.refetchQueries({ queryKey: ["wic-forecast"] })
    queryClient.refetchQueries({ queryKey: ["wic-cards", dateFrom], exact: true })
    queryClient.refetchQueries({ queryKey: ["wic-coverage-gaps"] })
  }

  const proceedWithAssignment = async () => {
    setError(null)
    setOutcome(null)
    setSubmitting(true)
    const results = await runDays(dates, false)
    const needConfirm = results.filter(r => r.requiresConfirmation)
    if (needConfirm.length > 0) {
      setSubmitting(false)
      setProgress(null)
      setConfirmDays(needConfirm)
      setOutcome(results)
      return
    }
    finish(results)
  }

  const confirmReplace = async () => {
    const pending = confirmDays ?? []
    setConfirmDays(null)
    setSubmitting(true)
    const before = (outcome ?? []).filter(r => !r.requiresConfirmation)
    const rerun = await runDays(pending.map(r => r.date), true)
    finish([...before, ...rerun].sort((a, b) => a.date.localeCompare(b.date)))
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!employeeId || !locationCode || dates.length === 0) return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    setNppWarn(null)
    setOutcome(null)
    setConfirmDays(null)

    // Check if moving leaves source location uncovered
    const agentsRes = await fetch(`/api/wic/cards?date=${dateFrom}`)
    if (agentsRes.ok) {
      const cards: any[] = await agentsRes.json()
      const sourceCard = cards.find((c: any) =>
        c.assignedAgents?.some((a: any) => a.employeeId === employeeId)
      )
      if (sourceCard && sourceCard.locationCode !== locationCode) {
        const otherAgents = (sourceCard.assignedAgents ?? []).filter(
          (a: any) => a.employeeId !== employeeId &&
          a.coverageMatch !== "NONE" &&
          a.shiftStart !== "SICK" && a.shiftStart !== "AL"
        )
        if (otherAgents.length === 0) {
          const emp = employees.find(e => e.employeeId === employeeId)
          pendingProceed.current = proceedWithAssignment
          setUncoveredWarn({
            agent: emp?.fullName ?? employeeId,
            location: sourceCard.displayName,
            date: dateFrom
          })
          setSubmitting(false)
          return
        }
      }
    }

    await proceedWithAssignment()
  }

  if (!isOpen) return null

  const savedCount = outcome?.filter(r => r.saved).length ?? 0
  const notSavedCount = outcome ? outcome.length - savedCount : 0

  return (
    <>
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 1000,
        background: "rgba(0,0,0,0.55)",
        display: "flex", alignItems: "center", justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        className="bg-raised border border-line-subtle"
        style={{
          borderRadius: 12, padding: 24, width: 460, maxWidth: "90vw",
          maxHeight: "90vh", overflowY: "auto",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
          <Users size={16} className="text-info-fg" />
          <span className="text-ink" style={{ fontSize: 15, fontWeight: 600 }}>{p("title")}</span>
        </div>

        {success && (
          <div style={{
            background: "rgb(var(--st-good-bg) / 0.12)",
            border: "1px solid rgb(var(--st-good-bd) / 0.3)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }} className="text-good-fg">
            {success}
          </div>
        )}
        {outcome && notSavedCount > 0 && (
          <div
            className="bg-warn-bg border border-warn-bd text-warn-fg"
            style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14 }}
          >
            <div style={{ fontWeight: 700, marginBottom: 6 }}>{p("resultTitle")}</div>
            <div style={{ marginBottom: 6 }}>
              {savedCount === 0
                ? p("nothingSaved")
                : p("partialSummary", { saved: savedCount, total: outcome.length, notSaved: notSavedCount })}
            </div>
            <ul style={{ margin: 0, paddingLeft: 16, lineHeight: 1.6 }}>
              {outcome.map(o => (
                <li key={o.date} className={o.saved ? "text-good-fg" : "text-warn-fg"}>
                  {o.saved
                    ? p("daySaved", { date: o.date, loc: o.displayName ?? locationCode })
                    : p("dayNotSaved", { date: o.date, reason: reasonText(p, o) })}
                </li>
              ))}
            </ul>
          </div>
        )}
        {nppWarn && (
          <div style={{
            background: "rgb(var(--st-crit-bg) / 0.12)",
            border: "1px solid rgb(var(--st-crit-bd) / 0.4)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
            display: "flex", alignItems: "flex-start", gap: 8,
          }} className="text-crit-fg">
            <AlertTriangle size={14} style={{ flexShrink: 0, marginTop: 1 }} />
            <span style={{ fontWeight: 700, flexShrink: 0 }}>NPP Warning:</span>
            <span>{nppWarn}</span>
          </div>
        )}
        {error && (
          <div
            className="bg-crit-bg border border-crit-bd text-crit-fg"
            style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14 }}
          >
            {error}
          </div>
        )}
        {progress && (
          <div style={{
            background: "rgb(var(--st-info-bg) / 0.08)",
            border: "1px solid rgb(var(--st-info-bd) / 0.25)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }} className="text-info-fg">
            {progress}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("employee")}
            <SearchableSelect
              options={employees.map(e => ({ value: e.employeeId, label: e.fullName ?? e.employeeId }))}
              value={employeeId}
              onChange={setEmployeeId}
              placeholder={p("selectEmployee")}
              required
              className={inputCls}
              style={inputStyle}
            />
          </label>

          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("location")}
            <select value={locationCode} onChange={e => setLocationCode(e.target.value)} required className={inputCls} style={inputStyle}>
              <option value="">{p("selectLocation")}</option>
              {locations.map(l => (
                <option key={l.locationCode} value={l.locationCode}>
                  {l.displayName}{l.isNpp ? " (npp)" : ""}
                </option>
              ))}
            </select>
          </label>

          {locations.find(l => l.locationCode === locationCode)?.isNpp && (
            <div style={{
              background: "rgb(var(--st-crit-bg) / 0.08)",
              border: "1px solid rgb(var(--st-crit-bd) / 0.3)",
              borderRadius: 6, padding: "7px 11px", fontSize: 11,
              display: "flex", alignItems: "center", gap: 6,
            }} className="text-crit-fg">
              <NppBadge />
              NPP site — only NPP-qualified agents may be assigned here.
            </div>
          )}

          {closedDay && (
            <div
              className="bg-crit-bg border border-crit-bd text-crit-fg"
              style={{ borderRadius: 6, padding: "7px 11px", fontSize: 11, display: "flex", alignItems: "flex-start", gap: 6 }}
            >
              <AlertTriangle size={13} style={{ flexShrink: 0, marginTop: 1 }} />
              <span>{p("closedDay")}</span>
            </div>
          )}

          {/* Date range */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
              {p("from")}
              <input
                type="date"
                value={dateFrom}
                onChange={e => {
                  setDateFrom(e.target.value)
                  if (e.target.value > dateTo) setDateTo(e.target.value)
                }}
                required
                className={inputCls}
                style={inputStyle}
              />
            </label>
            <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
              {p("to")}
              <input
                type="date"
                value={dateTo}
                min={dateFrom}
                onChange={e => setDateTo(e.target.value)}
                required
                className={inputCls}
                style={inputStyle}
              />
            </label>
          </div>

          {/* Day count badge + skip weekends */}
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
            <span className="text-ink-soft" style={{ fontSize: 11 }}>
              {dates.length === 0
                ? <span className="text-crit-fg">{p("noWorkingDays")}</span>
                : <span><span className="text-info-fg" style={{ fontWeight: 600 }}>{dates.length}</span> {p("daysSelected", { count: dates.length })}</span>}
            </span>
            <label className="text-ink-soft" style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={skipWeekends}
                onChange={e => setSkipWeekends(e.target.checked)}
                className="accent-info-solid"
              />
              {p("skipWeekends")}
            </label>
          </div>

          {/* Shift times — pre-filled from WIC opening hours; editable */}
          <div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                {p("startTime")}
                <input type="time" value={shiftStart} onChange={e => setShiftStart(e.target.value)} className={inputCls} style={inputStyle} />
              </label>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                {p("endTime")}
                <input type="time" value={shiftEnd} onChange={e => setShiftEnd(e.target.value)} className={inputCls} style={inputStyle} />
              </label>
            </div>
            <div className="text-ink-soft" style={{ fontSize: 9, marginTop: 4 }}>
              {p("timesFromHours")}
            </div>
          </div>

          <div style={{ display: "flex", gap: 10, marginTop: 6 }}>
            <button
              type="button"
              onClick={onClose}
              className="bg-sunken border border-line-subtle text-ink"
              style={{ flex: 1, borderRadius: 6, padding: "8px 0", fontSize: 12, cursor: "pointer" }}
            >
              {p("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting || !employeeId || !locationCode || dates.length === 0}
              className="bg-info-solid"
              style={{
                flex: 2, border: "none", color: "#fff",
                borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600,
                cursor: submitting || !employeeId || !locationCode || dates.length === 0 ? "not-allowed" : "pointer",
                opacity: submitting || !employeeId || !locationCode || dates.length === 0 ? 0.6 : 1,
              }}
            >
              {submitting
                ? p("saving")
                : isRange
                  ? p("assignDays", { count: dates.length })
                  : p("save")}
            </button>
          </div>
        </form>

        {/* Move to GSD section */}
        {employeeId && dateFrom && (
          <>
            <div style={{ display: "flex", alignItems: "center", gap: 10, margin: "8px 0" }}>
              <div style={{ flex: 1, height: 1, background: "rgb(var(--line-subtle))" }} />
              <span className="text-ink-soft" style={{ fontSize: 11 }}>{p("or")}</span>
              <div style={{ flex: 1, height: 1, background: "rgb(var(--line-subtle))" }} />
            </div>
            <MoveToGsdBacklogAction
              employeeId={employeeId}
              agentName={employees.find(e => e.employeeId === employeeId)?.fullName ?? employeeId}
              shiftDate={dateFrom}
              shiftDateTo={dateTo}
              skipWeekends={skipWeekends}
              onSuccess={() => {
                const agentLabel = employees.find(e => e.employeeId === employeeId)?.fullName ?? employeeId
                setSuccess(isRange
                  ? String(t("wicShifts.moveToBacklog.successRange", { agent: agentLabel, count: dates.length }))
                  : String(t("wicShifts.moveToBacklog.success", { agent: agentLabel })))
                setTimeout(() => { setSuccess(null); onClose() }, 1800)
              }}
            >
              {({ onClick, isPending }) => (
                <button
                  type="button"
                  onClick={onClick}
                  disabled={isPending}
                  className="bg-transparent border border-warn-bd text-warn-fg"
                  style={{
                    width: "100%", borderRadius: 6, padding: "8px 0", fontSize: 12,
                    cursor: isPending ? "not-allowed" : "pointer",
                    opacity: isPending ? 0.6 : 1,
                  }}
                >
                  {isPending ? t("wicShifts.moveToBacklog.moving") as string : t("wicShifts.moveToBacklog.button") as string}
                </button>
              )}
            </MoveToGsdBacklogAction>
          </>
        )}
      </div>
    </div>
    {confirmDays && confirmDays.length > 0 && (
      <div style={{ position: "fixed", inset: 0, zIndex: 2500, background: "rgba(0,0,0,0.6)", display: "flex", alignItems: "center", justifyContent: "center" }}>
        <div className="bg-raised border border-warn-bd" style={{ borderRadius: 12, padding: 24, width: 460, maxWidth: "90vw" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
            <AlertTriangle size={16} className="text-warn-fg" />
            <span className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>{p("confirmTitle")}</span>
          </div>
          <ul className="text-ink-muted" style={{ fontSize: 12, lineHeight: 1.6, marginBottom: 18, paddingLeft: 16 }}>
            {confirmDays.flatMap(d => (d.conflicts ?? []).map((c, i) => (
              <li key={`${d.date}-${i}`}>
                {p("confirmBody", {
                  date: d.date,
                  loc: c.location,
                  shift: c.workingShift ?? "—",
                  actor: c.actor ?? p("confirmUnknownActor"),
                  at: c.atBerlin ?? "—",
                  target: locations.find(l => l.locationCode === locationCode)?.displayName ?? locationCode,
                })}
              </li>
            )))}
          </ul>
          <div style={{ display: "flex", gap: 10 }}>
            <button
              onClick={() => { setConfirmDays(null); setOutcome(null) }}
              className="bg-info-solid"
              style={{ flex: 2, border: "none", color: "#fff", borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600, cursor: "pointer" }}
            >
              {p("confirmCancel")}
            </button>
            <button
              onClick={confirmReplace}
              className="bg-transparent border border-warn-bd text-warn-fg"
              style={{ flex: 1, borderRadius: 6, padding: "8px 0", fontSize: 12, cursor: "pointer" }}
            >
              {p("confirmReplace")}
            </button>
          </div>
        </div>
      </div>
    )}
    {uncoveredWarn && (
      <div style={{ position: "fixed", inset: 0, zIndex: 2000, background: "rgba(0,0,0,0.6)", display: "flex", alignItems: "center", justifyContent: "center" }}>
        <div className="bg-raised border border-warn-bd" style={{ borderRadius: 12, padding: 24, width: 400, maxWidth: "90vw" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
            <AlertTriangle size={16} className="text-warn-fg" />
            <span className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>
              {p("uncoveredTitle")}
            </span>
          </div>
          <p className="text-ink-muted" style={{ fontSize: 13, marginBottom: 20 }}>
            {p("uncoveredBody", { agent: uncoveredWarn.agent, location: uncoveredWarn.location, date: uncoveredWarn.date })}
          </p>
          <div style={{ display: "flex", gap: 10 }}>
            <button
              onClick={() => setUncoveredWarn(null)}
              className="bg-info-solid"
              style={{ flex: 2, border: "none", color: "#fff", borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600, cursor: "pointer" }}
            >
              {p("cancel")}
            </button>
            <button
              onClick={() => { setUncoveredWarn(null); pendingProceed.current?.() }}
              className="bg-transparent border border-warn-bd text-warn-fg"
              style={{ flex: 1, borderRadius: 6, padding: "8px 0", fontSize: 12, cursor: "pointer" }}
            >
              {p("moveAnyway")}
            </button>
          </div>
        </div>
      </div>
    )}
    </>
  )
}
