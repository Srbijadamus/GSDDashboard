import { useMemo, useState } from "react"
import { useTranslation } from "react-i18next"
import { groupGapsV21, reasonOf, isoWeek, type CoverageGap, type GapGroupV21, type CandidateV21, type ReasonCode } from "../../utils/groupGaps"
import { postSubstituteAccept, substituteAcceptErrorText } from "../../utils/substituteAccept"

interface Props {
  gaps: CoverageGap[]
  heatDates: string[]
  today: string
  absentToday: number | null
  onFindSubstitute: (args: { locationCode: string; date: string; displayName: string }) => void
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

function shortCode(city: string): string {
  return city.replace(/[^A-Za-z]/g, "").slice(0, 3).toUpperCase()
}
function initials(name: string): string {
  return name.trim().split(/\s+/).slice(0, 2).map(p => p[0]?.toUpperCase() ?? "").join("")
}
function fmtRange(from: string, to: string): string {
  const f = new Date(from), t = new Date(to)
  const mon = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
  const fKey = `${mon[f.getMonth()]} ${String(f.getDate()).padStart(2, "0")}`
  const tKey = `${mon[t.getMonth()]} ${String(t.getDate()).padStart(2, "0")}`
  return from === to ? fKey : `${fKey} – ${tKey}`
}

export function OverviewV21({ gaps, heatDates, today, absentToday, onFindSubstitute }: Props) {
  const { t } = useTranslation()
  const [selectedDay, setSelectedDay] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<{ group: GapGroupV21; candidate: CandidateV21; dates: string[] } | null>(null)
  const [openGroup, setOpenGroup] = useState<string | null>(null)
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<{ ok: string[]; failed: Record<string, string> } | null>(null)

  const groups = useMemo(() => groupGapsV21(gaps), [gaps])
  const uncoveredCount = gaps.filter(g => g.present === 0).length
  const partialCount = gaps.length - uncoveredCount
  const filtered = selectedDay ? groups.filter(g => g.days.some(d => d.date === selectedDay)) : groups

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

  async function doAssign(group: GapGroupV21, candidate: CandidateV21, dates: string[]) {
    if (!candidate.sourceType) return
    setBusy(true)
    setResult(null)
    const ok: string[] = []
    const failed: Record<string, string> = {}
    for (const d of dates) {
      try {
        await postSubstituteAccept({ employeeId: candidate.employeeId, locationCode: group.locationCode, date: d, sourceType: candidate.sourceType })
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
      {gaps.length > 0 && (
        <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap", padding: "10px 14px", borderRadius: 8, background: "rgb(var(--ov2-uncovered-bg))", color: "rgb(var(--ov2-uncovered-fg))", border: "1px solid rgb(var(--ov2-uncovered-bar) / .35)" }}>
          <strong style={{ fontSize: 14 }}>{t("overviewV2.problem.title", { count: gaps.length })}</strong>
          <span style={{ fontSize: 12 }}>{t("overviewV2.problem.summary", { groups: groups.length, uncovered: uncoveredCount, partial: partialCount, absent: absentToday ?? 0 })}</span>
          <button onClick={() => document.getElementById("ov2-gap-list")?.scrollIntoView({ behavior: "smooth", block: "start" })} style={{ marginLeft: "auto", padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-uncovered-bar))", color: "rgb(var(--ov2-uncovered-bg))" }}>{t("overviewV2.problem.review")}</button>
        </div>
      )}

      <div style={{ display: "flex", gap: 4, overflowX: "auto", paddingBottom: 6 }}>
        {heatDates.map(d => {
          const st = dayStatus.get(d)
          const isToday = d === today
          const isSel = selectedDay === d
          const dt = new Date(d)
          return (
            <button key={d} onClick={() => setSelectedDay(isSel ? null : d)} title={`${d}: ${st?.uncovered ?? 0} uncovered, ${st?.partial ?? 0} partial`} style={{ flex: "0 0 34px", height: 44, borderRadius: 6, border: isToday ? "2px solid rgb(var(--focus-ring))" : "1px solid rgb(var(--border-subtle))", background: "rgb(var(--surface-raised))", cursor: "pointer", padding: 3, display: "flex", flexDirection: "column", alignItems: "center", gap: 2, outline: isSel ? "2px solid rgb(var(--focus-ring))" : "none" }}>
              <span style={{ fontSize: 9, color: "rgb(var(--text-tertiary))" }}>{["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]}</span>
              <span style={{ fontSize: 11, fontWeight: 600, color: "rgb(var(--text-primary))" }}>{String(dt.getDate()).padStart(2, "0")}</span>
              <span style={{ display: "flex", width: "100%", height: 5, borderRadius: 3, overflow: "hidden", background: "rgb(var(--surface-sunken))" }}>
                <span style={{ width: `${Math.min(100, (st?.uncovered ?? 0) * 34)}%`, background: "rgb(var(--ov2-uncovered-bar))" }} />
                <span style={{ width: `${Math.min(100, (st?.partial ?? 0) * 34)}%`, background: "rgb(var(--ov2-partial-bar))" }} />
              </span>
            </button>
          )
        })}
      </div>

      <div id="ov2-gap-list" style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {filtered.length === 0 && (
          <div style={{ padding: 20, textAlign: "center", color: "rgb(var(--text-tertiary))", fontSize: 13 }}>{selectedDay ? t("overviewV2.emptyFiltered") : t("overviewV2.empty")}</div>
        )}
        {filtered.map(grp => {
          const key = `${grp.locationCode}-${grp.from}`
          const open = openGroup === key
          const needed = Math.max(0, grp.days[0].required - grp.days[0].present)
          const visibleCandidates = expanded[key] ? grp.candidates : grp.candidates.slice(0, 3)
          return (
            <div key={key} style={{ border: "1px solid rgb(var(--border-subtle))", borderRadius: 10, overflow: "hidden", background: "rgb(var(--surface-raised))" }}>
              <div style={{ display: "flex" }}>
                <div style={{ width: 6, alignSelf: "stretch", background: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-bar))" : "rgb(var(--ov2-partial-bar))" }} />
                <div style={{ flex: 1, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
                    <span style={{ padding: "1px 8px", borderRadius: 999, fontSize: 11, fontWeight: 700, background: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-bg))" : "rgb(var(--ov2-partial-bg))", color: grp.status === "UNCOVERED" ? "rgb(var(--ov2-uncovered-fg))" : "rgb(var(--ov2-partial-fg))" }}>{grp.status}</span>
                    <strong style={{ fontSize: 14 }}>{grp.displayName}</strong>
                    <span title={grp.locationCode} style={{ fontFamily: "monospace", fontSize: 12, color: "rgb(var(--text-tertiary))", cursor: "help", borderBottom: "1px dotted rgb(var(--border-strong))" }}>{shortCode(grp.city)}</span>
                    <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{fmtRange(grp.from, grp.to)}</span>
                    <span style={{ fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{t("overviewV2.days", { count: grp.dayCount })}</span>
                    {grp.weeks.length > 1 && <span style={{ fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{t("overviewV21.weeks", { weeks: grp.weeks.map(w => w.week).join(", ") })}</span>}
                    <span style={{ fontFamily: "monospace", fontSize: 12, color: "rgb(var(--text-secondary))" }}>{grp.timeVaries ? t("overviewV21.differentHours") : grp.time}</span>
                    <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{grp.days[0].present}/{grp.days[0].required}</span>
                    {needed > 0 && <span style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--st-warn-fg))" }}>{t("overviewV21.needsMore", { count: needed })}</span>}
                    <div style={{ marginLeft: "auto", display: "flex", alignItems: "center", gap: 6 }}>
                      <button onClick={() => onFindSubstitute({ locationCode: grp.locationCode, date: grp.days[0].date, displayName: grp.displayName })} style={{ fontSize: 11, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", borderRadius: 6, padding: "2px 8px", cursor: "pointer" }}>{t("overviewV21.findSubstitute")}</button>
                      <button onClick={() => setOpenGroup(open ? null : key)} style={{ fontSize: 11, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", borderRadius: 6, padding: "2px 8px", cursor: "pointer" }}>{open ? t("overviewV21.hideDays") : t("overviewV21.showDays", { count: grp.dayCount })}</button>
                    </div>
                  </div>

                  {grp.agents.length > 0 && (
                    <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                      {grp.agents.map((a, j) => {
                        const rc = a.varies ? "other" : (a.reason as ReasonCode)
                        return (
                          <span key={j} style={{ display: "inline-flex", alignItems: "center", gap: 6, padding: "2px 8px 2px 3px", borderRadius: 999, background: REASON_TOKEN[rc], color: REASON_FG[rc], fontSize: 11, fontWeight: 500 }}>
                            <span style={{ width: 20, height: 20, borderRadius: 999, background: "rgb(var(--surface-raised))", color: "rgb(var(--text-primary))", display: "grid", placeItems: "center", fontSize: 10, fontWeight: 700 }}>{initials(a.fullName)}</span>
                            <span>{a.fullName}</span>
                            <span style={{ opacity: .7 }}>{a.assignmentType}</span>
                            {a.varies ? <span style={{ fontStyle: "italic" }}>{t("overviewV21.varies")}</span> : <span style={{ fontStyle: "italic" }}>{t(`overviewV2.reason.${rc}`)}</span>}
                          </span>
                        )
                      })}
                    </div>
                  )}

                  {grp.candidates.length === 0 ? (
                    <div style={{ border: `1px solid rgb(var(--ov2-suggest-border))`, background: "rgb(var(--ov2-suggest-bg))", borderRadius: 8, padding: "8px 10px", display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
                      <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{t("overviewV21.noOneAvailable")}</span>
                      <button onClick={() => onFindSubstitute({ locationCode: grp.locationCode, date: grp.days[0].date, displayName: grp.displayName })} style={{ marginLeft: "auto", padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--action-solid))", color: "rgb(var(--action-fg))" }}>{t("overviewV21.findSubstitute")}</button>
                    </div>
                  ) : (
                    <div style={{ border: `1px solid rgb(var(--ov2-suggest-border))`, background: "rgb(var(--ov2-suggest-bg))", borderRadius: 8, padding: "8px 10px", display: "flex", flexDirection: "column", gap: 6 }}>
                      {visibleCandidates.map(c => {
                        const all = c.availableDates.length === grp.dayCount
                        const x = c.availableDates.length
                        const firstWeekDates = grp.weeks.length > 0 ? grp.weeks[0].days.map(d => d.date) : []
                        const thisWeekDates = c.availableDates.filter(d => firstWeekDates.includes(d))
                        return (
                          <div key={c.employeeId} style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
                            <span style={{ fontSize: 12, color: "rgb(var(--text-primary))" }}><strong>{c.fullName}</strong></span>
                            <span style={{ fontSize: 12, color: "rgb(var(--text-tertiary))" }}>{c.source}</span>
                            <span style={{ fontSize: 12, color: "rgb(var(--text-secondary))" }}>{t("overviewV21.availableXOfN", { x, n: grp.dayCount })}</span>
                            <div style={{ marginLeft: "auto", display: "flex", gap: 6 }}>
                              {!c.sourceType ? (
                                <span style={{ fontSize: 12, color: "rgb(var(--st-warn-fg))" }}>{t("overviewV21.noSourceType")}</span>
                              ) : (
                                <>
                                  {grp.weeks.length > 1 && thisWeekDates.length > 0 && (
                                    <button onClick={() => setConfirm({ group: grp, candidate: c, dates: thisWeekDates })} style={{ padding: "4px 10px", borderRadius: 6, fontSize: 12, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer" }}>{t("overviewV21.assignThisWeek")}</button>
                                  )}
                                  <button onClick={() => setConfirm({ group: grp, candidate: c, dates: c.availableDates })} style={{ padding: "4px 12px", borderRadius: 6, fontSize: 12, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-assign-bg))", color: "rgb(var(--ov2-assign-fg))" }}>
                                    {grp.weeks.length > 1 ? t("overviewV21.assignAllWeeks", { count: x }) : (all ? t("overviewV2.assignAll", { count: grp.dayCount }) : t("overviewV2.assignX", { count: x }))}
                                  </button>
                                </>
                              )}
                            </div>
                          </div>
                        )
                      })}
                      {grp.candidates.length > 3 && (
                        <button onClick={() => setExpanded(prev => ({ ...prev, [key]: !prev[key] }))} style={{ alignSelf: "flex-start", fontSize: 11, border: "none", background: "transparent", color: "rgb(var(--focus-ring))", cursor: "pointer", padding: 0 }}>
                          {expanded[key] ? t("overviewV21.showLess") : t("overviewV21.showAll", { count: grp.candidates.length - 3 })}
                        </button>
                      )}
                    </div>
                  )}

                  {open && (
                    <div style={{ borderTop: "1px solid rgb(var(--border-subtle))", paddingTop: 6, display: "flex", flexDirection: "column", gap: 4 }}>
                      {grp.days.map(d => (
                        <div key={d.date} style={{ fontSize: 12, display: "flex", gap: 10, alignItems: "center", color: "rgb(var(--text-secondary))", flexWrap: "wrap" }}>
                          <span style={{ fontFamily: "monospace" }}>{d.date}</span>
                          <span style={{ fontFamily: "monospace" }}>{d.time || "—"}</span>
                          <span>{d.present}/{d.required}</span>
                          <span style={{ display: "flex", gap: 4, flexWrap: "wrap" }}>
                            {d.agents.map((a, j) => { const rc = reasonOf(a); return <span key={j} style={{ padding: "0 6px", borderRadius: 999, background: REASON_TOKEN[rc], color: REASON_FG[rc], fontSize: 10 }}>{a.fullName} · {t(`overviewV2.reason.${rc}`)}</span> })}
                          </span>
                          <button onClick={() => onFindSubstitute({ locationCode: grp.locationCode, date: d.date, displayName: grp.displayName })} style={{ fontSize: 10, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", borderRadius: 4, padding: "1px 6px", cursor: "pointer" }}>{t("overviewV21.findSubstitute")}</button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>
          )
        })}
      </div>

      {confirm && (
        <div style={{ position: "fixed", inset: 0, background: "rgb(var(--surface-scrim) / .5)", display: "grid", placeItems: "center", zIndex: 50 }}>
          <div style={{ background: "rgb(var(--surface-overlay))", border: "1px solid rgb(var(--border-default))", borderRadius: 12, padding: 18, maxWidth: 420, width: "100%" }}>
            <h3 style={{ fontSize: 15, margin: "0 0 8px" }}>{t("overviewV2.confirm.title", { name: confirm.candidate.fullName })}</h3>
            {confirm.dates.length > 5 && (
              <div style={{ fontSize: 12, color: "rgb(var(--text-tertiary))", marginBottom: 8 }}>
                {t("overviewV21.weekCount", { count: new Set(confirm.dates.map(d => isoWeek(new Date(d)))).size })}
                {confirm.candidate.source !== "rostered, no task" && <div style={{ color: "rgb(var(--st-warn-fg))", marginTop: 4 }}>{t("overviewV21.weekWarning")}</div>}
              </div>
            )}
            <div style={{ maxHeight: 160, overflowY: "auto", display: "flex", flexDirection: "column", gap: 4, marginBottom: 12 }}>
              {confirm.dates.map(d => <div key={d} style={{ fontSize: 13 }}>{d} · {confirm.group.displayName}</div>)}
            </div>
            <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
              <button onClick={() => setConfirm(null)} style={{ padding: "6px 12px", borderRadius: 6, fontSize: 13, border: "1px solid rgb(var(--border-default))", background: "transparent", color: "rgb(var(--text-secondary))", cursor: "pointer" }}>{t("overviewV2.cancel")}</button>
              <button disabled={busy} onClick={() => doAssign(confirm.group, confirm.candidate, confirm.dates)} style={{ padding: "6px 12px", borderRadius: 6, fontSize: 13, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--ov2-assign-bg))", color: "rgb(var(--ov2-assign-fg))" }}>{t("overviewV2.confirm.confirm")}</button>
            </div>
          </div>
        </div>
      )}

      {result && (
        <div style={{ padding: "8px 12px", borderRadius: 8, fontSize: 13, background: "rgb(var(--st-info-bg))", color: "rgb(var(--st-info-fg))" }}>
          {Object.keys(result.failed).length === 0 ? t("overviewV2.assignOk", { count: result.ok.length }) : t("overviewV2.assignPartial", { ok: result.ok.length, failed: Object.keys(result.failed).length }) + " " + Object.entries(result.failed).map(([d, e]) => `${d}: ${e}`).join("; ")}
        </div>
      )}
    </div>
  )
}
