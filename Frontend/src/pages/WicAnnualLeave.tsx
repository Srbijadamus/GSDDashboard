import { useMemo, useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { RefreshCw, AlertTriangle, Users, CalendarOff } from "lucide-react"
import { apiFetch } from "../api/client"

// ── Types ─────────────────────────────────────────────────────────────────────

interface WicAgent {
  employeeId: string
  fullName: string
  wicRoles: Array<{ locationCode: string; displayName: string; assignmentType: string }>
}

interface VacationRecord {
  id: number
  employeeId: string
  firstName: string   // API maps employee.FullName into this field
  firstDay: string
  lastDay: string
  workDaysNet: number
  sourceSheet: string
}

interface WicVacRow {
  id: number
  employeeId: string
  fullName: string
  firstDay: string
  lastDay: string
  workDaysNet: number
  wicLocation: string
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtDate(d: Date): string {
  return (
    d.getFullYear() + "-" +
    String(d.getMonth() + 1).padStart(2, "0") + "-" +
    String(d.getDate()).padStart(2, "0")
  )
}

function displayDate(iso: string): string {
  const [y, m, d] = iso.split("-")
  return `${d}.${m}.${y}`
}

const DOW = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

function cleanLocation(name: string): string {
  // Prefer names that are already clean ("Helmstedt", "Neu-Isenburg")
  // Strip legacy prefixes like "DE_Helmstedt" or "NL_Denbosch"
  if (/^(DE|NL)_/.test(name)) {
    return name.replace(/^(DE|NL)_/, "").replace(/_/g, " ")
  }
  return name
}

function buildLocMap(agents: WicAgent[]): Map<string, string> {
  const map = new Map<string, string>()
  for (const a of agents) {
    const mains = (a.wicRoles ?? []).filter(r => r.assignmentType === "MAIN")
    // Prefer a clean displayName (not a legacy DE_/NL_ code)
    const best =
      mains.find(r => !/^(DE|NL)_/.test(r.displayName)) ?? mains[0]
    if (best) map.set(a.employeeId, cleanLocation(best.displayName))
  }
  return map
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

function KpiCard({
  label, value, sub, color, isLoading,
}: {
  label: string; value: React.ReactNode; sub?: string; color: string; isLoading?: boolean
}) {
  return (
    <div style={{
      background: "var(--card)", border: "1px solid var(--border)",
      borderTop: `3px solid ${color}`, borderRadius: 8, padding: "14px 18px",
    }}>
      <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>
        {label}
      </div>
      {isLoading
        ? <div className="skeleton" style={{ height: 36, width: 80 }} />
        : <div style={{ fontSize: 30, fontWeight: 700, fontFamily: "IBM Plex Mono", color }}>{value}</div>}
      {sub && !isLoading && (
        <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 4 }}>{sub}</div>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function WicAnnualLeave() {
  const [refreshKey, setRefreshKey] = useState(0)

  // Dates computed once per mount / refresh — uses local time to avoid UTC offset issues
  const dates = useMemo(() => {
    const now = new Date()
    const end = new Date(now)
    end.setDate(end.getDate() + 14)
    return { from: fmtDate(now), to: fmtDate(end) }
  }, [refreshKey]) // eslint-disable-line react-hooks/exhaustive-deps

  // ── Queries ─────────────────────────────────────────────────────────────────

  const {
    data: agents,
    isLoading: agentsLoading,
    isError: agentsError,
    isFetching: agentsFetching,
  } = useQuery({
    queryKey: ["wic-agents-al", refreshKey],
    queryFn: () => apiFetch<WicAgent[]>("/api/wic-coverage/agents"),
    staleTime: 0,
    retry: 1,
  })

  const {
    data: vacations,
    isLoading: vacsLoading,
    isError: vacsError,
    isFetching: vacsFetching,
  } = useQuery({
    queryKey: ["wic-al-vacs", dates.from, dates.to, refreshKey],
    queryFn: () => apiFetch<VacationRecord[]>(
      `/api/vacations?from=${dates.from}&to=${dates.to}`
    ),
    staleTime: 0,
    retry: 1,
  })

  const isLoading  = agentsLoading || vacsLoading
  const isFetching = agentsFetching || vacsFetching
  const isError    = agentsError || vacsError

  // ── Cross-reference ──────────────────────────────────────────────────────────

  const wicVacs = useMemo<WicVacRow[]>(() => {
    if (!agents || !vacations) return []
    const wicIds = new Set(agents.map(a => a.employeeId))
    const locMap = buildLocMap(agents)
    return vacations
      .filter(v => wicIds.has(v.employeeId))
      .map(v => ({
        id:          v.id,
        employeeId:  v.employeeId,
        fullName:    v.firstName,   // API maps fullName → firstName
        firstDay:    v.firstDay,
        lastDay:     v.lastDay,
        workDaysNet: v.workDaysNet,
        wicLocation: locMap.get(v.employeeId) ?? "—",
      }))
      .sort((a, b) => a.firstDay.localeCompare(b.firstDay))
  }, [agents, vacations])

  // ── Summary ──────────────────────────────────────────────────────────────────

  const { uniqueAgents, lowestDays, maxAbsences } = useMemo(() => {
    if (wicVacs.length === 0) return { uniqueAgents: 0, lowestDays: [] as string[], maxAbsences: 0 }

    // Count absences per working day in the range
    const absPerDay = new Map<string, number>()
    const cur = new Date(dates.from)
    const end = new Date(dates.to)
    while (cur <= end) {
      const dow = cur.getDay()
      if (dow !== 0 && dow !== 6) {      // weekdays only
        const d = fmtDate(cur)
        const n = wicVacs.filter(v => v.firstDay <= d && v.lastDay >= d).length
        absPerDay.set(d, n)
      }
      cur.setDate(cur.getDate() + 1)
    }

    const maxAbs = Math.max(...absPerDay.values(), 0)
    const lowest = maxAbs > 0
      ? [...absPerDay.entries()].filter(([, c]) => c === maxAbs).map(([d]) => d)
      : []

    return {
      uniqueAgents: new Set(wicVacs.map(v => v.employeeId)).size,
      lowestDays:   lowest,
      maxAbsences:  maxAbs,
    }
  }, [wicVacs, dates])

  // ── Render ───────────────────────────────────────────────────────────────────

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>

      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)", margin: 0 }}>
            WIC Annual Leave
          </h1>
          <div style={{ fontSize: 12, color: "var(--text3)", marginTop: 4, fontFamily: "IBM Plex Mono" }}>
            Checked:&nbsp;
            <span style={{ color: "var(--text2)" }}>{dates.from}</span>
            &nbsp;→&nbsp;
            <span style={{ color: "var(--text2)" }}>{dates.to}</span>
            &nbsp;(14 calendar days)
          </div>
        </div>
        <button
          onClick={() => setRefreshKey(k => k + 1)}
          disabled={isFetching}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            background: "var(--card)", border: "1px solid var(--border)",
            color: isFetching ? "var(--text3)" : "var(--text2)",
            padding: "8px 14px", borderRadius: 6, fontSize: 12,
            cursor: isFetching ? "not-allowed" : "pointer",
          }}
        >
          <RefreshCw
            size={13}
            className={isFetching ? "spin" : undefined}
          />
          {isFetching ? "Loading…" : "Refresh"}
        </button>
      </div>

      {/* Error banner */}
      {isError && (
        <div style={{
          display: "flex", alignItems: "center", gap: 10,
          background: "rgba(255,59,92,.08)", border: "1px solid rgba(255,59,92,.3)",
          borderRadius: 8, padding: "12px 16px", color: "var(--danger)", fontSize: 13,
        }}>
          <AlertTriangle size={16} style={{ flexShrink: 0 }} />
          <span>
            Could not retrieve data from the backend — please check the API connection.
          </span>
        </div>
      )}

      {/* Loading skeletons */}
      {isLoading && !isError && (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 12 }}>
            {[1, 2, 3].map(i => <div key={i} className="skeleton" style={{ height: 90, borderRadius: 8 }} />)}
          </div>
          {[1, 2, 3, 4, 5].map(i => <div key={i} className="skeleton" style={{ height: 40, borderRadius: 6 }} />)}
        </div>
      )}

      {/* Content */}
      {!isLoading && !isError && (
        <>
          {/* KPI row */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(3,1fr)", gap: 12 }}>
            <KpiCard
              label="WIC Agents on Leave"
              value={uniqueAgents}
              sub={`${wicVacs.length} leave record${wicVacs.length !== 1 ? "s" : ""}`}
              color="var(--accent)"
            />
            <div style={{
              background: "var(--card)", border: "1px solid var(--border)",
              borderTop: `3px solid ${maxAbsences > 0 ? "var(--danger)" : "var(--green)"}`,
              borderRadius: 8, padding: "14px 18px",
            }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 8 }}>
                Lowest Coverage Days
              </div>
              {lowestDays.length === 0 ? (
                <div style={{ fontSize: 13, color: "var(--green)", fontWeight: 600 }}>
                  No absences in range
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                  {lowestDays.slice(0, 4).map(d => (
                    <div key={d} style={{ display: "flex", gap: 8, alignItems: "center" }}>
                      <span style={{
                        fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
                        background: "rgba(255,59,92,.12)", color: "var(--danger)",
                        padding: "1px 6px", borderRadius: 4, minWidth: 30, textAlign: "center",
                      }}>
                        {DOW[new Date(d).getDay()]}
                      </span>
                      <span style={{ fontSize: 12, fontFamily: "IBM Plex Mono", color: "var(--text2)" }}>
                        {displayDate(d)}
                      </span>
                      <span style={{ fontSize: 11, color: "var(--text3)" }}>
                        {maxAbsences} absent
                      </span>
                    </div>
                  ))}
                  {lowestDays.length > 4 && (
                    <div style={{ fontSize: 11, color: "var(--text3)" }}>
                      +{lowestDays.length - 4} more days
                    </div>
                  )}
                </div>
              )}
            </div>
            <div style={{
              background: "var(--card)", border: "1px solid var(--border)",
              borderTop: "3px solid var(--green)", borderRadius: 8, padding: "14px 18px",
            }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 8 }}>
                Period
              </div>
              <div style={{ fontFamily: "IBM Plex Mono", fontSize: 12, lineHeight: 2, color: "var(--text2)" }}>
                <div>{displayDate(dates.from)}</div>
                <div style={{ color: "var(--text3)", fontSize: 11 }}>↓ 14 calendar days</div>
                <div>{displayDate(dates.to)}</div>
              </div>
            </div>
          </div>

          {/* Table */}
          {wicVacs.length === 0 ? (
            <div style={{
              background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8,
              padding: "48px 20px", textAlign: "center",
            }}>
              <CalendarOff size={32} style={{ color: "var(--text3)", marginBottom: 12 }} />
              <div style={{ fontSize: 14, fontWeight: 600, color: "var(--text2)" }}>
                No annual leave found for WIC agents
              </div>
              <div style={{ fontSize: 12, color: "var(--text3)", marginTop: 4, fontFamily: "IBM Plex Mono" }}>
                {dates.from} → {dates.to}
              </div>
            </div>
          ) : (
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
              <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <div style={{ fontSize: 11, fontWeight: 600, color: "var(--text2)", textTransform: "uppercase", letterSpacing: ".08em", display: "flex", alignItems: "center", gap: 8 }}>
                  <Users size={13} />
                  Leave Records
                  <span style={{ fontSize: 10, background: "rgba(59,126,255,.12)", color: "var(--accent)", padding: "1px 7px", borderRadius: 10, fontFamily: "IBM Plex Mono" }}>
                    {wicVacs.length}
                  </span>
                </div>
                <div style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                  sorted by start date
                </div>
              </div>
              <div style={{ overflowX: "auto" }}>
                <table style={{ borderCollapse: "collapse", fontSize: 12, width: "100%", minWidth: 620 }}>
                  <thead>
                    <tr style={{ background: "var(--card2)" }}>
                      {[
                        { label: "Employee",    width: "auto" },
                        { label: "Employee ID", width: 110 },
                        { label: "AL Start",    width: 110 },
                        { label: "AL End",      width: 110 },
                        { label: "Work Days",   width: 90 },
                        { label: "WIC Location",width: "auto" },
                      ].map(({ label, width }) => (
                        <th key={label} style={{
                          padding: "10px 14px", textAlign: "left",
                          fontSize: 10, fontWeight: 600, color: "var(--text3)",
                          textTransform: "uppercase", letterSpacing: ".06em",
                          borderBottom: "1px solid var(--border)",
                          whiteSpace: "nowrap",
                          width: width === "auto" ? undefined : width,
                        }}>
                          {label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {wicVacs.map((row, i) => (
                      <tr
                        key={row.id}
                        style={{
                          borderBottom: "1px solid var(--border)",
                          background: i % 2 === 0 ? "transparent" : "rgba(0,0,0,.018)",
                        }}
                      >
                        <td style={{ padding: "10px 14px", fontWeight: 500, color: "var(--text)" }}>
                          {row.fullName}
                        </td>
                        <td style={{ padding: "10px 14px", fontFamily: "IBM Plex Mono", color: "var(--text3)", fontSize: 11 }}>
                          {row.employeeId}
                        </td>
                        <td style={{ padding: "10px 14px", fontFamily: "IBM Plex Mono", color: "var(--text2)", whiteSpace: "nowrap" }}>
                          {displayDate(row.firstDay)}
                        </td>
                        <td style={{ padding: "10px 14px", fontFamily: "IBM Plex Mono", color: "var(--text2)", whiteSpace: "nowrap" }}>
                          {displayDate(row.lastDay)}
                        </td>
                        <td style={{ padding: "10px 14px", fontFamily: "IBM Plex Mono", textAlign: "center", fontWeight: 600 }}>
                          <span style={{
                            background: "rgba(59,126,255,.1)", color: "var(--accent)",
                            padding: "2px 9px", borderRadius: 4,
                          }}>
                            {row.workDaysNet}
                          </span>
                        </td>
                        <td style={{ padding: "10px 14px" }}>
                          {row.wicLocation !== "—" ? (
                            <span style={{
                              background: "rgba(34,208,122,.1)", color: "var(--green)",
                              padding: "2px 9px", borderRadius: 4, fontSize: 11, fontWeight: 500,
                            }}>
                              {row.wicLocation}
                            </span>
                          ) : (
                            <span style={{ color: "var(--text3)" }}>—</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}
