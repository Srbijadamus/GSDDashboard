import { useState, useEffect } from "react"

const KIOSK = "https://lj9dgfgw-8000.euw.devtunnels.ms"
const today = new Date().toISOString().split("T")[0]
const minus7 = new Date(Date.now() - 7*24*60*60*1000).toISOString().split("T")[0]

function formatTime(dt: string | null): string {
  if (!dt) return "—"
  const parts = dt.split(" ")
  if (parts.length < 2) return "—"
  return parts[1].slice(0,5)
}
function formatDuration(minutes: number | null): string {
  if (minutes === null || minutes === undefined) return "—"
  if (minutes < 0) return "0m"
  const h = Math.floor(minutes / 60), m = minutes % 60
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}
function LocationBadge({ location }: { location: string }) {
  if (!location) return null
  return (
    <div style={{ display:"flex", gap:4, flexWrap:"wrap" }}>
      {location.split("|").map(s => s.trim()).filter(Boolean).map((p,i) => (
        <span key={i} style={{ background:"rgba(96,165,250,0.12)", border:"1px solid rgba(96,165,250,0.3)", color:"#60a5fa", borderRadius:4, fontSize:10, padding:"2px 8px" }}>{p}</span>
      ))}
    </div>
  )
}
const inputStyle: any = { background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text)", padding:"7px 10px", borderRadius:6, fontSize:12, fontFamily:"IBM Plex Sans", outline:"none" }
const thStyle: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)", background:"var(--card2)", textAlign:"left" as const }
const tdStyle: any = { padding:"9px 12px", borderBottom:"1px solid rgba(30,45,69,.5)", fontSize:12, color:"var(--text)" }

export default function WicAttendance() {
  const [tab, setTab] = useState<"live"|"report">("live")
  const [date, setDate] = useState(today)
  const [attendance, setAttendance] = useState<any[]>([])
  const [history, setHistory] = useState<any[]>([])
  const [loading, setLoading] = useState(false)
  const [reportFrom, setReportFrom] = useState(minus7)
  const [reportTo, setReportTo] = useState(today)
  const [locationFilter, setLocationFilter] = useState("")
  const [teamLeadFilter, setTeamLeadFilter] = useState("")
  const [lastUpdated, setLastUpdated] = useState<Date|null>(null)
  const [clockStr, setClockStr] = useState("")

  const fetchLive = async () => {
    try {
      const r = await fetch(`${KIOSK}/api/attendance?date=${date}`)
      const d = await r.json()
      const filtered = d.filter((a: any) => a.location !== null && a.location !== "")
      const sorted = [...filtered].sort((a: any, b: any) => {
        const aA = a.attendance_status === "ACTIVE" ? 0 : a.attendance_status === "DONE" ? 1 : 2
        const bA = b.attendance_status === "ACTIVE" ? 0 : b.attendance_status === "DONE" ? 1 : 2
        if (aA !== bA) return aA - bA
        return (a.full_name || "").localeCompare(b.full_name || "")
      })
      setAttendance(sorted)
      setLastUpdated(new Date())
    } catch {}
  }

  const fetchHistory = async () => {
    setLoading(true)
    try {
      const r = await fetch(`${KIOSK}/api/history?from=${reportFrom}&to=${reportTo}`)
      const d = await r.json()
      setHistory(d.filter((a: any) => a.location !== null && a.location !== ""))
    } catch {}
    setLoading(false)
  }

  useEffect(() => {
    if (tab === "live") {
      fetchLive()
      const iv = setInterval(fetchLive, 30000)
      return () => clearInterval(iv)
    }
  }, [tab, date])

  useEffect(() => {
    const iv = setInterval(() => { if (lastUpdated) setClockStr(lastUpdated.toLocaleTimeString()) }, 1000)
    return () => clearInterval(iv)
  }, [lastUpdated])

  const locations = [...new Set(attendance.map((a: any) => a.location).filter(Boolean))]
  const teamLeads = [...new Set(attendance.map((a: any) => a.team_leader).filter(Boolean))]
  const filtered  = attendance.filter((a: any) => {
    if (locationFilter && a.location !== locationFilter) return false
    if (teamLeadFilter && a.team_leader !== teamLeadFilter) return false
    return true
  })

  const activeCount = attendance.filter((a: any) => a.attendance_status === "ACTIVE").length
  const doneCount = attendance.filter((a: any) => a.attendance_status === "DONE").length
  const notInCount = attendance.filter((a: any) => a.attendance_status === "NOT_CHECKED_IN").length

  const exportCSV = () => {
    const header = '"Name","Mitarbeiter-Nr.","Team Lead","WIC Zentrum","Datum","Einloggen","Ausloggen","Dauer (Min)","Status"'
    const rows = history.map((a: any) => {
      const status = a.checkin_time !== null && a.checkout_time === null ? "ANWESEND" : a.checkout_time !== null ? "ABGEMELDET" : "NICHT ERSCHIENEN"
      return `"${a.full_name}","${a.employee_id}","${a.team_leader}","${a.location}","${a.work_date}","${a.checkin_time ?? ""}","${a.checkout_time ?? ""}","${a.minutes_on_shift ?? ""}","${status}"`
    })
    const csv = [header, ...rows].join("\n")
    const blob = new Blob([csv], { type:"text/csv;charset=utf-8;" })
    const url = URL.createObjectURL(blob)
    const el = document.createElement("a")
    el.href = url; el.download = `WIC_Anwesenheit_${reportFrom}_${reportTo}.csv`; el.click()
  }

  const historyByDate: Record<string,any[]> = {}
  history.forEach((a: any) => { if (!historyByDate[a.work_date]) historyByDate[a.work_date] = []; historyByDate[a.work_date].push(a) })

  const dotStyle = (ag: any) => ({
    width:10, height:10, borderRadius:"50%", display:"inline-block", flexShrink:0,
    background: ag.attendance_status === "ACTIVE" ? "#22c55e" : ag.attendance_status === "DONE" ? "#60a5fa" : "#ef4444",
    animation: ag.attendance_status === "ACTIVE" ? "pulse-green 2s infinite" : "none"
  })
  const statusColor = (ag: any) => ag.attendance_status === "ACTIVE" ? "#22c55e" : ag.attendance_status === "DONE" ? "#60a5fa" : "#ef4444"
  const statusText  = (ag: any) => ag.attendance_status === "ACTIVE" ? "ACTIVE" : ag.attendance_status === "DONE" ? "DONE" : "NOT IN"

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <style>{`@keyframes pulse-green { 0% { box-shadow: 0 0 0 0 rgba(34,197,94,0.6); } 70% { box-shadow: 0 0 0 8px rgba(34,197,94,0); } 100% { box-shadow: 0 0 0 0 rgba(34,197,94,0); } }`}</style>

      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>WIC Attendance</h1>
        <div style={{ display:"flex", gap:8, alignItems:"center" }}>
          {tab === "live" && <>
            <input type="date" value={date} onChange={e => setDate(e.target.value)} style={inputStyle} />
            <button onClick={fetchLive} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Refresh</button>
            {lastUpdated && <span style={{ fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>Last updated: {clockStr}</span>}
          </>}
        </div>
      </div>

      <div style={{ display:"flex", gap:4 }}>
        {(["live","report"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} style={{ background:tab===t?"var(--accent)":"var(--card)", border:`1px solid ${tab===t?"var(--accent)":"var(--border)"}`, color:tab===t?"#fff":"var(--text2)", borderRadius:6, padding:"6px 16px", fontSize:12, cursor:"pointer", fontWeight:tab===t?600:400, textTransform:"capitalize" }}>
            {t === "live" ? "Live View" : "Report"}
          </button>
        ))}
      </div>

      {tab === "live" && (<>
        <div style={{ display:"grid", gridTemplateColumns:"repeat(4,1fr)", gap:12 }}>
          {[
            { label:"Active Now",       value:activeCount, color:"#22c55e" },
            { label:"WIC Agents Today", value:attendance.length, color:"var(--text)" },
            { label:"Checked Out",      value:doneCount,   color:"#60a5fa" },
            { label:"Not Checked In",   value:notInCount,  color:"#ef4444" },
          ].map(c => (
            <div key={c.label} style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"14px 18px" }}>
              <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>{c.label}</div>
              <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:c.color }}>{c.value}</div>
            </div>
          ))}
        </div>

        <div style={{ display:"flex", gap:8 }}>
          <select value={locationFilter} onChange={e => setLocationFilter(e.target.value)} style={inputStyle}>
            <option value="">All Locations</option>
            {locations.map((l: any) => <option key={l} value={l}>{l}</option>)}
          </select>
          <select value={teamLeadFilter} onChange={e => setTeamLeadFilter(e.target.value)} style={inputStyle}>
            <option value="">All Team Leads</option>
            {teamLeads.map((tl: any) => <option key={tl} value={tl}>{tl}</option>)}
          </select>
        </div>

        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead><tr>{["Agent","WIC Center","Check In","Check Out","Duration","Status"].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
              <tbody>
                {filtered.length === 0 && <tr><td colSpan={6} style={{ ...tdStyle, textAlign:"center", color:"var(--text3)", padding:40 }}>No WIC agents found for this date.</td></tr>}
                {filtered.map((ag: any, i: number) => (
                  <tr key={i} onMouseEnter={e => e.currentTarget.style.background="var(--card2)"} onMouseLeave={e => e.currentTarget.style.background="transparent"}>
                    <td style={tdStyle}>
                      <div style={{ fontWeight:500 }}>{ag.full_name}</div>
                      <div style={{ fontSize:9, fontFamily:"IBM Plex Mono", color:"var(--text3)" }}>{ag.employee_id}</div>
                      {ag.location && <span style={{ display:"inline-block", marginTop:2, background:"rgba(96,165,250,0.12)", border:"1px solid rgba(96,165,250,0.3)", color:"#60a5fa", borderRadius:4, fontSize:9, padding:"1px 5px", fontWeight:600 }}>{ag.location}</span>}
                    </td>
                    <td style={tdStyle}><LocationBadge location={ag.location} /></td>
                    <td style={{ ...tdStyle, color: ag.checkin_time ? "#22c55e" : "var(--text3)", fontFamily:"IBM Plex Mono", fontSize:11 }}>{formatTime(ag.checkin_time)}</td>
                    <td style={{ ...tdStyle, color: ag.checkout_time ? "var(--text2)" : "var(--text3)", fontFamily:"IBM Plex Mono", fontSize:11 }}>{formatTime(ag.checkout_time)}</td>
                    <td style={{ ...tdStyle, color:"var(--text2)", fontFamily:"IBM Plex Mono", fontSize:11 }}>{formatDuration(ag.minutes_on_shift)}</td>
                    <td style={{ padding:"9px 12px" }}>
                      <div style={{ display:"flex", alignItems:"center", gap:8 }}>
                        <span style={dotStyle(ag)} />
                        <span style={{ fontSize:10, fontWeight:600, textTransform:"uppercase" as const, color:statusColor(ag) }}>{statusText(ag)}</span>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{filtered.length} agents</div>
        </div>
      </>)}

      {tab === "report" && (<>
        <div style={{ display:"flex", gap:8, alignItems:"center", flexWrap:"wrap" }}>
          <div style={{ display:"flex", alignItems:"center", gap:6 }}>
            <span style={{ fontSize:11, color:"var(--text3)" }}>Von</span>
            <input type="date" value={reportFrom} onChange={e => setReportFrom(e.target.value)} style={inputStyle} />
          </div>
          <div style={{ display:"flex", alignItems:"center", gap:6 }}>
            <span style={{ fontSize:11, color:"var(--text3)" }}>Bis</span>
            <input type="date" value={reportTo} onChange={e => setReportTo(e.target.value)} style={inputStyle} />
          </div>
          <button onClick={fetchHistory} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>{loading ? "Laden..." : "Load Report"}</button>
          <button onClick={exportCSV} style={{ background:"rgba(34,197,94,0.15)", border:"1px solid #22c55e", color:"#22c55e", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>⬇ Export Excel</button>
          <span style={{ fontSize:10, color:"var(--text3)" }}>CSV-Datei kann direkt in Excel geöffnet werden</span>
        </div>

        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead><tr>{["Datum","Agent","Mitarbeiter-Nr.","WIC Zentrum","Einloggen","Ausloggen","Dauer","Status"].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
              <tbody>
                {Object.keys(historyByDate).length === 0 && <tr><td colSpan={8} style={{ ...tdStyle, textAlign:"center", color:"var(--text3)", padding:40 }}>Keine Daten für diesen Zeitraum.</td></tr>}
                {Object.entries(historyByDate).sort(([a],[b]) => b.localeCompare(a)).map(([dateStr, rows]) => (<>
                  <tr key={`sep-${dateStr}`}>
                    <td colSpan={8} style={{ background:"rgba(30,45,69,.4)", fontSize:10, fontWeight:600, color:"var(--text2)", padding:"4px 12px" }}>
                      {new Date(dateStr).toLocaleDateString("de-DE", { weekday:"long", year:"numeric", month:"2-digit", day:"2-digit" })}
                    </td>
                  </tr>
                  {(rows as any[]).map((a: any, i: number) => (
                    <tr key={`${dateStr}-${i}`} onMouseEnter={e => e.currentTarget.style.background="var(--card2)"} onMouseLeave={e => e.currentTarget.style.background="transparent"}>
                      <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11 }}>{new Date(a.work_date).toLocaleDateString("de-DE")}</td>
                      <td style={tdStyle}><div style={{ fontWeight:500 }}>{a.full_name}</div></td>
                      <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{a.employee_id}</td>
                      <td style={tdStyle}><LocationBadge location={a.location || ""} /></td>
                      <td style={{ ...tdStyle, color: a.checkin_time ? "#22c55e" : "var(--text3)", fontFamily:"IBM Plex Mono", fontSize:11 }}>{formatTime(a.checkin_time)}</td>
                      <td style={{ ...tdStyle, color: a.checkout_time ? "var(--text2)" : "var(--text3)", fontFamily:"IBM Plex Mono", fontSize:11 }}>{formatTime(a.checkout_time)}</td>
                      <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text2)" }}>{formatDuration(a.minutes_on_shift)}</td>
                      <td style={{ padding:"9px 12px" }}>
                        <span style={{ display:"inline-flex", alignItems:"center", gap:5, fontSize:10, fontWeight:600, color: a.checkin_time !== null ? (a.checkout_time !== null ? "#60a5fa" : "#22c55e") : "#ef4444" }}>
                          <span style={{ width:7, height:7, borderRadius:"50%", display:"inline-block", background: a.checkin_time !== null ? (a.checkout_time !== null ? "#60a5fa" : "#22c55e") : "#ef4444" }} />
                          {a.checkin_time !== null ? (a.checkout_time !== null ? "DONE" : "ACTIVE") : "NOT IN"}
                        </span>
                      </td>
                    </tr>
                  ))}
                </>))}
              </tbody>
            </table>
          </div>
          <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{history.length} records</div>
        </div>
      </>)}
    </div>
  )
}

