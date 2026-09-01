import { useState, useEffect } from "react"

const BASE = ""

const STATUS_COLORS: Record<string,any> = {
  PLANNED:   { bg:"rgb(var(--st-neutral-bg))", border:"rgb(var(--st-neutral-bd))", color:"rgb(var(--st-neutral-fg))" },
  CONFIRMED: { bg:"rgb(var(--st-good-bg))",    border:"rgb(var(--st-good-bd))",    color:"rgb(var(--st-good-fg))"    },
  CANCELLED: { bg:"rgb(var(--st-crit-bg))",    border:"rgb(var(--st-crit-bd))",    color:"rgb(var(--st-crit-fg))"    },
}
const HANDLING_COLORS: Record<string,any> = {
  ADDITIONAL: { bg:"rgb(var(--st-info-bg))",  border:"rgb(var(--st-info-bd))",  color:"rgb(var(--st-info-fg))"  },
  LOCAL:      { bg:"rgb(var(--st-learn-bg))", border:"rgb(var(--st-learn-bd))", color:"rgb(var(--st-learn-fg))" },
}

const inputStyle: any = { padding:"7px 10px", borderRadius:6, fontSize:12, width:"100%", fontFamily:"IBM Plex Sans", outline:"none" }
const inputCls = "bg-sunken border border-line-subtle text-ink"
const labelStyle: any = { fontSize:11, marginBottom:4, display:"block" }
const labelCls = "text-ink-soft"

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
      <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:24, width:500, maxHeight:"90vh", overflowY:"auto" }}>
        <h2 className="text-ink" style={{ fontSize:16, fontWeight:600, marginBottom:20 }}>{event ? "Edit Pipeline Event" : "New Pipeline Event"}</h2>
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div><label className={labelCls} style={labelStyle}>Title</label><input value={form.title} onChange={e => h("title", e.target.value)} className={inputCls} style={inputStyle} placeholder="Event title..." /></div>
          <div><label className={labelCls} style={labelStyle}>Location</label>
            <select value={form.locationCode} onChange={e => h("locationCode", e.target.value)} className={inputCls} style={inputStyle}>
              <option value="">-- No specific location --</option>
              {locations.map((l: any) => <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>)}
            </select>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>Date From</label><input type="date" value={form.pipelineDate} onChange={e => h("pipelineDate", e.target.value)} className={inputCls} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>Date To</label><input type="date" value={form.pipelineDateEnd} onChange={e => h("pipelineDateEnd", e.target.value)} className={inputCls} style={inputStyle} /></div>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>Start Time</label><input type="time" value={form.startTime} onChange={e => h("startTime", e.target.value)} className={inputCls} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>End Time</label><input type="time" value={form.endTime} onChange={e => h("endTime", e.target.value)} className={inputCls} style={inputStyle} /></div>
          </div>
          <div>
            <label className={labelCls} style={labelStyle}>Handling Type</label>
            <div style={{ display:"flex", gap:8 }}>
              {["ADDITIONAL","LOCAL"].map(t => (
                <button key={t} onClick={() => h("handlingType", t)} className={form.handlingType===t ? "" : "border border-line-subtle text-ink-soft"} style={{ flex:1, padding:"7px 0", borderRadius:6, border: form.handlingType===t ? `1px solid ${HANDLING_COLORS[t].border}` : undefined, background: form.handlingType===t ? HANDLING_COLORS[t].bg : "transparent", color: form.handlingType===t ? HANDLING_COLORS[t].color : undefined, cursor:"pointer", fontSize:12, fontWeight:600 }}>{t}</button>
              ))}
            </div>
          </div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>Agents Required</label><input type="number" min="1" value={form.agentsRequired} onChange={e => h("agentsRequired", e.target.value)} className={inputCls} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label className={labelCls} style={labelStyle}>Additional Needed</label><input type="number" min="0" value={form.additionalAgentsNeeded} onChange={e => h("additionalAgentsNeeded", e.target.value)} className={inputCls} style={inputStyle} /></div>
          </div>
          <div><label className={labelCls} style={labelStyle}>Primary Agent</label><input value={form.primaryAgent} onChange={e => h("primaryAgent", e.target.value)} className={inputCls} style={inputStyle} placeholder="Primary agent name..." /></div>
          <div><label className={labelCls} style={labelStyle}>Backup Agent</label><input value={form.backupAgent} onChange={e => h("backupAgent", e.target.value)} className={inputCls} style={inputStyle} placeholder="Backup agent name..." /></div>
          <div><label className={labelCls} style={labelStyle}>Handled By</label><input value={form.handledBy} onChange={e => h("handledBy", e.target.value)} className={inputCls} style={inputStyle} placeholder="Team lead or handler..." /></div>
          {event && (
            <div><label className={labelCls} style={labelStyle}>Status</label>
              <select value={form.status} onChange={e => h("status", e.target.value)} className={inputCls} style={inputStyle}>
                {["PLANNED","CONFIRMED","CANCELLED"].map(s => <option key={s} value={s}>{s}</option>)}
              </select>
            </div>
          )}
          <div><label className={labelCls} style={labelStyle}>Notes</label><textarea value={form.description} onChange={(e: any) => h("description", e.target.value)} rows={3} className={inputCls} style={{ ...inputStyle, resize:"vertical" }} placeholder="Notes..." /></div>
        </div>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:20 }}>
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          <button onClick={handleSave} className="bg-info-solid" style={{ border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>{event ? "Save Changes" : "Create Event"}</button>
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
      if (!r.ok) throw new Error(`API ${r.status}`)
      setEvents(await r.json())
    } catch {}
    setLoading(false)
  }

  const fetchLocations = async () => {
    try {
      const r = await fetch(`${BASE}/api/wic/locations`)
      if (!r.ok) throw new Error(`API ${r.status}`)
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

  const tdStyle: any = { padding:"9px 12px", fontSize:12 }
  const tdCls = "border-b border-line-subtle text-ink"
  const thStyle: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase" as const, letterSpacing:".07em" }
  const thCls = "text-ink-soft border-b border-line-subtle bg-sunken"

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 className="text-ink" style={{ fontSize:22, fontWeight:600 }}>Pipeline</h1>
        <div style={{ display:"flex", gap:8 }}>
          <div className="bg-sunken border border-line-subtle" style={{ display:"flex", gap:2, borderRadius:6, padding:3 }}>
            {(["list","timeline"] as const).map(v => (
              <button key={v} onClick={() => setView(v)} className={view===v ? "bg-info-solid" : "text-ink-soft"} style={{ border:"none", color: view===v ? "#fff" : undefined, padding:"5px 12px", borderRadius:4, fontSize:11, cursor:"pointer", fontWeight: view===v ? 600 : 400 }}>
                {v === "list" ? "List" : "Timeline"}
              </button>
            ))}
          </div>
          <button onClick={() => setShowModal(true)} className="bg-info-solid" style={{ border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>+ New Event</button>
        </div>
      </div>

      {loading && <div className="text-ink-soft" style={{ padding:40, textAlign:"center" }}>Loading...</div>}

      {/* LIST VIEW */}
      {view === "list" && !loading && (
        <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead>
                <tr>{["Title","Location","Dates","Time","Handling","Agents","Status","Actions"].map(h => <th key={h} className={thCls} style={thStyle}>{h}</th>)}</tr>
              </thead>
              <tbody>
                {events.length === 0 && <tr><td colSpan={8} className={`${tdCls} text-ink-soft`} style={{ ...tdStyle, textAlign:"center" }}>No pipeline events</td></tr>}
                {events.map(e => {
                  const sc = STATUS_COLORS[e.status] ?? STATUS_COLORS.PLANNED
                  const hc = HANDLING_COLORS[e.handlingType ?? "ADDITIONAL"] ?? HANDLING_COLORS.ADDITIONAL
                  const dateStr = e.pipelineDateEnd && e.pipelineDateEnd !== e.pipelineDate
                    ? `${e.pipelineDate?.slice(5).replace("-",".")} – ${e.pipelineDateEnd?.slice(5).replace("-",".")}`
                    : e.pipelineDate?.slice(5).replace("-",".")
                  return (
                    <tr key={e.id} className="hover:bg-hovered">
                      <td className={tdCls} style={{ ...tdStyle, fontWeight:600, maxWidth:200, overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>{e.title ?? "—"}</td>
                      <td className={`${tdCls} text-ink-muted`} style={tdStyle}>{e.locationName ?? "—"}</td>
                      <td className={`${tdCls} font-mono text-ink-muted`} style={{ ...tdStyle, fontSize:11 }}>{dateStr}</td>
                      <td className={`${tdCls} font-mono text-ink-muted`} style={{ ...tdStyle, fontSize:11 }}>{e.startTime && e.endTime ? `${e.startTime}–${e.endTime}` : "—"}</td>
                      <td className={tdCls} style={tdStyle}><Badge text={e.handlingType ?? "ADDITIONAL"} style={hc} /></td>
                      <td className={`${tdCls} text-ink-muted`} style={{ ...tdStyle, fontSize:11 }}>
                        <div>{e.agentsRequired} req.</div>
                        {e.primaryAgent && <div style={{ fontSize:10, color:"rgb(var(--st-info-fg))" }}>P: {e.primaryAgent}</div>}
                        {e.backupAgent  && <div className="text-learn-fg" style={{ fontSize:10 }}>B: {e.backupAgent}</div>}
                      </td>
                      <td className={tdCls} style={tdStyle}><Badge text={e.status} style={sc} /></td>
                      <td className={tdCls} style={tdStyle}>
                        <div style={{ display:"flex", gap:4 }}>
                          <button onClick={() => setEditEvent(e)} className="bg-info-bg border border-info-bd text-info-fg" style={{ padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Edit</button>
                          <button onClick={() => handleDelete(e.id)} className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Delete</button>
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
          <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding:"8px 12px", fontSize:11 }}>{events.length} events</div>
        </div>
      )}

      {/* TIMELINE VIEW */}
      {view === "timeline" && !loading && (
        <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
          <div style={{ overflowX:"auto" }}>
            <table style={{ borderCollapse:"collapse", fontSize:11 }}>
              <thead>
                <tr>
                  <th className={thCls} style={{ ...thStyle, minWidth:140, position:"sticky", left:0, zIndex:2 }}>Location</th>
                  {dates.map(d => {
                    const dt = new Date(d)
                    const isToday = d === fromDate
                    const isWE = dt.getDay() === 0 || dt.getDay() === 6
                    return <th key={d} className={`${thCls} ${isToday ? "bg-info-bg" : isWE ? "bg-raised" : "bg-sunken"}`} style={{ ...thStyle, width:36, minWidth:36, maxWidth:36, padding:"4px 2px", textAlign:"center", fontSize:9 }}>
                      <div>{["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]}</div>
                      <div className="font-mono">{dt.getDate().toString().padStart(2,"0")}</div>
                    </th>
                  })}
                </tr>
              </thead>
              <tbody>
                {timelineLocations.length === 0 && (
                  <tr><td colSpan={dates.length+1} className={`${tdCls} text-ink-soft`} style={{ ...tdStyle, textAlign:"center" }}>No pipeline events</td></tr>
                )}
                {timelineLocations.map(locName => (
                  <tr key={locName}>
                    <td className={`${tdCls} bg-raised`} style={{ ...tdStyle, position:"sticky", left:0, fontWeight:500, minWidth:140 }}>{locName}</td>
                    {dates.map(d => {
                      const ev = events.find(e => e.locationName === locName && e.pipelineDate <= d && (e.pipelineDateEnd ?? e.pipelineDate) >= d)
                      if (!ev) return <td key={d} className={`${tdCls} border-l border-line-subtle`} style={{ ...tdStyle, padding:0, width:36 }} />
                      const isFirst = ev.pipelineDate === d
                      const color = ev.status === "CONFIRMED" ? "rgb(var(--st-good-mid))" : ev.status === "CANCELLED" ? "rgb(var(--st-neutral-bg))" : "rgb(var(--st-info-mid))"
                      return (
                        <td key={d} onClick={() => setSelectedEvent(ev)} className={tdCls} style={{ ...tdStyle, padding:0, width:36, background:color, borderLeft:"1px solid rgb(var(--line-subtle))", cursor:"pointer", overflow:"hidden", whiteSpace:"nowrap" }}>
                          {isFirst && <span className="text-ink" style={{ fontSize:9, padding:"0 3px" }}>{ev.title?.slice(0,8)}</span>}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {selectedEvent && (
            <div className="border-t border-line-subtle bg-sunken" style={{ padding:16 }}>
              <div style={{ display:"flex", justifyContent:"space-between", alignItems:"flex-start" }}>
                <div>
                  <div className="text-ink" style={{ fontSize:14, fontWeight:700, marginBottom:6 }}>{selectedEvent.title}</div>
                  <div className="text-ink-muted" style={{ display:"flex", gap:12, fontSize:11 }}>
                    <span>{selectedEvent.locationName}</span>
                    <span>{selectedEvent.pipelineDate} {selectedEvent.pipelineDateEnd ? `– ${selectedEvent.pipelineDateEnd}` : ""}</span>
                    {selectedEvent.startTime && <span>{selectedEvent.startTime}–{selectedEvent.endTime}</span>}
                    <span>{selectedEvent.agentsRequired} required</span>
                  </div>
                  {selectedEvent.primaryAgent && <div style={{ fontSize:11, color:"rgb(var(--st-info-fg))", marginTop:4 }}>Primary: {selectedEvent.primaryAgent}</div>}
                  {selectedEvent.backupAgent  && <div className="text-learn-fg" style={{ fontSize:11 }}>Backup: {selectedEvent.backupAgent}</div>}
                  {selectedEvent.description  && <div className="text-ink-soft" style={{ fontSize:11, marginTop:4 }}>{selectedEvent.description}</div>}
                </div>
                <div style={{ display:"flex", gap:6 }}>
                  <button onClick={() => setEditEvent(selectedEvent)} className="bg-info-bg border border-info-bd text-info-fg" style={{ padding:"6px 12px", borderRadius:4, fontSize:11, cursor:"pointer" }}>Edit</button>
                  <button onClick={() => setSelectedEvent(null)} className="bg-raised border border-line-subtle text-ink-muted" style={{ padding:"6px 12px", borderRadius:4, fontSize:11, cursor:"pointer" }}>Close</button>
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
