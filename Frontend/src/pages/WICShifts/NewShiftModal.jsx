import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { maxFutureDateStr } from "../../constants"

const SHIFT_TYPES = [
  { value: "WORKING",   label: "Working" },
  { value: "WIC_DUTY",  label: "WIC Duty" },
  { value: "AL",        label: "Annual Leave (AL)" },
  { value: "HALF_AL",   label: "Half AL" },
  { value: "SL",        label: "Sick Leave (SL)" },
  { value: "UL",        label: "Unpaid Leave (UL)" },
  { value: "OL",        label: "Other Leave (OL)" },
  { value: "OFF",       label: "Off Day" },
  { value: "TRAINING",  label: "Training" },
  { value: "PH",        label: "Public Holiday (PH)" },
]

export default function NewShiftModal({ isOpen, onClose, onSuccess }) {
  const { t } = useTranslation()
  const p = (k, opts) => String(t(`wicShifts.newShift.${k}`, opts))

  const [employeeId, setEmployeeId] = useState("")
  const [date, setDate] = useState(new Date().toISOString().split("T")[0])
  const [shiftType, setShiftType] = useState("WORKING")
  const [shiftStart, setShiftStart] = useState("")
  const [shiftEnd, setShiftEnd] = useState("")
  const [locationCode, setLocationCode] = useState("")
  const [employees, setEmployees] = useState([])
  const [locations, setLocations] = useState([])
  const [submitting, setSubmitting] = useState(false)
  const [success, setSuccess] = useState(null)
  const [error, setError] = useState(null)

  useEffect(() => {
    if (!isOpen) return
    setEmployeeId(""); setDate(new Date().toISOString().split("T")[0])
    setShiftType("WORKING"); setShiftStart(""); setShiftEnd("")
    setLocationCode(""); setSuccess(null); setError(null)

    fetch("/api/employees/?active=true")
      .then(r => r.json())
      .then(setEmployees)
      .catch(() => {})

    fetch("/api/wic/locations")
      .then(r => r.json())
      .then(setLocations)
      .catch(() => {})
  }, [isOpen])

  const inputStyle = {
    width: "100%", background: "var(--card2)", border: "1px solid var(--border)",
    color: "var(--text)", padding: "6px 10px", borderRadius: 6,
    fontSize: 12, fontFamily: "IBM Plex Sans", outline: "none",
    boxSizing: "border-box", marginTop: 4,
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (!employeeId) return
    setSubmitting(true); setError(null); setSuccess(null)
    try {
      const res = await fetch("/api/wic/shifts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId, date, shiftType,
          shiftStart: shiftStart || null,
          shiftEnd: shiftEnd || null,
          agentTask: null,
          locationCode: locationCode || null,
        }),
      })
      if (!res.ok) {
        const body = await res.text().catch(() => "")
        throw new Error(`HTTP ${res.status}${body ? ": " + body : ""}`)
      }
      const data = await res.json()
      setSuccess(p("success", { name: data.employeeName }))
      onSuccess?.()
      setTimeout(() => { setSuccess(null); onClose() }, 1800)
    } catch (err) {
      setError(String(err))
    } finally {
      setSubmitting(false)
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
          borderRadius: 12, padding: 24, width: 440, maxWidth: "90vw",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ fontSize: 15, fontWeight: 600, color: "var(--text)", marginBottom: 20 }}>
          {p("title")}
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

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              {p("date")}
              <input
                type="date" value={date} max={maxFutureDateStr()}
                onChange={e => setDate(e.target.value)}
                required style={inputStyle}
              />
            </label>
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              {p("shiftType")}
              <select value={shiftType} onChange={e => setShiftType(e.target.value)} style={inputStyle}>
                {SHIFT_TYPES.map(s => (
                  <option key={s.value} value={s.value}>{s.label}</option>
                ))}
              </select>
            </label>
          </div>

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

          {shiftType === "WIC_DUTY" && (
            <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
              {p("location")}
              <select value={locationCode} onChange={e => setLocationCode(e.target.value)} style={inputStyle}>
                <option value="">{p("selectLocation")}</option>
                {locations.map(l => (
                  <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>
                ))}
              </select>
            </label>
          )}

          <div style={{ display: "flex", gap: 10, marginTop: 6 }}>
            <button
              type="button" onClick={onClose}
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
              disabled={submitting || !employeeId}
              style={{
                flex: 2, background: "var(--accent)", border: "none", color: "#fff",
                borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600,
                cursor: submitting || !employeeId ? "not-allowed" : "pointer",
                opacity: submitting || !employeeId ? 0.6 : 1,
              }}
            >
              {submitting ? p("saving") : p("save")}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
