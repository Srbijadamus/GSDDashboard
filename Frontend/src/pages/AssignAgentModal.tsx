import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Users } from "lucide-react"

interface Employee {
  employeeId: string
  fullName: string | null
}

interface WicLocation {
  locationCode: string
  displayName: string
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
  const [error, setError]                 = useState<string | null>(null)

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
      setError(null)
      setProgress(null)
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

    let lastDisplayName = ""
    let failed = 0
    let skipped = 0

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
      queryClient.refetchQueries({ queryKey: ["wic-forecast"] })
      queryClient.refetchQueries({ queryKey: ["wic-cards", dateFrom], exact: true })
      setTimeout(() => { setSuccess(null); onClose() }, 1800)
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
                <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>
              ))}
            </select>
          </label>

          {/* Date range */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              From
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
              To
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
                ? <span style={{ color: "var(--danger)" }}>No working days in range</span>
                : <span><span style={{ fontWeight: 600, color: "var(--accent)" }}>{dates.length}</span> day{dates.length !== 1 ? "s" : ""} selected</span>}
            </span>
            <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, color: "var(--text3)", cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={skipWeekends}
                onChange={e => setSkipWeekends(e.target.checked)}
                style={{ accentColor: "var(--accent)" }}
              />
              Skip weekends
            </label>
          </div>

          {/* Shift times */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              {p("startTime")}
              <input type="time" value={shiftStart} onChange={e => setShiftStart(e.target.value)} style={inputStyle} />
            </label>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              {p("endTime")}
              <input type="time" value={shiftEnd} onChange={e => setShiftEnd(e.target.value)} style={inputStyle} />
            </label>
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
                  ? `Assign ${dates.length} days`
                  : p("save")}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
