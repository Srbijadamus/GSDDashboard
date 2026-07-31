import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Users } from "lucide-react"
import { NppBadge } from "../components/NppBadge"
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
        .then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() })
        .then((rows: { locationCode: string; displayName: string }[]) =>
          rows.map(r => ({ locationCode: r.locationCode, displayName: r.displayName }))
        ),
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

  const inputStyle: React.CSSProperties = {
    width: "100%",
    background: "var(--card2)",
    border: "1px solid var(--border)",
    color: "var(--text)",
    padding: "6px 10px",
    borderRadius: 6,
    fontSize: 12,
    fontFamily: "IBM Plex Sans",
    outline: "none",
    boxSizing: "border-box",
    marginTop: 4,
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!employeeId || !locationCode || dates.length === 0) return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    setNppWarn(null)

    let lastDisplayName = ""
    let failed = 0
    let skipped = 0
    let firstNppWarning: string | null = null

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
        if (data.skipped) { skipped++; continue }
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
      const msg = isRange
        ? skipped > 0
          ? `${assigned} day${assigned !== 1 ? "s" : ""} assigned to ${lastDisplayName} (${skipped} skipped — non-working days)`
          : `${assigned} day${assigned !== 1 ? "s" : ""} assigned to ${lastDisplayName}`
        : p("success", { loc: lastDisplayName })
      setSuccess(msg)
      if (firstNppWarning) setNppWarn(firstNppWarning)
      queryClient.refetchQueries({ queryKey: ["wic-forecast"] })
      queryClient.refetchQueries({ queryKey: ["wic-cards", dateFrom], exact: true })
      setTimeout(() => { setSuccess(null); setNppWarn(null); onClose() }, firstNppWarning ? 5000 : 1800)
    }
  }

  if (!isOpen) return null

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 1000,
        background: "rgba(0,0,0,0.55)",
        display: "flex", alignItems: "center", justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "var(--card)", border: "1px solid var(--border)",
          borderRadius: 12, padding: 24, width: 420, maxWidth: "90vw",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
          <Users size={16} color="var(--accent)" />
          <span style={{ fontSize: 15, fontWeight: 600, color: "var(--text)" }}>{p("title")}</span>
        </div>

        {success && (
          <div style={{
            background: "rgba(34,208,122,.12)", border: "1px solid rgba(34,208,122,.3)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--green)", marginBottom: 14,
          }}>
            {success}
          </div>
        )}
        {nppWarn && (
          <div style={{
            background: "rgba(239,68,68,.12)", border: "1px solid rgba(239,68,68,.4)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "#ef4444", marginBottom: 14,
            display: "flex", alignItems: "flex-start", gap: 8,
          }}>
            <span style={{ fontWeight: 700, flexShrink: 0 }}>⚠ NPP Warning:</span>
            <span>{nppWarn}</span>
          </div>
        )}
        {error && (
          <div style={{
            background: "rgba(255,59,92,.12)", border: "1px solid rgba(255,59,92,.3)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--danger)", marginBottom: 14,
          }}>
            {error}
          </div>
        )}
        {progress && (
          <div style={{
            background: "rgba(99,102,241,.08)", border: "1px solid rgba(99,102,241,.25)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--accent)", marginBottom: 14,
          }}>
            {progress}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
            {p("employee")}
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} required style={inputStyle}>
              <option value="">{p("selectEmployee")}</option>
              {employees.map(e => (
                <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
              ))}
            </select>
          </label>

          <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
            {p("location")}
            <select value={locationCode} onChange={e => setLocationCode(e.target.value)} required style={inputStyle}>
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
              background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.3)",
              borderRadius: 6, padding: "7px 11px", fontSize: 11, color: "#ef4444",
              display: "flex", alignItems: "center", gap: 6,
            }}>
              <NppBadge />
              NPP site — only NPP-qualified agents may be assigned here.
            </div>
          )}

          {closedDay && (
            <div style={{
              background: "rgba(255,59,92,.08)", border: "1px solid rgba(255,59,92,.25)",
              borderRadius: 6, padding: "7px 11px", fontSize: 11, color: "var(--danger)",
            }}>
              ⚠ WIC centre is closed on this weekday — assignment will be skipped.
              / Standort an diesem Wochentag geschlossen — Einsatz wird übersprungen.
            </div>
          )}

          {/* Date range */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              Von / From
              <input
                type="date"
                value={dateFrom}
                onChange={e => {
                  setDateFrom(e.target.value)
                  if (e.target.value > dateTo) setDateTo(e.target.value)
                }}
                required
                style={inputStyle}
              />
            </label>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              Bis / To
              <input
                type="date"
                value={dateTo}
                min={dateFrom}
                onChange={e => setDateTo(e.target.value)}
                required
                style={inputStyle}
              />
            </label>
          </div>

          {/* Day count badge + skip weekends */}
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>
              {dates.length === 0
                ? <span style={{ color: "var(--danger)" }}>Keine Arbeitstage / No working days in range</span>
                : <span><span style={{ fontWeight: 600, color: "var(--accent)" }}>{dates.length}</span> Tag{dates.length !== 1 ? "e" : ""} / day{dates.length !== 1 ? "s" : ""} ausgewählt / selected</span>}
            </span>
            <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, color: "var(--text3)", cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={skipWeekends}
                onChange={e => setSkipWeekends(e.target.checked)}
                style={{ accentColor: "var(--accent)" }}
              />
              Wochenenden überspringen / Skip weekends
            </label>
          </div>

          {/* Shift times — pre-filled from WIC opening hours; editable */}
          <div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
                Beginn / {p("startTime")}
                <input type="time" value={shiftStart} onChange={e => setShiftStart(e.target.value)} style={inputStyle} />
              </label>
              <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
                Ende / {p("endTime")}
                <input type="time" value={shiftEnd} onChange={e => setShiftEnd(e.target.value)} style={inputStyle} />
              </label>
            </div>
            <div style={{ fontSize: 9, color: "var(--text3)", marginTop: 4 }}>
              Aus Öffnungszeiten vorausgefüllt / Pre-filled from opening hours — änderbar / editable
            </div>
          </div>

          <div style={{ display: "flex", gap: 10, marginTop: 6 }}>
            <button
              type="button"
              onClick={onClose}
              style={{
                flex: 1, background: "var(--card2)", border: "1px solid var(--border)",
                color: "var(--text)", borderRadius: 6, padding: "8px 0",
                fontSize: 12, cursor: "pointer",
              }}
            >
              {p("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting || !employeeId || !locationCode || dates.length === 0}
              style={{
                flex: 2, background: "var(--accent)", border: "none", color: "#fff",
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
      </div>
    </div>
  )
}
