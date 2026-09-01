import { useState, useMemo } from "react"
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { Coffee, AlertTriangle, CheckCircle, Clock, X, Shuffle } from "lucide-react"

// ─── Types ────────────────────────────────────────────────────────────────────

interface BreakSlotDto {
  id:              number
  employeeId:      string
  fullName:        string | null
  teamLeadName:    string | null
  breakDate:       string
  breakStart:      string
  breakEnd:        string
  actualStart:     string | null
  actualEnd:       string | null
  durationMinutes: number
  status:          "SCHEDULED" | "ON_BREAK" | "DONE" | "CANCELLED"
  agentRole:       string | null
}

interface BreakDistributeResult {
  date:               string
  scheduled:          number
  unscheduled:        number
  maxVwicConcurrent:  number
  maxVoiceConcurrent: number
  totalVwic:          number
  totalVoice:         number
  unscheduledAgents:  string[]
  slots:              BreakSlotDto[]
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

const API_BASE = (import.meta as any).env?.VITE_API_BASE_URL ?? ""

function timeToMin(t: string): number {
  const [h, m] = t.split(":").map(Number)
  return h * 60 + (m || 0)
}

function minToTime(m: number): string {
  return `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`
}

function nowHHMM(): string {
  const d = new Date()
  return `${String(d.getHours()).padStart(2,"0")}:${String(d.getMinutes()).padStart(2,"0")}`
}

function overlaps(aStart: number, aEnd: number, bStart: number, bEnd: number) {
  return aStart < bEnd && bStart < aEnd
}

async function apiFetch<T>(path: string, opts?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, opts)
  if (!res.ok) throw new Error(`${res.status}`)
  return res.json()
}

function post(path: string, body?: unknown) {
  return apiFetch<any>(path, {
    method: "POST",
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
}

// ─── Status badge ─────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls: Record<string, string> = {
    SCHEDULED: "bg-info-bg text-info-fg",
    ON_BREAK:  "bg-warn-bg text-warn-fg",
    DONE:      "bg-good-bg text-good-fg",
    CANCELLED: "bg-neutralst-bg text-neutralst-fg",
  }
  const c = cls[status] ?? cls.CANCELLED
  return (
    <span className={`font-mono ${c}`} style={{
      fontSize: 10, fontWeight: 700,
      padding: "2px 7px", borderRadius: 4, letterSpacing: 0.5
    }}>{status}</span>
  )
}

// ─── Timeline visualization ───────────────────────────────────────────────────

function Timeline({
  slots, windowStart, windowEnd, maxVwic, maxVoice
}: {
  slots: BreakSlotDto[]
  windowStart: string
  windowEnd: string
  maxVwic: number
  maxVoice: number
}) {
  const wsMin = timeToMin(windowStart)
  const weMin = timeToMin(windowEnd)
  const total = weMin - wsMin
  if (total <= 0) return null

  const active = slots.filter(s => s.status !== "CANCELLED")

  const concSlots: number[] = []
  for (let t = wsMin; t < weMin; t += 15) concSlots.push(t)

  const timeLabels: string[] = []
  for (let t = wsMin; t <= weMin; t += 30) timeLabels.push(minToTime(t))

  function barStyle(s: BreakSlotDto) {
    const start = timeToMin(s.actualStart ?? s.breakStart)
    const end   = timeToMin(s.breakEnd)
    const left  = Math.max(0, (start - wsMin) / total * 100)
    const width = Math.min(100 - left, (end - start) / total * 100)
    const isLate = s.actualStart && Math.abs(timeToMin(s.actualStart) - timeToMin(s.breakStart)) > 15
    const bg = s.status === "ON_BREAK"  ? "rgb(var(--st-warn-solid))"
             : s.status === "DONE"      ? "rgb(var(--st-good-solid))"
             : s.agentRole === "VWIC"   ? "rgb(var(--st-info-solid))"
             : "rgb(var(--st-good-solid))"
    return {
      position: "absolute" as const,
      left: `${left}%`, width: `${width}%`, top: 4, height: 16,
      background: bg, borderRadius: 3, opacity: s.status === "DONE" ? 0.55 : 0.9,
      border: isLate ? "2px solid rgb(var(--st-warn-solid))" : "none",
      transition: "all .2s"
    }
  }

  function concCount(slotStart: number, role: "VWIC" | "Voice") {
    return active.filter(s =>
      s.agentRole === role &&
      overlaps(
        timeToMin(s.actualStart ?? s.breakStart),
        timeToMin(s.breakEnd),
        slotStart, slotStart + 15
      )
    ).length
  }

  return (
    <div style={{ overflowX: "auto" }}>
      <div style={{ display: "flex", marginBottom: 4, marginLeft: 140 }}>
        {timeLabels.map(l => (
          <div key={l}
            className="font-mono text-ink-soft"
            style={{
              flex: 1, fontSize: 10,
              textAlign: l === timeLabels[timeLabels.length - 1] ? "right" : "left"
            }}>{l}</div>
        ))}
      </div>

      {active.length === 0 && (
        <div className="text-ink-soft" style={{ fontSize: 12, padding: "8px 0", marginLeft: 140 }}>
          No breaks scheduled — click Auto-Distribute to generate a plan.
        </div>
      )}
      {active.map(s => (
        <div key={s.id} style={{ display: "flex", alignItems: "center", marginBottom: 3 }}>
          <div className="text-ink-muted" style={{
            width: 140, fontSize: 11, flexShrink: 0,
            display: "flex", alignItems: "center", gap: 5, overflow: "hidden"
          }}>
            <span
              className={`font-mono ${s.agentRole === "VWIC" ? "bg-info-bg text-info-fg" : "bg-good-bg text-good-fg"}`}
              style={{ fontSize: 9, fontWeight: 700, padding: "1px 4px", borderRadius: 3, flexShrink: 0 }}
            >{s.agentRole}</span>
            <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
              {s.fullName ?? s.employeeId}
            </span>
          </div>
          <div className="bg-sunken" style={{ flex: 1, height: 24, position: "relative", borderRadius: 4 }}>
            <div style={barStyle(s)} title={`${s.breakStart}–${s.breakEnd}${s.actualStart ? ` (actual: ${s.actualStart})` : ""}`} />
          </div>
        </div>
      ))}

      {active.length > 0 && (
        <div style={{ marginTop: 8, marginLeft: 140, display: "flex" }}>
          {concSlots.map(t => {
            const vwicN  = concCount(t, "VWIC")
            const voiceN = concCount(t, "Voice")
            const over   = (maxVwic  > 0 && vwicN  >= maxVwic)
                        || (maxVoice > 0 && voiceN >= maxVoice)
            const pct    = 15 / total * 100
            return (
              <div key={t}
                className={`font-mono ${over ? "bg-crit-bg text-crit-fg" : "text-ink-soft"}`}
                style={{ width: `${pct}%`, textAlign: "center", fontSize: 10, borderRadius: 2, padding: "1px 0" }}>
                {vwicN + voiceN > 0 ? vwicN + voiceN : "·"}
              </div>
            )
          })}
        </div>
      )}
      {active.length > 0 && (
        <div className="text-ink-soft" style={{ marginLeft: 140, fontSize: 10, marginTop: 2 }}>
          concurrent on-break count per 15 min · red = at limit
        </div>
      )}
    </div>
  )
}

// ─── Manual break modal ───────────────────────────────────────────────────────

function ManualModal({
  date, onClose, onSave
}: { date: string; onClose: () => void; onSave: (r: any) => void }) {
  const [empId, setEmpId]     = useState("")
  const [start, setStart]     = useState("12:00")
  const [dur,   setDur]       = useState(30)

  return (
    <div style={{
      position: "fixed", inset: 0, background: "rgba(0,0,0,.5)",
      display: "flex", alignItems: "center", justifyContent: "center", zIndex: 1000
    }}>
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 10, padding: 24, width: 340 }}>
        <div className="text-ink" style={{ fontWeight: 700, marginBottom: 16 }}>Manual Break Slot</div>
        <label className="text-ink-muted" style={{ fontSize: 11, display: "block", marginBottom: 4 }}>Employee ID</label>
        <input value={empId} onChange={e => setEmpId(e.target.value)}
          placeholder="e.g. E12345"
          className="bg-sunken border border-line-subtle text-ink"
          style={{
            width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13,
            boxSizing: "border-box", marginBottom: 12
          }} />
        <div style={{ display: "flex", gap: 10, marginBottom: 16 }}>
          <div style={{ flex: 1 }}>
            <label className="text-ink-muted" style={{ fontSize: 11, display: "block", marginBottom: 4 }}>Start time</label>
            <input type="time" value={start} onChange={e => setStart(e.target.value)}
              className="bg-sunken border border-line-subtle text-ink"
              style={{ width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13 }} />
          </div>
          <div style={{ flex: 1 }}>
            <label className="text-ink-muted" style={{ fontSize: 11, display: "block", marginBottom: 4 }}>Duration (min)</label>
            <input type="number" value={dur} onChange={e => setDur(Number(e.target.value))} min={15} max={60}
              className="bg-sunken border border-line-subtle text-ink"
              style={{ width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13 }} />
          </div>
        </div>
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button onClick={onClose}
            className="bg-sunken border border-line-subtle text-ink-muted"
            style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer" }}>Cancel</button>
          <button onClick={() => empId && onSave({ employeeId: empId.trim(), date, breakStart: start, durationMinutes: dur })}
            className="bg-info-solid text-white"
            style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer", fontWeight: 600 }}>Save</button>
        </div>
      </div>
    </div>
  )
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function BreakPlanner() {
  const today = new Date().toISOString().split("T")[0]
  const qc    = useQueryClient()

  const [date,         setDate]         = useState(today)
  const [windowStart,  setWindowStart]  = useState("11:30")
  const [windowEnd,    setWindowEnd]    = useState("14:30")
  const [voiceMinPct,  setVoiceMinPct]  = useState(70)
  const [showManual,   setShowManual]   = useState(false)
  const [lastResult,   setLastResult]   = useState<BreakDistributeResult | null>(null)

  const { data: breaks = [], isLoading } = useQuery<BreakSlotDto[]>({
    queryKey: ["breaks", date],
    queryFn:  () => apiFetch(`/api/breaks?date=${date}`),
    refetchInterval: 30_000,
  })

  const invalidate = () => qc.invalidateQueries({ queryKey: ["breaks", date] })

  const distributeMut = useMutation({
    mutationFn: () => post("/api/breaks/auto-distribute", {
      date, windowStart, windowEnd, voiceMinPct: voiceMinPct / 100
    }),
    onSuccess: (data: BreakDistributeResult) => { setLastResult(data); invalidate() }
  })

  const startMut  = useMutation({ mutationFn: (id: number) => post(`/api/breaks/${id}/start`),  onSuccess: invalidate })
  const endMut    = useMutation({ mutationFn: (id: number) => post(`/api/breaks/${id}/end`),    onSuccess: invalidate })
  const cancelMut = useMutation({ mutationFn: (id: number) => post(`/api/breaks/${id}/cancel`), onSuccess: invalidate })
  const manualMut = useMutation({ mutationFn: (body: any)  => post("/api/breaks/manual", body), onSuccess: () => { setShowManual(false); invalidate() } })

  const now = nowHHMM()
  const nowMin = timeToMin(now)

  const kpi = useMemo(() => {
    const scheduled  = breaks.filter(b => b.status === "SCHEDULED").length
    const onBreak    = breaks.filter(b => b.status === "ON_BREAK").length
    const done       = breaks.filter(b => b.status === "DONE").length
    const autoOnBreak = breaks.filter(b =>
      b.status === "SCHEDULED" &&
      overlaps(timeToMin(b.breakStart), timeToMin(b.breakEnd), nowMin, nowMin + 1)
    ).length
    const effectiveOnBreak = onBreak + autoOnBreak
    const vwicOnBreak = breaks.filter(b =>
      b.agentRole === "VWIC" &&
      (b.status === "ON_BREAK" ||
       (b.status === "SCHEDULED" && overlaps(timeToMin(b.breakStart), timeToMin(b.breakEnd), nowMin, nowMin + 1)))
    ).length
    return { scheduled, onBreak: effectiveOnBreak, done, vwicOnBreak }
  }, [breaks, nowMin])

  const maxVwic  = lastResult?.maxVwicConcurrent  ?? 0
  const maxVoice = lastResult?.maxVoiceConcurrent ?? 0

  const unscheduled = lastResult?.unscheduledAgents ?? []
  const busy = distributeMut.isPending || startMut.isPending || endMut.isPending || cancelMut.isPending || manualMut.isPending

  const btn = (label: string, onClick: () => void, cls = "bg-sunken text-ink-muted", disabled = false) => (
    <button
      onClick={onClick}
      disabled={disabled || busy}
      className={`border border-line-subtle ${cls}`}
      style={{
        padding: "5px 11px", borderRadius: 5, fontSize: 11, cursor: disabled ? "not-allowed" : "pointer",
        fontWeight: 600, opacity: disabled ? 0.45 : 1, display: "flex", alignItems: "center", gap: 4
      }}
    >{label}</button>
  )

  return (
    <div style={{ maxWidth: 1100, margin: "0 auto" }}>

      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 20 }}>
        <Coffee size={20} className="text-info-fg" />
        <span className="text-ink" style={{ fontSize: 18, fontWeight: 700 }}>Break Planner</span>
        <span className="text-ink-soft" style={{ fontSize: 12, marginLeft: 4 }}>Voice + VWIC · 30 min lunch</span>
      </div>

      {/* Controls */}
      <div className="bg-raised border border-line-subtle" style={{
        borderRadius: 8, padding: "14px 16px", marginBottom: 16,
        display: "flex", alignItems: "center", gap: 16, flexWrap: "wrap"
      }}>
        <div>
          <label className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>DATE</label>
          <input type="date" value={date} onChange={e => setDate(e.target.value)}
            className="bg-sunken border border-line-subtle text-ink font-mono"
            style={{ padding: "5px 9px", borderRadius: 6, fontSize: 12 }} />
        </div>
        <div>
          <label className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>WINDOW START</label>
          <input type="time" value={windowStart} onChange={e => setWindowStart(e.target.value)}
            className="bg-sunken border border-line-subtle text-ink font-mono"
            style={{ padding: "5px 9px", borderRadius: 6, fontSize: 12 }} />
        </div>
        <div>
          <label className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>WINDOW END</label>
          <input type="time" value={windowEnd} onChange={e => setWindowEnd(e.target.value)}
            className="bg-sunken border border-line-subtle text-ink font-mono"
            style={{ padding: "5px 9px", borderRadius: 6, fontSize: 12 }} />
        </div>
        <div>
          <label className="text-ink-soft" style={{ fontSize: 10, display: "block", marginBottom: 3 }}>VOICE MIN ON-LINE %</label>
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <input type="number" value={voiceMinPct} onChange={e => setVoiceMinPct(Number(e.target.value))}
              min={50} max={100} step={5}
              className="bg-sunken border border-line-subtle text-ink font-mono"
              style={{ padding: "5px 9px", borderRadius: 6, fontSize: 12, width: 64 }} />
            <span className="text-ink-soft" style={{ fontSize: 11 }}>%</span>
          </div>
        </div>
        <div style={{ marginLeft: "auto", display: "flex", gap: 8 }}>
          <button
            onClick={() => setShowManual(true)}
            disabled={busy}
            className="bg-sunken border border-line-subtle text-ink-muted"
            style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer", display: "flex", alignItems: "center", gap: 6 }}
          ><X size={13} /> Manual slot</button>
          <button
            onClick={() => distributeMut.mutate()}
            disabled={busy}
            className="bg-info-solid text-white"
            style={{ padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer", fontWeight: 700, display: "flex", alignItems: "center", gap: 6 }}
          >
            <Shuffle size={13} />
            {distributeMut.isPending ? "Distributing…" : "Auto-Distribute"}
          </button>
        </div>
      </div>

      {/* KPI strip */}
      <div style={{ display: "flex", gap: 10, marginBottom: 16 }}>
        {[
          { label: "Scheduled today", value: kpi.scheduled,     icon: <Clock size={14} />,         cls: "text-info-fg" },
          { label: "On break now",    value: kpi.onBreak,        icon: <Coffee size={14} />,        cls: "text-warn-fg" },
          { label: "Completed",       value: kpi.done,           icon: <CheckCircle size={14} />,   cls: "text-good-fg" },
          { label: "VWIC on break",   value: kpi.vwicOnBreak,    icon: <AlertTriangle size={14} />, cls: kpi.vwicOnBreak > (maxVwic || 99) ? "text-crit-fg" : "text-ink-muted" },
        ].map(k => (
          <div key={k.label} className="bg-raised border border-line-subtle" style={{ flex: 1, borderRadius: 8, padding: "12px 14px" }}>
            <div className={k.cls} style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 4 }}>
              {k.icon}
              <span className="font-mono" style={{ fontSize: 10, fontWeight: 600 }}>{k.label.toUpperCase()}</span>
            </div>
            <div className="text-ink" style={{ fontSize: 28, fontWeight: 700 }}>{k.value}</div>
          </div>
        ))}
      </div>

      {/* Constraint info */}
      {lastResult && (
        <div className="bg-raised border border-line-subtle font-mono text-ink-muted" style={{
          borderRadius: 8, padding: "10px 14px", marginBottom: 12,
          display: "flex", gap: 20, fontSize: 11
        }}>
          <span>VWIC working: <b className="text-ink">{lastResult.totalVwic}</b> · max {maxVwic} on break</span>
          <span>Voice working: <b className="text-ink">{lastResult.totalVoice}</b> · max {maxVoice} on break</span>
          <span>Scheduled: <b className="text-good-fg">{lastResult.scheduled}</b></span>
          {lastResult.unscheduled > 0 && (
            <span className="text-warn-fg"><AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> Unscheduled: {lastResult.unscheduled}</span>
          )}
        </div>
      )}

      {/* Unscheduled warning */}
      {unscheduled.length > 0 && (
        <div className="bg-warn-bg border border-warn-bd" style={{
          borderRadius: 8, padding: "10px 14px", marginBottom: 12,
          display: "flex", alignItems: "center", gap: 8
        }}>
          <AlertTriangle size={14} className="text-warn-fg" style={{ flexShrink: 0 }} />
          <span className="text-warn-fg" style={{ fontSize: 11 }}>
            Could not schedule: {unscheduled.join(", ")} — assign manual slots or widen the window.
          </span>
        </div>
      )}

      {/* Timeline */}
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "14px 16px", marginBottom: 16 }}>
        <div className="text-ink" style={{ fontSize: 11, fontWeight: 700, marginBottom: 12, display: "flex", alignItems: "center", gap: 6 }}>
          <Clock size={13} className="text-info-fg" /> Break Timeline · {windowStart}–{windowEnd}
        </div>
        {isLoading
          ? <div className="text-ink-soft" style={{ fontSize: 12 }}>Loading…</div>
          : <Timeline slots={breaks} windowStart={windowStart} windowEnd={windowEnd} maxVwic={maxVwic} maxVoice={maxVoice} />
        }
      </div>

      {/* Agent table */}
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
        <div className="text-ink border-b border-line-subtle" style={{ padding: "12px 16px", fontSize: 11, fontWeight: 700 }}>
          Agent Break List
        </div>

        {breaks.length === 0 && !isLoading && (
          <div className="text-ink-soft" style={{ padding: 20, fontSize: 12, textAlign: "center" }}>
            No break slots for this date. Run Auto-Distribute to generate.
          </div>
        )}

        {breaks.length > 0 && (
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr className="bg-sunken">
                {["Agent", "Role", "Scheduled", "Actual Start", "Actual End", "Status", "Actions"].map(h => (
                  <th key={h} className="font-mono text-ink-soft border-b border-line-subtle" style={{
                    padding: "8px 12px", textAlign: "left",
                    fontSize: 10, fontWeight: 700, letterSpacing: 0.5
                  }}>{h.toUpperCase()}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {breaks.map((b, i) => {
                const lateStart = b.actualStart && Math.abs(timeToMin(b.actualStart) - timeToMin(b.breakStart)) > 15
                return (
                  <tr key={b.id} className="border-b border-line-subtle" style={{ background: i % 2 === 0 ? "transparent" : "rgba(0,0,0,.02)" }}>
                    <td className="text-ink" style={{ padding: "9px 12px", fontWeight: 500 }}>
                      {b.fullName ?? b.employeeId}
                      {b.teamLeadName && (
                        <div className="text-ink-soft" style={{ fontSize: 10 }}>{b.teamLeadName}</div>
                      )}
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <span
                        className={`font-mono ${b.agentRole === "VWIC" ? "bg-info-bg text-info-fg" : "bg-good-bg text-good-fg"}`}
                        style={{ fontSize: 10, fontWeight: 700, padding: "2px 6px", borderRadius: 3 }}
                      >{b.agentRole}</span>
                    </td>
                    <td className="font-mono text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>
                      {b.breakStart} – {b.breakEnd}
                      <div className="text-ink-soft" style={{ fontSize: 10 }}>{b.durationMinutes} min</div>
                    </td>
                    <td className="font-mono" style={{ padding: "9px 12px", fontSize: 11 }}>
                      {b.actualStart
                        ? <span className={lateStart ? "text-warn-fg" : "text-ink"}>{b.actualStart}</span>
                        : <span className="text-ink-soft">—</span>}
                      {lateStart && <div className="text-warn-fg" style={{ fontSize: 10 }}>≠ scheduled</div>}
                    </td>
                    <td className="font-mono text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>
                      {b.actualEnd ?? "—"}
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <StatusBadge status={b.status} />
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <div style={{ display: "flex", gap: 4 }}>
                        {b.status === "SCHEDULED" && btn("▶ Start", () => startMut.mutate(b.id), "bg-info-bg text-info-fg")}
                        {b.status === "ON_BREAK"  && btn("■ End",   () => endMut.mutate(b.id),   "bg-good-bg text-good-fg")}
                        {(b.status === "SCHEDULED" || b.status === "ON_BREAK") &&
                          btn("✕", () => cancelMut.mutate(b.id))}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {showManual && (
        <ManualModal
          date={date}
          onClose={() => setShowManual(false)}
          onSave={body => manualMut.mutate(body)}
        />
      )}
    </div>
  )
}
