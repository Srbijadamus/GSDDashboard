import { useState, useEffect } from "react"
import { AlertTriangle } from "lucide-react"

const BASE = ""

const inputCls = "bg-sunken border border-line-subtle text-ink"
const inputSty: any = { padding:"7px 10px", borderRadius:6, fontSize:12, width:"100%", fontFamily:"IBM Plex Sans", outline:"none" }
const labelCls = "text-ink-soft"
const labelSty: any = { fontSize:11, marginBottom:4, display:"block" }

function TopicModal({ onClose, onSave }: any) {
  const [form, setForm] = useState({ name:"", durationHours:2, minGroupSize:3, maxGroupSize:15, isMandatory:false, notes:"" })
  const h = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }))
  const save = async () => {
    await fetch(`${BASE}/api/training/topics`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ ...form, id:0 }) })
    onSave()
  }
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000, display:"flex", alignItems:"center", justifyContent:"center" }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:24, width:460 }}>
        <h2 className="text-ink" style={{ fontSize:15, fontWeight:600, marginBottom:20 }}>New Training Topic</h2>
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div><label className={labelCls} style={labelSty}>Name</label><input value={form.name} onChange={e => h("name", e.target.value)} className={inputCls} style={inputSty} placeholder="Topic name..." /></div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label className={labelCls} style={labelSty}>Duration (h)</label><input type="number" min={1} max={8} value={form.durationHours} onChange={e => h("durationHours", Number(e.target.value))} className={inputCls} style={inputSty} /></div>
            <div style={{ flex:1 }}><label className={labelCls} style={labelSty}>Min Group</label><input type="number" min={1} value={form.minGroupSize} onChange={e => h("minGroupSize", Number(e.target.value))} className={inputCls} style={inputSty} /></div>
            <div style={{ flex:1 }}><label className={labelCls} style={labelSty}>Max Group</label><input type="number" min={1} value={form.maxGroupSize} onChange={e => h("maxGroupSize", Number(e.target.value))} className={inputCls} style={inputSty} /></div>
          </div>
          <div>
            <label className={labelCls} style={labelSty}>Mandatory</label>
            <div style={{ display:"flex", gap:8 }}>
              {[true, false].map(v => (
                <button key={String(v)} onClick={() => h("isMandatory", v)}
                  className={`border ${form.isMandatory===v ? (v ? "bg-good-bg border-good-bd text-good-fg" : "bg-neutralst-bg border-neutralst-bd text-neutralst-fg") : "border-line-subtle text-ink-soft"}`}
                  style={{ flex:1, padding:"7px 0", borderRadius:6, cursor:"pointer", fontSize:12, fontWeight:600, background: form.isMandatory===v ? undefined : "transparent" }}>
                  {v ? "YES" : "NO"}
                </button>
              ))}
            </div>
          </div>
          <div><label className={labelCls} style={labelSty}>Notes</label><textarea value={form.notes} onChange={(e: any) => h("notes", e.target.value)} rows={2} className={inputCls} style={{ ...inputSty, resize:"vertical" }} /></div>
        </div>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:20 }}>
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          <button onClick={save} className="bg-info-solid text-white" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>Create Topic</button>
        </div>
      </div>
    </div>
  )
}

export default function Training() {
  const [tab, setTab] = useState<"scheduler"|"sessions"|"topics">("scheduler")
  const [topics, setTopics] = useState<any[]>([])
  const [sessions, setSessions] = useState<any[]>([])
  const [employees, setEmployees] = useState<any[]>([])
  const [empSearch, setEmpSearch] = useState("")
  const [selectedTopic, setSelectedTopic] = useState<number|null>(null)
  const [selectedAgents, setSelectedAgents] = useState<string[]>([])
  const today = new Date().toISOString().split("T")[0]
  const plus14 = new Date(Date.now()+14*24*60*60*1000).toISOString().split("T")[0]
  const [dateFrom, setDateFrom] = useState(today)
  const [dateTo, setDateTo]     = useState(plus14)
  const [suggestions, setSuggestions] = useState<any[]>([])
  const [warning, setWarning] = useState("")
  const [slotView, setSlotView] = useState<"list"|"calendar">("list")
  const [expandedSession, setExpandedSession] = useState<number | null>(null)
  const [sessionShifts, setSessionShifts] = useState<any>({})
  const [suggesting, setSuggesting]   = useState(false)
  const [hasSuggested, setHasSuggested] = useState(false)
  const [suggestError, setSuggestError] = useState("")
  const [confirmingSlot, setConfirmingSlot] = useState<any>(null)
  const [showTopicModal, setShowTopicModal] = useState(false)
  const [toast, setToast] = useState("")

  const fetchTopics = async () => {
    const r = await fetch(`${BASE}/api/training/topics`)
    if (!r.ok) return
    const d = await r.json()
    setTopics(Array.isArray(d) ? d : [])
  }
  const fetchSessions = async () => { const r = await fetch(`${BASE}/api/training/sessions?from=${today}&to=${new Date(Date.now()+30*24*60*60*1000).toISOString().split("T")[0]}`); if (!r.ok) return; setSessions(await r.json()) }
  const fetchEmployees = async () => { const r = await fetch(`${BASE}/api/employees`); if (!r.ok) return; setEmployees(await r.json()) }

  useEffect(() => { fetchTopics(); fetchSessions(); fetchEmployees() }, [])

  const toggleAgent = (id: string) => setSelectedAgents(p => p.includes(id) ? p.filter(x => x !== id) : [...p, id])

  const suggest = async () => {
    if (!selectedTopic) return
    setSuggesting(true); setSuggestions([]); setWarning(""); setSuggestError(""); setHasSuggested(false)
    try {
      const r = await fetch(`${BASE}/api/training/suggest`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ topicId: selectedTopic, dateFrom, dateTo, selectedAgentIds: selectedAgents, maxResults: 12 }) })
      if (!r.ok) { setSuggestError(`Server error ${r.status} — ${r.statusText}`); setSuggesting(false); setHasSuggested(true); return }
      const data = await r.json()
      if (data && data.warning) setWarning(data.warning); else setWarning("")
      const slots = Array.isArray(data) ? data : (data.slots ?? [])
      setSuggestions(slots)
    } catch (err: any) {
      setSuggestError(err?.message ?? "Network error — could not reach the server.")
    }
    setSuggesting(false)
    setHasSuggested(true)
  }

  const confirm = async () => {
    if (!confirmingSlot) return
    const r = await fetch(`${BASE}/api/training/confirm`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ topicId: selectedTopic, date: confirmingSlot.date, startTime: confirmingSlot.startTime, endTime: confirmingSlot.endTime, agentIds: confirmingSlot.selectedAttendees.map((a: any) => a.employeeId), notes: null }) })
    const data = await r.json()
    showToast(`✅ Training scheduled! ${data.agentIds?.length ?? confirmingSlot.selectedAttendees.length} agents have TRAINING entries created.`)
    setConfirmingSlot(null); setSuggestions([]); fetchSessions()
  }

  const showToast = (msg: string) => {
    setToast(msg)
    setTimeout(() => setToast(""), 4000)
  }

  const toggleSession = async (s: any) => {
    if (expandedSession === s.id) { setExpandedSession(null); return }
    setExpandedSession(s.id)
    if (!sessionShifts[s.scheduledDate]) {
      try {
        const r = await fetch(`${BASE}/api/shifts?from=${s.scheduledDate}&to=${s.scheduledDate}`)
        const data = await r.json()
        setSessionShifts((prev: any) => ({ ...prev, [s.scheduledDate]: data }))
      } catch {}
    }
  }

  const deleteSession = async (id: number) => {
    if (!window.confirm("Delete this session?")) return
    const r = await fetch(`${BASE}/api/training/sessions/${id}`, { method:"DELETE" })
    if (!r.ok) { showToast(`Could not delete session — ${r.status}`); return }
    fetchSessions()
  }

  const filteredEmps = employees.filter((e: any) => !empSearch || e.fullName?.toLowerCase().includes(empSearch.toLowerCase()) || e.teamLeadName?.toLowerCase().includes(empSearch.toLowerCase()))

  const thCls = "bg-sunken border-b border-line-subtle text-ink-soft"
  const thSty: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase" as const, letterSpacing:".07em" }
  const tdCls = "border-b border-line-subtle"
  const tdSty: any = { padding:"9px 12px", fontSize:12 }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      {toast && <div className="bg-good-solid text-white" style={{ position:"fixed", bottom:24, right:24, padding:"10px 18px", borderRadius:8, fontSize:12, zIndex:9999, fontFamily:"IBM Plex Sans" }}>{toast}</div>}

      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 className="text-ink" style={{ fontSize:22, fontWeight:600 }}>Training</h1>
      </div>

      <div style={{ display:"flex", gap:4 }}>
        {(["scheduler","sessions","topics"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)}
            className={tab===t ? "bg-info-solid border border-info-bd text-white" : "bg-raised border border-line-subtle text-ink-muted"}
            style={{ borderRadius:6, padding:"6px 16px", fontSize:12, cursor:"pointer", fontWeight: tab===t ? 600 : 400, textTransform:"capitalize" }}>{t}</button>
        ))}
      </div>

      {/* SCHEDULER TAB */}
      {tab === "scheduler" && (
        <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
          <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, padding:16 }}>
            <div className="text-ink-soft" style={{ fontSize:11, fontWeight:700, letterSpacing:".08em", textTransform:"uppercase", marginBottom:14 }}>Training Scheduler</div>
            <div style={{ display:"flex", gap:12, flexWrap:"wrap", marginBottom:14 }}>
              <div style={{ flex:2, minWidth:200 }}>
                <label className={labelCls} style={labelSty}>Topic</label>
                <select value={selectedTopic ?? ""} onChange={e => { const v = parseInt(e.target.value); setSelectedTopic(isNaN(v) ? null : v) }} className={inputCls} style={inputSty}>
                  <option value="">-- Select topic --</option>
                  {topics.map((t: any) => <option key={t.id} value={t.id}>{t.name} ({t.durationHours}h, min {t.minGroupSize} agents)</option>)}
                </select>
              </div>
              <div style={{ flex:1, minWidth:140 }}>
                <label className={labelCls} style={labelSty}>Date From</label>
                <input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} className={inputCls} style={inputSty} />
              </div>
              <div style={{ flex:1, minWidth:140 }}>
                <label className={labelCls} style={labelSty}>Date To</label>
                <input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} className={inputCls} style={inputSty} />
              </div>
            </div>

            <label className={labelCls} style={labelSty}>Select Agents ({selectedAgents.length} selected)</label>
            <input value={empSearch} onChange={e => setEmpSearch(e.target.value)} placeholder="Search agents..." className={inputCls} style={{ ...inputSty, marginBottom:8 }} />
            <div style={{ maxHeight:220, overflowY:"auto", display:"flex", flexDirection:"column", gap:4 }}>
              {filteredEmps.map((e: any) => {
                const sel = selectedAgents.includes(e.employeeId)
                return (
                  <div key={e.employeeId} onClick={() => toggleAgent(e.employeeId)}
                    className={sel ? "bg-info-bg border border-info-bd" : "bg-sunken border border-line-subtle"}
                    style={{ display:"flex", alignItems:"center", gap:8, padding:"8px 10px", borderRadius:6, cursor:"pointer", fontSize:12 }}>
                    <span className={sel ? "text-info-fg" : "text-ink-soft"} style={{ fontSize:12 }}>{sel ? "✓" : "○"}</span>
                    <span className="text-ink" style={{ fontWeight:500 }}>{e.fullName}</span>
                    <span className="text-ink-soft" style={{ fontSize:10 }}>{e.teamLeadName}</span>
                    {e.engagement === "Student" && <span className="bg-holiday-bg border border-holiday-bd text-holiday-fg" style={{ fontSize:9, padding:"1px 5px", borderRadius:3, fontWeight:600 }}>STU</span>}
                  </div>
                )
              })}
            </div>

            <button onClick={suggest} disabled={!selectedTopic || suggesting}
              className={!selectedTopic ? "" : "bg-info-solid text-white"}
              style={{ marginTop:14, background: !selectedTopic ? "rgb(var(--st-info-solid) / 0.3)" : undefined, border:"none", color: !selectedTopic ? "#fff" : undefined, padding:"10px 20px", borderRadius:6, fontSize:12, cursor: !selectedTopic ? "not-allowed" : "pointer", fontWeight:600 }}>
              {suggesting ? "⏳ Analyzing shifts..." : "Find Available Slots"}
            </button>
          </div>

          {suggestError && (
            <div className="bg-crit-bg border border-crit-bd text-crit-fg rounded-md px-3.5 py-2.5 text-xs mb-2">
              <AlertTriangle size={11} className="inline align-text-bottom" /> {suggestError}
            </div>
          )}
          {warning && (
            <div className="bg-warn-bg border border-warn-bd text-warn-fg rounded-md px-3.5 py-2.5 text-xs mb-2">
              <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> {warning}
            </div>
          )}
          {hasSuggested && !suggesting && suggestions.length === 0 && !warning && !suggestError && (
            <div className="bg-raised border border-line-subtle text-ink-soft" style={{ borderRadius:8, padding:"32px 20px", textAlign:"center", fontSize:12 }}>
              No available slots found in this range. Try a wider date range or select different agents.
            </div>
          )}
          {suggestions.length > 0 && (
            <div>
              <div style={{ display:"flex", alignItems:"center", marginBottom:10 }}>
                <div className="text-ink-soft" style={{ fontSize:11, fontWeight:700, letterSpacing:".08em", textTransform:"uppercase" }}>Available Slots — {suggestions.length} found</div>
                <div style={{ marginLeft:"auto", display:"flex", gap:4 }}>
                  {(["list","calendar"] as const).map(v => (
                    <button key={v} onClick={() => setSlotView(v)}
                      className={slotView===v ? "bg-info-solid border border-info-bd text-white" : "bg-sunken border border-line-subtle text-ink-muted"}
                      style={{ borderRadius:5, padding:"4px 12px", fontSize:11, cursor:"pointer", fontWeight: slotView===v ? 600 : 400, textTransform:"capitalize" }}>{v}</button>
                  ))}
                </div>
              </div>
              {slotView === "list" && suggestions.map((s: any, i: number) => {
                const dt = new Date(s.date)
                const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                const label = `${dow} ${dt.getDate().toString().padStart(2,"0")}.${(dt.getMonth()+1).toString().padStart(2,"0")}.${dt.getFullYear()}`
                return (
                  <div key={i} className="bg-raised" style={{ border:"1px solid rgb(var(--line-subtle))", borderRadius:8, padding:16, marginBottom:12 }}>
                    <div className="text-ink" style={{ fontSize:14, fontWeight:700, marginBottom:8 }}>📅 {label} &nbsp; {s.startTime} – {s.endTime}</div>
                    <div style={{ display:"flex", alignItems:"center", gap:10, marginBottom:8 }}>
                      <div style={{ fontSize:13, fontWeight:600, color: s.coveragePct >= 80 ? "rgb(var(--st-good-fg))" : s.coveragePct >= 50 ? "rgb(var(--st-holiday-fg))" : "rgb(var(--st-crit-fg))" }}>{s.selectedAvailable} / {s.totalSelected} selected agents can attend</div>
                      <div className="text-ink-soft" style={{ fontSize:11 }}>(coverage {s.coveragePct}%)</div>
                      <div className="text-ink-soft" style={{ marginLeft:"auto", fontSize:10 }}>score {s.score}</div>
                    </div>
                    <div style={{ marginBottom:8 }}>
                      <div className="text-ink-soft" style={{ fontSize:10, marginBottom:3 }}>Production impact: {s.availableCount} of {s.totalOnDuty} on duty ({s.impactPct}%)</div>
                      <div className="bg-line-subtle" style={{ height:6, borderRadius:3, overflow:"hidden" }}>
                        <div style={{ width:`${s.impactPct}%`, height:"100%", background: s.impactPct < 25 ? "rgb(var(--st-good-solid))" : s.impactPct < 40 ? "rgb(var(--st-holiday-solid))" : "rgb(var(--st-crit-solid))", borderRadius:3 }} />
                      </div>
                    </div>
                    {s.selectedAttendees && s.selectedAttendees.length > 0 && (
                      <div style={{ display:"flex", flexWrap:"wrap", gap:4, marginBottom:6 }}>
                        {s.selectedAttendees.map((a: any) => (
                          <span key={a.employeeId} className="bg-good-bg border border-good-bd text-good-fg" style={{ fontSize:10, padding:"2px 8px", borderRadius:4 }}>✓ {a.fullName}</span>
                        ))}
                      </div>
                    )}
                    {s.missingSelected && s.missingSelected.length > 0 && (
                      <div style={{ display:"flex", flexWrap:"wrap", gap:4, marginBottom:8 }}>
                        {s.missingSelected.map((n: string, mi: number) => (
                          <span key={mi} className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ fontSize:10, padding:"2px 8px", borderRadius:4 }}>✖ {n}</span>
                        ))}
                      </div>
                    )}
                    {confirmingSlot === s ? (
                      <div className="bg-good-bg border border-good-bd" style={{ borderRadius:6, padding:12, marginTop:8 }}>
                        <div className="text-good-fg" style={{ fontSize:12, marginBottom:8 }}>Confirm training session? {label} {s.startTime}–{s.endTime} · {s.selectedAttendees.length} agents</div>
                        <div style={{ display:"flex", gap:8 }}>
                          <button onClick={() => setConfirmingSlot(null)} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer" }}>Cancel</button>
                          <button onClick={confirm} className="bg-good-solid text-white" style={{ padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Confirm & Create TRAINING entries</button>
                        </div>
                      </div>
                    ) : (
                      <button onClick={() => setConfirmingSlot(s)} className="bg-good-bg border border-good-bd text-good-fg" style={{ padding:"7px 16px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Select This Slot</button>
                    )}
                  </div>
                )
              })}
              {slotView === "calendar" && (() => {
                const byDay: any = {}
                suggestions.forEach((s: any) => { (byDay[s.date] = byDay[s.date] || []).push(s) })
                const days = Object.keys(byDay).sort()
                if (days.length === 0) return null
                const first = new Date(days[0])
                const year = first.getFullYear(), month = first.getMonth()
                const firstOfMonth = new Date(year, month, 1)
                const startOffset = (firstOfMonth.getDay() + 6) % 7
                const daysInMonth = new Date(year, month + 1, 0).getDate()
                const cells: any[] = []
                for (let i = 0; i < startOffset; i++) cells.push(null)
                for (let d = 1; d <= daysInMonth; d++) cells.push(d)
                const monthName = first.toLocaleString("en-US", { month: "long", year: "numeric" })
                const wd = ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"]
                return (
                  <div className="bg-raised" style={{ border:"1px solid rgb(var(--line-subtle))", borderRadius:8, padding:16 }}>
                    <div className="text-ink" style={{ fontSize:13, fontWeight:700, marginBottom:12, textAlign:"center" }}>{monthName}</div>
                    <div style={{ display:"grid", gridTemplateColumns:"repeat(7,1fr)", gap:6 }}>
                      {wd.map(d => <div key={d} className="text-ink-soft" style={{ fontSize:10, fontWeight:600, textAlign:"center", textTransform:"uppercase", paddingBottom:4 }}>{d}</div>)}
                      {cells.map((d, ci) => {
                        if (d === null) return <div key={"e"+ci} />
                        const dateStr = `${year}-${String(month+1).padStart(2,"0")}-${String(d).padStart(2,"0")}`
                        const slots = (byDay[dateStr] || []).slice().sort((a:any,b:any)=> b.score - a.score)
                        const best = slots[0]
                        return (
                          <div key={dateStr}
                            className={`border ${best ? "bg-sunken border-info-bd" : "border-line-subtle"}`}
                            style={{ minHeight:80, borderRadius:6, padding:6, display:"flex", flexDirection:"column", gap:3 }}>
                            <div className="text-ink-soft" style={{ fontSize:10, fontWeight:600 }}>{d}</div>
                            {slots.slice(0,3).map((s:any, si:number) => (
                              <div key={si} onClick={() => setConfirmingSlot(s)} title={`${s.selectedAvailable}/${s.totalSelected} can attend, impact ${s.impactPct}%`}
                                style={{ cursor:"pointer", fontSize:9, padding:"2px 4px", borderRadius:3,
                                  background: s.coveragePct >= 80 ? "rgb(var(--st-good-bg))" : s.coveragePct >= 50 ? "rgb(var(--st-holiday-bg))" : "rgb(var(--st-crit-bg))",
                                  color: s.coveragePct >= 80 ? "rgb(var(--st-good-fg))" : s.coveragePct >= 50 ? "rgb(var(--st-holiday-fg))" : "rgb(var(--st-crit-fg))",
                                  border:`1px solid ${s.coveragePct >= 80 ? "rgb(var(--st-good-bd))" : s.coveragePct >= 50 ? "rgb(var(--st-holiday-bd))" : "rgb(var(--st-crit-bd))"}`
                                }}>
                                {s.startTime} ({s.selectedAvailable}/{s.totalSelected})
                              </div>
                            ))}
                            {slots.length > 3 && <div className="text-ink-soft" style={{ fontSize:8 }}>+{slots.length-3} more</div>}
                          </div>
                        )
                      })}
                    </div>
                    {confirmingSlot && (
                      <div className="bg-good-bg border border-good-bd" style={{ marginTop:12, borderRadius:6, padding:12 }}>
                        <div className="text-good-fg" style={{ fontSize:12, marginBottom:8 }}>Confirm: {confirmingSlot.date} {confirmingSlot.startTime}–{confirmingSlot.endTime} · {confirmingSlot.selectedAttendees.length} agents</div>
                        <div style={{ display:"flex", gap:8 }}>
                          <button onClick={() => setConfirmingSlot(null)} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer" }}>Cancel</button>
                          <button onClick={confirm} className="bg-good-solid text-white" style={{ padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Confirm & Create TRAINING entries</button>
                        </div>
                      </div>
                    )}
                  </div>
                )
              })()}
            </div>
          )}
        </div>
      )}

      {/* SESSIONS TAB */}
      {tab === "sessions" && (
        <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead><tr>{["Topic","Date","Time","Agents","Status","Actions"].map(h => <th key={h} className={thCls} style={thSty}>{h}</th>)}</tr></thead>
            <tbody>
              {sessions.length === 0 && <tr><td colSpan={6} className={`${tdCls} text-ink-soft`} style={{ ...tdSty, textAlign:"center" }}>No training sessions scheduled.</td></tr>}
              {sessions.map((s: any) => {
                const dt = new Date(s.scheduledDate)
                const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                const scCls = s.status === "CONFIRMED" ? "bg-good-bg border border-good-bd text-good-fg"
                            : s.status === "CANCELLED" ? "bg-crit-bg border border-crit-bd text-crit-fg"
                            : "bg-neutralst-bg border border-neutralst-bd text-neutralst-fg"
                return (<>
                  <tr key={s.id} onClick={() => toggleSession(s)} className="cursor-pointer transition-colors hover:bg-hovered">
                    <td className={`${tdCls} text-ink`} style={{ ...tdSty, fontWeight:600 }}>{s.topicName}</td>
                    <td className={`${tdCls} text-ink font-mono`} style={{ ...tdSty, fontSize:11 }}>{dow} {dt.getDate().toString().padStart(2,"0")}.{(dt.getMonth()+1).toString().padStart(2,"0")}.{dt.getFullYear()}</td>
                    <td className={`${tdCls} text-ink font-mono`} style={{ ...tdSty, fontSize:11 }}>{s.startTime} – {s.endTime}</td>
                    <td className={`${tdCls} text-ink-muted`} style={{ ...tdSty, fontSize:11 }}>{expandedSession === s.id ? "▾" : "▸"} {s.agentIds?.length ?? 0} agents</td>
                    <td className={`${tdCls} text-ink`} style={tdSty}><span className={scCls} style={{ padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600 }}>{s.status}</span></td>
                    <td className={`${tdCls} text-ink`} style={tdSty}><button onClick={(e) => { e.stopPropagation(); deleteSession(s.id) }} className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Delete</button></td>
                  </tr>
                  {expandedSession === s.id && (
                    <tr key={s.id + "-detail"}>
                      <td colSpan={6} className="bg-sunken" style={{ padding:"0 12px 12px 12px" }}>
                        <div style={{ padding:12, display:"flex", flexDirection:"column", gap:6 }}>
                          <div className="text-ink-soft" style={{ fontSize:10, fontWeight:700, letterSpacing:".06em", textTransform:"uppercase", marginBottom:4 }}>Attendees ({s.agentIds?.length ?? 0})</div>
                          {(s.agentIds ?? []).map((id: string) => {
                            const emp = employees.find((e: any) => e.employeeId === id)
                            const dayShifts = sessionShifts[s.scheduledDate] ?? []
                            const sh = dayShifts.find((x: any) => x.employeeId === id)
                            const shiftLabel = sh ? (sh.shiftStart && sh.shiftEnd ? `${sh.shiftStart} – ${sh.shiftEnd}` : sh.shiftType) : "…"
                            const task = sh ? (sh.agentTask ?? null) : null
                            return (
                              <div key={id} className="bg-raised border border-line-subtle" style={{ display:"flex", alignItems:"center", gap:12, fontSize:12, padding:"6px 10px", borderRadius:6 }}>
                                <span className="text-ink" style={{ fontWeight:600, minWidth:180 }}>{emp?.fullName ?? id}</span>
                                <span className="text-ink-soft" style={{ fontSize:11, minWidth:160 }}>TL: {emp?.teamLeadName ?? "—"}</span>
                                <span className="font-mono text-info-fg" style={{ fontSize:11 }}>Shift that day: {shiftLabel}</span>
                                {task && <span
                                  className={`border ${task === "BACKLOG" ? "bg-neutralst-bg border-neutralst-bd text-neutralst-fg" : task === "TRAINING" ? "bg-learn-bg border-learn-bd text-learn-fg" : "bg-info-bg border-info-bd text-info-fg"}`}
                                  style={{ fontSize:9, fontWeight:600, padding:"1px 6px", borderRadius:3 }}>
                                  {task === "BACKLOG" ? "BACKLOG/VOICE" : task}
                                </span>}
                                {emp?.engagement === "Student" && <span className="bg-holiday-bg border border-holiday-bd text-holiday-fg" style={{ fontSize:9, padding:"1px 5px", borderRadius:3, fontWeight:600 }}>STU</span>}
                              </div>
                            )
                          })}
                        </div>
                      </td>
                    </tr>
                  )}
                  </>)
                })}
            </tbody>
          </table>
          <div className="font-mono text-ink-soft border-t border-line-subtle" style={{ padding:"8px 12px", fontSize:11 }}>{sessions.length} sessions</div>
        </div>
      )}

      {/* TOPICS TAB */}
      {tab === "topics" && (
        <div>
          <div style={{ display:"flex", justifyContent:"flex-end", marginBottom:12 }}>
            <button onClick={() => setShowTopicModal(true)} className="bg-info-solid text-white" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>+ New Topic</button>
          </div>
          <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead><tr>{["Name","Duration","Min Group","Max Group","Mandatory"].map(h => <th key={h} className={thCls} style={thSty}>{h}</th>)}</tr></thead>
              <tbody>
                {topics.map((t: any) => (
                  <tr key={t.id} className="transition-colors hover:bg-hovered">
                    <td className={`${tdCls} text-ink`} style={{ ...tdSty, fontWeight:600 }}>{t.name}</td>
                    <td className={`${tdCls} text-ink font-mono`} style={tdSty}>{t.durationHours}h</td>
                    <td className={`${tdCls} text-ink font-mono`} style={tdSty}>{t.minGroupSize}</td>
                    <td className={`${tdCls} text-ink font-mono`} style={tdSty}>{t.maxGroupSize}</td>
                    <td className={`${tdCls} text-ink`} style={tdSty}><span
                      className={`border ${t.isMandatory ? "bg-good-bg border-good-bd text-good-fg" : "bg-neutralst-bg border-neutralst-bd text-neutralst-fg"}`}
                      style={{ padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600 }}>{t.isMandatory ? "YES" : "NO"}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {showTopicModal && <TopicModal onClose={() => setShowTopicModal(false)} onSave={() => { setShowTopicModal(false); fetchTopics() }} />}
    </div>
  )
}
