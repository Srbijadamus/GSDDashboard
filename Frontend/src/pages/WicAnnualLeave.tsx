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
  if (/^(DE|NL)_/.test(name)) {
    return name.replace(/^(DE|NL)_/, "").replace(/_/g, " ")
  }
  return name
}

function buildLocMap(agents: WicAgent[]): Map<string, string> {
  const map = new Map<string, string>()
  for (const a of agents) {
    const mains = (a.wicRoles ?? []).filter(r => r.assignmentType === "MAIN")
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
    <div className="bg-raised border border-line-subtle" style={{
      borderTop: `3px solid ${color}`,
      borderRadius: 8, padding: "14px 18px",
    }}>
      <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>
        {label}
      </div>
      {isLoading
        ? <div className="skeleton" style={{ height: 36, width: 80 }} />
        : <div className="font-mono" style={{ fontSize: 30, fontWeight: 700, color }}>{value}</div>}
      {sub && !isLoading && (
        <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 4 }}>{sub}</div>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function WicAnnualLeave() {
  const [refreshKey, setRefreshKey] = useState(0)

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

    const absPerDay = new Map<string, number>()
    const cur = new Date(dates.from)
    const end = new Date(dates.to)
    while (cur <= end) {
      const dow = cur.getDay()
      if (dow !== 0 && dow !== 6) {
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
          <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600, margin: 0 }}>
            WIC Annual Leave
          </h1>
          <div className="text-ink-soft font-mono" style={{ fontSize: 12, marginTop: 4 }}>
            Checked:&nbsp;
            <span className="text-ink-muted">{dates.from}</span>
            &nbsp;→&nbsp;
            <span className="text-ink-muted">{dates.to}</span>
            &nbsp;(14 calendar days)
          </div>
        </div>
        <button
          onClick={() => setRefreshKey(k => k + 1)}
          disabled={isFetching}
          className={`bg-raised border border-line-subtle ${isFetching ? "text-ink-soft" : "text-ink-muted"}`}
          style={{
            display: "flex", alignItems: "center", gap: 6,
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
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{
          display: "flex", alignItems: "center", gap: 10,
          borderRadius: 8, padding: "12px 16px", fontSize: 13,
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
              color="rgb(var(--st-info-fg))"
            />
            <div className="bg-raised border border-line-subtle" style={{
              borderTop: `3px solid ${maxAbsences > 0 ? "rgb(var(--st-crit-fg))" : "rgb(var(--st-good-fg))"}`,
              borderRadius: 8, padding: "14px 18px",
            }}>
              <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 8 }}>
                Lowest Coverage Days
              </div>
              {lowestDays.length === 0 ? (
                <div className="text-good-fg" style={{ fontSize: 13, fontWeight: 600 }}>
                  No absences in range
                </div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
                  {lowestDays.slice(0, 4).map(d => (
                    <div key={d} style={{ display: "flex", gap: 8, alignItems: "center" }}>
                      <span className="font-mono bg-crit-bg text-crit-fg" style={{
                        fontSize: 10, fontWeight: 700,
                        padding: "1px 6px", borderRadius: 4, minWidth: 30, textAlign: "center",
                      }}>
                        {DOW[new Date(d).getDay()]}
                      </span>
                      <span className="font-mono text-ink-muted" style={{ fontSize: 12 }}>
                        {displayDate(d)}
                      </span>
                      <span className="text-ink-soft" style={{ fontSize: 11 }}>
                        {maxAbsences} absent
                      </span>
                    </div>
                  ))}
                  {lowestDays.length > 4 && (
                    <div className="text-ink-soft" style={{ fontSize: 11 }}>
                      +{lowestDays.length - 4} more days
                    </div>
                  )}
                </div>
              )}
            </div>
            <div className="bg-raised border border-line-subtle" style={{
              borderTop: "3px solid rgb(var(--st-good-fg))",
              borderRadius: 8, padding: "14px 18px",
            }}>
              <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 8 }}>
                Period
              </div>
              <div className="font-mono text-ink-muted" style={{ fontSize: 12, lineHeight: 2 }}>
                <div>{displayDate(dates.from)}</div>
                <div className="text-ink-soft" style={{ fontSize: 11 }}>↓ 14 calendar days</div>
                <div>{displayDate(dates.to)}</div>
              </div>
            </div>
          </div>

          {/* Table */}
          {wicVacs.length === 0 ? (
            <div className="bg-raised border border-line-subtle" style={{
              borderRadius: 8,
              padding: "48px 20px", textAlign: "center",
            }}>
              <CalendarOff size={32} className="text-ink-soft" style={{ marginBottom: 12 }} />
              <div className="text-ink-muted" style={{ fontSize: 14, fontWeight: 600 }}>
                No annual leave found for WIC agents
              </div>
              <div className="text-ink-soft font-mono" style={{ fontSize: 12, marginTop: 4 }}>
                {dates.from} → {dates.to}
              </div>
            </div>
          ) : (
            <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
              <div className="border-b border-line-subtle" style={{ padding: "12px 16px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <div className="text-ink-muted" style={{ fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", display: "flex", alignItems: "center", gap: 8 }}>
                  <Users size={13} />
                  Leave Records
                  <span className="font-mono bg-info-bg text-info-fg" style={{ fontSize: 10, padding: "1px 7px", borderRadius: 10 }}>
                    {wicVacs.length}
                  </span>
                </div>
                <div className="text-ink-soft font-mono" style={{ fontSize: 11 }}>
                  sorted by start date
                </div>
              </div>
              <div style={{ overflowX: "auto" }}>
                <table style={{ borderCollapse: "collapse", fontSize: 12, width: "100%", minWidth: 620 }}>
                  <thead>
                    <tr className="bg-sunken">
                      {[
                        { label: "Employee",    width: "auto" },
                        { label: "Employee ID", width: 110 },
                        { label: "AL Start",    width: 110 },
                        { label: "AL End",      width: 110 },
                        { label: "Work Days",   width: 90 },
                        { label: "WIC Location",width: "auto" },
                      ].map(({ label, width }) => (
                        <th key={label} className="text-ink-soft border-b border-line-subtle" style={{
                          padding: "10px 14px", textAlign: "left",
                          fontSize: 10, fontWeight: 600,
                          textTransform: "uppercase", letterSpacing: ".06em",
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
                        className="border-b border-line-subtle transition-colors hover:bg-hovered"
                        style={{
                          background: i % 2 === 0 ? "transparent" : "rgba(0,0,0,.018)",
                        }}
                      >
                        <td className="text-ink" style={{ padding: "10px 14px", fontWeight: 500 }}>
                          {row.fullName}
                        </td>
                        <td className="font-mono text-ink-soft" style={{ padding: "10px 14px", fontSize: 11 }}>
                          {row.employeeId}
                        </td>
                        <td className="font-mono text-ink-muted" style={{ padding: "10px 14px", whiteSpace: "nowrap" }}>
                          {displayDate(row.firstDay)}
                        </td>
                        <td className="font-mono text-ink-muted" style={{ padding: "10px 14px", whiteSpace: "nowrap" }}>
                          {displayDate(row.lastDay)}
                        </td>
                        <td className="font-mono" style={{ padding: "10px 14px", textAlign: "center", fontWeight: 600 }}>
                          <span className="bg-info-bg text-info-fg" style={{ padding: "2px 9px", borderRadius: 4 }}>
                            {row.workDaysNet}
                          </span>
                        </td>
                        <td style={{ padding: "10px 14px" }}>
                          {row.wicLocation !== "—" ? (
                            <span className="bg-good-bg text-good-fg" style={{
                              padding: "2px 9px", borderRadius: 4, fontSize: 11, fontWeight: 500,
                            }}>
                              {row.wicLocation}
                            </span>
                          ) : (
                            <span className="text-ink-soft">—</span>
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
