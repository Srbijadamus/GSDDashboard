import { useState } from "react"
import AgentRow from "./AgentRow"

const borderColor = { covered: "#22c55e", partial: "#f97316", uncovered: "#ef4444" }
const statusBg    = { covered: "rgba(34,197,94,.12)",  partial: "rgba(249,115,22,.12)",  uncovered: "rgba(239,68,68,.12)" }
const statusText  = { covered: "#22c55e", partial: "#f97316", uncovered: "#ef4444" }
const statusDot   = { covered: "#22c55e", partial: "#f97316", uncovered: "#ef4444" }

export default function LocationCard({ location, onAgentClick, onDrop, dragAgent, setDragAgent }) {
  const [isDragOver, setIsDragOver] = useState(false)
  const s = location.status || "uncovered"

  return (
    <div
      onDragOver={e => { e.preventDefault(); setIsDragOver(true) }}
      onDragLeave={() => setIsDragOver(false)}
      onDrop={e => {
        e.preventDefault()
        setIsDragOver(false)
        const agentId = e.dataTransfer.getData("agentId")
        if (agentId) onDrop(agentId, location.name)
      }}
      style={{
        background: "#181e2e",
        border: isDragOver ? "1px solid #60a5fa" : "1px solid rgba(255,255,255,0.07)",
        borderTop: `3px solid ${borderColor[s]}`,
        borderRadius: 8,
        overflow: "hidden",
        boxShadow: isDragOver ? "0 0 0 2px #60a5fa44" : "none",
        transition: "box-shadow .15s, border .15s"
      }}
      onMouseEnter={e => { if (!isDragOver) e.currentTarget.style.boxShadow = "0 4px 20px rgba(0,0,0,0.35)" }}
      onMouseLeave={e => { if (!isDragOver) e.currentTarget.style.boxShadow = "none" }}>

      {/* CARD HEADER */}
      <div style={{ padding: "10px 12px 8px", borderBottom: "1px solid rgba(255,255,255,0.06)" }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
          <div>
            <div style={{ fontSize: 13, fontWeight: 700, color: "#e2e8f0" }}>{location.name}</div>
            <div style={{ display: "flex", alignItems: "center", gap: 6, marginTop: 4 }}>
              <span style={{ fontSize: 10, color: "#8892a4" }}>{location.city}</span>
              <span style={{ fontSize: 9, background: "rgba(255,255,255,.06)", color: "#e2e8f0", padding: "1px 5px", borderRadius: 4 }}>{location.country}</span>
              <span style={{ fontSize: 10, color: "#8892a4", fontFamily: "IBM Plex Mono" }}>{location.required}</span>
            </div>
          </div>
          <span style={{ fontSize: 10, display: "flex", alignItems: "center", gap: 4, background: statusBg[s], color: statusText[s], border: `1px solid ${statusText[s]}33`, padding: "3px 8px", borderRadius: 20, fontWeight: 600, whiteSpace: "nowrap" }}>
            <span style={{ width: 5, height: 5, borderRadius: "50%", background: statusDot[s], display: "inline-block" }} />
            {s.toUpperCase()}
          </span>
        </div>
      </div>

      {/* AGENTS */}
      <div style={{ padding: "4px 0 6px" }}>
        {location.agents.length === 0 ? (
          <div style={{ padding: "10px 12px", fontSize: 11, color: "#8892a4", textAlign: "center", fontStyle: "italic" }}>
            No agents assigned
          </div>
        ) : (
          location.agents.map(agent => (
            <AgentRow
              key={agent.id}
              agent={agent}
              onDragStart={a => setDragAgent(a)}
              onDragEnd={() => setDragAgent(null)}
              onClick={onAgentClick}
            />
          ))
        )}
      </div>
    </div>
  )
}
