import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useState, useMemo } from "react"
import { useTranslation } from "react-i18next"
import { api, apiFetch } from "../api/client"
import { DownloadButtons } from "../components/DownloadButtons"
import { Trash2, ChevronDown, ChevronRight, Plus, X, Check, CheckCircle2, AlertCircle, XCircle } from "lucide-react"
import { maxFutureDateStr } from "../constants"
import { resolveEmployee } from "../utils/resolveEmployee"
import type { BaseRowStatus, MatchType } from "../utils/resolveEmployee"

interface EmployeeOption {
  employeeId: string
  fullName: string | null
}

// ── Bulk import helpers ───────────────────────────────────────────────────────

type VacRowStatus = BaseRowStatus | "parse-error"

interface VacParsedRow {
  rawLine: string
  rawName: string
  firstDay: string
  lastDay: string
  resolved: EmployeeOption | null
  ambiguous: EmployeeOption[]
  status: VacRowStatus
  matchType?: MatchType
  suggestions?: EmployeeOption[]
}

interface VacSaveResult {
  name: string
  status: "saved" | "skipped" | "error"
  reason?: string
}

// Parses lines like:
//   "Shelikhov Dmytro — 27.07.26–29.07.26"
//   "Nguyen Tim 28.07.26 - 31.07.26"
//   "Karatas Ayten 27.07.2026 - 14.08.2026"
function parseVacLine(line: string): { rawName: string; firstDay: string; lastDay: string } | null {
  const dateRx = /(\d{1,2})\.(\d{2})\.(\d{2,4})\s*[–—-]+\s*(\d{1,2})\.(\d{2})\.(\d{2,4})/
  const m = line.match(dateRx)
  if (!m || m.index === undefined) return null
  const rawName = line.slice(0, m.index).replace(/[—–\s-]+$/, "").trim()
  if (!rawName) return null
  const iso = (d: string, mo: string, y: string) =>
    `${y.length === 2 ? "20" + y : y}-${mo.padStart(2, "0")}-${d.padStart(2, "0")}`
  return { rawName, firstDay: iso(m[1], m[2], m[3]), lastDay: iso(m[4], m[5], m[6]) }
}

// ── Bulk Import Panel ─────────────────────────────────────────────────────────

function BulkImportPanel({ onSaved }: { onSaved: () => void }) {
  const { data: employees = [] } = useQuery<EmployeeOption[]>({
    queryKey: ["employees-active"],
    queryFn: () => fetch("/api/employees/?active=true").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60_000,
  })

  const [text, setText]               = useState("")
  const [rows, setRows]               = useState<VacParsedRow[] | null>(null)
  const [saving, setSaving]           = useState(false)
  const [saveResults, setSaveResults] = useState<VacSaveResult[] | null>(null)
  const [error, setError]             = useState<string | null>(null)

  const card: React.CSSProperties = {
    background: "var(--card)", border: "1px solid var(--border)",
    borderRadius: 10, padding: "18px 20px", marginBottom: 16,
  }
  const inp: React.CSSProperties = {
    background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 6,
    padding: "7px 10px", color: "var(--text)", fontSize: 12,
    fontFamily: "IBM Plex Sans", outline: "none", boxSizing: "border-box" as const,
  }
  const btnP: React.CSSProperties = {
    background: "var(--accent)", border: "none", color: "#fff",
    padding: "7px 16px", borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: "pointer",
  }
  const btnS: React.CSSProperties = {
    background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)",
    padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
  }
  const thS: React.CSSProperties = {
    padding: "8px 12px", textAlign: "left" as const, fontSize: 10, fontWeight: 600,
    color: "var(--text3)", textTransform: "uppercase" as const, letterSpacing: "0.06em",
    borderBottom: "1px solid var(--border)",
  }
  const tdS: React.CSSProperties = {
    padding: "8px 12px", fontSize: 12, color: "var(--text)", borderBottom: "1px solid var(--border)",
  }

  const handleParse = () => {
    setError(null); setSaveResults(null)
    const lines = text.split("\n").map(l => l.trim()).filter(l => l.length > 0)
    if (!lines.length) { setError("Nothing to parse."); return }
    setRows(lines.map(line => {
      const p = parseVacLine(line)
      if (!p) return { rawLine: line, rawName: line, firstDay: "", lastDay: "", resolved: null, ambiguous: [], status: "parse-error" as const }
      return { rawLine: line, ...p, ...resolveEmployee(p.rawName, employees) }
    }))
  }

  const handleSuggestionPick = (i: number, empId: string) => {
    const emp = employees.find(e => e.employeeId === empId)
    if (!emp || !rows) return
    setRows(rows.map((r, idx) => idx !== i ? r : { ...r, resolved: emp, status: "resolved" as const, matchType: "fuzzy" as const }))
  }

  const handleSave = async () => {
    if (!rows) return
    setSaving(true); setSaveResults(null); setError(null)
    const results: VacSaveResult[] = []
    const seen = new Set<string>()

    for (const row of rows) {
      if ((row.status !== "resolved" && row.status !== "resolved-corrected") || !row.resolved) {
        results.push({
          name: row.rawName, status: "skipped",
          reason: row.status === "parse-error" ? "Could not parse date range"
                : row.status === "suggest"      ? "Needs selection (Did you mean?)"
                : row.status === "ambiguous"    ? "Ambiguous name"
                : "Name not in system",
        })
        continue
      }
      const key = `${row.resolved.employeeId}|${row.firstDay}|${row.lastDay}`
      if (seen.has(key)) {
        results.push({ name: row.resolved.fullName ?? row.rawName, status: "skipped", reason: "Duplicate in paste" })
        continue
      }
      seen.add(key)
      const name = row.resolved.fullName ?? row.rawName
      try {
        const r = await fetch("/api/vacations", {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ employeeId: row.resolved.employeeId, firstDay: row.firstDay, lastDay: row.lastDay, comments: null }),
        })
        const body = r.ok ? null : await r.json().catch(() => ({}))
        results.push(r.ok
          ? { name, status: "saved" }
          : { name, status: "error", reason: (body as any)?.error ?? (body as any)?.message ?? `HTTP ${r.status}` })
      } catch (e) {
        results.push({ name, status: "error", reason: String(e) })
      }
    }

    setSaveResults(results)
    setSaving(false)
    if (results.some(r => r.status === "saved")) {
      setText(""); setRows(null); onSaved()
    }
  }

  const resolvedCount   = rows?.filter(r => r.status === "resolved" || r.status === "resolved-corrected").length ?? 0
  const unresolvedCount = rows ? rows.length - resolvedCount : 0

  return (
    <div>
      {saveResults && (
        <div style={card}>
          <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text)", marginBottom: 10 }}>
            Import complete — {saveResults.filter(r => r.status === "saved").length} saved
            {saveResults.some(r => r.status === "skipped") && `, ${saveResults.filter(r => r.status === "skipped").length} skipped`}
            {saveResults.some(r => r.status === "error") && `, ${saveResults.filter(r => r.status === "error").length} errors`}
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            {saveResults.map((r, i) => (
              <div key={i} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12 }}>
                {r.status === "saved"
                  ? <CheckCircle2 size={13} color="var(--green)" />
                  : r.status === "skipped"
                    ? <AlertCircle size={13} color="#f97316" />
                    : <XCircle size={13} color="#ef4444" />}
                <span style={{ color: "var(--text)", fontWeight: r.status === "saved" ? 500 : 400 }}>{r.name}</span>
                {r.reason && <span style={{ color: r.status === "error" ? "#ef4444" : "#f97316", fontSize: 11 }}>— {r.reason}</span>}
              </div>
            ))}
          </div>
        </div>
      )}

      {error && (
        <div style={{ ...card, background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.3)", fontSize: 12, color: "#ef4444" }}>
          {error}
        </div>
      )}

      <div style={card}>
        <div style={{ fontSize: 11, color: "var(--text3)", marginBottom: 10, fontFamily: "IBM Plex Mono" }}>
          One entry per line:  Agent Name — DD.MM.YY–DD.MM.YY   or   Agent Name DD.MM.YY - DD.MM.YY
        </div>
        <textarea
          value={text}
          onChange={e => { setText(e.target.value); setRows(null); setSaveResults(null); setError(null) }}
          placeholder={"Shelikhov Dmytro — 27.07.26–29.07.26\nNguyen Tim — 28.07.26–31.07.26\nKaratas Ayten 27.07.26 - 14.08.26"}
          rows={14}
          style={{ ...inp, width: "100%", resize: "vertical", fontFamily: "IBM Plex Mono", fontSize: 12, lineHeight: 1.6 }}
        />
        <div style={{ display: "flex", gap: 10, marginTop: 12 }}>
          <button onClick={handleParse} disabled={!text.trim()}
            style={{ ...btnP, opacity: text.trim() ? 1 : 0.5, cursor: text.trim() ? "pointer" : "not-allowed" }}>
            Parse Preview
          </button>
          {text.trim() && (
            <button onClick={() => { setText(""); setRows(null); setSaveResults(null); setError(null) }} style={btnS}>
              Clear
            </button>
          )}
        </div>
      </div>

      {rows && (
        <div style={card}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
              {rows.length} lines ·{" "}
              <span style={{ color: "var(--green)" }}>{resolvedCount} resolved</span>
              {unresolvedCount > 0 && <span style={{ color: "#f97316" }}>, {unresolvedCount} issue{unresolvedCount !== 1 ? "s" : ""}</span>}
            </span>
            <button
              onClick={handleSave}
              disabled={saving || resolvedCount === 0}
              style={{ ...btnP, opacity: saving || resolvedCount === 0 ? 0.5 : 1, cursor: saving || resolvedCount === 0 ? "not-allowed" : "pointer" }}
            >
              {saving ? "Saving…" : `Save ${resolvedCount} Vacation${resolvedCount !== 1 ? "s" : ""}`}
            </button>
          </div>

          {unresolvedCount > 0 && (
            <div style={{
              fontSize: 11, color: "#f97316",
              background: "rgba(249,115,22,.08)", border: "1px solid rgba(249,115,22,.3)",
              borderRadius: 6, padding: "7px 12px", marginBottom: 12,
            }}>
              <strong>Will be skipped:</strong>{" "}
              {rows.filter(r => r.status !== "resolved" && r.status !== "resolved-corrected").map(r => r.rawName || r.rawLine).join(", ")}
            </div>
          )}

          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thS}>#</th>
                  <th style={thS}>Raw Input</th>
                  <th style={thS}>Resolved</th>
                  <th style={thS}>First Day</th>
                  <th style={thS}>Last Day</th>
                  <th style={thS}>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => (
                  <tr key={i} style={{ opacity: row.status === "resolved" || row.status === "resolved-corrected" ? 1 : 0.65 }}>
                    <td style={{ ...tdS, color: "var(--text3)", fontFamily: "IBM Plex Mono", width: 32 }}>{i + 1}</td>
                    <td style={{ ...tdS, fontFamily: "IBM Plex Mono", fontSize: 11 }}>{row.rawName || <em style={{ color: "var(--text3)" }}>—</em>}</td>
                    <td style={tdS}>
                      {(row.status === "resolved" || row.status === "resolved-corrected") && row.resolved ? (
                        <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                          <span style={{ color: "var(--green)", fontWeight: 500 }}>{row.resolved.fullName}</span>
                          {row.status === "resolved-corrected" && (
                            <span style={{ fontSize: 10, color: "#d97706", background: "rgba(217,119,6,.1)", border: "1px solid rgba(217,119,6,.3)", borderRadius: 3, padding: "1px 5px" }}>corrected</span>
                          )}
                        </div>
                      ) : row.status === "suggest" ? (
                        <select defaultValue="" onChange={e => { if (e.target.value) handleSuggestionPick(i, e.target.value) }}
                          style={{ ...inp, fontSize: 11, padding: "4px 8px", minWidth: 200 }}>
                          <option value="">— select —</option>
                          {row.suggestions?.map(s => (
                            <option key={s.employeeId} value={s.employeeId}>{s.fullName} ({s.employeeId})</option>
                          ))}
                        </select>
                      ) : row.status === "ambiguous" ? (
                        <span style={{ color: "#f97316" }}>Ambiguous ({row.ambiguous.length})</span>
                      ) : (
                        <span style={{ color: "#ef4444" }}>—</span>
                      )}
                    </td>
                    <td style={{ ...tdS, fontFamily: "IBM Plex Mono", fontSize: 11, color: row.firstDay ? "var(--text)" : "#ef4444" }}>
                      {row.firstDay || "—"}
                    </td>
                    <td style={{ ...tdS, fontFamily: "IBM Plex Mono", fontSize: 11, color: row.lastDay ? "var(--text)" : "#ef4444" }}>
                      {row.lastDay || "—"}
                    </td>
                    <td style={tdS}>
                      {row.status === "resolved"
                        ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "var(--green)", fontSize: 11 }}><CheckCircle2 size={13} /> Resolved</span>
                        : row.status === "resolved-corrected"
                          ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#d97706", fontSize: 11 }}><CheckCircle2 size={13} /> Resolved (corrected)</span>
                          : row.status === "suggest"
                            ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#f97316", fontSize: 11 }}><AlertCircle size={13} /> Did you mean?</span>
                            : row.status === "ambiguous"
                              ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#f97316", fontSize: 11 }}><AlertCircle size={13} /> Ambiguous</span>
                              : row.status === "parse-error"
                                ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#8892a4", fontSize: 11 }}><XCircle size={13} /> Parse error</span>
                                : <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#ef4444", fontSize: 11 }}><XCircle size={13} /> Name not in system</span>
                      }
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}

// ── Add Vacation Modal ────────────────────────────────────────────────────────

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

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function Vacations() {
  const { t } = useTranslation()
  const qc = useQueryClient()
  const [activeTab, setActiveTab]   = useState<"list" | "import">("list")
  const [sheet, setSheet]           = useState("")
  const [deleteId, setDeleteId]     = useState<number | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [error, setError]           = useState("")
  const [expanded, setExpanded]     = useState<Set<string>>(new Set())

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
        const sorted  = [...periods].sort((a: any, b: any) => a.firstDay.localeCompare(b.firstDay))
        const future  = sorted.filter((p: any) => p.firstDay >= todayStr)
        return {
          empId, name, teamLead,
          totalDays:   periods.reduce((s: number, p: any) => s + (p.workDaysNet ?? 0), 0),
          periodCount: periods.length,
          nextVacation: future[0]?.firstDay ?? null,
          periods: sorted,
        }
      })
      .sort((a, b) =>
        (a.teamLead || "￿").localeCompare(b.teamLead || "￿") || a.name.localeCompare(b.name)
      )
  }, [data, todayStr])

  const toggleExpand = (empId: string) => {
    setExpanded(prev => {
      const next = new Set(prev)
      if (next.has(empId)) next.delete(empId); else next.add(empId)
      return next
    })
  }

  const invalidateAll = () => {
    qc.invalidateQueries({ queryKey: ["vacations"] })
    qc.invalidateQueries({ queryKey: ["vac-active"] })
    qc.invalidateQueries({ queryKey: ["vac-upcoming"] })
    qc.invalidateQueries({ queryKey: ["albalance"] })
  }

  const handleAdd = async (form: any) => {
    try {
      await apiFetch("/api/vacations", {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(form)
      } as any)
      invalidateAll()
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
      invalidateAll()
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

      {/* Header */}
      <div style={{ display:"flex", justifyContent:"space-between", alignItems:"center", flexWrap:"wrap", gap:10 }}>
        <h1 style={{ fontSize:22, fontWeight:600, color:"var(--text)" }}>{t("nav.vacations")}</h1>
        <div style={{ display:"flex", gap:8, alignItems:"center", flexWrap:"wrap" }}>

          {/* Tab toggle */}
          <div style={{ display:"flex", gap:2 }}>
            {(["list", "import"] as const).map(tab => (
              <button key={tab} onClick={() => setActiveTab(tab)} style={{
                background: activeTab === tab ? "var(--accent)" : "var(--card2)",
                border: `1px solid ${activeTab === tab ? "var(--accent)" : "var(--border)"}`,
                color: activeTab === tab ? "#fff" : "var(--text2)",
                padding: "7px 18px", borderRadius: 6, fontSize: 12, fontWeight: 600,
                cursor: "pointer",
              }}>
                {tab === "list" ? "List" : "Bulk Import"}
              </button>
            ))}
          </div>

          {activeTab === "list" && (
            <>
              <button onClick={() => setShowAddModal(true)} style={{
                background:"var(--accent)", border:"none", color:"#fff",
                padding:"8px 16px", borderRadius:6, fontSize:12, cursor:"pointer",
                display:"flex", alignItems:"center", gap:6
              }}>
                <Plus size={14} /> Add Vacation
              </button>
              <DownloadButtons onToday={api.vacations.downloadToday} on7Days={api.vacations.download7} on30Days={api.vacations.download30} />
            </>
          )}
        </div>
      </div>

      {/* Bulk Import tab */}
      {activeTab === "import" && (
        <BulkImportPanel onSaved={() => { invalidateAll(); setActiveTab("list") }} />
      )}

      {/* List tab */}
      {activeTab === "list" && (
        <>
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
        </>
      )}

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
