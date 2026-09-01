import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useRef, useEffect, Fragment } from "react"
import { useTranslation } from "react-i18next"
import { AlertTriangle } from "lucide-react"
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
  const [saveErr, setSaveErr] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => { if (editing) inputRef.current?.focus() }, [editing])

  const save = async () => {
    if (saving) return
    setSaveErr(false)
    setSaving(true)
    try {
      await api.sickLeave.patch(id, { notes: value })
      onSaved(value)
      setEditing(false)
    } catch (err) {
      console.error("Failed to save comment:", err)
      setSaveErr(true)
    } finally { setSaving(false) }
  }

  const cancel = () => { setValue(initial ?? ""); setEditing(false) }

  if (editing) {
    return (
      <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
          <input ref={inputRef} value={value} onChange={e => { setValue(e.target.value); setSaveErr(false) }}
            onKeyDown={e => { if (e.key === "Enter") save(); if (e.key === "Escape") cancel() }}
            className={`bg-sunken border text-ink rounded py-0.5 px-[7px] text-[11px] outline-none w-[180px] ${saveErr ? "border-crit-bd" : "border-info-bd"}`} />
          <button onClick={save} disabled={saving}
            className="bg-info-solid border-none text-white rounded py-0.5 px-[7px] text-[10px] cursor-pointer"
            style={{ opacity: saving ? .6 : 1 }}>
            {saving ? "…" : "✓"}
          </button>
          <button onClick={cancel}
            className="bg-sunken border border-line-subtle text-ink-muted rounded py-0.5 px-[7px] text-[10px] cursor-pointer">
            ✕
          </button>
        </div>
        {saveErr && <span className="text-crit-fg" style={{ fontSize: 9 }}>Save failed</span>}
      </div>
    )
  }

  return (
    <div onClick={() => setEditing(true)} title="Klicken zum Bearbeiten"
      className={`cursor-pointer text-[11px] py-0.5 px-[6px] rounded border border-transparent hover:border-line-subtle hover:bg-hovered transition-[border-color,background] duration-150 min-w-[80px] ${value ? "text-ink-muted" : "text-ink-soft"}`}>
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
    } catch (err: any) { setError(err?.message || "Fehler beim Speichern") }
    finally { setSaving(false) }
  }
  const inpCls = "bg-sunken border border-line-subtle text-ink py-2 px-3 rounded-md text-[13px] outline-none w-full"
  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ borderRadius: 10, width: 480, overflow: "hidden" }} className="bg-raised border border-line-subtle" onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px" }} className="border-b border-line-subtle">
          <span style={{ fontWeight: 600, fontSize: 15 }}>Krankmeldung erfassen</span>
          <button onClick={onClose} style={{ background: "none", border: "none", fontSize: 18, cursor: "pointer" }} className="text-ink-muted">✕</button>
        </div>
        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={{ fontSize: 11, textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }} className="text-ink-soft">Agent</label>
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} className={inpCls}>
              <option value="">Agent auswaehlen</option>
              {(employees ?? []).filter((e: any) => e.isActive !== false).sort((a: any, b: any) => (a.fullName ?? "").localeCompare(b.fullName ?? "")).map((e: any) => (
                <option key={e.employeeId} value={e.employeeId}>{e.fullName} ({e.employeeId})</option>
              ))}
            </select>
          </div>
          <div>
            <label style={{ fontSize: 11, textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }} className="text-ink-soft">Typ</label>
            <div style={{ display: "flex", gap: 8 }}>
              {["Self", "Child"].map(t => (
                <button key={t} onClick={() => setType(t)} className={`flex-1 py-2 rounded-md border border-line-subtle text-[13px] cursor-pointer ${type === t ? "bg-info-solid text-white" : "bg-sunken text-ink-muted"}`}>{t}</button>
              ))}
            </div>
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={{ fontSize: 11, textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }} className="text-ink-soft">Start</label>
              <input type="date" value={startDate} max={maxFutureDateStr()} onChange={e => setStartDate(e.target.value)} className={inpCls} />
            </div>
            <div>
              <label style={{ fontSize: 11, textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }} className="text-ink-soft">Ende</label>
              <input type="date" value={endDate} max={maxFutureDateStr()} onChange={e => setEndDate(e.target.value)} className={inpCls} />
            </div>
          </div>
          <div>
            <label style={{ fontSize: 11, textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }} className="text-ink-soft">Notizen</label>
            <input value={notes} onChange={e => setNotes(e.target.value)} placeholder="optional..." className={inpCls} />
          </div>
          <div style={{ borderRadius: 6, padding: "10px 14px", fontSize: 12 }} className="bg-sunken text-ink-muted">
            Arbeitstage: <strong className="text-ink font-mono">{workDays()}</strong>
          </div>
          {error && <div style={{ fontSize: 12 }} className="text-warn-fg">{error}</div>}
        </div>
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, padding: "14px 20px" }} className="border-t border-line-subtle">
          <button onClick={onClose} style={{ padding: "8px 18px", fontSize: 13 }} className="rounded-md border border-line-subtle bg-transparent text-ink-muted cursor-pointer">Abbrechen</button>
          <button onClick={save} disabled={saving} style={{ padding: "8px 18px", fontSize: 13, fontWeight: 600, opacity: saving ? .6 : 1 }} className="rounded-md border-none bg-info-solid text-white cursor-pointer">{saving ? "Speichern..." : "Speichern"}</button>
        </div>
      </div>
    </div>
  )
}

function DrillDownModal({ title, entries, onClose, onEnd }: { title: string; entries: any[]; onClose: () => void; onEnd?: () => void }) {
  const today = new Date().toLocaleDateString("en-CA")
  const [endingEmpId, setEndingEmpId] = useState<string | null>(null)

  const activeCountForEmp = (empId: string) =>
    entries.filter(e => String(e.employeeId) === String(empId)).length

  const endAgent = async (empId: string) => {
    const count = activeCountForEmp(empId)
    const msg = count > 1 ? `${count} aktive Krankmeldungen beenden?` : "Krankmeldung heute beenden?"
    if (!confirm(msg)) return
    setEndingEmpId(empId)
    try {
      await api.sickLeave.endActive(String(empId))
      onEnd?.()
      onClose()
    } catch (err) { console.error("Fehler beim Beenden:", err) }
    finally { setEndingEmpId(null) }
  }

  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 1000, display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ borderRadius: 10, width: 860, maxHeight: "80vh", overflow: "hidden", display: "flex", flexDirection: "column" }} className="bg-raised border border-line-subtle" onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px" }} className="border-b border-line-subtle">
          <span style={{ fontWeight: 600, fontSize: 15 }}>{title}</span>
          <button onClick={onClose} style={{ background: "none", border: "none", fontSize: 18, cursor: "pointer" }} className="text-ink-muted">✕</button>
        </div>
        <div style={{ overflowY: "auto", padding: 16 }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr className="bg-sunken">
                {["ID", "Name", "Team Lead", "Sick Since", "Expected Return", "Days So Far", ""].map(h => (
                  <th key={h} style={{ padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em" }} className="text-ink-soft border-b border-line-subtle">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {entries.length === 0 && <tr><td colSpan={7} style={{ padding: 24, textAlign: "center" }} className="text-ink-soft">No entries</td></tr>}
              {entries.map((s: any, i: number) => {
                const since = new Date(s.firstDay)
                const todayD = new Date(today)
                const daysSoFar = Math.max(0, Math.floor((todayD.getTime() - since.getTime()) / 86400000) + 1)
                const isOpenEnded = s.lastDay >= "2099-01-01"
                const lastDay = new Date(s.lastDay); lastDay.setDate(lastDay.getDate() + 1)
                const expectedReturn = isOpenEnded ? "–" : lastDay.toLocaleDateString("en-CA")
                const isCritical = daysSoFar >= 10; const isWarn = daysSoFar >= 5
                const empId = String(s.employeeId)
                const isEnding = endingEmpId === empId
                return (
                  <tr key={i} className="border-b border-line-subtle">
                    <td style={{ padding: "8px 12px", fontSize: 11 }} className="font-mono text-ink-soft">{s.employeeId}</td>
                    <td style={{ padding: "8px 12px", fontWeight: 500 }}>{resolveName(s)}</td>
                    <td style={{ padding: "8px 12px", fontSize: 11 }} className="text-ink-muted">{s.teamLeadName}</td>
                    <td style={{ padding: "8px 12px", fontSize: 11 }} className="font-mono">{s.firstDay}</td>
                    <td style={{ padding: "8px 12px", fontSize: 11 }} className="font-mono">{expectedReturn}</td>
                    <td style={{ padding: "8px 12px" }}>
                      <span className={`font-mono font-semibold ${isCritical ? "text-warn-fg" : isWarn ? "text-warn-fg" : "text-ink-muted"}`} style={{ fontSize: 11 }}>
                        {isCritical ? <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> : isWarn ? <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> : ""}{daysSoFar}d
                      </span>
                    </td>
                    <td style={{ padding: "8px 8px" }}>
                      <button
                        onClick={e => { e.stopPropagation(); endAgent(empId) }}
                        disabled={isEnding}
                        className="bg-warn-bg border border-warn-bd text-warn-fg rounded font-semibold cursor-pointer whitespace-nowrap"
                        style={{ padding: "3px 10px", fontSize: 11, opacity: isEnding ? .5 : 1 }}>
                        {isEnding ? "…" : "End"}
                      </button>
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
  if (isLoading) return <div style={{ padding: 24, textAlign: "center" }} className="text-ink-soft">Loading...</div>
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
  const dayHeaderColor = (count: number) => count >= 6 ? "text-warn-fg" : count >= 3 ? "[color:var(--yellow)]" : "text-good-fg"
  const dayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
  return (
    <div style={{ borderRadius: 8, overflow: "hidden" }} className="bg-raised border border-line-subtle">
      <div style={{ overflowX: "auto" }}>
        <table style={{ borderCollapse: "collapse", fontSize: 11, minWidth: "100%" }}>
          <thead>
            <tr className="bg-sunken">
              <th style={{ padding: "10px 14px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", minWidth: 160, position: "sticky", left: 0, zIndex: 2 }} className="text-ink-soft border-b border-line-subtle bg-sunken">Agent</th>
              {dates.map((d, i) => {
                const ds = d.toISOString().split("T")[0]
                const label = dayNames[d.getDay()] + " " + String(d.getDate()).padStart(2, "0") + "." + String(d.getMonth() + 1).padStart(2, "0")
                return (
                  <th key={ds} style={{ padding: "10px 8px", textAlign: "center", minWidth: 72 }} className="border-b border-line-subtle">
                    <div style={{ fontSize: 10, fontWeight: 400 }} className="text-ink-soft">{label}</div>
                    <div className={`font-mono font-semibold ${dayHeaderColor(dayCount[i])}`} style={{ fontSize: 12 }}>{dayCount[i]} sick</div>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {agentIds.length === 0 && <tr><td colSpan={dates.length + 1} style={{ padding: 24, textAlign: "center" }} className="text-ink-soft">No sick leave in this period</td></tr>}
            {agentIds.map(id => {
              const info = agentInfo[id]
              return (
                <tr key={id} className="border-b border-line-subtle">
                  <td style={{ padding: "8px 14px", position: "sticky", left: 0, zIndex: 1 }} className="bg-raised">
                    <div style={{ fontWeight: 500, fontSize: 12 }}>{resolveName(info)}</div>
                    <div style={{ fontSize: 10 }} className="text-ink-soft">{info.teamLeadName}</div>
                  </td>
                  {dates.map(d => {
                    const ds = d.toISOString().split("T")[0]; const sick = sickDates[id].has(ds)
                    return (
                      <td key={ds} style={{ padding: "8px 4px", textAlign: "center" }}>
                        {sick && <div className="bg-warn-mid border border-warn-solid/25 rounded-[3px] h-6 w-full" />}
                      </td>
                    )
                  })}
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <div style={{ padding: "8px 12px", fontSize: 11 }} className="border-t border-line-subtle text-ink-soft font-mono">
        {agentIds.length} agents / {days}-day view
      </div>
    </div>
  )
}

// ─── Grouped table ───────────────────────────────────────────────────────────
function GroupedSickTable({ data, commentCache, setCommentCache, onEnd }: {
  data: any[]; commentCache: Record<number, string>; setCommentCache: (fn: (prev: Record<number, string>) => Record<number, string>) => void; onEnd: () => void
}) {
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const [endingEmpId, setEndingEmpId] = useState<string | null>(null)
  const todayStr = new Date().toLocaleDateString("en-CA")

  const endAgent = async (empId: string, activeCount: number) => {
    const msg = activeCount > 1 ? `${activeCount} aktive Krankmeldungen beenden?` : "Krankmeldung heute beenden?"
    if (!confirm(msg)) return
    setEndingEmpId(empId)
    try {
      await api.sickLeave.endActive(empId)
      onEnd()
    } catch (err) { console.error("Fehler beim Beenden der Krankmeldung:", err) }
    finally { setEndingEmpId(null) }
  }

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

  const dayColor = (d: number) => d > 30 ? "text-crit-fg" : d >= 14 ? "text-warn-fg" : d >= 7 ? "[color:var(--yellow)]" : "text-ink-muted"

  return (
    <div style={{ borderRadius: 8, overflow: "hidden" }} className="bg-raised border border-line-subtle">
      <div style={{ overflowX: "auto" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr className="bg-sunken">
              {["Name", "Team Lead", "Total Days", "Periods", "Last Sick Leave", "Notes"].map(h => (
                <th key={h} style={{ padding: "10px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", whiteSpace: "nowrap" }} className="text-ink-soft border-b border-line-subtle">{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {order.length === 0 && (
              <tr><td colSpan={6} style={{ padding: 24, textAlign: "center" }} className="text-ink-soft">No sick leave records found</td></tr>
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
                    className={`hover:bg-hovered ${isExp ? "" : "border-b border-line-subtle"}`}
                    style={{ cursor: multi ? "pointer" : "default" }}>

                    {/* Name + expand arrow */}
                    <td style={{ padding: "10px 12px", fontWeight: 600, whiteSpace: "nowrap" }}>
                      <span style={{ marginRight: 6, fontSize: 10, display: "inline-block", opacity: multi ? 1 : 0, transform: isExp ? "rotate(90deg)" : "rotate(0deg)", transition: "transform .15s" }} className="text-ink-soft">▶</span>
                      {resolveName(first)}
                      <span style={{ fontSize: 10, marginLeft: 6 }} className="text-ink-soft font-mono">{first.employeeId}</span>
                    </td>

                    {/* Team Lead */}
                    <td style={{ padding: "10px 12px", fontSize: 11, whiteSpace: "nowrap" }} className="text-ink-muted">{first.teamLeadName ?? "—"}</td>

                    {/* Total Days */}
                    <td style={{ padding: "10px 12px" }}>
                      <span className={`font-mono font-semibold ${dayColor(totalDays)}`} style={{ fontSize: 12 }}>{totalDays}d</span>
                    </td>

                    {/* Periods badge */}
                    <td style={{ padding: "10px 12px" }}>
                      {multi
                        ? <span className="font-mono bg-learn-bg text-learn-fg" style={{ padding: "2px 8px", borderRadius: 12, fontSize: 10 }}>{periods.length}×</span>
                        : <span style={{ fontSize: 10 }} className="text-ink-soft">1×</span>}
                    </td>

                    {/* Last Sick Leave + End button inline */}
                    <td style={{ padding: "10px 12px", fontSize: 11 }} className="font-mono">
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <span>{lastSick}</span>
                        {(() => {
                          const activeCount = groups[empId].filter((s: any) => s.lastDay >= todayStr).length
                          if (activeCount === 0) return null
                          const isEnding = endingEmpId === empId
                          return (
                            <button
                              onClick={e => { e.stopPropagation(); endAgent(empId, activeCount) }}
                              disabled={isEnding}
                              className="bg-warn-bg border border-warn-bd text-warn-fg rounded font-semibold cursor-pointer"
                              style={{ padding: "2px 8px", fontSize: 10, opacity: isEnding ? .5 : 1, fontFamily: "sans-serif" }}>
                              {isEnding ? "…" : "End"}
                            </button>
                          )
                        })()}
                      </div>
                    </td>

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
                    <tr key={p.id ?? i} style={{ borderBottom: i === periods.length - 1 ? undefined : "none" }} className={`bg-sunken ${i === periods.length - 1 ? "border-b border-line-subtle" : ""}`}>
                      <td style={{ padding: "7px 12px 7px 32px", fontSize: 11 }} className="font-mono text-ink-muted">
                        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                          <span>
                            <span className="text-learn-fg" style={{ opacity: 0.5, marginRight: 6 }}>└</span>
                            {p.firstDay} – {p.lastDay >= "2099-01-01" ? "aktiv" : p.lastDay}
                          </span>
                        </div>
                      </td>
                      <td />
                      <td style={{ padding: "7px 12px" }}>
                        {p.lastDay >= "2099-01-01"
                          ? <span style={{ fontSize: 11 }} className="font-mono text-warn-fg">open</span>
                          : <span className={`font-mono ${dayColor(p.durationDays ?? 0)}`} style={{ fontSize: 11 }}>{p.durationDays ?? "?"}d</span>}
                      </td>
                      <td style={{ padding: "7px 12px" }}>
                        <span className={`font-mono ${p.leaveType === "Self" ? "bg-warn-bg text-warn-fg" : "bg-holiday-bg text-holiday-fg"}`} style={{ padding: "2px 7px", borderRadius: 4, fontSize: 10 }}>{p.leaveType}</span>
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
      <div style={{ padding: "8px 12px", fontSize: 11 }} className="border-t border-line-subtle text-ink-soft font-mono">
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
    { label: "Currently Sick", value: stats?.totalActive, cls: "text-warn-fg", onClick: () => setModal({ title: "Currently Sick Agents", entries: activeToday ?? [] }) },
    { label: "Self", value: stats?.selfCount, cls: "text-warn-fg", onClick: () => setModal({ title: "Self Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Self") }) },
    { label: "Child", value: stats?.childCount, cls: "[color:var(--yellow)]", onClick: () => setModal({ title: "Child Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Child") }) },
    { label: "Avg Duration", value: stats?.averageDuration ? stats.averageDuration + "d" : "0d", cls: "text-ink-muted", onClick: null },
  ]

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {modal && <DrillDownModal title={modal.title} entries={modal.entries} onClose={() => setModal(null)} onEnd={() => { queryClient.invalidateQueries(); setModal(null) }} />}
      {showAdd && <AddSickLeaveModal onClose={() => setShowAdd(false)} onSaved={() => queryClient.invalidateQueries()} />}

      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600 }} className="text-ink">{t("nav.sickLeave")}</h1>
        <div style={{ display: "flex", gap: 10, alignItems: "center" }}>
          <button onClick={() => setShowAdd(true)} style={{ border: "none", borderRadius: 6, padding: "8px 16px", fontSize: 13, fontWeight: 600, cursor: "pointer" }} className="bg-info-solid text-white">+ Krankmeldung</button>
          <DownloadButtons onToday={api.sickLeave.downloadToday} on7Days={api.sickLeave.download7} on30Days={api.sickLeave.download30} />
        </div>
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        {cards.map(s => (
          <div key={s.label} onClick={s.onClick ?? undefined}
            style={{ borderRadius: 8, padding: "16px 20px", cursor: s.onClick ? "pointer" : "default" }}
            className={`bg-raised border border-line-subtle ${s.onClick ? "hover:border-info-bd" : ""}`}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }} className="text-ink-soft">{s.label}</div>
            <div className={`font-mono font-semibold ${s.cls}`} style={{ fontSize: 28 }}>{s.value ?? 0}</div>
            {s.onClick && <div style={{ fontSize: 10, marginTop: 4 }} className="text-ink-soft">Click for details</div>}
          </div>
        ))}
      </div>

      {stats?.byTeamLead?.length > 0 && (
        <div style={{ borderRadius: 8, padding: "14px 16px" }} className="bg-raised border border-line-subtle">
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".07em", marginBottom: 10 }} className="text-ink-soft">By Team Lead (active)</div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {stats.byTeamLead.map((tl: any) => (
              <span key={tl.teamLead} onClick={() => setTeamLead(tl.teamLead === teamLead ? "" : tl.teamLead)}
                className={`text-warn-fg font-mono ${tl.teamLead === teamLead ? "bg-warn-mid" : "bg-warn-bg"}`}
                style={{ padding: "3px 10px", borderRadius: 20, fontSize: 11, cursor: "pointer" }}>
                {tl.teamLead}: {tl.count}
              </span>
            ))}
          </div>
        </div>
      )}

      <div style={{ display: "flex", gap: 4, padding: 4, alignSelf: "flex-start", borderRadius: 8 }} className="bg-raised border border-line-subtle">
        {[{ key: "today", label: "Heute" }, { key: "7d", label: "Naechste 7 Tage" }, { key: "14d", label: "Naechste 14 Tage" }].map(tab => (
          <button key={tab.key} onClick={() => setView(tab.key as any)}
            style={{ border: "none", borderRadius: 6, padding: "6px 16px", fontSize: 12, fontWeight: 500, cursor: "pointer" }}
            className={view === tab.key ? "bg-info-solid text-white" : "bg-transparent text-ink-muted"}>
            {tab.label}
          </button>
        ))}
      </div>

      {view === "today" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", gap: 10 }}>
            <input placeholder="Team Lead..." value={teamLead} onChange={e => setTeamLead(e.target.value)}
              style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none", width: 200 }}
              className="bg-raised border border-line-subtle text-ink" />
            <select value={type} onChange={e => setType(e.target.value)}
              style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12 }}
              className="bg-raised border border-line-subtle text-ink-muted">
              <option value="">All Types</option>
              <option value="Self">Self</option>
              <option value="Child">Child</option>
            </select>
            <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 12, cursor: "pointer" }} className="text-ink-muted">
              <input type="checkbox" checked={activeOnly} onChange={e => setActiveOnly(e.target.checked)} />
              Active only
            </label>
          </div>

          {isLoading
            ? <div style={{ padding: 24, textAlign: "center" }} className="text-ink-soft">Loading...</div>
            : <GroupedSickTable data={data ?? []} commentCache={commentCache} setCommentCache={setCommentCache} onEnd={() => queryClient.invalidateQueries()} />
          }
        </div>
      )}
      {view === "7d" && <DayGrid days={7} />}
      {view === "14d" && <DayGrid days={14} />}
    </div>
  )
}
