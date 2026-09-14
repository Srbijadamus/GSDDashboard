import { useQuery } from "@tanstack/react-query"
import { useState, useMemo } from "react"
import { useTranslation } from "react-i18next"
import { apiFetch, api } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { EmptyState } from "../components/EmptyState"
import { ShiftBadge, type ShiftStatus } from "../components/ShiftBadge"
import { StatusBadge } from "../components/StatusBadge"

interface AgentAttendanceDto {
  employeeId: string
  fullName: string | null
  teamLeadName: string | null
  primaryRole: string | null
  wicLocation: string | null
  shiftDate: string
  plannedStart: string | null
  plannedEnd: string | null
  shiftType: string | null
  agentTask: string | null
}

interface KioskRecord {
  employee_id: string
  checkin_time?: string | null
  [key: string]: unknown
}

type AttendanceStatus = "Present" | "Absent" | "No Check-in"

const ABSENT_TYPES = new Set([
  "SL", "AL", "OFF", "OFF_WEEKEND", "PH", "LPH", "UL", "OL", "RESIGNED",
])

const KNOWN_SHIFT_TYPES = new Set<ShiftStatus>([
  "WORKING", "OFF", "OFF_WEEKEND", "WIC_DUTY", "AL", "HALF_AL",
  "SL", "UL", "TRAINING", "PH", "LPH", "CD", "CO", "OL", "RESIGNED",
])

function isKnownShiftType(t: string | null): t is ShiftStatus {
  return t != null && KNOWN_SHIFT_TYPES.has(t as ShiftStatus)
}

function deriveStatus(
  shiftType: string | null,
  kioskRecord: KioskRecord | undefined,
): AttendanceStatus {
  if (shiftType && ABSENT_TYPES.has(shiftType)) return "Absent"
  if (kioskRecord) return "Present"
  return "No Check-in"
}

const KIOSK_URL = import.meta.env.VITE_KIOSK_API_URL as string | undefined
const COLS = 7

export default function Attendance() {
  const { t } = useTranslation()
  const today = new Date().toISOString().split("T")[0]

  const [date, setDate] = useState(today)
  const [teamLead, setTeamLead] = useState("")
  const [location, setLocation] = useState("")
  const [statusFilter, setStatusFilter] = useState<"all" | AttendanceStatus>("all")

  const { data: agents = [], isLoading } = useQuery<AgentAttendanceDto[]>({
    queryKey: ["attendance-agents", date, teamLead, location],
    queryFn: () => {
      const params = new URLSearchParams({ date })
      if (teamLead) params.set("teamLead", teamLead)
      if (location) params.set("location", location)
      return apiFetch<AgentAttendanceDto[]>(`/api/attendance/agents?${params}`)
    },
  })

  const { data: kioskRecords = [] } = useQuery<KioskRecord[]>({
    queryKey: ["attendance-kiosk"],
    queryFn: async () => {
      if (!KIOSK_URL) return []
      try {
        const res = await fetch(`${KIOSK_URL}/api/attendance`)
        if (!res.ok) return []
        return (await res.json()) as KioskRecord[]
      } catch {
        return []
      }
    },
    refetchInterval: 60_000,
    retry: false,
  })

  const { data: teamLeads = [] } = useQuery<string[]>({
    queryKey: ["attendance-teamleads"],
    queryFn: () => apiFetch<string[]>("/api/attendance/teamleads"),
  })

  const kioskMap = useMemo(() => {
    const m = new Map<string, KioskRecord>()
    for (const r of kioskRecords) m.set(r.employee_id, r)
    return m
  }, [kioskRecords])

  const rows = useMemo(
    () =>
      agents.map(a => ({
        ...a,
        kiosk: kioskMap.get(a.employeeId),
        status: deriveStatus(a.shiftType, kioskMap.get(a.employeeId)),
      })),
    [agents, kioskMap],
  )

  const filtered = useMemo(
    () => (statusFilter === "all" ? rows : rows.filter(r => r.status === statusFilter)),
    [rows, statusFilter],
  )

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600 }}>
          {t("nav.attendance")}
        </h1>
        <DownloadButtons
          onToday={api.attendance.downloadToday}
          on7Days={api.attendance.download7}
          on30Days={api.attendance.download30}
        />
      </div>

      {/* Filter bar */}
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
        <input
          type="date"
          value={date}
          onChange={e => setDate(e.target.value)}
          className="bg-raised border border-line-subtle text-ink"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none" }}
        />
        <select
          value={teamLead}
          onChange={e => setTeamLead(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12 }}
        >
          <option value="">All Team Leads</option>
          {teamLeads.map(tl => (
            <option key={tl} value={tl}>{tl}</option>
          ))}
        </select>
        <input
          type="text"
          value={location}
          onChange={e => setLocation(e.target.value)}
          placeholder="Location…"
          className="bg-raised border border-line-subtle text-ink"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none", minWidth: 150 }}
        />
        <select
          value={statusFilter}
          onChange={e => setStatusFilter(e.target.value as "all" | AttendanceStatus)}
          className="bg-raised border border-line-subtle text-ink-muted"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12 }}
        >
          <option value="all">All Statuses</option>
          <option value="Present">Present</option>
          <option value="No Check-in">No Check-in</option>
          <option value="Absent">Absent</option>
        </select>
      </div>

      {/* Table */}
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr className="bg-sunken">
              {["Agent", "Team Lead", "Location", "Planned", "Check-in", "Status", "Shift Type"].map(h => (
                <th
                  key={h}
                  className="text-ink-soft border-b border-line-subtle"
                  style={{
                    padding: "10px 12px",
                    textAlign: "left",
                    fontSize: 10,
                    fontWeight: 500,
                    textTransform: "uppercase",
                    letterSpacing: ".07em",
                  }}
                >
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {isLoading &&
              Array.from({ length: 8 }).map((_, i) => (
                <tr key={`sk-${i}`} className="border-b border-line-subtle">
                  {Array.from({ length: COLS }).map((_, j) => (
                    <td key={j} style={{ padding: "10px 12px" }}>
                      <div className="skeleton" style={{ height: 11 }} />
                    </td>
                  ))}
                </tr>
              ))}

            {!isLoading && filtered.length === 0 && (
              <tr>
                <td colSpan={COLS}>
                  <EmptyState title="No shift data for this date" />
                </td>
              </tr>
            )}

            {!isLoading &&
              filtered.map(row => (
                <tr
                  key={row.employeeId}
                  className="border-b border-line-subtle transition-colors hover:bg-hovered"
                >
                  {/* Agent */}
                  <td style={{ padding: "9px 12px" }}>
                    <div className="text-ink" style={{ fontWeight: 500 }}>
                      {row.fullName ?? row.employeeId}
                    </div>
                    <div className="font-mono text-ink-muted" style={{ fontSize: 10 }}>
                      {row.employeeId}
                    </div>
                  </td>

                  {/* Team Lead */}
                  <td className="text-ink-soft" style={{ padding: "9px 12px", fontSize: 11 }}>
                    {row.teamLeadName ?? "—"}
                  </td>

                  {/* Location */}
                  <td className="text-ink-soft" style={{ padding: "9px 12px", fontSize: 11 }}>
                    {row.wicLocation ?? "—"}
                  </td>

                  {/* Planned */}
                  <td className="font-mono text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>
                    {row.plannedStart && row.plannedEnd
                      ? `${row.plannedStart} – ${row.plannedEnd}`
                      : row.plannedStart ?? "—"}
                  </td>

                  {/* Check-in */}
                  <td className="font-mono text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>
                    {row.kiosk
                      ? typeof row.kiosk.checkin_time === "string"
                        ? row.kiosk.checkin_time.slice(11, 16)
                        : "✓"
                      : "—"}
                  </td>

                  {/* Status */}
                  <td style={{ padding: "9px 12px" }}>
                    <StatusBadge
                      tone={
                        row.status === "Present"
                          ? "good"
                          : row.status === "Absent"
                            ? "warn"
                            : "neutralst"
                      }
                    >
                      {row.status}
                    </StatusBadge>
                  </td>

                  {/* Shift Type */}
                  <td style={{ padding: "9px 12px" }}>
                    {isKnownShiftType(row.shiftType) ? (
                      <ShiftBadge status={row.shiftType} />
                    ) : (
                      <span className="font-mono text-ink-muted" style={{ fontSize: 11 }}>
                        {row.shiftType ?? "—"}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
          </tbody>
        </table>

        {/* Footer */}
        <div
          className="border-t border-line-subtle text-ink-soft font-mono"
          style={{ padding: "8px 12px", fontSize: 11 }}
        >
          {filtered.length} / {agents.length} agents
        </div>
      </div>
    </div>
  )
}
