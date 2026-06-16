import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"

export default function ALBalance() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [search, setSearch] = useState("")
  const [editId, setEditId] = useState<string | null>(null)
  const [editVal, setEditVal] = useState("")
  const [error, setError] = useState("")

  const { data, isLoading } = useQuery({
    queryKey: ["albalance"],
    queryFn: api.alBalance.get
  })

  const filtered = data?.filter((a: any) =>
    !search ||
    a.employeeName?.toLowerCase().includes(search.toLowerCase()) ||
    a.employeeId?.toString().includes(search)
  ) ?? []

  const critical = filtered.filter((a: any) => a.remainingAL <= 5).length
  const warning  = filtered.filter((a: any) => a.remainingAL > 5 && a.remainingAL <= 10).length

  const handleEditSave = async (employeeId: string) => {
    const val = parseInt(editVal)
    if (isNaN(val) || val < 0 || val > 28) {
      setError("Value must be between 0 and 28")
      return
    }
    try {
      await apiFetch(`/api/employees/${employeeId}/albalance`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ alUsed: val })
      } as any)
      qc.invalidateQueries({ queryKey: ["albalance"] })
      setEditId(null)
      setError("")
    } catch {
      setError("Failed to update AL balance")
    }
  }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.alBalance")}</h1>

      <div style={{ display:"grid", gridTemplateColumns:"repeat(3, 1fr)", gap:12 }}>
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"16px 20px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>Total Employees</div>
          <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--text)" }}>{filtered.length}</div>
        </div>
        <div style={{ background:"var(--card)", border:"1px solid rgba(255,124,59,.2)", borderRadius:8, padding:"16px 20px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>⚠ Low Balance (≤10)</div>
          <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--warn)" }}>{warning}</div>
        </div>
        <div style={{ background:"var(--card)", border:"1px solid rgba(255,59,92,.2)", borderRadius:8, padding:"16px 20px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>🔴 Critical (≤5)</div>
          <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:"var(--danger)" }}>{critical}</div>
        </div>
      </div>

      {error && (
        <div style={{ background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.3)",
          borderRadius:6, padding:"8px 14px", fontSize:12, color:"var(--danger)" }}>
          ❌ {error}
        </div>
      )}

      <input placeholder="Search employee..." value={search} onChange={e => setSearch(e.target.value)}
        style={{ background:"var(--card)", border:"1px solid var(--border)", color:"var(--text)",
          padding:"7px 12px", borderRadius:6, fontSize:12, outline:"none", maxWidth:300 }} />

      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
        <div style={{ overflowX:"auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr style={{ background:"var(--card2)" }}>
                {["ID","Name","Eligible","Taken (click to edit)","Remaining","SL Days","Progress"].map(h => (
                  <th key={h} style={{ padding:"10px 12px", textAlign:"left", fontSize:10,
                    fontWeight:500, textTransform:"uppercase", letterSpacing:".07em",
                    color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && Array.from({length: 5}).map((_, i) => (
                <tr key={`sk-${i}`} style={{ borderBottom: "1px solid var(--border)" }}>
                  {Array.from({length: 7}).map((_, j) => (
                    <td key={j} style={{ padding: "10px 12px" }}><div className="skeleton" style={{ height: 11 }} /></td>
                  ))}
                </tr>
              ))}
              {filtered.length === 0 && !isLoading && (
                <tr><td colSpan={7} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>
                  No AL balance data yet
                </td></tr>
              )}
              {filtered.map((a: any) => {
                const pct = a.eligibleDays > 0 ? Math.round((a.plannedTakenAL / a.eligibleDays) * 100) : 0
                const isCritical = a.remainingAL <= 5
                const isWarning  = a.remainingAL > 5 && a.remainingAL <= 10
                const barColor   = isCritical ? "var(--danger)" : isWarning ? "var(--warn)" : "var(--accent)"
                const remColor   = isCritical ? "var(--danger)" : isWarning ? "var(--warn)" : "var(--green)"
                const isEditing  = editId === a.employeeId

                return (
                  <tr key={a.id}
                    style={{ borderBottom:"1px solid var(--border)",
                      background: isCritical ? "rgba(255,59,92,.04)" : "transparent" }}
                    onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                    onMouseLeave={ev => (ev.currentTarget.style.background = isCritical ? "rgba(255,59,92,.04)" : "transparent")}>
                    <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{a.employeeId}</td>
                    <td style={{ padding:"9px 12px", fontWeight:500 }}>
                      {a.employeeName}
                      {isCritical && <span style={{ marginLeft:6, fontSize:10, color:"var(--danger)" }}>🔴</span>}
                    </td>
                    <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", textAlign:"center" }}>{a.eligibleDays}</td>

                    {/* INLINE EDIT CELL */}
                    <td style={{ padding:"9px 12px", textAlign:"center" }}>
                      {isEditing ? (
                        <div style={{ display:"flex", alignItems:"center", gap:4, justifyContent:"center" }}>
                          <input
                            value={editVal}
                            onChange={e => setEditVal(e.target.value)}
                            onKeyDown={e => { if (e.key === "Enter") handleEditSave(a.employeeId); if (e.key === "Escape") setEditId(null) }}
                            autoFocus
                            style={{ width:44, background:"var(--card2)", border:"1px solid var(--accent)",
                              color:"var(--text)", padding:"3px 6px", borderRadius:4,
                              fontSize:12, fontFamily:"IBM Plex Mono", outline:"none", textAlign:"center" }}
                          />
                          <button onClick={() => handleEditSave(a.employeeId)}
                            style={{ background:"var(--accent)", border:"none", color:"#fff",
                              padding:"3px 7px", borderRadius:4, fontSize:10, cursor:"pointer" }}>✓</button>
                          <button onClick={() => { setEditId(null); setError("") }}
                            style={{ background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text3)",
                              padding:"3px 7px", borderRadius:4, fontSize:10, cursor:"pointer" }}>✕</button>
                        </div>
                      ) : (
                        <span
                          onClick={() => { setEditId(a.employeeId); setEditVal(a.plannedTakenAL.toString()); setError("") }}
                          title="Click to edit"
                          style={{ fontFamily:"IBM Plex Mono", color:"var(--accent)", cursor:"pointer",
                            padding:"2px 8px", borderRadius:4, border:"1px solid transparent",
                            transition:"border .15s" }}
                          onMouseEnter={e => (e.currentTarget.style.borderColor = "var(--accent)")}
                          onMouseLeave={e => (e.currentTarget.style.borderColor = "transparent")}>
                          {a.plannedTakenAL}
                        </span>
                      )}
                    </td>

                    <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontWeight:600,
                      color:remColor, textAlign:"center" }}>{a.remainingAL}</td>
                    <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono",
                      color:"var(--warn)", textAlign:"center" }}>{a.countSL}</td>
                    <td style={{ padding:"9px 12px" }}>
                      <div style={{ display:"flex", alignItems:"center", gap:6 }}>
                        <div style={{ width:80, height:5, background:"var(--border)", borderRadius:3, overflow:"hidden" }}>
                          <div style={{ width:`${Math.min(pct,100)}%`, height:"100%",
                            background:barColor, borderRadius:3, transition:"width .3s" }} />
                        </div>
                        <span style={{ fontSize:10, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>{pct}%</span>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)",
          fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>
          {filtered.length} records · click Taken value to edit
        </div>
      </div>
    </div>
  )
}
