import { useQuery } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api } from "../api/client"
import { NppBadge } from "../components/NppBadge"

export default function WicLocations() {
  const { t } = useTranslation()
  const [country, setCountry] = useState("")
  const [search, setSearch] = useState("")

  const { data, isLoading } = useQuery({
    queryKey: ["wic-locations"],
    queryFn: api.wic.locations
  })

  const filtered = data?.filter((l: any) =>
    (!country || l.country === country) &&
    (!search || l.displayName?.toLowerCase().includes(search.toLowerCase()) ||
     l.city?.toLowerCase().includes(search.toLowerCase()))
  ) ?? []

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      <h1 className="text-ink" style={{ fontSize: 22, fontWeight: 600 }}>{t("nav.wicLocations")}</h1>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 12 }}>
        {[
          { label: "Total", value: data?.length ?? 0,                                         color: "rgb(var(--st-info-fg))" },
          { label: "DE",    value: data?.filter((l: any) => l.country === "DE").length ?? 0,  color: "rgb(var(--text-primary))" },
          { label: "NL",    value: data?.filter((l: any) => l.country === "NL").length ?? 0,  color: "rgb(var(--st-warn-fg))" },
        ].map(s => (
          <div key={s.label} className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "16px 20px" }}>
            <div className="text-ink-soft" style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 6 }}>{s.label}</div>
            <div className="font-mono" style={{ fontSize: 28, fontWeight: 600, color: s.color }}>{s.value}</div>
          </div>
        ))}
      </div>

      <div style={{ display: "flex", gap: 10 }}>
        <input placeholder="Search location or city..." value={search} onChange={e => setSearch(e.target.value)}
          className="bg-raised border border-line-subtle text-ink"
          style={{ flex: 1, padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none" }} />
        <select value={country} onChange={e => setCountry(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted"
          style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
          <option value="">All Countries</option>
          <option value="DE">DE</option>
          <option value="NL">NL</option>
        </select>
      </div>

      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 8, overflow: "hidden" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr className="bg-sunken">
              {["Location", "City", "Country", "Address", "Opening Schedule", "Status"].map(h => (
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
            {!isLoading && filtered.length === 0 && (
              <tr><td colSpan={6} className="text-ink-soft" style={{ padding: 24, textAlign: "center" }}>No locations found</td></tr>
            )}
            {filtered.map((l: any) => (
              <tr key={l.id} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                <td style={{ padding: "9px 12px", fontWeight: 500, display: "flex", alignItems: "center", gap: 5 }}>
                  {l.displayName}
                  {l.isNpp && <NppBadge />}
                </td>
                <td className="text-ink-muted" style={{ padding: "9px 12px" }}>{l.city}</td>
                <td style={{ padding: "9px 12px" }}>
                  <span className={`font-mono ${l.country === "NL" ? "bg-warn-bg text-warn-fg" : "bg-info-bg text-info-fg"}`}
                    style={{ padding: "2px 7px", borderRadius: 4, fontSize: 10 }}>{l.country}</span>
                </td>
                <td className="text-ink-soft" style={{ padding: "9px 12px", fontSize: 11 }}>{l.fullAddress}</td>
                <td className="text-ink-muted" style={{ padding: "9px 12px", fontSize: 11 }}>{l.openingSchedule || "—"}</td>
                <td style={{ padding: "9px 12px" }}>
                  <span className="font-mono bg-good-bg text-good-fg"
                    style={{ padding: "2px 7px", borderRadius: 4, fontSize: 10 }}>Active</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding: "8px 12px", fontSize: 11 }}>
          {filtered.length} locations
        </div>
      </div>
    </div>
  )
}
