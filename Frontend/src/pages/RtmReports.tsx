import { useMemo, useState, type ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Copy, Download, CheckCheck, CalendarDays, CalendarRange, ClipboardCheck } from "lucide-react"
import { apiFetch, downloadExcel } from "../api/client"

// ── API types (camelCase, as serialised by the backend) ───────────────────────

interface RtmPlanAgent { employeeId: string; name: string; start: string | null; end: string | null }
interface RtmPlanDay { date: string; status: "CLOSED" | "UNCOVERED" | "COVERED"; agents: RtmPlanAgent[] }
interface RtmLocationPlan { locationCode: string; city: string; displayName: string; country: string; days: RtmPlanDay[] }
interface RtmPlanResponse { startDate: string; endDate: string; locations: RtmLocationPlan[] }

interface RtmPresentAgent { employeeId: string; name: string; checkIn: string | null; checkOut: string | null }
interface RtmAttendanceDay {
  date: string
  status: "CLOSED" | "NOT_PRESENT" | "PRESENT"
  present: RtmPresentAgent[]
  plannedAbsent: string[]
}
interface RtmLocationAttendance {
  locationCode: string; city: string; displayName: string; country: string
  openDays: number; daysWithPresence: number; days: RtmAttendanceDay[]
}
interface RtmAttendanceResponse { startDate: string; endDate: string; locations: RtmLocationAttendance[] }

interface RtmSnapshotInfo { reportType: string; periodFrom: string | null; periodTo: string | null; createdAt: string | null; createdBy: string | null }
interface RtmCorrectionRow { date: string; locationCode: string; location: string; before: string; now: string }
interface RtmWeeklyResponse { plan: RtmPlanResponse; lastWeeklySent: RtmSnapshotInfo | null }
interface RtmCorrectionsResponse { noWeeklySent: boolean; lastSent: RtmSnapshotInfo | null; changes: RtmCorrectionRow[] }

type Tab = "weekly" | "corrections" | "lastWeek"

// ── Local-date helpers (server runs Europe/Berlin) ────────────────────────────

function localDate(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, "0")
  const day = String(d.getDate()).padStart(2, "0")
  return `${y}-${m}-${day}`
}
function addDays(dateStr: string, n: number): string {
  const d = new Date(dateStr + "T00:00:00")
  d.setDate(d.getDate() + n)
  return localDate(d)
}
function lastMonday(): string {
  const t = new Date()
  const dow = (t.getDay() + 6) % 7 // Mon=0 … Sun=6
  const d = new Date(t)
  d.setDate(t.getDate() - dow - 7)
  return localDate(d)
}
function todayStr(): string {
  return localDate(new Date())
}
function dayHeader(dateStr: string): string {
  const d = new Date(dateStr + "T00:00:00")
  return d.toLocaleDateString("en-GB", { weekday: "short", day: "2-digit", month: "2-digit" })
}

// ── Canonical cell text (mirrors the backend so the email/Excel text matches) ─

function planCellText(day: RtmPlanDay): string {
  if (day.status === "CLOSED") return "Closed"
  if (day.status === "UNCOVERED") return "Uncovered"
  return day.agents.map(a => (a.start && a.end ? `${a.name} ${a.start}–${a.end}` : a.name)).join("\n")
}
function attendanceCellText(day: RtmAttendanceDay): string {
  if (day.status === "CLOSED") return "Closed"
  if (day.status === "NOT_PRESENT") return "Not present"
  const lines: string[] = []
  for (const p of day.present) {
    const time = p.checkIn ? (p.checkOut ? `${p.checkIn}–${p.checkOut}` : p.checkIn) : null
    lines.push(time ? `${p.name} ${time}` : p.name)
  }
  for (const n of day.plannedAbsent) lines.push(`Planned: ${n}`)
  return lines.join("\n")
}

// ── Clipboard (HTML table suitable for Outlook, with a plain-text fallback) ───

async function copyForEmail(html: string, plain: string): Promise<boolean> {
  let ok = false
  try {
    if (navigator.clipboard?.write && typeof ClipboardItem !== "undefined") {
      await navigator.clipboard.write([
        new ClipboardItem({
          "text/html": new Blob([html], { type: "text/html" }),
          "text/plain": new Blob([plain], { type: "text/plain" }),
        }),
      ])
      ok = true
    }
  } catch { ok = false }
  if (!ok) {
    try {
      const ta = document.createElement("textarea")
      ta.value = plain
      ta.style.position = "fixed"
      ta.style.left = "-9999px"
      document.body.appendChild(ta)
      ta.select()
      ok = document.execCommand("copy")
      document.body.removeChild(ta)
    } catch { ok = false }
  }
  return ok
}

const cellStyle = "border:1px solid #cbd5e1;padding:4px 8px;vertical-align:top;font-family:Arial,sans-serif;font-size:12px;"
const headStyle = cellStyle + "background:#f1f5f9;font-weight:bold;"

function buildTable(headers: string[], rows: string[][]): { html: string; plain: string } {
  const html = [
    '<table style="border-collapse:collapse;width:100%;">',
    "<thead><tr>" + headers.map(h => `<th style="${headStyle}">${escapeHtml(h)}</th>`).join("") + "</tr></thead>",
    "<tbody>",
    ...rows.map(r => "<tr>" + r.map(c => `<td style="${cellStyle}">${escapeHtml(c)}</td>`).join("") + "</tr>"),
    "</tbody></table>",
  ].join("")
  const plain = [headers.join("\t"), ...rows.map(r => r.join("\t"))].join("\n")
  return { html, plain }
}

function escapeHtml(s: string): string {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/\n/g, "<br/>")
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function RtmReports() {
  const { t } = useTranslation()
  const qc = useQueryClient()

  const [tab, setTab] = useState<Tab>("weekly")
  const [startDate, setStartDate] = useState(() => addDays(todayStr(), 1))
  const [monday, setMonday] = useState(() => lastMonday())
  const [copyState, setCopyState] = useState<string | null>(null)
  const [marking, setMarking] = useState(false)

  const weekly = useQuery<RtmWeeklyResponse>({
    queryKey: ["rtm-weekly", startDate],
    queryFn: () => apiFetch<RtmWeeklyResponse>(`/api/rtm-reports/weekly?start=${startDate}`),
    staleTime: 60_000,
  })

  const corrections = useQuery<RtmCorrectionsResponse>({
    queryKey: ["rtm-corrections"],
    queryFn: () => apiFetch<RtmCorrectionsResponse>("/api/rtm-reports/corrections"),
    staleTime: 60_000,
    enabled: tab === "corrections",
  })

  const lastWeek = useQuery<RtmAttendanceResponse>({
    queryKey: ["rtm-last-week", monday],
    queryFn: () => apiFetch<RtmAttendanceResponse>(`/api/rtm-reports/last-week?monday=${monday}`),
    staleTime: 60_000,
    enabled: tab === "lastWeek",
  })

  const flash = (msg: string) => {
    setCopyState(msg)
    window.setTimeout(() => setCopyState(null), 2500)
  }

  const doCopy = async (html: string, plain: string) => {
    const ok = await copyForEmail(html, plain)
    flash(ok ? t("rtmReports.copied") : t("rtmReports.copyFailed"))
  }

  const doMarkSent = async (reportType: "WEEKLY" | "DAILY", periodFrom: string, periodTo: string) => {
    setMarking(true)
    try {
      await apiFetch("/api/rtm-reports/mark-sent", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reportType, periodFrom, periodTo }),
      })
      qc.invalidateQueries({ queryKey: ["rtm-weekly"] })
      qc.invalidateQueries({ queryKey: ["rtm-corrections"] })
      flash(t("rtmReports.markedSent"))
    } catch {
      flash(t("rtmReports.error"))
    } finally {
      setMarking(false)
    }
  }

  // ── Weekly forecast ─────────────────────────────────────────────────────────
  const weeklyPlan = weekly.data?.plan
  const weeklyTable = useMemo(() => {
    if (!weeklyPlan) return null
    const headers = ["Location", ...(weeklyPlan.locations[0]?.days.map(d => dayHeader(d.date)) ?? [])]
    const rows = weeklyPlan.locations.map(loc => [
      `${loc.city} (${loc.locationCode})`,
      ...loc.days.map(d => planCellText(d)),
    ])
    return buildTable(headers, rows)
  }, [weeklyPlan])

  // ── Last week attendance ────────────────────────────────────────────────────
  const lastWeekTable = useMemo(() => {
    if (!lastWeek.data) return null
    const headers = ["Location", ...(lastWeek.data.locations[0]?.days.map(d => dayHeader(d.date)) ?? []), "Open days", "Days with presence"]
    const rows = lastWeek.data.locations.map(loc => [
      `${loc.city} (${loc.locationCode})`,
      ...loc.days.map(d => attendanceCellText(d)),
      String(loc.openDays),
      String(loc.daysWithPresence),
    ])
    return buildTable(headers, rows)
  }, [lastWeek.data])

  // ── Daily corrections ───────────────────────────────────────────────────────
  const correctionsTable = useMemo(() => {
    if (!corrections.data) return null
    const headers = ["Date", "Location", "Before", "Now"]
    const rows = corrections.data.changes.map(c => [c.date, `${c.location} (${c.locationCode})`, c.before, c.now])
    return buildTable(headers, rows)
  }, [corrections.data])

  const inputCls = "bg-sunken border border-line-subtle text-ink py-[6px] px-[10px] rounded-[6px] text-[13px] outline-none focus:border-accent"

  const btnCls =
    "flex items-center gap-2 px-4 py-2 rounded-lg font-semibold text-sm transition-colors duration-fast " +
    "bg-raised border border-line-subtle text-ink hover:bg-hovered"

  const tabBtn = (id: Tab, label: string, icon: ReactNode) => (
    <button
      key={id}
      onClick={() => setTab(id)}
      className={[
        "flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors duration-fast",
        tab === id ? "bg-accent text-white" : "bg-raised border border-line-subtle text-ink hover:bg-hovered",
      ].join(" ")}
    >
      {icon}
      {label}
    </button>
  )

  return (
    <div className="space-y-5 py-2">
      {/* Header */}
      <div>
        <h1 className="text-xl font-semibold text-ink">{t("rtmReports.title")}</h1>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap gap-2">
        {tabBtn("weekly", t("rtmReports.tab.weekly"), <CalendarRange size={15} />)}
        {tabBtn("corrections", t("rtmReports.tab.corrections"), <ClipboardCheck size={15} />)}
        {tabBtn("lastWeek", t("rtmReports.tab.lastWeek"), <CalendarDays size={15} />)}
      </div>

      {/* Feedback */}
      {copyState && (
        <div className="bg-good-bg border border-good-bd text-good-fg rounded-lg px-4 py-2 text-sm">{copyState}</div>
      )}

      {/* ── Weekly forecast ── */}
      {tab === "weekly" && (
        <section className="space-y-3">
          <div className="bg-raised border border-line-subtle rounded-lg p-4 flex flex-wrap items-end gap-4">
            <div>
              <label className="block text-xs text-ink-muted mb-1">{t("rtmReports.startDate")}</label>
              <input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} className={inputCls} />
            </div>
            <div className="flex gap-2">
              <button className={btnCls} onClick={() => weeklyTable && doCopy(weeklyTable.html, weeklyTable.plain)}>
                <Copy size={14} /> {t("rtmReports.copyEmail")}
              </button>
              <button className={btnCls} onClick={() => downloadExcel(`/api/rtm-reports/download/weekly?start=${startDate}`, `RTM_Weekly_${startDate}.xlsx`)}>
                <Download size={14} /> {t("rtmReports.downloadExcel")}
              </button>
              <button
                className="flex items-center gap-2 px-4 py-2 rounded-lg font-semibold text-sm transition-colors duration-fast bg-accent text-white disabled:opacity-50"
                disabled={marking}
                onClick={() => doMarkSent("WEEKLY", startDate, addDays(startDate, 6))}
              >
                <CheckCheck size={14} /> {t("rtmReports.markSent")}
              </button>
            </div>
            {weekly.data?.lastWeeklySent && (
              <div className="text-xs text-ink-soft ml-auto">
                {t("rtmReports.lastSent", {
                  when: weekly.data.lastWeeklySent?.createdAt ?? "",
                  who: weekly.data.lastWeeklySent?.createdBy ?? "—",
                })}
              </div>
            )}
          </div>

          {weekly.isLoading ? (
            <div className="text-ink-soft text-sm">{t("rtmReports.loading")}</div>
          ) : weekly.isError || !weeklyPlan ? (
            <div className="text-crit-fg text-sm">{t("rtmReports.error")}</div>
          ) : (
            <div className="bg-raised border border-line-subtle rounded-lg overflow-auto">
              <table className="w-full text-sm border-collapse min-w-[900px]">
                <thead>
                  <tr className="bg-sunken">
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle sticky left-0 bg-sunken">Location</th>
                    {weeklyPlan.locations[0]?.days.map(d => (
                      <th key={d.date} className="px-2 py-2 text-xs font-medium text-ink-soft border-b border-line-subtle whitespace-nowrap">{dayHeader(d.date)}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {weeklyPlan.locations.map(loc => (
                    <tr key={loc.locationCode} className="border-b border-line-subtle">
                      <td className="px-3 py-2 text-ink font-medium whitespace-nowrap sticky left-0 bg-raised">
                        {loc.city} <span className="text-ink-muted font-mono text-xs">({loc.locationCode})</span>
                      </td>
                      {loc.days.map(d => (
                        <td key={d.date} className="px-2 py-2 align-top">
                          {d.status === "CLOSED" ? (
                            <span className="text-ink-muted">{t("rtmReports.closed")}</span>
                          ) : d.status === "UNCOVERED" ? (
                            <span className="text-crit-fg font-semibold">{t("rtmReports.uncovered")}</span>
                          ) : (
                            <div className="space-y-0.5">
                              {d.agents.map(a => (
                                <div key={a.employeeId} className="whitespace-nowrap">
                                  <span className="text-ink">{a.name}</span>{" "}
                                  {a.start && a.end && <span className="text-ink-soft font-mono text-xs">{a.start}–{a.end}</span>}
                                </div>
                              ))}
                            </div>
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* ── Daily corrections ── */}
      {tab === "corrections" && (
        <section className="space-y-3">
          <div className="bg-raised border border-line-subtle rounded-lg p-4 flex flex-wrap items-center gap-3">
            <button className={btnCls} onClick={() => correctionsTable && doCopy(correctionsTable.html, correctionsTable.plain)} disabled={!correctionsTable}>
              <Copy size={14} /> {t("rtmReports.copyEmail")}
            </button>
            <button className={btnCls} onClick={() => downloadExcel("/api/rtm-reports/download/corrections", `RTM_Corrections_${todayStr()}.xlsx`)}>
              <Download size={14} /> {t("rtmReports.downloadExcel")}
            </button>
            {corrections.data && !corrections.data.noWeeklySent && corrections.data.lastSent && (
              <button
                className="flex items-center gap-2 px-4 py-2 rounded-lg font-semibold text-sm transition-colors duration-fast bg-accent text-white disabled:opacity-50"
                disabled={marking}
                onClick={() => doMarkSent("DAILY", todayStr(), corrections.data!.lastSent!.periodTo!)}
              >
                <CheckCheck size={14} /> {t("rtmReports.markSent")}
              </button>
            )}
            {corrections.data && !corrections.data.noWeeklySent && corrections.data.lastSent && (
              <div className="text-xs text-ink-soft ml-auto">
                {t("rtmReports.lastSent", {
                  when: corrections.data.lastSent.createdAt,
                  who: corrections.data.lastSent.createdBy ?? "—",
                })}
              </div>
            )}
          </div>

          {corrections.isLoading ? (
            <div className="text-ink-soft text-sm">{t("rtmReports.loading")}</div>
          ) : corrections.isError ? (
            <div className="text-crit-fg text-sm">{t("rtmReports.error")}</div>
          ) : corrections.data?.noWeeklySent ? (
            <div className="text-warn-fg text-sm font-medium">{t("rtmReports.sendWeeklyFirst")}</div>
          ) : corrections.data && corrections.data.changes.length === 0 ? (
            <div className="text-good-fg text-sm font-medium">
              {t("rtmReports.noChanges", { when: corrections.data.lastSent?.createdAt ?? "" })}
            </div>
          ) : (
            <div className="bg-raised border border-line-subtle rounded-lg overflow-auto">
              <table className="w-full text-sm border-collapse">
                <thead>
                  <tr className="bg-sunken">
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle">{t("rtmReports.date")}</th>
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle">{t("rtmReports.location")}</th>
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle">{t("rtmReports.before")}</th>
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle">{t("rtmReports.now")}</th>
                  </tr>
                </thead>
                <tbody>
                  {corrections.data!.changes.map((c, i) => (
                    <tr key={i} className="border-b border-line-subtle">
                      <td className="px-3 py-2 font-mono whitespace-nowrap">{c.date}</td>
                      <td className="px-3 py-2">{c.location} <span className="text-ink-muted font-mono text-xs">({c.locationCode})</span></td>
                      <td className="px-3 py-2 text-ink-muted whitespace-pre-line">{c.before}</td>
                      <td className="px-3 py-2 text-ink font-medium whitespace-pre-line">{c.now}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {/* ── Last week attendance ── */}
      {tab === "lastWeek" && (
        <section className="space-y-3">
          <div className="bg-raised border border-line-subtle rounded-lg p-4 flex flex-wrap items-end gap-4">
            <div>
              <label className="block text-xs text-ink-muted mb-1">{t("rtmReports.week")}</label>
              <input type="date" value={monday} onChange={e => setMonday(e.target.value)} className={inputCls} />
            </div>
            <div className="flex gap-2">
              <button className={btnCls} onClick={() => lastWeekTable && doCopy(lastWeekTable.html, lastWeekTable.plain)}>
                <Copy size={14} /> {t("rtmReports.copyEmail")}
              </button>
              <button className={btnCls} onClick={() => downloadExcel(`/api/rtm-reports/download/last-week?monday=${monday}`, `RTM_LastWeek_${monday}.xlsx`)}>
                <Download size={14} /> {t("rtmReports.downloadExcel")}
              </button>
            </div>
          </div>

          {lastWeek.isLoading ? (
            <div className="text-ink-soft text-sm">{t("rtmReports.loading")}</div>
          ) : lastWeek.isError || !lastWeek.data ? (
            <div className="text-crit-fg text-sm">{t("rtmReports.error")}</div>
          ) : (
            <div className="bg-raised border border-line-subtle rounded-lg overflow-auto">
              <table className="w-full text-sm border-collapse min-w-[900px]">
                <thead>
                  <tr className="bg-sunken">
                    <th className="text-left px-3 py-2 text-xs uppercase tracking-wide text-ink-soft border-b border-line-subtle sticky left-0 bg-sunken">Location</th>
                    {lastWeek.data.locations[0]?.days.map(d => (
                      <th key={d.date} className="px-2 py-2 text-xs font-medium text-ink-soft border-b border-line-subtle whitespace-nowrap">{dayHeader(d.date)}</th>
                    ))}
                    <th className="px-2 py-2 text-xs font-medium text-ink-soft border-b border-line-subtle">{t("rtmReports.openDays")}</th>
                    <th className="px-2 py-2 text-xs font-medium text-ink-soft border-b border-line-subtle">{t("rtmReports.daysWithPresence")}</th>
                  </tr>
                </thead>
                <tbody>
                  {lastWeek.data.locations.map(loc => (
                    <tr key={loc.locationCode} className="border-b border-line-subtle">
                      <td className="px-3 py-2 text-ink font-medium whitespace-nowrap sticky left-0 bg-raised">
                        {loc.city} <span className="text-ink-muted font-mono text-xs">({loc.locationCode})</span>
                      </td>
                      {loc.days.map(d => (
                        <td key={d.date} className="px-2 py-2 align-top">
                          {d.status === "CLOSED" ? (
                            <span className="text-ink-muted">{t("rtmReports.closed")}</span>
                          ) : d.status === "NOT_PRESENT" ? (
                            <span className="text-crit-fg font-semibold">{t("rtmReports.notPresent")}</span>
                          ) : (
                            <div className="space-y-1">
                              {d.present.map(p => (
                                <div key={p.employeeId} className="whitespace-nowrap">
                                  <span className="text-ink font-medium">{p.name}</span>{" "}
                                  {p.checkIn && (
                                    <span className="text-ink-soft font-mono text-xs">{p.checkIn}{p.checkOut ? `–${p.checkOut}` : ""}</span>
                                  )}
                                </div>
                              ))}
                              {d.plannedAbsent.map(n => (
                                <div key={n} className="text-ink-muted text-xs whitespace-nowrap">{t("rtmReports.planned", { name: n })}</div>
                              ))}
                            </div>
                          )}
                        </td>
                      ))}
                      <td className="px-2 py-2 text-center font-mono text-ink">{loc.openDays}</td>
                      <td className="px-2 py-2 text-center font-mono text-ink">{loc.daysWithPresence}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}
    </div>
  )
}
