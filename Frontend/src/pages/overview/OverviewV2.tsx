import { useMemo, useState } from "react"
import { useTranslation } from "react-i18next"
import { groupGaps, type CoverageGap, type GapGroup, type GapAgent, type FloaterSuggestion } from "../../utils/groupGaps"
import { postSubstituteAccept, substituteAcceptErrorText } from "../../utils/substituteAccept"

interface Props {
  gaps: CoverageGap[]
  heatDates: string[]
  today: string
  absentToday: number | null
}

type ReasonCode = "annual-leave" | "bo" | "other-wic" | "not-rostered" | "no-task" | "other"

function reasonOf(a: GapAgent): ReasonCode {
  const s = (a.status ?? "").toUpperCase()
  const task = (a.taskLabel ?? "").toUpperCase()
  if (["AL", "HALF_AL", "UL", "OL", "SL", "PH", "LPH", "OFF", "OFF_WEEKEND", "RESIGNED"].includes(s)) return "annual-leave"
  if (s === "BO" || task === "BO") return "bo"
  if (s === "WIC_DUTY" || task.startsWith("WIC")) return "other-wic"
  if (s === "NONE" || s === "") return "not-rostered"
  if (s === "WORKING" && task === "") return "no-task"
  return "other"
}

const REASON_TOKEN: Record<ReasonCode, string> = {
  "annual-leave": "rgb(var(--ov2-annualleave-bg))",
  "bo": "rgb(var(--ov2-bo-bg))",
  "other-wic": "rgb(var(--ov2-otherwic-bg))",
  "not-rostered": "rgb(var(--ov2-notrostered-bg))",
  "no-task": "rgb(var(--ov2-notask-bg))",
  "other": "rgb(var(--ov2-notask-bg))",
}
const REASON_FG: Record<ReasonCode, string> = {
  "annual-leave": "rgb(var(--ov2-annualleave-fg))",
  "bo": "rgb(var(--ov2-bo-fg))",
  "other-wic": "rgb(var(--ov2-otherwic-fg))",
  "not-rostered": "rgb(var(--ov2-notrostered-fg))",
  "no-task": "rgb(var(--ov2-notask-fg))",
  "other": "rgb(var(--ov2-notask-fg))",
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? "").join("")
}

function fmtRange(from: string, to: string): string {
  const f = new Date(from), t = new Date(to)
  const mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
  const fKey = `${mon[f.getMonth()]} ${String(f.getDate()).padStart(2, "0")}`
  const tKey = `${mon[t.getMonth()]} ${String(t.getDate()).padStart(2, "0")}`
  return from === to ? fKey : `${fKey} – ${tKey}`
}

export function OverviewV2({ gaps, heatDates, today, absentToday }: Props) {
  const { t } = useTranslation()
  const [selectedDay, setSelectedDay] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<{ group: GapGroup; suggestion: FloaterSuggestion; target: string[] } | null>(null)
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<{ ok: string[]; failed: Record<string, string> } | null>(null)

  const groups = useMemo(() => groupGaps(gaps), [gaps])
  const uncoveredCount = gaps.filter(g => g.present === 0).length
  const partialCount = gaps.length - uncoveredCount
  const filtered = selectedDay ? groups.filter(g => g.days.includes(selectedDay)) : groups

  const dayStatus = useMemo(() => {
    const m = new Map<string, { uncovered: number; partial: number }>()
    for (const g of gaps) {
      const e = m.get(g.date) ?? { uncovered: 0, partial: 0 }
      if (g.present === 0) e.uncovered++
      else e.partial++
      m.set(g.date, e)
    }
    return m
  }, [gaps])

  async function doAssign(group: GapGroup, s: FloaterSuggestion, dates: string[]) {
    setBusy(true)
    setResult(null)
    const ok: string[] = []
    const failed: Record<string, string> = {}
    for (const d of dates) {
      try {
        await postSubstituteAccept({ employeeId: s.employeeId, locationCode: group.locationCode, date: d, sourceType: s.assignmentType })
        ok.push(d)
      } catch (e) {
        failed[d] = substituteAcceptErrorText(e, t)
      }
    }
    setResult({ ok, failed })
    setBusy(false)
    setConfirm(null)
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* 1. Problem bar */}
      {gaps.length > 0 && (
        <div style={{
          display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap",
          padding: "10px 14px", borderRadius: 8,
          background: "rgb(var(--ov2-uncovered-bg))", color: "rgb(var(--ov2-uncovered-fg))",
          border: "1px solid rgb(var(--ov2-uncovered-bar) / .35)",
        }}>
          <strong style={{ fontSize: 14 }}>{t("overviewV2.problem.title", { count: gaps.length })}</strong>
          <span style={{ fontSize: 12 }}>
            {t("overviewV2.problem.summary", { groups: groups.length, uncovered: uncoveredCount, partial: partialCount, absent: absentToday ?? 0 })}
          </span>
          <button
            onClick={() => document.getElementById("ov2-gap-list")?.scrollIntoView({ behavior: "smooth", block: "start" })}
            style={{ marginLeft: "auto", padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-uncovered-bar))", color: "rgb(var(--ov2-uncovered-bg))" }}
          >
            {t("overviewV2.problem.review")}
          </button>
        </div>
      )}

      {/* 2. Coverage risk bar (28 days, click-to-filter) */}
      <div style={{ display: "flex", gap: 4, overflowX: "auto", paddingBottom: 6 }}>
        {heatDates.map(d => {
          const st = dayStatus.get(d)
          const uncovered = st?.uncovered ?? 0
          const partial = st?.partial ?? 0
          const isToday = d === today
          const isSel = selectedDay === d
          const dt = new Date(d)
          return (
            <button
              key={d}
              onClick={() => setSelectedDay(isSel ? null : d)}
              title={`${d}: ${uncovered} uncovered, ${partial} partial`}
              style={{
                flex: "0 0 34px", height: 44, borderRadius: 6, border: isToday ? "2px solid rgb(var(--focus-ring))" : "1px solid rgb(var(--border-subtle))",
                background: "rgb(var(--surface-raised))", cursor: "pointer", padding: 3,
                display: "flex", flexDirection: "column", alignItems: "center", gap: 2,
                outline: isSel ? "2px solid rgb(var(--focus-ring))" : "none",
              }}
            >
              <span style={{ fontSize: 9, color: "rgb(var(--text-tertiary))" }}>{["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]}</span>
              <span style={{ fontSize: 11, fontWeight: 600, color: "rgb(var(--text-primary))" }}>{String(dt.getDate()).padStart(2, "0")}</span>
              <span style={{ display: "flex", width: "100%", height: 5, borderRadius: 3, overflow: "hidden", background: "rgb(var(--surface-sunken))" }}>
                <span style={{ width: `${Math.min(100, uncovered * 34)}%`, background: "rgb(var(--ov2-uncovered-bar))" }} />
                <span style={{ width: `${Math.min(100, partial * 34)}%`, background: "rgb(var(--ov2-partial-bar))" }} />
              </span>
            </button>
          )
        })}
      </div>

      {/* 3. Grouped gaps list */}
      <div id="ov2-gap-list" style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {filtered.length === 0 && (
          <div style={{ padding: 20, textAlign: "center", color: "rgb(var(--text-tertiary))", fontSize: 13 }}>
            {selectedDay ? t("overviewV2.emptyFiltered") : t("overviewV2.empty")}
          </div>
        )}
        {filtered.map((grp, i) => (
          <div key={`${grp.locationCode}-${grp.from}-${i}`} style={{
            border: "1px solid rgb(var(--border-subtle))", borderRadius: 10, overflow: "hidden",
            background: "rgb(var(--surface-raised))",
          }}>
            {/* left status bar */}
            <div style={{ display: "flex" }}>
              <div style={{ width: 6, alignSelf: "stretch", background: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-bar))" : "rgb(var(--ov2-partial-bar))" }} />
              <div style={{ flex: 1, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
                {/* row 1: status + location + date + time + present/min */}
                <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
                  <span style={{
                    padding: "1px 8px", borderRadius: 999, fontSize: 11, fontWeight: 700,
                    background: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-bg))" : "rgb(var(--ov2-partial-bg))",
                    color: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-fg))" : "rgb(var(--ov2-partial-fg))",
                  }}>{grp.status}</span>
                  <strong style={{ fontSize: 14 }}>{grp.displayName}</strong>
                  <span style={{ fontFamily: "monospace", fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{grp.locationCode}</span>
                  <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{fmtRange(grp.from, grp.to)}</span>
                  <span style={{ fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{t("overviewV2.days", { count: grp.dayCount })}</span>
                  {grp.time && <span style={{ fontFamily: "monospace", fontSize: 12, color: "rgb(var(--text-secondary))" }}>{grp.time}</span>}
                  <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
                    <div style={{ display: "flex", gap: 2, width: 40 }}>
                      {Array.from({ length: Math.max(grp.required, 1) }).map((_, j) => (
                        <span key={j} style={{ height: 8, flex: 1, borderRadius: 2, background: j < grp.present ? "rgb(var(--st-good-solid))" : "rgb(var(--surface-sunken))" }} />
                      ))}
                    </div>
                    <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{grp.present}/{grp.required}</span>
                  </div>
                </div>

                {/* row 2: person chips */}
                {grp.agents.length > 0 && (
                  <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                    {grp.agents.map((a, j) => {
                      const rc = reasonOf(a)
                      return (
                        <span key={j} style={{
                          display: "inline-flex", alignItems: "center", gap: 6, padding: "2px 8px 2px 3px", borderRadius: 999,
                          background: REASON_TOKEN[rc], color: REASON_FG[rc], fontSize: 11, fontWeight: 500,
                        }}>
                          <span style={{ width: 20, height: 20, borderRadius: 999, background: "rgb(var(--surface-raised))", color: "rgb(var(--text-primary))", display: "grid", placeItems: "center", fontSize: 10, fontWeight: 700 }}>{initials(a.fullName)}</span>
                          <span>{a.fullName}</span>
                          <span style={{ opacity: .7 }}>{a.assignmentType}</span>
                          <span style={{ fontStyle: "italic" }}>{t(`overviewV2.reason.${rc}`)}</span>
                        </span>
                      )
                    })}
                  </div>
                )}

                {/* row 3: suggestion + assign */}
                {grp.suggestions.length > 0 && (
                  <div style={{ border: `1px solid rgb(var(--ov2-suggest-border))`, background: "rgb(var(--ov2-suggest-bg))", borderRadius: 8, padding: "8px 10px", display: "flex", flexDirection: "column", gap: 6 }}>
                    {grp.suggestions.slice(0, 1).map(s => {
                      const all = s.availableDates.length === grp.dayCount
                      return (
                        <div key={s.employeeId} style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
                          <span style={{ fontSize: 12, color: "rgb(var(--text-primary))" }}>
                            <strong>{t("overviewV2.suggested")}:</strong> {s.fullName}
                            <span style={{ color: "rgb(var(--text-tertiary))" }}> · {all ? t("overviewV2.availableAllDays") : t("overviewV2.availableXDays", { count: s.availableDates.length })}</span>
                          </span>
                          <div style={{ marginLeft: "auto", display: "flex", gap: 6 }}>
                            <button onClick={() => setConfirm({ group: grp, suggestion: s, target: grp.days })} style={{ padding: "4px 10px", borderRadius: 6, fontSize: 12, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer" }}>
                              {t("overviewV2.pickDays")}
                            </button>
                            <button onClick={() => setConfirm({ group: grp, suggestion: s, target: s.availableDates })} style={{ padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-assign-bg))", color: "rgb(var(--ov2-assign-fg))" }}>
                              {all ? t("overviewV2.assignAll", { count: grp.dayCount }) : t("overviewV2.assignX", { count: s.availableDates.length })}
                            </button>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* confirm dialog (multi-day) */}
      {confirm && (
        <div style={{ position: "fixed", inset: 0, background: "rgb(var(--surface-scrim) / .5)", display: "grid", placeItems: "center", zIndex: 50 }}>
          <div style={{ background: "rgb(var(--surface-overlay))", border: "1px solid rgb(var(--border-default))", borderRadius: 12, padding: 18, maxWidth: 420, width: "100%" }}>
            <h3 style={{ fontSize: 15, margin: "0 0 8px" }}>{t("overviewV2.confirm.title", { name: confirm.suggestion.fullName })}</h3>
            <div style={{ maxHeight: 160, overflowY: "auto", display: "flex", flexDirection: "column", gap: 4, marginBottom: 12 }}>
              {confirm.target.map(d => (
                <div key={d} style={{ fontSize: 13 }}>{d} · {confirm.group.displayName}</div>
              ))}
            </div>
            <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
              <button onClick={() => setConfirm(null)} style={{ padding: "6px 12px", borderRadius: 6, fontSize: 13, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer" }}>{t("overviewV2.cancel")}</button>
              <button disabled={busy} onClick={() => doAssign(confirm.group, confirm.suggestion, confirm.target)} style={{ padding: "6px 12px", borderRadius: 6, fontSize: 13, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-assign-bg))", color: "rgb(var(--ov2-assign-fg))" }}>{t("overviewV2.confirm.confirm")}</button>
            </div>
          </div>
        </div>
      )}

      {/* result */}
      {result && (
        <div style={{ padding: "8px 12px", borderRadius: 8, fontSize: 13, background: "rgb(var(--st-info-bg))", color: "rgb(var(--st-info-fg))" }}>
          {Object.keys(result.failed).length === 0
            ? t("overviewV2.assignOk", { count: result.ok.length })
            : t("overviewV2.assignPartial", { ok: result.ok.length, failed: Object.keys(result.failed).length }) + " " + Object.entries(result.failed).map(([d, e]) => `${d}: ${e}`).join("; ")}
        </div>
      )}
    </div>
  )
}
