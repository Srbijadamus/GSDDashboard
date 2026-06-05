export default function UncoveredBanner({ locations }) {
  if (!locations || locations.length === 0) return null
  return (
    <div style={{ background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.25)", borderRadius: 8, padding: "10px 16px", marginBottom: 4, display: "flex", flexWrap: "wrap", gap: 8, alignItems: "center" }}>
      <span style={{ fontSize: 11, color: "#ef4444", fontWeight: 700, marginRight: 4 }}>UNCOVERED</span>
      {locations.map(loc => (
        <span key={loc.id} style={{ fontSize: 11, background: "rgba(239,68,68,.12)", border: "1px solid rgba(239,68,68,.3)", color: "#ef4444", padding: "3px 10px", borderRadius: 20, fontFamily: "IBM Plex Mono", whiteSpace: "nowrap" }}>
          {loc.name} · {loc.country} · {loc.required} · 0 agents
        </span>
      ))}
    </div>
  )
}
