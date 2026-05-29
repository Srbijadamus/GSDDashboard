import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"

const BASE = "https://n8jlr9dr-5000.euw.devtunnels.ms"

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
      onSaved()
      onClose()
    } catch { setError("Fehler beim Speichern") }
    finally { setSaving(false) }
  }
  const inp: React.CSSProperties = { background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)", padding: "8px 12px", borderRadius: 6, fontSize: 13, outline: "none", width: "100%" }
  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }} onClick={onClose}>
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, width: 480, overflow: "hidden" }} onClick={e => e.stopPropagation()}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "16px 20px", borderBottom: "1px solid var(--border)" }}>
          <span style={{ fontWeight: 600, fontSize: 15 }}>Krankmeldung erfassen</span>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer" }}>X</button>
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
              <input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} style={inp} />
            </div>
            <div>
              <label style={{ fontSize: 11, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Ende</label>
              <input type="date" value={endDate} onChange={e => setEndDate(e.target.value)} style={inp} />
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
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer" }}>X</button>
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
                const lastDay = new Date(s.lastDay)
                lastDay.setDate(lastDay.getDate() + 1)
                const expectedReturn = lastDay.toISOString().split("T")[0]
                const isCritical = daysSoFar >= 10
                const isWarn = daysSoFar >= 5
                return (
                  <tr key={i} style={{ borderBottom: "1px solid rgba(30,45,69,.5)" }}>
                    <td style={{ padding: "8px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{s.employeeId}</td>
                    <td style={{ padding: "8px 12px", fontWeight: 500 }}>{s.fullName ?? (s.firstName ?? "") + " " + (s.lastName ?? "")}</td>
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
  const dates = Array.from({ length: days }, (_, i) => {
    const d = new Date(today)
    d.setDate(today.getDate() + i)
    return d
  })
  const from = dates[0].toISOString().split("T")[0]
  const to = dates[dates.length - 1].toISOString().split("T")[0]
  const { data, isLoading } = useQuery({
    queryKey: ["sl-range", from, to],
    queryFn: () => fetch(BASE + "/api/sickleave?from=" + from + "&to=" + to).then(r => r.json())
  })
  if (isLoading) return <div style={{ padding: 24, color: "var(--text3)", textAlign: "center" }}>Loading...</div>
  const entries: any[] = data ?? []
  const sickDates: Record<string, Set<string>> = {}
  const agentInfo: Record<string, any> = {}
  entries.forEach((e: any) => {
    const id = String(e.employeeId)
    if (!sickDates[id]) sickDates[id] = new Set()
    agentInfo[id] = e
    const start = new Date(e.firstDay)
    const end = new Date(e.lastDay)
    for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
      sickDates[id].add(d.toISOString().split("T")[0])
    }
  })
  const agentIds = Object.keys(sickDates)
  const dayCount = dates.map(d => {
    const ds = d.toISOString().split("T")[0]
    return agentIds.filter(id => sickDates[id].has(ds)).length
  })
  const dayHeaderColor = (count: number) => {
    if (count >= 6) return "var(--danger)"
    if (count >= 3) return "#facc15"
    return "#4ade80"
  }
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
              const name = info.fullName || ((info.firstName ?? "") + " " + (info.lastName ?? "")).trim() || ("ID " + id)
              return (
                <tr key={id} style={{ borderBottom: "1px solid rgba(30,45,69,.5)" }}>
                  <td style={{ padding: "8px 14px", position: "sticky", left: 0, background: "var(--card)", zIndex: 1 }}>
                    <div style={{ fontWeight: 500, fontSize: 12 }}>{name}</div>
                    <div style={{ fontSize: 10, color: "var(--text3)" }}>{info.teamLeadName}</div>
                  </td>
                  {dates.map(d => {
                    const ds = d.toISOString().split("T")[0]
                    const sick = sickDates[id].has(ds)
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

export default function SickLeave() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [teamLead, setTeamLead] = useState("")
  const [type, setType] = useState("")
  const [activeOnly, setActiveOnly] = useState(false)
  const [modal, setModal] = useState<null | { title: string; entries: any[] }>(null)
  const [showAdd, setShowAdd] = useState(false)
  const [view, setView] = useState<"today" | "7d" | "14d">("today")
  const params = (activeOnly ? "activeOnly=true" : "from=2026-01-01&to=2026-12-31") + (teamLead ? "&teamLead=" + teamLead : "") + (type ? "&type=" + type : "")
  const { data, isLoading } = useQuery({ queryKey: ["sl", params], queryFn: () => api.sickLeave.get(params) })
  const { data: stats } = useQuery({ queryKey: ["sl-stats"], queryFn: api.sickLeave.stats })
  const { data: activeToday } = useQuery({
    queryKey: ["sl-active-today"],
    queryFn: () => fetch(BASE + "/api/sickleave?activeOnly=true").then(r => r.json())
  })
  const durationColor = (days: number | null) => {
    if (!days) return "var(--text3)"
    if (days > 30) return "var(--danger)"
    if (days >= 14) return "var(--warn)"
    if (days >= 7) return "#facc15"
    return "var(--text2)"
  }
  const durationBg = (days: number | null) => {
    if (!days) return "transparent"
    if (days > 30) return "rgba(255,59,92,.08)"
    if (days >= 14) return "rgba(255,124,59,.08)"
    if (days >= 7) return "rgba(250,204,21,.08)"
    return "transparent"
  }
  const cards = [
    { label: "Currently Sick", value: stats?.totalActive, color: "var(--danger)", onClick: () => setModal({ title: "Currently Sick Agents", entries: activeToday ?? [] }) },
    { label: "Self", value: stats?.selfCount, color: "var(--warn)", onClick: () => setModal({ title: "Self Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Self") }) },
    { label: "Child", value: stats?.childCount, color: "#facc15", onClick: () => setModal({ title: "Child Active", entries: (activeToday ?? []).filter((e: any) => e.leaveType === "Child") }) },
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
            {s.onClick && <div style={{ fontSize: 10, color: "var(--text3)", marginTop: 4 }}>klik za detalje</div>}
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
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <thead>
                  <tr style={{ background: "var(--card2)" }}>
                    {["ID", "Name", "Team Lead", "First Day", "Last Day", "Duration", "Type", "Comments"].map(h => (
                      <th key={h} style={{ padding: "10px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", borderBottom: "1px solid var(--border)" }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {isLoading && <tr><td colSpan={8} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>Loading...</td></tr>}
                  {data?.map((s: any) => (
                    <tr key={s.id} style={{ borderBottom: "1px solid rgba(30,45,69,.5)", background: durationBg(s.durationDays) }}
                      onMouseEnter={ev => (ev.currentTarget.style.filter = "brightness(1.15)")}
                      onMouseLeave={ev => (ev.currentTarget.style.filter = "brightness(1)")}>
                      <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{s.employeeId}</td>
                      <td style={{ padding: "9px 12px", fontWeight: 500 }}>{s.fullName ?? (s.firstName ?? "") + " " + (s.lastName ?? "")}</td>
                      <td style={{ padding: "9px 12px", color: "var(--text2)", fontSize: 11 }}>{s.teamLeadName}</td>
                      <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{s.firstDay}</td>
                      <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{s.lastDay}</td>
                      <td style={{ padding: "9px 12px" }}>
                        <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, fontWeight: 600, color: durationColor(s.durationDays) }}>{s.durationDays ?? "?"}d</span>
                      </td>
                      <td style={{ padding: "9px 12px" }}>
                        <span style={{ background: s.leaveType === "Self" ? "rgba(255,124,59,.15)" : "rgba(250,204,21,.15)", color: s.leaveType === "Self" ? "var(--warn)" : "#facc15", padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono" }}>{s.leaveType}</span>
                      </td>
                      <td style={{ padding: "9px 12px", color: "var(--text3)", fontSize: 11 }}>{s.comments}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>{data?.length ?? 0} records</div>
          </div>
        </div>
      )}
      {view === "7d" && <DayGrid days={7} />}
      {view === "14d" && <DayGrid days={14} />}
    </div>
  )
}
