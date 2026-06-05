export default function AgentRow({ agent, onDragStart, onDragEnd, onClick }) {
  const s = agent.agentStatus || (agent.al === true ? "AL" : null)
  const badges = {
    AL:          { bg: "rgba(249,115,22,0.15)",  border: "#f97316", color: "#f97316", label: "AL" },
    SL:          { bg: "rgba(239,68,68,0.15)",   border: "#ef4444", color: "#ef4444", label: "SL" },
    OFF:         { bg: "rgba(136,146,164,0.15)", border: "#8892a4", color: "#8892a4", label: "OFF" },
    OFF_WEEKEND: { bg: "rgba(136,146,164,0.15)", border: "#8892a4", color: "#8892a4", label: "OFF" },
    PH:          { bg: "rgba(250,204,21,0.15)",  border: "#facc15", color: "#facc15", label: "PH" },
    Training:    { bg: "rgba(192,132,252,0.15)", border: "#c084fc", color: "#c084fc", label: "Training" },
    TRAINING:    { bg: "rgba(192,132,252,0.15)", border: "#c084fc", color: "#c084fc", label: "Training" },
  }
  const badge = s ? badges[s] : null
  const showTime = !badge

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
      <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
        {badge && (
          <span style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", padding: "2px 6px", borderRadius: 4, background: badge.bg, border: `1px solid ${badge.border}`, color: badge.color, fontFamily: "IBM Plex Mono" }}>
            {badge.label}
          </span>
        )}
        {showTime && (
          <span style={{ fontSize: 10, color: "#8892a4", fontFamily: "IBM Plex Mono" }}>{agent.time ?? "—"}</span>
        )}
      </div>
    </div>
  )
}

