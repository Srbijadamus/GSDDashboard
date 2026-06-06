import { useState, useEffect } from "react"

const BASE = "https://n8jlr9dr-5000.euw.devtunnels.ms"

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
  if (!day) return <td style={{padding:"3px 4px",borderLeft:"1px solid rgba(30,45,69,.3)"}}><div style={{textAlign:"center",fontSize:9,color:"var(--text3)"}}>—</div></td>
  let bg="transparent",color="var(--text3)",text="—"
  const ws = day.workingShift??""
  if (day.isOffDay) {
    if (ws==="PH"||ws==="LPH"){bg="rgba(250,204,21,.15)";color="#facc15";text=ws}
    else if (ws==="AL"){bg="rgba(59,126,255,.12)";color="var(--accent)";text="AL"}
    else if (ws==="SL"){bg="rgba(255,59,92,.12)";color="var(--danger)";text="SL"}
    else if (ws.includes("OFF")){bg="rgba(30,45,69,.3)";color="var(--text3)";text="OFF"}
    else{text=ws||"—"}
  } else if (day.isOnSite&&day.supportLocation) {
    bg="rgba(96,165,250,.15)";color="#60a5fa"
    text=day.supportLocation.length>10?day.supportLocation.slice(0,10)+"…":day.supportLocation
  } else if (!day.isOffDay&&ws) {
    bg="rgba(34,197,94,.08)";color="var(--green)";text=ws
  }
  return (
    <td style={{padding:"3px 4px",borderLeft:"1px solid rgba(30,45,69,.3)"}}>
      <div title={day.isOnSite?`${day.supportLocation}\n${day.wicOpeningHours??""}`:undefined}
        style={{background:bg,color,fontSize:9,fontFamily:"IBM Plex Mono",padding:"2px 3px",borderRadius:3,textAlign:"center",minHeight:18,display:"flex",alignItems:"center",justifyContent:"center"}}>
        {text}
      </div>
    </td>
  )
}

export default function WicSchedule() {
  const [tab,setTab]=useState<"14d"|"weekly"|"hours">("14d")
  const [loading,setLoading]=useState(false)
  const [agentData,setAgentData]=useState<any[]>([])
  const [openingHours,setOpeningHours]=useState<any[]>([])
  const [from,setFrom]=useState(today)
  const [to,setTo]=useState(plus13)
  const [weekStart,setWeekStart]=useState(getMondayOfWeek(new Date()))
  const [locationFilter,setLocationFilter]=useState("")
  const [teamLeadFilter,setTeamLeadFilter]=useState("")

  const weekEnd=new Date(new Date(weekStart).getTime()+6*24*60*60*1000).toISOString().split("T")[0]

  const fetchAgents=async(f:string,t:string)=>{
    setLoading(true)
    try{const r=await fetch(`${BASE}/api/wicschedule/agents?from=${f}&to=${t}`);setAgentData(await r.json())}catch{}
    setLoading(false)
  }
  const fetchHours=async()=>{
    try{const r=await fetch(`${BASE}/api/wicschedule/opening-hours`);setOpeningHours(await r.json())}catch{}
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
  const thStyle:any={padding:"8px 6px",fontSize:9,fontWeight:500,textTransform:"uppercase",letterSpacing:".06em",color:"var(--text3)",borderBottom:"1px solid var(--border)",background:"var(--card2)",textAlign:"left" as const}
  const tdStyle:any={padding:"7px 10px",borderBottom:"1px solid rgba(30,45,69,.5)",fontSize:12}
  const todayDow=new Date().getDay()===0?7:new Date().getDay()
  const openToday=openingHours.filter((l:any)=>l.weeklyHours?.some((h:any)=>h.dayOfWeek===todayDow&&!h.isClosed)).length
  const totalAgents=openingHours.reduce((s:number,l:any)=>s+(l.assignedAgentCount??0),0)
  const iStyle:any={background:"var(--card2)",border:"1px solid var(--border)",color:"var(--text)",padding:"6px 10px",borderRadius:6,fontSize:12,outline:"none"}

  return (
    <div style={{display:"flex",flexDirection:"column",gap:16}}>
      <h1 style={{fontSize:22,fontWeight:600,color:"var(--text)"}}>WIC Schedule</h1>
      <div style={{display:"flex",gap:4}}>
        {[["14d","14-Day View"],["weekly","Weekly Report"],["hours","Location Hours"]].map(([v,label])=>(
          <button key={v} onClick={()=>setTab(v as any)} style={{background:tab===v?"var(--accent)":"var(--card)",border:`1px solid ${tab===v?"var(--accent)":"var(--border)"}`,color:tab===v?"#fff":"var(--text2)",borderRadius:6,padding:"6px 16px",fontSize:12,cursor:"pointer",fontWeight:tab===v?600:400}}>{label}</button>
        ))}
      </div>

      {tab==="14d"&&(<>
        <div style={{display:"flex",gap:8,alignItems:"center",flexWrap:"wrap"}}>
          <input type="date" value={from} onChange={e=>setFrom(e.target.value)} style={iStyle}/>
          <input type="date" value={to} onChange={e=>setTo(e.target.value)} style={iStyle}/>
          <button onClick={()=>fetchAgents(from,to)} style={{background:"var(--accent)",border:"none",color:"#fff",padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>Load</button>
          <select value={teamLeadFilter} onChange={e=>setTeamLeadFilter(e.target.value)} style={iStyle}>
            <option value="">All Team Leads</option>
            {allTLs.map((tl:any)=><option key={tl} value={tl}>{tl}</option>)}
          </select>
          <select value={locationFilter} onChange={e=>setLocationFilter(e.target.value)} style={iStyle}>
            <option value="">All Locations</option>
            {allLocations.map((l:any)=><option key={l} value={l}>{l}</option>)}
          </select>
        </div>
        {loading&&<div style={{padding:40,textAlign:"center",color:"var(--text3)"}}>Loading...</div>}
        {!loading&&(
          <div style={{background:"var(--card)",border:"1px solid var(--border)",borderRadius:8,overflow:"hidden"}}>
            <div style={{overflowX:"auto"}}>
              <table style={{borderCollapse:"collapse",fontSize:11}}>
                <thead><tr style={{background:"var(--card2)"}}>
                  <th style={{...thStyle,minWidth:180,position:"sticky",left:0,zIndex:2,background:"var(--card2)"}}>Agent</th>
                  {dates.map(d=>{const dt=new Date(d);const isWE=dt.getDay()===0||dt.getDay()===6;const isTod=d===today;const dow=["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                    return <th key={d} style={{...thStyle,width:52,minWidth:52,textAlign:"center",background:isTod?"rgba(59,126,255,.12)":isWE?"rgba(30,45,69,.3)":"var(--card2)",color:isTod?"var(--accent)":"var(--text3)",padding:"5px 2px"}}>
                      <div style={{fontSize:8}}>{dow}</div><div style={{fontFamily:"IBM Plex Mono",fontSize:8}}>{fmtDate(d)}</div>
                    </th>})}
                </tr></thead>
                <tbody>
                  {Object.entries(byTL).map(([tl,agents])=>(<>
                    <tr key={`tl-${tl}`}><td colSpan={1+dates.length} style={{padding:"5px 12px",fontSize:10,fontWeight:600,textTransform:"uppercase",letterSpacing:".07em",color:TL_COLORS[tl]??"var(--text2)",background:"rgba(30,45,69,.4)",borderTop:"1px solid var(--border)"}}>{tl} ({(agents as any[]).length})</td></tr>
                    {(agents as any[]).map((agent:any)=>(
                      <tr key={agent.employeeId} onMouseEnter={e=>e.currentTarget.style.background="var(--card2)"} onMouseLeave={e=>e.currentTarget.style.background="transparent"}>
                        <td style={{padding:"5px 10px",position:"sticky",left:0,background:"var(--card)",zIndex:1,borderRight:"1px solid rgba(30,45,69,.4)",minWidth:180}}>
                          <div style={{fontWeight:500,fontSize:11,color:"var(--text)"}}>{agent.fullName}</div>
                          <div style={{display:"flex",gap:3,flexWrap:"wrap",marginTop:2}}>
                            {(agent.assignedLocations as string[]).slice(0,2).map((l:string)=>(
                              <span key={l} style={{fontSize:8,background:"rgba(96,165,250,.12)",color:"#60a5fa",border:"1px solid rgba(96,165,250,.2)",borderRadius:3,padding:"1px 4px"}}>{l.replace("DE_","")}</span>
                            ))}
                            {agent.assignedLocations.length>2&&<span style={{fontSize:8,color:"var(--text3)"}}>+{agent.assignedLocations.length-2}</span>}
                          </div>
                        </td>
                        {dates.map(d=>{const day=agent.days?.find((x:any)=>x.date===d);return <DayCell key={d} day={day}/>})}
                      </tr>
                    ))}
                  </>))}
                  {filteredAgents.length===0&&<tr><td colSpan={1+dates.length} style={{padding:40,textAlign:"center",color:"var(--text3)"}}>No WIC agents found.</td></tr>}
                </tbody>
              </table>
            </div>
            <div style={{padding:"8px 12px",borderTop:"1px solid var(--border)",fontSize:11,color:"var(--text3)",fontFamily:"IBM Plex Mono"}}>{filteredAgents.length} agents · {dates.length} days</div>
          </div>
        )}
      </>)}

      {tab==="weekly"&&(<>
        <div style={{display:"flex",gap:8,alignItems:"center"}}>
          <input type="week" onChange={e=>{if(!e.target.value)return;const[y,w]=e.target.value.split("-W");const jan4=new Date(Number(y),0,4);const sow=new Date(jan4.getTime()+(Number(w)-1)*7*24*60*60*1000);sow.setDate(sow.getDate()-sow.getDay()+1);setWeekStart(sow.toISOString().split("T")[0])}} style={iStyle}/>
          <button onClick={()=>fetchAgents(weekStart,weekEnd)} style={{background:"var(--accent)",border:"none",color:"#fff",padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>Load</button>
          <button onClick={()=>window.open(`${BASE}/api/wicschedule/export/agents/csv?from=${weekStart}&to=${weekEnd}`)} style={{background:"rgba(34,197,94,.15)",border:"1px solid #22c55e",color:"#22c55e",padding:"6px 14px",borderRadius:6,fontSize:12,cursor:"pointer",fontWeight:600}}>⬇ Download CSV</button>
        </div>
        {loading&&<div style={{padding:40,textAlign:"center",color:"var(--text3)"}}>Loading...</div>}
        {!loading&&(
          <div style={{background:"var(--card)",border:"1px solid var(--border)",borderRadius:8,overflow:"hidden"}}>
            <div style={{overflowX:"auto"}}>
              <table style={{width:"100%",borderCollapse:"collapse",fontSize:12}}>
                <thead><tr style={{background:"var(--card2)"}}>
                  <th style={{...thStyle,minWidth:160}}>Name</th>
                  {weekDates.map(d=>{const dt=new Date(d);const dow=["So","Mo","Di","Mi","Do","Fr","Sa"][dt.getDay()];const isWE=dt.getDay()===0||dt.getDay()===6
                    return <th key={d} style={{...thStyle,minWidth:80,textAlign:"center",background:isWE?"rgba(30,45,69,.3)":"var(--card2)"}}>{dow}<br/><span style={{fontFamily:"IBM Plex Mono",fontSize:9}}>{fmtDate(d)}</span></th>})}
                </tr></thead>
                <tbody>
                  {agentData.map((agent:any)=>(
                    <tr key={agent.employeeId} onMouseEnter={e=>e.currentTarget.style.background="var(--card2)"} onMouseLeave={e=>e.currentTarget.style.background="transparent"}>
                      <td style={{...tdStyle,fontWeight:500}}>{agent.fullName}</td>
                      {weekDates.map(d=>{const day=agent.days?.find((x:any)=>x.date===d);const isWE=new Date(d).getDay()===0||new Date(d).getDay()===6
                        if(!day)return <td key={d} style={{...tdStyle,background:isWE?"rgba(30,45,69,.2)":"transparent",textAlign:"center",color:"var(--text3)",fontSize:11}}>—</td>
                        return <td key={d} style={{...tdStyle,background:isWE?"rgba(30,45,69,.2)":"transparent",minWidth:80}}>
                          {day.isOffDay?<span style={{fontSize:10,fontWeight:600,color:day.workingShift==="AL"?"var(--accent)":day.workingShift==="SL"?"var(--danger)":"var(--text3)"}}>{day.workingShift||"OFF"}</span>
                          :day.isOnSite&&day.supportLocation?<div style={{fontSize:9}}><div style={{fontWeight:600,color:"#60a5fa"}}>{day.supportLocation}</div>{day.wicOpeningHours&&<div style={{color:"#8892a4"}}>{day.wicOpeningHours}</div>}{day.workingShift&&<div style={{color:"#22c55e"}}>{day.workingShift}</div>}</div>
                          :day.workingShift?<div style={{fontSize:9,color:"var(--green)"}}>{day.workingShift}</div>
                          :<span style={{color:"var(--text3)",fontSize:10}}>—</span>}
                        </td>})}
                    </tr>
                  ))}
                  {agentData.length===0&&<tr><td colSpan={8} style={{...tdStyle,textAlign:"center",color:"var(--text3)",padding:40}}>Keine Daten.</td></tr>}
                </tbody>
              </table>
            </div>
            <div style={{padding:"8px 12px",borderTop:"1px solid var(--border)",fontSize:10,color:"var(--text3)",fontStyle:"italic"}}>Nur WIC-Agenten mit aktiven Einsätzen werden angezeigt</div>
          </div>
        )}
      </>)}

      {tab==="hours"&&(<>
        <div style={{display:"grid",gridTemplateColumns:"repeat(3,1fr)",gap:12}}>
          {[{label:"Total WIC Locations",value:openingHours.length,color:"var(--text)"},{label:"Open Today",value:openToday,color:"#22c55e"},{label:"Total Assigned Agents",value:totalAgents,color:"#60a5fa"}].map(c=>(
            <div key={c.label} style={{background:"var(--card)",border:"1px solid var(--border)",borderRadius:8,padding:"14px 18px"}}>
              <div style={{fontSize:10,textTransform:"uppercase",letterSpacing:".08em",color:"var(--text3)",marginBottom:6}}>{c.label}</div>
              <div style={{fontSize:26,fontWeight:600,fontFamily:"IBM Plex Mono",color:c.color}}>{c.value}</div>
            </div>
          ))}
        </div>
        <div style={{background:"var(--card)",border:"1px solid var(--border)",borderRadius:8,overflow:"hidden"}}>
          <div style={{overflowX:"auto"}}>
            <table style={{width:"100%",borderCollapse:"collapse",fontSize:12}}>
              <thead><tr style={{background:"var(--card2)"}}>
                {["Standort","Stadt","Agenten","Mo","Di","Mi","Do","Fr","Sa","So"].map(h=><th key={h} style={thStyle}>{h}</th>)}
              </tr></thead>
              <tbody>
                {openingHours.map((loc:any)=>{
                  const hbd:Record<number,any>={}
                  loc.weeklyHours?.forEach((h:any)=>{hbd[h.dayOfWeek]=h})
                  return (
                    <tr key={loc.locationCode} onMouseEnter={e=>e.currentTarget.style.background="var(--card2)"} onMouseLeave={e=>e.currentTarget.style.background="transparent"}>
                      <td style={{...tdStyle,fontWeight:600}}>{loc.displayName}</td>
                      <td style={{...tdStyle,color:"var(--text2)",fontSize:11}}>{loc.city}</td>
                      <td style={tdStyle}><span style={{background:"rgba(96,165,250,.12)",border:"1px solid rgba(96,165,250,.2)",color:"#60a5fa",borderRadius:4,fontSize:10,padding:"2px 6px",fontWeight:600}}>{loc.assignedAgentCount}</span></td>
                      {[1,2,3,4,5,6,7].map(dow=>{const h=hbd[dow]
                        if(!h)return <td key={dow} style={{...tdStyle,textAlign:"center",color:"var(--text3)",fontSize:10}}>—</td>
                        if(h.isClosed)return <td key={dow} style={{...tdStyle,textAlign:"center",color:"var(--text3)",fontSize:9}}>—</td>
                        return <td key={dow} style={{...tdStyle,padding:"4px 6px"}}>
                          <div style={{background:"rgba(34,197,94,.08)",border:"1px solid rgba(34,197,94,.15)",borderRadius:4,padding:"2px 4px",fontSize:9,color:"#22c55e",fontWeight:600,textAlign:"center"}}>
                            {h.openTime}–{h.closeTime}{h.openTime2&&<><br/>{h.openTime2}–{h.closeTime2}</>}
                          </div>
                        </td>})}
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div style={{padding:"8px 12px",borderTop:"1px solid var(--border)",fontSize:11,color:"var(--text3)",fontFamily:"IBM Plex Mono"}}>{openingHours.length} locations</div>
        </div>
      </>)}
    </div>
  )
}
