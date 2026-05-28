import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"

export default function SickLeave() {
  const { t } = useTranslation()
  const [teamLead, setTeamLead] = useState("")
  const [type, setType] = useState("")
  const [activeOnly, setActiveOnly] = useState(false)

  const params = `${activeOnly ? "activeOnly=true" : "from=2026-01-01&to=2026-12-31"}${teamLead ? "&teamLead=" + teamLead : ""}${type ? "&type=" + type : ""}`

  const { data, isLoading } = useQuery({ queryKey: ["sl", params], queryFn: () => api.sickLeave.get(params) })
  const { data: stats } = useQuery({ queryKey: ["sl-stats"], queryFn: api.sickLeave.stats })

  const durationColor = (days: number | null) => {
    if (!days) return "var(--text3)"
    if (days > 30) return "var(--danger)"
    if (days >= 14) return "var(--warn)"
    if (days >= 7) return "#facc15"
    return "var(--text2)"
  }

  const durationBg = (days: number | null) => {
    if (!days) return "transparent"
    if (days > 30) return "rgba(255,59,92,.08)"
    if (days >= 14) return "rgba(255,124,59,.08)"
    if (days >= 7) return "rgba(250,204,21,.08)"
    return "transparent"
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>{t("nav.sickLeave")}</h1>
        <DownloadButtons onToday={api.sickLeave.downloadToday} on7Days={api.sickLeave.download7} on30Days={api.sickLeave.download30} />
      </div>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 12 }}>
        {[
          { label: "Currently Sick", value: stats?.totalActive, color: "var(--danger)" },
          { label: "Self", value: stats?.selfCount, color: "var(--warn)" },
          { label: "Child", value: stats?.childCount, color: "#facc15" },
          { label: "Avg Duration", value: stats?.averageDuration ? `${stats.averageDuration}d` : "0d", color: "var(--text2)" },
        ].map(s => (
          <div key={s.label} style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px 20px" }}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>{s.label}</div>
            <div style={{ fontSize: 28, fontWeight: 600, fontFamily: "IBM Plex Mono", color: s.color }}>{s.value ?? 0}</div>
          </div>
        ))}
      </div>

      {stats?.byTeamLead?.length > 0 && (
        <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "14px 16px" }}>
          <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".07em", color: "var(--text3)", marginBottom: 10 }}>By Team Lead (active)</div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            {stats.byTeamLead.map((tl: any) => (
              <span key={tl.teamLead} style={{
                background: "rgba(255,124,59,.12)", color: "var(--warn)",
                padding: "3px 10px", borderRadius: 20, fontSize: 11, fontFamily: "IBM Plex Mono"
              }}>{tl.teamLead}: {tl.count}</span>
            ))}
          </div>
        </div>
      )}

      <div style={{ display: "flex", gap: 10 }}>
        <input placeholder="Team Lead..." value={teamLead} onChange={e => setTeamLead(e.target.value)}
          style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
            padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none", width: 200 }} />
        <select value={type} onChange={e => setType(e.target.value)}
          style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text2)", padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
          <option value="">All Types</option>
          <option value="Self">Self</option>
          <option value="Child">Child</option>
        </select>
        <label style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 12, color: "var(--text2)", cursor: "pointer" }}>
          <input type="checkbox" checked={activeOnly} onChange={e => setActiveOnly(e.target.checked)} />
          Active only
        </label>
      </div>

      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
            <thead>
              <tr style={{ background: "var(--card2)" }}>
                {["ID", "Name", "Team Lead", "First Day", "Last Day", "Duration", "Type", "Comments"].map(h => (
                  <th key={h} style={{ padding: "10px 12px", textAlign: "left", fontSize: 10,
                    fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em",
                    color: "var(--text3)", borderBottom: "1px solid var(--border)" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && <tr><td colSpan={8} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>Loading...</td></tr>}
              {data?.map((s: any) => (
                <tr key={s.id} style={{ borderBottom: "1px solid rgba(30,45,69,.5)", background: durationBg(s.durationDays) }}
                  onMouseEnter={ev => (ev.currentTarget.style.filter = "brightness(1.15)")}
                  onMouseLeave={ev => (ev.currentTarget.style.filter = "brightness(1)")}>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{s.employeeId}</td>
                  <td style={{ padding: "9px 12px", fontWeight: 500 }}>{s.fullName ?? `${s.firstName} ${s.lastName}`}</td>
                  <td style={{ padding: "9px 12px", color: "var(--text2)", fontSize: 11 }}>{s.teamLeadName}</td>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{s.firstDay}</td>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11 }}>{s.lastDay}</td>
                  <td style={{ padding: "9px 12px" }}>
                    <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, fontWeight: 600, color: durationColor(s.durationDays) }}>
                      {s.durationDays ?? "?"}d
                    </span>
                  </td>
                  <td style={{ padding: "9px 12px" }}>
                    <span style={{
                      background: s.leaveType === "Self" ? "rgba(255,124,59,.15)" : "rgba(250,204,21,.15)",
                      color: s.leaveType === "Self" ? "var(--warn)" : "#facc15",
                      padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono"
                    }}>{s.leaveType}</span>
                  </td>
                  <td style={{ padding: "9px 12px", color: "var(--text3)", fontSize: 11 }}>{s.comments}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
          {data?.length ?? 0} records
        </div>
      </div>
    </div>
  )
}
