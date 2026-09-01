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
  COVERED:   { bg: "rgb(var(--st-good-bg))",    color: "rgb(var(--st-good-fg))",    label: "covered"   },
  PARTIAL:   { bg: "rgb(var(--st-warn-bg))",    color: "rgb(var(--st-warn-fg))",    label: "partial"   },
  UNCOVERED: { bg: "rgb(var(--st-crit-bg))",    color: "rgb(var(--st-crit-fg))",    label: "uncovered" },
  CLOSED:    { bg: "rgb(var(--st-neutral-bg))", color: "rgb(var(--st-neutral-fg))", label: "closed"    },
}

const SOURCE_COLORS: Record<string, { bg: string; color: string }> = {
  BACKUP:    { bg: "rgb(var(--st-learn-bg))",  color: "rgb(var(--st-learn-fg))" },
  SSP:       { bg: "rgb(var(--st-info-bg))",   color: "rgb(var(--st-info-fg))"  },
  WIC_DONOR: { bg: "rgb(var(--st-good-bg))",   color: "rgb(var(--st-good-fg))"  },
  CALL_IN:   { bg: "rgb(var(--st-warn-bg))",   color: "rgb(var(--st-warn-fg))"  },
}

const ABSENCE_COLORS: Record<string, { bg: string; color: string }> = {
  AL:       { bg: "rgb(var(--st-info-bg))",    color: "rgb(var(--st-info-fg))"    },
  HALF_AL:  { bg: "rgb(var(--st-info-bg))",    color: "rgb(var(--st-info-fg))"    },
  SL:       { bg: "rgb(var(--st-crit-bg))",    color: "rgb(var(--st-crit-fg))"    },
  UL:       { bg: "rgb(var(--st-neutral-bg))", color: "rgb(var(--st-neutral-fg))" },
  TRAINING: { bg: "rgb(var(--st-learn-bg))",   color: "rgb(var(--st-learn-fg))"   },
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
      <div
        className="bg-page border border-line-subtle"
        style={{
          borderRadius: 14, width: "100%", maxWidth: 860,
          maxHeight: "90vh", display: "flex", flexDirection: "column",
          boxShadow: "0 24px 64px rgba(0,0,0,.5)",
        }}
      >
        {/* Header */}
        <div
          className="border-b border-line-subtle"
          style={{
            display: "flex", alignItems: "center", justifyContent: "space-between",
            padding: "18px 22px", flexShrink: 0,
          }}
        >
          <div>
            <div className="text-ink" style={{ fontWeight: 600, fontSize: 15 }}>{p("title")}</div>
            <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 2 }}>
              Simulation only — no changes are made to the schedule
            </div>
          </div>
          <button
            onClick={handleClose}
            className="text-ink-soft"
            style={{ background: "none", border: "none", cursor: "pointer", padding: 4, borderRadius: 6, lineHeight: 0 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Scrollable body */}
        <div style={{ overflowY: "auto", flex: 1, padding: "20px 22px" }}>

          {/* ── Form ── */}
          <div
            className="bg-raised border border-line-subtle"
            style={{ borderRadius: 10, padding: 16, marginBottom: 20 }}
          >
            {/* Employee selector */}
            <div style={{ marginBottom: 14 }}>
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block", marginBottom: 5 }}>
                {p("employee")}
              </label>
              <select
                value={selectedEmpId}
                onChange={e => setSelectedEmpId(e.target.value)}
                className="bg-page border border-line-subtle text-ink"
                style={{ width: "100%", borderRadius: 7, fontSize: 13, padding: "7px 10px", outline: "none" }}
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
              <label className="text-ink-soft" style={{ fontSize: 11, display: "block", marginBottom: 8 }}>
                {p("dateRanges")}
              </label>
              {ranges.map((r, i) => (
                <div key={i} style={{ display: "flex", gap: 8, alignItems: "center", marginBottom: 8 }}>
                  <div style={{ flex: 1 }}>
                    <span className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>
                      {p("from")}
                    </span>
                    <input
                      type="date"
                      value={r.from}
                      onChange={e => updateRange(i, "from", e.target.value)}
                      className="bg-page border border-line-subtle text-ink"
                      style={{ width: "100%", borderRadius: 7, fontSize: 13, padding: "6px 10px", outline: "none" }}
                    />
                  </div>
                  <div style={{ flex: 1 }}>
                    <span className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>
                      {p("to")}
                    </span>
                    <input
                      type="date"
                      value={r.to}
                      min={r.from || undefined}
                      onChange={e => updateRange(i, "to", e.target.value)}
                      className="bg-page border border-line-subtle text-ink"
                      style={{ width: "100%", borderRadius: 7, fontSize: 13, padding: "6px 10px", outline: "none" }}
                    />
                  </div>
                  {ranges.length > 1 && (
                    <button
                      onClick={() => removeRange(i)}
                      className="text-ink-soft"
                      style={{ marginTop: 18, background: "none", border: "none", cursor: "pointer", padding: 4, borderRadius: 6, lineHeight: 0 }}
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
                    className="border border-dashed border-line-subtle text-ink-soft"
                    style={{
                      background: "none", borderRadius: 7,
                      fontSize: 12, padding: "5px 12px",
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
                  className={loading ? "bg-line-subtle" : "bg-info-solid"}
                  style={{
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
            <div
              className="bg-crit-bg border border-crit-bd text-crit-fg"
              style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 16 }}
            >
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
                  <div className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>
                    {result.fullName ?? result.employeeId}
                  </div>
                  <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 2 }}>
                    {result.note}
                  </div>
                </div>
              </div>

              {/* Per-range sections */}
              {result.dateRanges.map((range, ri) => (
                <div key={ri} style={{ marginBottom: 24 }}>
                  {/* Range header */}
                  <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 10 }}>
                    <span className="text-ink" style={{ fontSize: 13, fontWeight: 600 }}>
                      {formatDate(range.from)} – {formatDate(range.to)}
                    </span>
                    <span className="text-ink-soft" style={{ fontSize: 11 }}>
                      {range.totalDays} day(s)
                    </span>
                    {range.atRiskDays > 0 ? (
                      <span
                        className="bg-crit-bg text-crit-fg"
                        style={{ fontSize: 11, padding: "2px 8px", borderRadius: 20, fontWeight: 600 }}
                      >
                        {p("atRisk", { n: range.atRiskDays })}
                      </span>
                    ) : (
                      <span
                        className="text-good-fg"
                        style={{ background: "rgb(var(--st-good-bg) / 0.12)", fontSize: 11, padding: "2px 8px", borderRadius: 20 }}
                      >
                        {p("noImpact")}
                      </span>
                    )}
                  </div>

                  {/* Days table */}
                  {range.days.some(d => d.locations.length > 0) ? (
                    <div
                      className="bg-raised border border-line-subtle"
                      style={{ borderRadius: 10, overflow: "hidden" }}
                    >
                      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                        <thead>
                          <tr className="border-b border-line-subtle">
                            {["Date", "Location", "Status", "Present/Min", p("bestSub"), p("conflicts")].map(h => (
                              <th key={h} className="text-ink-soft" style={{
                                textAlign: "left", padding: "8px 12px",
                                fontWeight: 500, fontSize: 11, whiteSpace: "nowrap",
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
                                  className="border-b border-line-subtle"
                                  style={{ background: di % 2 === 0 ? "transparent" : "rgb(var(--surface-raised))" }}
                                >
                                  {/* Date cell — only for first location of the day */}
                                  <td style={{ padding: "8px 12px", whiteSpace: "nowrap", verticalAlign: "top" }}>
                                    {li === 0 ? (
                                      <>
                                        <span className="text-ink" style={{ fontWeight: 500 }}>
                                          {formatDate(day.date)}
                                        </span>
                                        <span className="text-ink-soft" style={{ marginLeft: 5 }}>
                                          {shortDay(day.dayOfWeek)}
                                        </span>
                                      </>
                                    ) : null}
                                  </td>

                                  {/* Location */}
                                  <td className="text-ink" style={{ padding: "8px 12px", maxWidth: 200 }}>
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
                                  <td
                                    className={`font-mono ${loc.gap > 0 ? "text-crit-fg" : "text-ink-soft"}`}
                                    style={{ padding: "8px 12px" }}
                                  >
                                    {loc.present.toFixed(1)} / {loc.required}
                                  </td>

                                  {/* Best substitute */}
                                  <td style={{ padding: "8px 12px" }}>
                                    {loc.bestSubstitute ? (
                                      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                                        <span className="text-ink">
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
                                          <span className="text-ink-soft" style={{ fontSize: 10 }}>
                                            {Math.round(loc.bestSubstitute.distanceKm)} km
                                          </span>
                                        )}
                                      </div>
                                    ) : (
                                      <span className="text-ink-soft">—</span>
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
                                      <span className="text-ink-soft" style={{ fontSize: 11 }}>{p("noConflicts")}</span>
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
                    <div
                      className="bg-raised border border-line-subtle text-ink-soft"
                      style={{ borderRadius: 10, padding: "20px 16px", textAlign: "center", fontSize: 13 }}
                    >
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
