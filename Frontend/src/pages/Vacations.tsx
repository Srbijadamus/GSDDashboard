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

  const card: React.CSSProperties = { borderRadius: 10, padding: "18px 20px", marginBottom: 16 }
  const cardCls = "bg-raised border border-line-subtle"
  const inp: React.CSSProperties = {
    borderRadius: 6, padding: "7px 10px", fontSize: 12,
    fontFamily: "IBM Plex Sans", outline: "none", boxSizing: "border-box" as const,
  }
  const inpCls = "bg-sunken border border-line-subtle text-ink"
  const btnP: React.CSSProperties = {
    border: "none", color: "#fff",
    padding: "7px 16px", borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: "pointer",
  }
  const btnPCls = "bg-info-solid"
  const btnS: React.CSSProperties = {
    padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
  }
  const btnSCls = "bg-sunken border border-line-subtle text-ink-muted"
  const thS: React.CSSProperties = {
    padding: "8px 12px", textAlign: "left" as const, fontSize: 10, fontWeight: 600,
    textTransform: "uppercase" as const, letterSpacing: "0.06em",
  }
  const thSCls = "text-ink-soft border-b border-line-subtle"
  const tdS: React.CSSProperties = { padding: "8px 12px", fontSize: 12 }
  const tdSCls = "text-ink border-b border-line-subtle"

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
        <div style={card} className={cardCls}>
          <div className="text-ink" style={{ fontSize: 13, fontWeight: 600, marginBottom: 10 }}>
            Import complete — {saveResults.filter(r => r.status === "saved").length} saved
            {saveResults.some(r => r.status === "skipped") && `, ${saveResults.filter(r => r.status === "skipped").length} skipped`}
            {saveResults.some(r => r.status === "error") && `, ${saveResults.filter(r => r.status === "error").length} errors`}
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
            {saveResults.map((r, i) => (
              <div key={i} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 12 }}>
                {r.status === "saved"
                  ? <CheckCircle2 size={13} className="text-good-fg" />
                  : r.status === "skipped"
                    ? <AlertCircle size={13} className="text-warn-fg" />
                    : <XCircle size={13} className="text-crit-fg" />}
                <span className="text-ink" style={{ fontWeight: r.status === "saved" ? 500 : 400 }}>{r.name}</span>
                {r.reason && <span className={r.status === "error" ? "text-crit-fg" : "text-warn-fg"} style={{ fontSize: 11 }}>— {r.reason}</span>}
              </div>
            ))}
          </div>
        </div>
      )}

      {error && (
        <div style={{ ...card, fontSize: 12 }} className="bg-crit-bg border border-crit-bd text-crit-fg">
          {error}
        </div>
      )}

      <div style={card} className={cardCls}>
        <div className="font-mono text-ink-soft" style={{ fontSize: 11, marginBottom: 10 }}>
          One entry per line:  Agent Name — DD.MM.YY–DD.MM.YY   or   Agent Name DD.MM.YY - DD.MM.YY
        </div>
        <textarea
          value={text}
          onChange={e => { setText(e.target.value); setRows(null); setSaveResults(null); setError(null) }}
          placeholder={"Shelikhov Dmytro — 27.07.26–29.07.26\nNguyen Tim — 28.07.26–31.07.26\nKaratas Ayten 27.07.26 - 14.08.26"}
          rows={14}
          className={`font-mono ${inpCls}`}
          style={{ ...inp, width: "100%", resize: "vertical", fontSize: 12, lineHeight: 1.6 }}
        />
        <div style={{ display: "flex", gap: 10, marginTop: 12 }}>
          <button onClick={handleParse} disabled={!text.trim()} className={btnPCls}
            style={{ ...btnP, opacity: text.trim() ? 1 : 0.5, cursor: text.trim() ? "pointer" : "not-allowed" }}>
            Parse Preview
          </button>
          {text.trim() && (
            <button onClick={() => { setText(""); setRows(null); setSaveResults(null); setError(null) }} style={btnS} className={btnSCls}>
              Clear
            </button>
          )}
        </div>
      </div>

      {rows && (
        <div style={card} className={cardCls}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <span style={{ fontSize: 13, fontWeight: 600 }} className="text-ink">
              {rows.length} lines ·{" "}
              <span className="text-good-fg">{resolvedCount} resolved</span>
              {unresolvedCount > 0 && <span className="text-warn-fg">, {unresolvedCount} issue{unresolvedCount !== 1 ? "s" : ""}</span>}
            </span>
            <button
              onClick={handleSave}
              disabled={saving || resolvedCount === 0}
              className={btnPCls}
              style={{ ...btnP, opacity: saving || resolvedCount === 0 ? 0.5 : 1, cursor: saving || resolvedCount === 0 ? "not-allowed" : "pointer" }}
            >
              {saving ? "Saving…" : `Save ${resolvedCount} Vacation${resolvedCount !== 1 ? "s" : ""}`}
            </button>
          </div>

          {unresolvedCount > 0 && (
            <div className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ fontSize: 11, borderRadius: 6, padding: "7px 12px", marginBottom: 12 }}>
              <strong>Will be skipped:</strong>{" "}
              {rows.filter(r => r.status !== "resolved" && r.status !== "resolved-corrected").map(r => r.rawName || r.rawLine).join(", ")}
            </div>
          )}

          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thS} className={thSCls}>#</th>
                  <th style={thS} className={thSCls}>Raw Input</th>
                  <th style={thS} className={thSCls}>Resolved</th>
                  <th style={thS} className={thSCls}>First Day</th>
                  <th style={thS} className={thSCls}>Last Day</th>
                  <th style={thS} className={thSCls}>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => (
                  <tr key={i} style={{ opacity: row.status === "resolved" || row.status === "resolved-corrected" ? 1 : 0.65 }}>
                    <td style={{ ...tdS, width: 32 }} className={`font-mono text-ink-soft ${tdSCls}`}>{i + 1}</td>
                    <td style={{ ...tdS, fontSize: 11 }} className={`font-mono ${tdSCls}`}>{row.rawName || <em className="text-ink-soft">—</em>}</td>
                    <td style={tdS} className={tdSCls}>
                      {(row.status === "resolved" || row.status === "resolved-corrected") && row.resolved ? (
                        <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                          <span className="text-good-fg" style={{ fontWeight: 500 }}>{row.resolved.fullName}</span>
                          {row.status === "resolved-corrected" && (
                            <span className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ fontSize: 10, borderRadius: 3, padding: "1px 5px" }}>corrected</span>
                          )}
                        </div>
                      ) : row.status === "suggest" ? (
                        <select defaultValue="" onChange={e => { if (e.target.value) handleSuggestionPick(i, e.target.value) }}
                          className={inpCls}
                          style={{ ...inp, fontSize: 11, padding: "4px 8px", minWidth: 200 }}>
                          <option value="">— select —</option>
                          {row.suggestions?.map(s => (
                            <option key={s.employeeId} value={s.employeeId}>{s.fullName} ({s.employeeId})</option>
                          ))}
                        </select>
                      ) : row.status === "ambiguous" ? (
                        <span className="text-warn-fg">Ambiguous ({row.ambiguous.length})</span>
                      ) : (
                        <span className="text-crit-fg">—</span>
                      )}
                    </td>
                    <td style={{ ...tdS, fontSize: 11 }} className={`font-mono ${tdSCls} ${row.firstDay ? "text-ink" : "text-crit-fg"}`}>
                      {row.firstDay || "—"}
                    </td>
                    <td style={{ ...tdS, fontSize: 11 }} className={`font-mono ${tdSCls} ${row.lastDay ? "text-ink" : "text-crit-fg"}`}>
                      {row.lastDay || "—"}
                    </td>
                    <td style={tdS} className={tdSCls}>
                      {row.status === "resolved"
                        ? <span className="text-good-fg" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><CheckCircle2 size={13} /> Resolved</span>
                        : row.status === "resolved-corrected"
                          ? <span className="text-warn-fg" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><CheckCircle2 size={13} /> Resolved (corrected)</span>
                          : row.status === "suggest"
                            ? <span className="text-warn-fg" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><AlertCircle size={13} /> Did you mean?</span>
                            : row.status === "ambiguous"
                              ? <span className="text-warn-fg" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><AlertCircle size={13} /> Ambiguous</span>
                              : row.status === "parse-error"
                                ? <span className="text-ink-soft" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><XCircle size={13} /> Parse error</span>
                                : <span className="text-crit-fg" style={{ display: "flex", alignItems: "center", gap: 4, fontSize: 11 }}><XCircle size={13} /> Name not in system</span>
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
  width: "100%", padding: "7px 10px", borderRadius: 6, fontSize: 12,
  outline: "none", fontFamily: "IBM Plex Sans", boxSizing: "border-box",
}
const modalInputCls = "bg-sunken border border-line-subtle text-ink"

export function AddVacationModal({
  onClose, onSave, initialEmployeeId, initialDate,
}: {
  onClose: () => void
  onSave: (data: any) => void
  initialEmployeeId?: string
  initialDate?: string
}) {
  const { t } = useTranslation()
  const today = new Date().toISOString().slice(0, 10)
  const [employeeId, setEmployeeId] = useState(initialEmployeeId ?? "")
  const [firstDay, setFirstDay]     = useState(initialDate ?? today)
  const [lastDay, setLastDay]       = useState(initialDate ?? today)
  const [comments, setComments]     = useState("")
  const [isHalfDay, setIsHalfDay]   = useState(false)
  const [error, setError]           = useState("")

  const { data: employees = [] } = useQuery<EmployeeOption[]>({
    queryKey: ["employees-active"],
    queryFn: () => fetch("/api/employees/?active=true").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60 * 1000,
  })

  const handleFirstDayChange = (val: string) => {
    setFirstDay(val)
    if (isHalfDay) setLastDay(val)
  }

  const handleHalfDayToggle = (half: boolean) => {
    setIsHalfDay(half)
    if (half) setLastDay(firstDay)
  }

  const handleSubmit = () => {
    if (!employeeId) { setError("Select an employee"); return }
    if (!isHalfDay && lastDay < firstDay) { setError("To date must be on or after From date"); return }
    onSave({ employeeId, firstDay, lastDay: isHalfDay ? firstDay : lastDay, comments: comments || null, isHalfDay })
  }

  return (
    <div style={{
      position: "fixed", inset: 0, background: "rgba(0,0,0,.7)", zIndex: 1000,
      display: "flex", alignItems: "center", justifyContent: "center",
    }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="bg-raised border border-line-subtle" style={{ borderRadius: 10, padding: 24, width: 420 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 18 }}>
          <h2 className="text-ink" style={{ fontSize: 16, fontWeight: 600 }}>Add Vacation (AL)</h2>
          <button onClick={onClose} className="text-ink-soft" style={{ background: "none", border: "none", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label className="text-ink-soft" style={{ fontSize: 11, marginBottom: 4, display: "block" }}>Employee *</label>
            <select value={employeeId} onChange={e => setEmployeeId(e.target.value)} style={modalInputStyle} className={modalInputCls}>
              <option value="">-- Select employee --</option>
              {employees.map(e => (
                <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
              ))}
            </select>
          </div>

          {/* Full / Half day toggle */}
          <div style={{ display: "flex", gap: 6 }}>
            {[false, true].map(half => (
              <button
                key={String(half)}
                onClick={() => handleHalfDayToggle(half)}
                className={isHalfDay === half ? "bg-info-solid border border-info-bd text-white" : "bg-sunken border border-line-subtle text-ink-muted"}
                style={{ flex: 1, padding: "6px 0", borderRadius: 6, fontSize: 12, fontWeight: 500, cursor: "pointer" }}
              >
                {half ? t("vacations.halfDay") : t("vacations.fullDay")}
              </button>
            ))}
          </div>

          <div style={{ display: "flex", gap: 10 }}>
            <div style={{ flex: 1 }}>
              <label className="text-ink-soft" style={{ fontSize: 11, marginBottom: 4, display: "block" }}>From *</label>
              <input type="date" value={firstDay} max={maxFutureDateStr()} onChange={e => handleFirstDayChange(e.target.value)} style={modalInputStyle} className={modalInputCls} />
            </div>
            {!isHalfDay && (
              <div style={{ flex: 1 }}>
                <label className="text-ink-soft" style={{ fontSize: 11, marginBottom: 4, display: "block" }}>To *</label>
                <input type="date" value={lastDay} max={maxFutureDateStr()} onChange={e => setLastDay(e.target.value)} style={modalInputStyle} className={modalInputCls} />
              </div>
            )}
          </div>
          <div>
            <label className="text-ink-soft" style={{ fontSize: 11, marginBottom: 4, display: "block" }}>Comments</label>
            <input value={comments} onChange={e => setComments(e.target.value)} style={modalInputStyle} className={modalInputCls} placeholder="Optional" />
          </div>
          {error && <div className="text-crit-fg" style={{ fontSize: 11 }}>{error}</div>}
        </div>

        <div style={{ display: "flex", gap: 8, marginTop: 20, justifyContent: "flex-end" }}>
          <button onClick={onClose} className="bg-sunken border border-line-subtle text-ink-muted" style={{
            padding: "8px 16px", borderRadius: 6, fontSize: 12, cursor: "pointer",
          }}>Cancel</button>
          <button onClick={handleSubmit} className="bg-info-solid" style={{
            border: "none", color: "#fff",
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

// ── AL History Drawer ─────────────────────────────────────────────────────────

export function ALHistoryDrawer({ employeeId, name, onClose }: { employeeId: string; name: string; onClose: () => void }) {
  const { t } = useTranslation()

  const { data, isLoading } = useQuery<any[]>({
    queryKey: ["vac-history", employeeId],
    queryFn: () => apiFetch(`/api/vacations?employeeId=${encodeURIComponent(employeeId)}&year=2026`) as Promise<any[]>,
    staleTime: 60_000,
  })

  const sorted = data ? [...data].sort((a, b) => b.firstDay.localeCompare(a.firstDay)) : []

  return (
    <div style={{
      position: "fixed", inset: 0, background: "rgba(0,0,0,.6)", zIndex: 1100,
      display: "flex", justifyContent: "flex-end",
    }} onClick={e => e.target === e.currentTarget && onClose()}>
      <div className="bg-raised border-l border-line-subtle" style={{
        width: 460, display: "flex", flexDirection: "column", overflowY: "auto",
      }}>
        <div className="border-b border-line-subtle" style={{ padding: "18px 20px", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div className="text-ink" style={{ fontSize: 14, fontWeight: 600 }}>{t("vacations.alHistory")}</div>
            <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 2 }}>{name}</div>
          </div>
          <button onClick={onClose} className="text-ink-soft" style={{ background: "none", border: "none", cursor: "pointer" }}>
            <X size={18} />
          </button>
        </div>

        <div style={{ padding: "14px 20px", flex: 1 }}>
          {isLoading && Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="skeleton" style={{ height: 38, borderRadius: 6, marginBottom: 8 }} />
          ))}
          {!isLoading && sorted.length === 0 && (
            <div className="text-ink-soft" style={{ fontSize: 12, textAlign: "center", paddingTop: 24 }}>
              {t("vacations.alHistoryEmpty")}
            </div>
          )}
          {!isLoading && sorted.map((v: any) => (
            <div key={v.id} className="bg-sunken border border-line-subtle" style={{
              padding: "10px 12px", borderRadius: 7, marginBottom: 8,
            }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <span className="font-mono text-info-fg" style={{ fontSize: 11 }}>
                  {v.firstDay} → {v.lastDay}
                </span>
                <span className="font-mono text-good-fg" style={{ fontSize: 12, fontWeight: 600 }}>
                  {v.workDaysNet}d
                </span>
              </div>
              {v.comments && (
                <div className="text-ink-soft" style={{ fontSize: 11, marginTop: 4, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {v.comments}
                </div>
              )}
            </div>
          ))}
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
  const [historyAgent, setHistoryAgent] = useState<{ id: string; name: string } | null>(null)
  const [nameSearch, setNameSearch] = useState("")
  const [sortKey, setSortKey] = useState<"name" | "teamLead" | "periodCount" | "totalDays" | "nextVacation" | null>(null)
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc")

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

  const toggleSort = (key: typeof sortKey) => {
    if (!key) return
    if (sortKey === key) setSortDir(d => d === "asc" ? "desc" : "asc")
    else { setSortKey(key); setSortDir("asc") }
  }

  const visibleGrouped = useMemo(() => {
    const filtered = nameSearch
      ? grouped.filter(g => g.name.toLowerCase().includes(nameSearch.toLowerCase()))
      : grouped
    if (!sortKey) return filtered
    const sorted = [...filtered].sort((a, b) => {
      const va = a[sortKey], vb = b[sortKey]
      const cmp = typeof va === "number" && typeof vb === "number"
        ? va - vb
        : String(va ?? "").localeCompare(String(vb ?? ""))
      return sortDir === "asc" ? cmp : -cmp
    })
    return sorted
  }, [grouped, nameSearch, sortKey, sortDir])

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
        <h1 className="text-ink" style={{ fontSize:22, fontWeight:600 }}>{t("nav.vacations")}</h1>
        <div style={{ display:"flex", gap:8, alignItems:"center", flexWrap:"wrap" }}>

          {/* Tab toggle */}
          <div style={{ display:"flex", gap:2 }}>
            {(["list", "import"] as const).map(tab => (
              <button key={tab} onClick={() => setActiveTab(tab)}
                className={activeTab === tab ? "bg-info-solid border border-info-bd text-white" : "bg-sunken border border-line-subtle text-ink-muted"}
                style={{ padding: "7px 18px", borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: "pointer" }}>
                {tab === "list" ? "List" : "Bulk Import"}
              </button>
            ))}
          </div>

          {activeTab === "list" && (
            <>
              <button onClick={() => setShowAddModal(true)} className="bg-info-solid" style={{
                border:"none", color:"#fff",
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
            <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius:6, padding:"8px 14px", fontSize:12 }}>
              ❌ {error}
            </div>
          )}

          <div style={{ display:"grid", gridTemplateColumns:"repeat(3, 1fr)", gap:12 }}>
            {[
              { label:"On AL Today",     value:(active as any[])?.length ?? 0, colorCls:"text-good-fg" },
              { label:"Upcoming 7 Days", value:(upcoming as any[])?.length ?? 0, colorCls:"text-info-fg" },
              { label:"Employees",       value:grouped.length,                  colorCls:"text-ink" },
            ].map(s => (
              <div key={s.label} className="bg-raised border border-line-subtle" style={{ borderRadius:8, padding:"16px 20px" }}>
                <div className="text-ink-soft" style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".08em", marginBottom:6 }}>{s.label}</div>
                <div className={`font-mono ${s.colorCls}`} style={{ fontSize:28, fontWeight:600 }}>{s.value}</div>
              </div>
            ))}
          </div>

          {upcoming && (upcoming as any[]).length > 0 && (
            <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, padding:"14px 16px" }}>
              <div className="text-ink-soft" style={{ fontSize:10, textTransform:"uppercase", letterSpacing:".07em", marginBottom:10 }}>
                Starting next 7 days
              </div>
              {(upcoming as any[]).map((v: any) => (
                <div key={v.id} className="border-b border-line-subtle" style={{ display:"flex", justifyContent:"space-between", alignItems:"center",
                  padding:"5px 0", fontSize:12 }}>
                  <span className="font-mono text-ink-soft" style={{ fontSize:11 }}>{v.employeeId}</span>
                  <span className="text-ink-muted">{v.firstName} {v.lastName}</span>
                  <span className="font-mono text-info-fg" style={{ fontSize:11 }}>{v.firstDay} → {v.lastDay}</span>
                  <span className="font-mono text-ink-soft" style={{ fontSize:11 }}>{v.workDaysNet}d</span>
                </div>
              ))}
            </div>
          )}

          <div style={{ display:"flex", gap:10 }}>
            <select value={sheet} onChange={e => setSheet(e.target.value)}
              className="bg-raised border border-line-subtle text-ink-muted"
              style={{ padding:"7px 12px", borderRadius:6, fontSize:12 }}>
              <option value="">All</option>
              <option value="Agents">Agents</option>
              <option value="Overhead">Overhead</option>
            </select>
            <input placeholder="Search agent..." value={nameSearch} onChange={e => setNameSearch(e.target.value)}
              style={{ padding: "7px 12px", borderRadius: 6, fontSize: 12, outline: "none", width: 200 }}
              className="bg-raised border border-line-subtle text-ink" />
          </div>

          <div className="bg-raised border border-line-subtle" style={{ borderRadius:8, overflow:"hidden" }}>
            <div className="bg-sunken border-b border-line-subtle text-ink-soft" style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
              padding:"9px 12px",
              fontSize:10, fontWeight:500, textTransform:"uppercase", letterSpacing:".07em" }}>
              <div/>
              {([
                { label: "Name", key: "name" as const },
                { label: "Team Lead", key: "teamLead" as const },
                { label: "Periods", key: "periodCount" as const, center: true },
                { label: "Days", key: "totalDays" as const, center: true },
                { label: "Next Vacation", key: "nextVacation" as const },
              ]).map(col => (
                <div key={col.label} onClick={() => toggleSort(col.key)}
                  style={{ cursor: "pointer", userSelect: "none", textAlign: col.center ? "center" : "left" }}>
                  {col.label}{sortKey === col.key && (sortDir === "asc" ? " ▲" : " ▼")}
                </div>
              ))}
            </div>

            {isLoading && Array.from({length: 8}).map((_, i) => (
              <div key={i} className="border-b border-line-subtle" style={{ padding:"12px 12px" }}>
                <div className="skeleton" style={{ height:11, width:"55%" }}/>
              </div>
            ))}

            {visibleGrouped.map(grp => {
              const isOpen = expanded.has(grp.empId)
              return (
                <div key={grp.empId} className="border-b border-line-subtle">
                  <div
                    onClick={() => toggleExpand(grp.empId)}
                    className={`hover:bg-hovered ${isOpen ? "bg-raised" : ""}`}
                    style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
                      padding:"10px 12px", cursor:"pointer", alignItems:"center",
                      transition:"background 0.1s" }}>
                    <div className="text-ink-soft" style={{ display:"flex", alignItems:"center" }}>
                      {isOpen ? <ChevronDown size={14}/> : <ChevronRight size={14}/>}
                    </div>
                    <div
                      className="text-ink"
                      style={{ fontSize:13, fontWeight:500, cursor:"pointer", textDecoration:"underline dotted", textUnderlineOffset:3 }}
                      onClick={e => { e.stopPropagation(); setHistoryAgent({ id: grp.empId, name: grp.name }) }}
                    >{grp.name}</div>
                    <div className="text-ink-soft" style={{ fontSize:11 }}>{grp.teamLead}</div>
                    <div className="font-mono text-ink-muted" style={{ textAlign:"center", fontSize:11 }}>
                      {grp.periodCount}
                    </div>
                    <div className="font-mono text-good-fg" style={{ textAlign:"center", fontSize:12, fontWeight:600 }}>
                      {grp.totalDays}d
                    </div>
                    <div className={`font-mono ${grp.nextVacation ? "text-info-fg" : "text-ink-soft"}`} style={{ fontSize:11 }}>
                      {grp.nextVacation ?? "—"}
                    </div>
                  </div>

                  {isOpen && (
                    <div className="border-t border-line-subtle bg-sunken">
                      {grp.periods.map((p: any) => (
                        <div key={p.id}
                          style={{ display:"grid", gridTemplateColumns:"28px 1fr 160px 70px 70px 140px",
                            padding:"7px 12px", alignItems:"center",
                            borderBottom:"1px solid rgb(var(--line-subtle))", fontSize:11 }}>
                          <div/>
                          <div className="font-mono text-ink-muted" style={{ fontSize:11 }}>
                            {p.firstDay} → {p.lastDay}
                          </div>
                          <div>
                            {p.isOverhead
                              ? <span className="bg-learn-bg text-learn-fg px-1.5 py-px rounded text-2xs">Overhead</span>
                              : <span className="bg-good-bg text-good-fg px-1.5 py-px rounded text-2xs">Agent</span>}
                          </div>
                          <div/>
                          <div className="font-mono text-ink-soft" style={{ textAlign:"center" }}>
                            {p.workDaysNet ?? "—"}d
                          </div>
                          <div style={{ display:"flex", alignItems:"center", justifyContent:"space-between", gap:6 }}>
                            <span className="text-ink-soft" style={{ fontSize:10, overflow:"hidden", textOverflow:"ellipsis", whiteSpace:"nowrap" }}>
                              {p.comments ?? ""}
                            </span>
                            <button onClick={e => { e.stopPropagation(); setDeleteId(p.id) }} className="bg-crit-bg border border-crit-bd text-crit-fg" style={{
                              padding:"3px 7px", borderRadius:4,
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

            <div className="font-mono text-ink-soft border-t border-line-subtle" style={{ padding:"8px 12px", fontSize:11 }}>
              {visibleGrouped.length} employees{nameSearch ? ` (of ${grouped.length})` : ""} · {(data as any[])?.length ?? 0} periods
            </div>
          </div>
        </>
      )}

      {showAddModal && <AddVacationModal onClose={() => { setShowAddModal(false); setError("") }} onSave={handleAdd} />}
      {historyAgent && <ALHistoryDrawer employeeId={historyAgent.id} name={historyAgent.name} onClose={() => setHistoryAgent(null)} />}

      {deleteId && deleteVac && (
        <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:1000,
          display:"flex", alignItems:"center", justifyContent:"center" }}>
          <div className="bg-raised border border-crit-bd" style={{ borderRadius:10, padding:24, width:400 }}>
            <h2 className="text-crit-fg" style={{ fontSize:15, fontWeight:600, marginBottom:12 }}>Delete Vacation</h2>
            <p className="text-ink-muted" style={{ fontSize:13, marginBottom:8 }}>
              Delete vacation for <strong className="text-ink">{deleteVac.firstName} {deleteVac.lastName}</strong>?
            </p>
            <p className="text-ink-soft" style={{ fontSize:12, marginBottom:16 }}>
              {deleteVac.firstDay} → {deleteVac.lastDay} ({deleteVac.workDaysNet} days)
            </p>
            {deleteVac.workDaysNet > 0 && (
              <div className="bg-good-solid/10 border border-good-solid/20 text-good-fg" style={{ borderRadius:6, padding:"8px 12px", marginBottom:14, fontSize:12 }}>
                ✅ {deleteVac.workDaysNet} AL days will be restored to balance
              </div>
            )}
            <div style={{ display:"flex", gap:8, justifyContent:"flex-end" }}>
              <button onClick={() => setDeleteId(null)} className="bg-sunken border border-line-subtle text-ink-muted" style={{
                padding:"7px 14px", borderRadius:6, fontSize:12, cursor:"pointer" }}>Cancel</button>
              <button onClick={handleDelete} className="bg-crit-solid" style={{
                border:"none", color:"#fff",
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
