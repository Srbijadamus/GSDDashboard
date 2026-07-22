import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { Plus, Pencil, Trash2, X, Check } from "lucide-react"

// const API_BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000"

const ROLES = ["Voice","SSP","Chat","Dispatcher","SME","WIC","Bulk PWs"]
const TEAM_LEADS = ["Karlo Coric","Oliver Schleusen","Tobias Rossberg","Delia Panaitescu","Ion Ciuceanu","Jaroslaw Brzeszkiewicz"]
const ENGAGEMENTS = ["Full Time","Part-Time","Student"]
const SOURCES = ["GSD_DE","GSD_NL","GSD_WIC"]
const BUNDESLAENDER = ["Bayern","NRW","Hamburg","Berlin","Baden-Württemberg","Sachsen","Thüringen","Brandenburg","Sachsen-Anhalt","Mecklenburg-Vorpommern","Niedersachsen","Bremen","Hessen","Rheinland-Pfalz","Saarland","Schleswig-Holstein"]
const SHIFT_PATTERNS = [
  { value:"EARLY",     label:"Early (06/07)" },
  { value:"MORNING",   label:"Morning (08)" },
  { value:"AFTERNOON", label:"Afternoon (13)" },
  { value:"NIGHT",     label:"Night" },
  { value:"BACKUP",    label:"Backup" },
]

const shiftBadge = (pattern: string) => {
  if (!pattern) return null
  const colors: Record<string,string> = {
    EARLY:"var(--warn)", MORNING:"var(--accent)", AFTERNOON:"var(--accent2)",
    NIGHT:"var(--purple)", BACKUP:"var(--text2)",
  }
  const label = SHIFT_PATTERNS.find(s => s.value === pattern)?.label ?? pattern
  return <span style={{ color: colors[pattern] ?? "var(--text2)", fontSize:11, fontFamily:"IBM Plex Mono" }}>{label}</span>
}

const badge = (type: string) => {
  const styles: Record<string,any> = {
    "Full Time": { background:"rgba(59,126,255,.15)", color:"var(--blue-light)" },
    "Part-Time": { background:"rgba(255,124,59,.15)", color:"var(--warn)" },
    "Student":   { background:"rgba(0,210,160,.15)",  color:"var(--accent2)" },
  }
  const s = styles[type] ?? { background:"rgba(255,255,255,.08)", color:"var(--text2)" }
  return <span style={{ ...s, padding:"2px 7px", borderRadius:4, fontSize:10, fontWeight:600, fontFamily:"IBM Plex Mono" }}>
    {type === "Full Time" ? "FT" : type === "Part-Time" ? "PT" : "STU"}
  </span>
}

const roleBadge = (role: string) => {
  const colors: Record<string,string> = {
    "Voice":"var(--accent2)", "SSP":"var(--accent)", "Chat":"var(--purple)",
    "Dispatcher":"var(--warn)", "WIC":"var(--blue-light)", "SME":"var(--accent2)",
  }
  return <span style={{ color: colors[role] ?? "var(--text2)", fontSize:11, fontFamily:"IBM Plex Mono" }}>{role}</span>
}

const inputStyle = {
  background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text)",
  padding:"7px 10px", borderRadius:6, fontSize:12, outline:"none",
  fontFamily:"IBM Plex Sans", width:"100%"
}

const selectStyle = { ...inputStyle }

function EmployeeModal({ emp, onClose, onSave }: {
  emp?: any; onClose: () => void; onSave: (data: any) => void
}) {
  const [form, setForm] = useState({
    employeeId:   emp?.employeeId ?? "",
    fullName:     emp?.fullName ?? "",
    engagement:   emp?.engagement ?? "Full Time",
    primaryRole:  emp?.primaryRole ?? "Voice",
    teamLeadName: emp?.teamLeadName ?? TEAM_LEADS[0],
    sourceSheet:  emp?.sourceSheet ?? "GSD_DE",
    category:     emp?.category ?? "",
    bundesland:   emp?.bundesland ?? "",
    shiftPattern: emp?.shiftPattern ?? "",
  })
  const [errors, setErrors] = useState<Record<string,string>>({})

  const validate = () => {
    const e: Record<string,string> = {}
    if (!form.employeeId.trim()) e.employeeId = "Required"
    if (!/^\d+$/.test(form.employeeId.trim())) e.employeeId = "Must be numeric"
    if (!form.fullName.trim() || form.fullName.trim().length < 2) e.fullName = "Min 2 characters"
    return e
  }

  const handleChange = (field: string, value: string) => {
    setForm(f => ({ ...f, [field]: value }))
    setErrors(e => { const ne = { ...e }; delete ne[field]; return ne })
  }

  const handleSubmit = () => {
    const e = validate()
    if (Object.keys(e).length > 0) { setErrors(e); return }
    onSave(form)
  }

  return (
    <div style={{
      position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000,
      display:"flex", alignItems:"center", justifyContent:"center"
    }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{
        background:"var(--card)", border:"1px solid var(--border)", borderRadius:10,
        padding:24, width:460, maxHeight:"90vh", overflowY:"auto"
      }}>
        <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center", marginBottom:20 }}>
          <h2 style={{ fontSize:16, fontWeight:600, color:"var(--text)" }}>
            {emp ? "Edit Agent" : "Add New Agent"}
          </h2>
          <button onClick={onClose} style={{ background:"none", border:"none", color:"var(--text3)", cursor:"pointer" }}>
            <X size={18} />
          </button>
        </div>

        <div style={{ display:"flex", flexDirection:"column", gap:14 }}>
          {/* Employee ID */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Employee ID *</label>
            <input value={form.employeeId} onChange={e => handleChange("employeeId", e.target.value)}
              disabled={!!emp} style={{ ...inputStyle, opacity: emp ? .5 : 1 }} placeholder="e.g. 9130648" />
            {errors.employeeId && <div style={{ fontSize:10, color:"var(--danger)", marginTop:3 }}>{errors.employeeId}</div>}
          </div>

          {/* Full Name */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Full Name *</label>
            <input value={form.fullName} onChange={e => handleChange("fullName", e.target.value)}
              style={inputStyle} placeholder="First Last" />
            {errors.fullName && <div style={{ fontSize:10, color:"var(--danger)", marginTop:3 }}>{errors.fullName}</div>}
          </div>

          {/* Engagement */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Type</label>
            <select value={form.engagement} onChange={e => handleChange("engagement", e.target.value)} style={selectStyle}>
              {ENGAGEMENTS.map(t => <option key={t}>{t}</option>)}
            </select>
          </div>

          {/* Role */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Primary Role</label>
            <select value={form.primaryRole} onChange={e => handleChange("primaryRole", e.target.value)} style={selectStyle}>
              {ROLES.map(r => <option key={r}>{r}</option>)}
            </select>
          </div>

          {/* Team Lead */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Team Lead</label>
            <select value={form.teamLeadName} onChange={e => handleChange("teamLeadName", e.target.value)} style={selectStyle}>
              {TEAM_LEADS.map(tl => <option key={tl}>{tl}</option>)}
            </select>
          </div>

          {/* Source */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Source Sheet</label>
            <select value={form.sourceSheet} onChange={e => handleChange("sourceSheet", e.target.value)} style={selectStyle}>
              {SOURCES.map(s => <option key={s}>{s}</option>)}
            </select>
          </div>
          {/* Bundesland */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Bundesland</label>
            <select value={(form as any).bundesland} onChange={e => handleChange("bundesland", e.target.value)} style={selectStyle}>
              <option value="">-- Select Bundesland --</option>
              {BUNDESLAENDER.map(b => <option key={b} value={b}>{b}</option>)}
            </select>
          </div>
          {/* Shift Pattern */}
          <div>
            <label style={{ fontSize:11, color:"var(--text3)", marginBottom:4, display:"block" }}>Shift</label>
            <select value={(form as any).shiftPattern} onChange={e => handleChange("shiftPattern", e.target.value)} style={selectStyle}>
              <option value="">-- Select Shift --</option>
              {SHIFT_PATTERNS.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </div>
        </div>

        <div style={{ display:"flex", gap:8, marginTop:20, justifyContent:"flex-end" }}>
          <button onClick={onClose} style={{
            background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)",
            padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer"
          }}>Cancel</button>
          <button onClick={handleSubmit} style={{
            background:"var(--accent)", border:"none", color:"#fff",
            padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer",
            display:"flex", alignItems:"center", gap:4
          }}>
            <Check size={14} /> {emp ? "Save Changes" : "Add Agent"}
          </button>
        </div>
      </div>
    </div>
  )
}

function DeleteModal({ emp, futureCount, onClose, onConfirm }: {
  emp: any; futureCount: number; onClose: () => void; onConfirm: () => void
}) {
  return (
    <div style={{
      position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000,
      display:"flex", alignItems:"center", justifyContent:"center"
    }}>
      <div style={{
        background:"var(--card)", border:"1px solid rgba(255,59,92,.3)", borderRadius:10, padding:24, width:400
      }}>
        <h2 style={{ fontSize:16, fontWeight:600, color:"var(--danger)", marginBottom:12 }}>Delete Agent</h2>
        <p style={{ fontSize:13, color:"var(--text2)", marginBottom:8 }}>
          Are you sure you want to delete <strong style={{ color:"var(--text)" }}>{emp.fullName}</strong>?
        </p>
        {futureCount > 0 && (
          <div style={{
            background:"rgba(255,59,92,.08)", border:"1px solid rgba(255,59,92,.2)",
            borderRadius:6, padding:"10px 12px", marginBottom:12, fontSize:12, color:"var(--danger)"
          }}>
            ⚠ This agent has <strong>{futureCount}</strong> scheduled future shifts that will be affected.
          </div>
        )}
        <div style={{ display:"flex", gap:8, justifyContent:"flex-end" }}>
          <button onClick={onClose} style={{
            background:"var(--card2)", border:"1px solid var(--border)", color:"var(--text2)",
            padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer"
          }}>Cancel</button>
          <button onClick={onConfirm} style={{
            background:"var(--danger)", border:"none", color:"#fff",
            padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer",
            display:"flex", alignItems:"center", gap:4
          }}>
            <Trash2 size={14} /> Delete
          </button>
        </div>
      </div>
    </div>
  )
}

export default function Employees() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [search, setSearch]         = useState("")
  const [source, setSource]         = useState("")
  const [engagement, setEngagement] = useState("")
  const [role, setRole]             = useState("")
  const [showModal, setShowModal]   = useState(false)
  const [editEmp, setEditEmp]       = useState<any>(null)
  const [deleteEmp, setDeleteEmp]   = useState<any>(null)
  const [futureCount, setFutureCount] = useState(0)
  const [error, setError]           = useState("")

  const { data, isLoading } = useQuery({
    queryKey: ["employees", source, engagement],
    queryFn: () => {
      const params = []
      if (source)     params.push(`source=${source}`)
      if (engagement) params.push(`engagement=${engagement}`)
      return api.employees.get(params.join("&"))
    }
  })

  const filtered = data?.filter((e: any) =>
    (!role || e.primaryRole === role) &&
    (!search ||
      e.fullName?.toLowerCase().includes(search.toLowerCase()) ||
      e.employeeId?.toString().includes(search) ||
      e.teamLeadName?.toLowerCase().includes(search.toLowerCase()))
  ) ?? []

  const handleAdd = async (form: any) => {
    try {
      await apiFetch("/api/employees", {
        method:"POST", headers:{"Content-Type":"application/json"}, body:JSON.stringify(form)
      } as any)
      qc.invalidateQueries({ queryKey:["employees"] })
      setShowModal(false)
      setError("")
    } catch { setError("Failed to add agent. ID may already exist.") }
  }

  const handleEdit = async (form: any) => {
    try {
      await apiFetch(`/api/employees/${editEmp.employeeId}`, {
        method:"PATCH", headers:{"Content-Type":"application/json"}, body:JSON.stringify(form)
      } as any)
      qc.invalidateQueries({ queryKey:["employees"] })
      setEditEmp(null)
      setError("")
    } catch { setError("Failed to update agent.") }
  }

  const openDelete = async (emp: any) => {
    const res = await apiFetch<any>(`/api/employees/${emp.employeeId}/future-shifts`)
    setFutureCount(res.count)
    setDeleteEmp(emp)
  }

  const handleDelete = async () => {
    try {
      await apiFetch(`/api/employees/${deleteEmp.employeeId}`, { method:"DELETE" } as any)
      qc.invalidateQueries({ queryKey:["employees"] })
      setDeleteEmp(null)
      setError("")
    } catch { setError("Failed to delete agent.") }
  }

  return (
    <div style={{ display:"flex", flexDirection:"column", gap:16 }}>
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center" }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.employees")}</h1>
        <button onClick={() => setShowModal(true)} style={{
          background:"var(--accent)", border:"none", color:"#fff",
          padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer",
          display:"flex", alignItems:"center", gap:6
        }}>
          <Plus size={14} /> Add Agent
        </button>
      </div>

      {error && (
        <div style={{ background:"rgba(255,59,92,.1)", border:"1px solid rgba(255,59,92,.3)",
          borderRadius:6, padding:"10px 14px", fontSize:12, color:"var(--danger)" }}>
          ❌ {error}
        </div>
      )}

      <div style={{ display:"flex", gap:10 }}>
        <input placeholder="Search name, ID, team lead..." value={search}
          onChange={e => setSearch(e.target.value)}
          style={{ flex:1, background:"var(--card)", border:"1px solid var(--border)",
            color:"var(--text)", padding:"7px 12px", borderRadius:6, fontSize:12, outline:"none" }} />
        <select value={source} onChange={e => setSource(e.target.value)}
          style={{ background:"var(--card)", border:"1px solid var(--border)",
            color:"var(--text2)", padding:"7px 12px", borderRadius:6, fontSize:12 }}>
          <option value="">All Teams</option>
          <option value="GSD_DE">GSD DE</option>
          <option value="GSD_NL">GSD NL</option>
          <option value="GSD_WIC">WIC</option>
        </select>
        <select value={engagement} onChange={e => setEngagement(e.target.value)}
          style={{ background:"var(--card)", border:"1px solid var(--border)",
            color:"var(--text2)", padding:"7px 12px", borderRadius:6, fontSize:12 }}>
          <option value="">All Types</option>
          <option value="Full Time">Full Time</option>
          <option value="Part-Time">Part-Time</option>
          <option value="Student">Student</option>
        </select>
        <select value={role} onChange={e => setRole(e.target.value)}
          style={{ background:"var(--card)", border:"1px solid var(--border)",
            color:"var(--text2)", padding:"7px 12px", borderRadius:6, fontSize:12 }}>
          <option value="">All Roles</option>
          {ROLES.map(r => <option key={r} value={r}>{r}</option>)}
        </select>
      </div>

      <div style={{ background:"var(--card)", border:"1px solid var(--border)", borderRadius:8, overflow:"hidden" }}>
        <div style={{ overflowX:"auto" }}>
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr style={{ background:"var(--card2)" }}>
                {["ID","Full Name","Type","Primary Role","Team Lead","Source","Bundesland","Shift","Actions"].map(h => (
                  <th key={h} style={{ padding:"10px 12px", textAlign:"left", fontSize:10,
                    fontWeight:500, textTransform:"uppercase", letterSpacing:".07em",
                    color:"var(--text3)", borderBottom:"1px solid var(--border)" }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && <tr><td colSpan={9} style={{ padding:24, textAlign:"center", color:"var(--text3)" }}>Loading...</td></tr>}
              {filtered.map((e: any) => (
                <tr key={e.employeeId}
                  style={{ borderBottom:"1px solid var(--border)" }}
                  onMouseEnter={ev => (ev.currentTarget.style.background = "var(--card2)")}
                  onMouseLeave={ev => (ev.currentTarget.style.background = "transparent")}>
                  <td style={{ padding:"9px 12px", fontFamily:"IBM Plex Mono", fontSize:11, color:"var(--text3)" }}>{e.employeeId}</td>
                  <td style={{ padding:"9px 12px", fontWeight:500 }}>{e.fullName}</td>
                  <td style={{ padding:"9px 12px" }}>{badge(e.engagement)}</td>
                  <td style={{ padding:"9px 12px" }}>{roleBadge(e.primaryRole)}</td>
                  <td style={{ padding:"9px 12px", color:"var(--text2)", fontSize:11 }}>{e.teamLeadName}</td>
                  <td style={{ padding:"9px 12px", fontSize:10, fontFamily:"IBM Plex Mono", color:"var(--text3)" }}>{e.sourceSheet}</td>
                  <td style={{ padding:"9px 12px" }}>{(e as any).bundesland && <span style={{ background:"rgba(250,204,21,0.1)", border:"1px solid rgba(250,204,21,0.3)", color:"var(--yellow)", borderRadius:4, fontSize:9, padding:"2px 6px", fontWeight:600 }}>{(e as any).bundesland}</span>}</td>
                  <td style={{ padding:"9px 12px" }}>{shiftBadge((e as any).shiftPattern)}</td>
                  <td style={{ padding:"9px 12px" }}>
                    <div style={{ display:"flex", gap:6 }}>
                      <button onClick={() => setEditEmp(e)} style={{
                        background:"rgba(59,126,255,.12)", border:"1px solid rgba(59,126,255,.2)",
                        color:"var(--accent)", padding:"4px 8px", borderRadius:4,
                        fontSize:10, cursor:"pointer", display:"flex", alignItems:"center", gap:3
                      }}><Pencil size={11} /> Edit</button>
                      <button onClick={() => openDelete(e)} style={{
                        background:"rgba(255,59,92,.12)", border:"1px solid rgba(255,59,92,.2)",
                        color:"var(--danger)", padding:"4px 8px", borderRadius:4,
                        fontSize:10, cursor:"pointer", display:"flex", alignItems:"center", gap:3
                      }}><Trash2 size={11} /> Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div style={{ padding:"8px 12px", borderTop:"1px solid var(--border)",
          fontSize:11, color:"var(--text3)", fontFamily:"IBM Plex Mono" }}>
          {filtered.length} employees
        </div>
      </div>

      {showModal && <EmployeeModal onClose={() => { setShowModal(false); setError("") }} onSave={handleAdd} />}
      {editEmp  && <EmployeeModal emp={editEmp} onClose={() => { setEditEmp(null); setError("") }} onSave={handleEdit} />}
      {deleteEmp && <DeleteModal emp={deleteEmp} futureCount={futureCount} onClose={() => setDeleteEmp(null)} onConfirm={handleDelete} />}
    </div>
  )
}





