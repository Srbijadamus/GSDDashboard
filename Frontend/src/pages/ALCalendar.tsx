// @ts-ignore
import LeaveAvailabilityBar from './LeaveAvailabilityBar'
import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { apiFetch } from "../api/client"

function getDateRange(startDate: Date, days: number): string[] {
  return Array.from({ length: days }, (_, i) => {
    const d = new Date(startDate)
    d.setDate(d.getDate() + i)
    return d.toISOString().split("T")[0]
  })
}

function headerColor(count: number) {
  if (count <= 2)  return "var(--green)"
  if (count <= 5)  return "#facc15"
  return "var(--danger)"
}

function dayLabel(dateStr: string) {
  const d = new Date(dateStr)
  const dow = ["Sun","Mon","Tue","Wed","Thu","Fri","Sat"][d.getDay()]
  return { dow, day: d.getDate().toString().padStart(2,"0"), month: (d.getMonth()+1).toString().padStart(2,"0") }
}

const TL_COLORS: Record<string, string> = {
  "Delia Panaitescu":       "#3b7eff",
  "Ion Ciuceanu":           "#00d2a0",
  "Jaroslaw Brzeszkiewicz": "#a78bfa",
  "Karlo Coric":            "#f97316",
  "Oliver Schleusen":       "#ec4899",
  "Tobias Rossberg":        "#facc15",
}

const MONTH_NAMES = [
  "January","February","March","April","May","June",
  "July","August","September","October","November","December"
]

// Convert JS getDay() (0=Sun) to Mon-based index (0=Mon, 6=Sun)
function monBasedDow(date: Date): number {
  const d = date.getDay()
  return d === 0 ? 6 : d - 1
}

export default function ALCalendar() {
  const today = new Date()
  const todayStr = today.toISOString().split("T")[0]

  const [view, setView]         = useState<"14d"|"7d"|"3m">("14d")
  const [teamLead, setTeamLead] = useState("")
  const [expandedDay, setExpandedDay] = useState<string | null>(null)
  const [calMonth, setCalMonth] = useState(() =>
    new Date(today.getFullYear(), today.getMonth(), 1)
  )

  // ── date range ────────────────────────────────────────────────────────────
  const calYear     = calMonth.getFullYear()
  const calMon      = calMonth.getMonth()
  const calLastDay  = new Date(calYear, calMon + 1, 0)
  const daysInMonth = calLastDay.getDate()

  let from: string, to: string, rollingDays: number
  if (view === "3m") {
    from        = calMonth.toISOString().split("T")[0]
    to          = calLastDay.toISOString().split("T")[0]
    rollingDays = daysInMonth
  } else {
    rollingDays = view === "7d" ? 7 : 14
    from        = todayStr
    const td    = new Date(today)
    td.setDate(td.getDate() + rollingDays - 1)
    to = td.toISOString().split("T")[0]
  }

  // ── AL calendar query ─────────────────────────────────────────────────────
  const { data, isLoading } = useQuery({
    queryKey: ["alcalendar", from, to, teamLead],
    queryFn:  () => apiFetch<any>(`/api/alcalendar?from=${from}&to=${to}${teamLead ? "&teamLead=" + teamLead : ""}`)
  })

  // ── WIC forecast query (month view only) ──────────────────────────────────
  const { data: wicData } = useQuery({
    queryKey: ["wic-forecast-month", from, daysInMonth],
    queryFn:  () => apiFetch<any>(`/api/wic/forecast?startDate=${from}&horizon=${daysInMonth}`),
    enabled:  view === "3m"
  })

  // ── derived data ──────────────────────────────────────────────────────────
  const dates = view !== "3m" ? getDateRange(today, rollingDays) : []

  const dayMap: Record<string, any> = {}
  data?.days?.forEach((d: any) => { dayMap[d.date] = d })

  const agentMap: Record<string, any> = {}
  data?.days?.forEach((d: any) => {
    d.agents?.forEach((a: any) => {
      if (!agentMap[a.employeeId]) agentMap[a.employeeId] = a
    })
  })
  const agents = Object.values(agentMap).sort((a: any, b: any) => {
    const tlA = a.teamLeadName ?? ""
    const tlB = b.teamLeadName ?? ""
    return tlA.localeCompare(tlB) || a.fullName.localeCompare(b.fullName)
  })
  const byTL: Record<string, any[]> = {}
  agents.forEach((a: any) => {
    const tl = a.teamLeadName ?? "Unknown"
    if (!byTL[tl]) byTL[tl] = []
    byTL[tl].push(a)
  })

  // Per-date WIC risk summary
  const wicByDate: Record<string, { isAtRisk: boolean; allClosed: boolean }> = {}
  if (wicData?.locations) {
    const acc: Record<string, { atRisk: boolean; anyOpen: boolean }> = {}
    wicData.locations.forEach((loc: any) => {
      loc.forecast?.forEach((d: any) => {
        if (!acc[d.date]) acc[d.date] = { atRisk: false, anyOpen: false }
        if (d.isOpen) acc[d.date].anyOpen = true
        if (d.isAtRisk) acc[d.date].atRisk = true
      })
    })
    Object.entries(acc).forEach(([date, { atRisk, anyOpen }]) => {
      wicByDate[date] = { isAtRisk: atRisk, allClosed: !anyOpen }
    })
  }

  // Month calendar grid
  const firstDayIdx  = monBasedDow(calMonth)                       // 0=Mon .. 6=Sun
  const totalCells   = Math.ceil((firstDayIdx + daysInMonth) / 7) * 7
  const calCells     = Array.from({ length: totalCells }, (_, i) => {
    const n = i - firstDayIdx + 1
    return (n >= 1 && n <= daysInMonth) ? n : null
  })
  const calDateStr   = (n: number) =>
    `${calYear}-${String(calMon + 1).padStart(2,"0")}-${String(n).padStart(2,"0")}`

  const isWeekend    = (dateStr: string) => {
    const dt = new Date(dateStr)
    return dt.getDay() === 0 || dt.getDay() === 6
  }

  const prevMonth = () => setCalMonth(new Date(calYear, calMon - 1, 1))
  const nextMonth = () => setCalMonth(new Date(calYear, calMon + 1, 1))

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* @ts-ignore */}
      <LeaveAvailabilityBar from={from} to={to} maxLeave={8} />

      {/* HEADER */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>AL Calendar</h1>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <select value={teamLead} onChange={e => setTeamLead(e.target.value)}
            style={{ background: "var(--card)", border: "1px solid var(--border)",
              color: "var(--text2)", padding: "6px 10px", borderRadius: 6, fontSize: 12 }}>
            <option value="">All Team Leads</option>
            {data?.teamLeads?.map((tl: string) => (
              <option key={tl} value={tl}>{tl}</option>
            ))}
          </select>
          {(["7d","14d","3m"] as const).map(v => (
            <button key={v} onClick={() => { setView(v); setExpandedDay(null) }} style={{
              background: view === v ? "var(--accent)" : "var(--card)",
              border: `1px solid ${view === v ? "var(--accent)" : "var(--border)"}`,
              color: view === v ? "#fff" : "var(--text2)",
              padding: "6px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
              fontFamily: "IBM Plex Mono"
            }}>
              {v === "7d" ? "7 Days" : v === "14d" ? "14 Days" : "Month"}
            </button>
          ))}
        </div>
      </div>

      {/* SUMMARY CARDS */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 18px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>
            {view === "3m" ? "1st of Month" : "On AL Today"}
          </div>
          <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--accent)" }}>
            {view === "3m"
              ? (dayMap[from]?.totalOnAL ?? 0)
              : (dayMap[todayStr]?.totalOnAL ?? 0)}
          </div>
        </div>
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 18px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>Peak Day</div>
          <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--warn)" }}>
            {data?.days ? Math.max(...data.days.map((d: any) => d.totalOnAL)) : 0}
          </div>
        </div>
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 18px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>Warning Days</div>
          <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--danger)" }}>
            {data?.days?.filter((d: any) => d.hasWarning).length ?? 0}
          </div>
        </div>
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 18px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>Agents Tracked</div>
          <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--text)" }}>
            {agents.length}
          </div>
        </div>
      </div>

      {isLoading && (
        <div style={{ padding: 40, textAlign: "center", color: "var(--text3)" }}>Loading...</div>
      )}

      {/* ── MONTH VIEW ───────────────────────────────────────────────────── */}
      {view === "3m" && !isLoading && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>

          {/* Month navigation */}
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center",
            padding: "10px 16px", borderBottom: "1px solid var(--border)" }}>
            <button onClick={prevMonth} style={{
              background: "var(--card2)", border: "1px solid var(--border)",
              color: "var(--text2)", borderRadius: 6, padding: "4px 12px",
              cursor: "pointer", fontSize: 14, fontFamily: "IBM Plex Mono"
            }}>‹</button>
            <span style={{ fontSize: 15, fontWeight: 600, color: "var(--text)", fontFamily: "IBM Plex Mono" }}>
              {MONTH_NAMES[calMon]} {calYear}
            </span>
            <button onClick={nextMonth} style={{
              background: "var(--card2)", border: "1px solid var(--border)",
              color: "var(--text2)", borderRadius: 6, padding: "4px 12px",
              cursor: "pointer", fontSize: 14, fontFamily: "IBM Plex Mono"
            }}>›</button>
          </div>

          {/* Day-of-week headers */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(7,1fr)",
            borderBottom: "1px solid var(--border)", background: "var(--card2)" }}>
            {["Mon","Tue","Wed","Thu","Fri","Sat","Sun"].map(d => (
              <div key={d} style={{ textAlign: "center", padding: "8px 0",
                fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono",
                letterSpacing: ".05em", textTransform: "uppercase" }}>{d}</div>
            ))}
          </div>

          {/* Calendar grid */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(7,1fr)", gap: 1,
            background: "var(--border)", padding: 1 }}>
            {calCells.map((dayNum, i) => {
              if (dayNum === null) {
                return <div key={`blank-${i}`} style={{ background: "var(--card)", minHeight: 72 }} />
              }
              const dateStr  = calDateStr(dayNum)
              const dayData  = dayMap[dateStr]
              const count    = dayData?.totalOnAL ?? 0
              const hasWarn  = dayData?.hasWarning ?? false
              const we       = isWeekend(dateStr)
              const isToday  = dateStr === todayStr
              const wic      = wicByDate[dateStr]
              const wicDot   = we || wic?.allClosed ? null
                : wic?.isAtRisk ? "var(--danger)"
                : wicData ? "var(--green)"
                : null

              const bg = isToday  ? "rgba(59,126,255,.15)"
                : we              ? "rgba(30,45,69,.35)"
                : count === 0     ? "var(--card)"
                : count <= 2      ? "rgba(34,208,122,.06)"
                : count <= 5      ? "rgba(250,204,21,.08)"
                :                   "rgba(255,59,92,.10)"

              const countColor = count <= 2 ? "var(--green)"
                : count <= 5    ? "#facc15"
                :                 "var(--danger)"

              return (
                <div key={dateStr}
                  onClick={() => setExpandedDay(expandedDay === dateStr ? null : dateStr)}
                  style={{
                    background: expandedDay === dateStr ? "rgba(59,126,255,.18)" : bg,
                    minHeight: 72, padding: "6px 8px",
                    cursor: "pointer", display: "flex", flexDirection: "column",
                    border: expandedDay === dateStr ? "1px solid var(--accent)" : "none",
                    transition: "background .12s",
                    position: "relative"
                  }}>
                  {/* Day number */}
                  <div style={{
                    fontSize: 11, fontFamily: "IBM Plex Mono",
                    fontWeight: isToday ? 700 : 400,
                    color: isToday ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)"
                  }}>{dayNum}</div>

                  {/* AL count */}
                  {!we && count > 0 && (
                    <div style={{
                      marginTop: 4, fontSize: 13, fontWeight: 700,
                      fontFamily: "IBM Plex Mono", color: countColor,
                      display: "flex", alignItems: "center", gap: 3
                    }}>
                      {count}
                      <span style={{ fontSize: 9, color: countColor, fontWeight: 400 }}>AL</span>
                      {hasWarn && <span style={{ fontSize: 10 }}>⚠</span>}
                    </div>
                  )}

                  {/* WIC coverage dot */}
                  {wicDot && (
                    <div style={{
                      position: "absolute", bottom: 6, right: 7,
                      width: 7, height: 7, borderRadius: "50%",
                      background: wicDot, opacity: 0.85
                    }} title={wic?.isAtRisk ? "WIC at risk" : "WIC covered"} />
                  )}
                </div>
              )
            })}
          </div>

          {/* Legend */}
          <div style={{ padding: "8px 16px", borderTop: "1px solid var(--border)",
            display: "flex", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
            <span style={{ fontSize: 10, color: "var(--text3)", letterSpacing: ".06em", textTransform: "uppercase" }}>Legend:</span>
            {[
              { color: "rgba(34,208,122,.25)", label: "1–2 on AL" },
              { color: "rgba(250,204,21,.25)", label: "3–5 on AL" },
              { color: "rgba(255,59,92,.25)",  label: "6+ on AL"  },
            ].map(({ color, label }) => (
              <div key={label} style={{ display: "flex", alignItems: "center", gap: 5 }}>
                <div style={{ width: 12, height: 12, borderRadius: 2, background: color }} />
                <span style={{ fontSize: 10, color: "var(--text3)" }}>{label}</span>
              </div>
            ))}
            <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div style={{ width: 7, height: 7, borderRadius: "50%", background: "var(--green)" }} />
              <span style={{ fontSize: 10, color: "var(--text3)" }}>WIC covered</span>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div style={{ width: 7, height: 7, borderRadius: "50%", background: "var(--danger)" }} />
              <span style={{ fontSize: 10, color: "var(--text3)" }}>WIC at risk</span>
            </div>
          </div>
        </div>
      )}

      {/* EXPANDED DAY DETAIL (month view) */}
      {view === "3m" && expandedDay && (
        <div style={{ background: "var(--card)", border: "1px solid var(--accent)44",
          borderRadius: 8, padding: "16px" }}>
          <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 12, color: "var(--text)" }}>
            {expandedDay}
            {dayMap[expandedDay] && (
              <span style={{ marginLeft: 8, color: "var(--text2)", fontWeight: 400 }}>
                — {dayMap[expandedDay].totalOnAL} agents on AL
              </span>
            )}
            {dayMap[expandedDay]?.hasWarning && (
              <span style={{ marginLeft: 8, fontSize: 11, color: "var(--danger)" }}>
                ⚠ {dayMap[expandedDay].warningTeams?.join(", ")}
              </span>
            )}
          </div>

          {/* Agents on AL */}
          {dayMap[expandedDay]?.agents?.length > 0 ? (
            <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 14 }}>
              {dayMap[expandedDay].agents.map((a: any) => (
                <div key={a.employeeId} style={{
                  background: `${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}18`,
                  border: `1px solid ${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}33`,
                  borderRadius: 6, padding: "5px 10px", fontSize: 11
                }}>
                  <div style={{ fontWeight: 500, color: "var(--text)" }}>{a.fullName}</div>
                  <div style={{ fontSize: 10, color: TL_COLORS[a.teamLeadName] ?? "var(--text3)" }}>
                    {a.teamLeadName?.split(" ")[0]}
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ fontSize: 11, color: "var(--text3)", marginBottom: 14 }}>No agents on AL this day.</div>
          )}

          {/* WIC coverage per location */}
          {wicData?.locations && (
            <>
              <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".07em",
                color: "var(--text3)", marginBottom: 8 }}>WIC Coverage</div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {wicData.locations.map((loc: any) => {
                  const df = loc.forecast?.find((d: any) => d.date === expandedDay)
                  if (!df) return null
                  const riskColor = !df.isOpen ? "var(--text3)"
                    : df.isAtRisk ? "var(--danger)"
                    : "var(--green)"
                  return (
                    <div key={loc.locationCode} style={{
                      background: "var(--card2)", border: `1px solid ${riskColor}44`,
                      borderRadius: 6, padding: "6px 10px", fontSize: 11, minWidth: 120
                    }}>
                      <div style={{ fontWeight: 500, color: "var(--text)", marginBottom: 2 }}>
                        {loc.displayName}
                      </div>
                      <div style={{ fontSize: 10, color: riskColor, fontFamily: "IBM Plex Mono" }}>
                        {!df.isOpen
                          ? df.closedReason ?? "Closed"
                          : `${df.effectiveCoverage}/${df.minRequired} · ${df.status}`}
                      </div>
                    </div>
                  )
                })}
              </div>
            </>
          )}
        </div>
      )}

      {/* ── 14 DAY / 7 DAY GRID ─────────────────────────────────────────── */}
      {(view === "14d" || view === "7d") && !isLoading && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
              <thead>
                <tr style={{ background: "var(--card2)" }}>
                  <th style={{ padding: "10px 12px", textAlign: "left", fontSize: 10, fontWeight: 500,
                    textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)",
                    borderBottom: "1px solid var(--border)", minWidth: 160, position: "sticky", left: 0,
                    background: "var(--card2)", zIndex: 2 }}>Agent</th>
                  <th style={{ padding: "10px 8px", textAlign: "left", fontSize: 10, fontWeight: 500,
                    textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)",
                    borderBottom: "1px solid var(--border)", minWidth: 120 }}>Team Lead</th>
                  {dates.map(d => {
                    const { dow, day, month } = dayLabel(d)
                    const dayData = dayMap[d]
                    const count = dayData?.totalOnAL ?? 0
                    const hasWarn = dayData?.hasWarning ?? false
                    const we = isWeekend(d)
                    const isToday = d === todayStr
                    return (
                      <th key={d} style={{
                        padding: "6px 4px", textAlign: "center", fontSize: 10,
                        borderBottom: "1px solid var(--border)",
                        borderLeft: "1px solid rgba(30,45,69,.5)",
                        minWidth: 52, maxWidth: 52,
                        background: isToday ? "rgba(59,126,255,.08)" : we ? "rgba(30,45,69,.3)" : "transparent"
                      }}>
                        <div style={{ color: isToday ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)", fontWeight: isToday ? 700 : 400 }}>
                          {dow}
                        </div>
                        <div style={{ color: isToday ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)", fontFamily: "IBM Plex Mono", fontSize: 9 }}>
                          {day}.{month}
                        </div>
                        <div style={{
                          marginTop: 3, fontFamily: "IBM Plex Mono", fontWeight: 700,
                          color: we ? "var(--text3)" : headerColor(count),
                          fontSize: 12
                        }}>
                          {we ? "—" : count}
                        </div>
                        {hasWarn && <div style={{ fontSize: 9, color: "var(--danger)" }}>⚠</div>}
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {Object.entries(byTL).map(([tl, tlAgents]) => (
                  <>
                    <tr key={`tl-${tl}`}>
                      <td colSpan={2 + dates.length} style={{
                        padding: "6px 12px", fontSize: 10, fontWeight: 600,
                        textTransform: "uppercase", letterSpacing: ".08em",
                        color: TL_COLORS[tl] ?? "var(--text2)",
                        background: "rgba(30,45,69,.3)",
                        borderBottom: "1px solid var(--border)",
                        borderTop: "1px solid var(--border)"
                      }}>
                        {tl} ({tlAgents.length})
                      </td>
                    </tr>
                    {(tlAgents as any[]).map((agent: any) => (
                      <tr key={agent.employeeId}
                        style={{ borderBottom: "1px solid rgba(30,45,69,.4)" }}
                        onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                        onMouseLeave={ev => (ev.currentTarget.style.background = "transparent")}>
                        <td style={{ padding: "6px 12px", position: "sticky", left: 0,
                          background: "var(--card)", zIndex: 1,
                          borderRight: "1px solid rgba(30,45,69,.4)" }}>
                          <div style={{ fontWeight: 500, color: "var(--text)", fontSize: 11 }}>{agent.fullName}</div>
                          <div style={{ fontFamily: "IBM Plex Mono", fontSize: 9, color: "var(--text3)" }}>{agent.employeeId}</div>
                        </td>
                        <td style={{ padding: "6px 8px", fontSize: 10, color: "var(--text3)" }}>
                          <span style={{ color: TL_COLORS[agent.teamLeadName] ?? "var(--text2)", fontSize: 10 }}>
                            {agent.teamLeadName?.split(" ")[0]}
                          </span>
                        </td>
                        {dates.map(d => {
                          const dayData = dayMap[d]
                          const onAL = dayData?.agents?.find((a: any) => a.employeeId === agent.employeeId)
                          const we = isWeekend(d)
                          const isToday = d === todayStr
                          if (we) return (
                            <td key={d} style={{ borderLeft: "1px solid rgba(30,45,69,.3)",
                              background: "rgba(30,45,69,.2)" }} />
                          )
                          if (!onAL) return (
                            <td key={d} style={{
                              borderLeft: "1px solid rgba(30,45,69,.3)",
                              background: isToday ? "rgba(59,126,255,.04)" : "transparent"
                            }} />
                          )
                          const tlColor = TL_COLORS[agent.teamLeadName] ?? "var(--accent)"
                          return (
                            <td key={d} style={{ borderLeft: "1px solid rgba(30,45,69,.3)", padding: "2px" }}>
                              <div style={{
                                background: `${tlColor}22`, border: `1px solid ${tlColor}44`,
                                borderRadius: 3, padding: "2px 0",
                                textAlign: "center", fontSize: 9,
                                color: tlColor, fontFamily: "IBM Plex Mono", fontWeight: 600
                              }}>AL</div>
                            </td>
                          )
                        })}
                      </tr>
                    ))}
                  </>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── 7-DAY LIST (supplemental) ────────────────────────────────────── */}
      {view === "7d" && !isLoading && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden", marginTop: 8 }}>
          <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", fontSize: 11,
            textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)" }}>
            Upcoming 7 Days — Who is on AL
          </div>
          {dates.filter(d => !isWeekend(d)).map(d => {
            const dayData = dayMap[d]
            if (!dayData || dayData.totalOnAL === 0) return null
            const { dow, day, month } = dayLabel(d)
            return (
              <div key={d} style={{ padding: "10px 16px", borderBottom: "1px solid rgba(30,45,69,.4)" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 6 }}>
                  <span style={{ fontFamily: "IBM Plex Mono", fontSize: 12,
                    color: d === todayStr ? "var(--accent)" : "var(--text2)",
                    fontWeight: d === todayStr ? 700 : 400 }}>
                    {dow} {day}.{month}
                  </span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11,
                    color: headerColor(dayData.totalOnAL), fontWeight: 700 }}>
                    {dayData.totalOnAL} on AL
                  </span>
                  {dayData.hasWarning && (
                    <span style={{ fontSize: 10, color: "var(--danger)" }}>
                      ⚠ {dayData.warningTeams.join(", ")}
                    </span>
                  )}
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                  {dayData.agents.map((a: any) => (
                    <span key={a.employeeId} style={{
                      background: `${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}18`,
                      color: TL_COLORS[a.teamLeadName] ?? "var(--accent)",
                      border: `1px solid ${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}33`,
                      padding: "2px 8px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono"
                    }}>{a.fullName}</span>
                  ))}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
