import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { Trash2 } from "lucide-react"

export default function Vacations() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [sheet, setSheet] = useState("")
  const [deleteId, setDeleteId] = useState<number | null>(null)
  const [error, setError] = useState("")

  const { data: active }   = useQuery({ queryKey:["vac-active"],   queryFn: () => api.vacations.current() })
  const { data: upcoming } = useQuery({ queryKey:["vac-upcoming"], queryFn: () => api.vacations.upcoming(7) })
  const { data, isLoading } = useQuery({
    queryKey: ["vacations", sheet],
    queryFn: () => api.vacations.get(`year=2026${sheet ? "&sheet=" + sheet : ""}`)
  })

  const handleDelete = async () => {
    if (!deleteId) return
    try {
      await apiFetch(`/api/vacations/${deleteId}`, { method: "DELETE" } as any)
      qc.invalidateQueries({ queryKey:["vacations"] })
      qc.invalidateQueries({ queryKey:["vac-active"] })
      qc.invalidateQueries({ queryKey:["vac-upcoming"] })
      qc.invalidateQueries({ queryKey:["albalance"] })
      setDeleteId(null)
      setError("")
    } catch {
      setError("Failed to delete vacation")
      setDeleteId(null)
    }
  }

  const deleteVac = data?.find((v: any) => v.id === deleteId)

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.vacations")}</h1>
        <DownloadButtons onToday={api.vacations.downloadToday} on7Days={api.vacations.download7} on30Days={api.vacations.download30} />
      </div>

      {error && (
        <div style={{ background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.3)",
          borderRadius:6, padding:"8px 14px", fontSize:12, color:"var(--danger)" }}>
          ❌ {error}
        </div>
      )}

      <div style={{ display:"grid", gridTemplateColumns:"repeat(3, 1fr)", gap:12 }}>
        {[
          { label:"On AL Today",     value:(active as any[])?.length ?? 0,   color:"var(--green)" },
          { label:"Upcoming 7 Days", value:upcoming?.length ?? 0, color:"var(--accent)" },
          { label:"Total 2026",      value:data?.length ?? 0,     color:"var(--text)" },
        ].map(s => (
          <div key={s.label} style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"16px 20px" }}>
            <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>{s.label}</div>
            <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:s.color }}>{s.value}</div>
          </div>
        ))}
      </div>

      {upcoming && upcoming.length > 0 && (
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"14px 16px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", marginBottom:10 }}>
            Starting next 7 days
          </div>
          {upcoming.map((v: any) => (
            <div key={v.id} style={{ display:"flex", justifyContent:"space-between", alignItems:"center",
              padding:"5px 0", borderBottom:"1px solid var(--border)", fontSize:12 }}>
              <span style={{ fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{v.employeeId}</span>
              <span style={{ color:"var(--text2)" }}>{v.firstName} {v.lastName}</span>
              <span style={{ fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--accent)" }}>{v.firstDay} → {v.lastDay}</span>
              <span style={{ fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{v.workDaysNet}d</span>
            </div>
          ))}
        </div>
      )}

      <div style={{ display:"flex", gap:10 }}>
        <select value={sheet} onChange={e => setSheet(e.target.value)}
          style={{ background:"var(--card)", border:"1px solid var(--border)", color:"var(--text2)",
            padding:"7px 12px", borderRadius:6, fontSize:12 }}>
          <option value="">All</option>
          <option value="Agents">Agents</option>
          <option value="Overhead">Overhead</option>
        </select>
      </div>

      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
        <div style={{ overflowX:"auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr style={{ background:"var(--card2)" }}>
                {["ID","Last Name","First Name","First Day","Last Day","Work Days","Type","Comments",""].map(h => (
                  <th key={h} style={{ padding:"10px 12px", textAlign:"left", fontSize:10,
                    fontWeight:500, textTransform:"uppercase", letterSpacing:".07em",
                    color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && Array.from({length: 5}).map((_, i) => (
                <tr key={`sk-${i}`} style={{ borderBottom: "1px solid var(--border)" }}>
                  {Array.from({length: 9}).map((_, j) => (
                    <td key={j} style={{ padding: "10px 12px" }}><div className="skeleton" style={{ height: 11 }} /></td>
                  ))}
                </tr>
              ))}
              {data?.map((v: any) => (
                <tr key={v.id} style={{ borderBottom:"1px solid var(--border)" }}
                  onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                  onMouseLeave={ev => (ev.currentTarget.style.background = "transparent")}>
                  <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{v.employeeId}</td>
                  <td style={{ padding:"9px 12px", fontWeight:500 }}>{v.lastName}</td>
                  <td style={{ padding:"9px 12px" }}>{v.firstName}</td>
                  <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11 }}>{v.firstDay}</td>
                  <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11 }}>{v.lastDay}</td>
                  <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11, textAlign:"center" }}>{v.workDaysNet}</td>
                  <td style={{ padding:"9px 12px" }}>
                    <span style={{
                      background: v.isOverhead ? "rgba(167,139,250,.15)" : "rgba(34,208,122,.15)",
                      color: v.isOverhead ? "var(--purple)" : "var(--green)",
                      padding:"2px 7px", borderRadius:4, fontSize:10, fontFamily:"IBM Plex Mono"
                    }}>{v.isOverhead ? "Overhead" : "Agent"}</span>
                  </td>
                  <td style={{ padding:"9px 12px", color:"var(--text3)", fontSize:11 }}>{v.comments}</td>
                  <td style={{ padding:"9px 12px" }}>
                    <button onClick={() => setDeleteId(v.id)} style={{
                      background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.2)",
                      color:"var(--danger)", padding:"3px 7px", borderRadius:4,
                      fontSize:10, cursor:"pointer", display:"flex", alignItems:"center", gap:3
                    }}><Trash2 size={10} /></button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)",
          fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>
          {data?.length ?? 0} records
        </div>
      </div>

      {deleteId && deleteVac && (
        <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000,
          display:"flex", alignItems:"center", justifyContent:"center" }}>
          <div style={{ background:"var(--card)", border:"1px solid rgba(255,59,92,.3)",
            borderRadius:10, padding:24, width:400 }}>
            <h2 style={{ fontSize:15, fontWeight:600, color:"var(--danger)", marginBottom:12 }}>Delete Vacation</h2>
            <p style={{ fontSize:13, color:"var(--text2)", marginBottom:8 }}>
              Delete vacation for <strong style={{ color:"var(--text)" }}>{deleteVac.firstName} {deleteVac.lastName}</strong>?
            </p>
            <p style={{ fontSize:12, color:"var(--text3)", marginBottom:16 }}>
              {deleteVac.firstDay} → {deleteVac.lastDay} ({deleteVac.workDaysNet} days)
            </p>
            {deleteVac.workDaysNet > 0 && (
              <div style={{ background:"rgba(34,208,122,.08)", border:"1px solid rgba(34,208,122,.2)",
                borderRadius:6, padding:"8px 12px", marginBottom:14, fontSize:12, color:"var(--green)" }}>
                ✅ {deleteVac.workDaysNet} AL days will be restored to balance
              </div>
            )}
            <div style={{ display:"flex", gap:8, justifyContent:"flex-end" }}>
              <button onClick={() => setDeleteId(null)} style={{
                background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)",
                padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
              <button onClick={handleDelete} style={{
                background:"var(--danger)", border:"none", color:"#fff",
                padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer",
                display:"flex", alignItems:"center", gap:4
              }}><Trash2 size={13} /> Delete & Restore AL</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}



