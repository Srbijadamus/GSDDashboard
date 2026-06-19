import { useState } from "react"
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query"

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
  hasMainAgent: boolean
  hasAnyAgent:  boolean
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
  employeeId:   string
  fullName:     string | null
  teamLeadName: string | null
  primaryRole:  string | null
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

// ─── Helpers ─────────────────────────────────────────────────────────────────

function parseDecimalHour(t: string | null): number | null {
  if (!t) return null
  const [h, m] = t.split(":").map(Number)
  return h + (m || 0) / 60
}

function coversHour(agent: VwicAgent, hour: number): boolean {
  const start = parseDecimalHour(agent.shiftStart)
  const end   = parseDecimalHour(agent.shiftEnd)
  if (start === null || end === null) return false
  return start < hour + 1 && end > hour
}

// ─── Assign modal ─────────────────────────────────────────────────────────────

function AssignSlotModal({
  slot, date, agents, onClose, onAssigned,
}: {
  slot:       VwicTimelineSlot
  date:       string
  agents:     VwicAgent[]
  onClose:    () => void
  onAssigned: () => void
}) {
  const [saving, setSaving] = useState<string | null>(null) // employeeId being saved
  const [error,  setError]  = useState<string | null>(null)

  // For red slots show backup agents; for orange (backup-only) show Main agents
  const needMain = !slot.hasMainAgent

  // Available = correct role, not absent, not already in slot
  const alreadyInSlot = new Set([...slot.mainAgents, ...slot.backupAgents])
  const available = agents.filter(a => {
    if (a.isAbsent) return false
    if (a.role !== (needMain ? "Backup" : "Main") &&
        !(needMain && a.role === "Main")) return false // for red, show both roles
    const name = a.fullName ?? a.employeeId
    if (alreadyInSlot.has(name)) return false
    return true
  })

  // Sort: those whose shift already covers the hour first
  const sorted = [...available].sort((a, b) => {
    const aC = coversHour(a, slot.hour) ? 0 : 1
    const bC = coversHour(b, slot.hour) ? 0 : 1
    return aC - bC || (a.fullName ?? "").localeCompare(b.fullName ?? "")
  })

  const assign = async (agent: VwicAgent) => {
    setSaving(agent.employeeId)
    setError(null)
    try {
      const res = await fetch("/api/vwic/assign", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId: agent.employeeId,
          date,
          hour:       slot.hour,
          shiftStart: agent.shiftStart ?? "07:00",
          shiftEnd:   agent.shiftEnd   ?? "18:00",
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
      style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.55)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={onClose}
    >
      <div
        style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, width: 460, maxHeight: "80vh", display: "flex", flexDirection: "column", overflow: "hidden" }}
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div style={{ padding: "16px 20px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: 15, marginBottom: 4 }}>Assign Agent</div>
            <div style={{ fontSize: 12, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>{slotLabel}</div>
            {isOrange && (
              <div style={{ marginTop: 6, fontSize: 11, color: "#f97316", background: "rgba(249,115,22,.1)", border: "1px solid rgba(249,115,22,.3)", borderRadius: 4, padding: "3px 8px", display: "inline-block" }}>
                Backup only — no Main agent covering
              </div>
            )}
            {!slot.hasAnyAgent && (
              <div style={{ marginTop: 6, fontSize: 11, color: "#ef4444", background: "rgba(239,68,68,.1)", border: "1px solid rgba(239,68,68,.3)", borderRadius: 4, padding: "3px 8px", display: "inline-block" }}>
                No coverage — gap
              </div>
            )}
          </div>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer", lineHeight: 1 }}>✕</button>
        </div>

        {/* Agent list */}
        <div style={{ overflowY: "auto", flex: 1 }}>
          {sorted.length === 0 && (
            <div style={{ padding: 24, textAlign: "center", color: "var(--text3)", fontSize: 13 }}>
              No available agents for this slot
            </div>
          )}

          {sorted.map(agent => {
            const covers  = coversHour(agent, slot.hour)
            const isSaving = saving === agent.employeeId
            return (
              <div
                key={agent.employeeId}
                style={{ display: "flex", alignItems: "center", gap: 12, padding: "11px 20px", borderBottom: "1px solid var(--border)" }}
              >
                {/* Agent info */}
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                    <span style={{ fontWeight: 600, fontSize: 13 }}>{agent.fullName ?? agent.employeeId}</span>
                    <span style={{
                      fontSize: 9, padding: "1px 6px", borderRadius: 10, fontFamily: "IBM Plex Mono",
                      background: agent.role === "Main" ? "rgba(59,126,255,.15)" : "rgba(99,102,241,.15)",
                      color:      agent.role === "Main" ? "var(--accent)"        : "#818cf8",
                    }}>{agent.role}</span>
                  </div>
                  <div style={{ fontSize: 10, color: "var(--text3)", marginTop: 2, fontFamily: "IBM Plex Mono" }}>
                    {agent.employeeId}
                    {agent.shiftStart && agent.shiftEnd
                      ? <span style={{ marginLeft: 8, color: covers ? "#22c55e" : "var(--text3)" }}>
                          {agent.shiftStart}–{agent.shiftEnd}
                          {covers ? " ✓" : " ⚠ outside shift"}
                        </span>
                      : <span style={{ marginLeft: 8, color: "var(--text3)" }}>no shift scheduled</span>
                    }
                  </div>
                </div>

                {/* Assign button */}
                <button
                  onClick={() => assign(agent)}
                  disabled={isSaving}
                  style={{
                    background: covers ? "var(--accent)" : "var(--card2)",
                    border: `1px solid ${covers ? "var(--accent)" : "var(--border)"}`,
                    color:  covers ? "#fff" : "var(--text2)",
                    padding: "6px 14px", borderRadius: 6, fontSize: 12,
                    cursor: isSaving ? "wait" : "pointer",
                    fontWeight: 600, whiteSpace: "nowrap",
                    opacity: isSaving ? 0.6 : 1,
                  }}
                >
                  {isSaving ? "Assigning…" : "Assign"}
                </button>
              </div>
            )
          })}
        </div>

        {error && (
          <div style={{ padding: "10px 20px", borderTop: "1px solid var(--border)", fontSize: 12, color: "#ef4444" }}>
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
    queryFn:  () => fetch("/api/vwic/candidates").then(r => r.json()),
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
      style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.55)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={onClose}
    >
      <div
        style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10, width: 420, padding: 24, display: "flex", flexDirection: "column", gap: 16 }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <span style={{ fontWeight: 700, fontSize: 15 }}>Add Agent to VWIC</span>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text2)", fontSize: 18, cursor: "pointer", lineHeight: 1 }}>✕</button>
        </div>

        <div style={{ fontSize: 12, color: "var(--text3)" }}>
          Sets SecondaryRole = VWIC so the agent appears in the Backup Pool.
        </div>

        {isLoading ? (
          <div style={{ color: "var(--text3)", fontSize: 13 }}>Loading employees…</div>
        ) : (
          <select
            value={selected}
            onChange={e => setSelected(e.target.value)}
            style={{ background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)", padding: "8px 12px", borderRadius: 6, fontSize: 13, width: "100%" }}
          >
            <option value="">Select employee…</option>
            {candidates?.map(c => (
              <option key={c.employeeId} value={c.employeeId}>
                {c.fullName ?? c.employeeId}{c.primaryRole ? ` · ${c.primaryRole}` : ""}
              </option>
            ))}
          </select>
        )}

        {error && <div style={{ fontSize: 12, color: "#ef4444" }}>{error}</div>}

        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button onClick={onClose}
            style={{ background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)", padding: "7px 16px", borderRadius: 6, fontSize: 13, cursor: "pointer" }}>
            Cancel
          </button>
          <button
            onClick={() => { setError(null); mutation.mutate(selected) }}
            disabled={!selected || mutation.isPending}
            style={{ background: "var(--accent)", color: "#fff", border: "none", padding: "7px 16px", borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: selected && !mutation.isPending ? "pointer" : "not-allowed", opacity: !selected || mutation.isPending ? 0.6 : 1 }}
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
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: 16 }}>
      <div style={{ fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", marginBottom: 12 }}>
        Coverage Timeline · 07:00 – 18:00
        <span style={{ marginLeft: 10, fontWeight: 400, color: "var(--text3)", textTransform: "none", letterSpacing: 0 }}>
          — click a red or orange slot to assign an agent
        </span>
      </div>

      <div style={{ display: "flex", gap: 3 }}>
        {timeline.map((slot, i) => {
          const isGap     = !slot.hasMainAgent
          const isOrange  = slot.hasAnyAgent && !slot.hasMainAgent
          const borderCol = slot.hasMainAgent ? "#22c55e" : isOrange ? "#f97316" : "#ef4444"
          const bgCol     = borderCol + "22"
          const isHov     = hovered === slot.hour
          const isLast    = i >= timeline.length - 3
          const clickable = isGap // red or orange

          return (
            <div
              key={slot.hour}
              onMouseEnter={() => setHovered(slot.hour)}
              onMouseLeave={() => setHovered(null)}
              onClick={() => clickable && onSlotClick(slot)}
              style={{ flex: 1, position: "relative" }}
            >
              {/* Hour cell */}
              <div style={{
                background:    bgCol,
                border:        `1px solid ${borderCol}`,
                borderRadius:  4,
                padding:       "8px 3px",
                textAlign:     "center",
                cursor:        clickable ? "pointer" : "default",
                opacity:       isHov ? 0.75 : 1,
                transition:    "opacity .12s, transform .1s",
                transform:     isHov && clickable ? "translateY(-2px)" : "none",
                boxShadow:     isHov && clickable ? `0 4px 12px ${borderCol}44` : "none",
              }}>
                <div style={{ fontSize: 9, color: "var(--text3)", marginBottom: 3 }}>{slot.label}</div>
                <div style={{ fontSize: 16, fontWeight: 700, fontFamily: "IBM Plex Mono", color: borderCol, lineHeight: 1 }}>
                  {slot.mainAgents.length + slot.backupAgents.length}
                </div>
                <div style={{ fontSize: 9, color: "var(--text3)", marginTop: 2 }}>
                  {slot.mainAgents.length   > 0 ? `${slot.mainAgents.length}M`   : ""}
                  {slot.backupAgents.length > 0 ? ` ${slot.backupAgents.length}B` : ""}
                  {!slot.hasAnyAgent ? "—" : ""}
                </div>
                {/* Clickable indicator */}
                {clickable && (
                  <div style={{ fontSize: 8, color: borderCol, marginTop: 3, opacity: 0.8 }}>+ assign</div>
                )}
              </div>

              {/* Hover tooltip (non-empty slots) */}
              {isHov && slot.hasAnyAgent && (
                <div style={{
                  position: "absolute",
                  top: "calc(100% + 6px)",
                  [isLast ? "right" : "left"]: 0,
                  zIndex: 100,
                  background: "var(--card2)", border: "1px solid var(--border)",
                  borderRadius: 6, padding: "10px 14px", minWidth: 160,
                  fontSize: 11, color: "var(--text)", whiteSpace: "nowrap",
                  boxShadow: "0 4px 16px rgba(0,0,0,.35)",
                  pointerEvents: "none",
                }}>
                  <div style={{ fontSize: 11, fontWeight: 600, color: "var(--text2)", marginBottom: 8 }}>
                    {slot.label} – {String(slot.hour + 1).padStart(2, "0")}:00
                  </div>
                  {slot.mainAgents.length > 0 && (
                    <>
                      <div style={{ fontSize: 10, color: "#22c55e", fontWeight: 600, marginBottom: 4 }}>Main</div>
                      {slot.mainAgents.map(n => <div key={n} style={{ marginBottom: 3, paddingLeft: 8 }}>· {n}</div>)}
                    </>
                  )}
                  {slot.backupAgents.length > 0 && (
                    <>
                      <div style={{ fontSize: 10, color: "#f97316", fontWeight: 600, marginTop: 8, marginBottom: 4 }}>Backup</div>
                      {slot.backupAgents.map(n => <div key={n} style={{ marginBottom: 3, paddingLeft: 8 }}>· {n}</div>)}
                    </>
                  )}
                </div>
              )}
            </div>
          )
        })}
      </div>

      {gaps.length > 0 && (
        <div style={{ marginTop: 12, padding: "8px 14px", background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.3)", borderRadius: 6, fontSize: 12, color: "#ef4444" }}>
          No Main agent coverage at:{" "}
          {gaps.map(h => `${String(h).padStart(2, "0")}:00–${String(h + 1).padStart(2, "0")}:00`).join(", ")}
        </div>
      )}
    </div>
  )
}

// ─── Agent table ─────────────────────────────────────────────────────────────

const ABSENCE_COLOR: Record<string, string> = {
  SL: "#ef4444", AL: "#facc15", UL: "#facc15",
  OFF: "#64748b", PH: "#64748b", OFF_WEEKEND: "#64748b",
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
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
      <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span style={{ fontWeight: 600, fontSize: 13 }}>{title}</span>
        <span style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
          {available}/{agents.length} available
        </span>
      </div>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
        <thead>
          <tr style={{ background: "var(--card2)" }}>
            {["Name", "Team Lead", "Shift", "Status", ...(onRemove ? [""] : [])].map((h, i) => (
              <th key={i} style={{ padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", borderBottom: "1px solid var(--border)", whiteSpace: "nowrap" }}>{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {agents.length === 0 && (
            <tr><td colSpan={cols} style={{ padding: 20, textAlign: "center", color: "var(--text3)", fontSize: 12 }}>No agents</td></tr>
          )}
          {agents.map(a => (
            <tr key={a.employeeId}
              style={{ borderBottom: "1px solid var(--border)", opacity: a.isAbsent ? 0.55 : 1 }}
              onMouseEnter={ev => (ev.currentTarget.style.background = "rgba(255,255,255,.02)")}
              onMouseLeave={ev => (ev.currentTarget.style.background = "")}>

              <td style={{ padding: "9px 12px" }}>
                <div style={{ fontWeight: 600 }}>{a.fullName ?? a.employeeId}</div>
                <div style={{ fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono", marginTop: 1 }}>{a.employeeId}</div>
              </td>
              <td style={{ padding: "9px 12px", color: "var(--text2)", fontSize: 11 }}>{a.teamLeadName ?? "—"}</td>
              <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: a.isAbsent ? "var(--text3)" : "var(--text2)" }}>
                {a.isAbsent
                  ? "—"
                  : a.shiftStart && a.shiftEnd
                    ? `${a.shiftStart} – ${a.shiftEnd}`
                    : a.shiftType ?? "No shift"}
              </td>
              <td style={{ padding: "9px 12px" }}>
                {a.isAbsent ? (
                  <span style={{ background: (ABSENCE_COLOR[a.absenceType!] ?? "#64748b") + "22", color: ABSENCE_COLOR[a.absenceType!] ?? "#64748b", padding: "2px 8px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono" }}>
                    {a.absenceType}
                  </span>
                ) : a.isVwicAssigned ? (
                  <span style={{ background: "rgba(59,126,255,.15)", color: "var(--accent)", padding: "2px 8px", borderRadius: 4, fontSize: 10, fontWeight: 600 }}>VWIC</span>
                ) : a.shiftStart && a.shiftEnd ? (
                  <span style={{ background: "rgba(34,197,94,.15)", color: "#22c55e", padding: "2px 8px", borderRadius: 4, fontSize: 10 }}>Working</span>
                ) : (
                  <span style={{ background: "rgba(99,102,241,.15)", color: "#818cf8", padding: "2px 8px", borderRadius: 4, fontSize: 10 }}>No shift</span>
                )}
              </td>
              {onRemove && (
                <td style={{ padding: "9px 12px" }}>
                  <button
                    onClick={() => onRemove(a)}
                    style={{ background: "rgba(239,68,68,.1)", border: "1px solid rgba(239,68,68,.3)", color: "#ef4444", padding: "3px 10px", borderRadius: 4, fontSize: 11, cursor: "pointer" }}
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
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px 20px" }}>
      <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>{label}</div>
      <div style={{ fontSize: 26, fontWeight: 700, fontFamily: "IBM Plex Mono", color }}>{value}</div>
      {sub && <div style={{ fontSize: 10, color: "var(--text3)", marginTop: 4 }}>{sub}</div>}
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

  const calculate = async () => {
    setLoading(true)
    setError(null)
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

  const maxVwicHours = result ? Math.max(...result.fairness.map(f => f.vwicHours), 1) : 1
  const allCovered   = result?.coverageProof.every(c => c.covered) ?? false

  const statusStyle = (s: string) =>
    s === "ON"       ? { bg: "rgba(34,197,94,.18)",  fg: "#22c55e", label: "ON" }
    : s === "HANDOVER" ? { bg: "rgba(250,204,21,.18)", fg: "#facc15", label: "HO" }
    : { bg: "transparent", fg: "var(--text3)", label: "—" }

  const inp: React.CSSProperties = {
    background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)",
    padding: "7px 10px", borderRadius: 6, fontSize: 13, outline: "none", width: "100%",
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* ── Parameters form ── */}
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: 20 }}>
        <div style={{ fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", marginBottom: 14 }}>
          Rotation Parameters
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(190px, 1fr))", gap: 14 }}>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>Date</span>
            <input type="date" value={date} onChange={e => setDate(e.target.value)} style={inp} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>Window start</span>
            <input type="time" value={startTime} onChange={e => setStartTime(e.target.value)} style={inp} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>Window end</span>
            <input type="time" value={endTime} onChange={e => setEndTime(e.target.value)} style={inp} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>Max continuous VWIC (h)</span>
            <input type="number" min={1} max={8} value={maxContinuousHours}
              onChange={e => setMaxContinuousHours(+e.target.value)} style={inp} />
          </label>
          <label style={{ display: "flex", flexDirection: "column", gap: 5 }}>
            <span style={{ fontSize: 11, color: "var(--text3)" }}>Handover buffer (min)</span>
            <input type="number" min={0} max={30} value={handoverMinutes}
              onChange={e => setHandoverMinutes(+e.target.value)} style={inp} />
          </label>
        </div>

        {/* Interval radio */}
        <div style={{ marginTop: 14 }}>
          <div style={{ fontSize: 11, color: "var(--text3)", marginBottom: 8 }}>Rotation interval</div>
          <div style={{ display: "flex", gap: 8 }}>
            {([1, 2, 4] as const).map(h => (
              <label key={h} style={{
                display: "flex", alignItems: "center", gap: 6, cursor: "pointer",
                background: intervalHours === h ? "rgba(59,126,255,.15)" : "var(--card2)",
                border: `1px solid ${intervalHours === h ? "var(--accent)" : "var(--border)"}`,
                borderRadius: 6, padding: "6px 16px", transition: "all .12s",
              }}>
                <input type="radio" name="rp-interval" value={h} checked={intervalHours === h}
                  onChange={() => setIntervalHours(h)}
                  style={{ accentColor: "var(--accent)", margin: 0 }} />
                <span style={{
                  fontSize: 13, fontWeight: intervalHours === h ? 700 : 400,
                  color: intervalHours === h ? "var(--accent)" : "var(--text2)",
                }}>
                  {h}h
                </span>
              </label>
            ))}
          </div>
        </div>

        <div style={{ marginTop: 16, display: "flex", alignItems: "center", gap: 12 }}>
          <button
            onClick={calculate} disabled={loading}
            style={{
              background: "var(--accent)", color: "#fff", border: "none",
              padding: "9px 22px", borderRadius: 6, fontSize: 13, fontWeight: 600,
              cursor: loading ? "wait" : "pointer", opacity: loading ? 0.7 : 1,
            }}
          >
            {loading ? "Calculating…" : "Calculate Rotation"}
          </button>
          {error && <span style={{ fontSize: 12, color: "#ef4444" }}>{error}</span>}
        </div>
      </div>

      {result && (
        <>
          {/* ── Recommendation ── */}
          <div style={{
            background: "var(--card)",
            border: `1px solid ${result.availableAgents < 3 ? "rgba(239,68,68,.35)" : "rgba(34,197,94,.35)"}`,
            borderRadius: 8, padding: "14px 20px", display: "flex", alignItems: "flex-start", gap: 14,
          }}>
            <div style={{ fontSize: 24, lineHeight: 1, flexShrink: 0 }}>
              {result.availableAgents < 3 ? "⚠️" : "✓"}
            </div>
            <div>
              <div style={{
                fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: ".08em",
                color: result.availableAgents < 3 ? "#f97316" : "#22c55e", marginBottom: 5,
              }}>
                Recommendation
              </div>
              <div style={{ fontSize: 13, color: "var(--text)", lineHeight: 1.6 }}>{result.recommendation}</div>
              <div style={{ marginTop: 6, fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                {result.availableAgents} agents available &nbsp;·&nbsp;
                {result.requiredAgentHours.toFixed(0)}h required &nbsp;·&nbsp;
                {result.availableAgentHours.toFixed(0)}h available capacity
              </div>
            </div>
          </div>

          {/* ── Schedule table ── */}
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
            <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <span style={{ fontWeight: 600, fontSize: 13 }}>Rotation Schedule</span>
              <div style={{ display: "flex", gap: 14, fontSize: 11, color: "var(--text3)" }}>
                <span><span style={{ color: "#22c55e", fontWeight: 700 }}>ON</span> — on VWIC</span>
                <span><span style={{ color: "#facc15", fontWeight: 700 }}>HO</span> — handover</span>
                <span><span style={{ color: "var(--text3)" }}>—</span> — off</span>
              </div>
            </div>
            <div style={{ overflowX: "auto" }}>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                <thead>
                  <tr style={{ background: "var(--card2)" }}>
                    <th style={{ padding: "8px 14px", textAlign: "left", fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".06em", color: "var(--text3)", borderBottom: "1px solid var(--border)", whiteSpace: "nowrap", minWidth: 110 }}>
                      Slot
                    </th>
                    {result.schedule.map(row => (
                      <th key={row.employeeId} style={{ padding: "8px 10px", textAlign: "center", fontSize: 11, fontWeight: 600, color: "var(--text2)", borderBottom: "1px solid var(--border)", whiteSpace: "nowrap", minWidth: 85 }}>
                        {row.fullName.includes(" ") ? row.fullName.split(" ")[0] : row.fullName}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.slotLabels.map((label, si) => (
                    <tr key={si} style={{ borderBottom: "1px solid var(--border)" }}
                      onMouseEnter={e => (e.currentTarget.style.background = "rgba(255,255,255,.02)")}
                      onMouseLeave={e => (e.currentTarget.style.background = "")}>
                      <td style={{ padding: "7px 14px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)", fontWeight: 600, whiteSpace: "nowrap" }}>
                        {label}
                      </td>
                      {result.schedule.map(row => {
                        const ss = statusStyle(row.slotStatus[si] ?? "OFF")
                        return (
                          <td key={row.employeeId} style={{ padding: "5px 8px", textAlign: "center", background: ss.bg }}>
                            <span style={{ fontSize: 10, fontWeight: 700, color: ss.fg, fontFamily: "IBM Plex Mono" }}>
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
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
              <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <span style={{ fontWeight: 600, fontSize: 13 }}>Coverage Proof</span>
                <span style={{ fontSize: 11, fontFamily: "IBM Plex Mono", color: allCovered ? "#22c55e" : "#ef4444" }}>
                  {allCovered ? "All slots ✓" : "Gaps detected ✗"}
                </span>
              </div>
              <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                <thead>
                  <tr style={{ background: "var(--card2)" }}>
                    {["Slot", "Agents", "Min", ""].map((h, i) => (
                      <th key={i} style={{ padding: "7px 12px", textAlign: i < 3 ? "left" : "center", fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".06em", color: "var(--text3)", borderBottom: "1px solid var(--border)" }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.coverageProof.map((item, i) => (
                    <tr key={i} style={{ borderBottom: "1px solid var(--border)", background: item.covered ? "transparent" : "rgba(239,68,68,.06)" }}>
                      <td style={{ padding: "7px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, whiteSpace: "nowrap" }}>{item.startTime}–{item.endTime}</td>
                      <td style={{ padding: "7px 12px", fontWeight: 700, color: item.covered ? "#22c55e" : "#ef4444" }}>{item.agentCount}</td>
                      <td style={{ padding: "7px 12px", color: "var(--text3)" }}>{item.required}</td>
                      <td style={{ padding: "7px 12px", textAlign: "center", fontSize: 14 }}>
                        {item.covered ? <span style={{ color: "#22c55e" }}>✓</span> : <span style={{ color: "#ef4444" }}>✗</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Fairness */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
              <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)" }}>
                <span style={{ fontWeight: 600, fontSize: 13 }}>Fairness Overview</span>
              </div>
              <div style={{ padding: "14px 16px", display: "flex", flexDirection: "column", gap: 12 }}>
                {result.fairness.map(item => (
                  <div key={item.employeeId}>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 5 }}>
                      <span style={{ fontSize: 12, fontWeight: 600 }}>{item.fullName}</span>
                      <span style={{ fontSize: 11, fontFamily: "IBM Plex Mono", color: "var(--text3)" }}>
                        {item.vwicHours}h &nbsp;·&nbsp; {item.slotCount} slot{item.slotCount !== 1 ? "s" : ""}
                      </span>
                    </div>
                    <div style={{ height: 6, background: "var(--card2)", borderRadius: 3, overflow: "hidden" }}>
                      <div style={{
                        height: "100%",
                        width: `${(item.vwicHours / maxVwicHours) * 100}%`,
                        background: "var(--accent)", borderRadius: 3, transition: "width .3s ease",
                      }} />
                    </div>
                  </div>
                ))}
                {result.fairness.length === 0 && (
                  <div style={{ color: "var(--text3)", fontSize: 12 }}>No agent data</div>
                )}
              </div>
            </div>
          </div>

          {/* ── Fallback warning ── */}
          {result.fallbackWarning && (() => {
            const isCritical = result.fallbackWarning.startsWith("CRITICAL")
            const isWarn     = result.fallbackWarning.startsWith("1 absence leaves")
            return (
              <div style={{
                background: isCritical ? "rgba(239,68,68,.08)" : isWarn ? "rgba(251,191,36,.08)" : "rgba(34,197,94,.08)",
                border: `1px solid ${isCritical ? "rgba(239,68,68,.3)" : isWarn ? "rgba(251,191,36,.3)" : "rgba(34,197,94,.3)"}`,
                borderRadius: 8, padding: "12px 18px",
              }}>
                <div style={{
                  fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: ".08em",
                  color: isCritical ? "#ef4444" : isWarn ? "#f59e0b" : "#22c55e", marginBottom: 5,
                }}>
                  Fallback Warning
                </div>
                <div style={{ fontSize: 13, color: "var(--text)", lineHeight: 1.5 }}>{result.fallbackWarning}</div>
              </div>
            )
          })()}
        </>
      )}
    </div>
  )
}

// ─── Page ─────────────────────────────────────────────────────────────────────

const btnStyle: React.CSSProperties = {
  background: "var(--card)", border: "1px solid var(--border)",
  color: "var(--text2)", padding: "6px 12px", borderRadius: 6,
  fontSize: 12, cursor: "pointer",
}

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

  const removeMutation = useMutation({
    mutationFn: (employeeId: string) =>
      fetch("/api/vwic/agents/remove", {
        method:  "PUT",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({ employeeId }),
      }).then(async r => {
        const d = await r.json()
        if (!r.ok) throw new Error(d.error ?? "Server error")
        return d
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["vwic-daily", date] })
      queryClient.invalidateQueries({ queryKey: ["vwic-candidates"] })
    },
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
  const hasCoverage  = (data?.coveredHours ?? 0) === (data?.totalHours ?? 11)

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>

      {/* Modals */}
      {assignSlot && data && (
        <AssignSlotModal
          slot={assignSlot}
          date={date}
          agents={data.agents}
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
          <h1 style={{ fontSize: 22, fontWeight: 700, color: "var(--text)", margin: 0 }}>VWIC</h1>
          <div style={{ fontSize: 12, color: "var(--text3)", marginTop: 3 }}>
            Virtual Walk-In Center · 07:00 – 18:00 · at least 1 Main agent required at all times
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <button
            onClick={() => setShowAddModal(true)}
            style={{ ...btnStyle, background: "var(--accent)", color: "#fff", border: "none", fontWeight: 600 }}
          >
            + Add Agent to VWIC
          </button>
          <div style={{ width: 1, height: 24, background: "var(--border)", margin: "0 4px" }} />
          <button onClick={() => shift(-1)} style={btnStyle}>←</button>
          <input type="date" value={date} onChange={e => setDate(e.target.value)}
            style={{ ...btnStyle, fontFamily: "IBM Plex Mono", outline: "none" }} />
          <button onClick={() => setDate(new Date().toISOString().split("T")[0])}
            style={{ ...btnStyle, background: "var(--card2)", color: "var(--text2)" }}>
            Today
          </button>
          <button onClick={() => shift(1)} style={btnStyle}>→</button>
        </div>
      </div>

      {/* Tab bar */}
      <div style={{ display: "flex", gap: 2, background: "var(--card2)", borderRadius: 8, padding: 3, alignSelf: "flex-start" }}>
        {(["coverage", "rotation"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} style={{
            padding: "6px 18px", borderRadius: 6, border: "none", fontSize: 13, fontWeight: 600,
            cursor: "pointer", transition: "all .12s",
            background: tab === t ? "var(--card)" : "transparent",
            color:      tab === t ? "var(--text)"  : "var(--text3)",
            boxShadow:  tab === t ? "0 1px 4px rgba(0,0,0,.18)" : "none",
          }}>
            {t === "coverage" ? "Coverage" : "Rotation Planner"}
          </button>
        ))}
      </div>

      {tab === "coverage" && (
        <>
          {isLoading && <div style={{ padding: 48, textAlign: "center", color: "var(--text3)" }}>Loading…</div>}

          {isError && (
            <div style={{ padding: 24, background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.3)", borderRadius: 8, color: "#ef4444", fontSize: 13 }}>
              Failed to load VWIC data. Check that the backend is running.
            </div>
          )}

          {data && (
            <>
              {/* Summary cards */}
              <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
                <SummaryCard label="Main Agents"   value={`${activeMain}/${mainAgents.length}`}     color="var(--accent)" sub="available today" />
                <SummaryCard label="Backup Pool"   value={`${activeBackup}/${backupAgents.length}`} color="var(--text2)"  sub="available today" />
                <SummaryCard label="Coverage"      value={`${data.coveredHours}/${data.totalHours}h`}
                  color={hasCoverage ? "#22c55e" : "#ef4444"} sub="hours with Main agent" />
                <SummaryCard label="Gaps"          value={data.gaps.length}
                  color={data.gaps.length === 0 ? "#22c55e" : "#ef4444"}
                  sub={data.gaps.length === 0 ? "fully covered" : "hours without Main"} />
              </div>

              {/* Timeline */}
              <CoverageTimeline
                timeline={data.timeline}
                gaps={data.gaps}
                onSlotClick={slot => setAssignSlot(slot)}
              />

              {/* Agent tables */}
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1.6fr", gap: 16 }}>
                <AgentTable title="Main Agents" agents={mainAgents} />
                <AgentTable title="Backup Pool" agents={backupAgents} onRemove={a => removeMutation.mutate(a.employeeId)} />
              </div>
            </>
          )}
        </>
      )}

      {tab === "rotation" && <RotationPlanner initialDate={date} />}
    </div>
  )
}
