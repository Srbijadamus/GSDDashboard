import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useRef, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
// @ts-ignore
import CoverageBar from "./CoverageBar"
import { MoreVertical, Pencil, Trash2, UserMinus, ChevronUp, ChevronDown, AlertTriangle } from "lucide-react"
import { maxFutureDateStr } from "../constants"
import { AddVacationModal } from "./Vacations"

const OVERRIDE_CONFIRM_TYPES = ["OFF_WEEKEND", "PH"]
const TASKS = ["WIC", "Voice", "Backlog"]

const shiftColor = (type: string) => {
  const map: Record<string,{background:string;color:string}> = {
    WORKING:     {background:"rgb(var(--st-good-bg))",    color:"rgb(var(--st-good-fg))"},
    WIC_DUTY:    {background:"rgb(var(--st-wic-bg))",     color:"rgb(var(--st-wic-fg))"},
    AL:          {background:"rgb(var(--st-info-bg))",    color:"rgb(var(--st-info-fg))"},
    HALF_AL:     {background:"rgb(var(--st-info-bg))",    color:"rgb(var(--st-info-fg))"},
    SL:          {background:"rgb(var(--st-warn-bg))",    color:"rgb(var(--st-warn-fg))"},
    UL:          {background:"rgb(var(--st-crit-bg))",    color:"rgb(var(--st-crit-fg))"},
    OL:          {background:"rgb(var(--surface-raised))",color:"rgb(var(--text-secondary))"},
    TRAINING:    {background:"rgb(var(--st-learn-bg))",   color:"rgb(var(--st-learn-fg))"},
    OFF:         {background:"rgb(var(--st-neutral-bg))", color:"rgb(var(--text-tertiary))"},
    OFF_WEEKEND: {background:"rgb(var(--surface-sunken))",color:"rgb(var(--text-tertiary))"},
    PH:          {background:"rgb(var(--st-holiday-bg))", color:"rgb(var(--st-holiday-fg))"},
    LPH:         {background:"rgb(var(--st-holiday-bg))", color:"rgb(var(--st-holiday-fg))"},
    CD:          {background:"rgb(var(--surface-raised))",color:"rgb(var(--text-secondary))"},
    CO:          {background:"rgb(var(--surface-raised))",color:"rgb(var(--text-secondary))"},
    RESIGNED:    {background:"rgb(var(--st-neutral-bg))", color:"rgb(var(--text-tertiary))"},
    EMPTY:       {background:"transparent",               color:"rgb(var(--text-tertiary))"},
  }
  return map[type] ?? map.EMPTY
}

const taskStyle = (task: string | null) => {
  if (task === "WIC")     return {background:"rgb(var(--st-wic-bg))",  color:"rgb(var(--st-wic-fg))",  border:"rgb(var(--st-wic-bd))"}
  if (task === "Voice")   return {background:"rgb(var(--st-good-bg))", color:"rgb(var(--st-good-fg))", border:"rgb(var(--st-good-bd))"}
  if (task === "Backlog") return {background:"rgb(var(--st-warn-bg))", color:"rgb(var(--st-warn-fg))", border:"rgb(var(--st-warn-bd))"}
  return {background:"rgb(var(--st-crit-bg))", color:"rgb(var(--st-crit-fg))", border:"rgb(var(--st-crit-bd))"}
}

const SHIFT_TYPES = ["WORKING","WIC_DUTY","AL","HALF_AL","SL","UL","OL","TRAINING","OFF","OFF_WEEKEND","PH","LPH","CD","CO","RESIGNED"]

function OverrideConfirmModal({ type, onConfirm, onCancel }: {
  type: string; onConfirm: () => void; onCancel: () => void
}) {
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:2000,
      display:"flex", alignItems:"center", justifyContent:"center" }}>
      <div className="bg-raised" style={{ border:"1px solid rgb(var(--st-holiday-bd))",
        borderRadius:10, padding:24, width:380 }}>
        <h2 className="text-holiday-fg" style={{ fontSize:15, fontWeight:600, marginBottom:12 }}>
          <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> Override Required
        </h2>
        <p className="text-ink-muted" style={{ fontSize:13, marginBottom:16 }}>
          <strong className="text-ink">{type}</strong> is automatically set by the system.
          Are you sure you want to override?
        </p>
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end" }}>
          <button onClick={onCancel} className="bg-sunken text-ink-muted border border-line-subtle"
            style={{ padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer" }}>
            Cancel
          </button>
          <button onClick={onConfirm} style={{ background:"rgb(var(--st-holiday-solid))", border:"none",
            color:"#000", padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>
            Override
          </button>
        </div>
      </div>
    </div>
  )
}

function LegalViolationModal({ violations, onClose, onConfirmAnyway }: {
  violations: any[]; onClose: () => void; onConfirmAnyway?: () => void
}) {
  const hardBlocks = violations.filter((v: any) => v.isHardBlock)
  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:3000, display:"flex", alignItems:"center", justifyContent:"center" }}>
      <div className="bg-raised" style={{ border:`1px solid ${hardBlocks.length > 0 ? "rgb(var(--st-crit-bd))" : "rgb(var(--st-holiday-bd))"}`, borderRadius:10, padding:24, width:480, maxHeight:"80vh", overflowY:"auto" }}>
        <h2 className={hardBlocks.length > 0 ? "text-crit-fg" : "text-holiday-fg"} style={{ fontSize:15, fontWeight:600, marginBottom:16 }}>
          {hardBlocks.length > 0 ? "⛔ Shift Validation Failed" : <><AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> Shift Warning</>}
        </h2>
        {violations.map((v: any, i: number) => (
          <div key={i} className={v.isHardBlock ? "bg-crit-bg border border-crit-bd" : "bg-warn-bg border border-warn-bd"}
            style={{ borderRadius:6, padding:"10px 14px", marginBottom:8 }}>
            <div style={{ display:"flex", justifyContent:"space-between", marginBottom:4 }}>
              <span className={`font-mono text-2xs font-bold ${v.isHardBlock ? "text-crit-fg" : "text-warn-fg"}`}>{v.rule}</span>
              <span className="font-mono text-ink-soft" style={{ fontSize:10 }}>{v.law}</span>
            </div>
            <div className="text-ink-muted" style={{ fontSize:12 }}>{v.description}</div>
          </div>
        ))}
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end", marginTop:16 }}>
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted" style={{ padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
          {hardBlocks.length === 0 && onConfirmAnyway && (
            <button onClick={onConfirmAnyway} style={{ background:"rgb(var(--st-holiday-solid))", border:"none", color:"#000", padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer", fontWeight:600 }}>Save Anyway</button>
          )}
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
    <div ref={ref} className="bg-sunken border border-line-subtle" style={{
      position:"absolute", top:"100%", left:0, zIndex:500, minWidth:220,
      borderRadius:6, padding:4, boxShadow:"0 8px 24px rgba(0,0,0,.4)",
      maxHeight:250, overflowY:"auto"
    }}>
      {de.length > 0 && <div className="text-ink-soft" style={{ padding:"4px 8px", fontSize:9, textTransform:"uppercase" }}>DE</div>}
      {de.map((l: any) => (
        <div key={l.locationCode} onClick={() => onSelect(l.locationCode, l.displayName)}
          className="text-ink-muted hover:bg-hovered"
          style={{ padding:"5px 8px", borderRadius:4, cursor:"pointer", fontSize:11 }}>
          {l.displayName}
          {l.openingSchedule && <span className="text-ink-soft" style={{ marginLeft:4, fontSize:9 }}>({l.openingSchedule})</span>}
        </div>
      ))}
      {nl.length > 0 && <div className="text-ink-soft" style={{ padding:"4px 8px", fontSize:9, textTransform:"uppercase", marginTop:4 }}>NL</div>}
      {nl.map((l: any) => (
        <div key={l.locationCode} onClick={() => onSelect(l.locationCode, l.displayName)}
          className="text-ink-muted hover:bg-hovered"
          style={{ padding:"5px 8px", borderRadius:4, cursor:"pointer", fontSize:11 }}>
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
    ? {background:"rgb(var(--st-crit-bg))", color:"rgb(var(--st-crit-fg))", border:"rgb(var(--st-crit-bd))"}
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
      <button onClick={cycleUp} className="text-ink-soft" style={{ background:"none", border:"none", cursor:"pointer", padding:"1px 2px" }}>
        <ChevronUp size={11} />
      </button>

      <div onClick={() => setOpen(!open)} className="font-mono"
        style={{ ...ts, padding:"2px 7px", borderRadius:4, fontSize:10,
          fontWeight:600, cursor:"pointer",
          border:`1px solid ${ts.border}`, whiteSpace:"nowrap"
        }}>
        {isUnassigned ? <><AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> WIC</> : task}
      </div>

      <button onClick={cycleDown} className="text-ink-soft" style={{ background:"none", border:"none", cursor:"pointer", padding:"1px 2px" }}>
        <ChevronDown size={11} />
      </button>

      {open && (
        <div className="bg-sunken border border-line-subtle" style={{
          position:"absolute", top:"100%", left:0, zIndex:300,
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
              }} className="font-mono" style={{
                ...ts2, padding:"5px 8px", borderRadius:4, cursor:"pointer",
                fontSize:11, marginBottom:2,
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
          <button onClick={() => setShowLocPicker(true)} className="bg-crit-bg border border-crit-bd text-crit-fg font-mono"
            style={{ padding:"1px 5px", borderRadius:4, fontSize:9, cursor:"pointer", whiteSpace:"nowrap" }}>assign ▼</button>
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

function ShiftCell({ shift, onUpdate, onSwapDone }: {
  shift: any
  onUpdate: (shift: any, type: string, start?: string, end?: string) => void
  onSwapDone?: () => void
}) {
  const [open, setOpen] = useState(false)
  const [editStart, setEditStart] = useState(shift.shiftStart ?? "")
  const [editEnd,   setEditEnd]   = useState(shift.shiftEnd   ?? "")
  const [overrideType, setOverrideType] = useState<string | null>(null)
  const [swapStep, setSwapStep] = useState<"idle"|"loading"|"warn"|"pick">("idle")
  const [wicEntries, setWicEntries] = useState<{supportLocation:string;workingShift:string}[]>([])
  const [swapSearch, setSwapSearch] = useState("")
  const [swapping, setSwapping] = useState(false)
  const c = shiftColor(shift.shiftType)
  const [, swapMM, swapDD] = (shift.shiftDate ?? "").split("-")
  const swapFmtDate = `${swapDD}.${swapMM}.`
  const showTime = (shift.shiftType === "WORKING" || shift.shiftType === "WIC_DUTY") && shift.shiftStart && shift.shiftEnd

  const { data: employees } = useQuery({
    queryKey: ["employees-all"],
    queryFn: () => api.employees.get("active=true"),
    enabled: swapStep === "pick",
    staleTime: 60000,
  })

  const handleTypeClick = (t: string) => {
    if (OVERRIDE_CONFIRM_TYPES.includes(shift.shiftType)) {
      setOverrideType(t); setOpen(false)
    } else {
      onUpdate(shift, t, editStart || undefined, editEnd || undefined)
      setOpen(false)
    }
  }

  const handleDelete = () => {
    if (!confirm("Delete this shift entry (set to empty)?")) return
    onUpdate(shift, "EMPTY")
    setOpen(false)
  }

  const startSwap = async () => {
    if (!shift.id) return
    setSwapStep("loading")
    setOpen(false)
    try {
      const res = await apiFetch<{entries:{supportLocation:string;workingShift:string}[]}>(`/api/shifts/${shift.id}/wic-entries`)
      const entries = res.entries ?? []
      setWicEntries(entries)
      setSwapStep(entries.length > 1 ? "warn" : "pick")
    } catch {
      setWicEntries([])
      setSwapStep("pick")
    }
  }

  const doSwap = async (newEmpId: string) => {
    if (!shift.id) return
    setSwapping(true)
    try {
      await apiFetch(`/api/shifts/${shift.id}/swap`, {
        method: "POST", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ newEmployeeId: newEmpId }),
      } as any)
      setSwapStep("idle"); setOpen(false)
      onSwapDone?.()
    } catch (e: any) {
      alert(e?.message ?? "Swap failed")
    } finally { setSwapping(false) }
  }

  const filteredEmps = (employees ?? [])
    .filter((e: any) => e.employeeId !== shift.employeeId)
    .filter((e: any) => !swapSearch ||
      e.fullName?.toLowerCase().includes(swapSearch.toLowerCase()) ||
      e.employeeId?.includes(swapSearch))
    .slice(0, 20)

  return (
    <td style={{ padding:"3px 4px", position:"relative", minWidth:90 }}>
      <div onClick={() => setOpen(!open)} className="font-mono"
        style={{ ...c, padding:"3px 6px", borderRadius:4, fontSize:10,
          fontWeight:600, cursor:"pointer",
          userSelect:"none", textAlign:"center"
        }}>
        {shift.shiftType === "EMPTY" ? "—" : shift.shiftType.replace("_"," ")}
        {showTime && (
          <div style={{ fontSize:9, opacity:.8, marginTop:1 }}>{shift.shiftStart}–{shift.shiftEnd}</div>
        )}
      </div>

      {open && (
        <div className="bg-sunken border border-line-subtle" style={{
          position:"absolute", top:"100%", left:0, zIndex:100,
          borderRadius:6, padding:6, minWidth:160,
          boxShadow:"0 8px 24px rgba(0,0,0,.4)"
        }}>
          {SHIFT_TYPES.map(t => {
            const tc = shiftColor(t)
            return (
              <div key={t} onClick={() => handleTypeClick(t)} className="font-mono" style={{
                ...tc, padding:"4px 8px", borderRadius:4, marginBottom:2,
                fontSize:10, cursor:"pointer",
                fontWeight: shift.shiftType === t ? 700 : 400,
                outline: shift.shiftType === t ? "1px solid currentColor" : "none"
              }}>{t.replace("_"," ")}</div>
            )
          })}
          {(shift.shiftType === "WORKING" || shift.shiftType === "WIC_DUTY") && (
            <div className="border-t border-line-subtle" style={{ marginTop:6, paddingTop:6 }}>
              <div className="text-ink-soft" style={{ fontSize:9, marginBottom:4 }}>SHIFT TIME</div>
              <div style={{ display:"flex", gap:4 }}>
                <input value={editStart} onChange={e => setEditStart(e.target.value)}
                  placeholder="08:00" className="bg-raised border border-line-subtle text-ink font-mono"
                  style={{ width:54, padding:"3px 4px", borderRadius:4, fontSize:10, outline:"none" }} />
                <span className="text-ink-soft" style={{ fontSize:10, alignSelf:"center" }}>–</span>
                <input value={editEnd} onChange={e => setEditEnd(e.target.value)}
                  placeholder="17:00" className="bg-raised border border-line-subtle text-ink font-mono"
                  style={{ width:54, padding:"3px 4px", borderRadius:4, fontSize:10, outline:"none" }} />
              </div>
              <button onClick={() => { onUpdate(shift, shift.shiftType, editStart, editEnd); setOpen(false) }}
                className="bg-info-solid text-white border-0"
                style={{ marginTop:4, width:"100%", padding:4, borderRadius:4, fontSize:10, cursor:"pointer" }}>
                Save Time
              </button>
            </div>
          )}
          {shift.id && (
            <div className="border-t border-line-subtle" style={{ marginTop:6, paddingTop:6, display:"flex", gap:4 }}>
              {shift.shiftType !== "EMPTY" && (
                <button onClick={handleDelete} className="bg-crit-bg border border-crit-bd text-crit-fg font-mono"
                  style={{ flex:1, padding:"4px 0", borderRadius:4, fontSize:9, cursor:"pointer" }}>Delete</button>
              )}
              {(shift.shiftType === "WIC_DUTY" || shift.shiftType === "WORKING") && (
                <button onClick={startSwap} className="bg-info-bg border border-info-bd text-info-fg font-mono"
                  style={{ flex:1, padding:"4px 0", borderRadius:4, fontSize:9, cursor:"pointer" }}>Swap</button>
              )}
            </div>
          )}
        </div>
      )}

      {swapStep !== "idle" && (
        <div className="bg-sunken border border-line-subtle" style={{
          position:"absolute", top:"100%", left:0, zIndex:200,
          borderRadius:6, padding:8, width:240,
          boxShadow:"0 8px 24px rgba(0,0,0,.4)"
        }}>
          {swapStep === "loading" && (
            <div className="text-ink-soft" style={{ fontSize:11, padding:"8px 4px", textAlign:"center" }}>Loading…</div>
          )}
          {swapStep === "warn" && (
            <div>
              <div className="text-warn-fg" style={{ fontSize:11, fontWeight:600, marginBottom:6 }}>
                <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> Dieser Agent deckt am {swapFmtDate} {wicEntries.length} Standorte ab: / This agent covers {wicEntries.length} locations on {swapFmtDate}:
              </div>
              <ul className="text-ink-muted" style={{ margin:"0 0 8px 0", paddingLeft:16, fontSize:11, lineHeight:1.6 }}>
                {wicEntries.map((entry, i) => (
                  <li key={i}>{entry.supportLocation}{entry.workingShift ? ` (${entry.workingShift})` : ""}</li>
                ))}
              </ul>
              <div className="text-ink-soft" style={{ fontSize:10, marginBottom:8 }}>
                Alle werden auf den ausgewählten Agenten übertragen. / All will be transferred to the selected agent.
              </div>
              <div style={{ display:"flex", gap:6 }}>
                <button onClick={() => setSwapStep("pick")} className="bg-info-solid text-white border-0"
                  style={{ flex:1, padding:"5px 0", borderRadius:4, fontSize:11, cursor:"pointer", fontWeight:600 }}>Weiter / Proceed</button>
                <button onClick={() => setSwapStep("idle")} className="bg-raised border border-line-subtle text-ink-muted"
                  style={{ padding:"5px 8px", borderRadius:4, fontSize:11, cursor:"pointer" }}>✕</button>
              </div>
            </div>
          )}
          {swapStep === "pick" && (
            <>
              <div className="text-ink-soft" style={{ fontSize:10, marginBottom:6 }}>Swap with agent:</div>
              <input autoFocus value={swapSearch} onChange={e => setSwapSearch(e.target.value)}
                placeholder="Search name or ID..."
                className="bg-raised border border-line-subtle text-ink"
                style={{ width:"100%", boxSizing:"border-box",
                  padding:"4px 7px", borderRadius:4, fontSize:11, outline:"none", marginBottom:4 }} />
              <div style={{ maxHeight:180, overflowY:"auto" }}>
                {filteredEmps.map((e: any) => (
                  <div key={e.employeeId} onClick={() => doSwap(e.employeeId)}
                    className="text-ink-muted hover:bg-hovered"
                    style={{ padding:"5px 8px", borderRadius:4, cursor: swapping ? "not-allowed" : "pointer",
                      fontSize:11, opacity: swapping ? 0.5 : 1 }}>
                    <div style={{ fontWeight:500 }}>{e.fullName}</div>
                    <div className="text-ink-soft font-mono" style={{ fontSize:9 }}>{e.employeeId}</div>
                  </div>
                ))}
                {filteredEmps.length === 0 && (
                  <div className="text-ink-soft" style={{ fontSize:11, padding:"6px 8px" }}>No matches</div>
                )}
              </div>
              <button onClick={() => setSwapStep("idle")} className="bg-raised border border-line-subtle text-ink-muted"
                style={{ marginTop:6, width:"100%", padding:"4px 0", borderRadius:4, fontSize:10, cursor:"pointer" }}>Cancel</button>
            </>
          )}
        </div>
      )}

      {overrideType && (
        <OverrideConfirmModal
          type={shift.shiftType}
          onConfirm={() => { onUpdate(shift, overrideType, editStart || undefined, editEnd || undefined); setOverrideType(null) }}
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
    <div ref={ref} className="bg-sunken border border-line-subtle" style={{
      position:"absolute", right:0, top:"100%", zIndex:500,
      borderRadius:6, padding:4, minWidth:160,
      boxShadow:"0 8px 24px rgba(0,0,0,.4)"
    }}>
      {[
        { icon:<Pencil size={12}/>, label:"Edit agent",         action:onEdit,   colorCls:"text-ink-muted" },
        { icon:<UserMinus size={12}/>, label:"Remove from plan", action:onRemove, colorCls:"text-warn-fg" },
        { icon:<Trash2 size={12}/>, label:"Delete agent",       action:onDelete, colorCls:"text-crit-fg" },
      ].map(item => (
        <div key={item.label} onClick={item.action}
          className={`flex items-center gap-2 rounded hover:bg-hovered cursor-pointer ${item.colorCls}`}
          style={{ padding:"7px 10px", fontSize:11 }}>
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
  const [startDate,  setStartDate]  = useState(today.toISOString().split("T")[0])
  const [order,      setOrder]      = useState<string[]>([])
  const [contextEmp, setContextEmp] = useState<{emp:any;idx:number} | null>(null)

  const dragIdx = useRef<number | null>(null)
  const [dragOver, setDragOver] = useState<number | null>(null)
  useEffect(() => {
    const bot = document.getElementById("shifts-scroll-bot")
    const topInner = document.getElementById("shifts-scroll-top-inner")
    if (!bot || !topInner) return
    const table = bot.querySelector("table") as HTMLElement
    if (table) topInner.style.width = table.scrollWidth + "px"
  })
  const [legalModal, setLegalModal] = useState<{violations:any[]; pendingUpdate:()=>void} | null>(null)
  const [assignError, setAssignError] = useState<string | null>(null)
  const [showAlModal, setShowAlModal] = useState(false)

  const dates: string[] = []
  for (let i = 0; i < days; i++) {
    const d = new Date(startDate); d.setDate(d.getDate() + i)
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

  const updateShift = async (shift: any, type: string, start?: string, end?: string) => {
    if (!shift.id) {
      try {
        await doAssignShift(shift.employeeId, shift.shiftDate, type, start, end)
      } catch (e: any) {
        setAssignError(e?.message ?? String(e))
      }
      return
    }
    try {
      const vRes = await fetch(`/api/shifts/validate`, {
        method:"POST", headers:{"Content-Type":"application/json"},
        body: JSON.stringify({ shiftId: shift.id, shiftType: type, shiftStart: start ?? null, shiftEnd: end ?? null })
      })
      const vData = await vRes.json()
      if (!vData.valid || vData.violations?.length > 0) {
        setLegalModal({ violations: vData.violations, pendingUpdate: () => doUpdateShift(shift.id, type, start, end) })
        return
      }
    } catch {}
    await doUpdateShift(shift.id, type, start, end)
  }
  const doUpdateShift = async (id: number, type: string, start?: string, end?: string) => {
    await apiFetch(`/api/shifts/${id}`, {
      method:"PATCH", headers:{"Content-Type":"application/json"},
      body: JSON.stringify({ shiftType:type, shiftStart:start ?? null, shiftEnd:end ?? null })
    } as any)
    qc.invalidateQueries({ queryKey:["shifts-cal"] })
  }
  const doAssignShift = async (employeeId: string, shiftDate: string, type: string, start?: string, end?: string) => {
    const res = await fetch("/api/shifts/assign", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ employeeId, shiftDate, shiftType: type, shiftStart: start ?? null, shiftEnd: end ?? null }),
    })
    if (!res.ok) {
      const body = await res.json().catch(() => ({}))
      throw new Error(body?.error ?? `HTTP ${res.status}`)
    }
    qc.invalidateQueries({ queryKey: ["shifts-cal"] })
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

  const inputStyle = { background:"rgb(var(--surface-raised))", border:"1px solid rgb(var(--border-subtle))", color:"rgb(var(--text-primary))",
    padding:"6px 10px", borderRadius:6, fontSize:12, outline:"none", fontFamily:"IBM Plex Sans" }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:14 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 className="text-ink" style={{ fontSize:22, fontWeight:600 }}>{t("nav.shifts")}</h1>
        <div style={{ display:"flex", gap:8, alignItems:"center" }}>
          <button onClick={() => setShowAlModal(true)} className="bg-info-solid text-white border-0"
            style={{ padding:"7px 14px", borderRadius:6, fontSize:12, fontWeight:600,
              cursor:"pointer", display:"flex", alignItems:"center", gap:5 }}>
            {t("vacations.addAl")}
          </button>
          <DownloadButtons onToday={api.shifts.downloadToday} on7Days={api.shifts.download7} on30Days={api.shifts.download30} />
        </div>
      </div>
      {assignError && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg"
          style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <span>{assignError}</span>
          <button onClick={() => setAssignError(null)} className="text-crit-fg" style={{ background: "none", border: "none", cursor: "pointer", fontSize: 16, lineHeight: 1, padding: "0 4px" }}>×</button>
        </div>
      )}
      <CoverageBar date={from} />

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
            <button key={d} onClick={() => setDays(d)}
              className={days === d ? "text-white" : "text-ink-muted"}
              style={{
                ...inputStyle, cursor:"pointer",
                background: days === d ? "rgb(var(--st-info-solid))" : "rgb(var(--surface-raised))",
                border: `1px solid ${days === d ? "rgb(var(--st-info-solid))" : "rgb(var(--border-subtle))"}`
              }}>{d}d</button>
          ))}
        </div>
        <input type="date" value={startDate} max={maxFutureDateStr()}
          onChange={e => setStartDate(e.target.value)} style={inputStyle} />
        {startDate !== today.toISOString().split("T")[0] && (
          <button onClick={() => setStartDate(today.toISOString().split("T")[0])}
            style={{ ...inputStyle, cursor:"pointer", color:"rgb(var(--text-secondary))" }}>Today</button>
        )}
      </div>

      <div className="bg-raised border border-line-subtle" style={{ borderRadius:8 }}>
        <div style={{ overflowX:"auto", direction:"rtl" }} onScroll={e => { const el = e.currentTarget; el.querySelectorAll("[data-scroll-sync]").forEach((s: any) => { if (s !== el) s.scrollLeft = el.scrollLeft }) }}>
        <div style={{ direction:"ltr" }}>
        </div>
      </div>
      <div id="shifts-scroll-top" style={{ overflowX:"auto", height:12, marginBottom:2 }} onScroll={e => { const b = document.getElementById("shifts-scroll-bot"); if(b) b.scrollLeft = e.currentTarget.scrollLeft }}>
        <div id="shifts-scroll-top-inner" style={{ height:1 }} />
      </div>
      <div id="shifts-scroll-bot" style={{ overflowX:"auto" }} onScroll={e => { const t = document.getElementById("shifts-scroll-top"); if(t) t.scrollLeft = e.currentTarget.scrollLeft }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr className="bg-sunken">
                <th className="border-b border-line-subtle" style={{ width:24, padding:"8px 4px" }} />
                <th className="text-ink-soft border-b border-line-subtle bg-sunken" style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em",
                  minWidth:130,
                  position:"sticky", left:0, zIndex:2 }}>Name</th>
                <th className="text-ink-soft border-b border-line-subtle" style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em",
                  minWidth:70 }}>Role</th>
                <th className="text-ink-soft border-b border-line-subtle" style={{ padding:"8px 10px", textAlign:"left", fontSize:10, fontWeight:500,
                  textTransform:"uppercase", letterSpacing:".07em",
                  minWidth:120 }}>Task</th>
                {dates.map(d => {
                  const we  = isWeekend(d)
                  const tod = isToday(d)
                  return (
                    <th key={d} className="border-b border-line-subtle" style={{
                      padding:"8px 4px", textAlign:"center", fontSize:10, fontWeight:500,
                      color: tod ? "rgb(var(--st-info-fg))" : we ? "rgb(var(--text-tertiary))" : "rgb(var(--text-secondary))",
                      borderLeft:"1px solid rgb(var(--border-subtle))",
                      minWidth:90, whiteSpace:"nowrap",
                      background: tod ? "rgb(var(--st-info-bg))" : we ? "rgb(var(--surface-sunken))" : "transparent"
                    }}>
                      {dayLabel(d)}
                    </th>
                  )
                })}
                <th className="border-b border-line-subtle" style={{ width:30, padding:"8px 4px" }} />
              </tr>
            </thead>
            <tbody>
              {isLoading && (
                <tr><td colSpan={6 + dates.length} className="text-ink-soft" style={{ padding:24, textAlign:"center" }}>
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
                      borderBottom:"1px solid rgb(var(--border-subtle))",
                      background: dragOver === idx ? "rgb(var(--st-info-bg))" : "transparent",
                      transition:"background .1s"
                    }}>
                    <td style={{ padding:4, textAlign:"center" }}>
                      <div style={{ display:"flex", flexDirection:"column", gap:1, alignItems:"center" }}>
                        <button onClick={() => moveRow(idx, -1)} className="text-ink-soft" style={{ background:"none", border:"none", cursor:"pointer", fontSize:9, padding:"1px 3px" }}>▲</button>
                        <span className="text-ink-soft" style={{ fontSize:10, cursor:"grab" }}>⠿</span>
                        <button onClick={() => moveRow(idx, 1)} className="text-ink-soft" style={{ background:"none", border:"none", cursor:"pointer", fontSize:9, padding:"1px 3px" }}>▼</button>
                      </div>
                    </td>
                    <td className="bg-raised" style={{ padding:"6px 10px", position:"sticky", left:0, zIndex:1, borderRight:"1px solid rgb(var(--border-subtle))" }}>
                      <div style={{ fontWeight:500, fontSize:12 }}>{emp.name}</div>
                      <div className="font-mono text-ink-soft" style={{ fontSize:9 }}>{emp.id}</div>
                    </td>
                    {(() => {
                      const absentTypes = new Set(["AL", "HALF_AL", "SL", "UL"])
                      const isAbsent = !!todayShift && absentTypes.has(todayShift.shiftType)
                      return (
                        <>
                          <td className={`font-mono ${isAbsent ? "text-ink-disabled opacity-50" : "text-wic-fg"}`} style={{ padding:"6px 10px", fontSize:11, whiteSpace:"nowrap" }}>{emp.role}</td>
                          <td style={{ padding:"6px 10px" }}>
                            {todayShift ? (
                              <div className={isAbsent ? "pointer-events-none" : ""}>
                                <TaskBadge shift={todayShift} onTaskChange={updateTask} />
                              </div>
                            ) : (
                              <span className="text-ink-soft" style={{ fontSize:10 }}>—</span>
                            )}
                          </td>
                        </>
                      )
                    })()}
                    {dates.map(d => {
                      const s  = shifts[d]
                      const we = isWeekend(d)
                      const tod = isToday(d)
                      if (!s && we) return (
                        <td key={d} style={{ padding:"3px 4px", borderLeft:"1px solid rgb(var(--border-subtle))",
                          background: "rgb(var(--surface-sunken))" }}>
                          <div className="text-ink-soft font-mono" style={{ textAlign:"center", fontSize:9 }}>WE</div>
                        </td>
                      )
                      const cellShift = s ?? { id: null, employeeId: emp.id, shiftDate: d, shiftType: "EMPTY", shiftStart: null, shiftEnd: null }
                      return (
                        <td key={d} style={{ borderLeft:"1px solid rgb(var(--border-subtle))",
                          background: !s && tod ? "rgb(var(--st-info-bg))" : "transparent" }}>
                          <ShiftCell shift={cellShift} onUpdate={updateShift} onSwapDone={() => qc.invalidateQueries({ queryKey:["shifts-cal"] })} />
                        </td>
                      )
                    })}
                    <td style={{ padding:"4px 6px", position:"relative" }}>
                      <button onClick={() => setContextEmp(contextEmp?.emp.id === emp.id ? null : { emp, idx })}
                        className="text-ink-soft" style={{ background:"none", border:"none", cursor:"pointer", padding:3, borderRadius:4 }}>
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
        <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding:"8px 12px", fontSize:11 }}>
          {orderedEmps.length} agents · {dates.length} days
        </div>
      </div>
      {legalModal && (
        <LegalViolationModal
          violations={legalModal.violations}
          onClose={() => setLegalModal(null)}
          onConfirmAnyway={legalModal.violations.every((v:any) => !v.isHardBlock) ? () => { legalModal.pendingUpdate(); setLegalModal(null) } : undefined}
        />
      )}
      {showAlModal && (
        <AddVacationModal
          onClose={() => setShowAlModal(false)}
          initialDate={today.toISOString().split("T")[0]}
          onSave={async (form: any) => {
            try {
              await apiFetch("/api/vacations", {
                method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(form)
              } as any)
              qc.invalidateQueries({ queryKey: ["shifts-cal"] })
              qc.invalidateQueries({ queryKey: ["vacations"] })
              qc.invalidateQueries({ queryKey: ["albalance"] })
            } catch {}
            setShowAlModal(false)
          }}
        />
      )}
    </div>
  )
}


