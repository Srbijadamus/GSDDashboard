import { useState, useEffect, useRef } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Users, AlertTriangle } from "lucide-react"
import { NppBadge } from "../components/NppBadge"
import { MoveToGsdBacklogAction } from "../components/MoveToGsdBacklogAction"
import { SearchableSelect } from "../components/SearchableSelect"
import { apiFetch } from "../api/client"

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

type SkipReasonKind = "closedDay" | "blockingShift" | "timeConflict" | "other"

// Classifies the `reason` string returned by POST /api/wic/assignments —
// see BLUEPRINT_LOGIC.md §7.2 for the three skip reasons this endpoint returns.
function classifySkipReason(reason: string | undefined): SkipReasonKind {
  if (!reason) return "other"
  if (reason.startsWith("WIC location is closed")) return "closedDay"
  if (reason.startsWith("Agent has shift type")) return "blockingShift"
  if (reason.startsWith("Time conflict")) return "timeConflict"
  return "other"
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

  const { data: openingHours = [] } = useQuery<any[]>({
    queryKey: ["wic-opening-hours"],
    queryFn: () => apiFetch<any[]>("/api/wicschedule/opening-hours"),
    staleTime: 60 * 1000,
    enabled: isOpen,
  })

  // Auto-populate times and detect closed day whenever location or from-date changes
  useEffect(() => {
    if (!locationCode || !dateFrom || openingHours.length === 0) { setClosedDay(false); return }
    const dow = new Date(dateFrom + "T00:00:00").getDay() // 0=Sun..6=Sat, same as .NET DayOfWeek
    const locHours = openingHours.find((l: any) => l.locationCode === locationCode)
    const dayHours = locHours?.weeklyHours?.find((d: any) => d.dayOfWeek === dow)
    if (!dayHours || dayHours.isClosed) {
      setClosedDay(true)
      setShiftStart("")
      setShiftEnd("")
    } else {
      setClosedDay(false)
      setShiftStart(dayHours.openTime ?? "")
      setShiftEnd(dayHours.closeTime ?? "")
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [locationCode, dateFrom, openingHours])

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

  const proceedWithAssignment = async () => {
    let lastDisplayName = ""
    let failed = 0
    let skipped = 0
    let firstNppWarning: string | null = null
    const skipCounts: Record<SkipReasonKind, number> = { closedDay: 0, blockingShift: 0, timeConflict: 0, other: 0 }

    for (let i = 0; i < dates.length; i++) {
      const d = dates[i]
      setProgress(`Assigning ${i + 1} / ${dates.length} (${d})…`)
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
          }),
        })
        if (!res.ok) {
          const body = await res.text().catch(() => "")
          throw new Error(`HTTP ${res.status}${body ? ": " + body : ""}`)
        }
        const data = await res.json()
        if (data.skipped) { skipped++; skipCounts[classifySkipReason(data.reason)]++; continue }
        lastDisplayName = data.displayName ?? lastDisplayName
        if (data.nppWarning && !firstNppWarning) firstNppWarning = data.nppWarning
      } catch (err) {
        failed++
        setError(`Failed on ${d}: ${String(err)}`)
        break
      }
    }

    setSubmitting(false)
    setProgress(null)

    if (failed === 0) {
      const assigned = dates.length - skipped
      const skipSummary = (Object.keys(skipCounts) as SkipReasonKind[])
        .filter(kind => skipCounts[kind] > 0)
        .map(kind => p(`skipReasons.${kind}`, { count: skipCounts[kind] }))
        .join(", ")
      const msg = isRange
        ? skipped > 0
          ? p("summaryRangeWithSkips", { count: assigned, loc: lastDisplayName, skipSummary })
          : p("summaryRange", { count: assigned, loc: lastDisplayName })
        : p("success", { loc: lastDisplayName })
      setSuccess(msg)
      if (firstNppWarning) setNppWarn(firstNppWarning)
      queryClient.refetchQueries({ queryKey: ["wic-forecast"] })
      queryClient.refetchQueries({ queryKey: ["wic-cards", dateFrom], exact: true })
      setTimeout(() => { setSuccess(null); setNppWarn(null); onClose() }, firstNppWarning ? 5000 : 1800)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!employeeId || !locationCode || dates.length === 0) return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    setNppWarn(null)

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
          borderRadius: 12, padding: 24, width: 420, maxWidth: "90vw",
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
              <span>WIC centre is closed on this weekday — assignment will be skipped.
              / Standort an diesem Wochentag geschlossen — Einsatz wird übersprungen.</span>
            </div>
          )}

          {/* Date range */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
              Von / From
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
              Bis / To
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
                ? <span className="text-crit-fg">Keine Arbeitstage / No working days in range</span>
                : <span><span className="text-info-fg" style={{ fontWeight: 600 }}>{dates.length}</span> Tag{dates.length !== 1 ? "e" : ""} / day{dates.length !== 1 ? "s" : ""} ausgewählt / selected</span>}
            </span>
            <label className="text-ink-soft" style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={skipWeekends}
                onChange={e => setSkipWeekends(e.target.checked)}
                className="accent-info-solid"
              />
              Wochenenden überspringen / Skip weekends
            </label>
          </div>

          {/* Shift times — pre-filled from WIC opening hours; editable */}
          <div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                Beginn / {p("startTime")}
                <input type="time" value={shiftStart} onChange={e => setShiftStart(e.target.value)} className={inputCls} style={inputStyle} />
              </label>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                Ende / {p("endTime")}
                <input type="time" value={shiftEnd} onChange={e => setShiftEnd(e.target.value)} className={inputCls} style={inputStyle} />
              </label>
            </div>
            <div className="text-ink-soft" style={{ fontSize: 9, marginTop: 4 }}>
              Aus Öffnungszeiten vorausgefüllt / Pre-filled from opening hours — änderbar / editable
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
                  ? `${dates.length} Tage zuweisen / Assign ${dates.length} days`
                  : p("save")}
            </button>
          </div>
        </form>

        {/* Move to GSD section */}
        {employeeId && dateFrom && (
          <>
            <div style={{ display: "flex", alignItems: "center", gap: 10, margin: "8px 0" }}>
              <div style={{ flex: 1, height: 1, background: "rgb(var(--line-subtle))" }} />
              <span className="text-ink-soft" style={{ fontSize: 11 }}>or</span>
              <div style={{ flex: 1, height: 1, background: "rgb(var(--line-subtle))" }} />
            </div>
            <MoveToGsdBacklogAction
              employeeId={employeeId}
              agentName={employees.find(e => e.employeeId === employeeId)?.fullName ?? employeeId}
              shiftDate={dateFrom}
              onSuccess={() => {
                setSuccess(String(t("wicShifts.moveToBacklog.success", { agent: employees.find(e => e.employeeId === employeeId)?.fullName ?? employeeId })))
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
    {uncoveredWarn && (
      <div style={{ position: "fixed", inset: 0, zIndex: 2000, background: "rgba(0,0,0,0.6)", display: "flex", alignItems: "center", justifyContent: "center" }}>
        <div className="bg-raised border border-warn-bd" style={{ borderRadius: 12, padding: 24, width: 400, maxWidth: "90vw" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
            <AlertTriangle size={16} className="text-warn-fg" />
            <span className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>
              Location will be uncovered / Standort bleibt unbesetzt
            </span>
          </div>
          <p className="text-ink-muted" style={{ fontSize: 13, marginBottom: 20 }}>
            Moving <strong className="text-ink">{uncoveredWarn.agent}</strong> leaves{" "}
            <strong className="text-ink">{uncoveredWarn.location}</strong> uncovered on{" "}
            <strong className="text-ink font-mono">{uncoveredWarn.date}</strong>.
            <br />
            Das Verschieben von <strong className="text-ink">{uncoveredWarn.agent}</strong> lässt{" "}
            <strong className="text-ink">{uncoveredWarn.location}</strong> am{" "}
            <strong className="text-ink font-mono">{uncoveredWarn.date}</strong> unbesetzt.
          </p>
          <div style={{ display: "flex", gap: 10 }}>
            <button
              onClick={() => setUncoveredWarn(null)}
              className="bg-info-solid"
              style={{ flex: 2, border: "none", color: "#fff", borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600, cursor: "pointer" }}
            >
              Cancel / Abbrechen
            </button>
            <button
              onClick={() => { setUncoveredWarn(null); pendingProceed.current?.() }}
              className="bg-transparent border border-warn-bd text-warn-fg"
              style={{ flex: 1, borderRadius: 6, padding: "8px 0", fontSize: 12, cursor: "pointer" }}
            >
              Move anyway / Trotzdem verschieben
            </button>
          </div>
        </div>
      </div>
    )}
    </>
  )
}
