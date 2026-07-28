import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useMemo } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { Trash2, ChevronDown, ChevronRight, Plus, X, Check } from "lucide-react"
import { maxFutureDateStr } from "../constants"

interface EmployeeOption {
  employeeId: string
  fullName: string | null
}

const modalInputStyle: React.CSSProperties = {
  width: "100%", background: "var(--card2)", border: "1px solid var(--border)",
  color: "var(--text)", padding: "7px 10px", borderRadius: 6, fontSize: 12,
  outline: "none", fontFamily: "IBM Plex Sans", boxSizing: "border-box",
}

function AddVacationModal({ onClose, onSave }: { onClose: () => void; onSave: (data: any) => void }) {
  const today = new Date().toISOString().slice(0, 10)
  const [employeeId, setEmployeeId] = useState("")
  const [firstDay, setFirstDay] = useState(today)
  const [lastDay, setLastDay] = useState(today)
  const [comments, setComments] = useState("")
  const [error, setError] = useState("")

  const { data: employees = [] } = useQuery<EmployeeOption[]>({
    queryKey: ["employees-active"],
    queryFn: () => fetch("/api/employees/?active=true").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60 * 1000,
  })

  const handleSubmit = () => {
    if (!employeeId) { setError("Select an employee"); return }
    if (lastDay < firstDay) { setError("To date must be on or after From date"); return }
    onSave({ employeeId, firstDay, lastDay, comments: comments || null })
  }

  return (
    <div style={{
      position: "fixed", inset: 0, background: "rgba(0,0,0,.7)", zIndex: 1000,
      display: "flex", alignItems: "center", justifyContent: "center",
    }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{
        background: "var(--card)", border: "1px solid var(--border)", borderRadius: 10,
        padding: 24, width: 420,
      }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 18 }}>
          <h2 style={{ fontSize: 16, fontWeight: 600, color: "var(--text)" }}>Add Vacation (AL)</h2>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "var(--text3)", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={{ fontSize: 11, color: "var(--text3)", marginBottom: 4, display: "block" }}>Employee *</label>
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} style={modalInputStyle}>
              <option value="">-- Select employee --</option>
              {employees.map(e => (
                <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
              ))}
            </select>
          </div>
          <div style={{ display: "flex", gap: 10 }}>
            <div style={{ flex: 1 }}>
              <label style={{ fontSize: 11, color: "var(--text3)", marginBottom: 4, display: "block" }}>From *</label>
              <input type="date" value={firstDay} max={maxFutureDateStr()} onChange={e => setFirstDay(e.target.value)} style={modalInputStyle} />
            </div>
            <div style={{ flex: 1 }}>
              <label style={{ fontSize: 11, color: "var(--text3)", marginBottom: 4, display: "block" }}>To *</label>
              <input type="date" value={lastDay} max={maxFutureDateStr()} onChange={e => setLastDay(e.target.value)} style={modalInputStyle} />
            </div>
          </div>
          <div>
            <label style={{ fontSize: 11, color: "var(--text3)", marginBottom: 4, display: "block" }}>Comments</label>
            <input value={comments} onChange={e => setComments(e.target.value)} style={modalInputStyle} placeholder="Optional" />
          </div>
          {error && <div style={{ fontSize: 11, color: "var(--danger)" }}>{error}</div>}
        </div>

        <div style={{ display: "flex", gap: 8, marginTop: 20, justifyContent: "flex-end" }}>
          <button onClick={onClose} style={{
            background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)",
            padding: "8px 16px", borderRadius: 6, fontSize: 12, cursor: "pointer",
          }}>Cancel</button>
          <button onClick={handleSubmit} style={{
            background: "var(--accent)", border: "none", color: "#fff",
            padding: "8px 16px", borderRadius: 6, fontSize: 12, cursor: "pointer",
            display: "flex", alignItems: "center", gap: 4,
          }}>
            <Check size={14} /> Add Vacation
          </button>
        </div>
      </div>
    </div>
  )
}

export default function Vacations() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [sheet, setSheet] = useState("")
  const [deleteId, setDeleteId] = useState<number | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [error, setError] = useState("")
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const todayStr = new Date().toISOString().slice(0, 10)

  const { data: active }   = useQuery({ queryKey:["vac-active"],   queryFn: () => api.vacations.current() })
  const { data: upcoming } = useQuery({ queryKey:["vac-upcoming"], queryFn: () => api.vacations.upcoming(7) })
  const { data, isLoading } = useQuery({
    queryKey: ["vacations", sheet],
    queryFn: () => api.vacations.get(`year=2026${sheet ? "&sheet=" + sheet : ""}`)
  })

  const grouped = useMemo(() => {
    if (!data) return []
    const map = new Map<string, { name: string; teamLead: string; periods: any[] }>()
    for (const v of (data as any[])) {
      const key = v.employeeId ?? `unknown-${v.id}`
      if (!map.has(key)) {
        const name = (v.firstName || v.lastName)
          ? `${v.firstName ?? ""} ${v.lastName ?? ""}`.trim()
          : `Unknown (${v.employeeId ?? "?"})`
        map.set(key, { name, teamLead: v.teamLeadName ?? "", periods: [] })
      }
      map.get(key)!.periods.push(v)
    }
    return Array.from(map.entries())
      .map(([empId, { name, teamLead, periods }]) => {
        const sorted = [...periods].sort((a: any, b: any) => a.firstDay.localeCompare(b.firstDay))
        const future = sorted.filter((p: any) => p.firstDay >= todayStr)
        return {
          empId,
          name,
          teamLead,
          totalDays: periods.reduce((s: number, p: any) => s + (p.workDaysNet ?? 0), 0),
          periodCount: periods.length,
          nextVacation: future[0]?.firstDay ?? null,
          periods: sorted,
        }
      })
      .sort((a, b) =>
        (a.teamLead || "￿").localeCompare(b.teamLead || "￿") ||
        a.name.localeCompare(b.name)
      )
  }, [data, todayStr])

  const toggleExpand = (empId: string) => {
    setExpanded(prev => {
      const next = new Set(prev)
      if (next.has(empId)) next.delete(empId); else next.add(empId)
      return next
    })
  }

  const handleAdd = async (form: any) => {
    try {
      await apiFetch("/api/vacations", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(form)
      } as any)
      qc.invalidateQueries({ queryKey: ["vacations"] })
      qc.invalidateQueries({ queryKey: ["vac-active"] })
      qc.invalidateQueries({ queryKey: ["vac-upcoming"] })
      qc.invalidateQueries({ queryKey: ["albalance"] })
      setShowAddModal(false)
      setError("")
    } catch {
      setError("Failed to add vacation. Check the employee and date range.")
    }
  }

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

  const deleteVac = data ? (data as any[]).find((v: any) => v.id === deleteId) : null

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.vacations")}</h1>
        <div style={{ display:"flex", gap:8, alignItems:"center" }}>
          <button onClick={() => setShowAddModal(true)} style={{
            background:"var(--accent)", border:"none", color:"#fff",
            padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer",
            display:"flex", alignItems:"center", gap:6
          }}>
            <Plus size={14} /> Add Vacation
          </button>
          <DownloadButtons onToday={api.vacations.downloadToday} on7Days={api.vacations.download7} on30Days={api.vacations.download30} />
        </div>
      </div>

      {error && (
        <div style={{ background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.3)",
          borderRadius:6, padding:"8px 14px", fontSize:12, color:"var(--danger)" }}>
          ❌ {error}
        </div>
      )}

      <div style={{ display:"grid", gridTemplateColumns:"repeat(3, 1fr)", gap:12 }}>
        {[
          { label:"On AL Today",     value:(active as any[])?.length ?? 0, color:"var(--green)" },
          { label:"Upcoming 7 Days", value:(upcoming as any[])?.length ?? 0, color:"var(--accent)" },
          { label:"Employees",       value:grouped.length,                  color:"var(--text)" },
        ].map(s => (
          <div key={s.label} style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"16px 20px" }}>
            <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", color:"var(--text3)", marginBottom:6 }}>{s.label}</div>
            <div style={{ fontSize:28, fontWeight:600, fontFamily:"IBM Plex Mono", color:s.color }}>{s.value}</div>
          </div>
        ))}
      </div>

      {upcoming && (upcoming as any[]).length > 0 && (
        <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, padding:"14px 16px" }}>
          <div style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)", marginBottom:10 }}>
            Starting next 7 days
          </div>
          {(upcoming as any[]).map((v: any) => (
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
        {/* Column header */}
        <div style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
          padding:"9px 12px", background:"var(--card2)", borderBottom:"1px solid var(--border)",
          fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em", color:"var(--text3)" }}>
          <div/>
          <div>Name</div>
          <div>Team Lead</div>
          <div style={{ textAlign:"center" }}>Periods</div>
          <div style={{ textAlign:"center" }}>Days</div>
          <div>Next Vacation</div>
        </div>

        {isLoading && Array.from({length: 8}).map((_, i) => (
          <div key={i} style={{ padding:"12px 12px", borderBottom:"1px solid var(--border)" }}>
            <div className="skeleton" style={{ height:11, width:"55%" }}/>
          </div>
        ))}

        {grouped.map(grp => {
          const isOpen = expanded.has(grp.empId)
          return (
            <div key={grp.empId} style={{ borderBottom:"1px solid var(--border)" }}>
              {/* Employee summary row */}
              <div
                onClick={() => toggleExpand(grp.empId)}
                style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
                  padding:"10px 12px", cursor:"pointer", alignItems:"center",
                  background: isOpen ? "rgba(255,255,255,0.03)" : "transparent",
                  transition:"background 0.1s" }}
                onMouseEnter={e => { if (!isOpen) (e.currentTarget as HTMLElement).style.background = "var(--card2)" }}
                onMouseLeave={e => { if (!isOpen) (e.currentTarget as HTMLElement).style.background = "transparent" }}>
                <div style={{ color:"var(--text3)", display:"flex", alignItems:"center" }}>
                  {isOpen ? <ChevronDown size={14}/> : <ChevronRight size={14}/>}
                </div>
                <div style={{ fontSize:13, fontWeight:500, color:"var(--text)" }}>{grp.name}</div>
                <div style={{ fontSize:11, color:"var(--text3)" }}>{grp.teamLead}</div>
                <div style={{ textAlign:"center", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text2)" }}>
                  {grp.periodCount}
                </div>
                <div style={{ textAlign:"center", fontFamily:"IBM Plex Mono", fontSize:12, fontWeight:600, color:"var(--green)" }}>
                  {grp.totalDays}d
                </div>
                <div style={{ fontFamily:"IBM Plex Mono", fontSize:11, color: grp.nextVacation ? "var(--accent)" : "var(--text3)" }}>
                  {grp.nextVacation ?? "—"}
                </div>
              </div>

              {/* Expanded period rows */}
              {isOpen && (
                <div style={{ background:"rgba(0,0,0,0.18)", borderTop:"1px solid var(--border)" }}>
                  {grp.periods.map((p: any) => (
                    <div key={p.id}
                      style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
                        padding:"7px 12px", alignItems:"center",
                        borderBottom:"1px solid rgba(255,255,255,0.04)", fontSize:11 }}>
                      <div/>
                      <div style={{ fontFamily:"IBM Plex Mono", color:"var(--text2)", fontSize:11 }}>
                        {p.firstDay} → {p.lastDay}
                      </div>
                      <div>
                        {p.isOverhead
                          ? <span style={{ background:"rgba(167,139,250,.15)", color:"var(--purple)", padding:"1px 6px", borderRadius:4, fontSize:10 }}>Overhead</span>
                          : <span style={{ background:"rgba(34,208,122,.1)", color:"var(--green)", padding:"1px 6px", borderRadius:4, fontSize:10 }}>Agent</span>}
                      </div>
                      <div/>
                      <div style={{ textAlign:"center", fontFamily:"IBM Plex Mono", color:"var(--text3)" }}>
                        {p.workDaysNet ?? "—"}d
                      </div>
                      <div style={{ display:"flex", alignItems:"center", justifyContent:"space-between", gap:6 }}>
                        <span style={{ color:"var(--text3)", fontSize:10, overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                          {p.comments ?? ""}
                        </span>
                        <button onClick={e => { e.stopPropagation(); setDeleteId(p.id) }} style={{
                          background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.2)",
                          color:"var(--danger)", padding:"3px 7px", borderRadius:4,
                          fontSize:10, cursor:"pointer", display:"flex", alignItems:"center", gap:3,
                          flexShrink:0
                        }}><Trash2 size={10}/></button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )
        })}

        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)",
          fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>
          {grouped.length} employees · {(data as any[])?.length ?? 0} periods
        </div>
      </div>

      {showAddModal && <AddVacationModal onClose={() => { setShowAddModal(false); setError("") }} onSave={handleAdd} />}

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
              }}><Trash2 size={13}/> Delete & Restore AL</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
