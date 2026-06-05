export default function AgentRow({ agent, onDragStart, onDragEnd, onClick }) {
  return (
    <div
      draggable
      onDragStart={e => { e.dataTransfer.setData("agentId", agent.id); onDragStart(agent) }}
      onDragEnd={onDragEnd}
      onClick={() => onClick(agent)}
      style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "6px 12px", borderRadius: 6, cursor: "grab", transition: "background .15s" }}
      onMouseEnter={e => e.currentTarget.style.background = "rgba(255,255,255,0.025)"}
      onMouseLeave={e => e.currentTarget.style.background = "transparent"}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <span style={{ fontSize: 12, color: "#e2e8f0", fontWeight: 500 }}>{agent.name}</span>
        <span style={{ fontSize: 9, padding: "1px 6px", borderRadius: 4, textTransform: "uppercase", letterSpacing: ".06em", fontWeight: 600,
          background: agent.role === "primary" ? "rgba(96,165,250,.12)" : "rgba(192,132,252,.12)",
          color: agent.role === "primary" ? "#60a5fa" : "#c084fc",
          border: `1px solid ${agent.role === "primary" ? "rgba(96,165,250,.3)" : "rgba(192,132,252,.3)"}` }}>
          {agent.role}
        </span>
      </div>
      {agent.al ? (
        <span style={{ fontSize: 10, background: "rgba(249,115,22,.15)", color: "#f97316", border: "1px solid rgba(249,115,22,.3)", padding: "1px 7px", borderRadius: 4, fontFamily: "IBM Plex Mono" }}>AL</span>
      ) : (
        <span style={{ fontSize: 10, color: "#8892a4", fontFamily: "IBM Plex Mono" }}>{agent.time ?? "—"}</span>
      )}
    </div>
  )
}
