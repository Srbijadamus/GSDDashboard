import { useState } from "react"
import AgentRow from "./AgentRow"
import { NppBadge } from "../../components/NppBadge"

const borderColor = { covered: "rgb(var(--st-good-solid))", partial: "rgb(var(--st-warn-solid))", uncovered: "rgb(var(--st-crit-solid))" }
const statusBg    = { covered: "rgb(var(--st-good-bg))",    partial: "rgb(var(--st-warn-bg))",    uncovered: "rgb(var(--st-crit-bg))" }
const statusText  = { covered: "rgb(var(--st-good-solid))", partial: "rgb(var(--st-warn-solid))", uncovered: "rgb(var(--st-crit-solid))" }
const statusDot   = { covered: "rgb(var(--st-good-solid))", partial: "rgb(var(--st-warn-solid))", uncovered: "rgb(var(--st-crit-solid))" }
const statusBd    = { covered: "rgb(var(--st-good-bd))",    partial: "rgb(var(--st-warn-bd))",    uncovered: "rgb(var(--st-crit-bd))" }

export default function LocationCard({ location, onAgentClick, onDrop, dragAgent, setDragAgent, onActionSuccess, shiftDate }) {
  const [isDragOver, setIsDragOver] = useState(false)
  const effectiveCoverage = location.agents.filter(a => !a.absent).length
  const rawStatus = location.status || "uncovered"
  const s = (effectiveCoverage === 0 && location.agents.length > 0 && rawStatus !== "uncovered")
    ? "uncovered"
    : rawStatus

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
      className="hover:shadow-[0_4px_20px_rgba(0,0,0,0.35)]"
      style={{
        background: "rgb(var(--surface-sunken))",
        border: isDragOver ? "1px solid rgb(var(--st-info-solid))" : "1px solid rgb(var(--line-subtle))",
        borderTop: `3px solid ${borderColor[s]}`,
        borderRadius: 8,
        overflow: "hidden",
        boxShadow: isDragOver ? "0 0 0 2px rgb(var(--st-info-solid) / 0.27)" : undefined,
        transition: "box-shadow .15s, border .15s"
      }}>

      <div style={{ padding: "10px 12px 8px", borderBottom: "1px solid rgb(var(--line-subtle))" }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
          <div>
            <div style={{ fontSize: 13, fontWeight: 700, color: "rgb(var(--text-primary))", display: "flex", alignItems: "center", gap: 6 }}>
              {location.name}
              {location.isNpp && <NppBadge />}
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: 6, marginTop: 4 }}>
              <span style={{ fontSize: 10, color: "rgb(var(--text-secondary))" }}>{location.city}</span>
              <span style={{ fontSize: 9, background: "rgb(var(--surface-sunken))", color: "rgb(var(--text-primary))", padding: "1px 5px", borderRadius: 4 }}>{location.country}</span>
              <span className="font-mono" style={{ fontSize: 10, color: "rgb(var(--text-secondary))" }}>{location.required}</span>
            </div>
          </div>
          <span style={{ fontSize: 10, display: "flex", alignItems: "center", gap: 4, background: statusBg[s], color: statusText[s], border: `1px solid ${statusBd[s]}`, padding: "3px 8px", borderRadius: 20, fontWeight: 600, whiteSpace: "nowrap" }}>
            <span style={{ width: 5, height: 5, borderRadius: "50%", background: statusDot[s], display: "inline-block" }} />
            {s.toUpperCase()}
          </span>
        </div>
      </div>

      <div style={{ padding: "4px 0 6px" }}>
        {location.agents.length === 0 ? (
          <div style={{ padding: "6px 12px 4px" }}>
            {(location.mainAgents ?? []).length === 0 && (location.backupAgents ?? []).length === 0 ? (
              <div style={{ padding: "6px 0", fontSize: 11, color: "rgb(var(--text-secondary))", textAlign: "center", fontStyle: "italic" }}>No agents assigned</div>
            ) : null}
            {(location.mainAgents ?? []).map(name => (
              <div key={name} style={{ display:"flex", alignItems:"center", gap:6, padding:"5px 0", borderBottom:"1px solid rgb(var(--line-subtle))" }}>
                <span style={{ fontSize:9, background:"rgb(var(--st-info-bg))", color:"rgb(var(--st-info-solid))", border:"1px solid rgb(var(--st-info-bd))", borderRadius:3, padding:"1px 5px", fontWeight:600 }}>PRIMARY</span>
                <span style={{ fontSize:11, color:"rgb(var(--text-primary))", fontWeight:500 }}>{name}</span>
                <span style={{ fontSize:9, color:"rgb(var(--st-crit-solid))", marginLeft:"auto", fontStyle:"italic" }}>not on shift</span>
              </div>
            ))}
            {(location.backupAgents ?? []).map(name => (
              <div key={name} style={{ display:"flex", alignItems:"center", gap:6, padding:"5px 0", borderBottom:"1px solid rgb(var(--line-subtle))" }}>
                <span style={{ fontSize:9, background:"rgb(var(--st-learn-bg))", color:"rgb(var(--st-learn-solid))", border:"1px solid rgb(var(--st-learn-bd))", borderRadius:3, padding:"1px 5px", fontWeight:600 }}>BACKUP</span>
                <span style={{ fontSize:11, color:"rgb(var(--text-secondary))" }}>{name}</span>
              </div>
            ))}
          </div>
        ) : (
          location.agents.map(agent => (
            <div key={agent.id} style={agent.absent ? { opacity: 0.45 } : undefined}>
              <AgentRow
                agent={agent}
                onDragStart={a => setDragAgent(a)}
                onDragEnd={() => setDragAgent(null)}
                onClick={onAgentClick}
                onActionSuccess={onActionSuccess}
                shiftDate={shiftDate}
              />
            </div>
          ))
        )}
      </div>
    </div>
  )
}
