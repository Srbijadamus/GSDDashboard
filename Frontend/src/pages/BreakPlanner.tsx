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

const API_BASE = (import.meta as any).env?.VITE_API_BASE_URL ?? "http://localhost:5000"

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
  const colors: Record<string, { bg: string; color: string }> = {
    SCHEDULED: { bg: "rgba(59,126,255,.15)", color: "var(--accent)" },
    ON_BREAK:  { bg: "rgba(249,115,22,.18)", color: "#f97316" },
    DONE:      { bg: "rgba(34,197,94,.15)",  color: "#22c55e" },
    CANCELLED: { bg: "rgba(100,116,139,.15)", color: "var(--text3)" },
  }
  const c = colors[status] ?? colors.CANCELLED
  return (
    <span style={{
      background: c.bg, color: c.color,
      fontSize: 10, fontWeight: 700, fontFamily: "IBM Plex Mono",
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

  // 15-min slots for concurrent count row
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
    const bg = s.status === "ON_BREAK"  ? "#f97316"
             : s.status === "DONE"      ? "#22c55e"
             : s.agentRole === "VWIC"   ? "var(--accent)"
             : "#10b981"
    return {
      position: "absolute" as const,
      left: `${left}%`, width: `${width}%`, top: 4, height: 16,
      background: bg, borderRadius: 3, opacity: s.status === "DONE" ? 0.55 : 0.9,
      border: isLate ? "2px solid #fbbf24" : "none",
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
      {/* Time axis */}
      <div style={{ display: "flex", marginBottom: 4, marginLeft: 140 }}>
        {timeLabels.map(l => (
          <div key={l} style={{
            flex: 1, fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono",
            textAlign: l === timeLabels[timeLabels.length - 1] ? "right" : "left"
          }}>{l}</div>
        ))}
      </div>

      {/* Agent rows */}
      {active.length === 0 && (
        <div style={{ color: "var(--text3)", fontSize: 12, padding: "8px 0", marginLeft: 140 }}>
          No breaks scheduled — click Auto-Distribute to generate a plan.
        </div>
      )}
      {active.map(s => (
        <div key={s.id} style={{ display: "flex", alignItems: "center", marginBottom: 3 }}>
          <div style={{
            width: 140, fontSize: 11, color: "var(--text2)", flexShrink: 0,
            display: "flex", alignItems: "center", gap: 5, overflow: "hidden"
          }}>
            <span style={{
              fontSize: 9, fontWeight: 700, fontFamily: "IBM Plex Mono",
              color: s.agentRole === "VWIC" ? "var(--accent)" : "#10b981",
              background: s.agentRole === "VWIC" ? "rgba(59,126,255,.12)" : "rgba(16,185,129,.12)",
              padding: "1px 4px", borderRadius: 3, flexShrink: 0
            }}>{s.agentRole}</span>
            <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
              {s.fullName ?? s.employeeId}
            </span>
          </div>
          <div style={{ flex: 1, height: 24, position: "relative", background: "var(--card2)", borderRadius: 4 }}>
            <div style={barStyle(s)} title={`${s.breakStart}–${s.breakEnd}${s.actualStart ? ` (actual: ${s.actualStart})` : ""}`} />
          </div>
        </div>
      ))}

      {/* Concurrent count row */}
      {active.length > 0 && (
        <div style={{ marginTop: 8, marginLeft: 140, display: "flex" }}>
          {concSlots.map(t => {
            const vwicN  = concCount(t, "VWIC")
            const voiceN = concCount(t, "Voice")
            const over   = (maxVwic  > 0 && vwicN  >= maxVwic)
                        || (maxVoice > 0 && voiceN >= maxVoice)
            const pct    = 15 / total * 100
            return (
              <div key={t} style={{
                width: `${pct}%`, textAlign: "center",
                fontSize: 10, fontFamily: "IBM Plex Mono",
                color: over ? "#ef4444" : "var(--text3)",
                background: over ? "rgba(239,68,68,.08)" : "transparent",
                borderRadius: 2, padding: "1px 0"
              }}>
                {vwicN + voiceN > 0 ? vwicN + voiceN : "·"}
              </div>
            )
          })}
        </div>
      )}
      {active.length > 0 && (
        <div style={{ marginLeft: 140, fontSize: 10, color: "var(--text3)", marginTop: 2 }}>
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
      <div style={{
        background: "var(--card)", border: "1px solid var(--border)",
        borderRadius: 10, padding: 24, width: 340
      }}>
        <div style={{ fontWeight: 700, marginBottom: 16, color: "var(--text)" }}>Manual Break Slot</div>
        <label style={{ fontSize: 11, color: "var(--text2)", display: "block", marginBottom: 4 }}>Employee ID</label>
        <input value={empId} onChange={e => setEmpId(e.target.value)}
          placeholder="e.g. E12345"
          style={{
            width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13,
            background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)",
            boxSizing: "border-box", marginBottom: 12
          }} />
        <div style={{ display: "flex", gap: 10, marginBottom: 16 }}>
          <div style={{ flex: 1 }}>
            <label style={{ fontSize: 11, color: "var(--text2)", display: "block", marginBottom: 4 }}>Start time</label>
            <input type="time" value={start} onChange={e => setStart(e.target.value)}
              style={{
                width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13,
                background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)"
              }} />
          </div>
          <div style={{ flex: 1 }}>
            <label style={{ fontSize: 11, color: "var(--text2)", display: "block", marginBottom: 4 }}>Duration (min)</label>
            <input type="number" value={dur} onChange={e => setDur(Number(e.target.value))} min={15} max={60}
              style={{
                width: "100%", padding: "6px 10px", borderRadius: 6, fontSize: 13,
                background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)"
              }} />
          </div>
        </div>
        <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
          <button onClick={onClose} style={{
            padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
            background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)"
          }}>Cancel</button>
          <button onClick={() => empId && onSave({ employeeId: empId.trim(), date, breakStart: start, durationMinutes: dur })}
            style={{
              padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
              background: "var(--accent)", border: "none", color: "#fff", fontWeight: 600
            }}>Save</button>
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

  // KPI derived from breaks data
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

  const btn = (label: string, onClick: () => void, color = "var(--card2)", textColor = "var(--text2)", disabled = false) => (
    <button
      onClick={onClick}
      disabled={disabled || busy}
      style={{
        padding: "5px 11px", borderRadius: 5, fontSize: 11, cursor: disabled ? "not-allowed" : "pointer",
        background: color, border: "1px solid var(--border)", color: textColor,
        fontWeight: 600, opacity: disabled ? 0.45 : 1, display: "flex", alignItems: "center", gap: 4
      }}
    >{label}</button>
  )

  return (
    <div style={{ maxWidth: 1100, margin: "0 auto" }}>

      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 20 }}>
        <Coffee size={20} style={{ color: "var(--accent)" }} />
        <span style={{ fontSize: 18, fontWeight: 700, color: "var(--text)" }}>Break Planner</span>
        <span style={{ fontSize: 12, color: "var(--text3)", marginLeft: 4 }}>Voice + VWIC · 30 min lunch</span>
      </div>

      {/* Controls */}
      <div style={{
        background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8,
        padding: "14px 16px", marginBottom: 16, display: "flex", alignItems: "center",
        gap: 16, flexWrap: "wrap"
      }}>
        <div>
          <label style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>DATE</label>
          <input type="date" value={date} onChange={e => setDate(e.target.value)}
            style={{
              padding: "5px 9px", borderRadius: 6, fontSize: 12, fontFamily: "IBM Plex Mono",
              background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)"
            }} />
        </div>
        <div>
          <label style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>WINDOW START</label>
          <input type="time" value={windowStart} onChange={e => setWindowStart(e.target.value)}
            style={{
              padding: "5px 9px", borderRadius: 6, fontSize: 12, fontFamily: "IBM Plex Mono",
              background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)"
            }} />
        </div>
        <div>
          <label style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>WINDOW END</label>
          <input type="time" value={windowEnd} onChange={e => setWindowEnd(e.target.value)}
            style={{
              padding: "5px 9px", borderRadius: 6, fontSize: 12, fontFamily: "IBM Plex Mono",
              background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)"
            }} />
        </div>
        <div>
          <label style={{ fontSize: 10, color: "var(--text3)", display: "block", marginBottom: 3 }}>VOICE MIN ON-LINE %</label>
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <input type="number" value={voiceMinPct} onChange={e => setVoiceMinPct(Number(e.target.value))}
              min={50} max={100} step={5}
              style={{
                padding: "5px 9px", borderRadius: 6, fontSize: 12, fontFamily: "IBM Plex Mono",
                background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text)", width: 64
              }} />
            <span style={{ fontSize: 11, color: "var(--text3)" }}>%</span>
          </div>
        </div>
        <div style={{ marginLeft: "auto", display: "flex", gap: 8 }}>
          <button
            onClick={() => setShowManual(true)}
            disabled={busy}
            style={{
              padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
              background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)",
              display: "flex", alignItems: "center", gap: 6
            }}
          ><X size={13} /> Manual slot</button>
          <button
            onClick={() => distributeMut.mutate()}
            disabled={busy}
            style={{
              padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
              background: "var(--accent)", border: "none", color: "#fff",
              fontWeight: 700, display: "flex", alignItems: "center", gap: 6
            }}
          >
            <Shuffle size={13} />
            {distributeMut.isPending ? "Distributing…" : "Auto-Distribute"}
          </button>
        </div>
      </div>

      {/* KPI strip */}
      <div style={{ display: "flex", gap: 10, marginBottom: 16 }}>
        {[
          { label: "Scheduled today", value: kpi.scheduled,     icon: <Clock size={14} />,        color: "var(--accent)" },
          { label: "On break now",    value: kpi.onBreak,        icon: <Coffee size={14} />,       color: "#f97316" },
          { label: "Completed",       value: kpi.done,           icon: <CheckCircle size={14} />,  color: "#22c55e" },
          { label: "VWIC on break",   value: kpi.vwicOnBreak,    icon: <AlertTriangle size={14} />,color: kpi.vwicOnBreak > (maxVwic || 99) ? "#ef4444" : "var(--text2)" },
        ].map(k => (
          <div key={k.label} style={{
            flex: 1, background: "var(--card)", border: "1px solid var(--border)",
            borderRadius: 8, padding: "12px 14px"
          }}>
            <div style={{ display: "flex", alignItems: "center", gap: 6, color: k.color, marginBottom: 4 }}>
              {k.icon}
              <span style={{ fontSize: 10, fontWeight: 600, fontFamily: "IBM Plex Mono" }}>{k.label.toUpperCase()}</span>
            </div>
            <div style={{ fontSize: 28, fontWeight: 700, color: "var(--text)" }}>{k.value}</div>
          </div>
        ))}
      </div>

      {/* Constraint info */}
      {lastResult && (
        <div style={{
          background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8,
          padding: "10px 14px", marginBottom: 12,
          display: "flex", gap: 20, fontSize: 11, color: "var(--text2)", fontFamily: "IBM Plex Mono"
        }}>
          <span>VWIC working: <b style={{color:"var(--text)"}}>{lastResult.totalVwic}</b> · max {maxVwic} on break</span>
          <span>Voice working: <b style={{color:"var(--text)"}}>{lastResult.totalVoice}</b> · max {maxVoice} on break</span>
          <span>Scheduled: <b style={{color:"#22c55e"}}>{lastResult.scheduled}</b></span>
          {lastResult.unscheduled > 0 && (
            <span style={{ color: "#f97316" }}>⚠ Unscheduled: {lastResult.unscheduled}</span>
          )}
        </div>
      )}

      {/* Unscheduled warning */}
      {unscheduled.length > 0 && (
        <div style={{
          background: "rgba(249,115,22,.08)", border: "1px solid rgba(249,115,22,.3)",
          borderRadius: 8, padding: "10px 14px", marginBottom: 12,
          display: "flex", alignItems: "center", gap: 8
        }}>
          <AlertTriangle size={14} style={{ color: "#f97316", flexShrink: 0 }} />
          <span style={{ fontSize: 11, color: "#f97316" }}>
            Could not schedule: {unscheduled.join(", ")} — assign manual slots or widen the window.
          </span>
        </div>
      )}

      {/* Timeline */}
      <div style={{
        background: "var(--card)", border: "1px solid var(--border)",
        borderRadius: 8, padding: "14px 16px", marginBottom: 16
      }}>
        <div style={{ fontSize: 11, fontWeight: 700, color: "var(--text)", marginBottom: 12,
          display: "flex", alignItems: "center", gap: 6 }}>
          <Clock size={13} style={{ color: "var(--accent)" }} /> Break Timeline · {windowStart}–{windowEnd}
        </div>
        {isLoading
          ? <div style={{ color: "var(--text3)", fontSize: 12 }}>Loading…</div>
          : <Timeline slots={breaks} windowStart={windowStart} windowEnd={windowEnd} maxVwic={maxVwic} maxVoice={maxVoice} />
        }
      </div>

      {/* Agent table */}
      <div style={{
        background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden"
      }}>
        <div style={{
          padding: "12px 16px", borderBottom: "1px solid var(--border)",
          fontSize: 11, fontWeight: 700, color: "var(--text)"
        }}>Agent Break List</div>

        {breaks.length === 0 && !isLoading && (
          <div style={{ padding: 20, color: "var(--text3)", fontSize: 12, textAlign: "center" }}>
            No break slots for this date. Run Auto-Distribute to generate.
          </div>
        )}

        {breaks.length > 0 && (
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr style={{ background: "var(--card2)" }}>
                {["Agent", "Role", "Scheduled", "Actual Start", "Actual End", "Status", "Actions"].map(h => (
                  <th key={h} style={{
                    padding: "8px 12px", textAlign: "left",
                    fontSize: 10, fontWeight: 700, color: "var(--text3)",
                    fontFamily: "IBM Plex Mono", letterSpacing: 0.5,
                    borderBottom: "1px solid var(--border)"
                  }}>{h.toUpperCase()}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {breaks.map((b, i) => {
                const lateStart = b.actualStart && Math.abs(timeToMin(b.actualStart) - timeToMin(b.breakStart)) > 15
                return (
                  <tr key={b.id} style={{
                    borderBottom: "1px solid var(--border)",
                    background: i % 2 === 0 ? "transparent" : "rgba(0,0,0,.02)"
                  }}>
                    <td style={{ padding: "9px 12px", color: "var(--text)", fontWeight: 500 }}>
                      {b.fullName ?? b.employeeId}
                      {b.teamLeadName && (
                        <div style={{ fontSize: 10, color: "var(--text3)" }}>{b.teamLeadName}</div>
                      )}
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <span style={{
                        fontSize: 10, fontWeight: 700, fontFamily: "IBM Plex Mono",
                        color: b.agentRole === "VWIC" ? "var(--accent)" : "#10b981",
                        background: b.agentRole === "VWIC" ? "rgba(59,126,255,.12)" : "rgba(16,185,129,.12)",
                        padding: "2px 6px", borderRadius: 3
                      }}>{b.agentRole}</span>
                    </td>
                    <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)" }}>
                      {b.breakStart} – {b.breakEnd}
                      <div style={{ fontSize: 10, color: "var(--text3)" }}>{b.durationMinutes} min</div>
                    </td>
                    <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>
                      {b.actualStart
                        ? <span style={{ color: lateStart ? "#fbbf24" : "var(--text)" }}>{b.actualStart}</span>
                        : <span style={{ color: "var(--text3)" }}>—</span>}
                      {lateStart && <div style={{ fontSize: 10, color: "#fbbf24" }}>≠ scheduled</div>}
                    </td>
                    <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)" }}>
                      {b.actualEnd ?? "—"}
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <StatusBadge status={b.status} />
                    </td>
                    <td style={{ padding: "9px 12px" }}>
                      <div style={{ display: "flex", gap: 4 }}>
                        {b.status === "SCHEDULED" && btn("▶ Start", () => startMut.mutate(b.id), "rgba(59,126,255,.15)", "var(--accent)")}
                        {b.status === "ON_BREAK"  && btn("■ End",   () => endMut.mutate(b.id),   "rgba(34,197,94,.15)",  "#22c55e")}
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
