import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useRef, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { MoreVertical, Pencil, Trash2, UserMinus, ChevronUp, ChevronDown } from "lucide-react"

// const READONLY_TYPES = ["OFF_WEEKEND", "PH"]
const OVERRIDE_CONFIRM_TYPES = ["OFF_WEEKEND", "PH"]
const TASKS = ["WIC", "Voice", "Backlog"]

const shiftColor = (type: string) => {
  const map: Record<string,{bg:string;color:string}> = {
    WORKING:     {bg:"rgba(34,208,122,.15)", color:"var(--green)"},
    WIC_DUTY:    {bg:"rgba(126,184,255,.15)",color:"#7eb8ff"},
    AL:          {bg:"rgba(59,126,255,.15)", color:"var(--accent)"},
    HALF_AL:     {bg:"rgba(59,126,255,.08)", color:"#93b4ff"},
    SL:          {bg:"rgba(255,124,59,.15)", color:"var(--warn)"},
    UL:          {bg:"rgba(255,59,92,.15)",  color:"var(--danger)"},
    TRAINING:    {bg:"rgba(167,139,250,.15)",color:"#a78bfa"},
    OFF:         {bg:"rgba(74,95,122,.12)",  color:"var(--text3)"},
    OFF_WEEKEND: {bg:"rgba(30,45,69,.4)",    color:"var(--text3)"},
    PH:          {bg:"rgba(250,204,21,.15)", color:"#facc15"},
    LPH:         {bg:"rgba(250,204,21,.08)", color:"#fde047"},
    CD:          {bg:"rgba(255,255,255,.05)",color:"var(--text2)"},
    CO:          {bg:"rgba(255,255,255,.05)",color:"var(--text2)"},
    RESIGNED:    {bg:"rgba(74,95,122,.2)",   color:"var(--text3)"},
    EMPTY:       {bg:"transparent",          color:"var(--text3)"},
  }
  return map[type] ?? map.EMPTY
}

const taskStyle = (task: string | null) => {
  if (task === "WIC")     return {bg:"rgba(126,184,255,.15)", color:"#7eb8ff", border:"rgba(126,184,255,.3)"}
  if (task === "Voice")   return {bg:"rgba(34,208,122,.15)",  color:"var(--green)", border:"rgba(34,208,122,.3)"}
  if (task === "Backlog") return {bg:"rgba(255,124,59,.15)",  color:"var(--warn)", border:"rgba(255,124,59,.3)"}
  return {bg:"rgba(255,59,92,.15)", color:"var(--danger)", border:"rgba(255,59,92,.3)"}
}

const SHIFT_TYPES = ["WORKING","WIC_DUTY","AL","HALF_AL","SL","UL","TRAINING","OFF","OFF_WEEKEND","PH","LPH","CD","CO","RESIGNED"]

function OverrideConfirmModal({ type, onConfirm, onCancel }: {
  type: string; onConfirm: () => void; onCancel: () => void
}) {
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:2000,
      display:"flex", alignItems:"center", justifyContent:"center" }}>
      <div style={{ background:"var(--card)", border:"1px solid rgba(250,204,21,.3)",
        borderRadius:10, padding:24, width:380 }}>
        <h2 style={{ fontSize:15, fontWeight:600, color:"#facc15", marginBottom:12 }}>⚠ Override Required</h2>
        <p style={{ fontSize:13, color:"var(--text2)", marginBottom:16 }}>
          <strong style={{ color:"var(--text)" }}>{type}</strong> is automatically set by the system.
          Are you sure you want to override?
        </p>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end" }}>
          <button onClick={onCancel} style={{ background:"var(--card2)", border:"1px solid var(--border)",
            color:"var(--text2)", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer" }}>
            Cancel
          </button>
          <button onClick={onConfirm} style={{ background:"#facc15", border:"none",
            color:"#000", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>
            Override
          </button>
        </div>
      </div>
    </div>
  )
}

function LocationPicker({ onSelect, onClose }: { onSelect: (locId: string, locName: string) => void; onClose: () => void }) {
  const { data: locations } = useQuery({ queryKey:["wic-locations"], queryFn: api.wic.locations })
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose() }
    document.addEventListener("mousedown", h)
    return () => document.removeEventListener("mousedown", h)
  }, [])

  const de = locations?.filter((l: any) => l.country === "DE") ?? []
  const nl = locations?.filter((l: any) => l.country === "NL") ?? []

  return (
    <div ref={ref} style={{
      position:"absolute", top:"100%", left:0, zIndex:500, minWidth:220,
      background:"var(--card2)", border:"1px solid var(--border)",
      borderRadius:6, padding:4, boxShadow:"0 8px 24px rgba(0,0,0,.4)",
      maxHeight:250, overflowY:"auto"
    }}>
      {de.length > 0 && <div style={{ padding:"4px 8px", fontSize:9, color:"var(--text3)", textTransform:"uppercase" }}>DE</div>}
      {de.map((l: any) => (
        <div key={l.locationCode} onClick={() => onSelect(l.locationCode, l.displayName)}
          style={{ padding:"5px 8px", borderRadius:4, cursor:"pointer", fontSize:11, color:"var(--text2)" }}
          onMouseEnter={e => (e.currentTarget.style.background = "rgba(255,255,255,.05)")}
          onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
          {l.displayName}
          {l.openingSchedule && <span style={{ marginLeft:4, fontSize:9, color:"var(--text3)" }}>({l.openingSchedule})</span>}
        </div>
      ))}
      {nl.length > 0 && <div style={{ padding:"4px 8px", fontSize:9, color:"var(--text3)", textTransform:"uppercase", marginTop:4 }}>NL</div>}
      {nl.map((l: any) => (
        <div key={l.locationCode} onClick={() => onSelect(l.locationCode, l.displayName)}
          style={{ padding:"5px 8px", borderRadius:4, cursor:"pointer", fontSize:11, color:"var(--text2)" }}
          onMouseEnter={e => (e.currentTarget.style.background = "rgba(255,255,255,.05)")}
          onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
          {l.displayName}
        </div>
      ))}
    </div>
  )
}

function TaskBadge({ shift, onTaskChange }: {
  shift: any
  onTaskChange: (id: number, task: string, locationId?: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [showLocPicker, setShowLocPicker] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) { setOpen(false); setShowLocPicker(false) } }
    document.addEventListener("mousedown", h)
    return () => document.removeEventListener("mousedown", h)
  }, [])

  const task = shift.agentTask ?? "Voice"
  const isUnassigned = task === "WIC" && !shift.locationId
  const ts = isUnassigned
    ? {bg:"rgba(255,59,92,.15)", color:"var(--danger)", border:"rgba(255,59,92,.3)"}
    : taskStyle(task)

  const cycleUp = () => {
    const idx = TASKS.indexOf(task)
    const next = TASKS[(idx - 1 + TASKS.length) % TASKS.length]
    if (next === "WIC") { setShowLocPicker(true); onTaskChange(shift.id, next) }
    else onTaskChange(shift.id, next)
  }

  const cycleDown = () => {
    const idx = TASKS.indexOf(task)
    const next = TASKS[(idx + 1) % TASKS.length]
    if (next === "WIC") { setShowLocPicker(true); onTaskChange(shift.id, next) }
    else onTaskChange(shift.id, next)
  }

  return (
    <div ref={ref} style={{ display:"flex", alignItems:"center", gap:2, position:"relative" }}>
      <button onClick={cycleUp} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer", padding:"1px 2px" }}>
        <ChevronUp size={11} />
      </button>

      <div onClick={() => setOpen(!open)} style={{
        ...ts, padding:"2px 7px", borderRadius:4, fontSize:10,
        fontFamily:"IBM Plex Mono", fontWeight:600, cursor:"pointer",
        border:`1px solid ${ts.border}`, whiteSpace:"nowrap"
      }}>
        {isUnassigned ? "⚠ WIC" : task}
      </div>

      <button onClick={cycleDown} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer", padding:"1px 2px" }}>
        <ChevronDown size={11} />
      </button>

      {open && (
        <div style={{
          position:"absolute", top:"100%", left:0, zIndex:300,
          background:"var(--card2)", border:"1px solid var(--border)",
          borderRadius:6, padding:4, minWidth:100,
          boxShadow:"0 8px 24px rgba(0,0,0,.4)"
        }}>
          {TASKS.map(t => {
            const ts2 = taskStyle(t)
            return (
              <div key={t} onClick={() => {
                if (t === "WIC") setShowLocPicker(true)
                onTaskChange(shift.id, t)
                setOpen(false)
              }} style={{
                ...ts2, padding:"5px 8px", borderRadius:4, cursor:"pointer",
                fontSize:11, fontFamily:"IBM Plex Mono", marginBottom:2,
                fontWeight: task === t ? 700 : 400,
                outline: task === t ? "1px solid currentColor" : "none"
              }}>{t}</div>
            )
          })}
        </div>
      )}

      {showLocPicker && (
        <LocationPicker
          onSelect={(locId, _locName) => {
            onTaskChange(shift.id, "WIC", locId)
            setShowLocPicker(false)
          }}
          onClose={() => setShowLocPicker(false)}
        />
      )}

      {isUnassigned && (
        <div style={{ position:"relative" }}>
          <button onClick={() => setShowLocPicker(true)} style={{
            background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.2)",
            color:"var(--danger)", padding:"1px 5px", borderRadius:4,
            fontSize:9, cursor:"pointer", fontFamily:"IBM Plex Mono", whiteSpace:"nowrap"
          }}>assign ▼</button>
          {showLocPicker && (
            <LocationPicker
              onSelect={(locId) => { onTaskChange(shift.id, "WIC", locId); setShowLocPicker(false) }}
              onClose={() => setShowLocPicker(false)}
            />
          )}
        </div>
      )}
    </div>
  )
}

function ShiftCell({ shift, onUpdate }: {
  shift: any
  onUpdate: (id: number, type: string, start?: string, end?: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [editStart, setEditStart] = useState(shift.shiftStart ?? "")
  const [editEnd,   setEditEnd]   = useState(shift.shiftEnd   ?? "")
  const [overrideType, setOverrideType] = useState<string | null>(null)
  const c = shiftColor(shift.shiftType)
  const showTime = (shift.shiftType === "WORKING" || shift.shiftType === "WIC_DUTY") && shift.shiftStart && shift.shiftEnd

  const handleTypeClick = (t: string) => {
    if (OVERRIDE_CONFIRM_TYPES.includes(shift.shiftType)) {
      setOverrideType(t); setOpen(false)
    } else {
      onUpdate(shift.id, t, editStart || undefined, editEnd || undefined)
      setOpen(false)
    }
  }

  return (
    <td style={{ padding:"3px 4px", position:"relative", minWidth:90 }}>
      <div onClick={() => setOpen(!open)} style={{
        ...c, padding:"3px 6px", borderRadius:4, fontSize:10,
        fontFamily:"IBM Plex Mono", fontWeight:600, cursor:"pointer",
        userSelect:"none", textAlign:"center"
      }}>
        {shift.shiftType === "EMPTY" ? "—" : shift.shiftType.replace("_"," ")}
        {showTime && (
          <div style={{ fontSize:9, opacity:.8, marginTop:1 }}>{shift.shiftStart}–{shift.shiftEnd}</div>
        )}
      </div>

      {open && (
        <div style={{
          position:"absolute", top:"100%", left:0, zIndex:100,
          background:"var(--card2)", border:"1px solid var(--border)",
          borderRadius:6, padding:6, minWidth:160,
          boxShadow:"0 8px 24px rgba(0,0,0,.4)"
        }}>
          {SHIFT_TYPES.map(t => {
            const tc = shiftColor(t)
            return (
              <div key={t} onClick={() => handleTypeClick(t)} style={{
                ...tc, padding:"4px 8px", borderRadius:4, marginBottom:2,
                fontSize:10, fontFamily:"IBM Plex Mono", cursor:"pointer",
                fontWeight: shift.shiftType === t ? 700 : 400,
                outline: shift.shiftType === t ? "1px solid currentColor" : "none"
              }}>{t.replace("_"," ")}</div>
            )
          })}
          {(shift.shiftType === "WORKING" || shift.shiftType === "WIC_DUTY") && (
            <div style={{ marginTop:6, borderTop:"1px solid var(--border)", paddingTop:6 }}>
              <div style={{ fontSize:9, color:"var(--text3)", marginBottom:4 }}>SHIFT TIME</div>
              <div style={{ display:"flex", gap:4 }}>
                <input value={editStart} onChange={e => setEditStart(e.target.value)}
                  placeholder="08:00" style={{ width:54, background:"var(--card)",
                    border:"1px solid var(--border)", color:"var(--text)",
                    padding:"3px 4px", borderRadius:4, fontSize:10,
                    fontFamily:"IBM Plex Mono", outline:"none" }} />
                <span style={{ color:"var(--text3)", fontSize:10, alignSelf:"center" }}>–</span>
                <input value={editEnd} onChange={e => setEditEnd(e.target.value)}
                  placeholder="17:00" style={{ width:54, background:"var(--card)",
                    border:"1px solid var(--border)", color:"var(--text)",
                    padding:"3px 4px", borderRadius:4, fontSize:10,
                    fontFamily:"IBM Plex Mono", outline:"none" }} />
              </div>
              <button onClick={() => { onUpdate(shift.id, shift.shiftType, editStart, editEnd); setOpen(false) }}
                style={{ marginTop:4, width:"100%", background:"var(--accent)", border:"none",
                  color:"#fff", padding:4, borderRadius:4, fontSize:10, cursor:"pointer" }}>
                Save Time
              </button>
            </div>
          )}
        </div>
      )}

      {overrideType && (
        <OverrideConfirmModal
          type={shift.shiftType}
          onConfirm={() => { onUpdate(shift.id, overrideType, editStart || undefined, editEnd || undefined); setOverrideType(null) }}
          onCancel={() => setOverrideType(null)}
        />
      )}
    </td>
  )
}

function ContextMenu({ emp: _emp, onEdit, onDelete, onRemove, onClose }: {
  emp: any; onEdit: () => void; onDelete: () => void; onRemove: () => void; onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) onClose() }
    document.addEventListener("mousedown", h)
    return () => document.removeEventListener("mousedown", h)
  }, [])

  return (
    <div ref={ref} style={{
      position:"absolute", right:0, top:"100%", zIndex:500,
      background:"var(--card2)", border:"1px solid var(--border)",
      borderRadius:6, padding:4, minWidth:160,
      boxShadow:"0 8px 24px rgba(0,0,0,.4)"
    }}>
      {[
        { icon:<Pencil size={12}/>, label:"Edit agent",         action:onEdit,   color:"var(--text2)" },
        { icon:<UserMinus size={12}/>, label:"Remove from plan", action:onRemove, color:"var(--warn)" },
        { icon:<Trash2 size={12}/>, label:"Delete agent",       action:onDelete, color:"var(--danger)" },
      ].map(item => (
        <div key={item.label} onClick={item.action} style={{
          display:"flex", alignItems:"center", gap:8,
          padding:"7px 10px", borderRadius:4, cursor:"pointer",
          fontSize:11, color:item.color
        }}
          onMouseEnter={e => (e.currentTarget.style.background = "rgba(255,255,255,.05)")}
          onMouseLeave={e => (e.currentTarget.style.background = "transparent")}>
          {item.icon} {item.label}
        </div>
      ))}
    </div>
  )
}

export default function Shifts() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const today = new Date()

  const [teamLead,   setTeamLead]   = useState("")
  const [role,       setRole]       = useState("")
  const [empType,    setEmpType]    = useState("")
  const [search,     setSearch]     = useState("")
  const [days,       setDays]       = useState(7)
  const [order,      setOrder]      = useState<string[]>([])
  const [contextEmp, setContextEmp] = useState<{emp:any;idx:number} | null>(null)

  const dragIdx = useRef<number | null>(null)
  const [dragOver, setDragOver] = useState<number | null>(null)

  const dates: string[] = []
  for (let i = 0; i < days; i++) {
    const d = new Date(today); d.setDate(d.getDate() + i)
    dates.push(d.toISOString().split("T")[0])
  }
  const from = dates[0]
  const to   = dates[dates.length - 1]

  const params = `from=${from}&to=${to}${teamLead ? "&teamLead="+teamLead : ""}${role ? "&role="+role : ""}`

  const { data: rows, isLoading } = useQuery({
    queryKey: ["shifts-cal", params],
    queryFn: () => api.shifts.get(params)
  })

  const byEmployee: Record<string, { emp:any; shifts:Record<string,any> }> = {}
  rows?.forEach((s: any) => {
    if (!byEmployee[s.employeeId]) {
      byEmployee[s.employeeId] = {
        emp: { id:s.employeeId, name:s.fullName, role:s.primaryRole, teamLead:s.teamLeadName, engagement:s.engagement },
        shifts: {}
      }
    }
    byEmployee[s.employeeId].shifts[s.shiftDate] = s
  })

  let empList = Object.values(byEmployee)
  if (search)  empList = empList.filter(({ emp }) => emp.name?.toLowerCase().includes(search.toLowerCase()) || emp.id?.toString().includes(search))
  if (empType) empList = empList.filter(({ emp }) => emp.engagement === empType)

  const orderedEmps = order.length > 0 ? order.map(id => byEmployee[id]).filter(Boolean) : empList

  const updateShift = async (id: number, type: string, start?: string, end?: string) => {
    await apiFetch(`/api/shifts/${id}`, {
      method:"PATCH", headers:{"Content-Type":"application/json"},
      body: JSON.stringify({ shiftType:type, shiftStart:start ?? null, shiftEnd:end ?? null })
    } as any)
    qc.invalidateQueries({ queryKey:["shifts-cal"] })
  }

  const updateTask = async (id: number, task: string, locationId?: string) => {
    await apiFetch(`/api/shifts/${id}`, {
      method:"PATCH", headers:{"Content-Type":"application/json"},
      body: JSON.stringify({
        agentTask: task,
        locationId: locationId ?? null,
        assignmentStatus: task === "WIC" && !locationId ? "UNASSIGNED" : "ASSIGNED"
      })
    } as any)
    qc.invalidateQueries({ queryKey:["shifts-cal"] })
  }

  const moveRow = (idx: number, dir: number) => {
    const ids = orderedEmps.map(e => e.emp.id)
    const newIds = [...ids]
    const swapIdx = idx + dir
    if (swapIdx < 0 || swapIdx >= newIds.length) return
    ;[newIds[idx], newIds[swapIdx]] = [newIds[swapIdx], newIds[idx]]
    setOrder(newIds)
  }

  const handleDrop = (toIdx: number) => {
    if (dragIdx.current === null || dragIdx.current === toIdx) { setDragOver(null); return }
    const ids = orderedEmps.map(e => e.emp.id)
    const newIds = [...ids]
    const [item] = newIds.splice(dragIdx.current, 1)
    newIds.splice(toIdx, 0, item)
    setOrder(newIds)
    dragIdx.current = null
    setDragOver(null)
    apiFetch("/api/shiftplan/reorder", {
      method:"PATCH", headers:{"Content-Type":"application/json"},
      body: JSON.stringify({ orderedEmployeeIds: newIds })
    } as any).catch(() => {})
  }

  const isWeekend = (d: string) => { const dt = new Date(d); return dt.getDay() === 0 || dt.getDay() === 6 }
  const isToday   = (d: string) => d === today.toISOString().split("T")[0]

  const dayLabel = (d: string) => {
    const dt = new Date(d)
    const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][dt.getDay()]
    return `${dow} ${dt.getDate().toString().padStart(2,"0")}.${(dt.getMonth()+1).toString().padStart(2,"0")}`
  }

  const inputStyle = { background:"var(--card)", border:"1px solid var(--border)", color:"var(--text)",
    padding:"6px 10px", borderRadius:6, fontSize:12, outline:"none", fontFamily:"IBM Plex Sans" }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:14 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.shifts")}</h1>
        <DownloadButtons onToday={api.shifts.downloadToday} on7Days={api.shifts.download7} on30Days={api.shifts.download30} />
      </div>

      <div style={{ display:"flex", gap:8, flexWrap:"wrap", alignItems:"center" }}>
        <input placeholder="Search name or ID..." value={search} onChange={e => setSearch(e.target.value)}
          style={{ ...inputStyle, width:180 }} />
        <input placeholder="Team Lead..." value={teamLead} onChange={e => setTeamLead(e.target.value)}
          style={{ ...inputStyle, width:150 }} />
        <select value={role} onChange={e => setRole(e.target.value)} style={inputStyle}>
          <option value="">All Roles</option>
          <option value="Voice">Voice</option>
          <option value="SSP">SSP</option>
          <option value="Chat">Chat</option>
          <option value="Dispatcher">Dispatcher</option>
          <option value="WIC">WIC</option>
        </select>
        <select value={empType} onChange={e => setEmpType(e.target.value)} style={inputStyle}>
          <option value="">All Types</option>
          <option value="Full Time">Full Time</option>
          <option value="Part-Time">Part-Time</option>
          <option value="Student">Student</option>
        </select>
        <div style={{ display:"flex", gap:4 }}>
          {[7, 14, 30].map(d => (
            <button key={d} onClick={() => setDays(d)} style={{
              ...inputStyle, cursor:"pointer",
              background: days === d ? "var(--accent)" : "var(--card)",
              color: days === d ? "#fff" : "var(--text2)",
              border: `1px solid ${days === d ? "var(--accent)" : "var(--border)"}`
            }}>{d}d</button>
          ))}
        </div>
      </div>

      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
        <div style={{ overflowX:"auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr style={{ background:"var(--card2)" }}>
                <th style={{ width:24, padding:"8px 4px", borderBottom:"1px solid var(--border)" }} />
                <th style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)",
                  borderBottom:"1px solid var(--border)", minWidth:130,
                  position:"sticky", left:0, background:"var(--card2)", zIndex:2 }}>Name</th>
                <th style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)",
                  borderBottom:"1px solid var(--border)", minWidth:70 }}>Role</th>
                <th style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)",
                  borderBottom:"1px solid var(--border)", minWidth:120 }}>Task</th>
                {dates.map(d => {
                  const we  = isWeekend(d)
                  const tod = isToday(d)
                  return (
                    <th key={d} style={{
                      padding:"8px 4px", textAlign:"center", fontSize:10, fontWeight:500,
                      color: tod ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)",
                      borderBottom:"1px solid var(--border)",
                      borderLeft:"1px solid rgba(30,45,69,.5)",
                      minWidth:90, whiteSpace:"nowrap",
                      background: tod ? "rgba(59,126,255,.06)" : we ? "rgba(30,45,69,.2)" : "transparent"
                    }}>
                      {dayLabel(d)}
                    </th>
                  )
                })}
                <th style={{ width:30, padding:"8px 4px", borderBottom:"1px solid var(--border)" }} />
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr><td colSpan={6 + dates.length} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>
                  Loading...
                </td></tr>
              )}
              {orderedEmps.map(({ emp, shifts }, idx) => {
                const todayShift = shifts[today.toISOString().split("T")[0]]
                return (
                  <tr key={emp.id}
                    draggable
                    onDragStart={() => { dragIdx.current = idx }}
                    onDragOver={e => { e.preventDefault(); setDragOver(idx) }}
                    onDrop={() => handleDrop(idx)}
                    style={{
                      borderBottom:"1px solid rgba(30,45,69,.5)",
                      background: dragOver === idx ? "rgba(59,126,255,.08)" : "transparent",
                      transition:"background .1s"
                    }}>
                    <td style={{ padding:4, textAlign:"center" }}>
                      <div style={{ display:"flex", flexDirection:"column", gap:1, alignItems:"center" }}>
                        <button onClick={() => moveRow(idx, -1)} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer", fontSize:9, padding:"1px 3px" }}>▲</button>
                        <span style={{ color:"var(--text3)", fontSize:10, cursor:"grab" }}>⠿</span>
                        <button onClick={() => moveRow(idx, 1)} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer", fontSize:9, padding:"1px 3px" }}>▼</button>
                      </div>
                    </td>
                    <td style={{ padding:"6px 10px", position:"sticky", left:0, background:"var(--card)", zIndex:1, borderRight:"1px solid rgba(30,45,69,.4)" }}>
                      <div style={{ fontWeight:500, fontSize:12 }}>{emp.name}</div>
                      <div style={{ fontFamily:"IBM Plex Mono", fontSize:9, color:"var(--text3)" }}>{emp.id}</div>
                    </td>
                    <td style={{ padding:"6px 10px", fontSize:11, fontFamily:"IBM Plex Mono", color:"var(--accent2)", whiteSpace:"nowrap" }}>{emp.role}</td>
                    <td style={{ padding:"6px 10px" }}>
                      {todayShift ? (
                        <TaskBadge shift={todayShift} onTaskChange={updateTask} />
                      ) : (
                        <span style={{ fontSize:10, color:"var(--text3)" }}>—</span>
                      )}
                    </td>
                    {dates.map(d => {
                      const s  = shifts[d]
                      const we = isWeekend(d)
                      const tod = isToday(d)
                      if (!s) return (
                        <td key={d} style={{ padding:"3px 4px", borderLeft:"1px solid rgba(30,45,69,.3)",
                          background: we ? "rgba(30,45,69,.2)" : tod ? "rgba(59,126,255,.03)" : "transparent" }}>
                          <div style={{ textAlign:"center", fontSize:9, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{we ? "WE" : "—"}</div>
                        </td>
                      )
                      return (
                        <td key={d} style={{ borderLeft:"1px solid rgba(30,45,69,.3)" }}>
                          <ShiftCell shift={s} onUpdate={updateShift} />
                        </td>
                      )
                    })}
                    <td style={{ padding:"4px 6px", position:"relative" }}>
                      <button onClick={() => setContextEmp(contextEmp?.emp.id === emp.id ? null : { emp, idx })}
                        style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer", padding:3, borderRadius:4 }}>
                        <MoreVertical size={14} />
                      </button>
                      {contextEmp?.emp.id === emp.id && (
                        <ContextMenu emp={emp} onClose={() => setContextEmp(null)}
                          onEdit={() => { window.location.href = "/employees"; setContextEmp(null) }}
                          onRemove={() => setContextEmp(null)}
                          onDelete={() => { window.location.href = "/employees"; setContextEmp(null) }}
                        />
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)", fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>
          {orderedEmps.length} agents · {dates.length} days
        </div>
      </div>
    </div>
  )
}



