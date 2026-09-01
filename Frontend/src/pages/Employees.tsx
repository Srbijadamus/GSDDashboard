import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { Plus, Pencil, Trash2, X, Check, AlertTriangle, XCircle } from "lucide-react"

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
    EARLY:"text-warn-fg", MORNING:"text-info-fg", AFTERNOON:"text-wic-fg",
    NIGHT:"text-learn-fg", BACKUP:"text-ink-muted",
  }
  const label = SHIFT_PATTERNS.find(s => s.value === pattern)?.label ?? pattern
  return <span className={`font-mono text-[11px] ${colors[pattern] ?? "text-ink-muted"}`}>{label}</span>
}

const badge = (type: string) => {
  const cls: Record<string,string> = {
    "Full Time": "bg-info-bg text-info-fg",
    "Part-Time": "bg-warn-bg text-warn-fg",
    "Student":   "bg-wic-bg text-wic-fg",
  }
  const c = cls[type] ?? "bg-raised text-ink-muted"
  return <span className={`font-mono text-[10px] font-semibold px-[7px] py-[2px] rounded ${c}`}>
    {type === "Full Time" ? "FT" : type === "Part-Time" ? "PT" : "STU"}
  </span>
}

const roleBadge = (role: string) => {
  const colors: Record<string,string> = {
    "Voice":"text-wic-fg", "SSP":"text-info-fg", "Chat":"text-learn-fg",
    "Dispatcher":"text-warn-fg", "WIC":"text-wic-fg", "SME":"text-wic-fg",
  }
  return <span className={`font-mono text-[11px] ${colors[role] ?? "text-ink-muted"}`}>{role}</span>
}

const inputCls = "bg-sunken border border-line-subtle text-ink py-[7px] px-[10px] rounded-[6px] text-[12px] outline-none w-full"
const selectCls = inputCls

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
    <div className="fixed inset-0 bg-[rgba(0,0,0,.7)] z-[1000] flex items-center justify-center" onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="bg-raised border border-line-subtle rounded-[10px] p-6 w-[460px] max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-center mb-5">
          <h2 className="text-base font-semibold text-ink">
            {emp ? "Edit Agent" : "Add New Agent"}
          </h2>
          <button onClick={onClose} className="bg-transparent border-none text-ink-soft cursor-pointer">
            <X size={18} />
          </button>
        </div>

        <div className="flex flex-col gap-[14px]">
          {/* Employee ID */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Employee ID *</label>
            <input value={form.employeeId} onChange={e => handleChange("employeeId", e.target.value)}
              disabled={!!emp} className={inputCls} style={{ opacity: emp ? .5 : 1 }} placeholder="e.g. 9130648" />
            {errors.employeeId && <div className="text-[10px] text-crit-fg mt-[3px]">{errors.employeeId}</div>}
          </div>

          {/* Full Name */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Full Name *</label>
            <input value={form.fullName} onChange={e => handleChange("fullName", e.target.value)}
              className={inputCls} placeholder="First Last" />
            {errors.fullName && <div className="text-[10px] text-crit-fg mt-[3px]">{errors.fullName}</div>}
          </div>

          {/* Engagement */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Type</label>
            <select value={form.engagement} onChange={e => handleChange("engagement", e.target.value)} className={selectCls}>
              {ENGAGEMENTS.map(t => <option key={t}>{t}</option>)}
            </select>
          </div>

          {/* Role */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Primary Role</label>
            <select value={form.primaryRole} onChange={e => handleChange("primaryRole", e.target.value)} className={selectCls}>
              {ROLES.map(r => <option key={r}>{r}</option>)}
            </select>
          </div>

          {/* Team Lead */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Team Lead</label>
            <select value={form.teamLeadName} onChange={e => handleChange("teamLeadName", e.target.value)} className={selectCls}>
              {TEAM_LEADS.map(tl => <option key={tl}>{tl}</option>)}
            </select>
          </div>

          {/* Source */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Source Sheet</label>
            <select value={form.sourceSheet} onChange={e => handleChange("sourceSheet", e.target.value)} className={selectCls}>
              {SOURCES.map(s => <option key={s}>{s}</option>)}
            </select>
          </div>
          {/* Bundesland */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Bundesland</label>
            <select value={(form as any).bundesland} onChange={e => handleChange("bundesland", e.target.value)} className={selectCls}>
              <option value="">-- Select Bundesland --</option>
              {BUNDESLAENDER.map(b => <option key={b} value={b}>{b}</option>)}
            </select>
          </div>
          {/* Shift Pattern */}
          <div>
            <label className="text-[11px] text-ink-soft mb-1 block">Shift</label>
            <select value={(form as any).shiftPattern} onChange={e => handleChange("shiftPattern", e.target.value)} className={selectCls}>
              <option value="">-- Select Shift --</option>
              {SHIFT_PATTERNS.map(s => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </div>
        </div>

        <div className="flex gap-2 mt-5 justify-end">
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted py-2 px-4 rounded-[6px] text-[12px] cursor-pointer">Cancel</button>
          <button onClick={handleSubmit} className="bg-info-solid border-none text-white py-2 px-4 rounded-[6px] text-[12px] cursor-pointer flex items-center gap-1">
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
    <div className="fixed inset-0 bg-[rgba(0,0,0,.7)] z-[1000] flex items-center justify-center">
      <div className="bg-raised border border-crit-bd rounded-[10px] p-6 w-[400px]">
        <h2 className="text-base font-semibold text-crit-fg mb-3">Delete Agent</h2>
        <p className="text-[13px] text-ink-muted mb-2">
          Are you sure you want to delete <strong className="text-ink">{emp.fullName}</strong>?
        </p>
        {futureCount > 0 && (
          <div className="bg-crit-bg border border-crit-bd rounded-[6px] px-3 py-[10px] mb-3 text-[12px] text-crit-fg flex items-center gap-2">
            <AlertTriangle size={12} /> This agent has <strong>{futureCount}</strong> scheduled future shifts that will be affected.
          </div>
        )}
        <div className="flex gap-2 justify-end">
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted py-2 px-4 rounded-[6px] text-[12px] cursor-pointer">Cancel</button>
          <button onClick={onConfirm} className="bg-crit-solid border-none text-white py-2 px-4 rounded-[6px] text-[12px] cursor-pointer flex items-center gap-1">
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
    <div className="flex flex-col gap-4">
      <div className="flex justify-between items-center">
        <h1 className="text-[22px] font-semibold text-ink">{t("nav.employees")}</h1>
        <button onClick={() => setShowModal(true)} className="bg-info-solid border-none text-white py-2 px-4 rounded-[6px] text-[12px] cursor-pointer flex items-center gap-[6px]">
          <Plus size={14} /> Add Agent
        </button>
      </div>

      {error && (
        <div className="bg-crit-bg border border-crit-bd rounded-[6px] px-[14px] py-[10px] text-[12px] text-crit-fg flex items-center gap-2">
          <XCircle size={14} /> {error}
        </div>
      )}

      <div className="flex gap-[10px]">
        <input placeholder="Search name, ID, team lead..." value={search}
          onChange={e => setSearch(e.target.value)}
          className="flex-1 bg-raised border border-line-subtle text-ink py-[7px] px-3 rounded-[6px] text-[12px] outline-none" />
        <select value={source} onChange={e => setSource(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted py-[7px] px-3 rounded-[6px] text-[12px]">
          <option value="">All Teams</option>
          <option value="GSD_DE">GSD DE</option>
          <option value="GSD_NL">GSD NL</option>
          <option value="GSD_WIC">WIC</option>
        </select>
        <select value={engagement} onChange={e => setEngagement(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted py-[7px] px-3 rounded-[6px] text-[12px]">
          <option value="">All Types</option>
          <option value="Full Time">Full Time</option>
          <option value="Part-Time">Part-Time</option>
          <option value="Student">Student</option>
        </select>
        <select value={role} onChange={e => setRole(e.target.value)}
          className="bg-raised border border-line-subtle text-ink-muted py-[7px] px-3 rounded-[6px] text-[12px]">
          <option value="">All Roles</option>
          {ROLES.map(r => <option key={r} value={r}>{r}</option>)}
        </select>
      </div>

      <div className="bg-raised border border-line-subtle rounded-[8px] overflow-hidden">
        <div className="overflow-x-auto">
          <table style={{ width:"100%", borderCollapse:"collapse", fontSize:12 }}>
            <thead>
              <tr className="bg-sunken">
                {["ID","Full Name","Type","Primary Role","Team Lead","Source","Bundesland","Shift","Actions"].map(h => (
                  <th key={h} className="px-3 py-[10px] text-left text-[10px] font-medium uppercase tracking-[.07em] text-ink-soft border-b border-line-subtle">{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isLoading && <tr><td colSpan={9} className="p-6 text-center text-ink-soft">Loading...</td></tr>}
              {filtered.map((e: any) => (
                <tr key={e.employeeId} className="border-b border-line-subtle hover:bg-hovered">
                  <td className="px-3 py-[9px] font-mono text-[11px] text-ink-soft">{e.employeeId}</td>
                  <td className="px-3 py-[9px] font-medium">{e.fullName}</td>
                  <td className="px-3 py-[9px]">{badge(e.engagement)}</td>
                  <td className="px-3 py-[9px]">{roleBadge(e.primaryRole)}</td>
                  <td className="px-3 py-[9px] text-ink-muted text-[11px]">{e.teamLeadName}</td>
                  <td className="px-3 py-[9px] text-[10px] font-mono text-ink-soft">{e.sourceSheet}</td>
                  <td className="px-3 py-[9px]">{(e as any).bundesland && <span className="bg-holiday-bg border border-holiday-bd text-holiday-fg" style={{ borderRadius:4, fontSize:9, padding:"2px 6px", fontWeight:600 }}>{(e as any).bundesland}</span>}</td>
                  <td className="px-3 py-[9px]">{shiftBadge((e as any).shiftPattern)}</td>
                  <td className="px-3 py-[9px]">
                    <div className="flex gap-[6px]">
                      <button onClick={() => setEditEmp(e)} className="bg-info-bg border border-info-bd text-info-fg px-2 py-1 rounded text-[10px] cursor-pointer flex items-center gap-[3px]"><Pencil size={11} /> Edit</button>
                      <button onClick={() => openDelete(e)} className="bg-crit-bg border border-crit-bd text-crit-fg px-2 py-1 rounded text-[10px] cursor-pointer flex items-center gap-[3px]"><Trash2 size={11} /> Delete</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className="px-3 py-2 border-t border-line-subtle text-[11px] text-ink-soft font-mono">
          {filtered.length} employees
        </div>
      </div>

      {showModal && <EmployeeModal onClose={() => { setShowModal(false); setError("") }} onSave={handleAdd} />}
      {editEmp  && <EmployeeModal emp={editEmp} onClose={() => { setEditEmp(null); setError("") }} onSave={handleEdit} />}
      {deleteEmp && <DeleteModal emp={deleteEmp} futureCount={futureCount} onClose={() => setDeleteEmp(null)} onConfirm={handleDelete} />}
    </div>
  )
}





