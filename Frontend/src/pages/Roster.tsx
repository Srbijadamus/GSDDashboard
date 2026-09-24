import { useMemo, useState } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useSearchParams } from "react-router-dom"
import { CalendarPlus, Trash2, Eye, Play, CheckCircle2, AlertCircle } from "lucide-react"
import { useAuth } from "../auth/AuthContext"

// ── Types (mirror the records in Backend/RosterService.cs) ────────────────────

interface Employee {
  employeeId: string
  fullName: string | null
}

interface PreviewResult {
  employeeId: string
  fullName: string | null
  rowCount: number
  firstDate: string | null
  lastDate: string | null
  skippedWeekends: number
  skippedHolidays: { date: string; name: string }[]
  collisions: { date: string; shiftType: string; sourceModule: string | null }[]
  rows: { date: string; shiftStart: string; shiftEnd: string; kind: string }[]
  wicDays: number
  boDays: number
  locationCode: string | null
  locationName: string | null
  replaceCount: number
  replacements: {
    date: string; existingShiftType: string; existingSourceModule: string | null
    kind: string; shiftStart: string; shiftEnd: string
  }[]
}

interface GenerateResult {
  batchId: number
  employeeId: string
  created: number
  skippedExisting: number
  firstDate: string | null
  lastDate: string | null
  wicDays: number
  boDays: number
  assignmentCreated: boolean
  replaced: number
}

interface DeleteResult {
  batchId: number
  deletedRows: number
  detachedRows: number
  wicRowsRemoved: number
  restoredRows: number
  restoredWicRows: number
  restoreSkipped: number
}

interface Batch {
  id: number
  employeeId: string
  fullName: string | null
  dateFrom: string
  dateTo: string
  shiftStart: string
  shiftEnd: string
  workingDays: string
  agentTask: string | null
  locationCode: string | null
  locationName: string | null
  rowCount: number
  skippedExisting: number
  skippedHolidays: number
  replacedExisting: number
  hasSnapshot: boolean
  createdByKid: string | null
  createdByName: string | null
  createdAt: string
}

interface Location {
  locationCode: string
  displayName: string
  city: string | null
  country: string | null
  openingDays: number[]
  openingLabel: string
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const DAYS = [
  { d: 1, label: "Mon" }, { d: 2, label: "Tue" }, { d: 3, label: "Wed" },
  { d: 4, label: "Thu" }, { d: 5, label: "Fri" }, { d: 6, label: "Sat" },
  { d: 0, label: "Sun" },
]

const DAY_SHORT: Record<number, string> = { 0: "Su", 1: "Mo", 2: "Tu", 3: "We", 4: "Th", 5: "Fr", 6: "Sa" }

function daysLabel(csv: string): string {
  return csv.split(",").map(s => DAY_SHORT[Number(s)] ?? s).join(" ")
}

async function errorMessage(res: Response): Promise<string> {
  let msg = `API ${res.status}`
  try { const b = await res.json(); if (b?.error) msg = b.error } catch { /* keep default */ }
  return msg
}

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await errorMessage(res))
  return res.json()
}

// ── Style tokens ─────────────────────────────────────────────────────────────

const card = "bg-raised border border-line-subtle rounded-[10px] py-[18px] px-5 mb-4"
const inputCls = "bg-sunken border border-line-subtle rounded-sm py-[7px] px-[10px] text-ink text-xs box-border outline-none"
const btnPrimary = "bg-info-solid border-none text-white py-[7px] px-4 rounded-sm text-xs font-semibold cursor-pointer"
const btnSecondary = "bg-sunken border border-line-subtle text-ink-muted py-[7px] px-[14px] rounded-sm text-xs cursor-pointer"
const thCls = "py-2 px-3 text-left text-[10px] font-semibold text-ink-soft uppercase tracking-[0.06em] border-b border-line-subtle"
const tdCls = "py-2 px-3 text-xs text-ink border-b border-line-subtle"
const labelCls = "block text-[10px] font-semibold text-ink-soft uppercase tracking-[0.06em] mb-1"

// ── Page (role gate here is cosmetic; the server enforces it) ────────────────

export default function Roster() {
  const { session } = useAuth()
  const allowed = session != null && ["RTM", "TEAM_LEAD", "DEV"].includes(session.role)

  if (!allowed) {
    return (
      <div style={{ maxWidth: 1100 }}>
        <div className={card}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <AlertCircle size={16} className="text-crit-fg" />
            <span className="text-sm text-ink">No access. The roster generator is available to RTM, Team Lead and DEV roles only.</span>
          </div>
        </div>
      </div>
    )
  }
  return <RosterInner />
}

// ── RosterInner ──────────────────────────────────────────────────────────────

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(await errorMessage(res))
  return res.json()
}

function fmtErr(e: unknown): string {
  const m = e instanceof Error ? e.message : String(e)
  if (m === "Forbidden" || m.includes("403"))
    return "Forbidden (403): your role is not allowed to use the roster generator."
  if (m.includes("401")) return "Authentication required (401): please sign in again."
  return m
}

function RosterInner() {
  const qc = useQueryClient()
  const today = new Date().toISOString().slice(0, 10)
  // Deep-link support: /roster?employeeId=X preselects the employee (used by the
  // missing-roster warning on the Overview page).
  const [searchParams] = useSearchParams()
  const [employeeId, setEmployeeId] = useState(searchParams.get("employeeId") ?? "")
  const [dateFrom, setDateFrom] = useState(today)
  const [dateTo, setDateTo] = useState(today)
  const [shiftStart, setShiftStart] = useState("09:00")
  const [shiftEnd, setShiftEnd] = useState("17:00")
  const [workingDays, setWorkingDays] = useState<number[]>([1, 2, 3, 4, 5])
  const [agentTask, setAgentTask] = useState("")
  const [locationCode, setLocationCode] = useState("")
  const [overwrite, setOverwrite] = useState(false)
  const [preview, setPreview] = useState<PreviewResult | null>(null)
  const [previewKey, setPreviewKey] = useState("")
  const [busy, setBusy] = useState<"preview" | "generate" | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const { data: employees = [] } = useQuery<Employee[]>({
    queryKey: ["employees-active"],
    queryFn: () => getJson<Employee[]>("/api/employees/?active=true"),
    staleTime: 5 * 60_000,
  })
  const sorted = useMemo(
    () => [...employees].sort((a, b) => (a.fullName ?? a.employeeId).localeCompare(b.fullName ?? b.employeeId)),
    [employees]
  )

  const { data: batches = [], error: batchesError } = useQuery<Batch[]>({
    queryKey: ["roster-batches"],
    queryFn: () => getJson<Batch[]>("/api/roster/batches"),
  })

  const { data: locations = [] } = useQuery<Location[]>({
    queryKey: ["roster-locations"],
    queryFn: () => getJson<Location[]>("/api/roster/locations"),
    staleTime: 5 * 60_000,
  })
  const selectedLocation = useMemo(
    () => locations.find(l => l.locationCode === locationCode) ?? null,
    [locations, locationCode]
  )

  // In WIC mode (a location is chosen) the centre's opening days decide which
  // days are WIC duty vs BO — WorkingDays/AgentTask are ignored by the server.
  const requestBody = {
    employeeId, dateFrom, dateTo, shiftStart, shiftEnd,
    workingDays: locationCode ? null : workingDays,
    agentTask: agentTask.trim() || null,
    locationCode: locationCode || null,
    overwrite,
  }
  const formKey = JSON.stringify(requestBody)
  const previewStale = preview != null && previewKey !== formKey
  const canGenerate = preview != null && previewKey === formKey && busy == null &&
    (preview.rowCount > 0 || preview.replaceCount > 0)

  const toggleDay = (d: number) =>
    setWorkingDays(prev => prev.includes(d) ? prev.filter(x => x !== d) : [...prev, d])

  const doPreview = async () => {
    setError(null); setSuccess(null)
    if (!employeeId) { setError("Please select an employee."); return }
    if (!locationCode && workingDays.length === 0) { setError("Select at least one working day."); return }
    setBusy("preview")
    try {
      const p = await postJson<PreviewResult>("/api/roster/preview", requestBody)
      setPreview(p)
      setPreviewKey(formKey)
    } catch (e) {
      setPreview(null); setPreviewKey("")
      setError(fmtErr(e))
    } finally { setBusy(null) }
  }

  const doGenerate = async () => {
    setError(null); setSuccess(null)
    setBusy("generate")
    try {
      const g = await postJson<GenerateResult>("/api/roster/generate", requestBody)
      setSuccess(`Batch #${g.batchId} created: ${g.created} new rows for ${g.employeeId}` +
        ` (${g.firstDate} → ${g.lastDate})` +
        (g.replaced > 0
          ? `, ${g.replaced} existing row(s) replaced — deleting the batch restores them`
          : "") +
        `, ${g.skippedExisting} existing day(s) skipped.` +
        (g.wicDays > 0 || g.boDays > 0 ? ` WIC days: ${g.wicDays}, BO days: ${g.boDays}.` : ""))
      setPreview(null); setPreviewKey("")
      qc.invalidateQueries({ queryKey: ["roster-batches"] })
    } catch (e) { setError(fmtErr(e)) }
    finally { setBusy(null) }
  }

  const doDelete = async (b: Batch) => {
    const restoreNote = b.replacedExisting > 0
      ? (b.hasSnapshot
          ? `\n\nThis batch replaced ${b.replacedExisting} existing row(s). Deleting it restores those rows — unless something else has since written to that date (that is reported, never overwritten).`
          : `\n\nThis batch replaced ${b.replacedExisting} existing row(s), but no snapshot is stored — deleting it will NOT restore them.`)
      : ""
    if (!window.confirm(`Delete batch #${b.id} and all of its roster rows?${restoreNote}`)) return
    setError(null); setSuccess(null)
    try {
      const res = await fetch(`/api/roster/batches/${b.id}`, { method: "DELETE" })
      if (!res.ok) throw new Error(await errorMessage(res))
      const d: DeleteResult = await res.json()
      setSuccess(`Batch #${d.batchId} deleted: ${d.deletedRows} row(s) removed.` +
        (d.restoredRows > 0 ? ` Restored ${d.restoredRows} replaced row(s).` : "") +
        (d.restoreSkipped > 0
          ? ` ${d.restoreSkipped} replaced row(s) could NOT be restored — another entry exists on that date now.`
          : ""))
      qc.invalidateQueries({ queryKey: ["roster-batches"] })
    } catch (e) { setError(fmtErr(e)) }
  }

  return (
    <div style={{ maxWidth: 1100 }}>
      {/* ── Generator form ── */}
      <div className={card}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
          <CalendarPlus size={16} className="text-info-solid" />
          <span className="text-sm font-semibold text-ink">Roster generator</span>
        </div>
        <div style={{ display: "flex", flexWrap: "wrap", gap: 14, alignItems: "flex-end" }}>
          <div>
            <label className={labelCls}>Employee</label>
            <select className={inputCls} style={{ minWidth: 220 }} value={employeeId}
              onChange={e => setEmployeeId(e.target.value)}>
              <option value="">— select —</option>
              {sorted.map(emp => (
                <option key={emp.employeeId} value={emp.employeeId}>
                  {emp.fullName ?? emp.employeeId}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className={labelCls}>From</label>
            <input type="date" className={inputCls} value={dateFrom}
              onChange={e => setDateFrom(e.target.value)} />
          </div>
          <div>
            <label className={labelCls}>To</label>
            <input type="date" className={inputCls} value={dateTo}
              onChange={e => setDateTo(e.target.value)} />
          </div>
          <div>
            <label className={labelCls}>Shift start</label>
            <input type="time" className={inputCls} value={shiftStart}
              onChange={e => setShiftStart(e.target.value)} />
          </div>
          <div>
            <label className={labelCls}>Shift end</label>
            <input type="time" className={inputCls} value={shiftEnd}
              onChange={e => setShiftEnd(e.target.value)} />
          </div>
          <div>
            <label className={labelCls}>Task (optional)</label>
            <input type="text" className={inputCls} style={{ minWidth: 160 }} value={agentTask}
              placeholder="e.g. BO" onChange={e => setAgentTask(e.target.value)} />
          </div>
          <div>
            <label className={labelCls}>WIC location (optional)</label>
            <select className={inputCls} style={{ minWidth: 220 }} value={locationCode}
              onChange={e => setLocationCode(e.target.value)}>
              <option value="">— none (plain roster) —</option>
              {locations.map(l => (
                <option key={l.locationCode} value={l.locationCode}>
                  {l.displayName}{l.city ? ` (${l.city})` : ""}
                </option>
              ))}
            </select>
          </div>
          {selectedLocation && (
            <div>
              <label className={labelCls}>Opening days</label>
              <div className="text-xs text-ink-muted" style={{ padding: "8px 0" }}>
                {selectedLocation.openingLabel}
              </div>
            </div>
          )}
        </div>
        {locationCode === "" ? (
          <div style={{ marginTop: 12 }}>
            <label className={labelCls}>Working days</label>
            <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
              {DAYS.map(({ d, label }) => (
                <label key={d} className="text-xs text-ink"
                  style={{ display: "flex", alignItems: "center", gap: 4, cursor: "pointer" }}>
                  <input type="checkbox" checked={workingDays.includes(d)} onChange={() => toggleDay(d)} />
                  {label}
                </label>
              ))}
            </div>
          </div>
        ) : (
          <div style={{ marginTop: 12 }} className="text-[11px] text-ink-muted">
            Working days are decided by the centre's opening days
            {selectedLocation ? ` (${selectedLocation.openingLabel})` : ""}:
            days the centre is open become WIC duty, the other weekdays become BO.
          </div>
        )}
        <div style={{ marginTop: 12 }}>
          <label className="text-xs text-ink"
            style={{ display: "flex", alignItems: "center", gap: 6, cursor: "pointer", maxWidth: 640 }}>
            <input type="checkbox" checked={overwrite} onChange={e => setOverwrite(e.target.checked)} />
            <span>
              <strong>Overwrite existing entries</strong> — WORKING, BO, WIC_DUTY and empty rows on
              existing dates are replaced by the generated row. Absences (AL, HALF_AL, SL, UL, OL,
              PH, LPH, CD, RESIGNED, OFF, OFF_WEEKEND) are never overwritten. Replaced rows are
              restored if you delete the batch afterwards.
            </span>
          </label>
        </div>
        <div style={{ display: "flex", gap: 8, marginTop: 14 }}>
          <button className={btnSecondary} disabled={busy != null} onClick={doPreview}
            style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <Eye size={13} /> {busy === "preview" ? "Previewing…" : "Preview"}
          </button>
          <button className={btnPrimary} disabled={!canGenerate} onClick={doGenerate}
            style={{ display: "flex", alignItems: "center", gap: 6, opacity: canGenerate ? 1 : 0.5,
              cursor: canGenerate ? "pointer" : "not-allowed" }}>
            <Play size={13} /> {busy === "generate" ? "Generating…" : "Generate"}
          </button>
        </div>
        {preview == null && (
          <div className="text-[11px] text-ink-soft" style={{ marginTop: 8 }}>
            Run a preview first — Generate stays disabled until a preview succeeds.
          </div>
        )}
      </div>

      {/* ── Messages ── */}
      {error && (
        <div className={card}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <AlertCircle size={16} className="text-crit-fg" />
            <span className="text-xs text-crit-fg">{error}</span>
          </div>
        </div>
      )}
      {success && (
        <div className={card}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <CheckCircle2 size={16} className="text-ok-fg" />
            <span className="text-xs text-ink">{success}</span>
          </div>
        </div>
      )}

      {/* ── Preview result ── */}
      {preview && (
        <div className={card}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 10 }}>
            <Eye size={15} className="text-info-solid" />
            <span className="text-sm font-semibold text-ink">
              Preview — {preview.fullName ?? preview.employeeId}
            </span>
            {previewStale && (
              <span className="text-[11px] text-warn-fg">
                Form changed after this preview — run Preview again to enable Generate.
              </span>
            )}
          </div>
          <div style={{ display: "flex", gap: 24, flexWrap: "wrap" }} className="text-xs text-ink">
            <span><strong>{preview.rowCount}</strong> rows to create</span>
            {overwrite && (
              <span className="text-warn-fg"><strong>{preview.replaceCount}</strong> rows to REPLACE</span>
            )}
            <span><strong>{preview.collisions.length}</strong> skipped
              {overwrite ? " (protected absences)" : " (existing entries)"}</span>
            <span><strong>{preview.skippedHolidays.length}</strong> skipped (holidays)</span>
            <span><strong>{preview.skippedWeekends}</strong> skipped (weekend days)</span>
            {preview.locationCode != null && (
              <>
                <span>WIC days: <strong>{preview.wicDays}</strong></span>
                <span>BO days: <strong>{preview.boDays}</strong></span>
                <span>Location: <strong>{preview.locationName ?? preview.locationCode}</strong></span>
              </>
            )}
            <span>First: <strong>{preview.firstDate ?? "—"}</strong></span>
            <span>Last: <strong>{preview.lastDate ?? "—"}</strong></span>
          </div>
          {preview.skippedHolidays.length > 0 && (
            <div className="text-[11px] text-ink-muted" style={{ marginTop: 8 }}>
              Holidays: {preview.skippedHolidays.map(h => `${h.date} (${h.name})`).join(", ")}
            </div>
          )}
          {preview.replacements.length > 0 && (
            <div className="text-[11px] text-warn-fg" style={{ marginTop: 4 }}>
              Will be replaced: {preview.replacements.map(r =>
                `${r.date} (${r.existingShiftType}${r.existingSourceModule ? ", " + r.existingSourceModule : ""} → ${r.kind} ${r.shiftStart}–${r.shiftEnd})`).join(", ")}
            </div>
          )}
          {preview.collisions.length > 0 && (
            <div className="text-[11px] text-ink-muted" style={{ marginTop: 4 }}>
              {overwrite ? "Protected absences (never overwritten): " : "Existing: "}
              {preview.collisions.map(c =>
                `${c.date} (${c.shiftType}${c.sourceModule ? ", " + c.sourceModule : ""})`).join(", ")}
            </div>
          )}
          {preview.rows.length > 0 && (
            <div style={{ overflowX: "auto", overflowY: "auto", maxHeight: 320, marginTop: 12 }}>
              <table style={{ width: "100%", borderCollapse: "collapse" }}>
                <thead>
                  <tr>
                    <th className={thCls}>Date</th>
                    <th className={thCls}>Shift</th>
                    <th className={thCls}>Type</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.rows.map(r => (
                    <tr key={r.date}>
                      <td className={tdCls}>{r.date}</td>
                      <td className={tdCls}>{r.shiftStart}–{r.shiftEnd}</td>
                      <td className={tdCls}>{r.kind}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* ── Batches ── */}
      <div className={card}>
        <div className="text-sm font-semibold text-ink" style={{ marginBottom: 10 }}>Roster batches</div>
        {batchesError && (
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
            <AlertCircle size={14} className="text-crit-fg" />
            <span className="text-xs text-crit-fg">{fmtErr(batchesError)}</span>
          </div>
        )}
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th className={thCls}>#</th>
                <th className={thCls}>Employee</th>
                <th className={thCls}>Range</th>
                <th className={thCls}>Shift</th>
                <th className={thCls}>Days</th>
                <th className={thCls}>Task</th>
                <th className={thCls}>Location</th>
                <th className={thCls}>Rows</th>
                <th className={thCls}>Skipped</th>
                <th className={thCls}>Replaced</th>
                <th className={thCls}>Created</th>
                <th className={thCls}></th>
              </tr>
            </thead>
            <tbody>
              {batches.length === 0 && (
                <tr><td className={tdCls} colSpan={12}>
                  <span className="text-ink-soft">No roster batches yet.</span>
                </td></tr>
              )}
              {batches.map(b => (
                <tr key={b.id}>
                  <td className={tdCls}>{b.id}</td>
                  <td className={tdCls}>{b.fullName ?? b.employeeId}</td>
                  <td className={tdCls}>{b.dateFrom} → {b.dateTo}</td>
                  <td className={tdCls}>{b.shiftStart}–{b.shiftEnd}</td>
                  <td className={tdCls}>{daysLabel(b.workingDays)}</td>
                  <td className={tdCls}>{b.agentTask ?? "—"}</td>
                  <td className={tdCls}>{b.locationName ?? "—"}</td>
                  <td className={tdCls}>{b.rowCount}</td>
                  <td className={tdCls} title="existing / holidays">
                    {b.skippedExisting} / {b.skippedHolidays}
                  </td>
                  <td className={tdCls}
                    title={b.replacedExisting > 0
                      ? (b.hasSnapshot
                          ? "Overwrite batch — deleting it restores the replaced rows"
                          : "Overwrite batch — no snapshot stored; deleting it will NOT restore the replaced rows")
                      : undefined}>
                    {b.replacedExisting > 0 ? b.replacedExisting : "—"}
                  </td>
                  <td className={tdCls}>
                    {b.createdByName ?? b.createdByKid ?? "—"},{" "}
                    {new Date(b.createdAt).toLocaleString()}
                  </td>
                  <td className={tdCls}>
                    <button className={btnSecondary} onClick={() => doDelete(b)}
                      title="Delete batch and its rows"
                      style={{ display: "flex", alignItems: "center", gap: 4, padding: "4px 8px" }}>
                      <Trash2 size={12} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
