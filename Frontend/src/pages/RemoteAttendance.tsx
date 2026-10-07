import { useMemo, useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { Headphones } from "lucide-react"
import { apiFetch } from "../api/client"
import { DataTable, type DataColumn } from "../components/DataTable"
import { StatusBadge, type StatusTone } from "../components/StatusBadge"
import { EmptyState } from "../components/EmptyState"
import { isTestEmployee } from "./attendance/checkinStatus"

// Group separation 2026-10-06: Voice, VWIC (Virtual WIC) and BO agents work from
// home or from an office — their location is irrelevant and must never be asked
// for or displayed, unlike WIC. This page is their equivalent of /wic-attendance:
// same single kiosk feed (ShiftKiosk server.py db_get_attendance, proxied through
// GSDDashboard's KioskProxy with a service device token — see KioskProxyService.cs),
// filtered to the three non-WIC groups instead of to WIC. No coverage, no centres,
// no map: those concepts do not exist for remote work.
//
// Every user-visible string on this page goes through i18n (EN/DE) — no language may
// see text from another language. The group names Voice/VWIC/BO are established
// product names and stay identical in both languages.

type RemoteGroup = "VOICE" | "VWIC" | "BO"
const REMOTE_GROUPS: RemoteGroup[] = ["VOICE", "VWIC", "BO"]

interface KioskRecord {
  employee_id: string
  full_name: string
  team_leader?: string | null
  attendance_status: "ACTIVE" | "NOT_CHECKED_IN" | "DONE" | "PAUSE" | "EMERGENCY" | "SICK"
  checkin_time: string | null
  checkout_time: string | null
  minutes_on_shift: number
  agent_group?: string | null
}

const STATUS_TONE: Record<string, StatusTone> = {
  ACTIVE: "good",
  PAUSE: "warn",
  DONE: "mutedst",
  EMERGENCY: "crit",
  SICK: "crit",
  NOT_CHECKED_IN: "neutralst",
}

// Established names — never translated.
const GROUP_LABEL: Record<RemoteGroup, string> = {
  VOICE: "Voice",
  VWIC: "VWIC",
  BO: "BO",
}

// Kiosk status codes mapped to translation keys; unknown codes fall back to the raw
// code (defaultValue) so a future status can never render as an empty cell.
const STATUS_I18N: Record<string, string> = {
  ACTIVE: "active",
  PAUSE: "pause",
  DONE: "done",
  EMERGENCY: "emergency",
  SICK: "sick",
  NOT_CHECKED_IN: "notCheckedIn",
}

function formatTime(iso: string | null): string {
  if (!iso) return "—"
  return iso.slice(11, 16)
}

function formatHours(minutes: number | null | undefined): string {
  if (!minutes || minutes <= 0) return "—"
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return `${h}h ${String(m).padStart(2, "0")}m`
}

export default function RemoteAttendance() {
  const { t } = useTranslation()
  const [groupFilter, setGroupFilter] = useState<RemoteGroup | "ALL">("ALL")

  const { data: kioskData = [], isLoading, isError } = useQuery<KioskRecord[]>({
    queryKey: ["kiosk-attendance"],
    queryFn: () => apiFetch<KioskRecord[]>("/api/kiosk/attendance"),
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
    refetchOnMount: "always",
    refetchOnWindowFocus: true,
  })

  const rows = useMemo(() => {
    return kioskData
      .filter(r => !isTestEmployee(r.employee_id))
      .filter(r => REMOTE_GROUPS.includes((r.agent_group ?? "") as RemoteGroup))
      .filter(r => groupFilter === "ALL" || r.agent_group === groupFilter)
      .sort((a, b) => a.full_name.localeCompare(b.full_name))
  }, [kioskData, groupFilter])

  const counts = useMemo(() => {
    const c: Record<string, number> = { ALL: 0, VOICE: 0, VWIC: 0, BO: 0 }
    for (const r of kioskData) {
      if (isTestEmployee(r.employee_id)) continue
      if (!REMOTE_GROUPS.includes((r.agent_group ?? "") as RemoteGroup)) continue
      c.ALL++
      c[r.agent_group as RemoteGroup]++
    }
    return c
  }, [kioskData])

  const columns: DataColumn<KioskRecord>[] = [
    {
      key: "name", header: t("remoteAttendance.name"),
      render: r => <span className="text-ink font-medium">{r.full_name}</span>,
    },
    {
      key: "group", header: t("remoteAttendance.group"),
      render: r => <StatusBadge tone="info">{GROUP_LABEL[r.agent_group as RemoteGroup] ?? r.agent_group}</StatusBadge>,
    },
    {
      key: "tl", header: t("remoteAttendance.teamLeader"),
      render: r => <span className="text-ink-muted">{r.team_leader || "—"}</span>,
    },
    {
      key: "status", header: t("remoteAttendance.status"),
      render: r => (
        <StatusBadge tone={STATUS_TONE[r.attendance_status] ?? "neutralst"}>
          {t(`remoteAttendance.statusLabel.${STATUS_I18N[r.attendance_status] ?? "unknown"}`, { defaultValue: r.attendance_status })}
        </StatusBadge>
      ),
    },
    { key: "checkin", header: t("remoteAttendance.checkIn"), render: r => <span className="font-mono text-ink-muted">{formatTime(r.checkin_time)}</span> },
    { key: "checkout", header: t("remoteAttendance.checkOut"), render: r => <span className="font-mono text-ink-muted">{formatTime(r.checkout_time)}</span> },
    { key: "hours", header: t("remoteAttendance.hours"), render: r => <span className="font-mono text-ink-muted">{formatHours(r.minutes_on_shift)}</span> },
  ]

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 10 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <Headphones size={18} className="text-info-fg" />
          <h1 className="text-ink" style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>{t("remoteAttendance.title")}</h1>
          <span className="bg-info-solid font-mono" style={{ color: "#fff", borderRadius: 12, padding: "2px 10px", fontSize: 12, fontWeight: 600 }}>
            {counts.ALL}
          </span>
        </div>
        <div style={{ display: "flex", gap: 6 }}>
          {(["ALL", ...REMOTE_GROUPS] as const).map(g => (
            <button
              key={g}
              onClick={() => setGroupFilter(g)}
              className={g === groupFilter ? "bg-info-solid" : "bg-sunken border border-line-subtle text-ink-muted"}
              style={{
                color: g === groupFilter ? "#fff" : undefined,
                border: g === groupFilter ? "none" : undefined,
                borderRadius: 6, padding: "6px 12px", fontSize: 12, cursor: "pointer", fontWeight: 600,
              }}
            >
              {g === "ALL" ? t("remoteAttendance.all", { n: counts.ALL }) : `${GROUP_LABEL[g]} (${counts[g]})`}
            </button>
          ))}
        </div>
      </div>

      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
        {isLoading ? (
          <div className="text-ink-soft" style={{ padding: 40, textAlign: "center", fontSize: 13 }}>{t("remoteAttendance.loading")}</div>
        ) : isError ? (
          <div className="text-crit-fg" style={{ padding: 40, textAlign: "center", fontSize: 13 }}>
            {t("remoteAttendance.error")}
          </div>
        ) : rows.length === 0 ? (
          <EmptyState title={t("remoteAttendance.emptyTitle")} description={t("remoteAttendance.emptyDescription")} />
        ) : (
          <DataTable columns={columns} data={rows} rowKey={r => r.employee_id} />
        )}
      </div>
    </div>
  )
}
