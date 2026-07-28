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
      <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>{t("nav.wicLocations")}</h1>

      <div style={{ display: "grid", gridTemplateColumns: "repeat(3, 1fr)", gap: 12 }}>
        {[
          { label: "Total",     value: data?.length ?? 0,                                color: "var(--accent)" },
          { label: "DE",        value: data?.filter((l: any) => l.country === "DE").length ?? 0, color: "var(--text)" },
          { label: "NL",        value: data?.filter((l: any) => l.country === "NL").length ?? 0, color: "var(--warn)" },
        ].map(s => (
          <div key={s.label} style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px 20px" }}>
            <div style={{ fontSize: 10, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>{s.label}</div>
            <div style={{ fontSize: 28, fontWeight: 600, fontFamily: "IBM Plex Mono", color: s.color }}>{s.value}</div>
          </div>
        ))}
      </div>

      <div style={{ display: "flex", gap: 10 }}>
        <input placeholder="Search location or city..." value={search} onChange={e => setSearch(e.target.value)}
          style={{ flex: 1, background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
            padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none" }} />
        <select value={country} onChange={e => setCountry(e.target.value)}
          style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text2)", padding: "7px 12px", borderRadius: 6, fontSize: 12 }}>
          <option value="">All Countries</option>
          <option value="DE">DE</option>
          <option value="NL">NL</option>
        </select>
      </div>

      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
        <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
          <thead>
            <tr style={{ background: "var(--card2)" }}>
              {["Location", "City", "Country", "Address", "Opening Schedule", "Status"].map(h => (
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
            {!isLoading && filtered.length === 0 && (
              <tr><td colSpan={6} style={{ padding: 24, textAlign: "center", color: "var(--text3)" }}>No locations found</td></tr>
            )}
            {filtered.map((l: any) => (
              <tr key={l.id} style={{ borderBottom: "1px solid var(--border)" }}
                onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                onMouseLeave={ev => (ev.currentTarget.style.background = "transparent")}>
                <td style={{ padding: "9px 12px", fontWeight: 500, display: "flex", alignItems: "center", gap: 5 }}>
                  {l.displayName}
                  {l.isNpp && <NppBadge />}
                </td>
                <td style={{ padding: "9px 12px", color: "var(--text2)" }}>{l.city}</td>
                <td style={{ padding: "9px 12px" }}>
                  <span style={{
                    background: l.country === "NL" ? "rgba(255,124,59,.15)" : "rgba(59,126,255,.15)",
                    color: l.country === "NL" ? "var(--warn)" : "var(--accent)",
                    padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono"
                  }}>{l.country}</span>
                </td>
                <td style={{ padding: "9px 12px", color: "var(--text3)", fontSize: 11 }}>{l.fullAddress}</td>
                <td style={{ padding: "9px 12px", color: "var(--text2)", fontSize: 11 }}>{l.openingSchedule || "—"}</td>
                <td style={{ padding: "9px 12px" }}>
                  <span style={{
                    background: "rgba(34,208,122,.15)", color: "var(--green)",
                    padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono"
                  }}>Active</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
          {filtered.length} locations
        </div>
      </div>
    </div>
  )
}
