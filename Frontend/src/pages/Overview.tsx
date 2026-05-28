import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { useState } from "react"
import { api } from "../api/client"

function StatCard({ label, value, color }: { label: string; value: any; color?: string }) {
  return (
    <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"16px 20px" }}>
      <div style={{ fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>{label}</div>
      <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:color ?? "var(--text)" }}>{value ?? "..."}</div>
    </div>
  )
}

function WicCard({ card }: { card: any }) {
  const covered = card.coverageStatus === "COVERED"
  const partial = card.coverageStatus === "PARTIAL"
  const closed  = card.coverageStatus === "CLOSED"
  const statusColor = covered ? "var(--green)" : partial ? "var(--warn)" : closed ? "var(--text3)" : "var(--danger)"
  const statusLabel = covered ? "COVERED" : partial ? "PARTIAL" : closed ? "CLOSED" : "UNCOVERED"
  return (
    <div style={{ background:"var(--card)", border:`1px solid ${covered ? "rgba(34,208,122,.2)" : partial ? "rgba(255,124,59,.2)" : "var(--border)"}`, borderRadius:8, padding:"12px 14px" }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"flex-start", marginBottom:8 }}>
        <div>
          <div style={{ fontWeight:600, fontSize:13, color:"var(--text)" }}>{card.displayName}</div>
          <div style={{ fontSize:11, color:"var(--text3)", marginTop:2 }}>{card.city} - {card.country}</div>
        </div>
        <span style={{ fontSize:10, fontWeight:600, color:statusColor, background:`${statusColor}18`, padding:"2px 8px", borderRadius:4, fontFamily:"IBM Plex Mono", whiteSpace:"nowrap" }}>{statusLabel}</span>
      </div>
      {card.todaySchedule?.rawSchedule && !closed && (
        <div style={{ fontSize:11, color:"var(--text3)", marginBottom:6 }}>{card.todaySchedule.rawSchedule}</div>
      )}
      {card.assignedAgents?.length > 0 && (
        <div style={{ borderTop:"1px solid var(--border)", paddingTop:8, marginTop:4 }}>
          {card.assignedAgents.map((a: any) => (
            <div key={a.employeeId} style={{ display:"flex", justifyContent:"space-between", fontSize:11, color:"var(--text2)", marginBottom:3 }}>
              <span>{a.name}</span>
              <span style={{ fontFamily:"IBM Plex Mono", color:a.coverageMatch === "FULL" ? "var(--green)" : "var(--warn)" }}>{a.shiftStart} - {a.shiftEnd}</span>
            </div>
          ))}
        </div>
      )}
      {card.mainAgents?.length > 0 && card.assignedAgents?.length === 0 && !closed && (
        <div style={{ fontSize:11, color:"var(--text3)", fontStyle:"italic" }}>Main: {card.mainAgents.join(", ")}</div>
      )}
    </div>
  )
}

function TLCard({ tl }: { tl: any }) {
  return (
    <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"12px 14px" }}>
      <div style={{ fontWeight:600, fontSize:12, marginBottom:10, color:"var(--text)", whiteSpace:"nowrap", overflow:"hidden", textOverflow:"ellipsis" }}>{tl.teamLeadName}</div>
      <div style={{ display:"grid", gridTemplateColumns:"1fr 1fr", gap:"4px 8px", fontSize:11 }}>
        <span style={{ color:"var(--text3)" }}>Total</span><span style={{ fontFamily:"IBM Plex Mono", textAlign:"right" }}>{tl.totalAgents}</span>
        <span style={{ color:"var(--text3)" }}>Working</span><span style={{ fontFamily:"IBM Plex Mono", color:"var(--green)", textAlign:"right" }}>{tl.working}</span>
        <span style={{ color:"var(--text3)" }}>On AL</span><span style={{ fontFamily:"IBM Plex Mono", color:"var(--accent)", textAlign:"right" }}>{tl.onAL}</span>
        <span style={{ color:"var(--text3)" }}>On SL</span><span style={{ fontFamily:"IBM Plex Mono", color:"var(--warn)", textAlign:"right" }}>{tl.onSL}</span>
        <span style={{ color:"var(--text3)" }}>WIC</span><span style={{ fontFamily:"IBM Plex Mono", color:"#7eb8ff", textAlign:"right" }}>{tl.wicAssigned}</span>
      </div>
    </div>
  )
}

export default function Overview() {
  const { t } = useTranslation()
  const today = new Date().toISOString().split("T")[0]
  const [showClosed, setShowClosed] = useState(false)

  const { data: summary } = useQuery({ queryKey:["summary", today], queryFn:() => api.dashboard.summary(today) })
  const { data: tls }     = useQuery({ queryKey:["tls", today],     queryFn:() => api.dashboard.teamleadSummary(today) })
  const { data: wicCards }= useQuery({ queryKey:["wic-cards", today], queryFn:() => api.wic.cards(today) })

  const occupied  = wicCards?.filter((c: any) => c.coverageStatus === "COVERED") ?? []
  const partial   = wicCards?.filter((c: any) => c.coverageStatus === "PARTIAL") ?? []
  const uncovered = wicCards?.filter((c: any) => c.coverageStatus === "UNCOVERED") ?? []
  const closed    = wicCards?.filter((c: any) => c.coverageStatus === "CLOSED") ?? []

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:20 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.overview")}</h1>
        <span style={{ fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{today}</span>
      </div>

      <div style={{ display:"grid", gridTemplateColumns:"repeat(4, 1fr)", gap:12 }}>
        <StatCard label="Voice Working"  value={summary?.workingVoice} color="var(--accent)" />
        <StatCard label="Chat + SSP"     value={(summary?.workingChat ?? 0) + (summary?.workingSSP ?? 0)} color="var(--accent2)" />
        <StatCard label="On AL"          value={summary?.onAL}         color="var(--accent)" />
        <StatCard label="On SL"          value={summary?.onSL}         color="var(--warn)" />
        <StatCard label="Training"       value={summary?.onTraining}   color="#a78bfa" />
        <StatCard label="WIC Duty"       value={summary?.onWicDuty}    color="#7eb8ff" />
        <StatCard label="Total Active"   value={summary?.totalActive}  color="var(--green)" />
        <StatCard label="WIC Unoccupied" value={summary?.wicUnoccupiedCount} color="var(--danger)" />
      </div>

      <div>
        <h2 style={{ fontSize:13, fontWeight:600, color:"var(--text2)", marginBottom:10, textTransform:"uppercase", letterSpacing:".07em" }}>Team Lead Summary</h2>
        <div style={{ display:"grid", gridTemplateColumns:"repeat(6, 1fr)", gap:10 }}>
          {tls?.map((tl: any) => <TLCard key={tl.teamLeadName} tl={tl} />)}
        </div>
      </div>

      {uncovered.length > 0 && (
        <div>
          <h2 style={{ fontSize:13, fontWeight:600, color:"var(--danger)", marginBottom:10, textTransform:"uppercase", letterSpacing:".07em" }}>Uncovered ({uncovered.length})</h2>
          <div style={{ display:"grid", gridTemplateColumns:"repeat(4, 1fr)", gap:10 }}>
            {uncovered.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
          </div>
        </div>
      )}

      {partial.length > 0 && (
        <div>
          <h2 style={{ fontSize:13, fontWeight:600, color:"var(--warn)", marginBottom:10, textTransform:"uppercase", letterSpacing:".07em" }}>Partial Coverage ({partial.length})</h2>
          <div style={{ display:"grid", gridTemplateColumns:"repeat(4, 1fr)", gap:10 }}>
            {partial.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
          </div>
        </div>
      )}

      {occupied.length > 0 && (
        <div>
          <h2 style={{ fontSize:13, fontWeight:600, color:"var(--green)", marginBottom:10, textTransform:"uppercase", letterSpacing:".07em" }}>Covered ({occupied.length})</h2>
          <div style={{ display:"grid", gridTemplateColumns:"repeat(4, 1fr)", gap:10 }}>
            {occupied.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
          </div>
        </div>
      )}

      <div>
        <button onClick={() => setShowClosed(!showClosed)} style={{ background:"var(--card)", border:"1px solid var(--border)", color:"var(--text3)", padding:"6px 14px", borderRadius:6, fontSize:11, cursor:"pointer", fontFamily:"IBM Plex Mono" }}>
          {showClosed ? "Hide" : "Show"} Closed Today ({closed.length})
        </button>
        {showClosed && (
          <div style={{ display:"grid", gridTemplateColumns:"repeat(5, 1fr)", gap:10, marginTop:10 }}>
            {closed.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
          </div>
        )}
      </div>
    </div>
  )
}