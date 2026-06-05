// @ts-ignore
import LeaveAvailabilityBar from './LeaveAvailabilityBar'
import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { apiFetch } from "../api/client"

// const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000"

function getDateRange(startDate: Date, days: number): string[] {
  return Array.from({ length: days }, (_, i) => {
    const d = new Date(startDate)
    d.setDate(d.getDate() + i)
    return d.toISOString().split("T")[0]
  })
}

function headerColor(count: number) {
  if (count === 0) return "var(--green)"
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

export default function ALCalendar() {
  const today = new Date()
  const [view, setView]         = useState<"14d"|"7d"|"3m">("14d")
  const [teamLead, setTeamLead] = useState("")
  const [expandedDay, setExpandedDay] = useState<string | null>(null)

  const days   = view === "7d" ? 7 : view === "14d" ? 14 : 90
  const from   = today.toISOString().split("T")[0]
  const toDate = new Date(today)
  toDate.setDate(toDate.getDate() + days - 1)
  const to = toDate.toISOString().split("T")[0]

  const { data, isLoading } = useQuery({
    queryKey: ["alcalendar", from, to, teamLead],
    queryFn: () => apiFetch<any>(`/api/alcalendar?from=${from}&to=${to}${teamLead ? "&teamLead=" + teamLead : ""}`)
  })

  const dates = getDateRange(today, days)
  const dayMap: Record<string, any> = {}
  data?.days?.forEach((d: any) => { dayMap[d.date] = d })

  // Get unique agents across all days for row headers
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

  // Group agents by team lead
  const byTL: Record<string, any[]> = {}
  agents.forEach((a: any) => {
    const tl = a.teamLeadName ?? "Unknown"
    if (!byTL[tl]) byTL[tl] = []
    byTL[tl].push(a)
  })

  const isWeekend = (d: string) => {
    const dt = new Date(d)
    return dt.getDay() === 0 || dt.getDay() === 6
  }

  const isToday = (d: string) => d === today.toISOString().split("T")[0]

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* @ts-ignore */}
      <LeaveAvailabilityBar from={from} to={to} maxLeave={8} />

      {/* HEADER */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>AL Calendar</h1>
        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          {/* Team Lead filter */}
          <select value={teamLead} onChange={e => setTeamLead(e.target.value)}
            style={{ background: "var(--card)", border: "1px solid var(--border)",
              color: "var(--text2)", padding: "6px 10px", borderRadius: 6, fontSize: 12 }}>
            <option value="">All Team Leads</option>
            {data?.teamLeads?.map((tl: string) => (
              <option key={tl} value={tl}>{tl}</option>
            ))}
          </select>
          {/* View selector */}
          {(["7d","14d","3m"] as const).map(v => (
            <button key={v} onClick={() => setView(v)} style={{
              background: view === v ? "var(--accent)" : "var(--card)",
              border: `1px solid ${view === v ? "var(--accent)" : "var(--border)"}`,
              color: view === v ? "#fff" : "var(--text2)",
              padding: "6px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
              fontFamily: "IBM Plex Mono"
            }}>
              {v === "7d" ? "7 Days" : v === "14d" ? "14 Days" : "3 Months"}
            </button>
          ))}
        </div>
      </div>

      {/* SUMMARY CARDS */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 18px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>On AL Today</div>
          <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--accent)" }}>
            {dayMap[from]?.totalOnAL ?? 0}
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

      {/* VIEW 2 — 14 DAY GRID */}
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
                    const tod = isToday(d)
                    return (
                      <th key={d} style={{
                        padding: "6px 4px", textAlign: "center", fontSize: 10,
                        borderBottom: "1px solid var(--border)",
                        borderLeft: "1px solid rgba(30,45,69,.5)",
                        minWidth: 52, maxWidth: 52,
                        background: tod ? "rgba(59,126,255,.08)" : we ? "rgba(30,45,69,.3)" : "transparent"
                      }}>
                        <div style={{ color: tod ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)", fontWeight: tod ? 700 : 400 }}>
                          {dow}
                        </div>
                        <div style={{ color: tod ? "var(--accent)" : we ? "var(--text3)" : "var(--text2)", fontFamily: "IBM Plex Mono", fontSize: 9 }}>
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
                    {/* Team Lead separator row */}
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
                          <span style={{
                            color: TL_COLORS[agent.teamLeadName] ?? "var(--text2)",
                            fontSize: 10
                          }}>{agent.teamLeadName?.split(" ")[0]}</span>
                        </td>
                        {dates.map(d => {
                          const dayData = dayMap[d]
                          const onAL = dayData?.agents?.find((a: any) => a.employeeId === agent.employeeId)
                          const we = isWeekend(d)
                          const tod = isToday(d)

                          if (we) return (
                            <td key={d} style={{ borderLeft: "1px solid rgba(30,45,69,.3)",
                              background: "rgba(30,45,69,.2)" }} />
                          )

                          if (!onAL) return (
                            <td key={d} style={{
                              borderLeft: "1px solid rgba(30,45,69,.3)",
                              background: tod ? "rgba(59,126,255,.04)" : "transparent"
                            }} />
                          )

                          const tlColor = TL_COLORS[agent.teamLeadName] ?? "var(--accent)"
                          return (
                            <td key={d} style={{ borderLeft: "1px solid rgba(30,45,69,.3)", padding: "2px" }}>
                              <div style={{
                                background: `${tlColor}22`,
                                border: `1px solid ${tlColor}44`,
                                borderRadius: 3, padding: "2px 0",
                                textAlign: "center", fontSize: 9,
                                color: tlColor, fontFamily: "IBM Plex Mono",
                                fontWeight: 600
                              }}>
                                AL
                              </div>
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

      {/* VIEW 3 — 3 MONTHS */}
      {view === "3m" && !isLoading && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 2 }}>
          {["Mon","Tue","Wed","Thu","Fri","Sat","Sun"].map(d => (
            <div key={d} style={{ textAlign: "center", fontSize: 10, color: "var(--text3)",
              padding: "6px 0", fontFamily: "IBM Plex Mono" }}>{d}</div>
          ))}
          {dates.map(d => {
            const dayData = dayMap[d]
            const count = dayData?.totalOnAL ?? 0
            const hasWarn = dayData?.hasWarning ?? false
            const we = isWeekend(d)
            const tod = isToday(d)
            const { day, month } = dayLabel(d)

            const bg = we ? "rgba(30,45,69,.2)"
              : count === 0 ? "rgba(34,208,122,.06)"
              : count <= 2  ? "rgba(34,208,122,.12)"
              : count <= 5  ? "rgba(250,204,21,.12)"
              : "rgba(255,59,92,.15)"

            const countColor = count === 0 ? "var(--green)"
              : count <= 2 ? "var(--green)"
              : count <= 5 ? "#facc15"
              : "var(--danger)"

            return (
              <div key={d}
                onClick={() => setExpandedDay(expandedDay === d ? null : d)}
                style={{
                  background: tod ? "rgba(59,126,255,.2)" : bg,
                  border: `1px solid ${tod ? "var(--accent)" : hasWarn ? "rgba(255,59,92,.3)" : "rgba(30,45,69,.4)"}`,
                  borderRadius: 4, padding: "6px 4px", cursor: "pointer",
                  minHeight: 48, display: "flex", flexDirection: "column",
                  alignItems: "center", justifyContent: "center", gap: 2,
                  transition: "opacity .15s"
                }}>
                <div style={{ fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                  {day}.{month}
                </div>
                {!we && (
                  <div style={{ fontSize: 14, fontWeight: 700, fontFamily: "IBM Plex Mono", color: countColor }}>
                    {count}
                  </div>
                )}
                {hasWarn && <div style={{ fontSize: 9 }}>⚠️</div>}
              </div>
            )
          })}
        </div>
      )}

      {/* EXPANDED DAY DETAIL (3m view) */}
      {view === "3m" && expandedDay && dayMap[expandedDay] && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px" }}>
          <div style={{ fontSize: 13, fontWeight: 600, marginBottom: 12, color: "var(--text)" }}>
            {expandedDay} — {dayMap[expandedDay].totalOnAL} agents on AL
            {dayMap[expandedDay].hasWarning && (
              <span style={{ marginLeft: 8, fontSize: 11, color: "var(--danger)" }}>
                ⚠ Warning: {dayMap[expandedDay].warningTeams.join(", ")}
              </span>
            )}
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
            {dayMap[expandedDay].agents.map((a: any) => (
              <div key={a.employeeId} style={{
                background: `${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}18`,
                border: `1px solid ${TL_COLORS[a.teamLeadName] ?? "var(--accent)"}33`,
                borderRadius: 6, padding: "6px 10px", fontSize: 11
              }}>
                <div style={{ fontWeight: 500, color: "var(--text)" }}>{a.fullName}</div>
                <div style={{ fontSize: 10, color: TL_COLORS[a.teamLeadName] ?? "var(--text3)" }}>
                  {a.teamLeadName?.split(" ")[0]}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* VIEW 1 — 7 DAYS LIST */}
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
                    color: isToday(d) ? "var(--accent)" : "var(--text2)",
                    fontWeight: isToday(d) ? 700 : 400 }}>
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



