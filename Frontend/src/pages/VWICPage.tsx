import { useState } from "react"
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, X, Check } from "lucide-react"

// ─── Types ────────────────────────────────────────────────────────────────────

interface VwicAgent {
  employeeId:     string
  fullName:       string | null
  teamLeadName:   string | null
  role:           "Main" | "Backup"
  shiftType:      string | null
  shiftStart:     string | null
  shiftEnd:       string | null
  isAbsent:       boolean
  absenceType:    string | null
  isVwicAssigned: boolean
}

interface VwicTimelineSlot {
  hour:         number
  label:        string
  mainAgents:   string[]
  backupAgents: string[]
  hasMainAgent: boolean   // true when mainAgents.length >= minRequired
  hasAnyAgent:  boolean
  minRequired:  number
}

interface VwicDailyResponse {
  date:         string
  agents:       VwicAgent[]
  timeline:     VwicTimelineSlot[]
  gaps:         number[]
  coveredHours: number
  totalHours:   number
}

interface VwicCandidate {
  employeeId:     string
  fullName:       string | null
  teamLeadName:   string | null
  primaryRole:    string | null
  shiftType:      string | null
  shiftStart:     string | null
  shiftEnd:       string | null
  isWorkingToday: boolean
}

// ─── Rotation Plan types ─────────────────────────────────────────────────────

interface VwicRotationScheduleRow {
  employeeId: string
  fullName:   string
  slotStatus: string[]  // "ON" | "HANDOVER" | "OFF"
}

interface VwicCoverageProofItem {
  startTime:  string
  endTime:    string
  agentCount: number
  required:   number
  covered:    boolean
}

interface VwicFairnessItem {
  employeeId: string
  fullName:   string
  vwicHours:  number
  slotCount:  number
}

interface VwicRotationResponse {
  date:                string
  slotLabels:          string[]
  schedule:            VwicRotationScheduleRow[]
  coverageProof:       VwicCoverageProofItem[]
  fairness:            VwicFairnessItem[]
  recommendation:      string
  fallbackWarning:     string | null
  availableAgents:     number
  requiredAgentHours:  number
  availableAgentHours: number
}

interface VwicWeekDayPlan {
  date:                string
  dayName:             string
  dayShort:            string
  slotLabels:          string[]
  schedule:            VwicRotationScheduleRow[]
  coverageProof:       VwicCoverageProofItem[]
  dailyFairness:       VwicFairnessItem[]
  recommendation:      string
  fallbackWarning:     string | null
  availableAgents:     number
  requiredAgentHours:  number
  availableAgentHours: number
}

interface VwicWeekFairnessItem {
  employeeId:     string
  fullName:       string
  totalVwicHours: number
  totalSlotCount: number
  daysWorked:     number
}

interface VwicWeekResponse {
  weekStartDate:  string
  days:           VwicWeekDayPlan[]
  weeklyFairness: VwicWeekFairnessItem[]
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function parseDecimalHour(t: string | null): number | null {
  if (!t) return null
  const [h, m] = t.split(":").map(Number)
  return h + (m || 0) / 60
}

// ─── Assign modal ─────────────────────────────────────────────────────────────

function AssignSlotModal({
  slot, date, onClose, onAssigned,
}: {
  slot:       VwicTimelineSlot
  date:       string
  onClose:    () => void
  onAssigned: () => void
}) {
  const [saving, setSaving] = useState<string | null>(null)
  const [error,  setError]  = useState<string | null>(null)

  const { data: candidates, isLoading } = useQuery<VwicCandidate[]>({
    queryKey: ["vwic-candidates", date],
    queryFn:  () => fetch(`/api/vwic/candidates?date=${date}`).then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
  })

  const alreadyInSlot = new Set([...slot.mainAgents, ...slot.backupAgents])

  const candidateCoversHour = (c: VwicCandidate, hour: number): boolean => {
    const start = parseDecimalHour(c.shiftStart)
    const end   = parseDecimalHour(c.shiftEnd)
    if (start === null || end === null) return false
    if (end >= start) return start < hour + 1 && end > hour
    return start < hour + 1 || end > hour
  }

  const sorted = (candidates ?? [])
    .filter(c => !alreadyInSlot.has(c.fullName ?? c.employeeId))
    .sort((a, b) => {
      const aC = candidateCoversHour(a, slot.hour) ? 0 : 1
      const bC = candidateCoversHour(b, slot.hour) ? 0 : 1
      return aC - bC || (a.fullName ?? "").localeCompare(b.fullName ?? "")
    })

  const assign = async (c: VwicCandidate) => {
    setSaving(c.employeeId)
    setError(null)
    try {
      const res = await fetch("/api/vwic/assign", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId: c.employeeId,
          date,
          hour:       slot.hour,
          shiftStart: c.shiftStart ?? "07:00",
          shiftEnd:   c.shiftEnd   ?? "17:00",
        }),
      })
      if (!res.ok) throw new Error("Server error")
      onAssigned()
    } catch {
      setError("Assignment failed — please try again")
    } finally {
      setSaving(null)
    }
  }

  const slotLabel = `${slot.label} – ${String(slot.hour + 1).padStart(2, "0")}:00`
  const isOrange  = slot.hasAnyAgent && !slot.hasMainAgent

  return (
    <div
      className="fixed inset-0 bg-black/55 z-[2000] flex items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-raised border border-line-subtle rounded-lg w-[460px] max-h-[80vh] flex flex-col overflow-hidden"
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-5 py-4 border-b border-line-subtle flex justify-between items-start">
          <div>
            <div style={{ fontWeight: 700, fontSize: 15, marginBottom: 4 }}>Assign Agent</div>
            <div className="text-xs text-ink-soft font-mono">{slotLabel}</div>
            {isOrange && (
              <div className="mt-1.5 text-[11px] text-warn-fg bg-warn-bg border border-warn-bd rounded px-2 py-0.5 inline-block">
                Understaffed — below minimum
              </div>
            )}
            {!slot.hasAnyAgent && (
              <div className="mt-1.5 text-[11px] text-crit-fg bg-crit-bg border border-crit-bd rounded px-2 py-0.5 inline-block">
                No coverage — gap
              </div>
            )}
          </div>
          <button onClick={onClose} className="bg-transparent border-none text-ink-muted cursor-pointer leading-none p-0">
            <X size={16} />
          </button>
        </div>

        {/* Agent list */}
        <div style={{ overflowY: "auto", flex: 1 }}>
          {isLoading && (
            <div className="p-6 text-center text-ink-soft text-sm">Loading agents…</div>
          )}
          {!isLoading && sorted.length === 0 && (
            <div className="p-6 text-center text-ink-soft text-sm">
              No available agents for this slot
            </div>
          )}
          {sorted.map(c => {
            const covers   = candidateCoversHour(c, slot.hour)
            const isSaving = saving === c.employeeId
            return (
              <div
                key={c.employeeId}
                className="flex items-center gap-3 px-5 py-[11px] border-b border-line-subtle"
              >
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="flex items-center gap-1.5">
                    <span style={{ fontWeight: 600, fontSize: 13 }}>{c.fullName ?? c.employeeId}</span>
                    {c.isWorkingToday && (
                      <span className="text-[9px] px-1.5 py-px rounded-full font-mono bg-good-bg text-good-fg">
                        WORKING
                      </span>
                    )}
                  </div>
                  <div className="text-[10px] text-ink-soft mt-0.5 font-mono">
                    {c.employeeId}
                    {c.shiftStart && c.shiftEnd
                      ? <span className={`ml-2 ${covers ? "text-good-fg" : "text-ink-soft"}`}>
                          {c.shiftStart}–{c.shiftEnd}
                          {covers ? " ✓ in shift" : " outside shift"}
                        </span>
                      : c.isWorkingToday
                        ? <span className="ml-2 text-ink-soft">working · no times set</span>
                        : <span className="ml-2 text-ink-soft">no shift today</span>
                    }
                  </div>
                </div>
                <button
                  onClick={() => assign(c)}
                  disabled={isSaving}
                  className={`px-3.5 py-1.5 rounded-md text-xs font-semibold whitespace-nowrap transition-opacity ${covers ? "bg-info-solid text-white border-none" : "bg-sunken border border-line-subtle text-ink-muted"} ${isSaving ? "opacity-60 cursor-wait" : "cursor-pointer"}`}
                >
                  {isSaving ? "Assigning…" : "Assign"}
                </button>
              </div>
            )
          })}
        </div>

        {error && (
          <div className="px-5 py-2.5 border-t border-line-subtle text-xs text-crit-fg">
            {error}
          </div>
        )}
      </div>
    </div>
  )
}

// ─── Add agent modal ──────────────────────────────────────────────────────────

function AddAgentModal({ onClose, onAdded }: { onClose: () => void; onAdded: () => void }) {
  const [selected, setSelected] = useState("")
  const [error,    setError]    = useState<string | null>(null)

  const { data: candidates, isLoading } = useQuery<VwicCandidate[]>({
    queryKey: ["vwic-candidates"],
    queryFn:  () => fetch("/api/vwic/candidates").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
  })

  const mutation = useMutation({
    mutationFn: (employeeId: string) =>
      fetch("/api/vwic/agents/add", {
        method:  "PUT",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({ employeeId }),
      }).then(async r => {
        const data = await r.json()
        if (!r.ok) throw new Error(data.error ?? "Server error")
        return data
      }),
    onSuccess: onAdded,
    onError:   (e: Error) => setError(e.message),
  })

  return (
    <div
      className="fixed inset-0 bg-black/55 z-[2000] flex items-center justify-center"
      onClick={onClose}
    >
      <div
        className="bg-raised border border-line-subtle rounded-lg w-[420px] p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex justify-between items-center">
          <span style={{ fontWeight: 700, fontSize: 15 }}>Add Agent to VWIC</span>
          <button onClick={onClose} className="bg-transparent border-none text-ink-muted cursor-pointer leading-none p-0">
            <X size={16} />
          </button>
        </div>

        <div className="text-xs text-ink-soft">
          Sets SecondaryRole = VWIC so the agent appears in the Backup Pool.
        </div>

        {isLoading ? (
          <div className="text-ink-soft text-sm">Loading employees…</div>
        ) : (
          <select
            value={selected}
            onChange={e => setSelected(e.target.value)}
            className="bg-sunken border border-line-subtle text-ink px-3 py-2 rounded-md text-sm w-full"
          >
            <option value="">Select employee…</option>
            {candidates?.map(c => (
              <option key={c.employeeId} value={c.employeeId}>
                {c.fullName ?? c.employeeId}{c.primaryRole ? ` · ${c.primaryRole}` : ""}
              </option>
            ))}
          </select>
        )}

        {error && <div className="text-xs text-crit-fg">{error}</div>}

        <div className="flex gap-2 justify-end">
          <button onClick={onClose}
            className="bg-sunken border border-line-subtle text-ink-muted px-4 py-[7px] rounded-md text-sm cursor-pointer">
            Cancel
          </button>
          <button
            onClick={() => { setError(null); mutation.mutate(selected) }}
            disabled={!selected || mutation.isPending}
            className={`bg-info-solid text-white border-none px-4 py-[7px] rounded-md text-sm font-semibold ${selected && !mutation.isPending ? "cursor-pointer" : "cursor-not-allowed opacity-60"}`}
          >
            {mutation.isPending ? "Adding…" : "Add to VWIC"}
          </button>
        </div>
      </div>
    </div>
  )
}

// ─── Coverage timeline ────────────────────────────────────────────────────────

function CoverageTimeline({
  timeline, gaps, onSlotClick,
}: {
  timeline:    VwicTimelineSlot[]
  gaps:        number[]
  onSlotClick: (slot: VwicTimelineSlot) => void
}) {
  const [hovered, setHovered] = useState<number | null>(null)

  return (
    <div className="bg-raised border border-line-subtle rounded-md p-4">
      <div className="text-[11px] font-semibold uppercase tracking-[.07em] text-ink-soft mb-3">
        Coverage Timeline · 24/7 · 00:00 – 24:00
        <span className="ml-2.5 font-normal text-ink-soft normal-case tracking-normal">
          — min 1 agent (00-07 &amp; 17-24) · min 3 agents (07-17) · click under-staffed slot to assign
        </span>
      </div>

      <div style={{ overflowX: "auto" }}>
        <div style={{ display: "flex", gap: 3, minWidth: 600 }}>
          {timeline.map((slot, i) => {
            const count     = slot.mainAgents.length
            const isPartial = !slot.hasMainAgent && count > 0
            const slotTone  = slot.hasMainAgent ? "good" : isPartial ? "warn" : "crit"
            const borderCol = `rgb(var(--st-${slotTone}-fg))`
            const bgCol     = `rgb(var(--st-${slotTone}-mid))`
            const isHov     = hovered === slot.hour
            const isLast    = i >= timeline.length - 4
            const clickable = !slot.hasMainAgent

            return (
              <div
                key={slot.hour}
                onMouseEnter={() => setHovered(slot.hour)}
                onMouseLeave={() => setHovered(null)}
                onClick={() => clickable && onSlotClick(slot)}
                style={{ flex: 1, position: "relative", minWidth: 36 }}
              >
                {/* Hour cell */}
                <div style={{
                  background:   bgCol,
                  border:       `1px solid ${borderCol}`,
                  borderRadius: 4,
                  padding:      "7px 2px",
                  textAlign:    "center",
                  cursor:       clickable ? "pointer" : "default",
                  transition:   "transform .1s",
                  transform:    isHov && clickable ? "translateY(-2px)" : "none",
                  boxShadow:    isHov && clickable ? `0 4px 12px rgb(var(--st-${slotTone}-fg) / 0.27)` : "none",
                }}>
                  <div className="text-ink-soft" style={{ fontSize: 8, marginBottom: 2 }}>{slot.label}</div>
                  <div className="font-mono" style={{ fontSize: 15, fontWeight: 700, color: borderCol, lineHeight: 1 }}>
                    {count}
                  </div>
                  <div className="text-ink-soft" style={{ fontSize: 8, marginTop: 2 }}>
                    /{slot.minRequired}
                    {slot.backupAgents.length > 0 ? ` +${slot.backupAgents.length}` : ""}
                  </div>
                  {clickable && (
                    <div style={{ fontSize: 7, color: borderCol, marginTop: 2, opacity: 0.8 }}>+assign</div>
                  )}
                </div>

                {/* Hover tooltip */}
                {isHov && (
                  <div style={{
                    position: "absolute",
                    top: "calc(100% + 6px)",
                    [isLast ? "right" : "left"]: 0,
                    zIndex: 100,
                    borderRadius: 6, padding: "10px 14px", minWidth: 170,
                    fontSize: 11, whiteSpace: "nowrap",
                    boxShadow: "0 4px 16px rgba(0,0,0,.35)",
                    pointerEvents: "none",
                  }} className="bg-sunken border border-line-subtle text-ink">
                    <div className="text-[11px] font-semibold text-ink-muted mb-1.5">
                      {slot.label} – {String(slot.hour + 1).padStart(2, "0")}:00
                    </div>
                    <div style={{ fontSize: 10, color: borderCol, marginBottom: 8 }}>
                      {count}/{slot.minRequired} required
                      {slot.hasMainAgent ? " ✓" : isPartial ? " — understaffed" : " — no coverage"}
                    </div>
                    {slot.mainAgents.length > 0 && (
                      <>
                        <div className="text-good-fg" style={{ fontSize: 10, fontWeight: 600, marginBottom: 4 }}>VWIC Assigned</div>
                        {slot.mainAgents.map(n => <div key={n} style={{ marginBottom: 3, paddingLeft: 8 }}>· {n}</div>)}
                      </>
                    )}
                    {slot.backupAgents.length > 0 && (
                      <>
                        <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, marginTop: 8, marginBottom: 4 }}>Voice pool on shift</div>
                        {slot.backupAgents.map(n => <div key={n} style={{ marginBottom: 3, paddingLeft: 8 }}>· {n}</div>)}
                      </>
                    )}
                    {!slot.hasAnyAgent && (
                      <div className="text-ink-soft" style={{ fontSize: 10 }}>No agents on shift this hour</div>
                    )}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>

      {gaps.length > 0 && (
        <div className="mt-3 px-3.5 py-2 bg-crit-bg border border-crit-bd rounded-md text-xs text-crit-fg">
          Below minimum staffing:{" "}
          {gaps.map(h => `${String(h).padStart(2, "0")}:00`).join(", ")}
        </div>
      )}
    </div>
  )
}

// ─── Agent table ─────────────────────────────────────────────────────────────

const ABSENCE_COLOR: Record<string, string> = {
  SL: "rgb(var(--st-warn-fg))", AL: "rgb(var(--st-holiday-fg))", UL: "rgb(var(--st-holiday-fg))",
  OFF: "rgb(var(--text-secondary))", PH: "rgb(var(--text-secondary))", OFF_WEEKEND: "rgb(var(--text-secondary))",
}
const ABSENCE_BG: Record<string, string> = {
  SL: "rgb(var(--st-warn-bg))", AL: "rgb(var(--st-holiday-bg))", UL: "rgb(var(--st-holiday-bg))",
  OFF: "rgb(var(--surface-sunken))", PH: "rgb(var(--surface-sunken))", OFF_WEEKEND: "rgb(var(--surface-sunken))",
}

function AgentTable({
  title, agents, onRemove,
}: {
  title:     string
  agents:    VwicAgent[]
  onRemove?: (agent: VwicAgent) => void
}) {
  const available = agents.filter(a => !a.isAbsent).length
  const cols = onRemove ? 5 : 4
  return (
    <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
      <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
        <span style={{ fontWeight: 600, fontSize: 13 }}>{title}</span>
        <span className="text-[11px] text-ink-soft font-mono">
          {available}/{agents.length} available
        </span>
      </div>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
        <thead>
          <tr className="bg-sunken">
            {["Name", "Team Lead", "Shift", "Status", ...(onRemove ? [""] : [])].map((h, i) => (
              <th key={i} className="px-3 py-2 text-left text-[10px] font-medium uppercase tracking-[.07em] text-ink-soft border-b border-line-subtle whitespace-nowrap">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {agents.length === 0 && (
            <tr><td colSpan={cols} className="p-5 text-center text-ink-soft text-xs">No agents</td></tr>
          )}
          {agents.map(a => (
            <tr key={a.employeeId}
              className={`border-b border-line-subtle transition-colors hover:bg-hovered ${a.isAbsent ? "opacity-55" : ""}`}>

              <td className="px-3 py-[9px]">
                <div style={{ fontWeight: 600 }}>{a.fullName ?? a.employeeId}</div>
                <div className="text-[10px] text-ink-soft font-mono mt-px">{a.employeeId}</div>
              </td>
              <td className="px-3 py-[9px] text-ink-muted text-[11px]">{a.teamLeadName ?? "—"}</td>
              <td className={`px-3 py-[9px] font-mono text-[11px] ${a.isAbsent ? "text-ink-soft" : "text-ink-muted"}`}>
                {a.isAbsent
                  ? "—"
                  : a.shiftStart && a.shiftEnd
                    ? `${a.shiftStart} – ${a.shiftEnd}`
                    : a.shiftType ?? "No shift"}
              </td>
              <td className="px-3 py-[9px]">
                {a.isAbsent ? (
                  <span className="px-2 py-0.5 rounded text-[10px] font-mono" style={{ background: ABSENCE_BG[a.absenceType!] ?? "rgb(var(--surface-sunken))", color: ABSENCE_COLOR[a.absenceType!] ?? "rgb(var(--text-secondary))" }}>
                    {a.absenceType}
                  </span>
                ) : a.isVwicAssigned ? (
                  <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-info-bg text-info-fg">VWIC</span>
                ) : a.shiftStart && a.shiftEnd ? (
                  <span className="px-2 py-0.5 rounded text-[10px] bg-good-bg text-good-fg">Working</span>
                ) : (
                  <span className="px-2 py-0.5 rounded text-[10px] bg-learn-bg text-learn-fg">No shift</span>
                )}
              </td>
              {onRemove && (
                <td className="px-3 py-[9px]">
                  <button
                    onClick={() => onRemove(a)}
                    className="bg-crit-bg border border-crit-bd text-crit-fg px-2.5 py-0.5 rounded text-[11px] cursor-pointer"
                  >
                    Remove
                  </button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ─── Summary card ─────────────────────────────────────────────────────────────

function SummaryCard({ label, value, color, sub }: { label: string; value: string | number; color: string; sub?: string }) {
  return (
    <div className="bg-raised border border-line-subtle rounded-md px-5 py-4">
      <div className="text-[10px] uppercase tracking-[.08em] text-ink-soft mb-1.5">{label}</div>
      <div className="font-mono" style={{ fontSize: 26, fontWeight: 700, color }}>{value}</div>
      {sub && <div className="text-[10px] text-ink-soft mt-1">{sub}</div>}
    </div>
  )
}

// ─── Rotation Planner ─────────────────────────────────────────────────────────

function RotationPlanner({ initialDate }: { initialDate: string }) {
  const [date,               setDate]               = useState(initialDate)
  const [startTime,          setStartTime]          = useState("07:00")
  const [endTime,            setEndTime]            = useState("18:00")
  const [intervalHours,      setIntervalHours]      = useState(1)
  const [maxContinuousHours, setMaxContinuousHours] = useState(2)
  const [handoverMinutes,    setHandoverMinutes]    = useState(15)
  const [result,             setResult]             = useState<VwicRotationResponse | null>(null)
  const [loading,            setLoading]            = useState(false)
  const [error,              setError]              = useState<string | null>(null)
  const [weekResult,         setWeekResult]         = useState<VwicWeekResponse | null>(null)
  const [weekLoading,        setWeekLoading]        = useState(false)
  const [weekError,          setWeekError]          = useState<string | null>(null)
  const [activeDay,          setActiveDay]          = useState(0)
  const [saveLoading,        setSaveLoading]        = useState(false)
  const [saveMsg,            setSaveMsg]            = useState<string | null>(null)

  const saveRotation = async () => {
    if (!result) return
    setSaveLoading(true)
    setSaveMsg(null)
    try {
      const res = await fetch("/api/vwic/rotation-plan/save", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          date:       result.date,
          slotLabels: result.slotLabels,
          schedule:   result.schedule.map(r => ({ employeeId: r.employeeId, slotStatus: r.slotStatus })),
        }),
      })
      if (!res.ok) throw new Error("Server error")
      const data = await res.json()
      setSaveMsg(`Saved ${data.saved} slot assignments for ${result.date}`)
    } catch {
      setSaveMsg("Save failed — please try again")
    } finally {
      setSaveLoading(false)
    }
  }

  const calculate = async () => {
    setLoading(true)
    setError(null)
    setWeekResult(null)
    setSaveMsg(null)
    try {
      const res = await fetch("/api/vwic/rotation-plan", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({ date, startTime, endTime, intervalHours, maxContinuousHours, handoverMinutes }),
      })
      if (!res.ok) throw new Error("Server error")
      setResult(await res.json())
    } catch {
      setError("Failed to calculate. Check that the backend is running.")
    } finally {
      setLoading(false)
    }
  }

  const getMonday = (d: string) => {
    const dt = new Date(d), day = dt.getDay()
    dt.setDate(dt.getDate() + (day === 0 ? -6 : 1 - day))
    return dt.toISOString().split("T")[0]
  }

  const planWeek = async () => {
    setWeekLoading(true)
    setWeekError(null)
    setResult(null)
    setActiveDay(0)
    try {
      const res = await fetch("/api/vwic/rotation-plan-week", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({
          weekStartDate: getMonday(date),
          startTime, endTime, intervalHours, maxContinuousHours, handoverMinutes,
        }),
      })
      if (!res.ok) throw new Error("Server error")
      setWeekResult(await res.json())
    } catch {
      setWeekError("Failed to calculate week plan.")
    } finally {
      setWeekLoading(false)
    }
  }

  const exportWeek = async () => {
    if (!weekResult) return
    const res = await fetch("/api/vwic/rotation-plan-week/export", {
      method:  "POST",
      headers: { "Content-Type": "application/json" },
      body:    JSON.stringify({
        weekStartDate: weekResult.weekStartDate,
        startTime, endTime, intervalHours, maxContinuousHours, handoverMinutes,
      }),
    })
    if (!res.ok) return
    const blob = await res.blob()
    const url  = URL.createObjectURL(blob)
    const a    = document.createElement("a")
    a.href     = url
    a.download = `VWIC_Week_${weekResult.weekStartDate}.xlsx`
    a.click()
    URL.revokeObjectURL(url)
  }

  const maxVwicHours = result ? Math.max(...result.fairness.map(f => f.vwicHours), 1) : 1
  const allCovered   = result?.coverageProof.every(c => c.covered) ?? false

  const statusStyle = (s: string) => ({
    bg:    s === "ON" ? "rgb(var(--st-good-bg))" : s === "HANDOVER" ? "rgb(var(--st-holiday-bg))" : "transparent",
    fgCls: s === "ON" ? "text-good-fg" : s === "HANDOVER" ? "text-warn-fg" : "text-ink-soft",
    label: s === "ON" ? "ON" : s === "HANDOVER" ? "HO" : "—",
  })

  const inpCls = "bg-sunken border border-line-subtle text-ink px-2.5 py-[7px] rounded-md text-sm outline-none w-full"

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* ── Parameters form ── */}
      <div className="bg-raised border border-line-subtle rounded-md p-5">
        <div className="text-[11px] font-semibold uppercase tracking-[.07em] text-ink-soft mb-3.5">
          Rotation Parameters
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(190px, 1fr))", gap: 14 }}>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span className="text-[11px] text-ink-soft">Date</span>
            <input type="date" value={date} onChange={e => setDate(e.target.value)} className={inpCls} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span className="text-[11px] text-ink-soft">Window start</span>
            <input type="time" value={startTime} onChange={e => setStartTime(e.target.value)} className={inpCls} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span className="text-[11px] text-ink-soft">Window end</span>
            <input type="time" value={endTime} onChange={e => setEndTime(e.target.value)} className={inpCls} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span className="text-[11px] text-ink-soft">Max continuous VWIC (h)</span>
            <input type="number" min={1} max={8} value={maxContinuousHours}
              onChange={e => setMaxContinuousHours(+e.target.value)} className={inpCls} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span className="text-[11px] text-ink-soft">Handover buffer (min)</span>
            <input type="number" min={0} max={30} value={handoverMinutes}
              onChange={e => setHandoverMinutes(+e.target.value)} className={inpCls} />
          </label>
        </div>

        {/* Interval radio */}
        <div style={{ marginTop: 14 }}>
          <div className="text-[11px] text-ink-soft mb-2">Rotation interval</div>
          <div style={{ display: "flex", gap: 8 }}>
            {([1, 2, 4] as const).map(h => (
              <label key={h} className={`flex items-center gap-1.5 cursor-pointer rounded-md px-4 py-1.5 transition-all border ${
                intervalHours === h ? "bg-info-bg border-info-bd" : "bg-sunken border-line-subtle"
              }`}>
                <input type="radio" name="rp-interval" value={h} checked={intervalHours === h}
                  onChange={() => setIntervalHours(h)}
                  style={{ accentColor: "inherit", margin: 0 }} />
                <span className={`text-sm ${intervalHours === h ? "font-bold text-info-fg" : "font-normal text-ink-muted"}`}>
                  {h}h
                </span>
              </label>
            ))}
          </div>
        </div>

        <div style={{ marginTop: 16, display: "flex", alignItems: "center", gap: 12 }}>
          <button
            onClick={calculate} disabled={loading || weekLoading}
            className={`bg-info-solid text-white border-none px-[22px] py-[9px] rounded-md text-sm font-semibold ${loading ? "opacity-70 cursor-wait" : "cursor-pointer"}`}
          >
            {loading ? "Calculating…" : "Calculate Rotation"}
          </button>
          <button
            onClick={planWeek} disabled={loading || weekLoading}
            className={`bg-sunken text-ink border border-line-subtle px-[22px] py-[9px] rounded-md text-sm font-semibold ${weekLoading ? "opacity-70 cursor-wait" : "cursor-pointer"}`}
          >
            {weekLoading ? "Planning…" : "Plan Week"}
          </button>
          {result && (
            <button
              onClick={saveRotation} disabled={saveLoading}
              className="bg-good-solid text-white"
              style={{
                border: "none",
                padding: "9px 22px", borderRadius: 6, fontSize: 13, fontWeight: 600,
                cursor: saveLoading ? "wait" : "pointer", opacity: saveLoading ? 0.7 : 1,
              }}
            >
              {saveLoading ? "Saving…" : "Save Rotation"}
            </button>
          )}
          {error     && <span className="text-xs text-crit-fg">{error}</span>}
          {weekError && <span className="text-xs text-crit-fg">{weekError}</span>}
          {saveMsg   && (
            <span className={saveMsg.includes("failed") ? "text-crit-fg text-xs" : "text-good-fg text-xs"}>
              {saveMsg}
            </span>
          )}
        </div>
      </div>

      {result && (
        <>
          {/* ── Recommendation ── */}
          <div className="bg-raised rounded-md px-5 py-3.5 flex items-start gap-3.5 border"
            style={{ borderColor: result.availableAgents < 3 ? "rgb(var(--st-crit-bd))" : "rgb(var(--st-good-bd))" }}>
            <div style={{ fontSize: 24, lineHeight: 1, flexShrink: 0 }}>
              {result.availableAgents < 3
                ? <AlertTriangle size={20} className="text-warn-fg" />
                : "✓"}
            </div>
            <div>
              <div className={`text-[10px] font-bold uppercase tracking-[.08em] mb-1.5 ${result.availableAgents < 3 ? "text-warn-fg" : "text-good-fg"}`}>
                Recommendation
              </div>
              <div className="text-sm text-ink" style={{ lineHeight: 1.6 }}>{result.recommendation}</div>
              <div className="mt-1.5 text-[11px] text-ink-soft font-mono">
                {result.availableAgents} Voice agents available for rotation &nbsp;·&nbsp;
                {result.requiredAgentHours.toFixed(0)}h required &nbsp;·&nbsp;
                {result.availableAgentHours.toFixed(0)}h available capacity
              </div>
            </div>
          </div>

          {/* ── Schedule table ── */}
          <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
            <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
              <span style={{ fontWeight: 600, fontSize: 13 }}>Rotation Schedule</span>
              <div className="flex gap-3.5 text-[11px] text-ink-soft">
                <span><span className="text-good-fg font-bold">ON</span> — on VWIC</span>
                <span><span className="text-warn-fg font-bold">HO</span> — handover</span>
                <span className="text-ink-soft">— off</span>
              </div>
            </div>
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                <thead>
                  <tr className="bg-sunken">
                    <th className="px-3.5 py-2 text-left text-[10px] font-semibold uppercase tracking-[.06em] text-ink-soft border-b border-line-subtle whitespace-nowrap min-w-[110px]">
                      Slot
                    </th>
                    {result.schedule.map(row => (
                      <th key={row.employeeId} className="px-2.5 py-2 text-center text-[11px] font-semibold text-ink-muted border-b border-line-subtle whitespace-nowrap min-w-[85px]">
                        {row.fullName.includes(" ") ? row.fullName.split(" ")[0] : row.fullName}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.slotLabels.map((label, si) => (
                    <tr key={si} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                      <td className="px-3.5 py-[7px] font-mono text-[11px] text-ink-muted font-semibold whitespace-nowrap">
                        {label}
                      </td>
                      {result.schedule.map(row => {
                        const ss = statusStyle(row.slotStatus[si] ?? "OFF")
                        return (
                          <td key={row.employeeId} className="py-1 px-2 text-center" style={{ background: ss.bg }}>
                            <span className={`text-[10px] font-bold font-mono ${ss.fgCls}`}>
                              {ss.label}
                            </span>
                          </td>
                        )
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* ── Coverage proof + Fairness ── */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>

            {/* Coverage proof */}
            <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
              <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
                <span style={{ fontWeight: 600, fontSize: 13 }}>Coverage Proof</span>
                <span className={`text-[11px] font-mono ${allCovered ? "text-good-fg" : "text-crit-fg"}`}>
                  {allCovered ? "All slots ✓" : "Gaps detected ✗"}
                </span>
              </div>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <thead>
                  <tr className="bg-sunken">
                    {["Slot", "Agents", "Min", ""].map((h, i) => (
                      <th key={i} className={`px-3 py-[7px] ${i < 3 ? "text-left" : "text-center"} text-[10px] font-medium uppercase tracking-[.06em] text-ink-soft border-b border-line-subtle`}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.coverageProof.map((item, i) => (
                    <tr key={i} className={`border-b border-line-subtle ${!item.covered ? "bg-crit-bg" : ""}`}>
                      <td className="px-3 py-[7px] font-mono text-[11px] whitespace-nowrap">{item.startTime}–{item.endTime}</td>
                      <td className={`px-3 py-[7px] font-bold ${item.covered ? "text-good-fg" : "text-crit-fg"}`}>{item.agentCount}</td>
                      <td className="px-3 py-[7px] text-ink-soft">{item.required}</td>
                      <td className="px-3 py-[7px] text-center" style={{ fontSize: 14 }}>
                        {item.covered
                          ? <Check size={12} className="inline text-good-fg" />
                          : <X size={12} className="inline text-crit-fg" />}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Fairness */}
            <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
              <div className="px-4 py-3 border-b border-line-subtle">
                <span style={{ fontWeight: 600, fontSize: 13 }}>Fairness Overview</span>
              </div>
              <div className="px-4 py-3.5 flex flex-col gap-3">
                {result.fairness.map(item => (
                  <div key={item.employeeId}>
                    <div className="flex justify-between items-center mb-1.5">
                      <span style={{ fontSize: 12, fontWeight: 600 }}>{item.fullName}</span>
                      <span className="text-[11px] font-mono text-ink-soft">
                        {item.vwicHours}h &nbsp;·&nbsp; {item.slotCount} slot{item.slotCount !== 1 ? "s" : ""}
                      </span>
                    </div>
                    <div className="h-1.5 bg-sunken rounded-full overflow-hidden">
                      <div className="h-full bg-info-solid rounded-full transition-[width_.3s_ease]"
                        style={{ width: `${(item.vwicHours / maxVwicHours) * 100}%` }} />
                    </div>
                  </div>
                ))}
                {result.fairness.length === 0 && (
                  <div className="text-ink-soft text-xs">No agent data</div>
                )}
              </div>
            </div>
          </div>

          {/* ── Fallback warning ── */}
          {result.fallbackWarning && (() => {
            const isCritical = result.fallbackWarning.startsWith("CRITICAL")
            const isWarn     = result.fallbackWarning.startsWith("1 absence leaves")
            return (
              <div className={`rounded-md px-[18px] py-3 border ${
                isCritical ? "bg-crit-bg border-crit-bd" : isWarn ? "bg-warn-bg border-warn-bd" : "border-good-bd"
              }`}>
                <div className={`text-[10px] font-bold uppercase tracking-[.08em] mb-1.5 ${
                  isCritical ? "text-crit-fg" : isWarn ? "text-warn-fg" : "text-good-fg"
                }`}>
                  Fallback Warning
                </div>
                <div className="text-sm text-ink" style={{ lineHeight: 1.5 }}>{result.fallbackWarning}</div>
              </div>
            )
          })()}
        </>
      )}

      {/* ── Week plan result ── */}
      {weekResult && (() => {
        const day        = weekResult.days[activeDay]
        const allCovDay  = day?.coverageProof.every(c => c.covered) ?? false
        const maxDayH    = day ? Math.max(...day.dailyFairness.map(f => f.vwicHours), 1) : 1
        return (
          <>
            {/* Day tabs + Export */}
            <div style={{ display: "flex", gap: 4, alignItems: "center" }}>
              {weekResult.days.map((d, i) => (
                <button key={i} onClick={() => setActiveDay(i)} className={`px-5 py-[7px] rounded-md border text-sm font-semibold cursor-pointer transition-all ${
                  activeDay === i ? "bg-info-solid text-white border-transparent" : "bg-sunken text-ink-muted border-line-subtle"
                }`}>
                  {d.dayShort}
                  {d.availableAgents < 3 && (
                    <AlertTriangle size={10} className={`inline ml-1.5 align-text-bottom ${activeDay === i ? "text-yellow-200" : "text-warn-fg"}`} />
                  )}
                </button>
              ))}
              <div style={{ flex: 1 }} />
              <button onClick={exportWeek} className="bg-good-solid text-white" style={{
                border: "none",
                padding: "7px 18px", borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: "pointer",
              }}>
                Export Excel
              </button>
            </div>

            {/* Active day detail */}
            {day && (
              <>
                {/* Recommendation */}
                <div className="bg-raised rounded-md px-5 py-3.5 flex items-start gap-3.5 border"
                  style={{ borderColor: day.availableAgents < 3 ? "rgb(var(--st-crit-bd))" : "rgb(var(--st-good-bd))" }}>
                  <div style={{ fontSize: 24, lineHeight: 1, flexShrink: 0 }}>
                    {day.availableAgents < 3
                      ? <AlertTriangle size={20} className="text-warn-fg" />
                      : "✓"}
                  </div>
                  <div>
                    <div className={`text-[10px] font-bold uppercase tracking-[.08em] mb-1.5 ${day.availableAgents < 3 ? "text-warn-fg" : "text-good-fg"}`}>
                      {day.dayName} — Recommendation
                    </div>
                    <div className="text-sm text-ink" style={{ lineHeight: 1.6 }}>{day.recommendation}</div>
                    <div className="mt-1.5 text-[11px] text-ink-soft font-mono">
                      {day.availableAgents} Voice agents available for rotation &nbsp;·&nbsp;
                      {day.requiredAgentHours.toFixed(0)}h required &nbsp;·&nbsp;
                      {day.availableAgentHours.toFixed(0)}h available capacity
                    </div>
                  </div>
                </div>

                {/* Schedule table */}
                <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
                  <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
                    <span style={{ fontWeight: 600, fontSize: 13 }}>{day.dayName} — Rotation Schedule</span>
                    <div className="flex gap-3.5 text-[11px] text-ink-soft">
                      <span><span className="text-good-fg font-bold">ON</span> — on VWIC</span>
                      <span><span className="text-warn-fg font-bold">HO</span> — handover</span>
                      <span className="text-ink-soft">— off</span>
                    </div>
                  </div>
                  <div style={{ overflowX: "auto" }}>
                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                      <thead>
                        <tr className="bg-sunken">
                          <th className="px-3.5 py-2 text-left text-[10px] font-semibold uppercase tracking-[.06em] text-ink-soft border-b border-line-subtle whitespace-nowrap min-w-[110px]">Slot</th>
                          {day.schedule.map(row => (
                            <th key={row.employeeId} className="px-2.5 py-2 text-center text-[11px] font-semibold text-ink-muted border-b border-line-subtle whitespace-nowrap min-w-[85px]">
                              {row.fullName.includes(" ") ? row.fullName.split(" ")[0] : row.fullName}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {day.slotLabels.map((label, si) => (
                          <tr key={si} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                            <td className="px-3.5 py-[7px] font-mono text-[11px] text-ink-muted font-semibold whitespace-nowrap">{label}</td>
                            {day.schedule.map(row => {
                              const ss = statusStyle(row.slotStatus[si] ?? "OFF")
                              return (
                                <td key={row.employeeId} className="py-1 px-2 text-center" style={{ background: ss.bg }}>
                                  <span className={`text-[10px] font-bold font-mono ${ss.fgCls}`}>{ss.label}</span>
                                </td>
                              )
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>

                {/* Coverage + day fairness */}
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
                  <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
                    <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
                      <span style={{ fontWeight: 600, fontSize: 13 }}>Coverage Proof</span>
                      <span className={`text-[11px] font-mono ${allCovDay ? "text-good-fg" : "text-crit-fg"}`}>
                        {allCovDay ? "All slots ✓" : "Gaps detected ✗"}
                      </span>
                    </div>
                    <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                      <thead>
                        <tr className="bg-sunken">
                          {["Slot", "Agents", "Min", ""].map((h, i) => (
                            <th key={i} className={`px-3 py-[7px] ${i < 3 ? "text-left" : "text-center"} text-[10px] font-medium uppercase tracking-[.06em] text-ink-soft border-b border-line-subtle`}>{h}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {day.coverageProof.map((item, i) => (
                          <tr key={i} className={`border-b border-line-subtle ${!item.covered ? "bg-crit-bg" : ""}`}>
                            <td className="px-3 py-[7px] font-mono text-[11px] whitespace-nowrap">{item.startTime}–{item.endTime}</td>
                            <td className={`px-3 py-[7px] font-bold ${item.covered ? "text-good-fg" : "text-crit-fg"}`}>{item.agentCount}</td>
                            <td className="px-3 py-[7px] text-ink-soft">{item.required}</td>
                            <td className="px-3 py-[7px] text-center" style={{ fontSize: 14 }}>
                              {item.covered
                                ? <Check size={12} className="inline text-good-fg" />
                                : <X size={12} className="inline text-crit-fg" />}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
                    <div className="px-4 py-3 border-b border-line-subtle">
                      <span style={{ fontWeight: 600, fontSize: 13 }}>{day.dayName} — Fairness</span>
                    </div>
                    <div className="px-4 py-3.5 flex flex-col gap-3">
                      {day.dailyFairness.map(item => (
                        <div key={item.employeeId}>
                          <div className="flex justify-between items-center mb-1.5">
                            <span style={{ fontSize: 12, fontWeight: 600 }}>{item.fullName}</span>
                            <span className="text-[11px] font-mono text-ink-soft">
                              {item.vwicHours}h &nbsp;·&nbsp; {item.slotCount} slot{item.slotCount !== 1 ? "s" : ""}
                            </span>
                          </div>
                          <div className="h-1.5 bg-sunken rounded-full overflow-hidden">
                            <div className="h-full bg-info-solid rounded-full transition-[width_.3s_ease]"
                              style={{ width: `${(item.vwicHours / maxDayH) * 100}%` }} />
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                {day.fallbackWarning && (() => {
                  const isCritical = day.fallbackWarning!.startsWith("CRITICAL")
                  const isWarn     = day.fallbackWarning!.startsWith("1 absence leaves")
                  return (
                    <div className={`rounded-md px-[18px] py-3 border ${
                      isCritical ? "bg-crit-bg border-crit-bd" : isWarn ? "bg-warn-bg border-warn-bd" : "border-good-bd"
                    }`}>
                      <div className={`text-[10px] font-bold uppercase tracking-[.08em] mb-1.5 ${
                        isCritical ? "text-crit-fg" : isWarn ? "text-warn-fg" : "text-good-fg"
                      }`}>
                        Fallback Warning
                      </div>
                      <div className="text-sm text-ink" style={{ lineHeight: 1.5 }}>{day.fallbackWarning}</div>
                    </div>
                  )
                })()}
              </>
            )}

            {/* Weekly fairness summary */}
            <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
              <div className="px-4 py-3 border-b border-line-subtle flex justify-between items-center">
                <span style={{ fontWeight: 600, fontSize: 13 }}>Weekly Fairness Summary</span>
                <span className="text-[11px] text-ink-soft font-mono">sorted by total VWIC hours ↑</span>
              </div>
              <div style={{ overflowX: "auto" }}>
                <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                  <thead>
                    <tr className="bg-sunken">
                      {["Agent", ...weekResult.days.map(d => d.dayShort), "Total h", "Slots", "Days"].map((h, i) => (
                        <th key={i} className={`px-3 py-[7px] ${i === 0 ? "text-left" : "text-center"} text-[10px] font-semibold uppercase tracking-[.06em] text-ink-soft border-b border-line-subtle whitespace-nowrap`}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {weekResult.weeklyFairness.map(item => (
                      <tr key={item.employeeId} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                        <td className="px-3 py-[7px] font-semibold">{item.fullName}</td>
                        {weekResult.days.map((d, di) => {
                          const df = d.dailyFairness.find(f => f.employeeId === item.employeeId)
                          return (
                            <td key={di} className={`px-3 py-[7px] text-center font-mono text-[11px] ${df && df.vwicHours > 0 ? "text-good-fg" : "text-ink-soft"}`}>
                              {df && df.vwicHours > 0 ? `${df.vwicHours}h` : "—"}
                            </td>
                          )
                        })}
                        <td className="px-3 py-[7px] text-center font-mono text-[11px] font-bold text-info-fg">
                          {item.totalVwicHours}h
                        </td>
                        <td className="px-3 py-[7px] text-center font-mono text-[11px] text-ink-soft">
                          {item.totalSlotCount}
                        </td>
                        <td className="px-3 py-[7px] text-center font-mono text-[11px] text-ink-soft">
                          {item.daysWorked}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </>
        )
      })()}
    </div>
  )
}

// ─── Page ─────────────────────────────────────────────────────────────────────

const btnCls = "border border-line-subtle px-3 py-1.5 rounded-md text-xs cursor-pointer"

export default function VWICPage() {
  const [date,         setDate]         = useState(new Date().toISOString().split("T")[0])
  const [assignSlot,   setAssignSlot]   = useState<VwicTimelineSlot | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [tab,          setTab]          = useState<"coverage" | "rotation">("coverage")
  const queryClient = useQueryClient()

  const { data, isLoading, isError } = useQuery<VwicDailyResponse>({
    queryKey: ["vwic-daily", date],
    queryFn:  () => fetch(`/api/vwic/daily?date=${date}`).then(r => {
      if (!r.ok) throw new Error("API error")
      return r.json()
    }),
  })


  const shift = (days: number) => {
    const d = new Date(date); d.setDate(d.getDate() + days)
    setDate(d.toISOString().split("T")[0])
  }

  const handleAssigned = () => {
    queryClient.invalidateQueries({ queryKey: ["vwic-daily", date] })
    setAssignSlot(null)
  }

  const handleAgentAdded = () => {
    queryClient.invalidateQueries({ queryKey: ["vwic-daily", date] })
    queryClient.invalidateQueries({ queryKey: ["vwic-candidates"] })
    setShowAddModal(false)
  }

  const mainAgents   = data?.agents.filter(a => a.role === "Main")   ?? []
  const backupAgents = data?.agents.filter(a => a.role === "Backup") ?? []
  const activeMain   = mainAgents.filter(a => !a.isAbsent).length
  const activeBackup = backupAgents.filter(a => !a.isAbsent).length
  const hasCoverage  = (data?.coveredHours ?? 0) === (data?.totalHours ?? 24)

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* Modals */}
      {assignSlot && (
        <AssignSlotModal
          slot={assignSlot}
          date={date}
          onClose={() => setAssignSlot(null)}
          onAssigned={handleAssigned}
        />
      )}
      {showAddModal && (
        <AddAgentModal onClose={() => setShowAddModal(false)} onAdded={handleAgentAdded} />
      )}

      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 700, margin: 0 }} className="text-ink">VWIC</h1>
          <div className="text-xs text-ink-soft mt-0.5">
            Virtual Walk-In Center · 24/7 · min 1 agent (00-07 &amp; 17-24) · min 3 agents (07-17)
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <button
            onClick={() => setShowAddModal(true)}
            className={`${btnCls} bg-info-solid text-white border-none font-semibold`}
          >
            + Add Agent to VWIC
          </button>
          <div className="w-px h-6 bg-line-subtle mx-1" />
          <button onClick={() => shift(-1)} className={`${btnCls} bg-raised text-ink-muted`}>←</button>
          <input type="date" value={date} onChange={e => setDate(e.target.value)}
            className={`${btnCls} bg-raised text-ink-muted font-mono outline-none`} />
          <button onClick={() => setDate(new Date().toISOString().split("T")[0])}
            className={`${btnCls} bg-sunken text-ink-muted`}>
            Today
          </button>
          <button onClick={() => shift(1)} className={`${btnCls} bg-raised text-ink-muted`}>→</button>
        </div>
      </div>

      {/* Tab bar */}
      <div className="flex gap-0.5 bg-sunken rounded-md p-0.5 self-start">
        {(["coverage", "rotation"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} className={`px-[18px] py-1.5 rounded-md border-none text-sm font-semibold cursor-pointer transition-all ${
            tab === t
              ? "bg-raised text-ink shadow-[0_1px_4px_rgba(0,0,0,.18)]"
              : "bg-transparent text-ink-soft"
          }`}>
            {t === "coverage" ? "Coverage" : "Rotation Planner"}
          </button>
        ))}
      </div>

      {tab === "coverage" && (
        <>
          {isLoading && <div className="py-12 text-center text-ink-soft">Loading…</div>}

          {isError && (
            <div className="p-6 bg-crit-bg border border-crit-bd rounded-md text-crit-fg text-sm">
              Failed to load VWIC data. Check that the backend is running.
            </div>
          )}

          {data && (
            <>
              {/* Summary cards */}
              <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
                <SummaryCard label="VWIC Assigned Today" value={`${activeMain}/${mainAgents.length}`}     color="rgb(var(--st-info-fg))" sub="with VWIC entry today" />
                <SummaryCard label="Voice Pool"         value={`${activeBackup}/${backupAgents.length}`} color="rgb(var(--text-secondary))"  sub="not assigned to VWIC" />
                <SummaryCard label="Coverage"           value={`${data.coveredHours}/${data.totalHours}h`}
                  color={hasCoverage ? "rgb(var(--st-good-fg))" : "rgb(var(--st-crit-fg))"} sub="hours meeting minimum staffing" />
                <SummaryCard label="Gaps"               value={data.gaps.length}
                  color={data.gaps.length === 0 ? "rgb(var(--st-good-fg))" : "rgb(var(--st-crit-fg))"}
                  sub={data.gaps.length === 0 ? "fully staffed 24/7" : "hours below minimum"} />
              </div>

              {/* Timeline */}
              <CoverageTimeline
                timeline={data.timeline}
                gaps={data.gaps}
                onSlotClick={slot => setAssignSlot(slot)}
              />

              {/* Agent tables */}
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1.6fr", gap: 16 }}>
                <AgentTable title="VWIC Assigned Today" agents={mainAgents} />
                <AgentTable title="Voice Pool — Not Assigned" agents={backupAgents} />
              </div>
            </>
          )}
        </>
      )}

      {tab === "rotation" && <RotationPlanner initialDate={date} />}
    </div>
  )
}
