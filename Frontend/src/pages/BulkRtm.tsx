import { useState, useCallback } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { FileText, Plus, Pencil, Trash2, CheckCircle2, XCircle, AlertCircle, MapPin } from "lucide-react"
import { resolveEmployee } from "../utils/resolveEmployee"
import type { BaseRowStatus, MatchType } from "../utils/resolveEmployee"

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

interface WicLocation {
  id: number
  locationCode: string
  displayName: string
  isActive: boolean
  isNpp: boolean
}

interface HomeWicEntry {
  employeeId: string
  locationCode: string | null
  displayName: string | null
}

type ParsedHeader = "AL" | "SL" | "OFF" | "CD" | "NIGHT" | "BO" | "WIC"

const VALID_HEADERS: ParsedHeader[] = ["AL", "SL", "OFF", "CD", "NIGHT", "BO", "WIC"]

const HEADER_LABELS: Record<ParsedHeader, string> = {
  AL:    "AL — Annual Leave",
  SL:    "SL — Sick Leave",
  OFF:   "OFF — Day Off",
  CD:    "CD — Compensation Day",
  NIGHT: "Night — WORKING (night shift hours)",
  BO:    "BO — BO Liste (replaces today)",
  WIC:   "WIC — WIC Location Assignment",
}

const HEADER_COLORS: Record<ParsedHeader, string> = {
  AL:    "rgb(var(--st-info-fg))",
  SL:    "rgb(var(--st-crit-fg))",
  OFF:   "rgb(var(--st-neutral-fg))",
  CD:    "rgb(var(--st-learn-fg))",
  NIGHT: "rgb(var(--st-wic-fg))",
  BO:    "rgb(var(--st-info-fg))",
  WIC:   "rgb(var(--st-good-fg))",
}

type RowStatus = BaseRowStatus | "night-no-time"

interface ParsedRow {
  rawLine: string
  rawName: string
  shiftStart: string
  shiftEnd: string
  tag: string | null
  resolved: Employee | null
  ambiguous: Employee[]
  status: RowStatus
  matchType?: MatchType
  suggestions?: Employee[]
}

interface SaveRowResult {
  name: string
  status: "saved" | "skipped" | "error"
  reason?: string
  locationUsed?: string
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const today = new Date().toISOString().split("T")[0]
const todayLabel = new Date().toLocaleDateString("en-GB", { weekday: "long", year: "numeric", month: "long", day: "numeric" })


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

// For SL: no times required. Strip any trailing HH:MM or HH:MM - HH:MM noise
// (users may paste from a shift-format list), then treat the rest as the name.
function parseSlLine(line: string): { rawName: string; tag: string | null } {
  const stripped = line.trim()
    .replace(/\s+\d{1,2}:\d{2}(\s*[-–]\s*\d{1,2}:\d{2})?(\s+.*)?$/, "")
    .trim()
  return { rawName: stripped || line.trim(), tag: null }
}

// ── Style tokens ──────────────────────────────────────────────────────────────

const card = "bg-raised border border-line-subtle rounded-[10px] py-[18px] px-5 mb-4"
const inputCls = "bg-sunken border border-line-subtle rounded-sm py-[7px] px-[10px] text-ink text-xs box-border outline-none"
const monoInputCls = "bg-sunken border border-line-subtle rounded-sm py-[7px] px-[10px] text-ink text-xs box-border outline-none font-mono"
const btnPrimary = "bg-info-solid border-none text-white py-[7px] px-4 rounded-sm text-xs font-semibold cursor-pointer"
const btnSecondary = "bg-sunken border border-line-subtle text-ink-muted py-[7px] px-[14px] rounded-sm text-xs cursor-pointer"
const thCls = "py-2 px-3 text-left text-[10px] font-semibold text-ink-soft uppercase tracking-[0.06em] border-b border-line-subtle"
const tdCls = "py-2 px-3 text-xs text-ink border-b border-line-subtle"

// ── Page ─────────────────────────────────────────────────────────────────────

export default function BulkRtm() {
  const qc = useQueryClient()
  const [tab, setTab] = useState<"paste" | "manual">("paste")

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

  const { data: wicLocations = [] } = useQuery<WicLocation[]>({
    queryKey: ["wic-locations"],
    queryFn: () => fetch("/api/wic/locations").then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() }),
    staleTime: 5 * 60_000,
  })

  const refetch = useCallback(() => qc.invalidateQueries({ queryKey: ["rtm-today", today] }), [qc])

  return (
    <div style={{ maxWidth: 960 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 20 }}>
        <FileText size={18} className="text-info-fg" />
        <h1 className="m-0 text-xl font-bold text-ink">Bulk RTM Entry</h1>
        <span className="text-[11px] font-mono text-ink-soft bg-sunken border border-line-subtle px-[9px] py-[3px] rounded-[5px]">{todayLabel}</span>
      </div>

      {/* Tab selector */}
      <div style={{ display: "flex", gap: 2, marginBottom: 18 }}>
        {(["paste", "manual"] as const).map(t => (
          <button key={t} onClick={() => setTab(t)} className={`${tab === t ? "bg-info-solid border border-info-bd text-white" : "bg-sunken border border-line-subtle text-ink-muted"} py-[7px] px-[18px] rounded-sm text-xs font-semibold cursor-pointer capitalize`}>
            {t === "paste" ? "Paste & Parse" : "Manual Entry"}
          </button>
        ))}
      </div>

      {tab === "paste"
        ? <PastePanel employees={allEmployees} wicLocations={wicLocations} onSaved={refetch} />
        : <ManualPanel entries={todayEntries} employees={allEmployees} loading={loadingEntries} onChanged={refetch} />
      }
    </div>
  )
}

// ── Paste Panel ───────────────────────────────────────────────────────────────

function PastePanel({
  employees, wicLocations, onSaved,
}: {
  employees: Employee[]
  wicLocations: WicLocation[]
  onSaved: () => void
}) {
  const [text, setText]                         = useState("")
  const [category, setCategory]                 = useState<ParsedHeader>("AL")
  const [wicCenterDefault, setWicCenterDefault] = useState<string | null>(null)
  const [rows, setRows]                         = useState<ParsedRow[] | null>(null)
  const [rowOverrides, setRowOverrides]         = useState<(string | null)[]>([])
  const [homeWicMap, setHomeWicMap]             = useState<Record<string, HomeWicEntry>>({})
  const [homeWicLoading, setHomeWicLoading]     = useState(false)
  const [saving, setSaving]                     = useState(false)
  const [saveResults, setSaveResults]           = useState<SaveRowResult[] | null>(null)
  const [error, setError]                       = useState<string | null>(null)

  // When a user picks from the "Did you mean?" dropdown, upgrade that row to resolved
  const handleSuggestionPick = (rowIndex: number, employeeId: string) => {
    const emp = employees.find(e => e.employeeId === employeeId)
    if (!emp || !rows) return
    setRows(rows.map((r, i) => i !== rowIndex ? r : { ...r, resolved: emp, status: "resolved" as RowStatus, matchType: "fuzzy" as MatchType }))
    if (category === "WIC" && !homeWicMap[employeeId]) {
      fetch(`/api/rtm/home-wic?ids=${employeeId}`)
        .then(r => r.ok ? r.json() : [])
        .then((data: HomeWicEntry[]) => {
          if (data.length > 0) setHomeWicMap(prev => ({ ...prev, [employeeId]: data[0] }))
        })
        .catch(() => {})
    }
  }

  const handleParse = async () => {
    setError(null)
    setSaveResults(null)
    setHomeWicMap({})
    setRowOverrides([])

    const lines = text.split("\n").map(l => l.trim()).filter(l => l.length > 0)
    if (lines.length === 0) { setError("Nothing to parse."); return }

    const parsedRows: ParsedRow[] = lines.map(line => {
      if (category === "SL") {
        const p = parseSlLine(line)
        return { rawLine: line, rawName: p.rawName, shiftStart: "", shiftEnd: "", tag: p.tag, ...resolveEmployee(p.rawName, employees) }
      }
      const p = parseLine(line)
      if (!p) {
        if (category === "NIGHT") {
          const resolved = resolveEmployee(line.trim(), employees)
          return { rawLine: line, rawName: line.trim(), shiftStart: "", shiftEnd: "", tag: null, ...resolved, status: "night-no-time" as const }
        }
        return { rawLine: line, rawName: line, shiftStart: "", shiftEnd: "", tag: null, resolved: null, ambiguous: [], status: "unresolved" as const }
      }
      return { rawLine: line, ...p, ...resolveEmployee(p.rawName, employees) }
    })

    setRows(parsedRows)
    setRowOverrides(new Array(parsedRows.length).fill(null))

    if (category === "WIC") {
      const resolvedIds = parsedRows
        .filter(r => (r.status === "resolved" || r.status === "resolved-corrected") && r.resolved)
        .map(r => r.resolved!.employeeId)
      if (resolvedIds.length > 0) {
        setHomeWicLoading(true)
        try {
          const data: HomeWicEntry[] = await fetch(`/api/rtm/home-wic?ids=${resolvedIds.join(",")}`)
            .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json() })
          const map: Record<string, HomeWicEntry> = {}
          data.forEach(e => { map[e.employeeId] = e })
          setHomeWicMap(map)
        } catch (e) {
          setError(`Could not load home WIC data: ${e}`)
        } finally {
          setHomeWicLoading(false)
        }
      }
    }
  }

  const handleSave = async () => {
    if (!rows) return
    setSaving(true)
    setSaveResults(null)
    setError(null)

    const results: SaveRowResult[] = []

    if (category === "BO") {
      try {
        const res = await fetch(`/api/bo-list/by-date?date=${today}`, { method: "DELETE" })
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
      } catch (e) {
        setError(`Failed to clear existing BO entries: ${e}`)
        setSaving(false)
        return
      }
    }

    for (let i = 0; i < rows.length; i++) {
      const row = rows[i]

      if ((row.status !== "resolved" && row.status !== "resolved-corrected") || !row.resolved) {
        results.push({
          name: row.rawName,
          status: "skipped",
          reason: row.status === "suggest" ? "Needs selection (Did you mean?)"
                : row.status === "ambiguous" ? "Ambiguous name"
                : row.status === "night-no-time" ? "Night requires shift hours, e.g. 22:00 - 07:00"
                : row.shiftStart ? "Name not in system" : "Parse error",
        })
        continue
      }

      const displayName = row.resolved.fullName ?? row.rawName

      try {
        if (category === "AL" || category === "OFF" || category === "CD") {
          const r = await fetch("/api/shifts/assign", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ employeeId: row.resolved.employeeId, shiftDate: today, shiftType: category, shiftStart: null, shiftEnd: null }),
          })
          const body = r.ok ? null : await r.json().catch(() => ({}))
          results.push(r.ok ? { name: displayName, status: "saved" } : { name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })

        } else if (category === "NIGHT") {
          const r = await fetch("/api/shifts/assign", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ employeeId: row.resolved.employeeId, shiftDate: today, shiftType: "WORKING", shiftStart: row.shiftStart, shiftEnd: row.shiftEnd }),
          })
          const body = r.ok ? null : await r.json().catch(() => ({}))
          results.push(r.ok ? { name: displayName, status: "saved" } : { name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })

        } else if (category === "BO") {
          const r = await fetch("/api/bo-list", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ date: today, employeeName: displayName, shiftStart: row.shiftStart, shiftEnd: row.shiftEnd, note: row.tag ?? null }),
          })
          const body = r.ok ? null : await r.json().catch(() => ({}))
          results.push(r.ok ? { name: displayName, status: "saved" } : { name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })

        } else if (category === "SL") {
          const r = await fetch("/api/sickleave", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
              employeeId: row.resolved.employeeId,
              startDate: today,
              endDate: "2099-12-31",
              type: "Self",
              notes: row.tag ?? null,
            }),
          })
          const body = r.ok ? null : await r.json().catch(() => ({}))
          results.push(r.ok ? { name: displayName, status: "saved" } : { name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })

        } else if (category === "WIC") {
          const override = rowOverrides[i]
          const homeWic  = homeWicMap[row.resolved.employeeId]
          // Precedence: per-row override > WIC-center dropdown default > home WIC
          const locCode  = override ?? wicCenterDefault ?? homeWic?.locationCode ?? null

          if (!locCode) {
            results.push({ name: displayName, status: "skipped", reason: "No home WIC and no location selected" })
            continue
          }

          const locDisp = wicLocations.find(l => l.locationCode === locCode)?.displayName ?? locCode
          const r = await fetch("/api/wic/assignments", {
            method: "POST", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ employeeId: row.resolved.employeeId, locationCode: locCode, date: today, shiftStart: row.shiftStart, shiftEnd: row.shiftEnd }),
          })
          const body = r.ok ? await r.json().catch(() => null) : await r.json().catch(() => ({}))
          if (!r.ok) {
            results.push({ name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })
          } else if (body?.skipped) {
            results.push({ name: displayName, status: "skipped", reason: body.reason ?? "Skipped by WIC assignment rules" })
          } else {
            results.push({ name: displayName, status: "saved", locationUsed: locDisp })
          }
        }
      } catch (e) {
        results.push({ name: displayName, status: "error", reason: String(e) })
      }
    }

    setSaveResults(results)
    setSaving(false)
    if (results.some(r => r.status === "saved")) {
      setText(""); setRows(null); setRowOverrides([]); setHomeWicMap({})
      onSaved()
    }
  }

  const resolvedCount   = rows?.filter(r => r.status === "resolved" || r.status === "resolved-corrected").length ?? 0
  const unresolvedCount = rows?.filter(r => r.status !== "resolved" && r.status !== "resolved-corrected").length ?? 0
  // When a center-level default is chosen every row has a location; noLocCount is only relevant without it.
  const noLocCount = category === "WIC" && rows && !wicCenterDefault
    ? rows.filter(r => (r.status === "resolved" || r.status === "resolved-corrected") && r.resolved && !rowOverrides[rows.indexOf(r)] && !homeWicMap[r.resolved.employeeId]?.locationCode).length
    : 0
  const saveDisabled = saving || resolvedCount === 0 || (category === "WIC" && homeWicLoading)

  const saveBtnLabel = (() => {
    if (saving) return "Saving…"
    if (category === "BO")  return `Replace Today's BO List (${resolvedCount} row${resolvedCount !== 1 ? "s" : ""})`
    if (category === "WIC") return `Save ${resolvedCount} WIC Assignment${resolvedCount !== 1 ? "s" : ""}`
    return `Save ${resolvedCount} Row${resolvedCount !== 1 ? "s" : ""}`
  })()

  const wicCenterName = wicCenterDefault
    ? (wicLocations.find(l => l.locationCode === wicCenterDefault)?.displayName ?? wicCenterDefault)
    : null

  return (
    <div>

      {/* Save results */}
      {saveResults && (
        <div className={card}>
          <div className="text-[13px] font-semibold text-ink mb-[10px]">
            Save complete — {saveResults.filter(r => r.status === "saved").length} saved
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
                    : <XCircle size={13} className="text-crit-fg" />
                }
                <span className={`text-ink ${r.status === "saved" ? "font-medium" : ""}`}>{r.name}</span>
                {r.locationUsed && <span className="text-ink-soft text-[11px]">→ {r.locationUsed}</span>}
                {r.reason && <span className={`text-[11px] ${r.status === "error" ? "text-crit-fg" : "text-warn-fg"}`}>— {r.reason}</span>}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="rounded-[10px] py-[18px] px-5 mb-4 text-xs text-crit-fg bg-crit-bg border border-crit-bd">
          {error}
        </div>
      )}

      {/* Input card: category selector + optional WIC center + textarea */}
      <div className={card}>

        {/* Category + WIC center selectors */}
        <div style={{ display: "flex", alignItems: "center", gap: 16, marginBottom: 14, flexWrap: "wrap" }}>
          <label className="flex items-center gap-2 text-xs text-ink-muted font-semibold">
            Category
            <select
              value={category}
              onChange={e => {
                setCategory(e.target.value as ParsedHeader)
                setRows(null); setSaveResults(null); setError(null)
              }}
              className={`${inputCls} min-w-[230px]`}
            >
              {VALID_HEADERS.map(h => (
                <option key={h} value={h}>{HEADER_LABELS[h]}</option>
              ))}
            </select>
          </label>

          {category === "WIC" && (
            <label className="flex items-center gap-2 text-xs text-ink-muted font-semibold">
              WIC Center
              <select
                value={wicCenterDefault ?? ""}
                onChange={e => setWicCenterDefault(e.target.value || null)}
                className={`${inputCls} min-w-[230px]`}
              >
                <option value="">— Home WIC —</option>
                {wicLocations.map(l => (
                  <option key={l.locationCode} value={l.locationCode}>{l.displayName}{l.isNpp ? " ★NPP" : ""}</option>
                ))}
              </select>
            </label>
          )}
        </div>

        {/* Format hint */}
        <div className="text-[11px] text-ink-soft mb-[10px] font-mono">
          {category === "SL"
            ? "One agent per line: Agent Name  [optional tag]"
            : category === "NIGHT"
              ? "One agent per line: Agent Name HH:MM - HH:MM  (times required, e.g. 22:00 - 07:00)"
              : "One agent per line: Agent Name  HH:MM - HH:MM  [optional tag]"
          }
        </div>

        <textarea
          value={text}
          onChange={e => { setText(e.target.value); setRows(null); setSaveResults(null); setError(null) }}
          placeholder={category === "SL"
            ? "Anas Daba\nTim Böger\nPascal Dutz"
            : "Tim Nguyen 08:00 - 17:00\nEva-Liane Schliwa 07:00 - 16:00"
          }
          rows={10}
          className={`${inputCls} font-mono w-full`}
          style={{ resize: "vertical", lineHeight: 1.6 }}
        />
        <div style={{ display: "flex", gap: 10, marginTop: 12 }}>
          <button onClick={handleParse} disabled={!text.trim()} className={`${btnPrimary}${!text.trim() ? " opacity-50 cursor-not-allowed" : ""}`}>
            Parse Preview
          </button>
          {text.trim() && (
            <button onClick={() => { setText(""); setRows(null); setSaveResults(null); setError(null); setRowOverrides([]); setHomeWicMap({}) }} className={btnSecondary}>
              Clear
            </button>
          )}
        </div>
      </div>

      {/* Preview */}
      {rows && (
        <div className={card}>
          {/* Category badge + stats */}
          <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 14 }}>
            <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
              <span className="font-mono text-[11px] font-bold px-[10px] py-[3px] rounded-[5px]" style={{
                background: HEADER_COLORS[category] + "22",
                border: `1px solid ${HEADER_COLORS[category]}55`,
                color: HEADER_COLORS[category],
              }}>
                {category}
              </span>
              <span className="text-xs text-ink-muted">
                {HEADER_LABELS[category]}
              </span>
              <span className="text-xs text-ink-soft">
                · {rows.length} line{rows.length !== 1 ? "s" : ""}
                {" "}<span className="text-good-fg">{resolvedCount} resolved</span>
                {unresolvedCount > 0 && <span className="text-warn-fg">, {unresolvedCount} not resolved</span>}
                {homeWicLoading && <span className="text-ink-soft"> · loading home WIC…</span>}
              </span>
            </div>
            <button
              onClick={handleSave}
              disabled={saveDisabled}
              className={`${btnPrimary}${saveDisabled ? " opacity-50 cursor-not-allowed" : ""}`}
            >
              {saveBtnLabel}
            </button>
          </div>

          {/* Unresolved warning */}
          {unresolvedCount > 0 && (
            <div className="text-[11px] text-warn-fg bg-warn-bg border border-warn-bd rounded-md py-[7px] px-3 mb-3">
              <strong>Not saved (skipped):</strong>{" "}
              {rows.filter(r => r.status !== "resolved" && r.status !== "resolved-corrected").map(r => r.rawName).join(", ")}
            </div>
          )}

          {/* WIC no-home warning (suppressed when a center default is chosen) */}
          {category === "WIC" && !homeWicLoading && noLocCount > 0 && (
            <div className="text-[11px] text-crit-fg bg-crit-bg border border-crit-bd rounded-md py-[7px] px-3 mb-3">
              <strong>{noLocCount} row{noLocCount !== 1 ? "s have" : " has"} no home WIC</strong> — select a location from the per-row dropdown or choose a WIC center above.
            </div>
          )}

          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th className={thCls}>#</th>
                  <th className={thCls}>Raw Input Name</th>
                  <th className={thCls}>Resolved</th>
                  {category !== "SL" && <th className={thCls}>Hours</th>}
                  {category !== "AL" && category !== "OFF" && category !== "CD" && <th className={thCls}>Tag / Notes</th>}
                  {category === "WIC" && <th className={`${thCls} min-w-[200px]`}>WIC Location</th>}
                  <th className={thCls}>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => {
                  const homeWic  = row.resolved ? homeWicMap[row.resolved.employeeId] : undefined
                  const override = rowOverrides[i] ?? null
                  // Red background only when there's truly no resolvable location for this row
                  const hasNoLoc = category === "WIC" && (row.status === "resolved" || row.status === "resolved-corrected") && !override && !wicCenterDefault && !homeWic?.locationCode

                  return (
                    <tr key={i} className={`border-b border-line-subtle transition-colors ${hasNoLoc ? "bg-crit-bg/50" : "hover:bg-hovered"}`} style={{ opacity: (row.status === "resolved" || row.status === "resolved-corrected") ? 1 : 0.7 }}>
                      <td className={`${tdCls} text-ink-soft font-mono`} style={{ width: 32 }}>{i + 1}</td>
                      <td className={`${tdCls} font-mono text-[11px]`}>{row.rawName || <em className="text-ink-soft">no name</em>}</td>
                      <td className={tdCls}>
                        {(row.status === "resolved" || row.status === "resolved-corrected" || row.status === "night-no-time") && row.resolved ? (
                          <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                            <span className="text-good-fg font-medium">{row.resolved.fullName}</span>
                            {row.status === "resolved-corrected" && (
                              <span className="text-[10px] text-warn-fg bg-warn-bg border border-warn-bd rounded-[3px] px-[5px] py-[1px]">corrected</span>
                            )}
                          </div>
                        ) : row.status === "suggest" ? (
                          <select
                            defaultValue=""
                            onChange={e => { if (e.target.value) handleSuggestionPick(i, e.target.value) }}
                            className={`${inputCls} text-[11px] py-1 px-2 min-w-[200px]`}
                          >
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
                      {category !== "SL" && (
                        <td className={`${tdCls} font-mono text-[11px]`}>
                          {row.shiftStart && row.shiftEnd ? `${row.shiftStart} – ${row.shiftEnd}` : <span className="text-ink-soft">—</span>}
                        </td>
                      )}
                      {category !== "AL" && category !== "OFF" && category !== "CD" && (
                        <td className={tdCls}>
                          {row.tag
                            ? <span className="bg-info-bg text-info-fg border border-info-bd rounded-xs px-[7px] py-[2px] text-[10px] font-semibold">{row.tag}</span>
                            : <span className="text-ink-soft text-[11px]">—</span>}
                        </td>
                      )}
                      {category === "WIC" && (
                        <td className={tdCls}>
                          {(row.status === "resolved" || row.status === "resolved-corrected") ? (
                            <div style={{ display: "flex", flexDirection: "column", gap: 3 }}>
                              <select
                                value={override ?? ""}
                                onChange={e => {
                                  const next = [...rowOverrides]
                                  next[i] = e.target.value || null
                                  setRowOverrides(next)
                                }}
                                className={`${inputCls} text-[11px] py-1 px-2 max-w-[220px]`}
                              >
                                <option value="">— Home WIC —</option>
                                {wicLocations.map(l => (
                                  <option key={l.locationCode} value={l.locationCode}>{l.displayName}{l.isNpp ? " ★NPP" : ""}</option>
                                ))}
                              </select>
                              {/* Effective location hint: per-row wins, then center default, then home WIC */}
                              {!override && (
                                homeWicLoading
                                  ? <span className="text-[10px] text-ink-soft italic">Loading…</span>
                                  : wicCenterDefault
                                    ? <span className="text-[10px] text-wic-fg flex items-center gap-[3px]">
                                        <MapPin size={9} /> {wicCenterName}
                                      </span>
                                    : homeWic?.displayName
                                      ? <span className="text-[10px] text-good-fg flex items-center gap-[3px]">
                                          <MapPin size={9} /> {homeWic.displayName}
                                        </span>
                                      : <span className="text-[10px] text-crit-fg">No home WIC — select one</span>
                              )}
                            </div>
                          ) : (
                            <span className="text-ink-soft text-[11px]">—</span>
                          )}
                        </td>
                      )}
                      <td className={tdCls}>
                        {row.status === "resolved"
                          ? <span className="flex items-center gap-1 text-good-fg text-[11px]"><CheckCircle2 size={13} /> Resolved</span>
                          : row.status === "resolved-corrected"
                            ? <span className="flex items-center gap-1 text-warn-fg text-[11px]"><CheckCircle2 size={13} /> Resolved (corrected)</span>
                            : row.status === "suggest"
                              ? <span className="flex items-center gap-1 text-warn-fg text-[11px]"><AlertCircle size={13} /> Did you mean?</span>
                              : row.status === "ambiguous"
                                ? <span className="flex items-center gap-1 text-warn-fg text-[11px]"><AlertCircle size={13} /> Ambiguous</span>
                                : row.status === "night-no-time"
                                  ? <span className="flex items-center gap-1 text-warn-fg text-[11px]"><AlertCircle size={13} /> Night requires shift hours, e.g. 22:00 - 07:00</span>
                                  : (row.shiftStart || (category === "SL" && row.rawName))
                                    ? <span className="flex items-center gap-1 text-crit-fg text-[11px]"><XCircle size={13} /> Name not in system</span>
                                    : <span className="flex items-center gap-1 text-ink-soft text-[11px]"><XCircle size={13} /> Parse error</span>
                        }
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {rows && rows.length === 0 && (
        <div className={`${card} text-ink-soft text-[13px] text-center`}>
          No agent rows found. Each line must include: Name HH:MM - HH:MM
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
        <div className={`fixed bottom-6 right-6 z-toast ${notice.startsWith("Error") ? "bg-crit-solid" : "bg-good-solid"} text-white px-[18px] py-[10px] rounded-lg text-xs`}>
          {notice}
        </div>
      )}

      <div className={card}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16 }}>
          <span className="text-[13px] font-semibold text-ink">
            Today's RTM List
            <span className="text-[11px] text-ink-soft font-normal ml-2 font-mono">{today}</span>
          </span>
          <button onClick={() => setAddOpen(!addOpen)} className={`${btnPrimary} flex items-center gap-1.5`}>
            <Plus size={13} /> Add Row
          </button>
        </div>

        {/* Add row form */}
        {addOpen && (
          <div className="bg-sunken border border-line-subtle rounded-lg py-[14px] px-4 mb-4">
            <div style={{ display: "grid", gridTemplateColumns: "1fr 90px 90px 160px auto", gap: 10, alignItems: "end" }}>
              <label className="text-[11px] text-ink-soft">
                Agent
                <select value={addEmp} onChange={e => setAddEmp(e.target.value)}
                  className={`${inputCls} block mt-1 w-full`}>
                  <option value="">— select —</option>
                  {employees.map(e => (
                    <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
                  ))}
                </select>
              </label>
              <label className="text-[11px] text-ink-soft">
                Start
                <input type="time" value={addStart} onChange={e => setAddStart(e.target.value)}
                  className={`${monoInputCls} block mt-1 w-full`} />
              </label>
              <label className="text-[11px] text-ink-soft">
                End
                <input type="time" value={addEnd} onChange={e => setAddEnd(e.target.value)}
                  className={`${monoInputCls} block mt-1 w-full`} />
              </label>
              <label className="text-[11px] text-ink-soft">
                Tag (optional)
                <input type="text" value={addTag} onChange={e => setAddTag(e.target.value)}
                  placeholder="e.g. Newjoiner, ENVIAM"
                  className={`${inputCls} block mt-1 w-full`} />
              </label>
              <div style={{ display: "flex", gap: 8, paddingBottom: 1 }}>
                <button onClick={handleAdd} disabled={!addEmp || addBusy}
                  className={`${btnPrimary}${!addEmp || addBusy ? " opacity-50 cursor-not-allowed" : ""}`}>
                  {addBusy ? "…" : "Add"}
                </button>
                <button onClick={() => { setAddOpen(false); setAddEmp(""); setAddTag("") }} className={btnSecondary}>
                  Cancel
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Entries table */}
        {loading ? (
          <div className="py-6 text-center text-ink-soft text-[13px]">Loading…</div>
        ) : entries.length === 0 ? (
          <div className="py-8 text-center text-ink-soft text-[13px]">
            No entries for today. Use the Paste tab to bulk-import, or click Add Row above.
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th className={thCls}>#</th>
                  <th className={thCls}>Employee</th>
                  <th className={thCls}>ID</th>
                  <th className={thCls}>Hours</th>
                  <th className={thCls}>Tag</th>
                  <th className={thCls}>Source</th>
                  <th className={`${thCls} text-right`}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((e, i) => (
                  editId === e.id ? (
                    <tr key={e.id} className="bg-info-bg/30">
                      <td className={`${tdCls} text-ink-soft font-mono`} style={{ width: 32 }}>{i + 1}</td>
                      <td className={`${tdCls} font-semibold`}>{e.fullName ?? e.employeeId}</td>
                      <td className={`${tdCls} font-mono text-[11px] text-ink-soft`}>{e.employeeId}</td>
                      <td className={tdCls}>
                        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
                          <input type="time" value={editStart} onChange={ev => setEditStart(ev.target.value)}
                            className={monoInputCls} style={{ width: 80 }} />
                          <span className="text-ink-soft">–</span>
                          <input type="time" value={editEnd} onChange={ev => setEditEnd(ev.target.value)}
                            className={monoInputCls} style={{ width: 80 }} />
                        </div>
                      </td>
                      <td className={tdCls}>
                        <input type="text" value={editTag} onChange={ev => setEditTag(ev.target.value)}
                          placeholder="tag" className={inputCls} style={{ width: 140 }} />
                      </td>
                      <td className={`${tdCls} text-ink-soft text-[11px]`}>—</td>
                      <td className={`${tdCls} text-right`}>
                        <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                          <button onClick={handleEdit} disabled={editBusy} className="bg-info-solid border-none text-white py-1 px-3 rounded-sm text-[11px] font-semibold cursor-pointer">
                            {editBusy ? "…" : "Save"}
                          </button>
                          <button onClick={() => setEditId(null)} className="bg-sunken border border-line-subtle text-ink-muted py-1 px-[10px] rounded-sm text-[11px] cursor-pointer">
                            Cancel
                          </button>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={e.id} className="border-b border-line-subtle transition-colors hover:bg-hovered">
                      <td className={`${tdCls} text-ink-soft font-mono`} style={{ width: 32 }}>{i + 1}</td>
                      <td className={`${tdCls} font-semibold`}>{e.fullName ?? e.employeeId}</td>
                      <td className={`${tdCls} font-mono text-[11px] text-ink-soft`}>{e.employeeId}</td>
                      <td className={`${tdCls} font-mono text-[11px]`}>{e.shiftStart} – {e.shiftEnd}</td>
                      <td className={tdCls}>
                        {e.tag
                          ? <span className="bg-info-bg text-info-fg border border-info-bd rounded-xs px-[7px] py-[2px] text-[10px] font-semibold">{e.tag}</span>
                          : <span className="text-ink-soft text-[11px]">—</span>}
                      </td>
                      <td className={`${tdCls} text-ink-soft text-[11px]`} style={{ maxWidth: 160, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {e.sourceLine ?? "manual"}
                      </td>
                      <td className={`${tdCls} text-right`}>
                        <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                          <button onClick={() => openEdit(e)} title="Edit"
                            className="bg-info-bg border border-info-bd text-info-fg p-1 rounded-[5px] cursor-pointer flex items-center">
                            <Pencil size={12} />
                          </button>
                          <button onClick={() => handleDelete(e.id)} title="Delete"
                            className="bg-crit-bg border border-crit-bd text-crit-fg py-[5px] px-[10px] rounded-[5px] text-[11px] cursor-pointer flex items-center">
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
