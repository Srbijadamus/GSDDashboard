import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { X, Plus, Trash2 } from "lucide-react"

// ─── Types ────────────────────────────────────────────────────────────────────

interface DateRange { from: string; to: string }

interface ALBestSubstitute {
  employeeId: string
  fullName: string
  sourceType: string
  distanceKm: number | null
  reachabilityTier: string | null
}

interface ALConflict {
  employeeId: string
  fullName: string
  absenceType: string
  locationCode: string
  locationName: string
}

interface ALLocationDay {
  locationCode: string
  displayName: string
  coverageStatus: string
  present: number
  required: number
  gap: number
  bestSubstitute: ALBestSubstitute | null
}

interface ALDayResult {
  date: string
  dayOfWeek: string
  locations: ALLocationDay[]
  conflicts: ALConflict[]
}

interface ALRangeResult {
  from: string
  to: string
  totalDays: number
  atRiskDays: number
  days: ALDayResult[]
}

interface ALPlanningResponse {
  employeeId: string
  fullName: string | null
  generatedAt: string
  dateRanges: ALRangeResult[]
  note: string
}

interface EmployeeRow {
  employeeId: string
  fullName: string | null
  primaryRole: string | null
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

const STATUS_STYLES: Record<string, { bg: string; color: string; label: string }> = {
  COVERED:   { bg: "rgba(0,210,160,.15)",  color: "#34d399", label: "covered" },
  PARTIAL:   { bg: "rgba(255,186,0,.15)",  color: "#fbbf24", label: "partial" },
  UNCOVERED: { bg: "rgba(255,59,92,.15)",  color: "#f87171", label: "uncovered" },
  CLOSED:    { bg: "rgba(120,120,140,.12)", color: "var(--text3)", label: "closed" },
}

const SOURCE_COLORS: Record<string, { bg: string; color: string }> = {
  BACKUP:    { bg: "rgba(124,58,237,.15)", color: "#a78bfa" },
  SSP:       { bg: "rgba(59,126,255,.15)", color: "#60a5fa" },
  WIC_DONOR: { bg: "rgba(0,210,160,.15)", color: "#34d399" },
  CALL_IN:   { bg: "rgba(255,124,59,.15)", color: "#fb923c" },
}

const ABSENCE_COLORS: Record<string, { bg: string; color: string }> = {
  AL:       { bg: "rgba(59,126,255,.15)", color: "#60a5fa" },
  HALF_AL:  { bg: "rgba(59,126,255,.15)", color: "#60a5fa" },
  SL:       { bg: "rgba(255,59,92,.15)",  color: "#f87171" },
  UL:       { bg: "rgba(120,120,140,.12)", color: "var(--text3)" },
  TRAINING: { bg: "rgba(255,186,0,.15)",  color: "#fbbf24" },
}

function formatDate(iso: string) {
  const d = new Date(iso + "T00:00:00")
  return d.toLocaleDateString(undefined, { day: "2-digit", month: "short" })
}

function shortDay(day: string) {
  return day.slice(0, 3)
}

// ─── Component ────────────────────────────────────────────────────────────────

export function ALPlanningModal({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const p = (k: string, opts?: Record<string, unknown>): string => String(t(`attendance.alPlanning.${k}`, opts as never))

  const [employees, setEmployees] = useState<EmployeeRow[]>([])
  const [selectedEmpId, setSelectedEmpId] = useState("")
  const [ranges, setRanges] = useState<DateRange[]>([{ from: "", to: "" }])
  const [loading, setLoading] = useState(false)
  const [result, setResult] = useState<ALPlanningResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!isOpen) return
    fetch("/api/employees/?active=true")
      .then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() })
      .then((data: EmployeeRow[]) =>
        setEmployees(data.filter(e => e.fullName).sort((a, b) =>
          (a.fullName ?? "").localeCompare(b.fullName ?? "")))
      )
      .catch(() => {})
  }, [isOpen])

  if (!isOpen) return null

  const addRange = () => setRanges(prev => [...prev, { from: "", to: "" }])
  const removeRange = (i: number) => setRanges(prev => prev.filter((_, idx) => idx !== i))
  const updateRange = (i: number, field: "from" | "to", value: string) =>
    setRanges(prev => prev.map((r, idx) => idx === i ? { ...r, [field]: value } : r))

  const handleSubmit = async () => {
    if (!selectedEmpId) { setError(p("selectEmployee")); return }
    const validRanges = ranges.filter(r => r.from && r.to)
    if (validRanges.length === 0) { setError("Add at least one complete date range."); return }
    setLoading(true); setError(null); setResult(null)
    try {
      const res = await fetch("/api/wic/al-planning", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ employeeId: selectedEmpId, dateRanges: validRanges }),
      })
      if (!res.ok) {
        const body = await res.text().catch(() => "")
        throw new Error(`HTTP ${res.status}${body ? ": " + body : ""}`)
      }
      setResult(await res.json())
    } catch (err) {
      setError(String(err))
    } finally {
      setLoading(false)
    }
  }

  const handleClose = () => {
    setResult(null); setError(null)
    setRanges([{ from: "", to: "" }])
    setSelectedEmpId("")
    onClose()
  }

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 1000,
        background: "rgba(0,0,0,.55)", backdropFilter: "blur(4px)",
        display: "flex", alignItems: "center", justifyContent: "center",
        padding: 20,
      }}
      onClick={e => { if (e.target === e.currentTarget) handleClose() }}
    >
      <div style={{
        background: "var(--sidebar)", border: "1px solid var(--border)",
        borderRadius: 14, width: "100%", maxWidth: 860,
        maxHeight: "90vh", display: "flex", flexDirection: "column",
        boxShadow: "0 24px 64px rgba(0,0,0,.5)",
      }}>
        {/* Header */}
        <div style={{
          display: "flex", alignItems: "center", justifyContent: "space-between",
          padding: "18px 22px", borderBottom: "1px solid var(--border)", flexShrink: 0,
        }}>
          <div>
            <div style={{ fontWeight: 600, fontSize: 15, color: "var(--text)" }}>{p("title")}</div>
            <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 2 }}>
              Simulation only — no changes are made to the schedule
            </div>
          </div>
          <button
            onClick={handleClose}
            style={{
              background: "none", border: "none", cursor: "pointer",
              color: "var(--text3)", padding: 4, borderRadius: 6, lineHeight: 0,
            }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Scrollable body */}
        <div style={{ overflowY: "auto", flex: 1, padding: "20px 22px" }}>

          {/* ── Form ── */}
          <div style={{
            background: "var(--card)", border: "1px solid var(--border)",
            borderRadius: 10, padding: 16, marginBottom: 20,
          }}>
            {/* Employee selector */}
            <div style={{ marginBottom: 14 }}>
              <label style={{ fontSize: 11, color: "var(--text3)", display: "block", marginBottom: 5 }}>
                {p("employee")}
              </label>
              <select
                value={selectedEmpId}
                onChange={e => setSelectedEmpId(e.target.value)}
                style={{
                  width: "100%", background: "var(--sidebar)", border: "1px solid var(--border)",
                  borderRadius: 7, color: "var(--text)", fontSize: 13, padding: "7px 10px",
                  outline: "none",
                }}
              >
                <option value="">{p("selectEmployee")}</option>
                {employees.map(e => (
                  <option key={e.employeeId} value={e.employeeId}>
                    {e.fullName}{e.primaryRole ? ` · ${e.primaryRole}` : ""}
                  </option>
                ))}
              </select>
            </div>

            {/* Date ranges */}
            <div>
              <label style={{ fontSize: 11, color: "var(--text3)", display: "block", marginBottom: 8 }}>
                {p("dateRanges")}
              </label>
              {ranges.map((r, i) => (
                <div key={i} style={{ display: "flex", gap: 8, alignItems: "center", marginBottom: 8 }}>
                  <div style={{ flex: 1 }}>
                    <span style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>
                      {p("from")}
                    </span>
                    <input
                      type="date"
                      value={r.from}
                      onChange={e => updateRange(i, "from", e.target.value)}
                      style={{
                        width: "100%", background: "var(--sidebar)", border: "1px solid var(--border)",
                        borderRadius: 7, color: "var(--text)", fontSize: 13, padding: "6px 10px",
                        outline: "none",
                      }}
                    />
                  </div>
                  <div style={{ flex: 1 }}>
                    <span style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>
                      {p("to")}
                    </span>
                    <input
                      type="date"
                      value={r.to}
                      min={r.from || undefined}
                      onChange={e => updateRange(i, "to", e.target.value)}
                      style={{
                        width: "100%", background: "var(--sidebar)", border: "1px solid var(--border)",
                        borderRadius: 7, color: "var(--text)", fontSize: 13, padding: "6px 10px",
                        outline: "none",
                      }}
                    />
                  </div>
                  {ranges.length > 1 && (
                    <button
                      onClick={() => removeRange(i)}
                      style={{
                        marginTop: 18, background: "none", border: "none", cursor: "pointer",
                        color: "var(--text3)", padding: 4, borderRadius: 6, lineHeight: 0,
                      }}
                    >
                      <Trash2 size={14} />
                    </button>
                  )}
                </div>
              ))}

              <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
                {ranges.length < 5 && (
                  <button
                    onClick={addRange}
                    style={{
                      background: "none", border: "1px dashed var(--border)", borderRadius: 7,
                      color: "var(--text3)", fontSize: 12, padding: "5px 12px",
                      cursor: "pointer", display: "flex", alignItems: "center", gap: 5,
                    }}
                  >
                    <Plus size={12} />
                    {p("addRange")}
                  </button>
                )}
                <button
                  onClick={handleSubmit}
                  disabled={loading}
                  style={{
                    background: loading ? "var(--border)" : "var(--accent)",
                    border: "none", borderRadius: 7, color: "#fff",
                    fontSize: 13, fontWeight: 600, padding: "6px 18px",
                    cursor: loading ? "not-allowed" : "pointer",
                    opacity: loading ? 0.7 : 1,
                  }}
                >
                  {loading ? p("loading") : p("check")}
                </button>
              </div>
            </div>
          </div>

          {/* Error */}
          {error && (
            <div style={{
              background: "rgba(255,59,92,.12)", border: "1px solid rgba(255,59,92,.3)",
              borderRadius: 8, padding: "10px 14px", fontSize: 12,
              color: "var(--danger)", marginBottom: 16,
            }}>
              {error}
            </div>
          )}

          {/* ── Results ── */}
          {result && (
            <div>
              {/* Employee + note */}
              <div style={{
                display: "flex", alignItems: "flex-start", justifyContent: "space-between",
                marginBottom: 16, gap: 12,
              }}>
                <div>
                  <div style={{ fontWeight: 600, fontSize: 14, color: "var(--text)" }}>
                    {result.fullName ?? result.employeeId}
                  </div>
                  <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 2 }}>
                    {result.note}
                  </div>
                </div>
              </div>

              {/* Per-range sections */}
              {result.dateRanges.map((range, ri) => (
                <div key={ri} style={{ marginBottom: 24 }}>
                  {/* Range header */}
                  <div style={{
                    display: "flex", alignItems: "center", gap: 10,
                    marginBottom: 10,
                  }}>
                    <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
                      {formatDate(range.from)} – {formatDate(range.to)}
                    </span>
                    <span style={{ fontSize: 11, color: "var(--text3)" }}>
                      {range.totalDays} day(s)
                    </span>
                    {range.atRiskDays > 0 ? (
                      <span style={{
                        background: "rgba(255,59,92,.15)", color: "#f87171",
                        fontSize: 11, padding: "2px 8px", borderRadius: 20, fontWeight: 600,
                      }}>
                        {p("atRisk", { n: range.atRiskDays })}
                      </span>
                    ) : (
                      <span style={{
                        background: "rgba(0,210,160,.12)", color: "#34d399",
                        fontSize: 11, padding: "2px 8px", borderRadius: 20,
                      }}>
                        {p("noImpact")}
                      </span>
                    )}
                  </div>

                  {/* Days table */}
                  {range.days.some(d => d.locations.length > 0) ? (
                    <div style={{
                      background: "var(--card)", border: "1px solid var(--border)",
                      borderRadius: 10, overflow: "hidden",
                    }}>
                      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                        <thead>
                          <tr style={{ borderBottom: "1px solid var(--border)" }}>
                            {["Date", "Location", "Status", "Present/Min", p("bestSub"), p("conflicts")].map(h => (
                              <th key={h} style={{
                                textAlign: "left", padding: "8px 12px",
                                color: "var(--text3)", fontWeight: 500, fontSize: 11,
                                whiteSpace: "nowrap",
                              }}>
                                {h}
                              </th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {range.days.flatMap((day, di) =>
                            day.locations.length === 0 ? [] :
                            day.locations.map((loc, li) => {
                              const statusStyle = STATUS_STYLES[loc.coverageStatus] ?? STATUS_STYLES.CLOSED
                              const dayConflicts = li === 0 ? day.conflicts : []

                              return (
                                <tr
                                  key={`${di}-${li}`}
                                  style={{
                                    borderBottom: "1px solid var(--border)",
                                    background: di % 2 === 0 ? "transparent" : "rgba(255,255,255,.02)",
                                  }}
                                >
                                  {/* Date cell — only for first location of the day */}
                                  <td style={{ padding: "8px 12px", whiteSpace: "nowrap", verticalAlign: "top" }}>
                                    {li === 0 ? (
                                      <>
                                        <span style={{ color: "var(--text)", fontWeight: 500 }}>
                                          {formatDate(day.date)}
                                        </span>
                                        <span style={{ color: "var(--text3)", marginLeft: 5 }}>
                                          {shortDay(day.dayOfWeek)}
                                        </span>
                                      </>
                                    ) : null}
                                  </td>

                                  {/* Location */}
                                  <td style={{ padding: "8px 12px", color: "var(--text)", maxWidth: 200 }}>
                                    <span style={{
                                      display: "block", overflow: "hidden",
                                      textOverflow: "ellipsis", whiteSpace: "nowrap",
                                    }}>
                                      {loc.displayName}
                                    </span>
                                  </td>

                                  {/* Status badge */}
                                  <td style={{ padding: "8px 12px" }}>
                                    <span style={{
                                      background: statusStyle.bg, color: statusStyle.color,
                                      padding: "2px 8px", borderRadius: 20, fontSize: 10,
                                      fontWeight: 600, whiteSpace: "nowrap",
                                    }}>
                                      {p(statusStyle.label)}
                                    </span>
                                  </td>

                                  {/* Present/Min */}
                                  <td style={{
                                    padding: "8px 12px", fontFamily: "IBM Plex Mono, monospace",
                                    color: loc.gap > 0 ? "#f87171" : "var(--text3)",
                                  }}>
                                    {loc.present.toFixed(1)} / {loc.required}
                                  </td>

                                  {/* Best substitute */}
                                  <td style={{ padding: "8px 12px" }}>
                                    {loc.bestSubstitute ? (
                                      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                                        <span style={{ color: "var(--text)" }}>
                                          {loc.bestSubstitute.fullName}
                                        </span>
                                        <span style={{
                                          background: (SOURCE_COLORS[loc.bestSubstitute.sourceType] ?? SOURCE_COLORS.CALL_IN).bg,
                                          color: (SOURCE_COLORS[loc.bestSubstitute.sourceType] ?? SOURCE_COLORS.CALL_IN).color,
                                          fontSize: 10, padding: "1px 6px", borderRadius: 10,
                                        }}>
                                          {loc.bestSubstitute.sourceType}
                                        </span>
                                        {loc.bestSubstitute.distanceKm != null && (
                                          <span style={{ fontSize: 10, color: "var(--text3)" }}>
                                            {Math.round(loc.bestSubstitute.distanceKm)} km
                                          </span>
                                        )}
                                      </div>
                                    ) : (
                                      <span style={{ color: "var(--text3)" }}>—</span>
                                    )}
                                  </td>

                                  {/* Conflicts */}
                                  <td style={{ padding: "8px 12px" }}>
                                    {li === 0 && dayConflicts.length > 0 ? (
                                      <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                                        {dayConflicts.map(c => {
                                          const abs = ABSENCE_COLORS[c.absenceType] ?? ABSENCE_COLORS.UL
                                          return (
                                            <span key={c.employeeId} style={{
                                              background: abs.bg, color: abs.color,
                                              fontSize: 10, padding: "2px 7px", borderRadius: 10,
                                              whiteSpace: "nowrap",
                                            }}>
                                              {c.fullName.split(" ")[0]} ({c.absenceType})
                                            </span>
                                          )
                                        })}
                                      </div>
                                    ) : li === 0 ? (
                                      <span style={{ color: "var(--text3)", fontSize: 11 }}>{p("noConflicts")}</span>
                                    ) : null}
                                  </td>
                                </tr>
                              )
                            })
                          )}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <div style={{
                      background: "var(--card)", border: "1px solid var(--border)",
                      borderRadius: 10, padding: "20px 16px",
                      textAlign: "center", color: "var(--text3)", fontSize: 13,
                    }}>
                      {p("noImpact")}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
