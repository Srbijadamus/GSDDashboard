import { useState } from "react"
import { useTranslation } from "react-i18next"
import { MoveToGsdBacklogAction } from "../../components/MoveToGsdBacklogAction"

export default function AgentRow({ agent, onDragStart, onDragEnd, onClick, onActionSuccess, shiftDate }) {
  const { t } = useTranslation()
  const [menuOpen, setMenuOpen] = useState(false)
  const s = agent.agentStatus || (agent.al === true ? "AL" : null)
  const badges = {
    AL:          { bg: "rgb(var(--st-good-bg))",    border: "rgb(var(--st-good-solid))",    color: "rgb(var(--st-good-solid))",    label: "AL" },
    SL:          { bg: "rgb(var(--st-crit-bg))",    border: "rgb(var(--st-crit-solid))",    color: "rgb(var(--st-crit-solid))",    label: "SL" },
    OFF:         { bg: "rgb(var(--st-neutral-bg))", border: "rgb(var(--text-secondary))",   color: "rgb(var(--text-secondary))",   label: "OFF" },
    OFF_WEEKEND: { bg: "rgb(var(--st-neutral-bg))", border: "rgb(var(--text-secondary))",   color: "rgb(var(--text-secondary))",   label: "OFF" },
    PH:          { bg: "rgb(var(--st-holiday-bg))", border: "rgb(var(--st-holiday-solid))", color: "rgb(var(--st-holiday-solid))", label: "PH" },
    Training:    { bg: "rgb(var(--st-learn-bg))",   border: "rgb(var(--st-learn-solid))",   color: "rgb(var(--st-learn-solid))",   label: "Training" },
    TRAINING:    { bg: "rgb(var(--st-learn-bg))",   border: "rgb(var(--st-learn-solid))",   color: "rgb(var(--st-learn-solid))",   label: "Training" },
  }
  const badge = s ? badges[s] : null
  const showTime = !badge
  const showKebab = !agent.absent && agent.employeeId

  return (
    <div
      draggable
      onDragStart={e => { e.dataTransfer.setData("agentId", agent.id); onDragStart(agent) }}
      onDragEnd={onDragEnd}
      onClick={() => onClick(agent)}
      className="hover:bg-hovered"
      style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "6px 12px", borderRadius: 6, cursor: "grab", transition: "background .15s", position: "relative" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
        <span style={{ fontSize: 12, color: "rgb(var(--text-primary))", fontWeight: 500 }}>{agent.name}</span>
        <span style={{ fontSize: 9, padding: "1px 6px", borderRadius: 4, textTransform: "uppercase", letterSpacing: ".06em", fontWeight: 600,
          background: agent.role === "primary" ? "rgb(var(--st-info-bg))" : "rgb(var(--st-learn-bg))",
          color: agent.role === "primary" ? "rgb(var(--st-info-solid))" : "rgb(var(--st-learn-solid))",
          border: `1px solid ${agent.role === "primary" ? "rgb(var(--st-info-bd))" : "rgb(var(--st-learn-bd))"}` }}>
          {agent.role}
        </span>
      </div>
      <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
        {badge && (
          <span className="font-mono" style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", padding: "2px 6px", borderRadius: 4, background: badge.bg, border: `1px solid ${badge.border}`, color: badge.color }}>
            {badge.label}
          </span>
        )}
        {showTime && (
          <span className="font-mono" style={{ fontSize: 10, color: "rgb(var(--text-secondary))" }}>{agent.time ?? "—"}</span>
        )}
        {showKebab && (
          <button
            onClick={e => { e.stopPropagation(); setMenuOpen(o => !o) }}
            style={{ background: "none", border: "none", color: "rgb(var(--text-secondary))", fontSize: 14, cursor: "pointer", padding: "2px 4px", lineHeight: 1 }}
            title={t("wicShifts.moveToBacklog.button")}
          >
            ⋮
          </button>
        )}
      </div>
      {menuOpen && (
        <div
          onClick={e => e.stopPropagation()}
          style={{ position: "absolute", top: "100%", right: 8, zIndex: 10, background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--line-subtle))", borderRadius: 6, boxShadow: "0 4px 16px rgba(0,0,0,0.35)", minWidth: 180, overflow: "hidden" }}
        >
          <MoveToGsdBacklogAction
            employeeId={agent.employeeId}
            agentName={agent.name}
            shiftDate={shiftDate}
            onSuccess={() => { setMenuOpen(false); onActionSuccess?.() }}
          >
            {({ onClick: onMove, isPending }) => (
              <button
                onClick={e => { e.stopPropagation(); onMove() }}
                disabled={isPending}
                className="hover:bg-hovered"
                style={{ display: "block", width: "100%", textAlign: "left", background: "none", border: "none", color: "rgb(var(--st-warn-solid))", fontSize: 12, padding: "8px 12px", cursor: isPending ? "not-allowed" : "pointer", opacity: isPending ? 0.6 : 1 }}
              >
                {isPending ? t("wicShifts.moveToBacklog.moving") : t("wicShifts.moveToBacklog.button")}
              </button>
            )}
          </MoveToGsdBacklogAction>
        </div>
      )}
    </div>
  )
}
