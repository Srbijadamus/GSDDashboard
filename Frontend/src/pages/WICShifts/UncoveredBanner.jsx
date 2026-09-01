export default function UncoveredBanner({ locations }) {
  if (!locations || locations.length === 0) return null
  return (
    <div style={{ background: "rgb(var(--st-crit-bg))", border: "1px solid rgb(var(--st-crit-bd))", borderRadius: 8, padding: "10px 16px", marginBottom: 4, display: "flex", flexWrap: "wrap", gap: 8, alignItems: "center" }}>
      <span style={{ fontSize: 11, color: "rgb(var(--st-crit-solid))", fontWeight: 700, marginRight: 4 }}>UNCOVERED</span>
      {locations.map(loc => (
        <span key={loc.id} className="font-mono" style={{ fontSize: 11, background: "rgb(var(--st-crit-bg))", border: "1px solid rgb(var(--st-crit-bd))", color: "rgb(var(--st-crit-solid))", padding: "3px 10px", borderRadius: 20, whiteSpace: "nowrap" }}>
          {loc.name} · {loc.country} · {loc.required} · 0 agents
        </span>
      ))}
    </div>
  )
}
