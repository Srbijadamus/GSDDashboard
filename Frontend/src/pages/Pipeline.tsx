import { useState, useEffect, useMemo } from "react"

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

const fmtDate = (d: string | null | undefined) => d ? `${d.slice(8,10)}.${d.slice(5,7)}.${d.slice(0,4)}` : ""

const WEEKDAYS = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"]
const groupLabel = (iso: string) => {
  const d = new Date(iso + "T00:00:00")
  return `${WEEKDAYS[d.getDay()]} ${fmtDate(iso)}`
}
// Renders a Description; the sentence containing "pending" is shown in orange (warn).
function renderDesc(desc: string) {
  const idx = desc.toLowerCase().indexOf("pending")
  if (idx < 0) return desc
  let start = 0
  for (let i = idx; i >= 0; i--) {
    if (i > 0 && desc[i] === "." && desc[i + 1] === " " && desc[i + 2] && /[A-Z]/.test(desc[i + 2])) { start = i + 2; break }
  }
  const before = desc.slice(0, start).trim()
  const note = desc.slice(start).trim()
  return (
    <>
      {before}{before ? " " : ""}
      <span style={{ color:"rgb(var(--st-warn-fg))" }}>{note}</span>
    </>
  )
}

function Badge({ text, style }: { text: string; style: any }) {
  return <span style={{ ...style, padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600, border:`1px solid ${style.border}` }}>{text}</span>
}

function EventModal({ event, locations, onClose, onSave }: any) {
  const isWicCode = (c: string) => locations.some((l: any) => l.locationCode === c)
  const initialCode = event?.locationCode ?? ""
  const [locationIsCustom, setLocationIsCustom] = useState(!!initialCode && !isWicCode(initialCode))
  const [error, setError] = useState("")
  const [saving, setSaving] = useState(false)
  const [form, setForm] = useState({
    title: event?.title ?? "",
    locationCode: initialCode,
    pipelineDate: event?.pipelineDate ?? new Date().toISOString().split("T")[0],
    pipelineDateEnd: event?.pipelineDateEnd ?? "",
    startTime: event?.startTime ?? "",
    endTime: event?.endTime ?? "",
    handlingType: event?.handlingType ?? "ADDITIONAL",
    agentsRequired: event?.agentsRequired ?? 1,
    primaryAgent: event?.primaryAgent ?? "",
    secondAgent: event?.secondAgent ?? "",
    backupAgent: event?.backupAgent ?? "",
    additionalAgentsNeeded: event?.additionalAgentsNeeded ?? 0,
    handledBy: event?.handledBy ?? "",
    description: event?.description ?? "",
    status: event?.status ?? "PLANNED",
  })

  const h = (field: string, val: any) => setForm(f => ({ ...f, [field]: val }))

  const handleSave = async () => {
    setError("")
    setSaving(true)
    const body: any = {
      locationCode: form.locationCode || null,
      pipelineDate: form.pipelineDate,
      pipelineDateEnd: form.pipelineDateEnd || null,
      title: form.title,
      description: form.description || null,
      primaryAgent: form.primaryAgent || null,
      secondAgent: form.secondAgent || null,
      backupAgent: form.backupAgent || null,
      additionalAgentsNeeded: Number(form.additionalAgentsNeeded),
      handledBy: form.handledBy || null,
      handlingType: form.handlingType || "ADDITIONAL",
      startTime: form.startTime || null,
      endTime: form.endTime || null,
      agentsRequired: Number(form.agentsRequired),
    }
    try {
      const r = event
        ? await fetch(`${BASE}/api/pipeline/${event.id}`, { method:"PATCH", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ ...body, status: form.status }) })
        : await fetch(`${BASE}/api/pipeline`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify(body) })
      if (!r.ok) {
        let msg = `API ${r.status}`
        try { const j = await r.json(); if (j && j.error) msg = j.error } catch { /* ignore */ }
        setError(msg)
        setSaving(false)
        return
      }
      onSave()
    } catch {
      setError("Request failed.")
      setSaving(false)
    }
  }

  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000, display:"flex", alignItems:"center", justifyContent:"center" }}
      onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:24, width:500, maxHeight:"90vh", overflowY:"auto" }}>
        <h2 className="text-ink" style={{ fontSize:16, fontWeight:600, marginBottom:20 }}>{event ? "Edit Pipeline Event" : "New Pipeline Event"}</h2>
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div><label className={labelCls} style={labelStyle}>Title</label><input value={form.title} onChange={e => h("title", e.target.value)} className={inputCls} style={inputStyle} placeholder="Event title..." /></div>
          <div><label className={labelCls} style={labelStyle}>Location</label>
            <select value={locationIsCustom ? "__custom__" : form.locationCode} onChange={e => {
                const v = e.target.value
                if (v === "__custom__") { setLocationIsCustom(true); h("locationCode", "") }
                else { setLocationIsCustom(false); h("locationCode", v) }
              }} className={inputCls} style={inputStyle}>
              <option value="">-- Select WIC location --</option>
              {locations.map((l: any) => <option key={l.locationCode} value={l.locationCode}>{l.displayName}</option>)}
              <option value="__custom__">-- Custom location (free text) --</option>
            </select>
            {locationIsCustom && <input value={form.locationCode} onChange={e => h("locationCode", e.target.value)} className={inputCls} style={{ ...inputStyle, marginTop:6 }} placeholder="Location name or address..." />}
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
          <div><label className={labelCls} style={labelStyle}>Second Agent</label><input value={form.secondAgent} onChange={e => h("secondAgent", e.target.value)} className={inputCls} style={inputStyle} placeholder="Second agent name..." /></div>
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
          {error && <div style={{ fontSize:12, color:"rgb(var(--st-crit-fg))" }}>{error}</div>}
        </div>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:20 }}>
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          <button onClick={handleSave} disabled={saving} className="bg-info-solid" style={{ border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>{event ? "Save Changes" : "Create Event"}</button>
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
  const [filter, setFilter] = useState<"upcoming"|"past"|"all">("upcoming")
  const [search, setSearch] = useState("")

  const today = new Date()
  const fromDate = today.toISOString().split("T")[0]
  const toDate = new Date(today.getTime() + 30*24*60*60*1000).toISOString().split("T")[0]

  const fetchEvents = async () => {
    setLoading(true)
    try {
      const r = await fetch(`${BASE}/api/pipeline?from=${fromDate}&to=${toDate}`)
      if (!r.ok) throw new Error(`API ${r.status}`)
      setEvents(await r.json())
    } catch { /* ignore */ }
    setLoading(false)
  }

  const fetchLocations = async () => {
    try {
      const r = await fetch(`${BASE}/api/wic/locations`)
      if (!r.ok) throw new Error(`API ${r.status}`)
      setLocations(await r.json())
    } catch { /* ignore */ }
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
  const timelineLocations = [...new Set(events.map(e => e.locationName || e.locationCode).filter(Boolean))]

  // List: filter + search + group by start date (ascending).
  const filtered = useMemo(() => {
    let evs = events.filter(e => {
      const end = e.pipelineDateEnd ?? e.pipelineDate
      if (filter === "upcoming") return end >= fromDate
      if (filter === "past") return end < fromDate
      return true
    })
    const q = search.trim().toLowerCase()
    if (q) {
      evs = evs.filter(e =>
        [e.title, e.locationName, e.locationCode, e.primaryAgent, e.secondAgent, e.backupAgent]
          .filter(Boolean).join(" ").toLowerCase().includes(q))
    }
    return evs
  }, [events, filter, search, fromDate])

  const groups = useMemo(() => {
    const m = new Map<string, any[]>()
    for (const e of filtered) {
      const k = e.pipelineDate
      if (!m.has(k)) m.set(k, [])
      m.get(k)!.push(e)
    }
    return [...m.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  }, [filtered])

  const thCls = "text-ink-soft border-b border-line-subtle bg-sunken"
  const thStyle: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase" as const, letterSpacing:".07em" }

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
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div style={{ display:"flex", gap:8, alignItems:"center", justifyContent:"space-between" }}>
            <div className="bg-sunken border border-line-subtle" style={{ display:"flex", gap:2, borderRadius:6, padding:3 }}>
              {(["upcoming","past","all"] as const).map(f => (
                <button key={f} onClick={() => setFilter(f)} className={filter===f ? "bg-info-solid" : "text-ink-soft"}
                  style={{ border:"none", color: filter===f ? "#fff" : undefined, padding:"5px 14px", borderRadius:4, fontSize:11, cursor:"pointer", fontWeight: filter===f ? 600 : 400, textTransform:"capitalize" }}>
                  {f}
                </button>
              ))}
            </div>
            <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search title, location, agent..."
              className="bg-sunken border border-line-subtle text-ink" style={{ padding:"7px 10px", borderRadius:6, fontSize:12, width:260, outline:"none" }} />
          </div>

          {groups.length === 0 && <div className="bg-raised border border-line-subtle text-ink-soft" style={{ borderRadius:8, padding:40, textAlign:"center" }}>No pipeline events</div>}

          {groups.map(([dateKey, evs]) => (
            <div key={dateKey} className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
              <div className="bg-sunken border-b border-line-subtle text-ink" style={{ padding:"8px 12px", fontSize:12, fontWeight:600 }}>{groupLabel(dateKey)}</div>
              {evs.map((e: any) => {
                const sc = STATUS_COLORS[e.status] ?? STATUS_COLORS.PLANNED
                const hc = HANDLING_COLORS[e.handlingType ?? "ADDITIONAL"] ?? HANDLING_COLORS.ADDITIONAL
                const cancelled = e.status === "CANCELLED"
                const period = e.pipelineDateEnd && e.pipelineDateEnd !== e.pipelineDate
                  ? `${fmtDate(e.pipelineDate)} – ${fmtDate(e.pipelineDateEnd)}` : null
                const staffCount = (e.primaryAgent ? 1 : 0) + (e.secondAgent ? 1 : 0)
                const req = e.agentsRequired ?? 1
                const staffFull = staffCount >= req
                const agents: { label: string; name: string }[] = []
                if (e.primaryAgent) agents.push({ label:"P", name:e.primaryAgent })
                if (e.secondAgent)  agents.push({ label:"P", name:e.secondAgent })
                if (e.backupAgent)  agents.push({ label:"B", name:e.backupAgent })
                return (
                  <div key={e.id} className="border-b border-line-subtle" style={{ padding:"10px 12px", display:"flex", gap:16, alignItems:"flex-start", borderBottom: evs[evs.length-1] === e ? "none" : undefined }}>
                    <div style={{ flex:2, minWidth:0 }}>
                      <div className="text-ink" style={{ fontSize:13, fontWeight:600, textDecoration: cancelled ? "line-through" : undefined }}>{e.title ?? "—"}</div>
                      {e.description && <div style={{ fontSize:11, color:"rgb(var(--text-secondary))", marginTop:2 }}>{renderDesc(e.description)}</div>}
                      {period && <div className="font-mono" style={{ fontSize:10, color:"rgb(var(--text-tertiary))", marginTop:2 }}>{period}</div>}
                    </div>
                    <div style={{ flex:1, minWidth:0, display:"flex", flexDirection:"column", gap:3 }}>
                      <div style={{ fontSize:12, color:"rgb(var(--text-secondary))" }}>{e.locationName || e.locationCode || "—"}</div>
                      <div className="font-mono" style={{ fontSize:11, color:"rgb(var(--text-secondary))" }}>{e.startTime && e.endTime ? `${e.startTime}–${e.endTime}` : "—"}</div>
                      <div><Badge text={e.handlingType ?? "ADDITIONAL"} style={hc} /></div>
                    </div>
                    <div style={{ flex:1.5, minWidth:0 }}>
                      <div style={{ display:"flex", flexWrap:"wrap", gap:4 }}>
                        {agents.map((a, i) => (
                          <span key={i} className="bg-sunken border border-line-subtle" style={{ display:"inline-flex", alignItems:"center", gap:4, padding:"1px 6px", borderRadius:4, fontSize:10 }}>
                            <span style={{ fontWeight:700, color:"rgb(var(--text-tertiary))" }}>{a.label}</span>
                            <span className="text-ink">{a.name}</span>
                          </span>
                        ))}
                      </div>
                      <div style={{ fontSize:11, fontWeight:600, marginTop:4, color: staffFull ? "rgb(var(--st-good-fg))" : "rgb(var(--st-warn-fg))" }}>
                        {staffCount}/{req} staffed
                      </div>
                    </div>
                    <div style={{ display:"flex", flexDirection:"column", alignItems:"flex-end", gap:6, flexShrink:0 }}>
                      <Badge text={e.status} style={sc} />
                      <div style={{ display:"flex", gap:4 }}>
                        <button onClick={() => setEditEvent(e)} className="bg-info-bg border border-info-bd text-info-fg" style={{ padding:"3px 7px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Edit</button>
                        <button onClick={() => handleDelete(e.id)} className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ padding:"3px 7px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Delete</button>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>
          ))}
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
                  <tr><td colSpan={dates.length+1} className="text-ink-soft" style={{ padding:"20px 12px", fontSize:12, textAlign:"center" }}>No pipeline events</td></tr>
                )}
                {timelineLocations.map(locName => (
                  <tr key={locName}>
                    <td className="bg-raised" style={{ padding:"9px 12px", fontSize:12, position:"sticky", left:0, fontWeight:500, minWidth:140 }}>{locName}</td>
                    {dates.map(d => {
                      const ev = events.find(e => (e.locationName || e.locationCode) === locName && e.pipelineDate <= d && (e.pipelineDateEnd ?? e.pipelineDate) >= d)
                      if (!ev) return <td key={d} className="border-l border-line-subtle" style={{ padding:0, width:36 }} />
                      const isFirst = ev.pipelineDate === d
                      const color = ev.status === "CONFIRMED" ? "rgb(var(--st-good-mid))" : ev.status === "CANCELLED" ? "rgb(var(--st-neutral-bg))" : "rgb(var(--st-info-mid))"
                      return (
                        <td key={d} onClick={() => setSelectedEvent(ev)} style={{ padding:0, width:36, background:color, borderLeft:"1px solid rgb(var(--line-subtle))", cursor:"pointer", overflow:"hidden", whiteSpace:"nowrap" }}>
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
                    <span>{selectedEvent.locationName || selectedEvent.locationCode}</span>
                    <span>{fmtDate(selectedEvent.pipelineDate)}{selectedEvent.pipelineDateEnd ? ` – ${fmtDate(selectedEvent.pipelineDateEnd)}` : ""}</span>
                    {selectedEvent.startTime && <span>{selectedEvent.startTime}–{selectedEvent.endTime}</span>}
                    <span>{selectedEvent.agentsRequired} required</span>
                  </div>
                  {(selectedEvent.primaryAgent || selectedEvent.secondAgent) && <div style={{ fontSize:11, color:"rgb(var(--st-info-fg))", marginTop:4 }}>Primary: {[selectedEvent.primaryAgent, selectedEvent.secondAgent].filter(Boolean).join(", ")}</div>}
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
