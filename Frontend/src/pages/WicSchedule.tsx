import { useState, useEffect } from "react"
import { useQuery } from "@tanstack/react-query"
import { NppBadge } from "../components/NppBadge"

const BASE = ""

const TL_COLORS: Record<string,string> = {
  "Delia Panaitescu":"#3b7eff","Ion Ciuceanu":"#00d2a0",
  "Jaroslaw Brzeszkiewicz":"#a78bfa","Karlo Coric":"#f97316",
  "Oliver Schleusen":"#ec4899","Tobias Rossberg":"#facc15",
}

const today = new Date().toISOString().split("T")[0]
const plus13 = new Date(Date.now()+13*24*60*60*1000).toISOString().split("T")[0]

function getMondayOfWeek(date: Date) {
  const d = new Date(date); const day = d.getDay()
  d.setDate(d.getDate() - day + (day===0?-6:1))
  return d.toISOString().split("T")[0]
}
function getDates(from: string, to: string) {
  const dates: string[] = []; let d = new Date(from); const end = new Date(to)
  while (d<=end){dates.push(d.toISOString().split("T")[0]);d.setDate(d.getDate()+1)}
  return dates
}
function fmtDate(d: string) {
  const dt = new Date(d)
  return `${dt.getDate().toString().padStart(2,"0")}.${(dt.getMonth()+1).toString().padStart(2,"0")}`
}

function DayCell({ day }: { day: any }) {
  if (!day) return <td style={{padding:"3px 4px",borderLeft:"1px solid rgb(var(--line-subtle))"}}><div className="text-center text-ink-soft" style={{fontSize:9}}>—</div></td>
  let bgClass="",colorClass="text-ink-soft",text="—"
  const ws = day.workingShift??""
  if (day.isOffDay) {
    if (ws==="PH"||ws==="LPH"){bgClass="bg-holiday-bg";colorClass="text-holiday-fg";text=ws}
    else if (ws==="AL"){bgClass="bg-info-bg";colorClass="text-info-fg";text="AL"}
    else if (ws==="SL"){bgClass="bg-crit-bg";colorClass="text-crit-fg";text="SL"}
    else if (ws.includes("OFF")){bgClass="bg-sunken";colorClass="text-ink-soft";text="OFF"}
    else{text=ws||"—"}
  } else if (day.isOnSite&&day.supportLocation) {
    bgClass="bg-info-bg";colorClass="text-info-fg"
    text=day.supportLocation.length>10?day.supportLocation.slice(0,10)+"…":day.supportLocation
  } else if (!day.isOffDay&&ws) {
    bgClass="bg-good-bg";colorClass="text-good-fg";text=ws
  }
  return (
    <td style={{padding:"3px 4px",borderLeft:"1px solid rgb(var(--line-subtle))"}}>
      <div title={day.isOnSite?`${day.supportLocation}\n${day.wicOpeningHours??""}`:undefined}
        className={`font-mono ${bgClass} ${colorClass} text-center flex items-center justify-center`}
        style={{fontSize:9,padding:"2px 3px",borderRadius:3,minHeight:18}}>
        {text}
      </div>
    </td>
  )
}

export default function WicSchedule() {
  const [tab,setTab]=useState<"14d"|"weekly"|"hours">("14d")
  const [loading,setLoading]=useState(false)
  const [fetchError,setFetchError]=useState<string|null>(null)
  const [agentData,setAgentData]=useState<any[]>([])
  const [openingHours,setOpeningHours]=useState<any[]>([])
  const [from,setFrom]=useState(today)
  const [to,setTo]=useState(plus13)
  const [weekStart,setWeekStart]=useState(getMondayOfWeek(new Date()))
  const [locationFilter,setLocationFilter]=useState("")
  const [teamLeadFilter,setTeamLeadFilter]=useState("")

  const weekEnd=new Date(new Date(weekStart).getTime()+6*24*60*60*1000).toISOString().split("T")[0]

  const { data: wicLocations } = useQuery<any[]>({
    queryKey: ["wic-locations"],
    queryFn: () => fetch(`${BASE}/api/wic/locations`).then(r => r.json()),
    staleTime: 15 * 60 * 1000,
  })
  const nppDisplayNames = new Set<string>(
    (wicLocations ?? []).filter((l: any) => l.isNpp).map((l: any) => l.displayName as string)
  )

  const fetchAgents=async(f:string,t:string)=>{
    setLoading(true);setFetchError(null)
    try{
      const r=await fetch(`${BASE}/api/wicschedule/agents?from=${f}&to=${t}`)
      if(!r.ok){setFetchError(`Could not load data — ${r.status}`);setAgentData([])}
      else setAgentData(await r.json())
    }catch{setFetchError("Could not load data — network error")}
    setLoading(false)
  }
  const fetchHours=async()=>{
    setFetchError(null)
    try{
      const r=await fetch(`${BASE}/api/wicschedule/opening-hours`)
      if(!r.ok){setFetchError(`Could not load data — ${r.status}`);setOpeningHours([])}
      else setOpeningHours(await r.json())
    }catch{setFetchError("Could not load data — network error")}
  }

  useEffect(()=>{
    if(tab==="14d")fetchAgents(from,to)
    if(tab==="weekly")fetchAgents(weekStart,weekEnd)
    if(tab==="hours")fetchHours()
  },[tab])

  const dates=getDates(from,to)
  const weekDates=getDates(weekStart,weekEnd)
  const filteredAgents=agentData.filter((a:any)=>{
    if(teamLeadFilter&&a.teamLeadName!==teamLeadFilter)return false
    if(locationFilter&&!a.assignedLocations.some((l:string)=>l.includes(locationFilter)))return false
    return true
  })
  const byTL:Record<string,any[]>={}
  filteredAgents.forEach((a:any)=>{const tl=a.teamLeadName??"Unknown";if(!byTL[tl])byTL[tl]=[];byTL[tl].push(a)})
  const allLocations=[...new Set(agentData.flatMap((a:any)=>a.assignedLocations))]
  const allTLs=[...new Set(agentData.map((a:any)=>a.teamLeadName).filter(Boolean))]
  const thStyle:any={padding:"8px 6px",fontSize:9,fontWeight:500,textTransform:"uppercase",letterSpacing:".06em",textAlign:"left" as const}
  const thCls="text-ink-soft border-b border-line-subtle bg-sunken"
  const tdStyle:any={padding:"7px 10px",borderBottom:"1px solid rgb(var(--line-subtle))",fontSize:12}
  const todayDow=new Date().getDay()===0?7:new Date().getDay()
  const openToday=openingHours.filter((l:any)=>l.weeklyHours?.some((h:any)=>h.dayOfWeek===todayDow&&!h.isClosed)).length
  const totalAgents=openingHours.reduce((s:number,l:any)=>s+(l.assignedAgentCount??0),0)
  const iStyle:any={padding:"6px 10px",borderRadius:6,fontSize:12,outline:"none"}
  const iCls="bg-sunken border border-line-subtle text-ink"

  return (
    <div style={{display:"flex",flexDirection:"column",gap:16}}>
      <h1 className="text-ink" style={{fontSize:22,fontWeight:600}}>WIC Schedule</h1>
      <div style={{display:"flex",gap:4}}>
        {[["14d","14-Day View"],["weekly","Weekly Report"],["hours","Location Hours"]].map(([v,label])=>(
          <button key={v} onClick={()=>setTab(v as any)} className={tab===v?"bg-info-solid border border-info-bd text-white":"bg-raised border border-line-subtle text-ink-muted"} style={{borderRadius:6,padding:"6px 16px",fontSize:12,cursor:"pointer",fontWeight:tab===v?600:400}}>{label}</button>
        ))}
      </div>
      {fetchError&&<div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{borderRadius:6,padding:"10px 14px",fontSize:13}}>{fetchError}</div>}

      {tab==="14d"&&(<>
        <div style={{display:"flex",gap:8,alignItems:"center",flexWrap:"wrap"}}>
          <input type="date" value={from} onChange={e=>setFrom(e.target.value)} style={iStyle} className={iCls}/>
          <input type="date" value={to} onChange={e=>setTo(e.target.value)} style={iStyle} className={iCls}/>
          <button onClick={()=>fetchAgents(from,to)} className="bg-info-solid text-white" style={{border:"none",padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>Load</button>
          <select value={teamLeadFilter} onChange={e=>setTeamLeadFilter(e.target.value)} style={iStyle} className={iCls}>
            <option value="">All Team Leads</option>
            {allTLs.map((tl:any)=><option key={tl} value={tl}>{tl}</option>)}
          </select>
          <select value={locationFilter} onChange={e=>setLocationFilter(e.target.value)} style={iStyle} className={iCls}>
            <option value="">All Locations</option>
            {allLocations.map((l:any)=><option key={l} value={l}>{l}</option>)}
          </select>
        </div>
        {loading&&<div className="text-ink-soft" style={{padding:40,textAlign:"center"}}>Loading...</div>}
        {!loading&&(
          <div className="bg-raised border border-line-subtle" style={{borderRadius:8,overflow:"hidden"}}>
            <div style={{overflowX:"auto"}}>
              <table style={{borderCollapse:"collapse",fontSize:11}}>
                <thead><tr className="bg-sunken">
                  <th className={thCls} style={{...thStyle,minWidth:180,position:"sticky",left:0,zIndex:2}}>Agent</th>
                  {dates.map(d=>{const dt=new Date(d);const isWE=dt.getDay()===0||dt.getDay()===6;const isTod=d===today;const dow=["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                    return <th key={d} className={`border-b border-line-subtle ${isTod?"text-info-fg bg-info-bg":isWE?"text-ink-soft bg-sunken":"text-ink-soft bg-sunken"}`} style={{...thStyle,width:52,minWidth:52,textAlign:"center",padding:"5px 2px"}}>
                      <div style={{fontSize:8}}>{dow}</div><div className="font-mono" style={{fontSize:8}}>{fmtDate(d)}</div>
                    </th>})}
                </tr></thead>
                <tbody>
                  {Object.entries(byTL).map(([tl,agents])=>(<>
                    <tr key={`tl-${tl}`}><td colSpan={1+dates.length} className={`border-t border-line-subtle${TL_COLORS[tl]?"":" text-ink-muted"}`} style={{padding:"5px 12px",fontSize:10,fontWeight:600,textTransform:"uppercase",letterSpacing:".07em",color:TL_COLORS[tl]??undefined,background:"rgb(var(--surface-sunken))"}}>{tl} ({(agents as any[]).length})</td></tr>
                    {(agents as any[]).map((agent:any)=>(
                      <tr key={agent.employeeId} className="hover:bg-hovered">
                        <td className="bg-raised" style={{padding:"5px 10px",position:"sticky",left:0,zIndex:1,borderRight:"1px solid rgb(var(--line-subtle))",minWidth:180}}>
                          <div className="text-ink" style={{fontWeight:500,fontSize:11}}>{agent.fullName}</div>
                          <div style={{display:"flex",gap:3,flexWrap:"wrap",marginTop:2}}>
                            {(agent.assignedLocations as string[]).slice(0,2).map((l:string)=>{
                              const stripped=l.replace("DE_","")
                              const isNpp=nppDisplayNames.has(stripped)
                              return <span key={l} style={{display:"inline-flex",alignItems:"center",gap:2}}>
                                <span style={{fontSize:8,background:"rgb(var(--st-info-bg))",color:"rgb(var(--st-info-solid))",border:"1px solid rgb(var(--st-info-bd))",borderRadius:3,padding:"1px 4px"}}>{stripped}</span>
                                {isNpp&&<NppBadge/>}
                              </span>
                            })}
                            {agent.assignedLocations.length>2&&<span className="text-ink-soft" style={{fontSize:8}}>+{agent.assignedLocations.length-2}</span>}
                          </div>
                        </td>
                        {dates.map(d=>{const day=agent.days?.find((x:any)=>x.date===d);return <DayCell key={d} day={day}/>})}
                      </tr>
                    ))}
                  </>))}
                  {filteredAgents.length===0&&<tr><td colSpan={1+dates.length} className="text-ink-soft" style={{padding:40,textAlign:"center"}}>No WIC agents found.</td></tr>}
                </tbody>
              </table>
            </div>
            <div className="font-mono border-t border-line-subtle text-ink-soft" style={{padding:"8px 12px",fontSize:11}}>{filteredAgents.length} agents · {dates.length} days</div>
          </div>
        )}
      </>)}

      {tab==="weekly"&&(<>
        <div style={{display:"flex",gap:8,alignItems:"center"}}>
          <input type="week" onChange={e=>{if(!e.target.value)return;const[y,w]=e.target.value.split("-W");const jan4=new Date(Number(y),0,4);const sow=new Date(jan4.getTime()+(Number(w)-1)*7*24*60*60*1000);sow.setDate(sow.getDate()-sow.getDay()+1);setWeekStart(sow.toISOString().split("T")[0])}} style={iStyle} className={iCls}/>
          <button onClick={()=>fetchAgents(weekStart,weekEnd)} className="bg-info-solid text-white" style={{border:"none",padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>Load</button>
          <button onClick={()=>window.open(`${BASE}/api/wicschedule/export/agents/csv?from=${weekStart}&to=${weekEnd}`)} className="bg-good-bg border border-good-bd text-good-fg" style={{padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>⬇ Download CSV</button>
        </div>
        {loading&&<div className="text-ink-soft" style={{padding:40,textAlign:"center"}}>Loading...</div>}
        {!loading&&(
          <div className="bg-raised border border-line-subtle" style={{borderRadius:8,overflow:"hidden"}}>
            <div style={{overflowX:"auto"}}>
              <table style={{width:"100%",borderCollapse:"collapse",fontSize:12}}>
                <thead><tr className="bg-sunken">
                  <th className={thCls} style={{...thStyle,minWidth:160}}>Name</th>
                  {weekDates.map(d=>{const dt=new Date(d);const dow=["So","Mo","Di","Mi","Do","Fr","Sa"][dt.getDay()];const isWE=dt.getDay()===0||dt.getDay()===6
                    return <th key={d} className={isWE?"text-ink-soft border-b border-line-subtle bg-sunken":thCls} style={{...thStyle,minWidth:80,textAlign:"center"}}>{dow}<br/><span className="font-mono" style={{fontSize:9}}>{fmtDate(d)}</span></th>})}
                </tr></thead>
                <tbody>
                  {agentData.map((agent:any)=>(
                    <tr key={agent.employeeId} className="hover:bg-hovered">
                      <td style={{...tdStyle,fontWeight:500}}>{agent.fullName}</td>
                      {weekDates.map(d=>{const day=agent.days?.find((x:any)=>x.date===d);const isWE=new Date(d).getDay()===0||new Date(d).getDay()===6
                        if(!day)return <td key={d} className="text-ink-soft" style={{...tdStyle,background:isWE?"rgb(var(--surface-sunken))":"transparent",textAlign:"center",fontSize:11}}>—</td>
                        return <td key={d} style={{...tdStyle,background:isWE?"rgb(var(--surface-sunken))":"transparent",minWidth:80}}>
                          {day.isOffDay?<span className={day.workingShift==="AL"?"text-info-fg":day.workingShift==="SL"?"text-crit-fg":"text-ink-soft"} style={{fontSize:10,fontWeight:600}}>{day.workingShift||"OFF"}</span>
                          :day.isOnSite&&day.supportLocation?<div style={{fontSize:9}}><div style={{fontWeight:600,color:"rgb(var(--st-info-solid))"}}>{day.supportLocation}</div>{day.wicOpeningHours&&<div style={{color:"rgb(var(--text-secondary))"}}>{day.wicOpeningHours}</div>}{day.workingShift&&<div style={{color:"rgb(var(--st-good-solid))"}}>{day.workingShift}</div>}</div>
                          :day.workingShift?<div className="text-good-fg" style={{fontSize:9}}>{day.workingShift}</div>
                          :<span className="text-ink-soft" style={{fontSize:10}}>—</span>}
                        </td>})}
                    </tr>
                  ))}
                  {agentData.length===0&&<tr><td colSpan={8} className="text-ink-soft" style={{...tdStyle,textAlign:"center",padding:40}}>Keine Daten.</td></tr>}
                </tbody>
              </table>
            </div>
            <div className="border-t border-line-subtle text-ink-soft" style={{padding:"8px 12px",fontSize:10,fontStyle:"italic"}}>Nur WIC-Agenten mit aktiven Einsätzen werden angezeigt</div>
          </div>
        )}
      </>)}

      {tab==="hours"&&(<>
        <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12}}>
          {[{label:"Total WIC Locations",value:openingHours.length,cls:"text-ink"},{label:"Open Today",value:openToday,cls:"text-good-fg"},{label:"Total Assigned Agents",value:totalAgents,cls:"text-info-fg"}].map(c=>(
            <div key={c.label} className="bg-raised border border-line-subtle" style={{borderRadius:8,padding:"14px 18px"}}>
              <div className="text-ink-soft" style={{fontSize:10,textTransform:"uppercase",letterSpacing:".08em",marginBottom:6}}>{c.label}</div>
              <div className={`font-mono ${c.cls}`} style={{fontSize:26,fontWeight:600}}>{c.value}</div>
            </div>
          ))}
        </div>
        <div className="bg-raised border border-line-subtle" style={{borderRadius:8,overflow:"hidden"}}>
          <div style={{overflowX:"auto"}}>
            <table style={{width:"100%",borderCollapse:"collapse",fontSize:12}}>
              <thead><tr className="bg-sunken">
                {["Standort","Stadt","Agenten","Mo","Di","Mi","Do","Fr","Sa","So"].map(h=><th key={h} className={thCls} style={thStyle}>{h}</th>)}
              </tr></thead>
              <tbody>
                {openingHours.map((loc:any)=>{
                  const hbd:Record<number,any>={}
                  loc.weeklyHours?.forEach((h:any)=>{hbd[h.dayOfWeek]=h})
                  return (
                    <tr key={loc.locationCode} className="hover:bg-hovered">
                      <td style={{...tdStyle,fontWeight:600}}>
                        <span style={{display:"flex",alignItems:"center",gap:5}}>
                          {loc.displayName}
                          {nppDisplayNames.has(loc.displayName)&&<NppBadge/>}
                        </span>
                      </td>
                      <td className="text-ink-muted" style={{...tdStyle,fontSize:11}}>{loc.city}</td>
                      <td style={tdStyle}><span style={{background:"rgb(var(--st-info-bg))",border:"1px solid rgb(var(--st-info-bd))",color:"rgb(var(--st-info-solid))",borderRadius:4,fontSize:10,padding:"2px 6px",fontWeight:600}}>{loc.assignedAgentCount}</span></td>
                      {[1,2,3,4,5,6,7].map(dow=>{const h=hbd[dow]
                        if(!h)return <td key={dow} className="text-ink-soft" style={{...tdStyle,textAlign:"center",fontSize:10}}>—</td>
                        if(h.isClosed)return <td key={dow} className="text-ink-soft" style={{...tdStyle,textAlign:"center",fontSize:9}}>—</td>
                        return <td key={dow} style={{...tdStyle,padding:"4px 6px"}}>
                          <div style={{background:"rgb(var(--st-good-bg))",border:"1px solid rgb(var(--st-good-bd))",borderRadius:4,padding:"2px 4px",fontSize:9,color:"rgb(var(--st-good-solid))",fontWeight:600,textAlign:"center"}}>
                            {h.openTime}–{h.closeTime}{h.openTime2&&<><br/>{h.openTime2}–{h.closeTime2}</>}
                          </div>
                        </td>})}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="font-mono border-t border-line-subtle text-ink-soft" style={{padding:"8px 12px",fontSize:11}}>{openingHours.length} locations</div>
        </div>
      </>)}
    </div>
  )
}
