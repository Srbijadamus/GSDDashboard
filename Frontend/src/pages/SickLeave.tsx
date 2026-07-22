import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useRef, useEffect, Fragment } from "react"
import { useTranslation } from "react-i18next"
import { api } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { maxFutureDateStr } from "../constants"

function resolveName(s: any): string {
  if (s.fullName && s.fullName.trim()) return s.fullName.trim()
  const composed = ((s.firstName ?? "") + " " + (s.lastName ?? "")).trim()
  if (composed) return composed
  return "ID " + s.employeeId
}

function CommentCell({ id, initial, onSaved }: { id: number; initial: string | null; onSaved: (val: string) => void }) {
  const [editing, setEditing] = useState(false)
  const [value, setValue] = useState(initial ?? "")
  const [saving, setSaving] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => { if (editing) inputRef.current?.focus() }, [editing])

  const save = async () => {
    if (saving) return
    setSaving(true)
    try {
      await api.sickLeave.patch(id, { notes: value })
      onSaved(value)
    } catch (err) {
      console.error("Failed to save comment:", err)
      alert("Failed to save — please try again.")
    } finally { setSaving(false); setEditing(false) }
  }

  const cancel = () => { setValue(initial ?? ""); setEditing(false) }

  if (editing) {
    return (
      <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
        <input ref={inputRef} value={value} onChange={e => setValue(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter") save(); if (e.key === "Escape") cancel() }}
          style={{ background: "var(--card2)", border: "1px solid var(--accent)", color: "var(--text)", padding: "3px 7px", borderRadius: 4, fontSize: 11, outline: "none", width: 180 }} />
        <button onClick={save} disabled={saving}
          style={{ background: "var(--accent)", border: "none", color: "#fff", borderRadius: 4, padding: "3px 7px", fontSize: 10, cursor: "pointer", opacity: saving ? .6 : 1 }}>
          {saving ? "…" : "✓"}
        </button>
        <button onClick={cancel}
          style={{ background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)", borderRadius: 4, padding: "3px 7px", fontSize: 10, cursor: "pointer" }}>
          ✕
        </button>
      </div>
    )
  }

  return (
    <div onClick={() => setEditing(true)} title="Klicken zum Bearbeiten"
      style={{ cursor: "pointer", color: value ? "var(--text2)" : "var(--text3)", fontSize: 11, padding: "2px 6px", borderRadius: 4, minWidth: 80, border: "1px solid transparent", transition: "border-color .15s, background .15s" }}
      onMouseEnter={e => { (e.currentTarget as HTMLElement).style.borderColor = "var(--border)"; (e.currentTarget as HTMLElement).style.background = "var(--card2)" }}
      onMouseLeave={e => { (e.currentTarget as HTMLElement).style.borderColor = "transparent"; (e.currentTarget as HTMLElement).style.background = "transparent" }}>
      {value || <span style={{ fontStyle: "italic", fontSize: 10 }}>— bearbeiten</span>}
    </div>
  )
}

function AddSickLeaveModal({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [employeeId, setEmployeeId] = useState("")
  const [startDate, setStartDate] = useState(new Date().toISOString().split("T")[0])
  const [endDate, setEndDate] = useState(new Date().toISOString().split("T")[0])
  const [type, setType] = useState("Self")
  const [notes, setNotes] = useState("")
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState("")
  const { data: employees } = useQuery({ queryKey: ["employees"], queryFn: () => api.employees.get() })
  const workDays = () => {
    const s = new Date(startDate), e = new Date(endDate)
    let count = 0
    for (let d = new Date(s); d <= e; d.setDate(d.getDate() + 1)) {
      if (d.getDay() !== 0 && d.getDay() !== 6) count++
    }
    return count
  }
  const save = async () => {
    if (!employeeId) { setError("Bitte Agent auswaehlen"); return }
    if (endDate < startDate) { setError("Enddatum muss nach Startdatum liegen"); return }
    setSaving(true)
    try {
      await api.sickLeave.create({ employeeId, startDate, endDate, type, notes })
      onSaved(); onClose()
    } catch { setError("Fehler beim Speichern") }
    finally { setSaving(false) }
  }
  const inp: React.CSSProperties = { background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)", padding: "8px 12px", borderRadius: 6, fontSize: 13, outline: "none", width: "100%" }
  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, width: 480, overflow: "hidden" }} onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px", borderBottom: "1px solid var(--border)" }}>
          <span style={{ fontWeight: 600, fontSize: 15 }}>Krankmeldung erfassen</span>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer" }}>✕</button>
        </div>
        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Agent</label>
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} style={inp}>
              <option value="">Agent auswaehlen</option>
              {(employees ?? []).filter((e: any) => e.isActive !== false).sort((a: any, b: any) => (a.fullName ?? "").localeCompare(b.fullName ?? "")).map((e: any) => (
                <option key={e.employeeId} value={e.employeeId}>{e.fullName} ({e.employeeId})</option>
              ))}
            </select>
          </div>
          <div>
            <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Typ</label>
            <div style={{ display: "flex", gap: 8 }}>
              {["Self", "Child"].map(t => (
                <button key={t} onClick={() => setType(t)} style={{ flex: 1, padding: "8px 0", borderRadius: 6, border: "1px solid var(--border)", background: type === t ? "var(--accent)" : "var(--card2)", color: type === t ? "#fff" : "var(--text2)", cursor: "pointer", fontSize: 13 }}>{t}</button>
              ))}
            </div>
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Start</label>
              <input type="date" value={startDate} max={maxFutureDateStr()} onChange={e => setStartDate(e.target.value)} style={inp} />
            </div>
            <div>
              <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Ende</label>
              <input type="date" value={endDate} max={maxFutureDateStr()} onChange={e => setEndDate(e.target.value)} style={inp} />
            </div>
          </div>
          <div>
            <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Notizen</label>
            <input value={notes} onChange={e => setNotes(e.target.value)} placeholder="optional..." style={inp} />
          </div>
          <div style={{ background: "var(--card2)", borderRadius: 6, padding: "10px 14px", fontSize: 12, color: "var(--text2)" }}>
            Arbeitstage: <strong style={{ color: "var(--text)", fontFamily: "IBM Plex Mono" }}>{workDays()}</strong>
          </div>
          {error && <div style={{ color: "var(--danger)", fontSize: 12 }}>{error}</div>}
        </div>
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, padding: "14px 20px", borderTop: "1px solid var(--border)" }}>
          <button onClick={onClose} style={{ padding: "8px 18px", borderRadius: 6, border: "1px solid var(--border)", background: "transparent", color: "var(--text2)", cursor: "pointer", fontSize: 13 }}>Abbrechen</button>
          <button onClick={save} disabled={saving} style={{ padding: "8px 18px", borderRadius: 6, border: "none", background: "var(--accent)", color: "#fff", cursor: "pointer", fontSize: 13, fontWeight: 600, opacity: saving ? .6 : 1 }}>{saving ? "Speichern..." : "Speichern"}</button>
        </div>
      </div>
    </div>
  )
}

function DrillDownModal({ title, entries, onClose }: { title: string; entries: any[]; onClose: () => void }) {
  const today = new Date().toISOString().split("T")[0]
  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 1000, display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, width: 780, maxHeight: "80vh", overflow: "hidden", display: "flex", flexDirection: "column" }} onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px", borderBottom: "1px solid var(--border)" }}>
          <span style={{ fontWeight: 600, fontSize: 15 }}>{title}</span>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer" }}>✕</button>
        </div>
        <div style={{ overflowY: "auto", padding: 16 }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr style={{ background: "var(--card2)" }}>
                {["ID", "Name", "Team Lead", "Sick Since", "Expected Return", "Days So Far"].map(h => (
                  <th key={h} style={{ padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", borderBottom: "1px solid var(--border)" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {entries.length === 0 && <tr><td colSpan={6} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>No entries</td></tr>}
              {entries.map((s: any, i: number) => {
                const since = new Date(s.firstDay)
                const todayD = new Date(today)
                const daysSoFar = Math.max(0, Math.floor((todayD.getTime() - since.getTime()) / 86400000) + 1)
                const lastDay = new Date(s.lastDay); lastDay.setDate(lastDay.getDate() + 1)
                const expectedReturn = lastDay.toISOString().split("T")[0]
                const isCritical = daysSoFar >= 10; const isWarn = daysSoFar >= 5
                return (
                  <tr key={i} style={{ borderBottom: "1px solid var(--border)" }}>
                    <td style={{ padding: "8px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{s.employeeId}</td>
                    <td style={{ padding: "8px 12px", fontWeight: 500 }}>{resolveName(s)}</td>
                    <td style={{ padding: "8px 12px", color: "var(--text2)", fontSize: 11 }}>{s.teamLeadName}</td>
                    <td style={{ padding: "8px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{s.firstDay}</td>
                    <td style={{ padding: "8px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{expectedReturn}</td>
                    <td style={{ padding: "8px 12px" }}>
                      <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, fontWeight: 600, color: isCritical ? "var(--danger)" : isWarn ? "var(--warn)" : "var(--text2)" }}>
                        {isCritical ? "🔴 " : isWarn ? "⚠️ " : ""}{daysSoFar}d
                      </span>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function DayGrid({ days }: { days: number }) {
  const today = new Date()
  const dates = Array.from({ length: days }, (_, i) => { const d = new Date(today); d.setDate(today.getDate() + i); return d })
  const from = dates[0].toISOString().split("T")[0]
  const to = dates[dates.length - 1].toISOString().split("T")[0]
  const { data, isLoading } = useQuery({
    queryKey: ["sl-range", from, to],
    queryFn: () => api.sickLeave.get(`from=${from}&to=${to}`)
  })
  if (isLoading) return <div style={{ padding: 24, color: "var(--text3)", textAlign: "center" }}>Loading...</div>
  const entries: any[] = data ?? []
  const sickDates: Record<string, Set<string>> = {}
  const agentInfo: Record<string, any> = {}
  entries.forEach((e: any) => {
    const id = String(e.employeeId)
    if (!sickDates[id]) sickDates[id] = new Set()
    agentInfo[id] = e
    const start = new Date(e.firstDay); const end = new Date(e.lastDay)
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      sickDates[id].add(d.toISOString().split("T")[0])
    }
  })
  const agentIds = Object.keys(sickDates)
  const dayCount = dates.map(d => { const ds = d.toISOString().split("T")[0]; return agentIds.filter(id => sickDates[id].has(ds)).length })
  const dayHeaderColor = (count: number) => count >= 6 ? "var(--danger)" : count >= 3 ? "var(--yellow)" : "var(--green)"
  const dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
  return (
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
      <div style={{ overflowX: "auto" }}>
        <table style={{ borderCollapse: "collapse", fontSize: 11, minWidth: "100%" }}>
          <thead>
            <tr style={{ background: "var(--card2)" }}>
              <th style={{ padding: "10px 14px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", borderBottom: "1px solid var(--border)", minWidth: 160, position: "sticky", left: 0, background: "var(--card2)", zIndex: 2 }}>Agent</th>
              {dates.map((d, i) => {
                const ds = d.toISOString().split("T")[0]
                const label = dayNames[d.getDay()] + " " + String(d.getDate()).padStart(2, "0") + "." + String(d.getMonth() + 1).padStart(2, "0")
                return (
                  <th key={ds} style={{ padding: "10px 8px", textAlign: "center", borderBottom: "1px solid var(--border)", minWidth: 72 }}>
                    <div style={{ fontSize: 10, color: "var(--text3)", fontWeight: 400 }}>{label}</div>
                    <div style={{ fontFamily: "IBM Plex Mono", fontWeight: 600, color: dayHeaderColor(dayCount[i]), fontSize: 12 }}>{dayCount[i]} sick</div>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {agentIds.length === 0 && <tr><td colSpan={dates.length + 1} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>No sick leave in this period</td></tr>}
            {agentIds.map(id => {
              const info = agentInfo[id]
              return (
                <tr key={id} style={{ borderBottom: "1px solid var(--border)" }}>
                  <td style={{ padding: "8px 14px", position: "sticky", left: 0, background: "var(--card)", zIndex: 1 }}>
                    <div style={{ fontWeight: 500, fontSize: 12 }}>{resolveName(info)}</div>
                    <div style={{ fontSize: 10, color: "var(--text3)" }}>{info.teamLeadName}</div>
                  </td>
                  {dates.map(d => {
                    const ds = d.toISOString().split("T")[0]; const sick = sickDates[id].has(ds)
                    return (
                      <td key={ds} style={{ padding: "8px 4px", textAlign: "center" }}>
                        {sick && <div style={{ background: "rgba(255,59,92,.2)", border: "1px solid rgba(255,59,92,.4)", borderRadius: 4, padding: "3px 0", color: "var(--danger)", fontSize: 10, fontWeight: 600 }}>sick</div>}
                      </td>
                    )
                  })}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
        {agentIds.length} agents / {days}-day view
      </div>
    </div>
  )
}

// ─── Grouped table ───────────────────────────────────────────────────────────
function GroupedSickTable({ data, commentCache, setCommentCache }: {
  data: any[]; commentCache: Record<number, string>; setCommentCache: (fn: (prev: Record<number, string>) => Record<number, string>) => void
}) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const toggle = (id: string) => setExpanded(prev => {
    const next = new Set(prev)
    if (next.has(id)) next.delete(id); else next.add(id)
    return next
  })

  // Group by employeeId, preserve first-appearance order
  const groups: Record<string, any[]> = {}
  const order: string[] = []
  data.forEach((s: any) => {
    const id = String(s.employeeId)
    if (!groups[id]) { groups[id] = []; order.push(id) }
    groups[id].push(s)
  })

  const dayColor = (d: number) => d > 30 ? "var(--danger)" : d >= 14 ? "var(--warn)" : d >= 7 ? "var(--yellow)" : "var(--text2)"

  return (
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr style={{ background: "var(--card2)" }}>
              {["Name", "Team Lead", "Total Days", "Periods", "Last Sick Leave", "Notes"].map(h => (
                <th key={h} style={{ padding: "10px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", borderBottom: "1px solid var(--border)", whiteSpace: "nowrap" }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {order.length === 0 && (
              <tr><td colSpan={6} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>No sick leave records found</td></tr>
            )}
            {order.map(empId => {
              const first = groups[empId][0]
              const isExp = expanded.has(empId)

              // Deduplicate periods: same (firstDay, lastDay) with different types
              // (e.g. "Self" + "SL" entered for same absence) → keep one, prefer non-SL
              const seen = new Map<string, any>()
              groups[empId].forEach((p: any) => {
                const key = p.firstDay + "|" + p.lastDay
                if (!seen.has(key) || p.leaveType !== "SL") seen.set(key, p)
              })
              const periods = Array.from(seen.values()).sort((a, b) => a.firstDay.localeCompare(b.firstDay))

              const totalDays = periods.reduce((sum, p) => sum + (p.durationDays ?? 0), 0)
              const lastSick = periods[periods.length - 1]?.lastDay ?? ""
              const multi = periods.length > 1

              return (
                <Fragment key={empId}>
                  <tr
                    onClick={() => multi && toggle(empId)}
                    style={{ borderBottom: isExp ? "none" : "1px solid var(--border)", cursor: multi ? "pointer" : "default" }}
                    onMouseEnter={ev => (ev.currentTarget.style.background = "rgba(255,255,255,.03)")}
                    onMouseLeave={ev => (ev.currentTarget.style.background = "")}>

                    {/* Name + expand arrow */}
                    <td style={{ padding: "10px 12px", fontWeight: 600, whiteSpace: "nowrap" }}>
                      <span style={{ marginRight: 6, fontSize: 10, color: "var(--text3)", display: "inline-block", opacity: multi ? 1 : 0, transform: isExp ? "rotate(90deg)" : "rotate(0deg)", transition: "transform .15s" }}>▶</span>
                      {resolveName(first)}
                      <span style={{ fontSize: 10, color: "var(--text3)", marginLeft: 6, fontFamily: "IBM Plex Mono" }}>{first.employeeId}</span>
                    </td>

                    {/* Team Lead */}
                    <td style={{ padding: "10px 12px", color: "var(--text2)", fontSize: 11, whiteSpace: "nowrap" }}>{first.teamLeadName ?? "—"}</td>

                    {/* Total Days */}
                    <td style={{ padding: "10px 12px" }}>
                      <span style={{ fontFamily: "IBM Plex Mono", fontSize: 12, fontWeight: 600, color: dayColor(totalDays) }}>{totalDays}d</span>
                    </td>

                    {/* Periods badge */}
                    <td style={{ padding: "10px 12px" }}>
                      {multi
                        ? <span style={{ background: "rgba(99,102,241,.2)", color: "#818cf8", padding: "2px 8px", borderRadius: 12, fontSize: 10, fontFamily: "IBM Plex Mono" }}>{periods.length}×</span>
                        : <span style={{ color: "var(--text3)", fontSize: 10 }}>1×</span>}
                    </td>

                    {/* Last Sick Leave */}
                    <td style={{ padding: "10px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{lastSick}</td>

                    {/* Notes — only on summary row for single-period agents */}
                    <td style={{ padding: "6px 8px", minWidth: 160 }}>
                      {!multi && (
                        <CommentCell id={first.id}
                          initial={commentCache[first.id] !== undefined ? commentCache[first.id] : first.comments}
                          onSaved={val => setCommentCache(prev => ({ ...prev, [first.id]: val }))} />
                      )}
                    </td>
                  </tr>

                  {/* Expanded detail rows — one per deduplicated period */}
                  {multi && isExp && periods.map((p: any, i: number) => (
                    <tr key={p.id ?? i} style={{ borderBottom: i === periods.length - 1 ? "1px solid var(--border)" : "none", background: "rgba(99,102,241,.03)" }}>
                      <td style={{ padding: "7px 12px 7px 32px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)" }}>
                        <span style={{ color: "rgba(99,102,241,.4)", marginRight: 6 }}>└</span>
                        {p.firstDay} – {p.lastDay}
                      </td>
                      <td />
                      <td style={{ padding: "7px 12px" }}>
                        <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color: dayColor(p.durationDays ?? 0) }}>{p.durationDays ?? "?"}d</span>
                      </td>
                      <td style={{ padding: "7px 12px" }}>
                        <span style={{ background: p.leaveType === "Self" ? "rgba(255,124,59,.15)" : "rgba(250,204,21,.15)", color: p.leaveType === "Self" ? "var(--warn)" : "var(--yellow)", padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono" }}>{p.leaveType}</span>
                      </td>
                      <td />
                      <td style={{ padding: "5px 8px", minWidth: 160 }}>
                        <CommentCell id={p.id}
                          initial={commentCache[p.id] !== undefined ? commentCache[p.id] : p.comments}
                          onSaved={val => setCommentCache(prev => ({ ...prev, [p.id]: val }))} />
                      </td>
                    </tr>
                  ))}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
      <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
        {order.length} agents / {data.length} records
      </div>
    </div>
  )
}

export default function SickLeave() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [teamLead, setTeamLead] = useState("")
  const [type, setType] = useState("")
  const [activeOnly, setActiveOnly] = useState(false)
  const [modal, setModal] = useState<null | { title: string; entries: any[] }>(null)
  const [showAdd, setShowAdd] = useState(false)
  const [view, setView] = useState<"today" | "7d" | "14d">("today")
  const [commentCache, setCommentCache] = useState<Record<number, string>>({})

  const params = (activeOnly ? "activeOnly=true" : "from=2026-01-01&to=2026-12-31") + (teamLead ? "&teamLead=" + teamLead : "") + (type ? "&type=" + type : "")
  const { data, isLoading } = useQuery({ queryKey: ["sl", params], queryFn: () => api.sickLeave.get(params) })
  const { data: stats } = useQuery({ queryKey: ["sl-stats"], queryFn: api.sickLeave.stats })
  const { data: activeToday } = useQuery({
    queryKey: ["sl-active-today"],
    queryFn: () => api.sickLeave.get("activeOnly=true")
  })

  const cards = [
    { label: "Currently Sick", value: stats?.totalActive, color: "var(--danger)", onClick: () => setModal({ title: "Currently Sick Agents", entries: activeToday ?? [] }) },
    { label: "Self", value: stats?.selfCount, color: "var(--warn)", onClick: () => setModal({ title: "Self Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Self") }) },
    { label: "Child", value: stats?.childCount, color: "var(--yellow)", onClick: () => setModal({ title: "Child Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Child") }) },
    { label: "Avg Duration", value: stats?.averageDuration ? stats.averageDuration + "d" : "0d", color: "var(--text2)", onClick: null },
  ]

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {modal && <DrillDownModal title={modal.title} entries={modal.entries} onClose={() => setModal(null)} />}
      {showAdd && <AddSickLeaveModal onClose={() => setShowAdd(false)} onSaved={() => queryClient.invalidateQueries()} />}

      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>{t("nav.sickLeave")}</h1>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          <button onClick={() => setShowAdd(true)} style={{ background: "var(--accent)", color: "#fff", border: "none", borderRadius: 6, padding: "8px 16px", fontSize: 13, fontWeight: 600, cursor: "pointer" }}>+ Krankmeldung</button>
          <DownloadButtons onToday={api.sickLeave.downloadToday} on7Days={api.sickLeave.download7} on30Days={api.sickLeave.download30} />
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        {cards.map(s => (
          <div key={s.label} onClick={s.onClick ?? undefined}
            style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px 20px", cursor: s.onClick ? "pointer" : "default" }}
            onMouseEnter={e => { if (s.onClick) (e.currentTarget as HTMLElement).style.borderColor = "var(--accent)" }}
            onMouseLeave={e => { (e.currentTarget as HTMLElement).style.borderColor = "var(--border)" }}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>{s.label}</div>
            <div style={{ fontSize: 28, fontWeight: 600, fontFamily: "IBM Plex Mono", color: s.color }}>{s.value ?? 0}</div>
            {s.onClick && <div style={{ fontSize: 10, color: "var(--text3)", marginTop: 4 }}>Click for details</div>}
          </div>
        ))}
      </div>

      {stats?.byTeamLead?.length > 0 && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 16px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", marginBottom: 10 }}>By Team Lead (active)</div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {stats.byTeamLead.map((tl: any) => (
              <span key={tl.teamLead} onClick={() => setTeamLead(tl.teamLead === teamLead ? "" : tl.teamLead)}
                style={{ background: tl.teamLead === teamLead ? "rgba(255,124,59,.3)" : "rgba(255,124,59,.12)", color: "var(--warn)", padding: "3px 10px", borderRadius: 20, fontSize: 11, fontFamily: "IBM Plex Mono", cursor: "pointer" }}>
                {tl.teamLead}: {tl.count}
              </span>
            ))}
          </div>
        </div>
      )}

      <div style={{ display: "flex", gap: 4, background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: 4, alignSelf: "flex-start" }}>
        {[{ key: "today", label: "Heute" }, { key: "7d", label: "Naechste 7 Tage" }, { key: "14d", label: "Naechste 14 Tage" }].map(tab => (
          <button key={tab.key} onClick={() => setView(tab.key as any)}
            style={{ background: view === tab.key ? "var(--accent)" : "transparent", color: view === tab.key ? "#fff" : "var(--text2)", border: "none", borderRadius: 6, padding: "6px 16px", fontSize: 12, fontWeight: 500, cursor: "pointer" }}>
            {tab.label}
          </button>
        ))}
      </div>

      {view === "today" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", gap: 10 }}>
            <input placeholder="Team Lead..." value={teamLead} onChange={e => setTeamLead(e.target.value)}
              style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)", padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none", width: 200 }} />
            <select value={type} onChange={e => setType(e.target.value)}
              style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text2)", padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
              <option value="">All Types</option>
              <option value="Self">Self</option>
              <option value="Child">Child</option>
            </select>
            <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 12, color: "var(--text2)", cursor: "pointer" }}>
              <input type="checkbox" checked={activeOnly} onChange={e => setActiveOnly(e.target.checked)} />
              Active only
            </label>
          </div>

          {isLoading
            ? <div style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>Loading...</div>
            : <GroupedSickTable data={data ?? []} commentCache={commentCache} setCommentCache={setCommentCache} />
          }
        </div>
      )}
      {view === "7d" && <DayGrid days={7} />}
      {view === "14d" && <DayGrid days={14} />}
    </div>
  )
}
