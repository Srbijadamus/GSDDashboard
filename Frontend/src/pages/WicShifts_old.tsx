import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useRef, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { X } from "lucide-react"

const BASE = ""

function toMin(t: string | null): number {
  if (!t) return 0
  const p = t.replace(" ","").split(":")
  return parseInt(p[0]) * 60 + parseInt(p[1])
}

function calcAvailable(agentStart: string | null, agentEnd: string | null, wicOpen: string | null, wicClose: string | null): { hours: number; tooShort: boolean } {
  if (!agentStart || !agentEnd || !wicOpen || !wicClose) return { hours: 0, tooShort: false }
  const aStart = toMin(agentStart), aEnd = toMin(agentEnd), wClose = toMin(wicClose)
  if (aEnd <= wClose) return { hours: 0, tooShort: aEnd < wClose }
  const available = Math.round((aEnd - Math.max(wClose, aStart)) / 60)
  return { hours: available > 0 ? available : 0, tooShort: false }
}

const isCellEditable = (s: any) => {
  if (!s) return false
  const ws = (s.workingShift ?? "").toUpperCase()
  if (ws.includes("AL") || ws.includes("SL")) return false
  if (s.isOffDay) return false
  return true
}

function UncoveredStrip({ cards }: { cards: any[] }) {
  const uncovered = cards.filter(c => c.coverageStatus === "UNCOVERED" && !c.todaySchedule?.isClosed)
  if (uncovered.length === 0) return null
  return (
    <div style={{ background:"rgba(255,59,92,.08)", border:"1px solid rgba(255,59,92,.3)", borderRadius:8, padding:"10px 16px" }}>
      <div style={{ fontSize:11, color:"var(--danger)", fontWeight:600, marginBottom:6 }}>{uncovered.length} location{uncovered.length > 1 ? "s" : ""} uncovered today</div>
      {uncovered.map(c => (
        <div key={c.locationCode} style={{ display:"flex", alignItems:"center", gap:10, fontSize:11, color:"var(--text2)", marginBottom:3 }}>
          <span style={{ color:"var(--text)", fontWeight:500 }}>{c.displayName}</span>
          <span style={{ color:"var(--text3)" }}>{c.country}</span>
          <span style={{ fontFamily:"IBM Plex Mono", fontSize:10, color:"var(--text3)" }}>{c.todaySchedule?.openTime}-{c.todaySchedule?.closeTime}</span>
          <span style={{ background:"rgba(255,59,92,.15)", color:"var(--danger)", padding:"1px 6px", borderRadius:4, fontSize:10, fontFamily:"IBM Plex Mono" }}>0 agents</span>
        </div>
      ))}
    </div>
  )
}

function DrillDownModal({ type, cards, onClose }: { type: "UNCOVERED"|"PARTIAL"; cards: any[]; onClose: () => void }) {
  const filtered = cards.filter(c => c.coverageStatus === type && !c.todaySchedule?.isClosed)
  const color = type === "UNCOVERED" ? "var(--danger)" : "var(--warn)"
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.75)", zIndex:1000, display:"flex", alignItems:"center", justifyContent:"center" }}
      onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:10, padding:24, width:620, maxHeight:"80vh", overflowY:"auto" }}>
        <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center", marginBottom:16 }}>
          <h2 style={{ fontSize:15, fontWeight:600, color }}>{type === "UNCOVERED" ? "Uncovered" : "Partial"} Locations</h2>
          <button onClick={onClose} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer" }}><X size={18} /></button>
        </div>
        <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
          <thead><tr style={{ background:"var(--card2)" }}>
            {["Location","City","Country","Opening Hours","Assigned"].map(h => (
              <th key={h} style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
            ))}
          </tr></thead>
          <tbody>
            {filtered.map(c => (
              <tr key={c.locationCode} style={{ borderBottom:"1px solid rgba(30,45,69,.5)" }}>
                <td style={{ padding:"9px 10px", fontWeight:500 }}>{c.displayName}</td>
                <td style={{ padding:"9px 10px", color:"var(--text2)" }}>{c.city}</td>
                <td style={{ padding:"9px 10px" }}><span style={{ background: c.country==="NL"?"rgba(255,124,59,.15)":"rgba(59,126,255,.15)", color: c.country==="NL"?"var(--warn)":"var(--accent)", padding:"2px 6px", borderRadius:4, fontSize:10 }}>{c.country}</span></td>
                <td style={{ padding:"9px 10px", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{c.todaySchedule?.rawSchedule ?? "—"}</td>
                <td style={{ padding:"9px 10px", textAlign:"center" }}><span style={{ fontFamily:"IBM Plex Mono", fontSize:12, fontWeight:700, color }}>{c.assignedAgents?.length ?? 0}</span></td>
              </tr>
            ))}
            {filtered.length === 0 && <tr><td colSpan={5} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>No locations</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function CellActionMenu({ shift, locations, wicCards, onPatch, onClose }: {
  shift: any; locations: any[]; wicCards: any[]; onPatch: (id: number, body: any) => void; onClose: () => void
}) {
  const [showLocPicker, setShowLocPicker] = useState(false)
  const [search, setSearch] = useState("")
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handler = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose() }
    document.addEventListener("mousedown", handler)
    return () => document.removeEventListener("mousedown", handler)
  }, [onClose])

  const filteredLocs = locations.filter(l => l.displayName.toLowerCase().includes(search.toLowerCase()))

  if (showLocPicker) return (
    <div ref={ref} style={{ position:"absolute", zIndex:100, background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:8, width:260, boxShadow:"0 4px 20px rgba(0,0,0,.4)" }}>
      <input autoFocus value={search} onChange={e => setSearch(e.target.value)} placeholder="Search location..."
        style={{ width:"100%", background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text)", padding:"5px 8px", borderRadius:5, fontSize:11, outline:"none", marginBottom:6 }} />
      <div style={{ maxHeight:200, overflowY:"auto" }}>
        {filteredLocs.map(l => {
          const card = wicCards?.find((c: any) => c.locationCode === l.locationCode || c.displayName === l.displayName)
          const oh = card?.todaySchedule
          return (
            <div key={l.locationCode} onClick={() => { onPatch(shift.id, { task:"WIC", supportLocation: l.displayName }); onClose() }}
              style={{ padding:"6px 8px", borderRadius:5, cursor:"pointer", fontSize:11, display:"flex", justifyContent:"space-between", alignItems:"center" }}
              onMouseEnter={e => (e.currentTarget.style.background = "var(--card2)")}
              onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
              <span style={{ color:"var(--text)" }}>{l.displayName}</span>
              {oh && <span style={{ fontFamily:"IBM Plex Mono", fontSize:9, color:"var(--text3)" }}>{oh.openTime}-{oh.closeTime}</span>}
            </div>
          )
        })}
      </div>
    </div>
  )

  return (
    <div ref={ref} style={{ position:"absolute", zIndex:100, background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:6, width:200, boxShadow:"0 4px 20px rgba(0,0,0,.4)" }}>
      <div style={{ fontSize:10, color:"var(--text3)", padding:"4px 8px", marginBottom:4, fontWeight:600 }}>
        {shift.fullName} — {shift.shiftDate?.slice(5)}
      </div>
      <div onClick={() => setShowLocPicker(true)}
        style={{ padding:"7px 10px", borderRadius:5, cursor:"pointer", fontSize:11, color:"var(--text)" }}
        onMouseEnter={e => (e.currentTarget.style.background = "var(--card2)")}
        onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
        Change WIC Location
      </div>
      <div onClick={() => { onPatch(shift.id, { task:"Voice", supportLocation:"" }); onClose() }}
        style={{ padding:"7px 10px", borderRadius:5, cursor:"pointer", fontSize:11, color:"#4ade80" }}
        onMouseEnter={e => (e.currentTarget.style.background = "var(--card2)")}
        onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
        Move to Voice
      </div>
      <div onClick={() => { onPatch(shift.id, { task:"Backlog", supportLocation:"" }); onClose() }}
        style={{ padding:"7px 10px", borderRadius:5, cursor:"pointer", fontSize:11, color:"var(--warn)" }}
        onMouseEnter={e => (e.currentTarget.style.background = "var(--card2)")}
        onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
        Move to Backlog
      </div>
      {(shift.task === "Voice" || shift.task === "Backlog") && (
        <div onClick={() => { onPatch(shift.id, { task:"WIC", supportLocation:"" }); onClose() }}
          style={{ padding:"7px 10px", borderRadius:5, cursor:"pointer", fontSize:11, color:"#7eb8ff" }}
          onMouseEnter={e => (e.currentTarget.style.background = "var(--card2)")}
          onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
          Move to WIC
        </div>
      )}
    </div>
  )
}

function AvailableSummary({ dates, empList, wicCards, from, to }: { dates: string[]; empList: any[]; wicCards: any[]; from: string; to: string }) {
  const [tab, setTab] = useState<"summary"|"detail">("summary")
  const [filterTeam, setFilterTeam] = useState("")

  const { data: detailData } = useQuery({
    queryKey: ["wic-avail-hours", from, to, filterTeam],
    queryFn: () => fetch(BASE + "/api/wic/availableHours?from=" + from + "&to=" + to + (filterTeam ? "&teamLead=" + filterTeam : "")).then(r => r.json()),
    enabled: tab === "detail"
  })

  const downloadCsv = () => {
    const a = document.createElement("a")
    a.href = BASE + "/api/wic/availableHours/download?from=" + from + "&to=" + to + (filterTeam ? "&teamLead=" + filterTeam : "")
    a.download = "AvailableHours.csv"
    a.click()
  }

  const rows = dates.map(d => {
    const dt = new Date(d)
    if (dt.getDay() === 0 || dt.getDay() === 6) return null
    let wicAgents = 0, totalAvail = 0
    const breakdown: Record<number, number> = {}
    empList.forEach(({ shifts }) => {
      const s = shifts[d]
      if (!s || !s.isOnSite) return
      wicAgents++
      const ws = s.workingShift ?? ""
      const times = ws.match(/(\d{2}:\d{2})\s*[-–]\s*(\d{2}:\d{2})/)
      const card = wicCards?.find((c: any) => c.displayName === s.supportLocation || c.city === s.supportLocation)
      const { hours } = calcAvailable(times?.[1]??null, times?.[2]??null, card?.todaySchedule?.openTime??null, card?.todaySchedule?.closeTime??null)
      if (hours > 0) { totalAvail += hours; breakdown[hours] = (breakdown[hours] ?? 0) + 1 }
    })
    const breakdownStr = Object.entries(breakdown).sort((a,b)=>Number(b[0])-Number(a[0])).map(([h,n])=>n+"x +"+h+"h").join(", ")
    const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
    return { date:d, label: dow+" "+dt.getDate().toString().padStart(2,"0")+"."+(dt.getMonth()+1).toString().padStart(2,"0"), wicAgents, totalAvail, breakdownStr }
  }).filter(Boolean)

  const teams = [...new Set((detailData??[]).map((r:any)=>r.teamLead).filter(Boolean))]
  const filteredDetail = (detailData??[]).filter((r:any)=>!filterTeam||r.teamLead===filterTeam)

  return (
    <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center", padding:"10px 14px", borderBottom:"1px solid var(--border)" }}>
        <div style={{ fontSize:11, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)" }}>Available Hours for Backlog / Voice</div>
        <div style={{ display:"flex", gap:6, alignItems:"center" }}>
          {tab === "detail" && (
            <>
              <select value={filterTeam} onChange={e => setFilterTeam(e.target.value)}
                style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"4px 8px", borderRadius:5, fontSize:11 }}>
                <option value="">All teams</option>
                {teams.map((t:any) => <option key={t} value={t}>{t}</option>)}
              </select>
              <button onClick={downloadCsv} style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"4px 10px", borderRadius:5, fontSize:11, cursor:"pointer" }}>CSV</button>
            </>
          )}
          <div style={{ display:"flex", gap:2, background:"var(--card2)", border:"1px solid var(--border)", borderRadius:6, padding:3 }}>
            {(["summary","detail"] as const).map(t => (
              <button key={t} onClick={() => setTab(t)} style={{ background: tab===t?"var(--accent)":"transparent", color: tab===t?"#fff":"var(--text2)", border:"none", borderRadius:4, padding:"4px 12px", fontSize:11, cursor:"pointer", fontWeight: tab===t?600:400 }}>
                {t === "summary" ? "Summary" : "Detail"}
              </button>
            ))}
          </div>
        </div>
      </div>
      {tab === "summary" && (
        <table style={{ width:"100%", borderCollapse:"collapse", fontSize:11 }}>
          <thead><tr style={{ background:"var(--card2)" }}>
            {["Date","WIC Agents","Total Available Hours","Breakdown"].map(h => (
              <th key={h} style={{ padding:"8px 12px", textAlign:"left", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
            ))}
          </tr></thead>
          <tbody>
            {rows.map((r:any) => (
              <tr key={r.date} style={{ borderBottom:"1px solid rgba(30,45,69,.4)" }}>
                <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text2)" }}>{r.label}</td>
                <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", textAlign:"center" }}>{r.wicAgents}</td>
                <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", fontWeight:700, color: r.totalAvail>0?"var(--accent2)":"var(--text3)" }}>{r.totalAvail>0?"+"+r.totalAvail+"h":"—"}</td>
                <td style={{ padding:"8px 12px", fontSize:10, color:"var(--text3)" }}>{r.breakdownStr||"—"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {tab === "detail" && (
        <table style={{ width:"100%", borderCollapse:"collapse", fontSize:11 }}>
          <thead><tr style={{ background:"var(--card2)" }}>
            {["Date","Name","Team Lead","Location","WIC Hours","Agent Shift","Free Hours"].map(h => (
              <th key={h} style={{ padding:"8px 12px", textAlign:"left", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
            ))}
          </tr></thead>
          <tbody>
            {filteredDetail.length === 0 && <tr><td colSpan={7} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>No data</td></tr>}
            {filteredDetail.map((r:any, i:number) => {
              const dt = new Date(r.date)
              const label = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]+" "+dt.getDate().toString().padStart(2,"0")+"."+(dt.getMonth()+1).toString().padStart(2,"0")
              return (
                <tr key={i} style={{ borderBottom:"1px solid rgba(30,45,69,.4)" }}>
                  <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", fontSize:10, color:"var(--text3)" }}>{label}</td>
                  <td style={{ padding:"8px 12px", fontWeight:500 }}>{r.name}</td>
                  <td style={{ padding:"8px 12px", fontSize:10, color:"var(--text2)" }}>{r.teamLead}</td>
                  <td style={{ padding:"8px 12px", fontSize:10, color:"var(--text2)" }}>{r.location}</td>
                  <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", fontSize:10, color:"var(--text3)" }}>{r.wicOpenTime}-{r.wicCloseTime}</td>
                  <td style={{ padding:"8px 12px", fontFamily:"IBM Plex Mono", fontSize:10, color:"var(--text3)" }}>{r.agentStartTime}-{r.agentEndTime}</td>
                  <td style={{ padding:"8px 12px" }}>
                    {r.status === "shift_too_short" ? (
                      <span style={{ fontSize:10, color:"var(--danger)", fontFamily:"IBM Plex Mono" }}>short</span>
                    ) : (
                      <div style={{ display:"flex", alignItems:"center", gap:6 }}>
                        <span style={{ fontFamily:"IBM Plex Mono", fontWeight:700, color:"var(--accent2)", fontSize:11, minWidth:32 }}>+{r.freeHours}h</span>
                        <div style={{ flex:1, height:6, background:"rgba(30,45,69,.5)", borderRadius:3, minWidth:60 }}>
                          <div style={{ height:"100%", borderRadius:3, background:"var(--accent2)", width: Math.min(100,(r.freeHours/5)*100)+"%" }} />
                        </div>
                      </div>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}

export default function WicShifts() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const today = new Date().toISOString().split("T")[0]
  const [days, setDays] = useState(7)
  const [teamLead, setTeamLead] = useState("")
  const [drillDown, setDrillDown] = useState<"UNCOVERED"|"PARTIAL"|null>(null)
  const [activeMenu, setActiveMenu] = useState<{ shiftId: number; empId: string; date: string } | null>(null)

  const dates: string[] = []
  for (let i = 0; i < days; i++) {
    const d = new Date(); d.setDate(d.getDate() + i)
    dates.push(d.toISOString().split("T")[0])
  }
  const from = dates[0], to = dates[dates.length - 1]

  const { data: wicCards } = useQuery({ queryKey: ["wic-cards", from], queryFn: () => apiFetch<any[]>("/api/wic/cards?date="+from) })
  const { data: locations } = useQuery({ queryKey: ["wic-locations"], queryFn: () => apiFetch<any[]>("/api/wic/locations") })
  const { data: shifts, isLoading } = useQuery({
    queryKey: ["wic-shifts-range", from, to, teamLead],
    queryFn: () => api.wic.shifts("from="+from+"&to="+to+(teamLead?"&teamLead="+teamLead:""))
  })

  const [localShifts, setLocalShifts] = useState<any[]>([])
  useEffect(() => { if (shifts) setLocalShifts(shifts) }, [shifts])

  const handlePatch = async (id: number, body: any) => {
    // Optimistic update
    setLocalShifts(prev => prev.map(s => s.id === id ? {
      ...s,
      task: body.task ?? s.task,
      supportLocation: body.supportLocation === "" ? null : (body.supportLocation ?? s.supportLocation),
      isOnSite: body.task === "Voice" || body.task === "Backlog" ? false : (body.supportLocation ? true : s.isOnSite)
    } : s))

    try {
      await fetch(BASE + "/api/wic/" + id, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      })
      queryClient.invalidateQueries({ queryKey: ["wic-cards"] })
    } catch {
      // Rollback
      setLocalShifts(shifts ?? [])
    }
  }

  const activeShifts = localShifts.length > 0 ? localShifts : (shifts ?? [])

  const byEmployee: Record<string, { emp: any; shifts: Record<string, any> }> = {}
  activeShifts.forEach((s: any) => {
    if (!byEmployee[s.employeeId]) byEmployee[s.employeeId] = { emp: { id: s.employeeId, name: s.fullName, teamLead: s.teamLeadName }, shifts: {} }
    byEmployee[s.employeeId].shifts[s.shiftDate] = s
  })
  const empList = Object.values(byEmployee)

  const freePerDay: Record<string, number> = {}
  dates.forEach(d => {
    const onSite = activeShifts.filter((s: any) => s.shiftDate === d && s.isOnSite).length ?? 0
    freePerDay[d] = Math.max(0, empList.length - onSite)
  })

  const covered   = wicCards?.filter((c: any) => c.coverageStatus === "COVERED").length ?? 0
  const partial   = wicCards?.filter((c: any) => c.coverageStatus === "PARTIAL").length ?? 0
  const uncovered = wicCards?.filter((c: any) => c.coverageStatus === "UNCOVERED" && !c.todaySchedule?.isClosed).length ?? 0
  const unassigned = activeShifts.filter((s: any) => s.shiftDate === today && s.isOnSite && !s.supportLocation).length

  const isWeekend = (d: string) => { const dt = new Date(d); return dt.getDay() === 0 || dt.getDay() === 6 }
  const isToday = (d: string) => d === today
  const dayLabel = (d: string) => {
    const dt = new Date(d)
    return ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()] + " " + dt.getDate().toString().padStart(2,"0") + "." + (dt.getMonth()+1).toString().padStart(2,"0")
  }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.wicShifts")}</h1>
        <DownloadButtons onToday={api.wic.downloadToday} on7Days={api.wic.download7} on30Days={api.wic.download30} />
      </div>

      <div style={{ display:"grid", gridTemplateColumns:"repeat(5, 1fr)", gap:12 }}>
        <div style={{ background:"var(--card)", border:"1px solid rgba(34,208,122,.2)", borderRadius:8, padding:"14px 18px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Covered</div>
          <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--green)" }}>{covered}</div>
        </div>
        <div onClick={() => setDrillDown("PARTIAL")} style={{ background:"var(--card)", border:"1px solid rgba(255,124,59,.2)", borderRadius:8, padding:"14px 18px", cursor:"pointer" }}
          onMouseEnter={e => (e.currentTarget.style.opacity=".8")} onMouseLeave={e => (e.currentTarget.style.opacity="1")}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Partial</div>
          <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--warn)" }}>{partial}</div>
          <div style={{ fontSize:9, color:"var(--text3)", marginTop:4 }}>click to see</div>
        </div>
        <div onClick={() => setDrillDown("UNCOVERED")} style={{ background:"var(--card)", border:"1px solid rgba(255,59,92,.2)", borderRadius:8, padding:"14px 18px", cursor:"pointer" }}
          onMouseEnter={e => (e.currentTarget.style.opacity=".8")} onMouseLeave={e => (e.currentTarget.style.opacity="1")}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Uncovered</div>
          <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--danger)" }}>{uncovered}</div>
          <div style={{ fontSize:9, color:"var(--text3)", marginTop:4 }}>click to see</div>
        </div>
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"14px 18px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Total Agents</div>
          <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--text)" }}>{empList.length}</div>
        </div>
        <div style={{ background:"var(--card)", border:"1px solid rgba(250,204,21,.2)", borderRadius:8, padding:"14px 18px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Unassigned</div>
          <div style={{ fontSize:26, fontWeight:600, fontFamily:"IBM Plex Mono", color:"#facc15" }}>{unassigned}</div>
          <div style={{ fontSize:9, color:"var(--text3)", marginTop:4 }}>WIC no location</div>
        </div>
      </div>

      {wicCards && <UncoveredStrip cards={wicCards} />}

      <div style={{ display:"flex", gap:8, alignItems:"center" }}>
        <input placeholder="Team Lead..." value={teamLead} onChange={e => setTeamLead(e.target.value)}
          style={{ background:"var(--card)", border:"1px solid var(--border)", color:"var(--text)", padding:"6px 10px", borderRadius:6, fontSize:12, outline:"none", width:180 }} />
        {[7, 14].map(d => (
          <button key={d} onClick={() => setDays(d)} style={{ background: days===d?"var(--accent)":"var(--card)", border:"1px solid "+(days===d?"var(--accent)":"var(--border)"), color: days===d?"#fff":"var(--text2)", padding:"6px 14px", borderRadius:6, fontSize:12, cursor:"pointer", fontFamily:"IBM Plex Mono" }}>{d}d</button>
        ))}
      </div>

      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
        <div style={{ overflowX:"auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:11 }}>
            <thead>
              <tr style={{ background:"var(--card2)" }}>
                <th style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)", minWidth:150, position:"sticky", left:0, background:"var(--card2)", zIndex:2 }}>Agent</th>
                <th style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)", minWidth:110 }}>Team Lead</th>
                {dates.map(d => {
                  const we = isWeekend(d), tod = isToday(d)
                  return (
                    <th key={d} style={{ padding:"6px 4px", textAlign:"center", fontSize:10, borderBottom:"1px solid var(--border)", borderLeft:"1px solid rgba(30,45,69,.5)", minWidth:82, background: tod?"rgba(59,126,255,.08)":we?"rgba(30,45,69,.3)":"transparent" }}>
                      <div style={{ color: tod?"var(--accent)":we?"var(--text3)":"var(--text2)", fontWeight: tod?700:400, whiteSpace:"nowrap" }}>{dayLabel(d)}</div>
                      {!we && <div style={{ fontSize:9, color:"var(--text3)", marginTop:2, fontFamily:"IBM Plex Mono" }}>{freePerDay[d]} free</div>}
                    </th>
                  )
                })}
              </tr>
            </thead>
            <tbody>
              {isLoading && <tr><td colSpan={2+dates.length} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>Loading...</td></tr>}
              {empList.map(({ emp, shifts: empShifts }) => (
                <tr key={emp.id} style={{ borderBottom:"1px solid rgba(30,45,69,.5)" }}
                  onMouseEnter={ev => (ev.currentTarget.style.background="var(--card2)")}
                  onMouseLeave={ev => (ev.currentTarget.style.background="transparent")}>
                  <td style={{ padding:"6px 10px", position:"sticky", left:0, background:"var(--card)", zIndex:1, borderRight:"1px solid rgba(30,45,69,.4)" }}>
                    <div style={{ fontWeight:500, fontSize:11 }}>{emp.name}</div>
                    <div style={{ fontFamily:"IBM Plex Mono", fontSize:9, color:"var(--text3)" }}>{emp.id}</div>
                  </td>
                  <td style={{ padding:"6px 10px", fontSize:10, color:"var(--text2)" }}>{emp.teamLead}</td>
                  {dates.map(d => {
                    const s = empShifts[d], we = isWeekend(d), tod = isToday(d)
                    if (we) return <td key={d} style={{ borderLeft:"1px solid rgba(30,45,69,.3)", background:"rgba(30,45,69,.2)" }} />
                    if (!s) return <td key={d} style={{ borderLeft:"1px solid rgba(30,45,69,.3)", background:tod?"rgba(59,126,255,.03)":"transparent", padding:"3px 4px", textAlign:"center" }}><span style={{ fontSize:9, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>-</span></td>

                    const task = s.task ?? "WIC"
                    const isOnSite = s.isOnSite
                    const ws = s.workingShift ?? ""
                    const times = ws.match(/(\d{2}:\d{2})\s*[-\u2013]\s*(\d{2}:\d{2})/)
                    const aStart = times?.[1] ?? null, aEnd = times?.[2] ?? null
                    const card = wicCards?.find((c: any) => c.displayName === s.supportLocation || c.city === s.supportLocation)
                    const { hours: availH, tooShort } = isOnSite ? calcAvailable(aStart, aEnd, card?.todaySchedule?.openTime??null, card?.todaySchedule?.closeTime??null) : { hours:0, tooShort:false }
                    const editable = isCellEditable(s)
                    const menuOpen = activeMenu?.shiftId === s.id && activeMenu?.date === d

                    let badgeColor = "rgba(126,184,255,.15)", badgeBorder = "rgba(126,184,255,.3)", badgeText = "#7eb8ff"
                    if (task === "Voice") { badgeColor="rgba(74,222,128,.12)"; badgeBorder="rgba(74,222,128,.3)"; badgeText="#4ade80" }
                    if (task === "Backlog") { badgeColor="rgba(255,124,59,.12)"; badgeBorder="rgba(255,124,59,.3)"; badgeText="var(--warn)" }
                    if (!s.supportLocation && task === "WIC") { badgeColor="rgba(250,204,21,.12)"; badgeBorder="rgba(250,204,21,.3)"; badgeText="#facc15" }

                    const badgeLabel = task === "Voice" ? "Voice" : task === "Backlog" ? "Backlog" : (!s.supportLocation ? "No Location" : (s.supportLocation.length > 10 ? s.supportLocation.substring(0,10)+"..." : s.supportLocation))

                    return (
                      <td key={d} style={{ borderLeft:"1px solid rgba(30,45,69,.3)", padding:"3px 4px", background:tod?"rgba(59,126,255,.03)":"transparent", position:"relative" }}>
                        <div style={{ display:"flex", flexDirection:"column", gap:2, alignItems:"center" }}>
                          <div onClick={editable ? () => setActiveMenu(menuOpen ? null : { shiftId:s.id, empId:emp.id, date:d }) : undefined}
                            style={{ background:badgeColor, border:"1px solid "+badgeBorder, borderRadius:3, padding:"2px 5px", fontSize:9, color:badgeText, fontFamily:"IBM Plex Mono", fontWeight:600, whiteSpace:"nowrap", textAlign:"center", cursor:editable?"pointer":"default" }}>
                            {!s.supportLocation && task==="WIC" ? "No Loc" : badgeLabel}
                          </div>
                          {aStart && aEnd && <div style={{ fontSize:8, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{aStart}-{aEnd}</div>}
                          {tooShort && <div style={{ background:"rgba(255,59,92,.15)", border:"1px solid rgba(255,59,92,.3)", borderRadius:3, padding:"1px 4px", fontSize:8, color:"var(--danger)", fontFamily:"IBM Plex Mono" }}>short</div>}
                          {isOnSite && availH > 0 && <div style={{ background:"rgba(0,210,160,.12)", border:"1px solid rgba(0,210,160,.2)", borderRadius:3, padding:"1px 4px", fontSize:8, color:"var(--accent2)", fontFamily:"IBM Plex Mono" }}>+{availH}h</div>}
                        </div>
                        {menuOpen && (
                          <CellActionMenu
                            shift={s}
                            locations={locations ?? []}
                            wicCards={wicCards ?? []}
                            onPatch={handlePatch}
                            onClose={() => setActiveMenu(null)}
                          />
                        )}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{empList.length} agents / {dates.length} days</div>
      </div>

      {wicCards && <AvailableSummary dates={dates} empList={empList} wicCards={wicCards} from={from} to={to} />}
      {drillDown && wicCards && <DrillDownModal type={drillDown} cards={wicCards} onClose={() => setDrillDown(null)} />}
    </div>
  )
}
