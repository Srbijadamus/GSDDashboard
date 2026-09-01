// @ts-ignore
import LeaveAvailabilityBar from './LeaveAvailabilityBar'
import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { apiFetch } from "../api/client"
import { AlertTriangle } from "lucide-react"

function getDateRange(startDate: Date, days: number): string[] {
  return Array.from({ length: days }, (_, i) => {
    const d = new Date(startDate)
    d.setDate(d.getDate() + i)
    return d.toISOString().split("T")[0]
  })
}

function headerColor(count: number): string {
  if (count <= 2)  return "text-good-fg"
  if (count <= 5)  return "text-warn-fg"
  return "text-crit-fg"
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

  const { data, isLoading } = useQuery({
    queryKey: ["alcalendar", from, to, teamLead],
    queryFn:  () => apiFetch<any>(`/api/alcalendar?from=${from}&to=${to}${teamLead ? "&teamLead=" + teamLead : ""}`)
  })

  const { data: wicData } = useQuery({
    queryKey: ["wic-forecast-month", from, daysInMonth],
    queryFn:  () => apiFetch<any>(`/api/wic/forecast?startDate=${from}&horizon=${daysInMonth}`),
    enabled:  view === "3m"
  })

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

  const firstDayIdx  = monBasedDow(calMonth)
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
        <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600 }}>AL Calendar</h1>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          <select value={teamLead} onChange={e => setTeamLead(e.target.value)}
            className="bg-raised border border-line-subtle text-ink-muted"
            style={{ padding: "6px 10px", borderRadius: 6, fontSize: 12 }}>
            <option value="">All Team Leads</option>
            {data?.teamLeads?.map((tl: string) => (
              <option key={tl} value={tl}>{tl}</option>
            ))}
          </select>
          {(["7d","14d","3m"] as const).map(v => (
            <button key={v} onClick={() => { setView(v); setExpandedDay(null) }}
              className={`font-mono ${view === v ? "bg-info-solid text-white" : "bg-raised border border-line-subtle text-ink-muted"}`}
              style={{ padding: "6px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
                border: view === v ? "none" : undefined }}>
              {v === "7d" ? "7 Days" : v === "14d" ? "14 Days" : "Month"}
            </button>
          ))}
        </div>
      </div>

      {/* SUMMARY CARDS */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "14px 18px" }}>
          <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>
            {view === "3m" ? "1st of Month" : "On AL Today"}
          </div>
          <div className="text-info-fg font-mono" style={{ fontSize: 26, fontWeight: 600 }}>
            {view === "3m"
              ? (dayMap[from]?.totalOnAL ?? 0)
              : (dayMap[todayStr]?.totalOnAL ?? 0)}
          </div>
        </div>
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "14px 18px" }}>
          <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>Peak Day</div>
          <div className="text-warn-fg font-mono" style={{ fontSize: 26, fontWeight: 600 }}>
            {data?.days ? Math.max(...data.days.map((d: any) => d.totalOnAL)) : 0}
          </div>
        </div>
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "14px 18px" }}>
          <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>Warning Days</div>
          <div className="text-crit-fg font-mono" style={{ fontSize: 26, fontWeight: 600 }}>
            {data?.days?.filter((d: any) => d.hasWarning).length ?? 0}
          </div>
        </div>
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "14px 18px" }}>
          <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>Agents Tracked</div>
          <div className="text-ink font-mono" style={{ fontSize: 26, fontWeight: 600 }}>
            {agents.length}
          </div>
        </div>
      </div>

      {isLoading && (
        <div className="text-ink-soft" style={{ padding: 40, textAlign: "center" }}>Loading...</div>
      )}

      {/* ── MONTH VIEW ───────────────────────────────────────────────────── */}
      {view === "3m" && !isLoading && (
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>

          {/* Month navigation */}
          <div className="border-b border-line-subtle" style={{ display: "flex", justifyContent: "space-between", alignItems: "center",
            padding: "10px 16px" }}>
            <button onClick={prevMonth} className="bg-sunken border border-line-subtle text-ink-muted font-mono"
              style={{ borderRadius: 6, padding: "4px 12px", cursor: "pointer", fontSize: 14 }}>‹</button>
            <span className="text-ink font-mono" style={{ fontSize: 15, fontWeight: 600 }}>
              {MONTH_NAMES[calMon]} {calYear}
            </span>
            <button onClick={nextMonth} className="bg-sunken border border-line-subtle text-ink-muted font-mono"
              style={{ borderRadius: 6, padding: "4px 12px", cursor: "pointer", fontSize: 14 }}>›</button>
          </div>

          {/* Day-of-week headers */}
          <div className="bg-sunken border-b border-line-subtle" style={{ display: "grid", gridTemplateColumns: "repeat(7,1fr)" }}>
            {["Mon","Tue","Wed","Thu","Fri","Sat","Sun"].map(d => (
              <div key={d} className="text-ink-soft font-mono" style={{ textAlign: "center", padding: "8px 0",
                fontSize: 10, letterSpacing: ".05em", textTransform: "uppercase" }}>{d}</div>
            ))}
          </div>

          {/* Calendar grid */}
          <div className="bg-line-subtle" style={{ display: "grid", gridTemplateColumns: "repeat(7,1fr)", gap: 1, padding: 1 }}>
            {calCells.map((dayNum, i) => {
              if (dayNum === null) {
                return <div key={`blank-${i}`} className="bg-raised" style={{ minHeight: 72 }} />
              }
              const dateStr  = calDateStr(dayNum)
              const dayData  = dayMap[dateStr]
              const count    = dayData?.totalOnAL ?? 0
              const hasWarn  = dayData?.hasWarning ?? false
              const we       = isWeekend(dateStr)
              const isToday  = dateStr === todayStr
              const wic      = wicByDate[dateStr]
              const wicDot   = we || wic?.allClosed ? null
                : wic?.isAtRisk ? "rgb(var(--st-crit-fg))"
                : wicData ? "rgb(var(--st-good-fg))"
                : null

              const cellBgClass = expandedDay === dateStr ? "bg-info-bg"
                : isToday  ? "bg-info-bg"
                : we       ? "bg-sunken"
                : count === 0 ? "bg-sunken"
                : count <= 2  ? "bg-good-bg"
                : count <= 5  ? "bg-warn-bg"
                :               "bg-crit-bg"
              const countClass = headerColor(count)

              return (
                <div key={dateStr}
                  onClick={() => setExpandedDay(expandedDay === dateStr ? null : dateStr)}
                  className={`${cellBgClass} ${expandedDay === dateStr ? "border border-info-solid" : ""}`}
                  style={{
                    minHeight: 72, padding: "6px 8px",
                    cursor: "pointer", display: "flex", flexDirection: "column",
                    transition: "background .12s",
                    position: "relative"
                  }}>
                  {/* Day number */}
                  <div className="font-mono" style={{
                    fontSize: 11,
                    fontWeight: isToday ? 700 : 400,
                    color: isToday ? "rgb(var(--st-info-fg))" : we ? "rgb(var(--text-tertiary))" : "rgb(var(--text-secondary))"
                  }}>{dayNum}</div>

                  {/* AL count */}
                  {!we && count > 0 && (
                    <div className={`font-mono ${countClass}`} style={{
                      marginTop: 4, fontSize: 13, fontWeight: 700,
                      display: "flex", alignItems: "center", gap: 3
                    }}>
                      {count}
                      <span className={countClass} style={{ fontSize: 9, fontWeight: 400 }}>AL</span>
                      {hasWarn && <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" />}
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
          <div className="border-t border-line-subtle" style={{ padding: "8px 16px",
            display: "flex", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
            <span className="text-ink-soft" style={{ fontSize: 10, letterSpacing: ".06em", textTransform: "uppercase" }}>Legend:</span>
            {[
              { cls: "bg-good-bg border border-good-bd", label: "1–2 on AL" },
              { cls: "bg-warn-bg border border-warn-bd", label: "3–5 on AL" },
              { cls: "bg-crit-bg border border-crit-bd", label: "6+ on AL"  },
            ].map(({ cls, label }) => (
              <div key={label} style={{ display: "flex", alignItems: "center", gap: 5 }}>
                <div className={cls} style={{ width: 12, height: 12, borderRadius: 2 }} />
                <span className="text-ink-soft" style={{ fontSize: 10 }}>{label}</span>
              </div>
            ))}
            <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div className="bg-good-solid" style={{ width: 7, height: 7, borderRadius: "50%" }} />
              <span className="text-ink-soft" style={{ fontSize: 10 }}>WIC covered</span>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
              <div className="bg-crit-solid" style={{ width: 7, height: 7, borderRadius: "50%" }} />
              <span className="text-ink-soft" style={{ fontSize: 10 }}>WIC at risk</span>
            </div>
          </div>
        </div>
      )}

      {/* EXPANDED DAY DETAIL (month view) */}
      {view === "3m" && expandedDay && (
        <div className="bg-raised border border-info-bd" style={{ borderRadius: 8, padding: "16px" }}>
          <div className="text-ink" style={{ fontSize: 13, fontWeight: 600, marginBottom: 12 }}>
            {expandedDay}
            {dayMap[expandedDay] && (
              <span className="text-ink-muted" style={{ marginLeft: 8, fontWeight: 400 }}>
                — {dayMap[expandedDay].totalOnAL} agents on AL
              </span>
            )}
            {dayMap[expandedDay]?.hasWarning && (
              <span className="text-crit-fg" style={{ marginLeft: 8, fontSize: 11 }}>
                <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> {dayMap[expandedDay].warningTeams?.join(", ")}
              </span>
            )}
          </div>

          {/* Agents on AL */}
          {dayMap[expandedDay]?.agents?.length > 0 ? (
            <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 14 }}>
              {dayMap[expandedDay].agents.map((a: any) => (
                <div key={a.employeeId} style={{
                  background: `${TL_COLORS[a.teamLeadName] ?? "rgb(var(--st-info-solid))"}18`,
                  border: `1px solid ${TL_COLORS[a.teamLeadName] ?? "rgb(var(--st-info-solid))"}33`,
                  borderRadius: 6, padding: "5px 10px", fontSize: 11
                }}>
                  <div className="text-ink" style={{ fontWeight: 500 }}>{a.fullName}</div>
                  <div style={{ fontSize: 10, color: TL_COLORS[a.teamLeadName] ?? "rgb(var(--text-tertiary))" }}>
                    {a.teamLeadName?.split(" ")[0]}
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="text-ink-soft" style={{ fontSize: 11, marginBottom: 14 }}>No agents on AL this day.</div>
          )}

          {/* WIC coverage per location */}
          {wicData?.locations && (
            <>
              <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".07em", marginBottom: 8 }}>WIC Coverage</div>
              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {wicData.locations.map((loc: any) => {
                  const df = loc.forecast?.find((d: any) => d.date === expandedDay)
                  if (!df) return null
                  const riskColor = !df.isOpen ? "rgb(var(--text-tertiary))"
                    : df.isAtRisk ? "rgb(var(--st-crit-fg))"
                    : "rgb(var(--st-good-fg))"
                  return (
                    <div key={loc.locationCode} className="bg-sunken" style={{
                      border: `1px solid ${riskColor}44`,
                      borderRadius: 6, padding: "6px 10px", fontSize: 11, minWidth: 120
                    }}>
                      <div className="text-ink" style={{ fontWeight: 500, marginBottom: 2 }}>
                        {loc.displayName}
                      </div>
                      <div className="font-mono" style={{ fontSize: 10, color: riskColor }}>
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
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
              <thead>
                <tr className="bg-sunken">
                  <th className="text-ink-soft border-b border-line-subtle bg-sunken" style={{ padding: "10px 12px", textAlign: "left", fontSize: 10, fontWeight: 500,
                    textTransform: "uppercase", letterSpacing: ".07em",
                    minWidth: 160, position: "sticky", left: 0,
                    zIndex: 2 }}>Agent</th>
                  <th className="text-ink-soft border-b border-line-subtle" style={{ padding: "10px 8px", textAlign: "left", fontSize: 10, fontWeight: 500,
                    textTransform: "uppercase", letterSpacing: ".07em",
                    minWidth: 120 }}>Team Lead</th>
                  {dates.map(d => {
                    const { dow, day, month } = dayLabel(d)
                    const dayData = dayMap[d]
                    const count = dayData?.totalOnAL ?? 0
                    const hasWarn = dayData?.hasWarning ?? false
                    const we = isWeekend(d)
                    const isToday = d === todayStr
                    return (
                      <th key={d} className="border-b border-line-subtle" style={{
                        padding: "6px 4px", textAlign: "center", fontSize: 10,
                        borderLeft: "1px solid rgb(var(--line-subtle))",
                        minWidth: 52, maxWidth: 52,
                        background: isToday ? "rgb(var(--st-info-bg))" : we ? "rgb(var(--surface-sunken))" : "transparent"
                      }}>
                        <div style={{ color: isToday ? "rgb(var(--st-info-fg))" : we ? "rgb(var(--text-tertiary))" : "rgb(var(--text-secondary))", fontWeight: isToday ? 700 : 400 }}>
                          {dow}
                        </div>
                        <div className="font-mono" style={{ color: isToday ? "rgb(var(--st-info-fg))" : we ? "rgb(var(--text-tertiary))" : "rgb(var(--text-secondary))", fontSize: 9 }}>
                          {day}.{month}
                        </div>
                        <div className={`font-mono ${we ? "" : headerColor(count)}`} style={{
                          marginTop: 3, fontWeight: 700,
                          color: we ? "rgb(var(--text-tertiary))" : undefined,
                          fontSize: 12
                        }}>
                          {we ? "—" : count}
                        </div>
                        {hasWarn && <div className="text-crit-fg" style={{ fontSize: 9 }}><AlertTriangle size={9} className="inline align-text-bottom" /></div>}
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
                        color: TL_COLORS[tl] ?? "rgb(var(--text-secondary))",
                        background: "rgb(var(--surface-sunken))",
                        borderBottom: "1px solid rgb(var(--line-subtle))",
                        borderTop: "1px solid rgb(var(--line-subtle))"
                      }}>
                        {tl} ({tlAgents.length})
                      </td>
                    </tr>
                    {(tlAgents as any[]).map((agent: any) => (
                      <tr key={agent.employeeId}
                        className="hover:bg-hovered transition-colors border-b border-line-subtle">
                        <td className="bg-raised" style={{ padding: "6px 12px", position: "sticky", left: 0,
                          zIndex: 1,
                          borderRight: "1px solid rgb(var(--line-subtle))" }}>
                          <div className="text-ink" style={{ fontWeight: 500, fontSize: 11 }}>{agent.fullName}</div>
                          <div className="font-mono text-ink-soft" style={{ fontSize: 9 }}>{agent.employeeId}</div>
                        </td>
                        <td style={{ padding: "6px 8px", fontSize: 10 }}>
                          <span style={{ color: TL_COLORS[agent.teamLeadName] ?? "rgb(var(--text-secondary))", fontSize: 10 }}>
                            {agent.teamLeadName?.split(" ")[0]}
                          </span>
                        </td>
                        {dates.map(d => {
                          const dayData = dayMap[d]
                          const onAL = dayData?.agents?.find((a: any) => a.employeeId === agent.employeeId)
                          const we = isWeekend(d)
                          const isToday = d === todayStr
                          if (we) return (
                            <td key={d} className="bg-sunken" style={{ borderLeft: "1px solid rgb(var(--line-subtle))" }} />
                          )
                          if (!onAL) return (
                            <td key={d} style={{
                              borderLeft: "1px solid rgb(var(--line-subtle))",
                              background: isToday ? "rgb(var(--st-info-bg))" : "transparent"
                            }} />
                          )
                          const tlColor = TL_COLORS[agent.teamLeadName] ?? "rgb(var(--st-info-solid))"
                          return (
                            <td key={d} style={{ borderLeft: "1px solid rgb(var(--line-subtle))", padding: "2px" }}>
                              <div className="font-mono" style={{
                                background: `${tlColor}22`, border: `1px solid ${tlColor}44`,
                                borderRadius: 3, padding: "2px 0",
                                textAlign: "center", fontSize: 9,
                                color: tlColor, fontWeight: 600
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
        <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden", marginTop: 8 }}>
          <div className="border-b border-line-subtle text-ink-soft" style={{ padding: "12px 16px", fontSize: 11,
            textTransform: "uppercase", letterSpacing: ".07em" }}>
            Upcoming 7 Days — Who is on AL
          </div>
          {dates.filter(d => !isWeekend(d)).map(d => {
            const dayData = dayMap[d]
            if (!dayData || dayData.totalOnAL === 0) return null
            const { dow, day, month } = dayLabel(d)
            return (
              <div key={d} className="border-b border-line-subtle" style={{ padding: "10px 16px" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 6 }}>
                  <span className="font-mono" style={{ fontSize: 12,
                    color: d === todayStr ? "rgb(var(--st-info-fg))" : "rgb(var(--text-secondary))",
                    fontWeight: d === todayStr ? 700 : 400 }}>
                    {dow} {day}.{month}
                  </span>
                  <span className={`font-mono font-bold ${headerColor(dayData.totalOnAL)}`} style={{ fontSize: 11 }}>
                    {dayData.totalOnAL} on AL
                  </span>
                  {dayData.hasWarning && (
                    <span className="text-crit-fg" style={{ fontSize: 10 }}>
                      <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> {dayData.warningTeams.join(", ")}
                    </span>
                  )}
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
                  {dayData.agents.map((a: any) => (
                    <span key={a.employeeId} className="font-mono" style={{
                      background: `${TL_COLORS[a.teamLeadName] ?? "rgb(var(--st-info-solid))"}18`,
                      color: TL_COLORS[a.teamLeadName] ?? "rgb(var(--st-info-solid))",
                      border: `1px solid ${TL_COLORS[a.teamLeadName] ?? "rgb(var(--st-info-solid))"}33`,
                      padding: "2px 8px", borderRadius: 4, fontSize: 10
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
