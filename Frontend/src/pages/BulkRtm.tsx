import { useState, useCallback } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { FileText, Plus, Pencil, Trash2, CheckCircle2, XCircle, AlertCircle, MapPin } from "lucide-react"
import type { TFunction } from "i18next"
import {
  BULK_RTM_CATEGORIES,
  buildBulkRtmWrites,
  isWritableStatus,
  localToday,
  resolveAgentList,
  summarizeRows,
  type BulkRtmCategory,
  type ResolvedAgentLine,
} from "../utils/bulkRtmParse"

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

const HEADER_COLORS: Record<BulkRtmCategory, string> = {
  AL:    "rgb(var(--st-info-fg))",
  SL:    "rgb(var(--st-crit-fg))",
  OFF:   "rgb(var(--st-neutral-fg))",
  CD:    "rgb(var(--st-learn-fg))",
  NIGHT: "rgb(var(--st-wic-fg))",
  BO:    "rgb(var(--st-info-fg))",
  WIC:   "rgb(var(--st-good-fg))",
}

interface ParsedRow extends ResolvedAgentLine<Employee> {
  ordinal: number
  /** True when RTM picked the employee from the "did you mean?" list. */
  picked?: boolean
}

interface SaveRowResult {
  name: string
  status: "saved" | "skipped" | "error"
  reason?: string
  locationUsed?: string
}

// ── Style tokens ──────────────────────────────────────────────────────────────

const card = "bg-raised border border-line-subtle rounded-[10px] py-[18px] px-5 mb-4"
const inputCls = "bg-sunken border border-line-subtle rounded-sm py-[7px] px-[10px] text-ink text-xs box-border outline-none"
const monoInputCls = "bg-sunken border border-line-subtle rounded-sm py-[7px] px-[10px] text-ink text-xs box-border outline-none font-mono"
const btnPrimary = "bg-info-solid border-none text-white py-[7px] px-4 rounded-sm text-xs font-semibold cursor-pointer"
const btnSecondary = "bg-sunken border border-line-subtle text-ink-muted py-[7px] px-[14px] rounded-sm text-xs cursor-pointer"
const thCls = "py-2 px-3 text-left text-[10px] font-semibold text-ink-soft uppercase tracking-[0.06em] border-b border-line-subtle"
const tdCls = "py-2 px-3 text-xs text-ink border-b border-line-subtle"

const isRecognized = (s: ParsedRow["status"]) => isWritableStatus(s)

function skipReason(t: TFunction, row: ParsedRow): string {
  if (row.status === "suggest") return t("bulkRtm.reason.needsSelection")
  if (row.status === "ambiguous") return t("bulkRtm.reason.ambiguous")
  return t("bulkRtm.reason.notFound")
}

// ── Page ─────────────────────────────────────────────────────────────────────

export default function BulkRtm() {
  const { t, i18n } = useTranslation()
  const qc = useQueryClient()
  const [tab, setTab] = useState<"paste" | "manual">("paste")
  const [today] = useState(() => localToday())

  const todayLabel = new Date().toLocaleDateString(
    i18n.language?.startsWith("de") ? "de-DE" : "en-GB",
    { weekday: "long", year: "numeric", month: "long", day: "numeric" }
  )

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

  const refetch = useCallback(() => qc.invalidateQueries({ queryKey: ["rtm-today", today] }), [qc, today])

  return (
    <div style={{ maxWidth: 960 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 20 }}>
        <FileText size={18} className="text-info-fg" />
        <h1 className="m-0 text-xl font-bold text-ink">{t("bulkRtm.title")}</h1>
        <span className="text-[11px] font-mono text-ink-soft bg-sunken border border-line-subtle px-[9px] py-[3px] rounded-[5px]">{todayLabel}</span>
      </div>

      {/* Tab selector */}
      <div style={{ display: "flex", gap: 2, marginBottom: 18 }}>
        {(["paste", "manual"] as const).map(x => (
          <button key={x} onClick={() => setTab(x)} className={`${tab === x ? "bg-info-solid border border-info-bd text-white" : "bg-sunken border border-line-subtle text-ink-muted"} py-[7px] px-[18px] rounded-sm text-xs font-semibold cursor-pointer`}>
            {t(x === "paste" ? "bulkRtm.tabs.paste" : "bulkRtm.tabs.manual")}
          </button>
        ))}
      </div>

      {tab === "paste"
        ? <PastePanel today={today} employees={allEmployees} wicLocations={wicLocations} onSaved={refetch} />
        : <ManualPanel today={today} entries={todayEntries} employees={allEmployees} loading={loadingEntries} onChanged={refetch} />
      }
    </div>
  )
}

// ── Paste Panel ───────────────────────────────────────────────────────────────

function PastePanel({
  today, employees, wicLocations, onSaved,
}: {
  today: string
  employees: Employee[]
  wicLocations: WicLocation[]
  onSaved: () => void
}) {
  const { t } = useTranslation()
  const [text, setText]                         = useState("")
  const [category, setCategory]                 = useState<BulkRtmCategory>("AL")
  const [wicCenterDefault, setWicCenterDefault] = useState<string | null>(null)
  const [rows, setRows]                         = useState<ParsedRow[] | null>(null)
  const [rowOverrides, setRowOverrides]         = useState<(string | null)[]>([])
  const [homeWicMap, setHomeWicMap]             = useState<Record<string, HomeWicEntry>>({})
  const [homeWicLoading, setHomeWicLoading]     = useState(false)
  const [saving, setSaving]                     = useState(false)
  const [saveResults, setSaveResults]           = useState<SaveRowResult[] | null>(null)
  const [error, setError]                       = useState<string | null>(null)

  // When a user picks from the "Did you mean?" dropdown the row becomes savable.
  // Nothing is picked for the user — this is the explicit human choice.
  const handleSuggestionPick = (rowIndex: number, employeeId: string) => {
    const emp = employees.find(e => e.employeeId === employeeId)
    if (!emp || !rows) return
    setRows(rows.map((r, i) => i !== rowIndex ? r : {
      ...r, resolved: emp, status: "resolved-corrected" as const, matchType: "fuzzy" as const, picked: true,
    }))
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

    if (!text.trim()) { setError(t("bulkRtm.nothingToParse")); return }

    const parsedRows: ParsedRow[] = resolveAgentList(text, employees)
      .map((r, i) => ({ ...r, ordinal: i + 1 }))

    setRows(parsedRows)
    setRowOverrides(new Array(parsedRows.length).fill(null))

    if (category === "WIC") {
      const resolvedIds = parsedRows.filter(r => isRecognized(r.status) && r.resolved).map(r => r.resolved!.employeeId)
      if (resolvedIds.length > 0) {
        setHomeWicLoading(true)
        try {
          const data: HomeWicEntry[] = await fetch(`/api/rtm/home-wic?ids=${resolvedIds.join(",")}`)
            .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.json() })
          const map: Record<string, HomeWicEntry> = {}
          data.forEach(e => { map[e.employeeId] = e })
          setHomeWicMap(map)
        } catch (e) {
          setError(`${t("bulkRtm.reason.homeWicFailed")}: ${e}`)
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
        setError(`${t("bulkRtm.reason.clearBoFailed")}: ${e}`)
        setSaving(false)
        return
      }
    }

    for (let i = 0; i < rows.length; i++) {
      const row = rows[i]

      if (!isRecognized(row.status) || !row.resolved) {
        results.push({ name: row.rawName || row.rawLine, status: "skipped", reason: skipReason(t, row) })
        continue
      }

      const displayName = row.resolved.fullName ?? row.rawName
      const override = rowOverrides[i]
      const homeWic  = homeWicMap[row.resolved.employeeId]
      // Precedence unchanged: per-row override > WIC-center dropdown default > home WIC
      const locCode  = category === "WIC"
        ? (override ?? wicCenterDefault ?? homeWic?.locationCode ?? null)
        : null

      const writes = buildBulkRtmWrites(category, {
        employeeId: row.resolved.employeeId,
        displayName,
        date: today,
        locationCode: locCode,
      })

      if (writes.length === 0) {
        results.push({ name: displayName, status: "skipped", reason: t("bulkRtm.reason.noLocation") })
        continue
      }

      try {
        let handled = false
        for (const w of writes) {
          const r = await fetch(w.url, {
            method: w.method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(w.body),
          })
          const body = r.ok ? await r.json().catch(() => null) : await r.json().catch(() => ({}))
          if (!r.ok) {
            results.push({ name: displayName, status: "error", reason: body?.error ?? `HTTP ${r.status}` })
            handled = true
            break
          }
          if (w.kind === "wic") {
            if (body?.skipped) {
              results.push({ name: displayName, status: "skipped", reason: body.reason ?? t("bulkRtm.reason.wicRules") })
            } else {
              const locDisp = wicLocations.find(l => l.locationCode === locCode)?.displayName ?? locCode ?? ""
              results.push({ name: displayName, status: "saved", locationUsed: locDisp })
            }
            handled = true
          }
        }
        if (!handled) results.push({ name: displayName, status: "saved" })
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

  const resolvedCount   = rows?.filter(r => isRecognized(r.status)).length ?? 0
  const unresolvedCount = rows ? rows.length - resolvedCount : 0
  const stats = rows ? summarizeRows(rows) : { recognized: 0, notRecognized: 0 }
  // With a centre-level default every row has a location; noLocCount only matters without it.
  const noLocCount = category === "WIC" && rows && !wicCenterDefault
    ? rows.filter(r => isRecognized(r.status) && r.resolved && !rowOverrides[rows.indexOf(r)] && !homeWicMap[r.resolved.employeeId]?.locationCode).length
    : 0
  const saveDisabled = saving || resolvedCount === 0 || (category === "WIC" && homeWicLoading)

  const saveBtnLabel = (() => {
    if (saving) return t("bulkRtm.saving")
    if (category === "BO")  return t("bulkRtm.saveBo", { count: resolvedCount })
    if (category === "WIC") return t("bulkRtm.saveWic", { count: resolvedCount })
    return t("bulkRtm.saveRows", { count: resolvedCount })
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
            {t("bulkRtm.saveComplete", { saved: saveResults.filter(r => r.status === "saved").length })}
            {saveResults.some(r => r.status === "skipped") && t("bulkRtm.skippedCount", { count: saveResults.filter(r => r.status === "skipped").length })}
            {saveResults.some(r => r.status === "error") && t("bulkRtm.errorCount", { count: saveResults.filter(r => r.status === "error").length })}
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
            {t("bulkRtm.category")}
            <select
              value={category}
              onChange={e => {
                setCategory(e.target.value as BulkRtmCategory)
                setRows(null); setSaveResults(null); setError(null)
              }}
              className={`${inputCls} min-w-[230px]`}
            >
              {BULK_RTM_CATEGORIES.map(h => (
                <option key={h} value={h}>{t(`bulkRtm.cat.${h}`)}</option>
              ))}
            </select>
          </label>

          {category === "WIC" && (
            <label className="flex items-center gap-2 text-xs text-ink-muted font-semibold">
              {t("bulkRtm.wicCenter")}
              <select
                value={wicCenterDefault ?? ""}
                onChange={e => setWicCenterDefault(e.target.value || null)}
                className={`${inputCls} min-w-[230px]`}
              >
                <option value="">{t("bulkRtm.homeWic")}</option>
                {wicLocations.map(l => (
                  <option key={l.locationCode} value={l.locationCode}>{l.displayName}{l.isNpp ? " ★NPP" : ""}</option>
                ))}
              </select>
            </label>
          )}
        </div>

        {/* Format hint — one name per line, today, whole day, no time */}
        <div className="text-[11px] text-ink-soft mb-[10px] font-mono">
          {t("bulkRtm.hint")}
        </div>

        <textarea
          value={text}
          onChange={e => { setText(e.target.value); setRows(null); setSaveResults(null); setError(null) }}
          placeholder={t("bulkRtm.placeholder")}
          rows={10}
          className={`${inputCls} font-mono w-full`}
          style={{ resize: "vertical", lineHeight: 1.6 }}
        />
        <div style={{ display: "flex", gap: 10, marginTop: 12 }}>
          <button onClick={handleParse} disabled={!text.trim()} className={`${btnPrimary}${!text.trim() ? " opacity-50 cursor-not-allowed" : ""}`}>
            {t("bulkRtm.parse")}
          </button>
          {text.trim() && (
            <button onClick={() => { setText(""); setRows(null); setSaveResults(null); setError(null); setRowOverrides([]); setHomeWicMap({}) }} className={btnSecondary}>
              {t("bulkRtm.clear")}
            </button>
          )}
        </div>
      </div>

      {/* Preview */}
      {rows && rows.length > 0 && (
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
                {t(`bulkRtm.cat.${category}`)}
              </span>
              <span className="text-xs text-ink-soft">
                · {t("bulkRtm.lines", { count: rows.length })}
                {" "}<span className="text-good-fg">{t("bulkRtm.recognizedCount", { count: stats.recognized })}</span>
                {unresolvedCount > 0 && <span className="text-warn-fg">, {t("bulkRtm.notRecognizedCount", { count: unresolvedCount })}</span>}
                {homeWicLoading && <span className="text-ink-soft"> · {t("bulkRtm.homeWicLoading")}</span>}
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
              <strong>{t("bulkRtm.notSavedSkipped")}</strong>{" "}
              {rows.filter(r => !isRecognized(r.status)).map(r => r.rawName || r.rawLine).join(", ")}
            </div>
          )}

          {/* WIC no-home warning (suppressed when a center default is chosen) */}
          {category === "WIC" && !homeWicLoading && noLocCount > 0 && (
            <div className="text-[11px] text-crit-fg bg-crit-bg border border-crit-bd rounded-md py-[7px] px-3 mb-3">
              {t("bulkRtm.noHomeWic", { count: noLocCount })}
            </div>
          )}

          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th className={thCls}>{t("bulkRtm.col.num")}</th>
                  <th className={thCls}>{t("bulkRtm.col.pasted")}</th>
                  <th className={thCls}>{t("bulkRtm.col.recognized")}</th>
                  <th className={thCls}>{t("bulkRtm.col.category")}</th>
                  <th className={thCls}>{t("bulkRtm.col.date")}</th>
                  {category === "WIC" && <th className={`${thCls} min-w-[200px]`}>{t("bulkRtm.col.wicLocation")}</th>}
                  <th className={thCls}>{t("bulkRtm.col.status")}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => {
                  const homeWic  = row.resolved ? homeWicMap[row.resolved.employeeId] : undefined
                  const override = rowOverrides[i] ?? null
                  // Red background only when there is truly no resolvable location for this row
                  const hasNoLoc = category === "WIC" && isRecognized(row.status) && !override && !wicCenterDefault && !homeWic?.locationCode
                  const candidates = row.status === "ambiguous"
                    ? row.ambiguous
                    : (row.status === "suggest" || row.status === "unresolved" ? (row.suggestions ?? []) : [])
                  const needsPick = !isRecognized(row.status) && candidates.length > 0

                  return (
                    <tr key={i} className={`border-b border-line-subtle transition-colors ${hasNoLoc ? "bg-crit-bg/50" : "hover:bg-hovered"}`} style={{ opacity: isRecognized(row.status) ? 1 : 0.7 }}>
                      <td className={`${tdCls} text-ink-soft font-mono`} style={{ width: 32 }}>{i + 1}</td>
                      <td className={`${tdCls} font-mono text-[11px]`}>{row.rawLine}</td>
                      <td className={tdCls}>
                        {isRecognized(row.status) && row.resolved ? (
                          <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                            <span className="text-good-fg font-medium">{row.resolved.fullName ?? row.rawName}</span>
                            {row.picked && (
                              <span className="text-[10px] text-warn-fg bg-warn-bg border border-warn-bd rounded-[3px] px-[5px] py-[1px]">{t("bulkRtm.status.chosen")}</span>
                            )}
                          </div>
                        ) : row.status === "ambiguous" ? (
                          <span className="text-warn-fg">{t("bulkRtm.status.ambiguous")}</span>
                        ) : (
                          <span className="text-crit-fg">{t("bulkRtm.status.nameNotFound")}</span>
                        )}
                        {needsPick && (
                          <select
                            defaultValue=""
                            onChange={e => { if (e.target.value) handleSuggestionPick(i, e.target.value) }}
                            className={`${inputCls} text-[11px] py-1 px-2 min-w-[200px] mt-1 block`}
                          >
                            <option value="">{t("bulkRtm.status.didYouMean")}</option>
                            {candidates.map(s => (
                              <option key={s.employeeId} value={s.employeeId}>{s.fullName} ({s.employeeId})</option>
                            ))}
                          </select>
                        )}
                      </td>
                      <td className={`${tdCls} font-mono text-[11px]`}>{category}</td>
                      <td className={`${tdCls} font-mono text-[11px]`}>{today}</td>
                      {category === "WIC" && (
                        <td className={tdCls}>
                          {isRecognized(row.status) ? (
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
                                <option value="">{t("bulkRtm.homeWic")}</option>
                                {wicLocations.map(l => (
                                  <option key={l.locationCode} value={l.locationCode}>{l.displayName}{l.isNpp ? " ★NPP" : ""}</option>
                                ))}
                              </select>
                              {/* Effective location hint: per-row wins, then center default, then home WIC */}
                              {!override && (
                                homeWicLoading
                                  ? <span className="text-[10px] text-ink-soft italic">{t("bulkRtm.loading")}</span>
                                  : wicCenterDefault
                                    ? <span className="text-[10px] text-wic-fg flex items-center gap-[3px]">
                                        <MapPin size={9} /> {wicCenterName}
                                      </span>
                                    : homeWic?.displayName
                                      ? <span className="text-[10px] text-good-fg flex items-center gap-[3px]">
                                          <MapPin size={9} /> {homeWic.displayName}
                                        </span>
                                      : <span className="text-[10px] text-crit-fg">{t("bulkRtm.noHomeWicRow")}</span>
                              )}
                            </div>
                          ) : (
                            <span className="text-ink-soft text-[11px]">—</span>
                          )}
                        </td>
                      )}
                      <td className={tdCls}>
                        {isRecognized(row.status) && row.picked
                          ? <span className="flex items-center gap-1 text-good-fg text-[11px]"><CheckCircle2 size={13} /> {t("bulkRtm.status.chosen")}</span>
                          : isRecognized(row.status)
                            ? <span className="flex items-center gap-1 text-good-fg text-[11px]"><CheckCircle2 size={13} /> {row.status === "resolved-corrected" ? t("bulkRtm.status.corrected") : t("bulkRtm.status.recognized")}</span>
                            : row.status === "ambiguous"
                              ? <span className="flex items-center gap-1 text-warn-fg text-[11px]"><AlertCircle size={13} /> {t("bulkRtm.status.ambiguous")}</span>
                              : <span className="flex items-center gap-1 text-crit-fg text-[11px]"><XCircle size={13} /> {t("bulkRtm.status.nameNotFound")}</span>
                        }
                        {row.ignoredTimeOrDate && (
                          <div className="text-[10px] text-ink-soft mt-[3px]">{t("bulkRtm.status.timeIgnored")}</div>
                        )}
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
          {t("bulkRtm.noRows")}
        </div>
      )}
    </div>
  )
}

// ── Manual Panel ──────────────────────────────────────────────────────────────

function ManualPanel({
  today, entries, employees, loading, onChanged,
}: {
  today: string
  entries: RtmEntry[]
  employees: Employee[]
  loading: boolean
  onChanged: () => void
}) {
  const { t } = useTranslation()
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

  const [notice, setNotice] = useState<{ text: string; isError: boolean } | null>(null)

  const flash = (text: string, isError = false) => { setNotice({ text, isError }); setTimeout(() => setNotice(null), 3000) }

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
      flash(t("bulkRtm.manual.rowAdded"))
      onChanged()
    } catch (e) {
      flash(`${t("bulkRtm.manual.error")}: ${e}`, true)
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
      flash(t("bulkRtm.manual.rowUpdated"))
      onChanged()
    } catch (e) {
      flash(`${t("bulkRtm.manual.error")}: ${e}`, true)
    } finally {
      setEditBusy(false)
    }
  }

  const handleDelete = async (id: number) => {
    try {
      const res = await fetch(`/api/rtm/rows/${id}`, { method: "DELETE" })
      if (!res.ok && res.status !== 204) throw new Error(`HTTP ${res.status}`)
      flash(t("bulkRtm.manual.rowDeleted"))
      onChanged()
    } catch (e) {
      flash(`${t("bulkRtm.manual.error")}: ${e}`, true)
    }
  }

  return (
    <div>
      {notice && (
        <div className={`fixed bottom-6 right-6 z-toast ${notice.isError ? "bg-crit-solid" : "bg-good-solid"} text-white px-[18px] py-[10px] rounded-lg text-xs`}>
          {notice.text}
        </div>
      )}

      <div className={card}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 16 }}>
          <span className="text-[13px] font-semibold text-ink">
            {t("bulkRtm.manual.listTitle")}
            <span className="text-[11px] text-ink-soft font-normal ml-2 font-mono">{today}</span>
          </span>
          <button onClick={() => setAddOpen(!addOpen)} className={`${btnPrimary} flex items-center gap-1.5`}>
            <Plus size={13} /> {t("bulkRtm.manual.addRow")}
          </button>
        </div>

        {/* Add row form */}
        {addOpen && (
          <div className="bg-sunken border border-line-subtle rounded-lg py-[14px] px-4 mb-4">
            <div style={{ display: "grid", gridTemplateColumns: "1fr 90px 90px 160px auto", gap: 10, alignItems: "end" }}>
              <label className="text-[11px] text-ink-soft">
                {t("bulkRtm.manual.agent")}
                <select value={addEmp} onChange={e => setAddEmp(e.target.value)}
                  className={`${inputCls} block mt-1 w-full`}>
                  <option value="">{t("bulkRtm.select")}</option>
                  {employees.map(e => (
                    <option key={e.employeeId} value={e.employeeId}>{e.fullName ?? e.employeeId}</option>
                  ))}
                </select>
              </label>
              <label className="text-[11px] text-ink-soft">
                {t("bulkRtm.manual.start")}
                <input type="time" value={addStart} onChange={e => setAddStart(e.target.value)}
                  className={`${monoInputCls} block mt-1 w-full`} />
              </label>
              <label className="text-[11px] text-ink-soft">
                {t("bulkRtm.manual.end")}
                <input type="time" value={addEnd} onChange={e => setAddEnd(e.target.value)}
                  className={`${monoInputCls} block mt-1 w-full`} />
              </label>
              <label className="text-[11px] text-ink-soft">
                {t("bulkRtm.manual.tagOptional")}
                <input type="text" value={addTag} onChange={e => setAddTag(e.target.value)}
                  placeholder={t("bulkRtm.manual.tagPlaceholder")}
                  className={`${inputCls} block mt-1 w-full`} />
              </label>
              <div style={{ display: "flex", gap: 8, paddingBottom: 1 }}>
                <button onClick={handleAdd} disabled={!addEmp || addBusy}
                  className={`${btnPrimary}${!addEmp || addBusy ? " opacity-50 cursor-not-allowed" : ""}`}>
                  {addBusy ? "…" : t("bulkRtm.manual.add")}
                </button>
                <button onClick={() => { setAddOpen(false); setAddEmp(""); setAddTag("") }} className={btnSecondary}>
                  {t("bulkRtm.manual.cancel")}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Entries table */}
        {loading ? (
          <div className="py-6 text-center text-ink-soft text-[13px]">{t("bulkRtm.loading")}</div>
        ) : entries.length === 0 ? (
          <div className="py-8 text-center text-ink-soft text-[13px]">
            {t("bulkRtm.manual.noEntries")}
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr>
                  <th className={thCls}>{t("bulkRtm.col.num")}</th>
                  <th className={thCls}>{t("bulkRtm.manual.employee")}</th>
                  <th className={thCls}>{t("bulkRtm.manual.id")}</th>
                  <th className={thCls}>{t("bulkRtm.manual.hours")}</th>
                  <th className={thCls}>{t("bulkRtm.manual.tag")}</th>
                  <th className={thCls}>{t("bulkRtm.manual.source")}</th>
                  <th className={`${thCls} text-right`}>{t("bulkRtm.manual.actions")}</th>
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
                            {editBusy ? "…" : t("bulkRtm.manual.save")}
                          </button>
                          <button onClick={() => setEditId(null)} className="bg-sunken border border-line-subtle text-ink-muted py-1 px-[10px] rounded-sm text-[11px] cursor-pointer">
                            {t("bulkRtm.manual.cancel")}
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
                        {e.sourceLine ?? t("bulkRtm.manual.manualSource")}
                      </td>
                      <td className={`${tdCls} text-right`}>
                        <div style={{ display: "flex", justifyContent: "flex-end", gap: 6 }}>
                          <button onClick={() => openEdit(e)} title={t("bulkRtm.manual.save")}
                            className="bg-info-bg border border-info-bd text-info-fg p-1 rounded-[5px] cursor-pointer flex items-center">
                            <Pencil size={12} />
                          </button>
                          <button onClick={() => handleDelete(e.id)} title={t("bulkRtm.manual.actions")}
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
