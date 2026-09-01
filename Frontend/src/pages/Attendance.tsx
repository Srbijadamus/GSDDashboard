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

  const statusClass = (type: string): string => {
    if (type === "ASSIGNED") return "bg-good-bg text-good-fg"
    if (type === "WO")       return "bg-warn-bg text-warn-fg"
    if (type === "CLOSED")   return "bg-neutralst-bg text-ink-soft"
    if (type === "PH")       return "bg-holiday-bg text-holiday-fg"
    return "text-ink-muted"
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600 }}>{t("nav.attendance")}</h1>
        <DownloadButtons onToday={api.attendance.downloadToday} on7Days={api.attendance.download7} on30Days={api.attendance.download30} />
      </div>

      <div style={{ display: "flex", gap: 10 }}>
        <input type="date" value={date} onChange={e => setDate(e.target.value)}
          className="bg-raised border border-line-subtle text-ink"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none" }} />
        <select value={country} onChange={e => setCountry(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
          <option value="">All Countries</option>
          <option value="DE">DE</option>
          <option value="NL">NL</option>
          <option value="CZ">CZ</option>
        </select>
      </div>

      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr className="bg-sunken">
              {["Location", "Country", "Date", "Employee ID", "Status", "Raw"].map(h => (
                <th key={h} className="text-ink-soft border-b border-line-subtle" style={{ padding: "10px 12px", textAlign: "left", fontSize: 10,
                  fontWeight: 500, textTransform: "uppercase", letterSpacing: ".07em" }}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {isLoading && Array.from({length: 5}).map((_, i) => (
              <tr key={`sk-${i}`} className="border-b border-line-subtle">
                {Array.from({length: 6}).map((_, j) => (
                  <td key={j} style={{ padding: "10px 12px" }}><div className="skeleton" style={{ height: 11 }} /></td>
                ))}
              </tr>
            ))}
            {data?.length === 0 && !isLoading && (
              <tr><td colSpan={6} className="text-ink-soft" style={{ padding: 24, textAlign: "center" }}>No attendance data for this date</td></tr>
            )}
            {data?.map((a: any) => {
              const sc = statusClass(a.attendanceType)
              return (
                <tr key={a.id} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                  <td style={{ padding: "9px 12px", fontWeight: 500 }}>{a.locationName}</td>
                  <td style={{ padding: "9px 12px" }}>
                    <span className={`font-mono ${a.country === "NL" ? "bg-warn-bg text-warn-fg" : "bg-info-bg text-info-fg"}`}
                      style={{ padding: "2px 7px", borderRadius: 4, fontSize: 10 }}>{a.country}</span>
                  </td>
                  <td className="font-mono text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>{a.attendanceDate}</td>
                  <td className="font-mono text-ink-soft" style={{ padding: "9px 12px", fontSize: 11 }}>{a.assignedEmployeeId ?? "—"}</td>
                  <td style={{ padding: "9px 12px" }}>
                    <span className={`font-mono ${sc}`} style={{ padding: "2px 7px", borderRadius: 4, fontSize: 10 }}>
                      {a.attendanceType ?? a.rawValue}
                    </span>
                  </td>
                  <td className="font-mono text-ink-soft" style={{ padding: "9px 12px", fontSize: 11 }}>{a.rawValue}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
        <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding: "8px 12px", fontSize: 11 }}>
          {data?.length ?? 0} records
        </div>
      </div>
    </div>
  )
}
