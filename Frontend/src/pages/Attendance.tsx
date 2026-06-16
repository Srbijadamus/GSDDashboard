import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"

export default function Attendance() {
  const { t } = useTranslation()
  const today = new Date().toISOString().split("T")[0]
  const [date, setDate] = useState(today)
  const [country, setCountry] = useState("")

  const { data, isLoading } = useQuery({
    queryKey: ["attendance", date, country],
    queryFn: () => api.attendance.get(`from=${date}&to=${date}${country ? "&country=" + country : ""}`)
  })

  const statusColor = (type: string) => {
    if (type === "ASSIGNED") return { bg: "rgba(34,208,122,.15)", color: "var(--green)" }
    if (type === "WO")       return { bg: "rgba(255,124,59,.15)", color: "var(--warn)" }
    if (type === "CLOSED")   return { bg: "rgba(74,95,122,.15)",  color: "var(--text3)" }
    if (type === "PH")       return { bg: "rgba(250,204,21,.15)", color: "var(--yellow)" }
    return { bg: "rgba(255,255,255,.05)", color: "var(--text2)" }
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>{t("nav.attendance")}</h1>
        <DownloadButtons onToday={api.attendance.downloadToday} on7Days={api.attendance.download7} on30Days={api.attendance.download30} />
      </div>

      <div style={{ display: "flex", gap: 10 }}>
        <input type="date" value={date} onChange={e => setDate(e.target.value)}
          style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
            padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none" }} />
        <select value={country} onChange={e => setCountry(e.target.value)}
          style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text2)", padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
          <option value="">All Countries</option>
          <option value="DE">DE</option>
          <option value="NL">NL</option>
          <option value="CZ">CZ</option>
        </select>
      </div>

      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr style={{ background: "var(--card2)" }}>
              {["Location", "Country", "Date", "Employee ID", "Status", "Raw"].map(h => (
                <th key={h} style={{ padding: "10px 12px", textAlign: "left", fontSize: 10,
                  fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em",
                  color: "var(--text3)", borderBottom: "1px solid var(--border)" }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {isLoading && Array.from({length: 5}).map((_, i) => (
              <tr key={`sk-${i}`} style={{ borderBottom: "1px solid var(--border)" }}>
                {Array.from({length: 6}).map((_, j) => (
                  <td key={j} style={{ padding: "10px 12px" }}><div className="skeleton" style={{ height: 11 }} /></td>
                ))}
              </tr>
            ))}
            {data?.length === 0 && !isLoading && (
              <tr><td colSpan={6} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>No attendance data for this date</td></tr>
            )}
            {data?.map((a: any) => {
              const sc = statusColor(a.attendanceType)
              return (
                <tr key={a.id} style={{ borderBottom: "1px solid var(--border)" }}
                  onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                  onMouseLeave={ev => (ev.currentTarget.style.background = "transparent")}>
                  <td style={{ padding: "9px 12px", fontWeight: 500 }}>{a.locationName}</td>
                  <td style={{ padding: "9px 12px" }}>
                    <span style={{
                      background: a.country === "NL" ? "rgba(255,124,59,.15)" : "rgba(59,126,255,.15)",
                      color: a.country === "NL" ? "var(--warn)" : "var(--accent)",
                      padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono"
                    }}>{a.country}</span>
                  </td>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)" }}>{a.attendanceDate}</td>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{a.assignedEmployeeId ?? "—"}</td>
                  <td style={{ padding: "9px 12px" }}>
                    <span style={{ ...sc, padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono" }}>
                      {a.attendanceType ?? a.rawValue}
                    </span>
                  </td>
                  <td style={{ padding: "9px 12px", fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{a.rawValue}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
        <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
          {data?.length ?? 0} records
        </div>
      </div>
    </div>
  )
}
