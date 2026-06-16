import { useState, useEffect } from "react"

const BASE = ""

const STATUS_COLORS: Record<string,any> = {
  PLANNED:   { bg:"rgba(136,146,164,0.15)", border:"#8892a4", color:"#8892a4" },
  CONFIRMED: { bg:"rgba(34,197,94,0.15)",  border:"#22c55e", color:"#22c55e" },
  CANCELLED: { bg:"rgba(239,68,68,0.15)",  border:"#ef4444", color:"#ef4444" },
}
const HANDLING_COLORS: Record<string,any> = {
  ADDITIONAL: { bg:"rgba(96,165,250,0.15)",  border:"#60a5fa", color:"#60a5fa" },
  LOCAL:      { bg:"rgba(192,132,252,0.15)", border:"#c084fc", color:"#c084fc" },
}

const inputStyle: any = {
  background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text)",
  padding:"7px 10px", borderRadius:6, fontSize:12, width:"100%", fontFamily:"IBM Plex Sans", outline:"none"
}
const labelStyle: any = { fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }

function Badge({ text, style }: { text: string; style: any }) {
  return <span style={{ ...style, padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600, border:`1px solid ${style.border}` }}>{text}</span>
}

function EventModal({ event, locations, onClose, onSave }: any) {
  const [form, setForm] = useState({
    title: event?.title ?? "",
    locationCode: event?.locationCode ?? "",
    pipelineDate: event?.pipelineDate ?? new Date().toISOString().split("T")[0],
    pipelineDateEnd: event?.pipelineDateEnd ?? "",
    startTime: event?.startTime ?? "",
    endTime: event?.endTime ?? "",
    handlingType: event?.handlingType ?? "ADDITIONAL",
    agentsRequired: event?.agentsRequired ?? 1,
    primaryAgent: event?.primaryAgent ?? "",
    backupAgent: event?.backupAgent ?? "",
    additionalAgentsNeeded: event?.additionalAgentsNeeded ?? 0,
    handledBy: event?.handledBy ?? "",
    description: event?.description ?? "",
    status: event?.status ?? "PLANNED",
  })

  const h = (field: string, val: any) => setForm(f => ({ ...f, [field]: val }))

  const handleSave = async () => {
    const body: any = {
      locationCode: form.locationCode || null,
      pipelineDate: form.pipelineDate,
      pipelineDateEnd: form.pipelineDateEnd || null,
      title: form.title,
      description: form.description || null,
      primaryAgent: form.primaryAgent || null,
      backupAgent: form.backupAgent || null,
      additionalAgentsNeeded: Number(form.additionalAgentsNeeded),
      handledBy: form.handledBy || null,
      startTime: form.startTime || null,
      endTime: form.endTime || null,
      agentsRequired: Number(form.agentsRequired),
    }
    if (event) {
      body.status = form.status
      await fetch(`${BASE}/api/pipeline/${event.id}`, { method:"PATCH", headers:{"Content-Type":"application/json"}, body: JSON.stringify(body) })
    } else {
      await fetch(`${BASE}/api/pipeline`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify(body) })
    }
    onSave()
  }

  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000, display:"flex", alignItems:"center", justifyContent:"center" }}
      onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:10, padding:24, width:500, maxHeight:"90vh", overflowY:"auto" }}>
        <h2 style={{ fontSize:16, fontWeight:600, color:"var(--text)", marginBottom:20 }}>{event ? "Edit Pipeline Event" : "New Pipeline Event"}</h2>
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div><label style={labelStyle}>Title</label><input value={form.title} onChange={e => h("title", e.target.value)} style={inputStyle} placeholder="Event title..." /></div>
          <div><label style={labelStyle}>Location</label>
            <select value={form.locationCode} onChange={e => h("locationCode", e.target.value)} style={inputStyle}>
              <option value="">-- No specific location --</option>
              {locations.map((l: any) => <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>)}
            </select>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label style={labelStyle}>Date From</label><input type="date" value={form.pipelineDate} onChange={e => h("pipelineDate", e.target.value)} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label style={labelStyle}>Date To</label><input type="date" value={form.pipelineDateEnd} onChange={e => h("pipelineDateEnd", e.target.value)} style={inputStyle} /></div>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label style={labelStyle}>Start Time</label><input type="time" value={form.startTime} onChange={e => h("startTime", e.target.value)} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label style={labelStyle}>End Time</label><input type="time" value={form.endTime} onChange={e => h("endTime", e.target.value)} style={inputStyle} /></div>
          </div>
          <div>
            <label style={labelStyle}>Handling Type</label>
            <div style={{ display:"flex", gap:8 }}>
              {["ADDITIONAL","LOCAL"].map(t => (
                <button key={t} onClick={() => h("handlingType", t)} style={{ flex:1, padding:"7px 0", borderRadius:6, border:`1px solid ${form.handlingType===t ? HANDLING_COLORS[t].border : "var(--border)"}`, background: form.handlingType===t ? HANDLING_COLORS[t].bg : "transparent", color: form.handlingType===t ? HANDLING_COLORS[t].color : "var(--text3)", cursor:"pointer", fontSize:12, fontWeight:600 }}>{t}</button>
              ))}
            </div>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label style={labelStyle}>Agents Required</label><input type="number" min="1" value={form.agentsRequired} onChange={e => h("agentsRequired", e.target.value)} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label style={labelStyle}>Additional Needed</label><input type="number" min="0" value={form.additionalAgentsNeeded} onChange={e => h("additionalAgentsNeeded", e.target.value)} style={inputStyle} /></div>
          </div>
          <div><label style={labelStyle}>Primary Agent</label><input value={form.primaryAgent} onChange={e => h("primaryAgent", e.target.value)} style={inputStyle} placeholder="Primary agent name..." /></div>
          <div><label style={labelStyle}>Backup Agent</label><input value={form.backupAgent} onChange={e => h("backupAgent", e.target.value)} style={inputStyle} placeholder="Backup agent name..." /></div>
          <div><label style={labelStyle}>Handled By</label><input value={form.handledBy} onChange={e => h("handledBy", e.target.value)} style={inputStyle} placeholder="Team lead or handler..." /></div>
          {event && (
            <div><label style={labelStyle}>Status</label>
              <select value={form.status} onChange={e => h("status", e.target.value)} style={inputStyle}>
                {["PLANNED","CONFIRMED","CANCELLED"].map(s => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
          )}
          <div><label style={labelStyle}>Notes</label><textarea value={form.description} onChange={(e: any) => h("description", e.target.value)} rows={3} style={{ ...inputStyle, resize:"vertical" }} placeholder="Notes..." /></div>
        </div>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:20 }}>
          <button onClick={onClose} style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          <button onClick={handleSave} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>{event ? "Save Changes" : "Create Event"}</button>
        </div>
      </div>
    </div>
  )
}

export default function Pipeline() {
  const [events, setEvents] = useState<any[]>([])
  const [locations, setLocations] = useState<any[]>([])
  const [loading, setLoading] = useState(false)
  const [view, setView] = useState<"list"|"timeline">("list")
  const [showModal, setShowModal] = useState(false)
  const [editEvent, setEditEvent] = useState<any>(null)
  const [selectedEvent, setSelectedEvent] = useState<any>(null)

  const today = new Date()
  const fromDate = today.toISOString().split("T")[0]
  const toDate = new Date(today.getTime() + 30*24*60*60*1000).toISOString().split("T")[0]

  const fetchEvents = async () => {
    setLoading(true)
    try {
      const r = await fetch(`${BASE}/api/pipeline?from=${fromDate}&to=${toDate}`)
      setEvents(await r.json())
    } catch {}
    setLoading(false)
  }

  const fetchLocations = async () => {
    try {
      const r = await fetch(`${BASE}/api/wic/locations`)
      setLocations(await r.json())
    } catch {}
  }

  useEffect(() => { fetchEvents(); fetchLocations() }, [])

  const handleDelete = async (id: number) => {
    if (!confirm("Delete this pipeline event?")) return
    await fetch(`${BASE}/api/pipeline/${id}`, { method:"DELETE" })
    fetchEvents()
  }

  const handleSave = () => {
    setShowModal(false); setEditEvent(null); fetchEvents()
  }

  // Timeline data
  const dates: string[] = []
  for (let i = 0; i < 30; i++) {
    const d = new Date(today); d.setDate(d.getDate() + i)
    dates.push(d.toISOString().split("T")[0])
  }
  const timelineLocations = [...new Set(events.filter(e => e.locationName).map(e => e.locationName))]

  const tdStyle: any = { padding:"9px 12px", borderBottom:"1px solid var(--border)", fontSize:12, color:"var(--text)" }
  const thStyle: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)", background:"var(--card2)" }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>Pipeline</h1>
        <div style={{ display:"flex", gap:8 }}>
          <div style={{ display:"flex", gap:2, background:"var(--card2)", border:"1px solid var(--border)", borderRadius:6, padding:3 }}>
            {(["list","timeline"] as const).map(v => (
              <button key={v} onClick={() => setView(v)} style={{ background: view===v ? "var(--accent)" : "transparent", border:"none", color: view===v ? "#fff" : "var(--text3)", padding:"5px 12px", borderRadius:4, fontSize:11, cursor:"pointer", fontWeight: view===v ? 600 : 400 }}>
                {v === "list" ? "List" : "Timeline"}
              </button>
            ))}
          </div>
          <button onClick={() => setShowModal(true)} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>+ New Event</button>
        </div>
      </div>

      {loading && <div style={{ padding:40, textAlign:"center", color:"var(--text3)" }}>Loading...</div>}

      {/* LIST VIEW */}
      {view === "list" && !loading && (
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead>
                <tr>{["Title","Location","Dates","Time","Handling","Agents","Status","Actions"].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr>
              </thead>
              <tbody>
                {events.length === 0 && <tr><td colSpan={8} style={{ ...tdStyle, textAlign:"center", color:"var(--text3)" }}>No pipeline events</td></tr>}
                {events.map(e => {
                  const sc = STATUS_COLORS[e.status] ?? STATUS_COLORS.PLANNED
                  const hc = HANDLING_COLORS[e.handlingType ?? "ADDITIONAL"] ?? HANDLING_COLORS.ADDITIONAL
                  const dateStr = e.pipelineDateEnd && e.pipelineDateEnd !== e.pipelineDate
                    ? `${e.pipelineDate?.slice(5).replace("-",".")} – ${e.pipelineDateEnd?.slice(5).replace("-",".")}`
                    : e.pipelineDate?.slice(5).replace("-",".")
                  return (
                    <tr key={e.id} onMouseEnter={ev => ev.currentTarget.style.background="var(--card2)"} onMouseLeave={ev => ev.currentTarget.style.background="transparent"}>
                      <td style={{ ...tdStyle, fontWeight:600, maxWidth:200, overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>{e.title ?? "—"}</td>
                      <td style={{ ...tdStyle, color:"var(--text2)" }}>{e.locationName ?? "—"}</td>
                      <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text2)" }}>{dateStr}</td>
                      <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text2)" }}>{e.startTime && e.endTime ? `${e.startTime}–${e.endTime}` : "—"}</td>
                      <td style={tdStyle}><Badge text={e.handlingType ?? "ADDITIONAL"} style={hc} /></td>
                      <td style={{ ...tdStyle, fontSize:11, color:"var(--text2)" }}>
                        <div>{e.agentsRequired} req.</div>
                        {e.primaryAgent && <div style={{ fontSize:10, color:"var(--blue-light)" }}>P: {e.primaryAgent}</div>}
                        {e.backupAgent  && <div style={{ fontSize:10, color:"var(--purple)" }}>B: {e.backupAgent}</div>}
                      </td>
                      <td style={tdStyle}><Badge text={e.status} style={sc} /></td>
                      <td style={tdStyle}>
                        <div style={{ display:"flex", gap:4 }}>
                          <button onClick={() => setEditEvent(e)} style={{ background:"rgba(59,126,255,.12)", border:"1px solid rgba(59,126,255,.2)", color:"var(--accent)", padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Edit</button>
                          <button onClick={() => handleDelete(e.id)} style={{ background:"rgba(255,59,92,.12)", border:"1px solid rgba(255,59,92,.2)", color:"var(--danger)", padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Delete</button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{events.length} events</div>
        </div>
      )}

      {/* TIMELINE VIEW */}
      {view === "timeline" && !loading && (
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ borderCollapse:"collapse", fontSize:11 }}>
              <thead>
                <tr>
                  <th style={{ ...thStyle, minWidth:140, position:"sticky", left:0, zIndex:2 }}>Location</th>
                  {dates.map(d => {
                    const dt = new Date(d)
                    const isToday = d === fromDate
                    const isWE = dt.getDay() === 0 || dt.getDay() === 6
                    return <th key={d} style={{ ...thStyle, width:36, minWidth:36, maxWidth:36, padding:"4px 2px", textAlign:"center", background: isToday ? "rgba(59,126,255,.15)" : isWE ? "var(--card)" : "var(--card2)", fontSize:9 }}>
                      <div>{["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]}</div>
                      <div style={{ fontFamily:"IBM Plex Mono" }}>{dt.getDate().toString().padStart(2,"0")}</div>
                    </th>
                  })}
                </tr>
              </thead>
              <tbody>
                {timelineLocations.length === 0 && (
                  <tr><td colSpan={dates.length+1} style={{ ...tdStyle, textAlign:"center", color:"var(--text3)" }}>No pipeline events</td></tr>
                )}
                {timelineLocations.map(locName => (
                  <tr key={locName}>
                    <td style={{ ...tdStyle, position:"sticky", left:0, background:"var(--card)", fontWeight:500, minWidth:140 }}>{locName}</td>
                    {dates.map(d => {
                      const ev = events.find(e => e.locationName === locName && e.pipelineDate <= d && (e.pipelineDateEnd ?? e.pipelineDate) >= d)
                      if (!ev) return <td key={d} style={{ ...tdStyle, padding:0, width:36, borderLeft:"1px solid var(--border)" }} />
                      const isFirst = ev.pipelineDate === d
                      const color = ev.status === "CONFIRMED" ? "rgba(34,197,94,0.3)" : ev.status === "CANCELLED" ? "rgba(136,146,164,0.2)" : "rgba(96,165,250,0.3)"
                      return (
                        <td key={d} onClick={() => setSelectedEvent(ev)} style={{ ...tdStyle, padding:0, width:36, background:color, borderLeft:"1px solid rgba(255,255,255,.05)", cursor:"pointer", overflow:"hidden", whiteSpace:"nowrap" }}>
                          {isFirst && <span style={{ fontSize:9, padding:"0 3px", color:"var(--text)" }}>{ev.title?.slice(0,8)}</span>}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {selectedEvent && (
            <div style={{ padding:16, borderTop:"1px solid var(--border)", background:"var(--card2)" }}>
              <div style={{ display:"flex", justifyContent:"space-between", alignItems:"flex-start" }}>
                <div>
                  <div style={{ fontSize:14, fontWeight:700, color:"var(--text)", marginBottom:6 }}>{selectedEvent.title}</div>
                  <div style={{ display:"flex", gap:12, fontSize:11, color:"var(--text2)" }}>
                    <span>📍 {selectedEvent.locationName}</span>
                    <span>📅 {selectedEvent.pipelineDate} {selectedEvent.pipelineDateEnd ? `– ${selectedEvent.pipelineDateEnd}` : ""}</span>
                    {selectedEvent.startTime && <span>🕐 {selectedEvent.startTime}–{selectedEvent.endTime}</span>}
                    <span>👥 {selectedEvent.agentsRequired} required</span>
                  </div>
                  {selectedEvent.primaryAgent && <div style={{ fontSize:11, color:"var(--blue-light)", marginTop:4 }}>Primary: {selectedEvent.primaryAgent}</div>}
                  {selectedEvent.backupAgent  && <div style={{ fontSize:11, color:"var(--purple)" }}>Backup: {selectedEvent.backupAgent}</div>}
                  {selectedEvent.description  && <div style={{ fontSize:11, color:"var(--text3)", marginTop:4 }}>{selectedEvent.description}</div>}
                </div>
                <div style={{ display:"flex", gap:6 }}>
                  <button onClick={() => setEditEvent(selectedEvent)} style={{ background:"rgba(59,126,255,.12)", border:"1px solid rgba(59,126,255,.2)", color:"var(--accent)", padding:"6px 12px", borderRadius:4, fontSize:11, cursor:"pointer" }}>Edit</button>
                  <button onClick={() => setSelectedEvent(null)} style={{ background:"var(--card)", border:"1px solid var(--border)", color:"var(--text2)", padding:"6px 12px", borderRadius:4, fontSize:11, cursor:"pointer" }}>Close</button>
                </div>
              </div>
            </div>
          )}
        </div>
      )}

      {(showModal || editEvent) && (
        <EventModal event={editEvent} locations={locations} onClose={() => { setShowModal(false); setEditEvent(null) }} onSave={handleSave} />
      )}
    </div>
  )
}
