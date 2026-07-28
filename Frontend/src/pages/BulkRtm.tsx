import { useState, useCallback } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { FileText, Plus, Pencil, Trash2, CheckCircle2, XCircle, AlertCircle } from "lucide-react"

// ── Types ────────────────────────────────────────────────────────────────────

interface Employee {
  employeeId: string
  fullName: string | null
  isActive: boolean
}

interface RtmEntry {
  id: number
  employeeId: string
  fullName: string | null
  shiftStart: string
  shiftEnd: string
  tag: string | null
  sourceLine: string | null
  createdAt: string
}

interface ParsedRow {
  rawLine: string
  rawName: string
  shiftStart: string
  shiftEnd: string
  tag: string | null
  resolved: Employee | null
  ambiguous: Employee[]
  status: "resolved" | "unresolved" | "ambiguous"
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const today = new Date().toISOString().split("T")[0]
const todayLabel = new Date().toLocaleDateString("en-GB", { weekday: "long", year: "numeric", month: "long", day: "numeric" })

function normStr(s: string): string {
  return (s ?? "")
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")  // strip diacritics: ü→u, ö→o, etc.
    .replace(/[-]/g, " ")              // hyphens → spaces (Eva-Liane → Eva Liane)
    .replace(/\s+/g, " ")
    .trim()
}

function resolveEmployee(rawName: string, employees: Employee[]): Pick<ParsedRow, "resolved" | "ambiguous" | "status"> {
  const q = normStr(rawName)
  const words = q.split(" ").filter(w => w.length > 1)

  const exact = employees.filter(e => normStr(e.fullName ?? "") === q)
  if (exact.length === 1) return { resolved: exact[0], ambiguous: [], status: "resolved" }
  if (exact.length > 1)  return { resolved: null, ambiguous: exact, status: "ambiguous" }

  if (words.length < 2) return { resolved: null, ambiguous: [], status: "unresolved" }

  const partial = employees.filter(e => {
    const en = normStr(e.fullName ?? "")
    return words.every(w => en.includes(w))
  })
  if (partial.length === 1) return { resolved: partial[0], ambiguous: [], status: "resolved" }
  if (partial.length > 1)  return { resolved: null, ambiguous: partial, status: "ambiguous" }

  return { resolved: null, ambiguous: [], status: "unresolved" }
}

function parseLine(line: string): { rawName: string; shiftStart: string; shiftEnd: string; tag: string | null } | null {
  const m = line.trim().match(/^(.+?)\s+(\d{1,2}:\d{2})\s*[-–]\s*(\d{1,2}:\d{2})\s*(.*)$/)
  if (!m) return null
  const pad = (t: string) => t.length === 4 ? "0" + t : t
  return {
    rawName:    m[1].trim(),
    shiftStart: pad(m[2]),
    shiftEnd:   pad(m[3]),
    tag:        m[4].trim() || null,
  }
}

// ── Styles ───────────────────────────────────────────────────────────────────

const card: React.CSSProperties = {
  background: "var(--card)", border: "1px solid var(--border)",
  borderRadius: 10, padding: "18px 20px", marginBottom: 16,
}

const inputStyle: React.CSSProperties = {
  background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 6,
  padding: "7px 10px", color: "var(--text)", fontSize: 12, boxSizing: "border-box",
  fontFamily: "IBM Plex Sans", outline: "none",
}

const monoInput: React.CSSProperties = { ...inputStyle, fontFamily: "IBM Plex Mono", width: 90 }

const btnPrimary: React.CSSProperties = {
  background: "var(--accent)", border: "none", color: "#fff",
  padding: "7px 16px", borderRadius: 6, fontSize: 12, fontWeight: 600, cursor: "pointer",
}

const btnSecondary: React.CSSProperties = {
  background: "var(--card2)", border: "1px solid var(--border)", color: "var(--text2)",
  padding: "7px 14px", borderRadius: 6, fontSize: 12, cursor: "pointer",
}

const btnDanger: React.CSSProperties = {
  background: "rgba(239,68,68,.12)", border: "1px solid rgba(239,68,68,.3)",
  color: "#ef4444", padding: "5px 10px", borderRadius: 5, fontSize: 11, cursor: "pointer",
}

const thStyle: React.CSSProperties = {
  padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 600,
  color: "var(--text3)", textTransform: "uppercase", letterSpacing: "0.06em",
  borderBottom: "1px solid var(--border)",
}

const tdStyle: React.CSSProperties = {
  padding: "8px 12px", fontSize: 12, color: "var(--text)", borderBottom: "1px solid var(--border)",
}

// ── Page ─────────────────────────────────────────────────────────────────────

export default function BulkRtm() {
  const qc = useQueryClient()
  const [tab, setTab] = useState<"paste" | "manual">("paste")

  // ── Data queries ──────────────────────────────────────────────────────────
  const { data: todayEntries = [], isLoading: loadingEntries } = useQuery<RtmEntry[]>({
    queryKey: ["rtm-today", today],
    queryFn: () => fetch(`/api/rtm?date=${today}`).then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 30_000,
  })

  const { data: allEmployees = [] } = useQuery<Employee[]>({
    queryKey: ["employees-active"],
    queryFn: () => fetch("/api/employees/?active=true").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60_000,
  })

  const refetch = useCallback(() => qc.invalidateQueries({ queryKey: ["rtm-today", today] }), [qc])

  return (
    <div style={{ maxWidth: 920 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <FileText size={18} color="var(--accent)" />
          <h1 style={{ margin: 0, fontSize: 20, fontWeight: 700, color: "var(--text)" }}>Bulk RTM Entry</h1>
          <span style={{
            fontSize: 11, fontFamily: "IBM Plex Mono", color: "var(--text3)",
            background: "var(--card2)", border: "1px solid var(--border)",
            padding: "3px 9px", borderRadius: 5,
          }}>{todayLabel}</span>
        </div>
        <span style={{
          fontSize: 11, color: "var(--text3)",
          fontFamily: "IBM Plex Mono",
        }}>
          {todayEntries.length} row{todayEntries.length !== 1 ? "s" : ""} saved today
        </span>
      </div>

      {/* Tab selector */}
      <div style={{ display: "flex", gap: 2, marginBottom: 18 }}>
        {(["paste", "manual"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} style={{
            background: tab === t ? "var(--accent)" : "var(--card2)",
            border: `1px solid ${tab === t ? "var(--accent)" : "var(--border)"}`,
            color: tab === t ? "#fff" : "var(--text2)",
            padding: "7px 18px", borderRadius: 6, fontSize: 12, fontWeight: 600,
            cursor: "pointer", textTransform: "capitalize",
          }}>
            {t === "paste" ? "Paste & Parse" : "Manual Entry"}
          </button>
        ))}
      </div>

      {tab === "paste"
        ? <PastePanel employees={allEmployees} onSaved={refetch} />
        : <ManualPanel entries={todayEntries} employees={allEmployees} loading={loadingEntries} onChanged={refetch} />
      }
    </div>
  )
}

// ── Paste Panel ───────────────────────────────────────────────────────────────

function PastePanel({ employees, onSaved }: { employees: Employee[]; onSaved: () => void }) {
  const [text, setText] = useState("")
  const [parsed, setParsed] = useState<ParsedRow[] | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveMsg, setSaveMsg] = useState<{ saved: number; unresolved: string[] } | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleParse = () => {
    const lines = text.split("\n").map(l => l.trim()).filter(Boolean)
    const rows: ParsedRow[] = lines.map(line => {
      const parsed = parseLine(line)
      if (!parsed) return { rawLine: line, rawName: line, shiftStart: "", shiftEnd: "", tag: null, resolved: null, ambiguous: [], status: "unresolved" as const }
      const resolution = resolveEmployee(parsed.rawName, employees)
      return { rawLine: line, ...parsed, ...resolution }
    })
    setParsed(rows)
    setSaveMsg(null)
    setError(null)
  }

  const handleSave = async () => {
    if (!parsed) return
    const resolved = parsed.filter(r => r.status === "resolved" && r.resolved)
    const unresolved = parsed.filter(r => r.status !== "resolved").map(r => r.rawName)

    if (resolved.length === 0) {
      setError("No rows could be resolved. Nothing to save.")
      return
    }

    setSaving(true)
    setError(null)
    try {
      const res = await fetch("/api/rtm/save", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          date: today,
          rows: resolved.map(r => ({
            employeeId: r.resolved!.employeeId,
            fullName:   r.resolved!.fullName,
            shiftStart: r.shiftStart,
            shiftEnd:   r.shiftEnd,
            tag:        r.tag,
            sourceLine: r.rawLine,
          })),
        }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setSaveMsg({ saved: resolved.length, unresolved })
      setText("")
      setParsed(null)
      onSaved()
    } catch (e) {
      setError(String(e))
    } finally {
      setSaving(false)
    }
  }

  const resolvedCount   = parsed?.filter(r => r.status === "resolved").length ?? 0
  const unresolvedCount = parsed?.filter(r => r.status !== "resolved").length ?? 0

  return (
    <div>
      {/* Save result */}
      {saveMsg && (
        <div style={{ ...card, background: "rgba(34,208,122,.08)", border: "1px solid rgba(34,208,122,.3)", marginBottom: 16 }}>
          <div style={{ fontSize: 13, color: "var(--green)", fontWeight: 600, marginBottom: saveMsg.unresolved.length > 0 ? 8 : 0 }}>
            ✓ {saveMsg.saved} row{saveMsg.saved !== 1 ? "s" : ""} saved for today (existing list replaced).
          </div>
          {saveMsg.unresolved.length > 0 && (
            <div style={{ fontSize: 12, color: "var(--text2)" }}>
              <span style={{ color: "#f97316", fontWeight: 600 }}>Not saved — could not resolve:</span>{" "}
              {saveMsg.unresolved.join(", ")}
            </div>
          )}
        </div>
      )}

      {/* Error */}
      {error && (
        <div style={{ ...card, background: "rgba(239,68,68,.08)", border: "1px solid rgba(239,68,68,.3)", marginBottom: 16, fontSize: 12, color: "#ef4444" }}>
          {error}
        </div>
      )}

      {/* Textarea */}
      <div style={card}>
        <div style={{ fontSize: 11, color: "var(--text3)", marginBottom: 8, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
          Paste list — one agent per line: Name HH:MM - HH:MM [optional tag]
        </div>
        <textarea
          value={text}
          onChange={e => { setText(e.target.value); setParsed(null); setSaveMsg(null) }}
          placeholder={"Tim Nguyen 08:00 - 17:00\nEva-Liane Schliwa 07:00 - 16:00 LEW\nJavier Sang 09:00-16:00 ENVIAM"}
          rows={10}
          style={{
            ...inputStyle, width: "100%", resize: "vertical",
            fontFamily: "IBM Plex Mono", fontSize: 12, lineHeight: 1.6,
          }}
        />
        <div style={{ display: "flex", gap: 10, marginTop: 12 }}>
          <button onClick={handleParse} disabled={!text.trim()} style={{
            ...btnPrimary,
            opacity: text.trim() ? 1 : 0.5,
            cursor: text.trim() ? "pointer" : "not-allowed",
          }}>
            Parse Preview
          </button>
          {text.trim() && (
            <button onClick={() => { setText(""); setParsed(null); setSaveMsg(null); setError(null) }} style={btnSecondary}>
              Clear
            </button>
          )}
        </div>
      </div>

      {/* Preview table */}
      {parsed && parsed.length > 0 && (
        <div style={card}>
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
              Preview — {parsed.length} line{parsed.length !== 1 ? "s" : ""} parsed
              {" "}<span style={{ color: "var(--green)", fontWeight: 500 }}>{resolvedCount} resolved</span>
              {unresolvedCount > 0 && <span style={{ color: "#f97316", fontWeight: 500 }}>, {unresolvedCount} not resolved</span>}
            </div>
            <button
              onClick={handleSave}
              disabled={saving || resolvedCount === 0}
              style={{
                ...btnPrimary,
                opacity: saving || resolvedCount === 0 ? 0.5 : 1,
                cursor: saving || resolvedCount === 0 ? "not-allowed" : "pointer",
              }}
            >
              {saving ? "Saving…" : `Save ${resolvedCount} Row${resolvedCount !== 1 ? "s" : ""}`}
            </button>
          </div>

          {unresolvedCount > 0 && (
            <div style={{
              fontSize: 11, color: "#f97316",
              background: "rgba(249,115,22,.08)", border: "1px solid rgba(249,115,22,.3)",
              borderRadius: 6, padding: "7px 12px", marginBottom: 12,
            }}>
              <strong>Not saved:</strong>{" "}
              {parsed.filter(r => r.status !== "resolved").map(r => r.rawName).join(", ")}
            </div>
          )}

          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle}>#</th>
                  <th style={thStyle}>Raw Input Name</th>
                  <th style={thStyle}>Resolved Employee</th>
                  <th style={thStyle}>Hours</th>
                  <th style={thStyle}>Tag</th>
                  <th style={thStyle}>Status</th>
                </tr>
              </thead>
              <tbody>
                {parsed.map((row, i) => (
                  <tr key={i} style={{ opacity: row.status !== "resolved" ? 0.7 : 1 }}>
                    <td style={{ ...tdStyle, color: "var(--text3)", fontFamily: "IBM Plex Mono", width: 32 }}>{i + 1}</td>
                    <td style={{ ...tdStyle, fontFamily: "IBM Plex Mono", fontSize: 11 }}>{row.rawName || <em style={{ color: "var(--text3)" }}>no name</em>}</td>
                    <td style={{ ...tdStyle }}>
                      {row.status === "resolved" && row.resolved
                        ? <span style={{ color: "var(--green)", fontWeight: 500 }}>{row.resolved.fullName}</span>
                        : row.status === "ambiguous"
                          ? <span style={{ color: "#f97316" }}>Ambiguous ({row.ambiguous.length} matches)</span>
                          : <span style={{ color: "#ef4444" }}>—</span>
                      }
                    </td>
                    <td style={{ ...tdStyle, fontFamily: "IBM Plex Mono", fontSize: 11 }}>
                      {row.shiftStart && row.shiftEnd ? `${row.shiftStart} – ${row.shiftEnd}` : <span style={{ color: "var(--text3)" }}>—</span>}
                    </td>
                    <td style={{ ...tdStyle }}>
                      {row.tag
                        ? <span style={{ background: "rgba(99,102,241,.12)", color: "var(--accent)", border: "1px solid rgba(99,102,241,.25)", borderRadius: 4, padding: "2px 7px", fontSize: 10, fontWeight: 600 }}>{row.tag}</span>
                        : <span style={{ color: "var(--text3)", fontSize: 11 }}>—</span>}
                    </td>
                    <td style={{ ...tdStyle }}>
                      {row.status === "resolved"
                        ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "var(--green)", fontSize: 11 }}><CheckCircle2 size={13} /> Resolved</span>
                        : row.status === "ambiguous"
                          ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#f97316", fontSize: 11 }}><AlertCircle size={13} /> Ambiguous</span>
                          : row.shiftStart
                            ? <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#ef4444", fontSize: 11 }}><XCircle size={13} /> Not found</span>
                            : <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#8892a4", fontSize: 11 }}><XCircle size={13} /> Parse error</span>
                      }
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {parsed && parsed.length === 0 && (
        <div style={{ ...card, color: "var(--text3)", fontSize: 13, textAlign: "center" }}>
          No parseable lines found. Each line must contain a name followed by times (e.g. "08:00 - 17:00").
        </div>
      )}
    </div>
  )
}

// ── Manual Panel ──────────────────────────────────────────────────────────────

function ManualPanel({
  entries, employees, loading, onChanged,
}: {
  entries: RtmEntry[]
  employees: Employee[]
  loading: boolean
  onChanged: () => void
}) {
  const [addOpen, setAddOpen]   = useState(false)
  const [addEmp, setAddEmp]     = useState("")
  const [addStart, setAddStart] = useState("08:00")
  const [addEnd, setAddEnd]     = useState("17:00")
  const [addTag, setAddTag]     = useState("")
  const [addBusy, setAddBusy]   = useState(false)

  const [editId, setEditId]       = useState<number | null>(null)
  const [editStart, setEditStart] = useState("")
  const [editEnd, setEditEnd]     = useState("")
  const [editTag, setEditTag]     = useState("")
  const [editBusy, setEditBusy]   = useState(false)

  const [notice, setNotice] = useState<string | null>(null)

  const flash = (msg: string) => { setNotice(msg); setTimeout(() => setNotice(null), 3000) }

  const handleAdd = async () => {
    if (!addEmp) return
    const emp = employees.find(e => e.employeeId === addEmp)
    setAddBusy(true)
    try {
      const res = await fetch(`/api/rtm/rows?date=${today}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId: addEmp,
          fullName:   emp?.fullName ?? null,
          shiftStart: addStart,
          shiftEnd:   addEnd,
          tag:        addTag.trim() || null,
        }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setAddEmp(""); setAddStart("08:00"); setAddEnd("17:00"); setAddTag("")
      setAddOpen(false)
      flash("Row added.")
      onChanged()
    } catch (e) {
      flash(`Error: ${e}`)
    } finally {
      setAddBusy(false)
    }
  }

  const openEdit = (e: RtmEntry) => {
    setEditId(e.id); setEditStart(e.shiftStart); setEditEnd(e.shiftEnd); setEditTag(e.tag ?? "")
  }

  const handleEdit = async () => {
    if (editId == null) return
    setEditBusy(true)
    try {
      const res = await fetch(`/api/rtm/rows/${editId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ shiftStart: editStart, shiftEnd: editEnd, tag: editTag.trim() || null }),
      })
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      setEditId(null)
      flash("Row updated.")
      onChanged()
    } catch (e) {
      flash(`Error: ${e}`)
    } finally {
      setEditBusy(false)
    }
  }

  const handleDelete = async (id: number) => {
    try {
      const res = await fetch(`/api/rtm/rows/${id}`, { method: "DELETE" })
      if (!res.ok && res.status !== 204) throw new Error(`HTTP ${res.status}`)
      flash("Row deleted.")
      onChanged()
    } catch (e) {
      flash(`Error: ${e}`)
    }
  }

  return (
    <div>
      {notice && (
        <div style={{
          position: "fixed", bottom: 24, right: 24, zIndex: 9999,
          background: notice.startsWith("Error") ? "#ef4444" : "#22c55e",
          color: "#fff", padding: "10px 18px", borderRadius: 8, fontSize: 12,
        }}>{notice}</div>
      )}

      <div style={card}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16 }}>
          <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
            Today's RTM List
            <span style={{ fontSize: 11, color: "var(--text3)", fontWeight: 400, marginLeft: 8, fontFamily: "IBM Plex Mono" }}>{today}</span>
          </span>
          <button onClick={() => setAddOpen(!addOpen)} style={{ ...btnPrimary, display: "flex", alignItems: "center", gap: 6 }}>
            <Plus size={13} /> Add Row
          </button>
        </div>

        {/* Add row form */}
        {addOpen && (
          <div style={{
            background: "var(--card2)", border: "1px solid var(--border)", borderRadius: 8,
            padding: "14px 16px", marginBottom: 16,
          }}>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 90px 90px 160px auto", gap: 10, alignItems: "end" }}>
              <label style={{ fontSize: 11, color: "var(--text3)" }}>
                Agent
                <select value={addEmp} onChange={e => setAddEmp(e.target.value)}
                  style={{ ...inputStyle, display: "block", marginTop: 4, width: "100%" }}>
                  <option value="">— select —</option>
                  {employees.map(e => (
                    <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
                  ))}
                </select>
              </label>
              <label style={{ fontSize: 11, color: "var(--text3)" }}>
                Start
                <input type="time" value={addStart} onChange={e => setAddStart(e.target.value)}
                  style={{ ...monoInput, display: "block", marginTop: 4, width: "100%" }} />
              </label>
              <label style={{ fontSize: 11, color: "var(--text3)" }}>
                End
                <input type="time" value={addEnd} onChange={e => setAddEnd(e.target.value)}
                  style={{ ...monoInput, display: "block", marginTop: 4, width: "100%" }} />
              </label>
              <label style={{ fontSize: 11, color: "var(--text3)" }}>
                Tag (optional)
                <input type="text" value={addTag} onChange={e => setAddTag(e.target.value)}
                  placeholder="e.g. Newjoiner, ENVIAM"
                  style={{ ...inputStyle, display: "block", marginTop: 4, width: "100%" }} />
              </label>
              <div style={{ display: "flex", gap: 8, paddingBottom: 1 }}>
                <button onClick={handleAdd} disabled={!addEmp || addBusy}
                  style={{ ...btnPrimary, opacity: !addEmp || addBusy ? 0.5 : 1, cursor: !addEmp || addBusy ? "not-allowed" : "pointer" }}>
                  {addBusy ? "…" : "Add"}
                </button>
                <button onClick={() => { setAddOpen(false); setAddEmp(""); setAddTag("") }} style={btnSecondary}>
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Entries table */}
        {loading ? (
          <div style={{ padding: "24px 0", textAlign: "center", color: "var(--text3)", fontSize: 13 }}>Loading…</div>
        ) : entries.length === 0 ? (
          <div style={{ padding: "32px 0", textAlign: "center", color: "var(--text3)", fontSize: 13 }}>
            No entries for today. Use the Paste tab to bulk-import, or click Add Row above.
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th style={thStyle}>#</th>
                  <th style={thStyle}>Employee</th>
                  <th style={thStyle}>ID</th>
                  <th style={thStyle}>Hours</th>
                  <th style={thStyle}>Tag</th>
                  <th style={thStyle}>Source</th>
                  <th style={{ ...thStyle, textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((e, i) => (
                  editId === e.id ? (
                    <tr key={e.id} style={{ background: "rgba(99,102,241,.05)" }}>
                      <td style={{ ...tdStyle, color: "var(--text3)", fontFamily: "IBM Plex Mono", width: 32 }}>{i + 1}</td>
                      <td style={{ ...tdStyle, fontWeight: 600 }}>{e.fullName ?? e.employeeId}</td>
                      <td style={{ ...tdStyle, fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{e.employeeId}</td>
                      <td style={{ ...tdStyle }}>
                        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                          <input type="time" value={editStart} onChange={ev => setEditStart(ev.target.value)}
                            style={{ ...monoInput, width: 80 }} />
                          <span style={{ color: "var(--text3)" }}>–</span>
                          <input type="time" value={editEnd} onChange={ev => setEditEnd(ev.target.value)}
                            style={{ ...monoInput, width: 80 }} />
                        </div>
                      </td>
                      <td style={{ ...tdStyle }}>
                        <input type="text" value={editTag} onChange={ev => setEditTag(ev.target.value)}
                          placeholder="tag" style={{ ...inputStyle, width: 140 }} />
                      </td>
                      <td style={{ ...tdStyle, color: "var(--text3)", fontSize: 11 }}>—</td>
                      <td style={{ ...tdStyle, textAlign: "right" }}>
                        <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                          <button onClick={handleEdit} disabled={editBusy} style={{ ...btnPrimary, padding: "4px 12px", fontSize: 11 }}>
                            {editBusy ? "…" : "Save"}
                          </button>
                          <button onClick={() => setEditId(null)} style={{ ...btnSecondary, padding: "4px 10px", fontSize: 11 }}>
                            Cancel
                          </button>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={e.id}>
                      <td style={{ ...tdStyle, color: "var(--text3)", fontFamily: "IBM Plex Mono", width: 32 }}>{i + 1}</td>
                      <td style={{ ...tdStyle, fontWeight: 600 }}>{e.fullName ?? e.employeeId}</td>
                      <td style={{ ...tdStyle, fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)" }}>{e.employeeId}</td>
                      <td style={{ ...tdStyle, fontFamily: "IBM Plex Mono", fontSize: 11 }}>{e.shiftStart} – {e.shiftEnd}</td>
                      <td style={{ ...tdStyle }}>
                        {e.tag
                          ? <span style={{ background: "rgba(99,102,241,.12)", color: "var(--accent)", border: "1px solid rgba(99,102,241,.25)", borderRadius: 4, padding: "2px 7px", fontSize: 10, fontWeight: 600 }}>{e.tag}</span>
                          : <span style={{ color: "var(--text3)", fontSize: 11 }}>—</span>}
                      </td>
                      <td style={{ ...tdStyle, color: "var(--text3)", fontSize: 11, maxWidth: 160, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {e.sourceLine ?? "manual"}
                      </td>
                      <td style={{ ...tdStyle, textAlign: "right" }}>
                        <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                          <button onClick={() => openEdit(e)} title="Edit"
                            style={{ background: "rgba(99,102,241,.1)", border: "1px solid rgba(99,102,241,.25)", color: "var(--accent)", padding: "4px 8px", borderRadius: 5, cursor: "pointer", display: "flex", alignItems: "center" }}>
                            <Pencil size={12} />
                          </button>
                          <button onClick={() => handleDelete(e.id)} title="Delete" style={{ ...btnDanger, padding: "4px 8px", display: "flex", alignItems: "center" }}>
                            <Trash2 size={12} />
                          </button>
                        </div>
                      </td>
                    </tr>
                  )
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
