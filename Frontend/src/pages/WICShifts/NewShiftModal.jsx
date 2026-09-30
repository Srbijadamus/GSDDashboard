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

// prefill (optional): { locationCode, displayName, date } — set when the modal
// is opened from a coverage-gap row or an UNCOVERED WIC Shifts card. Locks
// date/location/shift type to that gap and swaps the plain employee list for
// the ordered candidates from /api/wic/assign-candidates (floaters, then the
// location's own MAIN/BACKUP, then everyone else — absent ones greyed out via
// native <option disabled>, with the reason shown).
export default function NewShiftModal({ isOpen, onClose, onSuccess, prefill }) {
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
  const [candidates, setCandidates] = useState([])
  const [submitting, setSubmitting] = useState(false)
  const [success, setSuccess] = useState(null)
  const [error, setError] = useState(null)
  const [warnings, setWarnings] = useState(null) // soft violations after a successful save
  const [blocked, setBlocked] = useState(null)   // hard-block violations — save refused

  useEffect(() => {
    if (!isOpen) return
    setSuccess(null); setError(null); setWarnings(null); setBlocked(null)

    if (prefill) {
      setEmployeeId("")
      setDate(prefill.date)
      setShiftType("WIC_DUTY")
      setShiftStart(""); setShiftEnd("")
      setLocationCode(prefill.locationCode)
      fetch(`/api/wic/assign-candidates?locationCode=${encodeURIComponent(prefill.locationCode)}&date=${prefill.date}`)
        .then(r => r.json())
        .then(d => setCandidates(d.candidates ?? []))
        .catch(() => setCandidates([]))
      return
    }

    setEmployeeId(""); setDate(new Date().toISOString().split("T")[0])
    setShiftType("WORKING"); setShiftStart(""); setShiftEnd("")
    setLocationCode("")

    fetch("/api/employees/?active=true")
      .then(r => r.json())
      .then(setEmployees)
      .catch(() => {})

    fetch("/api/wic/locations")
      .then(r => r.json())
      .then(setLocations)
      .catch(() => {})
  }, [isOpen, prefill])

  const inputCls = "bg-sunken border border-line-subtle text-ink"
  const inputStyle = {
    width: "100%", padding: "6px 10px", borderRadius: 6,
    fontSize: 12, fontFamily: "IBM Plex Sans", outline: "none",
    boxSizing: "border-box", marginTop: 4,
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (!employeeId) return
    setSubmitting(true); setError(null); setSuccess(null); setWarnings(null); setBlocked(null)
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

      // Hard block (R1/R2/R3/R5): ShiftValidationService refused it before
      // anything was saved — nothing to undo, just show why and let the user
      // adjust or cancel. Never silently proceeds.
      if (data.blocked) {
        setBlocked(data.violations ?? [])
        return
      }

      // Soft violations (R4 Sunday, R7 public holiday, R6 student hours): the
      // shift IS already saved (validation never blocks these) — show them as
      // an acknowledgeable warning instead of auto-closing.
      onSuccess?.()
      if (data.violations?.length > 0) {
        setWarnings(data.violations)
        setSuccess(p("success", { name: data.employeeName }))
      } else {
        setSuccess(p("success", { name: data.employeeName }))
        setTimeout(() => { setSuccess(null); onClose() }, 1800)
      }
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
        className="bg-raised border border-line-subtle"
        style={{
          borderRadius: 12, padding: 24, width: 440, maxWidth: "90vw",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div className="text-ink" style={{ fontSize: 15, fontWeight: 600, marginBottom: prefill ? 4 : 20 }}>
          {prefill ? "Assign to gap" : p("title")}
        </div>
        {prefill && (
          <div className="text-ink-soft" style={{ fontSize: 12, marginBottom: 16, fontFamily: "monospace" }}>
            {prefill.displayName} · {prefill.date}
          </div>
        )}

        {success && (
          <div className="bg-good-bg border border-good-bd text-good-fg" style={{
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }}>
            {success}
          </div>
        )}
        {warnings && warnings.length > 0 && (
          <div className="bg-warn-bg border border-warn-bd" style={{
            borderRadius: 8, padding: "10px 14px", fontSize: 11, marginBottom: 14, color: "rgb(var(--st-warn-solid))",
          }}>
            <div style={{ fontWeight: 700, marginBottom: 4 }}>Shift created — with a warning:</div>
            {warnings.map((v, i) => (
              <div key={i} style={{ marginBottom: 2 }}><b>{v.rule}</b>: {v.description}</div>
            ))}
            <button type="button" onClick={() => { setWarnings(null); setSuccess(null); onClose() }}
              className="bg-warn-solid" style={{ marginTop: 8, border: "none", color: "#fff", borderRadius: 5, padding: "5px 12px", fontSize: 11, cursor: "pointer" }}>
              OK
            </button>
          </div>
        )}
        {blocked && (
          <div className="bg-crit-bg border-crit-bd text-crit-fg" style={{
            border: "1px solid", borderRadius: 8, padding: "10px 14px", fontSize: 11, marginBottom: 14,
          }}>
            <div style={{ fontWeight: 700, marginBottom: 4 }}>Not created — blocked:</div>
            {blocked.map((v, i) => (
              <div key={i} style={{ marginBottom: 2 }}><b>{v.rule}</b>: {v.description}</div>
            ))}
          </div>
        )}
        {error && (
          <div className="bg-crit-bg border-crit-bd text-crit-fg" style={{
            border: "1px solid", borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }}>
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("employee")}
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} required className={inputCls} style={inputStyle}>
              <option value="">{p("selectEmployee")}</option>
              {prefill ? (
                ["FLOATER", "OWN", "OTHER"].map(cat => {
                  const items = candidates.filter(c => c.category === cat)
                  if (items.length === 0) return null
                  const label = cat === "FLOATER" ? "Floater (available)"
                    : cat === "OWN" ? `${prefill.displayName} agents`
                    : "Everyone else"
                  return (
                    <optgroup key={cat} label={label}>
                      {items.map(c => (
                        <option key={c.employeeId} value={c.employeeId} disabled={!c.available}>
                          {c.fullName}{cat === "FLOATER" ? " (Floater)" : ""}{!c.available ? ` — ${c.absenceReason}` : ""}
                        </option>
                      ))}
                    </optgroup>
                  )
                })
              ) : (
                employees.map(e => (
                  <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
                ))
              )}
            </select>
          </label>

          {!prefill && (
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                {p("date")}
                <input
                  type="date" value={date} max={maxFutureDateStr()}
                  onChange={e => setDate(e.target.value)}
                  required className={inputCls} style={inputStyle}
                />
              </label>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
                {p("shiftType")}
                <select value={shiftType} onChange={e => setShiftType(e.target.value)} className={inputCls} style={inputStyle}>
                  {SHIFT_TYPES.map(s => (
                    <option key={s.value} value={s.value}>{s.label}</option>
                  ))}
                </select>
              </label>
            </div>
          )}

          {!prefill && (
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
          )}

          {!prefill && shiftType === "WIC_DUTY" && (
            <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
              {p("location")}
              <select value={locationCode} onChange={e => setLocationCode(e.target.value)} className={inputCls} style={inputStyle}>
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
              className="bg-sunken border border-line-subtle text-ink"
              style={{
                flex: 1, borderRadius: 6, padding: "8px 0",
                fontSize: 12, cursor: "pointer",
              }}
            >
              {p("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting || !employeeId}
              className="bg-info-solid"
              style={{
                flex: 2, border: "none", color: "#fff",
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
