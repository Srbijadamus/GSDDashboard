import { useState, useEffect } from "react"

const BASE = ""

const inputStyle: any = { background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text)", padding:"7px 10px", borderRadius:6, fontSize:12, width:"100%", fontFamily:"IBM Plex Sans", outline:"none" }
const labelStyle: any = { fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }

function TopicModal({ onClose, onSave }: any) {
  const [form, setForm] = useState({ name:"", durationHours:2, minGroupSize:3, maxGroupSize:15, isMandatory:false, notes:"" })
  const h = (k: string, v: any) => setForm(f => ({ ...f, [k]: v }))
  const save = async () => {
    await fetch(`${BASE}/api/training/topics`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ ...form, id:0 }) })
    onSave()
  }
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000, display:"flex", alignItems:"center", justifyContent:"center" }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:10, padding:24, width:460 }}>
        <h2 style={{ fontSize:15, fontWeight:600, color:"var(--text)", marginBottom:20 }}>New Training Topic</h2>
        <div style={{ display:"flex", flexDirection:"column", gap:12 }}>
          <div><label style={labelStyle}>Name</label><input value={form.name} onChange={e => h("name", e.target.value)} style={inputStyle} placeholder="Topic name..." /></div>
          <div style={{ display:"flex", gap:8 }}>
            <div style={{ flex:1 }}><label style={labelStyle}>Duration (h)</label><input type="number" min={1} max={8} value={form.durationHours} onChange={e => h("durationHours", Number(e.target.value))} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label style={labelStyle}>Min Group</label><input type="number" min={1} value={form.minGroupSize} onChange={e => h("minGroupSize", Number(e.target.value))} style={inputStyle} /></div>
            <div style={{ flex:1 }}><label style={labelStyle}>Max Group</label><input type="number" min={1} value={form.maxGroupSize} onChange={e => h("maxGroupSize", Number(e.target.value))} style={inputStyle} /></div>
          </div>
          <div>
            <label style={labelStyle}>Mandatory</label>
            <div style={{ display:"flex", gap:8 }}>
              {[true, false].map(v => (
                <button key={String(v)} onClick={() => h("isMandatory", v)} style={{ flex:1, padding:"7px 0", borderRadius:6, border:`1px solid ${form.isMandatory===v ? (v ? "#22c55e" : "#8892a4") : "var(--border)"}`, background: form.isMandatory===v ? (v ? "rgba(34,197,94,.15)" : "rgba(136,146,164,.15)") : "transparent", color: form.isMandatory===v ? (v ? "#22c55e" : "#8892a4") : "var(--text3)", cursor:"pointer", fontSize:12, fontWeight:600 }}>{v ? "YES" : "NO"}</button>
              ))}
            </div>
          </div>
          <div><label style={labelStyle}>Notes</label><textarea value={form.notes} onChange={(e: any) => h("notes", e.target.value)} rows={2} style={{ ...inputStyle, resize:"vertical" }} /></div>
        </div>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:20 }}>
          <button onClick={onClose} style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          <button onClick={save} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>Create Topic</button>
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
  const [confirmingSlot, setConfirmingSlot] = useState<any>(null)
  const [showTopicModal, setShowTopicModal] = useState(false)
  const [toast, setToast] = useState("")

  const fetchTopics = async () => {
    const r = await fetch(`${BASE}/api/training/topics`)
    const d = await r.json()
    setTopics(Array.isArray(d) ? d : [])
  }
  const fetchSessions = async () => { const r = await fetch(`${BASE}/api/training/sessions?from=${today}&to=${new Date(Date.now()+30*24*60*60*1000).toISOString().split("T")[0]}`); setSessions(await r.json()) }
  const fetchEmployees = async () => { const r = await fetch(`${BASE}/api/employees`); setEmployees(await r.json()) }

  useEffect(() => { fetchTopics(); fetchSessions(); fetchEmployees() }, [])

  const toggleAgent = (id: string) => setSelectedAgents(p => p.includes(id) ? p.filter(x => x !== id) : [...p, id])

  const suggest = async () => {
    if (!selectedTopic) return
    setSuggesting(true); setSuggestions([]); setWarning("")
    try {
      const r = await fetch(`${BASE}/api/training/suggest`, { method:"POST", headers:{"Content-Type":"application/json"}, body: JSON.stringify({ topicId: selectedTopic, dateFrom, dateTo, selectedAgentIds: selectedAgents, maxResults: 12 }) })
      const data = await r.json(); if (data && data.warning) setWarning(data.warning); else setWarning(""); const slots = Array.isArray(data) ? data : (data.slots ?? []); setSuggestions(slots)
    } catch {}
    setSuggesting(false)
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
    await fetch(`${BASE}/api/training/sessions/${id}`, { method:"DELETE" })
    fetchSessions()
  }

  const filteredEmps = employees.filter((e: any) => !empSearch || e.fullName?.toLowerCase().includes(empSearch.toLowerCase()) || e.teamLeadName?.toLowerCase().includes(empSearch.toLowerCase()))

  const thStyle: any = { padding:"10px 12px", fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", borderBottom:"1px solid var(--border)", background:"var(--card2)" }
  const tdStyle: any = { padding:"9px 12px", borderBottom:"1px solid rgba(30,45,69,.5)", fontSize:12, color:"var(--text)" }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      {toast && <div style={{ position:"fixed", bottom:24, right:24, background:"#22c55e", color:"#fff", padding:"10px 18px", borderRadius:8, fontSize:12, zIndex:9999, fontFamily:"IBM Plex Sans" }}>{toast}</div>}

      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>Training</h1>
      </div>

      <div style={{ display:"flex", gap:4 }}>
        {(["scheduler","sessions","topics"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} style={{ background: tab===t ? "var(--accent)" : "var(--card)", border:`1px solid ${tab===t ? "var(--accent)" : "var(--border)"}`, color: tab===t ? "#fff" : "var(--text2)", borderRadius:6, padding:"6px 16px", fontSize:12, cursor:"pointer", fontWeight: tab===t ? 600 : 400, textTransform:"capitalize" }}>{t}</button>
        ))}
      </div>

      {/* SCHEDULER TAB */}
      {tab === "scheduler" && (
        <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
          <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:16 }}>
            <div style={{ fontSize:11, fontWeight:700, color:"var(--text3)", letterSpacing:".08em", textTransform:"uppercase", marginBottom:14 }}>Training Scheduler</div>
            <div style={{ display:"flex", gap:12, flexWrap:"wrap", marginBottom:14 }}>
              <div style={{ flex:2, minWidth:200 }}>
                <label style={labelStyle}>Topic</label>
                <select value={selectedTopic ?? ""} onChange={e => { const v = parseInt(e.target.value); setSelectedTopic(isNaN(v) ? null : v) }} style={inputStyle}>
                  <option value="">-- Select topic --</option>
                  {topics.map((t: any) => <option key={t.id} value={t.id}>{t.name} ({t.durationHours}h, min {t.minGroupSize} agents)</option>)}
                </select>
              </div>
              <div style={{ flex:1, minWidth:140 }}>
                <label style={labelStyle}>Date From</label>
                <input type="date" value={dateFrom} onChange={e => setDateFrom(e.target.value)} style={inputStyle} />
              </div>
              <div style={{ flex:1, minWidth:140 }}>
                <label style={labelStyle}>Date To</label>
                <input type="date" value={dateTo} onChange={e => setDateTo(e.target.value)} style={inputStyle} />
              </div>
            </div>

            <label style={labelStyle}>Select Agents ({selectedAgents.length} selected)</label>
            <input value={empSearch} onChange={e => setEmpSearch(e.target.value)} placeholder="Search agents..." style={{ ...inputStyle, marginBottom:8 }} />
            <div style={{ maxHeight:220, overflowY:"auto", display:"flex", flexDirection:"column", gap:4 }}>
              {filteredEmps.map((e: any) => {
                const sel = selectedAgents.includes(e.employeeId)
                return (
                  <div key={e.employeeId} onClick={() => toggleAgent(e.employeeId)} style={{ display:"flex", alignItems:"center", gap:8, padding:"8px 10px", borderRadius:6, cursor:"pointer", fontSize:12, background: sel ? "rgba(59,126,255,.12)" : "var(--card2)", border:`1px solid ${sel ? "rgba(59,126,255,.3)" : "var(--border)"}` }}>
                    <span style={{ color: sel ? "var(--accent)" : "var(--text3)", fontSize:12 }}>{sel ? "✓" : "○"}</span>
                    <span style={{ fontWeight:500, color:"var(--text)" }}>{e.fullName}</span>
                    <span style={{ fontSize:10, color:"var(--text3)" }}>{e.teamLeadName}</span>
                    {e.engagement === "Student" && <span style={{ fontSize:9, background:"rgba(250,204,21,.15)", color:"#facc15", border:"1px solid rgba(250,204,21,.3)", padding:"1px 5px", borderRadius:3, fontWeight:600 }}>STU</span>}
                  </div>
                )
              })}
            </div>

            <button onClick={suggest} disabled={!selectedTopic || suggesting}
              style={{ marginTop:14, background: !selectedTopic ? "rgba(59,126,255,.3)" : "var(--accent)", border:"none", color:"#fff", padding:"10px 20px", borderRadius:6, fontSize:12, cursor: !selectedTopic ? "not-allowed" : "pointer", fontWeight:600 }}>
              {suggesting ? "⏳ Analyzing shifts..." : "Find Available Slots"}
            </button>
          </div>

          {warning && (
            <div style={{ background:"rgba(250,204,21,.1)", border:"1px solid #facc15", borderRadius:6, padding:"10px 14px", fontSize:12, color:"#facc15", marginBottom:8 }}>⚠ {warning}</div>
          )}
          {suggestions.length > 0 && (
            <div>
              <div style={{ display:"flex", alignItems:"center", marginBottom:10 }}>
                <div style={{ fontSize:11, fontWeight:700, color:"var(--text3)", letterSpacing:".08em", textTransform:"uppercase" }}>Available Slots — {suggestions.length} found</div>
                <div style={{ marginLeft:"auto", display:"flex", gap:4 }}>
                  {(["list","calendar"] as const).map(v => (
                    <button key={v} onClick={() => setSlotView(v)} style={{ background: slotView===v ? "var(--accent)" : "var(--card2)", border:`1px solid ${slotView===v ? "var(--accent)" : "var(--border)"}`, color: slotView===v ? "#fff" : "var(--text2)", borderRadius:5, padding:"4px 12px", fontSize:11, cursor:"pointer", fontWeight: slotView===v ? 600 : 400, textTransform:"capitalize" }}>{v}</button>
                  ))}
                </div>
              </div>
              {slotView === "list" && suggestions.map((s: any, i: number) => {
                const dt = new Date(s.date)
                const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                const label = `${dow} ${dt.getDate().toString().padStart(2,"0")}.${(dt.getMonth()+1).toString().padStart(2,"0")}.${dt.getFullYear()}`
                return (
                  <div key={i} style={{ background:"#181e2e", border:"1px solid rgba(255,255,255,.07)", borderRadius:8, padding:16, marginBottom:12 }}>
                    <div style={{ fontSize:14, fontWeight:700, color:"var(--text)", marginBottom:8 }}>📅 {label} &nbsp; {s.startTime} – {s.endTime}</div>
                    <div style={{ display:"flex", alignItems:"center", gap:10, marginBottom:8 }}>
                      <div style={{ fontSize:13, fontWeight:600, color: s.coveragePct >= 80 ? "#22c55e" : s.coveragePct >= 50 ? "#facc15" : "var(--danger)" }}>{s.selectedAvailable} / {s.totalSelected} selected agents can attend</div>
                      <div style={{ fontSize:11, color:"var(--text3)" }}>(coverage {s.coveragePct}%)</div>
                      <div style={{ marginLeft:"auto", fontSize:10, color:"var(--text3)" }}>score {s.score}</div>
                    </div>
                    <div style={{ marginBottom:8 }}>
                      <div style={{ fontSize:10, color:"var(--text3)", marginBottom:3 }}>Production impact: {s.availableCount} of {s.totalOnDuty} on duty ({s.impactPct}%)</div>
                      <div style={{ height:6, background:"var(--border)", borderRadius:3, overflow:"hidden" }}>
                        <div style={{ width:`${s.impactPct}%`, height:"100%", background: s.impactPct < 25 ? "#22c55e" : s.impactPct < 40 ? "#facc15" : "var(--danger)", borderRadius:3 }} />
                      </div>
                    </div>
                    {s.selectedAttendees && s.selectedAttendees.length > 0 && (
                      <div style={{ display:"flex", flexWrap:"wrap", gap:4, marginBottom:6 }}>
                        {s.selectedAttendees.map((a: any) => (
                          <span key={a.employeeId} style={{ fontSize:10, background:"rgba(34,197,94,.12)", border:"1px solid rgba(34,197,94,.3)", color:"#22c55e", padding:"2px 8px", borderRadius:4 }}>✓ {a.fullName}</span>
                        ))}
                      </div>
                    )}
                    {s.missingSelected && s.missingSelected.length > 0 && (
                      <div style={{ display:"flex", flexWrap:"wrap", gap:4, marginBottom:8 }}>
                        {s.missingSelected.map((n: string, mi: number) => (
                          <span key={mi} style={{ fontSize:10, background:"rgba(249,115,22,.1)", border:"1px solid rgba(249,115,22,.3)", color:"#f97316", padding:"2px 8px", borderRadius:4 }}>✖ {n}</span>
                        ))}
                      </div>
                    )}
                    {confirmingSlot === s ? (
                      <div style={{ background:"rgba(34,197,94,.08)", border:"1px solid rgba(34,197,94,.2)", borderRadius:6, padding:12, marginTop:8 }}>
                        <div style={{ fontSize:12, color:"#22c55e", marginBottom:8 }}>Confirm training session? {label} {s.startTime}–{s.endTime} · {s.selectedAttendees.length} agents</div>
                        <div style={{ display:"flex", gap:8 }}>
                          <button onClick={() => setConfirmingSlot(null)} style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer" }}>Cancel</button>
                          <button onClick={confirm} style={{ background:"#22c55e", border:"none", color:"#fff", padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Confirm & Create TRAINING entries</button>
                        </div>
                      </div>
                    ) : (
                      <button onClick={() => setConfirmingSlot(s)} style={{ background:"rgba(34,197,94,.15)", border:"1px solid #22c55e", color:"#22c55e", padding:"7px 16px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Select This Slot</button>
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
                const startOffset = (firstOfMonth.getDay() + 6) % 7 // Monday-first
                const daysInMonth = new Date(year, month + 1, 0).getDate()
                const cells: any[] = []
                for (let i = 0; i < startOffset; i++) cells.push(null)
                for (let d = 1; d <= daysInMonth; d++) cells.push(d)
                const monthName = first.toLocaleString("en-US", { month: "long", year: "numeric" })
                const wd = ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"]
                return (
                  <div style={{ background:"#181e2e", border:"1px solid rgba(255,255,255,.07)", borderRadius:8, padding:16 }}>
                    <div style={{ fontSize:13, fontWeight:700, color:"var(--text)", marginBottom:12, textAlign:"center" }}>{monthName}</div>
                    <div style={{ display:"grid", gridTemplateColumns:"repeat(7,1fr)", gap:6 }}>
                      {wd.map(d => <div key={d} style={{ fontSize:10, fontWeight:600, color:"var(--text3)", textAlign:"center", textTransform:"uppercase", paddingBottom:4 }}>{d}</div>)}
                      {cells.map((d, ci) => {
                        if (d === null) return <div key={"e"+ci} />
                        const dateStr = `${year}-${String(month+1).padStart(2,"0")}-${String(d).padStart(2,"0")}`
                        const slots = (byDay[dateStr] || []).slice().sort((a:any,b:any)=> b.score - a.score)
                        const best = slots[0]
                        return (
                          <div key={dateStr} style={{ minHeight:80, background: best ? "var(--card2)" : "transparent", border:`1px solid ${best ? "rgba(59,126,255,.2)" : "var(--border)"}`, borderRadius:6, padding:6, display:"flex", flexDirection:"column", gap:3 }}>
                            <div style={{ fontSize:10, color:"var(--text3)", fontWeight:600 }}>{d}</div>
                            {slots.slice(0,3).map((s:any, si:number) => (
                              <div key={si} onClick={() => setConfirmingSlot(s)} title={`${s.selectedAvailable}/${s.totalSelected} can attend, impact ${s.impactPct}%`} style={{ cursor:"pointer", fontSize:9, padding:"2px 4px", borderRadius:3, background: s.coveragePct >= 80 ? "rgba(34,197,94,.15)" : s.coveragePct >= 50 ? "rgba(250,204,21,.15)" : "rgba(255,59,92,.12)", color: s.coveragePct >= 80 ? "#22c55e" : s.coveragePct >= 50 ? "#facc15" : "var(--danger)", border:`1px solid ${s.coveragePct >= 80 ? "rgba(34,197,94,.3)" : s.coveragePct >= 50 ? "rgba(250,204,21,.3)" : "rgba(255,59,92,.3)"}` }}>
                                {s.startTime} ({s.selectedAvailable}/{s.totalSelected})
                              </div>
                            ))}
                            {slots.length > 3 && <div style={{ fontSize:8, color:"var(--text3)" }}>+{slots.length-3} more</div>}
                          </div>
                        )
                      })}
                    </div>
                    {confirmingSlot && (
                      <div style={{ marginTop:12, background:"rgba(34,197,94,.08)", border:"1px solid rgba(34,197,94,.2)", borderRadius:6, padding:12 }}>
                        <div style={{ fontSize:12, color:"#22c55e", marginBottom:8 }}>Confirm: {confirmingSlot.date} {confirmingSlot.startTime}–{confirmingSlot.endTime} · {confirmingSlot.selectedAttendees.length} agents</div>
                        <div style={{ display:"flex", gap:8 }}>
                          <button onClick={() => setConfirmingSlot(null)} style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)", padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer" }}>Cancel</button>
                          <button onClick={confirm} style={{ background:"#22c55e", border:"none", color:"#fff", padding:"6px 14px", borderRadius:5, fontSize:11, cursor:"pointer", fontWeight:600 }}>Confirm & Create TRAINING entries</button>
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
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead><tr>{["Topic","Date","Time","Agents","Status","Actions"].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
            <tbody>
              {sessions.length === 0 && <tr><td colSpan={6} style={{ ...tdStyle, textAlign:"center", color:"var(--text3)" }}>No training sessions scheduled.</td></tr>}
              {sessions.map((s: any) => {
                const dt = new Date(s.scheduledDate)
                const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
                const sc = s.status === "CONFIRMED" ? {bg:"rgba(34,197,94,.15)",border:"#22c55e",color:"#22c55e"} : s.status === "CANCELLED" ? {bg:"rgba(239,68,68,.15)",border:"#ef4444",color:"#ef4444"} : {bg:"rgba(136,146,164,.15)",border:"#8892a4",color:"#8892a4"}
                return (<>
                  <tr key={s.id} onClick={() => toggleSession(s)} style={{ cursor:"pointer" }} onMouseEnter={e => e.currentTarget.style.background="var(--card2)"} onMouseLeave={e => e.currentTarget.style.background="transparent"}>
                    <td style={{ ...tdStyle, fontWeight:600 }}>{s.topicName}</td>
                    <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11 }}>{dow} {dt.getDate().toString().padStart(2,"0")}.{(dt.getMonth()+1).toString().padStart(2,"0")}.{dt.getFullYear()}</td>
                    <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono", fontSize:11 }}>{s.startTime} – {s.endTime}</td>
                    <td style={{ ...tdStyle, fontSize:11, color:"var(--text2)" }}>{expandedSession === s.id ? "▾" : "▸"} {s.agentIds?.length ?? 0} agents</td>
                    <td style={tdStyle}><span style={{ ...sc, padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600, border:`1px solid ${sc.border}` }}>{s.status}</span></td>
                    <td style={tdStyle}><button onClick={(e) => { e.stopPropagation(); deleteSession(s.id) }} style={{ background:"rgba(255,59,92,.12)", border:"1px solid rgba(255,59,92,.2)", color:"var(--danger)", padding:"4px 8px", borderRadius:4, fontSize:10, cursor:"pointer" }}>Delete</button></td>
                  </tr>
                  {expandedSession === s.id && (
                    <tr key={s.id + "-detail"}>
                      <td colSpan={6} style={{ padding:"0 12px 12px 12px", background:"var(--card2)" }}>
                        <div style={{ padding:12, display:"flex", flexDirection:"column", gap:6 }}>
                          <div style={{ fontSize:10, fontWeight:700, color:"var(--text3)", letterSpacing:".06em", textTransform:"uppercase", marginBottom:4 }}>Attendees ({s.agentIds?.length ?? 0})</div>
                          {(s.agentIds ?? []).map((id: string) => {
                            const emp = employees.find((e: any) => e.employeeId === id)
                            const dayShifts = sessionShifts[s.scheduledDate] ?? []
                            const sh = dayShifts.find((x: any) => x.employeeId === id)
                            const shiftLabel = sh ? (sh.shiftStart && sh.shiftEnd ? `${sh.shiftStart} – ${sh.shiftEnd}` : sh.shiftType) : "…"
                            const task = sh ? (sh.agentTask ?? null) : null
                            return (
                              <div key={id} style={{ display:"flex", alignItems:"center", gap:12, fontSize:12, padding:"6px 10px", background:"var(--card)", borderRadius:6, border:"1px solid var(--border)" }}>
                                <span style={{ fontWeight:600, color:"var(--text)", minWidth:180 }}>{emp?.fullName ?? id}</span>
                                <span style={{ fontSize:11, color:"var(--text3)", minWidth:160 }}>TL: {emp?.teamLeadName ?? "—"}</span>
                                <span style={{ fontSize:11, fontFamily:"IBM Plex Mono", color:"#60a5fa" }}>Shift that day: {shiftLabel}</span>
                                {task && <span style={{ fontSize:9, fontWeight:600, padding:"1px 6px", borderRadius:3, background: task === "BACKLOG" ? "rgba(136,146,164,.15)" : task === "TRAINING" ? "rgba(168,85,247,.15)" : "rgba(59,126,255,.15)", color: task === "BACKLOG" ? "#8892a4" : task === "TRAINING" ? "#a855f7" : "#60a5fa", border:`1px solid ${task === "BACKLOG" ? "rgba(136,146,164,.3)" : task === "TRAINING" ? "rgba(168,85,247,.3)" : "rgba(59,126,255,.3)"}` }}>{task === "BACKLOG" ? "BACKLOG/VOICE" : task}</span>}
                                {emp?.engagement === "Student" && <span style={{ fontSize:9, background:"rgba(250,204,21,.15)", color:"#facc15", border:"1px solid rgba(250,204,21,.3)", padding:"1px 5px", borderRadius:3, fontWeight:600 }}>STU</span>}
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
          <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{sessions.length} sessions</div>
        </div>
      )}

      {/* TOPICS TAB */}
      {tab === "topics" && (
        <div>
          <div style={{ display:"flex", justifyContent:"flex-end", marginBottom:12 }}>
            <button onClick={() => setShowTopicModal(true)} style={{ background:"var(--accent)", border:"none", color:"#fff", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>+ New Topic</button>
          </div>
          <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
            <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
              <thead><tr>{["Name","Duration","Min Group","Max Group","Mandatory"].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr></thead>
              <tbody>
                {topics.map((t: any) => (
                  <tr key={t.id} onMouseEnter={e => e.currentTarget.style.background="var(--card2)"} onMouseLeave={e => e.currentTarget.style.background="transparent"}>
                    <td style={{ ...tdStyle, fontWeight:600 }}>{t.name}</td>
                    <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono" }}>{t.durationHours}h</td>
                    <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono" }}>{t.minGroupSize}</td>
                    <td style={{ ...tdStyle, fontFamily:"IBM Plex Mono" }}>{t.maxGroupSize}</td>
                    <td style={tdStyle}><span style={{ background: t.isMandatory ? "rgba(34,197,94,.15)" : "rgba(136,146,164,.15)", border:`1px solid ${t.isMandatory ? "#22c55e" : "#8892a4"}`, color: t.isMandatory ? "#22c55e" : "#8892a4", padding:"2px 8px", borderRadius:4, fontSize:10, fontWeight:600 }}>{t.isMandatory ? "YES" : "NO"}</span></td>
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









