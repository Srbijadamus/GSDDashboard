import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Search, AlertTriangle, UserCheck, Users, Clock, Calendar, Settings, Edit2, RefreshCw, CheckCircle2 } from "lucide-react"
import { apiFetch } from "../api/client"
import { CoverageBadge } from "../components/CoverageBadge"
import { NppBadge } from "../components/NppBadge"
import { Sheet } from "../components/Sheet"
import { StatusBadge } from "../components/StatusBadge"
import { EmptyState } from "../components/EmptyState"
import { ALPlanningModal } from "./ALPlanningModal"
import { AssignAgentModal } from "./AssignAgentModal"
import { ManualCheckinModal } from "./ManualCheckinModal"
import { Panel } from "../components/Panel"
import { DataTable, type DataColumn } from "../components/DataTable"

// ── API types ──────────────────────────────────────────────────────────────────

interface ForecastDay {
  date: string
  isOpen: boolean
  status: "COVERED" | "PARTIAL" | "UNCOVERED" | "CLOSED"
  effectiveCoverage: number
  minRequired: number
  coverageBuffer: number
  isAtRisk: boolean
}

interface ForecastLocation {
  locationCode: string
  displayName: string
  city: string
  country: string
  isNpp: boolean
  atRiskDays: number
  forecast: ForecastDay[]
}

interface ForecastResponse {
  generatedAt: string
  horizon: number
  locationCount: number
  totalAtRiskDays: number
  locations: ForecastLocation[]
}

interface AgentCard {
  employeeId: string
  name: string
  teamLead: string | null
  shiftStart: string | null
  shiftEnd: string | null
  isMain: boolean
  coverageMatch: "FULL" | "PARTIAL" | "NONE"
  coveredMinutes: number
  totalOpenMinutes: number
  mismatchNote: string | null
}

interface LocationCard {
  locationCode: string
  displayName: string
  city: string
  country: string
  address: string | null
  todaySchedule: {
    isClosed: boolean
    openTime: string | null
    closeTime: string | null
    totalOpenMinutes: number
  }
  assignedAgents: AgentCard[]
  coverageStatus: string
  coveragePercent: number
}

interface SubstituteCandidate {
  employeeId: string
  fullName: string
  sourceType: "BACKUP" | "SSP" | "WIC_DONOR" | "CALL_IN" | "REGIONAL"
  tier: string
  homeLocationName: string
  distanceKm: number
  loadScore: number
  score: number
}

interface SubstitutesDay {
  date: string
  currentStatus: string
  present: number
  gap: number
  candidates: SubstituteCandidate[]
}

interface SubstitutesResponse {
  locationCode: string
  displayName: string
  days: SubstitutesDay[]
}

interface KioskRecord {
  employee_id: string
  full_name: string
  attendance_status: "ACTIVE" | "NOT_CHECKED_IN" | "DONE"
  checkin_time: string | null
  checkout_time: string | null
  minutes_on_shift: number
  location: string | null
}

// ── Constants ──────────────────────────────────────────────────────────────────

const STATUS_RANK: Record<string, number> = {
  UNCOVERED: 0,
  PARTIAL:   1,
  COVERED:   2,
  CLOSED:    3,
}

const SOURCE_COLORS: Record<string, { bg: string; color: string }> = {
  BACKUP:    { bg: "rgb(var(--st-learn-bg) / 0.15)",  color: "rgb(var(--st-learn-fg))" },
  SSP:       { bg: "rgb(var(--st-info-bg) / 0.15)",   color: "rgb(var(--st-info-fg))"  },
  WIC_DONOR: { bg: "rgb(var(--st-good-bg) / 0.15)",   color: "rgb(var(--st-good-fg))" },
  CALL_IN:   { bg: "rgb(var(--st-warn-bg) / 0.15)",   color: "rgb(var(--st-warn-fg))" },
  REGIONAL:  { bg: "rgb(var(--st-wic-solid) / 0.15)", color: "rgb(var(--st-wic-fg))" },
}

const AGENT_MATCH_COLORS: Record<string, { bg: string; color: string }> = {
  FULL:    { bg: "rgb(var(--st-good-bg) / 0.12)",  color: "rgb(var(--st-good-fg))" },
  PARTIAL: { bg: "rgb(var(--st-warn-bg) / 0.12)",  color: "rgb(var(--st-warn-fg))" },
  NONE:    { bg: "rgb(var(--st-crit-bg) / 0.12)",  color: "rgb(var(--st-crit-fg))" },
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function chunkWeeks(days: ForecastDay[]): ForecastDay[][] {
  const weeks: ForecastDay[][] = []
  for (let i = 0; i < days.length; i += 7) weeks.push(days.slice(i, i + 7))
  return weeks
}

function checkinDuration(checkinTime: string | null): string {
  if (!checkinTime) return ""
  const normalized = checkinTime.replace(/\.(\d{3})\d*/, '.$1')
  const ms = Date.now() - new Date(normalized).getTime()
  if (ms < 0) return "0m"
  const totalMin = Math.floor(ms / 60_000)
  const h = Math.floor(totalMin / 60)
  const m = totalMin % 60
  return h > 0 ? `${h}h ${m}m` : `${m}m`
}

function kioskActiveAt(records: KioskRecord[], displayName: string): KioskRecord[] {
  if (!displayName) return []
  const q = displayName.toLowerCase()
  return records.filter(r =>
    r.attendance_status === "ACTIVE" &&
    (r.location ?? "").toLowerCase().includes(q)
  )
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function Skeleton({ width, height = 14 }: { width?: string | number; height?: number }) {
  return (
    <div
      className="skeleton"
      style={{ width: width ?? "100%", height, borderRadius: 4 }}
    />
  )
}

function StatCard({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="bg-raised border border-line-subtle" style={{
      borderRadius: 8, padding: "12px 16px",
    }}>
      <div className="text-ink-soft" style={{
        fontSize: 10, textTransform: "uppercase" as const, letterSpacing: ".07em",
        marginBottom: 8,
      }}>
        {label}
      </div>
      {children}
    </div>
  )
}

function SectionCard({
  title, icon, children, style, action,
}: {
  title: string; icon: React.ReactNode;
  children: React.ReactNode; style?: React.CSSProperties; action?: React.ReactNode
}) {
  return (
    <div className="bg-raised border border-line-subtle" style={{
      borderRadius: 8,
      ...style,
    }}>
      <div className="border-b border-line-subtle" style={{
        padding: "10px 16px",
        display: "flex", alignItems: "center", justifyContent: "space-between", gap: 6,
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span className="text-ink-soft">{icon}</span>
          <span className="text-ink" style={{ fontSize: 12, fontWeight: 600 }}>{title}</span>
        </div>
        {action}
      </div>
      <div style={{ padding: 16 }}>{children}</div>
    </div>
  )
}

const DOW_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

// Display order: Mon(1)…Fri(5) Sat(6) Sun(0)
const EDITOR_DOW_ORDER = [1, 2, 3, 4, 5, 6, 0]
const DOW_LABEL: Record<number, string> = { 0: "Sun", 1: "Mon", 2: "Tue", 3: "Wed", 4: "Thu", 5: "Fri", 6: "Sat" }

interface DayConfig {
  dayOfWeek: number
  isClosed: boolean
  openTime: string
  closeTime: string
  openTime2: string
  closeTime2: string
  hasSecondWindow: boolean
}

function blankDay(dow: number): DayConfig {
  return { dayOfWeek: dow, isClosed: true, openTime: "", closeTime: "", openTime2: "", closeTime2: "", hasSecondWindow: false }
}

function WicScheduleEditor({ locationCode }: { locationCode: string }) {
  const qc = useQueryClient()
  const todayStr = new Date().toISOString().split("T")[0]

  const { data: hoursData } = useQuery({
    queryKey: ["wic-opening-hours"],
    queryFn: () => apiFetch<any[]>("/api/wicschedule/opening-hours"),
    staleTime: 60000,
  })

  const locHours = (hoursData ?? []).find((l: any) => l.locationCode === locationCode)
  const weeklyHours: any[] = locHours?.weeklyHours ?? []

  const [open, setOpen] = useState(false)
  const [effectiveFrom, setEffectiveFrom] = useState(todayStr)
  const [changeNote, setChangeNote] = useState("")
  const [days, setDays] = useState<DayConfig[]>(() => EDITOR_DOW_ORDER.map(blankDay))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [consequences, setConsequences] = useState<any[] | null>(null)
  const [savedAt, setSavedAt] = useState<string | null>(null)

  // Pre-populate from current effective schedule whenever location or hours load
  useEffect(() => {
    if (weeklyHours.length === 0) return
    setDays(EDITOR_DOW_ORDER.map(dow => {
      const h = weeklyHours.find((w: any) => w.dayOfWeek === dow)
      if (!h) return blankDay(dow)
      return {
        dayOfWeek: dow,
        isClosed: h.isClosed,
        openTime: h.openTime ?? "",
        closeTime: h.closeTime ?? "",
        openTime2: h.openTime2 ?? "",
        closeTime2: h.closeTime2 ?? "",
        hasSecondWindow: !!(h.openTime2 && h.closeTime2),
      }
    }))
    setConsequences(null)
    setSavedAt(null)
    setError(null)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [weeklyHours.length, locationCode])

  const patchDay = (dow: number, patch: Partial<DayConfig>) =>
    setDays(prev => prev.map(d => d.dayOfWeek === dow ? { ...d, ...patch } : d))

  const closeCentre = () => setDays(EDITOR_DOW_ORDER.map(blankDay))

  const validate = (): string | null => {
    for (const d of days) {
      if (d.isClosed) continue
      if (!d.openTime || !d.closeTime)
        return `${DOW_LABEL[d.dayOfWeek]}: open and close times required.`
      if (d.openTime >= d.closeTime)
        return `${DOW_LABEL[d.dayOfWeek]}: close time must be after open time.`
      if (d.hasSecondWindow) {
        if (!d.openTime2 || !d.closeTime2)
          return `${DOW_LABEL[d.dayOfWeek]}: both times required for second window.`
        if (d.openTime2 <= d.closeTime)
          return `${DOW_LABEL[d.dayOfWeek]}: second window must start after first window ends.`
        if (d.closeTime2 <= d.openTime2)
          return `${DOW_LABEL[d.dayOfWeek]}: second window close must be after open.`
      }
    }
    return null
  }

  const save = async () => {
    const err = validate()
    if (err) { setError(err); return }
    setError(null)
    setSaving(true)
    setConsequences(null)
    try {
      const res = await apiFetch<any>(
        `/api/wicschedule/opening-hours/${encodeURIComponent(locationCode)}/version`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            effectiveFrom,
            changeNote: changeNote.trim() || null,
            closeEntireCentre: false,
            days: days.map(d => ({
              dayOfWeek: d.dayOfWeek,
              isClosed: d.isClosed,
              openTime: d.isClosed ? null : (d.openTime || null),
              closeTime: d.isClosed ? null : (d.closeTime || null),
              openTime2: (d.isClosed || !d.hasSecondWindow) ? null : (d.openTime2 || null),
              closeTime2: (d.isClosed || !d.hasSecondWindow) ? null : (d.closeTime2 || null),
            })),
          }),
        }
      )
      setConsequences(res.consequences ?? [])
      setSavedAt(effectiveFrom)
      setChangeNote("")
      qc.invalidateQueries({ queryKey: ["wic-opening-hours"] })
      qc.invalidateQueries({ queryKey: ["wic-forecast"] })
    } catch (e: any) {
      setError(e?.message ?? "Save failed")
    } finally {
      setSaving(false)
    }
  }

  const inputBase: React.CSSProperties = {
    borderRadius: 4, outline: "none",
    fontSize: 11, padding: "3px 5px",
  }
  const inputBaseClass = "bg-raised border border-line-subtle text-ink font-mono"

  return (
    <div style={{ marginTop: 14 }}>
      <SectionCard
        title="Opening Hours / Öffnungszeiten"
        icon={<Edit2 size={13} />}
        action={
          <button
            onClick={() => { setOpen(o => !o); setConsequences(null) }}
            className={open
              ? "bg-sunken border border-line-subtle text-ink-muted"
              : "bg-info-solid border border-info-bd text-white"}
            style={{ padding: "4px 10px", borderRadius: 5, fontSize: 10, fontWeight: 600, cursor: "pointer" }}
          >
            {open ? "▲ Collapse" : "▼ Edit / Bearbeiten"}
          </button>
        }
      >
        {!open ? (
          <div className="text-ink-soft" style={{ fontSize: 12 }}>
            {weeklyHours.length > 0
              ? weeklyHours.filter((h: any) => !h.isClosed)
                  .map((h: any) => `${DOW_LABEL[h.dayOfWeek] ?? "?"} ${h.openTime}–${h.closeTime}${h.openTime2 ? ` / ${h.openTime2}–${h.closeTime2}` : ""}`)
                  .join(" · ") || "All days closed"
              : "Loading…"}
          </div>
        ) : (
          <div>
            {/* ── Controls row ── */}
            <div style={{ display: "flex", gap: 10, marginBottom: 14, flexWrap: "wrap", alignItems: "flex-end" }}>
              <div>
                <div className="text-ink-soft" style={{ fontSize: 10, marginBottom: 3 }}>
                  Effective from / Gültig ab
                </div>
                <input
                  type="date"
                  value={effectiveFrom}
                  onChange={e => { setEffectiveFrom(e.target.value); setConsequences(null) }}
                  className={inputBaseClass}
                  style={{ ...inputBase, padding: "5px 7px" }}
                />
              </div>
              <div style={{ flex: 1, minWidth: 180 }}>
                <div className="text-ink-soft" style={{ fontSize: 10, marginBottom: 3 }}>
                  Note / Anmerkung (optional)
                </div>
                <input
                  value={changeNote}
                  onChange={e => setChangeNote(e.target.value)}
                  placeholder="Reason for change / Grund der Änderung"
                  className={inputBaseClass}
                  style={{ ...inputBase, width: "100%", boxSizing: "border-box", padding: "5px 7px" }}
                />
              </div>
              <button
                onClick={closeCentre}
                className="bg-crit-bg border border-crit-bd text-crit-fg"
                style={{ padding: "5px 12px", borderRadius: 5, fontSize: 11, fontWeight: 600, cursor: "pointer", whiteSpace: "nowrap" }}
              >
                Close entire centre / Standort schließen
              </button>
            </div>

            {/* ── 7-day grid ── */}
            <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6, marginBottom: 12 }}>
              {days.map(day => (
                <div
                  key={day.dayOfWeek}
                  className={day.isClosed ? "bg-sunken border border-line-subtle" : "bg-sunken border border-info-bd/25"}
                  style={{ borderRadius: 6, padding: "8px 5px", display: "flex", flexDirection: "column", gap: 5, alignItems: "center" }}
                >
                  <div className="text-ink-soft" style={{ fontSize: 10, fontWeight: 700, textTransform: "uppercase" }}>
                    {DOW_LABEL[day.dayOfWeek]}
                  </div>

                  <button
                    onClick={() => patchDay(day.dayOfWeek, { isClosed: !day.isClosed })}
                    className={day.isClosed ? "bg-crit-bg border border-crit-bd text-crit-fg" : "bg-good-bg border border-good-bd text-good-fg"}
                    style={{ width: "100%", padding: "3px 0", borderRadius: 4, fontSize: 9, fontWeight: 700, cursor: "pointer" }}
                  >
                    {day.isClosed ? "Closed" : "Open"}
                  </button>

                  {!day.isClosed && (
                    <>
                      <input
                        type="time"
                        value={day.openTime}
                        onChange={e => patchDay(day.dayOfWeek, { openTime: e.target.value })}
                        className={inputBaseClass}
                        style={{ ...inputBase, width: "100%", boxSizing: "border-box", textAlign: "center" }}
                      />
                      <input
                        type="time"
                        value={day.closeTime}
                        onChange={e => patchDay(day.dayOfWeek, { closeTime: e.target.value })}
                        className={inputBaseClass}
                        style={{ ...inputBase, width: "100%", boxSizing: "border-box", textAlign: "center" }}
                      />

                      {day.hasSecondWindow ? (
                        <>
                          <div className="border-t border-line-subtle border-dashed" style={{ width: "100%", paddingTop: 4 }}>
                            <div className="text-ink-soft" style={{ fontSize: 8, textAlign: "center", marginBottom: 3 }}>
                              2nd window
                            </div>
                            <input
                              type="time"
                              value={day.openTime2}
                              onChange={e => patchDay(day.dayOfWeek, { openTime2: e.target.value })}
                              className={inputBaseClass}
                              style={{ ...inputBase, width: "100%", boxSizing: "border-box", textAlign: "center", marginBottom: 4 }}
                            />
                            <input
                              type="time"
                              value={day.closeTime2}
                              onChange={e => patchDay(day.dayOfWeek, { closeTime2: e.target.value })}
                              className={inputBaseClass}
                        style={{ ...inputBase, width: "100%", boxSizing: "border-box", textAlign: "center" }}
                            />
                          </div>
                          <button
                            onClick={() => patchDay(day.dayOfWeek, { hasSecondWindow: false, openTime2: "", closeTime2: "" })}
                            className="text-ink-soft"
                            style={{ fontSize: 8, background: "none", border: "none", cursor: "pointer" }}
                          >
                            − remove 2nd
                          </button>
                        </>
                      ) : (
                        <button
                          onClick={() => patchDay(day.dayOfWeek, { hasSecondWindow: true })}
                          className="text-ink-soft"
                          style={{ fontSize: 8, background: "none", border: "none", cursor: "pointer" }}
                        >
                          + split shift
                        </button>
                      )}
                    </>
                  )}
                </div>
              ))}
            </div>

            {error && (
              <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 6, padding: "8px 12px", fontSize: 11, marginBottom: 10 }}>
                {error}
              </div>
            )}

            <button
              onClick={save}
              disabled={saving}
              className="bg-info-solid text-white"
              style={{
                border: "none",
                padding: "8px 22px", borderRadius: 6, fontSize: 12,
                fontWeight: 700, cursor: saving ? "not-allowed" : "pointer",
                opacity: saving ? 0.6 : 1,
              }}
            >
              {saving ? "Saving… / Speichere…" : "Save / Speichern"}
            </button>

            {/* ── Consequence list ── */}
            {consequences !== null && (
              <div style={{ marginTop: 14 }}>
                {consequences.length === 0 ? (
                  <div className="bg-good-bg border border-good-bd text-good-fg" style={{ borderRadius: 6, padding: "8px 12px", fontSize: 11 }}>
                    ✓ Saved (effective {savedAt}). No conflicting assignments found on/after that date.
                    / Gespeichert (gültig ab {savedAt}). Keine betroffenen Einsätze gefunden.
                  </div>
                ) : (
                  <div>
                    <div className="bg-warn-bg border border-warn-bd text-warn-fg" style={{ borderRadius: 6, padding: "10px 12px", fontSize: 11, fontWeight: 600, marginBottom: 8 }}>
                      <AlertTriangle size={11} className="inline text-warn-fg align-text-bottom" /> Saved (effective {savedAt}). {consequences.length} existing assignment(s) now conflict with the new schedule:
                      / Gespeichert (gültig ab {savedAt}). {consequences.length} bestehende Einsatz/Einsätze kollidieren mit dem neuen Plan:
                    </div>
                    <div style={{ overflowX: "auto" }}>
                      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11 }}>
                        <thead>
                          <tr className="bg-sunken">
                            {["Date / Datum", "Day / Tag", "Agent", "Shift / Schicht", "Issue / Problem"].map(h => (
                              <th key={h} className="text-ink-soft border-b border-line-subtle" style={{ padding: "5px 8px", textAlign: "left", fontSize: 9,
                                fontWeight: 700, textTransform: "uppercase",
                                whiteSpace: "nowrap" }}>
                                {h}
                              </th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {consequences.map((c, i) => (
                            <tr key={i} className="border-b border-line-subtle">
                              <td className="font-mono" style={{ padding: "5px 8px", fontSize: 11 }}>{c.date}</td>
                              <td className="font-mono" style={{ padding: "5px 8px", fontSize: 11 }}>{c.weekday}</td>
                              <td style={{ padding: "5px 8px", fontSize: 11 }}>{c.fullName}</td>
                              <td className="font-mono" style={{ padding: "5px 8px", fontSize: 11 }}>{c.workingShift ?? "—"}</td>
                              <td className={c.issue === "CLOSED_DAY" ? "text-crit-fg" : "text-warn-fg"} style={{ padding: "5px 8px", fontSize: 11, fontWeight: 600 }}>
                                {c.issue === "CLOSED_DAY"
                                  ? "Day now closed / Tag jetzt geschlossen"
                                  : "Outside new window / Außerhalb Öffnungszeit"}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    <div className="text-ink-soft" style={{ marginTop: 8, fontSize: 10 }}>
                      These assignments were NOT changed automatically. Please review and update them manually in the Shifts planner.
                      / Diese Einsätze wurden NICHT automatisch geändert. Bitte manuell im Schichtplan prüfen und anpassen.
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </SectionCard>
    </div>
  )
}

function MinRequiredEditor({ locationCode }: { locationCode: string }) {
  const qc = useQueryClient()
  const { data: hoursData } = useQuery({
    queryKey: ["wic-opening-hours"],
    queryFn: () => apiFetch<any[]>("/api/wicschedule/opening-hours"),
    staleTime: 60000,
  })

  const locHours = (hoursData ?? []).find((l: any) => l.locationCode === locationCode)
  const weeklyHours: any[] = locHours?.weeklyHours ?? []

  const [editing, setEditing] = useState<Record<number, string>>({})
  const [saving, setSaving] = useState<number | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)

  const saveMin = async (dow: number, value: string) => {
    const parsed = value === "" ? null : parseInt(value, 10)
    if (value !== "" && isNaN(parsed!)) return
    setSaving(dow)
    setSaveError(null)
    try {
      await apiFetch(
        `/api/wicschedule/opening-hours/${encodeURIComponent(locationCode)}/${dow}/min-required`,
        {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ value: parsed }),
        }
      )
      qc.invalidateQueries({ queryKey: ["wic-opening-hours"] })
      qc.invalidateQueries({ queryKey: ["wic-forecast"] })
      setEditing(prev => { const n = { ...prev }; delete n[dow]; return n })
    } catch (e: any) {
      setSaveError(e?.message ?? "Save failed")
    } finally { setSaving(null) }
  }

  if (weeklyHours.length === 0) return null

  return (
    <div style={{ marginTop: 14 }}>
      <SectionCard title="Required headcount per weekday" icon={<Settings size={13} />}>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 6 }}>
          {weeklyHours.map((h: any) => {
            const val = editing[h.dayOfWeek] !== undefined ? editing[h.dayOfWeek] : (h.minRequired ?? "")
            return (
              <div key={h.dayOfWeek} className={`border border-line-subtle ${h.isClosed ? "bg-sunken" : "bg-sunken"}`} style={{ borderRadius: 6, padding: "8px 6px", textAlign: "center", opacity: h.isClosed ? 0.5 : 1 }}>
                <div className="text-ink-soft" style={{ fontSize: 9, textTransform: "uppercase", marginBottom: 4 }}>
                  {DOW_NAMES[h.dayOfWeek]}
                </div>
                {h.isClosed ? (
                  <div className="text-ink-soft font-mono" style={{ fontSize: 10 }}>—</div>
                ) : (
                  <>
                    <input
                      type="number" min={0} max={20}
                      value={val}
                      onChange={e => setEditing(prev => ({ ...prev, [h.dayOfWeek]: e.target.value }))}
                      onKeyDown={e => { if (e.key === "Enter") saveMin(h.dayOfWeek, String(val)) }}
                      disabled={saving === h.dayOfWeek}
                      className="bg-raised border border-line-subtle text-ink font-mono"
                      style={{
                        width: "100%", boxSizing: "border-box" as const,
                        padding: "3px 4px", borderRadius: 4,
                        fontSize: 12, textAlign: "center",
                        outline: "none",
                      }}
                    />
                    {editing[h.dayOfWeek] !== undefined && (
                      <button
                        onClick={() => saveMin(h.dayOfWeek, String(val))}
                        disabled={saving === h.dayOfWeek}
                        className="bg-info-solid text-white"
                        style={{
                          marginTop: 4, width: "100%",
                          border: "none", padding: "2px 0",
                          borderRadius: 3, fontSize: 9, cursor: "pointer",
                        }}
                      >
                        {saving === h.dayOfWeek ? "…" : "Save"}
                      </button>
                    )}
                    {editing[h.dayOfWeek] === undefined && h.minRequired == null && (
                      <div className="text-ink-soft" style={{ fontSize: 9, marginTop: 2 }}>default</div>
                    )}
                  </>
                )}
              </div>
            )
          })}
        </div>
        <div className="text-ink-soft" style={{ marginTop: 8, fontSize: 10 }}>
          Blank = inherit from location default. Press Enter or Save after editing.
        </div>
        {saveError && (
          <div style={{
            marginTop: 8, background: "rgb(var(--st-crit-bg))", border: "1px solid rgb(var(--st-crit-solid))",
            borderRadius: 6, padding: "6px 10px", fontSize: 11, color: "rgb(var(--st-crit-solid))",
            display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8,
          }}>
            <span>{saveError}</span>
            <button onClick={() => setSaveError(null)} style={{ background: "none", border: "none", cursor: "pointer", color: "inherit", fontSize: 13, lineHeight: 1 }}>✕</button>
          </div>
        )}
      </SectionCard>
    </div>
  )
}

// ── Main component ─────────────────────────────────────────────────────────────

const HORIZON_OPTIONS = [7, 28]

export default function WicAttendance() {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const today = new Date().toISOString().split("T")[0]

  const [horizonDays, setHorizonDays] = useState(28)
  const [selectedDate, setSelectedDate] = useState(today)
  const [selectedLocationCode, setSelectedLocationCode] = useState<string | null>(null)
  const [isSheetOpen, setIsSheetOpen] = useState(false)
  const [sheetDate, setSheetDate] = useState(today)
  const [acceptedSubId, setAcceptedSubId] = useState<string | null>(null)
  const [acceptedSubName, setAcceptedSubName] = useState<string | null>(null)
  const [acceptingId, setAcceptingId] = useState<string | null>(null)
  const [acceptError, setAcceptError] = useState<string | null>(null)
  const [alPlanningOpen, setAlPlanningOpen] = useState(false)
  const [assignAgentOpen, setAssignAgentOpen] = useState(false)
  const [manualCheckinOpen, setManualCheckinOpen] = useState(false)
  const [kioskDrawerFilter, setKioskDrawerFilter] = useState<"checkedIn" | "expected" | "notYetIn" | null>(null)
  const [search, setSearch] = useState("")
  const [countryFilter, setCountryFilter] = useState("")

  const { data: forecast, isLoading: forecastLoading } = useQuery({
    queryKey: ["wic-forecast", horizonDays],
    queryFn: (): Promise<ForecastResponse> =>
      fetch(`/api/wic/forecast?horizon=${horizonDays}`).then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json()
      }),
    staleTime: 5 * 60 * 1000,
    refetchInterval: 10 * 60 * 1000,
  })

  const { data: cards, isLoading: cardsLoading } = useQuery({
    queryKey: ["wic-cards", selectedDate],
    queryFn: (): Promise<LocationCard[]> =>
      fetch(`/api/wic/cards?date=${selectedDate}`).then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json()
      }),
    staleTime: 2 * 60 * 1000,
  })

  const { data: subs, isLoading: subsLoading } = useQuery({
    queryKey: ["wic-subs", selectedLocationCode, sheetDate],
    queryFn: (): Promise<SubstitutesResponse> =>
      fetch(
        `/api/wic/substitutes?locationCode=${encodeURIComponent(selectedLocationCode!)}&date=${sheetDate}&horizon=1`
      ).then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json()
      }),
    enabled: isSheetOpen && !!selectedLocationCode,
    staleTime: 60 * 1000,
  })

  const { data: kioskData = [], isError: kioskError } = useQuery<KioskRecord[]>({
    queryKey: ["kiosk-attendance"],
    queryFn: (): Promise<KioskRecord[]> => {
      const url = import.meta.env.VITE_KIOSK_API_URL
      if (!url) throw new Error("VITE_KIOSK_API_URL not configured")
      return fetch(`${url}/api/attendance`).then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json()
      })
    },
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
    refetchOnMount: 'always',
    refetchOnWindowFocus: true,
  })

  const locations = forecast?.locations ?? []
  const countries = [...new Set(locations.map(l => l.country).filter(Boolean))].sort()

  const filteredLocations = [...locations]
    .filter(loc => {
      if (countryFilter && loc.country !== countryFilter) return false
      if (search) {
        const q = search.toLowerCase()
        if (!loc.displayName.toLowerCase().includes(q) && !loc.city.toLowerCase().includes(q)) return false
      }
      return true
    })
    .sort((a, b) => {
      const dA = (a.forecast ?? []).find(d => d.date === selectedDate)
      const dB = (b.forecast ?? []).find(d => d.date === selectedDate)
      const rA = STATUS_RANK[dA?.status ?? "CLOSED"] ?? 4
      const rB = STATUS_RANK[dB?.status ?? "CLOSED"] ?? 4
      if (rA !== rB) return rA - rB
      return a.displayName.localeCompare(b.displayName)
    })

  useEffect(() => {
    if (selectedLocationCode || filteredLocations.length === 0) return
    const atRisk = filteredLocations.find(l => l.atRiskDays > 0)
    setSelectedLocationCode((atRisk ?? filteredLocations[0]).locationCode)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filteredLocations.length])

  const selectedForecast = locations.find(l => l.locationCode === selectedLocationCode)
  const selectedCard     = cards?.find(c => c.locationCode === selectedLocationCode)
  const selectedDay      = (selectedForecast?.forecast ?? []).find(d => d.date === selectedDate)
  const subsDay          = subs?.days[0]

  const kioskMap  = new Map(kioskData.map(r => [r.employee_id, r]))
  const locationDisplayName = selectedForecast?.displayName ?? selectedCard?.displayName ?? ""
  const presentAgents = kioskActiveAt(kioskData, locationDisplayName)

  // Check-in status panel (global, all WIC locations, today)
  const allTodayAgents = cards?.flatMap(c => c.assignedAgents) ?? []
  const seenEmpIds = new Set<string>()
  const workingAgents = allTodayAgents.filter(a => {
    if (seenEmpIds.has(a.employeeId)) return false
    if (!a.shiftStart || a.shiftStart === "SICK" || a.shiftStart === "SL" || a.shiftStart === "AL") return false
    seenEmpIds.add(a.employeeId)
    return true
  })
  const checkedInIds = new Set(
    kioskData
      .filter(r => r.attendance_status === "ACTIVE" || r.attendance_status === "DONE")
      .map(r => r.employee_id)
  )
  const expectedCount  = workingAgents.length
  const checkedInCount = checkedInIds.size
  const notYetInList   = workingAgents.filter(a => !checkedInIds.has(a.employeeId))
  const notYetInCount  = notYetInList.length

  type KioskDrawerRow = { key: string; name: string; location: string | null; checkinTime: string | null; status: KioskRecord["attendance_status"]; shiftStart?: string | null }
  const kioskDrawerRows: KioskDrawerRow[] = (() => {
    if (kioskDrawerFilter === "checkedIn") {
      return [...kioskData]
        .filter(r => r.attendance_status === "ACTIVE" || r.attendance_status === "DONE")
        .sort((a, b) => (a.checkin_time ?? "").localeCompare(b.checkin_time ?? ""))
        .map(r => ({ key: r.employee_id, name: r.full_name, location: r.location, checkinTime: r.checkin_time, status: r.attendance_status }))
    }
    if (kioskDrawerFilter === "expected") {
      return workingAgents.map(a => {
        const k = kioskMap.get(a.employeeId)
        return { key: a.employeeId, name: a.name, location: k?.location ?? null, checkinTime: k?.checkin_time ?? null, status: (k?.attendance_status ?? "NOT_CHECKED_IN") as KioskRecord["attendance_status"], shiftStart: a.shiftStart }
      })
    }
    if (kioskDrawerFilter === "notYetIn") {
      return notYetInList.map(a => {
        const k = kioskMap.get(a.employeeId)
        return { key: a.employeeId, name: a.name, location: k?.location ?? null, checkinTime: null, status: "NOT_CHECKED_IN" as KioskRecord["attendance_status"], shiftStart: a.shiftStart }
      })
    }
    return []
  })()

  // ── Zone B — Not Checked In ───────────────────────────────────────────────────

  type ZoneBRow = {
    employeeId: string
    name: string
    locationCode: string
    locationCity: string
    shiftStart: string
    delayMinutes: number
    kioskStatus: "NOT_CHECKED_IN" | "NO_RECORD" | "UNKNOWN_STATUS"
    rawKioskStatus?: string
    shiftStarted: boolean
  }

  const zoneBRows = (() => {
    if (!cards) return [] as ZoneBRow[]
    const now = new Date()
    const nowMin = now.getHours() * 60 + now.getMinutes()
    const seen = new Set<string>()
    const rows: ZoneBRow[] = []
    for (const loc of cards) {
      for (const agent of loc.assignedAgents) {
        if (seen.has(agent.employeeId)) continue
        if (!agent.shiftStart || !/^\d{2}:\d{2}$/.test(agent.shiftStart)) continue
        const kiosk = kioskMap.get(agent.employeeId)
        if (kiosk?.attendance_status === "ACTIVE" || kiosk?.attendance_status === "DONE") {
          seen.add(agent.employeeId)
          continue
        }
        seen.add(agent.employeeId)
        const [hStr, mStr] = agent.shiftStart.split(":")
        const shiftMin = parseInt(hStr) * 60 + parseInt(mStr)
        const shiftStarted = nowMin >= shiftMin
        const delayMinutes = shiftStarted ? nowMin - shiftMin : 0
        let kioskStatus: ZoneBRow["kioskStatus"]
        let rawKioskStatus: string | undefined
        if (!kiosk) {
          kioskStatus = "NO_RECORD"
        } else if (kiosk.attendance_status === "NOT_CHECKED_IN") {
          kioskStatus = "NOT_CHECKED_IN"
        } else {
          kioskStatus = "UNKNOWN_STATUS"
          rawKioskStatus = kiosk.attendance_status
          console.warn("[ZoneB] Unexpected kiosk status:", kiosk.attendance_status, "for agent", agent.name, agent.employeeId)
        }
        rows.push({ employeeId: agent.employeeId, name: agent.name, locationCode: loc.locationCode, locationCity: loc.city, shiftStart: agent.shiftStart, delayMinutes, kioskStatus, rawKioskStatus, shiftStarted })
      }
    }
    return rows
  })()

  const zoneBMain = zoneBRows
    .filter(r => r.shiftStarted && r.kioskStatus !== "NO_RECORD")
    .sort((a, b) => b.delayMinutes - a.delayMinutes)
  const zoneBNoRecord = zoneBRows.filter(r => r.shiftStarted && r.kioskStatus === "NO_RECORD")
  const zoneBUpcoming = [...zoneBRows.filter(r => !r.shiftStarted)].sort((a, b) => a.shiftStart.localeCompare(b.shiftStart))
  const zoneBEmergency = zoneBRows.filter(r => r.rawKioskStatus === "EMERGENCY")

  const inputStyleClass = "bg-sunken border border-line-subtle text-ink"
  const inputStyle: React.CSSProperties = {
    width: "100%", padding: "6px 10px", borderRadius: 6,
    fontSize: 11, fontFamily: "IBM Plex Sans", outline: "none",
  }

  const openSub = (date: string) => {
    setSheetDate(date)
    setAcceptedSubId(null)
    setAcceptedSubName(null)
    setAcceptError(null)
    setIsSheetOpen(true)
  }

  const handleAcceptSub = async (c: SubstituteCandidate) => {
    if (!selectedLocationCode) return
    setAcceptingId(c.employeeId)
    try {
      const res = await fetch("/api/wic/substitutes/accept", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId: c.employeeId,
          locationCode: selectedLocationCode,
          date: sheetDate,
          shiftStart: null,
          shiftEnd: null,
          sourceType: c.sourceType,
        }),
      })
      if (!res.ok) {
        const body = await res.text().catch(() => "")
        throw new Error(`HTTP ${res.status}${body ? ": " + body : ""}`)
      }
      setAcceptedSubId(c.employeeId)
      setAcceptedSubName(c.fullName)
      setAcceptError(null)
      queryClient.invalidateQueries({ queryKey: ["wic-subs", selectedLocationCode, sheetDate] })
      queryClient.refetchQueries({ queryKey: ["wic-forecast"] })
      queryClient.refetchQueries({ queryKey: ["wic-cards", selectedDate], exact: true })
    } catch (err) {
      console.error("Accept error:", err)
      setAcceptError(String(err))
    } finally {
      setAcceptingId(null)
    }
  }

  const zoneBColumns: DataColumn<ZoneBRow>[] = [
    {
      key: "name",
      header: t("attendance.zoneB.name"),
      render: row => <span className="text-ink font-medium text-sm">{row.name}</span>,
    },
    {
      key: "location",
      header: t("attendance.zoneB.location"),
      render: row => <span className="text-ink-soft text-xs font-mono">{row.locationCode} · {row.locationCity}</span>,
    },
    {
      key: "shiftStart",
      header: t("attendance.zoneB.shiftStart"),
      render: row => <span className="font-mono text-sm text-ink">{row.shiftStart}</span>,
    },
    {
      key: "delay",
      header: t("attendance.zoneB.delay"),
      render: row => {
        if (!row.shiftStarted) return <span className="font-mono text-sm text-ink-soft">—</span>
        const cls = row.delayMinutes > 30
          ? "text-crit-fg font-semibold"
          : row.delayMinutes > 10
          ? "text-warn-fg"
          : "text-neutralst-fg"
        return <span className={`font-mono text-sm ${cls}`}>{row.delayMinutes}m</span>
      },
    },
    {
      key: "status",
      header: t("attendance.zoneB.status"),
      render: row => (
        <StatusBadge tone={row.kioskStatus === "NOT_CHECKED_IN" ? "warn" : "mutedst"}>
          {row.kioskStatus === "NOT_CHECKED_IN"
            ? t("attendance.zoneB.statusNotIn")
            : row.kioskStatus === "NO_RECORD"
            ? t("attendance.zoneB.statusNoRecord")
            : t("attendance.zoneB.statusUnknown")}
        </StatusBadge>
      ),
    },
    {
      key: "actions",
      header: t("attendance.zoneB.actions"),
      render: row => (
        <div className="flex items-center gap-2">
          <button
            onClick={() => setManualCheckinOpen(true)}
            className="bg-raised border border-line-subtle text-ink"
            style={{ padding: "3px 8px", borderRadius: 4, fontSize: 11, cursor: "pointer", whiteSpace: "nowrap" as const }}
          >
            {t("attendance.zoneB.manualCheckin")}
          </button>
          <button
            onClick={() => { setSelectedLocationCode(row.locationCode); openSub(selectedDate) }}
            className="bg-raised border border-line-subtle text-ink"
            style={{ padding: "3px 8px", borderRadius: 4, fontSize: 11, cursor: "pointer", whiteSpace: "nowrap" as const }}
          >
            {t("attendance.zoneB.findSub")}
          </button>
        </div>
      ),
    },
  ]

  return (
    <div style={{ display: "flex", height: "calc(100vh - 45px)", overflow: "hidden" }}>

      {/* ── LEFT SIDEBAR ──────────────────────────────────────────────────────── */}
      <aside className="border-r border-line-subtle" style={{
        width: 260, flexShrink: 0,
        background: "var(--sidebar)",
        display: "flex", flexDirection: "column",
        overflow: "hidden",
      }}>
        <div className="border-b border-line-subtle" style={{ padding: "14px 12px 10px", flexShrink: 0 }}>
          <div className="text-ink" style={{ fontSize: 13, fontWeight: 600, marginBottom: 10 }}>
            {t("attendance.title")}
          </div>
          <div style={{ position: "relative", marginBottom: 8 }}>
            <Search size={12} className="text-ink-soft" style={{
              position: "absolute", left: 8, top: "50%",
              transform: "translateY(-50%)",
            }} />
            <input
              placeholder={t("attendance.filter.search")}
              value={search}
              onChange={e => setSearch(e.target.value)}
              className={inputStyleClass}
              style={{ ...inputStyle, paddingLeft: 26 }}
            />
          </div>
          <select value={countryFilter} onChange={e => setCountryFilter(e.target.value)} className={inputStyleClass} style={inputStyle}>
            <option value="">{t("attendance.filter.allCountries")}</option>
            {countries.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        <div className="border-b border-line-subtle" style={{ padding: "8px 12px", flexShrink: 0 }}>
          <input
            type="date"
            value={selectedDate}
            onChange={e => setSelectedDate(e.target.value)}
            className={inputStyleClass}
            style={inputStyle}
          />
        </div>

        {/* ── CHECK-IN STATUS PANEL ────────────────────────────────────── */}
        {cards && (
          <div className="border-b border-line-subtle" style={{ flexShrink: 0 }}>
            <div style={{ padding: "7px 12px 5px", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <span className="text-ink-soft" style={{ fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: ".07em" }}>
                Check-in Status
              </span>
              <span className="text-ink-soft" style={{ fontSize: 9 }}>Anmeldungsstatus</span>
            </div>
            <div className="border-t border-line-subtle" style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr" }}>
              <div
                onClick={expectedCount > 0 ? () => setKioskDrawerFilter("expected") : undefined}
                style={{ padding: "7px 6px", textAlign: "center", cursor: expectedCount > 0 ? "pointer" : "default" }}
              >
                <div className={`font-mono ${expectedCount > 0 ? "text-ink" : ""}`} style={{ fontSize: 17, fontWeight: 700, color: expectedCount > 0 ? undefined : "rgb(var(--text-disabled))" }}>{expectedCount}</div>
                <div className="text-ink-soft" style={{ fontSize: 8, textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Expected</div>
              </div>
              <div
                onClick={checkedInCount > 0 ? () => setKioskDrawerFilter("checkedIn") : undefined}
                className="border-x border-line-subtle"
                style={{ padding: "7px 6px", textAlign: "center", cursor: checkedInCount > 0 ? "pointer" : "default" }}
              >
                <div className="font-mono" style={{ fontSize: 17, fontWeight: 700, color: checkedInCount > 0 ? "rgb(var(--signal-live))" : "rgb(var(--text-disabled))" }}>{checkedInCount}</div>
                <div className="text-ink-soft" style={{ fontSize: 8, textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Checked In</div>
              </div>
              <div
                onClick={notYetInCount > 0 ? () => setKioskDrawerFilter("notYetIn") : undefined}
                style={{ padding: "7px 6px", textAlign: "center", cursor: notYetInCount > 0 ? "pointer" : "default" }}
              >
                <div className={`font-mono ${notYetInCount > 0 ? "text-warn-fg" : ""}`} style={{ fontSize: 17, fontWeight: 700, color: notYetInCount > 0 ? undefined : "rgb(var(--text-disabled))" }}>{notYetInCount}</div>
                <div className="text-ink-soft" style={{ fontSize: 8, textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Not Yet In</div>
              </div>
            </div>
          </div>
        )}

        <div style={{ flex: 1, overflowY: "auto" }}>
          {forecastLoading
            ? Array.from({ length: 10 }).map((_, i) => (
                <div key={i} style={{ padding: "10px 12px", borderBottom: "1px solid rgb(var(--line-subtle))" }}>
                  <Skeleton height={13} width="75%" />
                  <div style={{ marginTop: 5 }}><Skeleton height={9} width="45%" /></div>
                </div>
              ))
            : filteredLocations.length === 0
            ? <div className="text-ink-soft" style={{ padding: 20, textAlign: "center", fontSize: 12 }}>
                {t("attendance.noLocations")}
              </div>
            : filteredLocations.map(loc => {
                const dayData        = (loc.forecast ?? []).find(d => d.date === selectedDate)
                const status         = dayData?.status ?? "CLOSED"
                const isSelected     = loc.locationCode === selectedLocationCode
                const locActiveCount = kioskActiveAt(kioskData, loc.displayName).length
                return (
                  <div
                    key={loc.locationCode}
                    onClick={() => setSelectedLocationCode(loc.locationCode)}
                    style={{
                      padding: "9px 12px", cursor: "pointer",
                      background: isSelected ? "rgb(var(--st-info-bg))" : "transparent",
                      borderLeft: isSelected ? "2px solid rgb(var(--st-info-solid))" : "2px solid transparent",
                      borderBottom: "1px solid rgb(var(--line-subtle))",
                      transition: "background 0.1s",
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 6 }}>
                      <span style={{ display: "flex", alignItems: "center", gap: 4, flex: 1, minWidth: 0 }}>
                        <span className={isSelected ? "text-info-fg" : "text-ink"} style={{
                          fontSize: 12, fontWeight: isSelected ? 600 : 400,
                          overflow: "hidden", textOverflow: "ellipsis",
                          whiteSpace: "nowrap",
                        }}>
                          {loc.displayName}
                        </span>
                        {loc.isNpp && <NppBadge />}
                      </span>
                      <div style={{ display: "flex", alignItems: "center", gap: 4 }}>
                        {locActiveCount > 0 && (
                          <span style={{ display: "flex", alignItems: "center", gap: 2 }}>
                            <span style={{
                              width: 6, height: 6, borderRadius: "50%",
                              background: "rgb(var(--signal-live))", display: "inline-block",
                            }} />
                            <span className="font-mono" style={{ fontSize: 9, color: "rgb(var(--signal-live))" }}>
                              {locActiveCount}
                            </span>
                          </span>
                        )}
                        <CoverageBadge status={status} compact />
                      </div>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", marginTop: 3 }}>
                      <span className="text-ink-soft" style={{ fontSize: 10 }}>{loc.city}</span>
                      {loc.atRiskDays > 0 && (
                        <span className="text-crit-fg font-mono" style={{
                          fontSize: 9,
                          display: "flex", alignItems: "center", gap: 2,
                        }}>
                          <AlertTriangle size={8} />
                          {loc.atRiskDays}d
                        </span>
                      )}
                    </div>
                  </div>
                )
              })
          }
        </div>

        {forecast && (
          <div className="border-t border-line-subtle text-ink-soft font-mono" style={{ padding: "7px 12px", flexShrink: 0, fontSize: 10 }}>
            {forecast.locationCount} loc · {forecast.totalAtRiskDays} at-risk
          </div>
        )}
      </aside>

      {/* ── RIGHT CONTENT ─────────────────────────────────────────────────────── */}
      <div style={{ flex: 1, overflowY: "auto", padding: 20, minWidth: 0 }}>
        {!selectedLocationCode ? (
          <div style={{
            display: "flex", alignItems: "center", justifyContent: "center",
            height: 200, fontSize: 13,
          }} className="text-ink-soft">
            {t("attendance.selectLocation")}
          </div>
        ) : (
          <>
            {/* Header */}
            <div style={{
              display: "flex", justifyContent: "space-between",
              alignItems: "flex-start", marginBottom: 16, gap: 12,
            }}>
              <div>
                <div className="text-ink" style={{ fontSize: 20, fontWeight: 600, display: "flex", alignItems: "center", gap: 8 }}>
                  {selectedForecast?.displayName ?? selectedCard?.displayName ?? "—"}
                  {selectedForecast?.isNpp && <NppBadge />}
                </div>
                <div className="text-ink-soft" style={{ fontSize: 12, marginTop: 2 }}>
                  {selectedForecast?.city} · {selectedForecast?.country}
                </div>
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
                {kioskError ? (
                  <span style={{
                    display: "flex", alignItems: "center", gap: 5,
                    background: "rgb(var(--st-warn-bg))", color: "rgb(var(--st-warn-fg))",
                    border: "1px solid rgb(var(--st-warn-bd))",
                    padding: "4px 10px", borderRadius: 20,
                    fontSize: 11, fontWeight: 600,
                  }}>
                    <AlertTriangle size={11} />
                    {t("attendance.kiosk.offline")}
                  </span>
                ) : presentAgents.length > 0 && (
                  <span style={{
                    display: "flex", alignItems: "center", gap: 5,
                    background: "rgb(var(--signal-live) / 0.15)", color: "rgb(var(--signal-live))",
                    border: "1px solid rgb(var(--signal-live) / 0.35)",
                    padding: "4px 10px", borderRadius: 20,
                    fontSize: 11, fontWeight: 600,
                  }}>
                    <span style={{
                      width: 7, height: 7, borderRadius: "50%",
                      background: "rgb(var(--signal-live))", display: "inline-block",
                      animation: "pulse-green 1.5s ease-in-out infinite",
                    }} />
                    Live {presentAgents.length}
                  </span>
                )}
                <button
                  onClick={() => setAlPlanningOpen(true)}
                  className="bg-raised border border-line-subtle text-ink"
                  style={{
                    padding: "8px 14px", borderRadius: 6, fontSize: 12,
                    cursor: "pointer", display: "flex", alignItems: "center",
                    gap: 6, fontWeight: 500,
                  }}
                >
                  <Calendar size={13} />
                  {t("attendance.alPlanning.button")}
                </button>
                <button
                  onClick={() => setAssignAgentOpen(true)}
                  className="bg-raised border border-line-subtle text-ink"
                  style={{
                    padding: "8px 14px", borderRadius: 6, fontSize: 12,
                    cursor: "pointer", display: "flex", alignItems: "center",
                    gap: 6, fontWeight: 500,
                  }}
                >
                  <Users size={13} />
                  {t("attendance.assignAgent.button")}
                </button>
                <button
                  onClick={() => setManualCheckinOpen(true)}
                  className="bg-raised border border-line-subtle text-ink"
                  style={{
                    padding: "8px 14px", borderRadius: 6, fontSize: 12,
                    cursor: "pointer", display: "flex", alignItems: "center",
                    gap: 6, fontWeight: 500,
                  }}
                >
                  <Clock size={13} />
                  {t("attendance.manualCheckin.button")}
                </button>
                <button
                  onClick={() => openSub(selectedDate)}
                  className="bg-info-solid text-white"
                  style={{
                    border: "none",
                    padding: "8px 14px", borderRadius: 6, fontSize: 12,
                    cursor: "pointer", display: "flex", alignItems: "center",
                    gap: 6, fontWeight: 600,
                  }}
                >
                  <UserCheck size={13} />
                  {t("attendance.substitute.find")}
                </button>
              </div>
            </div>

            {/* Currently Present */}
            {presentAgents.length > 0 && (
              <div style={{
                background: "rgb(var(--signal-live) / 0.07)",
                border: "1px solid rgb(var(--signal-live) / 0.25)",
                borderRadius: 8, padding: "12px 16px", marginBottom: 14,
              }}>
                <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 10 }}>
                  <span style={{
                    width: 8, height: 8, borderRadius: "50%",
                    background: "rgb(var(--signal-live))", display: "inline-block",
                    animation: "pulse-green 1.5s ease-in-out infinite",
                  }} />
                  <span style={{ fontSize: 12, fontWeight: 600, color: "rgb(var(--signal-live))" }}>
                    Currently Present · {presentAgents.length}
                  </span>
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  {presentAgents.map(r => (
                    <div key={r.employee_id} style={{
                      display: "flex", alignItems: "center", gap: 6,
                      background: "rgb(var(--signal-live) / 0.1)", border: "1px solid rgb(var(--signal-live) / 0.2)",
                      borderRadius: 6, padding: "5px 10px",
                    }}>
                      <span style={{
                        width: 6, height: 6, borderRadius: "50%",
                        background: "rgb(var(--signal-live))", display: "inline-block",
                        animation: "pulse-green 1.5s ease-in-out infinite",
                      }} />
                      <span className="text-ink" style={{ fontSize: 12, fontWeight: 600 }}>{r.full_name}</span>
                      {r.checkin_time && (
                        <span className="font-mono" style={{ fontSize: 10, color: "rgb(var(--signal-live))" }}>
                          {r.checkin_time.slice(11, 16)}
                        </span>
                      )}
                      {r.minutes_on_shift > 0 && (
                        <span className="text-ink-soft font-mono" style={{ fontSize: 10 }}>
                          {r.minutes_on_shift}m
                        </span>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Stats row */}
            <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 10, marginBottom: 14 }}>
              {(cardsLoading || forecastLoading)
                ? Array.from({ length: 4 }).map((_, i) => (
                    <div key={i} className="bg-raised border border-line-subtle" style={{ borderRadius: 8, padding: "12px 16px" }}>
                      <Skeleton height={10} width="55%" />
                      <div style={{ marginTop: 8 }}><Skeleton height={22} width="40%" /></div>
                    </div>
                  ))
                : <>
                    <StatCard label={t("status.occupied")}>
                      <CoverageBadge status={selectedCard?.coverageStatus ?? selectedDay?.status ?? "CLOSED"} />
                    </StatCard>
                    <StatCard label={t("attendance.agents.title")}>
                      <div className={`font-mono ${selectedDay?.isAtRisk ? "text-crit-fg" : "text-good-fg"}`} style={{ fontSize: 26, fontWeight: 600 }}>
                        {selectedDay?.effectiveCoverage ?? "—"}
                      </div>
                    </StatCard>
                    <StatCard label={t("attendance.risk.minRequired", { n: selectedDay?.minRequired ?? "?" })}>
                      <div className="font-mono text-ink-muted" style={{ fontSize: 26, fontWeight: 600 }}>
                        {selectedDay?.minRequired ?? "—"}
                      </div>
                    </StatCard>
                    <StatCard label={selectedCard?.todaySchedule.isClosed ? t("status.closed") : t("attendance.today")}>
                      <div className="font-mono text-ink-muted" style={{ fontSize: 14, fontWeight: 600, marginTop: 4 }}>
                        {selectedCard?.todaySchedule.isClosed
                          ? t("status.closed")
                          : selectedCard?.todaySchedule.openTime && selectedCard?.todaySchedule.closeTime
                          ? `${selectedCard.todaySchedule.openTime}–${selectedCard.todaySchedule.closeTime}`
                          : "—"}
                      </div>
                    </StatCard>
                  </>
              }
            </div>

            {/* Zone B — Not Checked In */}
            {cards && (
              <div style={{ marginBottom: 14 }}>
                {zoneBEmergency.map(r => (
                  <div key={r.employeeId} className="rounded-lg border border-crit-bd bg-crit-bg px-4 py-3 flex items-center gap-3 mb-3">
                    <AlertTriangle size={16} className="text-crit-solid" />
                    <span className="text-crit-fg font-medium text-sm">
                      {r.name} · {r.locationCode} · {r.locationCity} · {r.shiftStart}
                    </span>
                  </div>
                ))}
                <Panel title={t("attendance.zoneB.title")}>
                  {zoneBMain.length === 0 ? (
                    <div className="flex items-center gap-2 py-3 px-4 text-good-fg text-sm">
                      <CheckCircle2 size={14} />
                      {t("attendance.zoneB.allCheckedIn")}
                    </div>
                  ) : (
                    <DataTable columns={zoneBColumns} data={zoneBMain} rowKey={r => r.employeeId} />
                  )}
                </Panel>
                {zoneBUpcoming.length > 0 && (
                  <div style={{ marginTop: 10 }}>
                    <Panel
                      title={`${t("attendance.zoneB.upcoming")} (${zoneBUpcoming.length})`}
                      collapsible
                      storageKey="wic-zone-b-upcoming"
                      defaultOpen={false}
                    >
                      <DataTable columns={zoneBColumns} data={zoneBUpcoming} rowKey={r => r.employeeId} />
                    </Panel>
                  </div>
                )}
                {zoneBNoRecord.length > 0 && (
                  <div style={{ marginTop: 10 }}>
                    <Panel
                      title={`${t("attendance.zoneB.noKiosk")} (${zoneBNoRecord.length})`}
                      collapsible
                      storageKey="wic-zone-b-no-kiosk"
                      defaultOpen={false}
                    >
                      <DataTable columns={zoneBColumns} data={zoneBNoRecord} rowKey={r => r.employeeId} className="opacity-60" />
                    </Panel>
                  </div>
                )}
              </div>
            )}

            {/* Agent chips */}
            <SectionCard title={t("attendance.agents.title")} icon={<Users size={13} />}>
              {cardsLoading ? (
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} width={130} height={64} />)}
                </div>
              ) : !selectedCard || selectedCard.assignedAgents.length === 0 ? (
                <div className="text-ink-soft" style={{ fontSize: 12 }}>
                  {t("attendance.agents.noAgents")}
                </div>
              ) : (
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  {selectedCard.assignedAgents.map(agent => {
                    const mc = AGENT_MATCH_COLORS[agent.coverageMatch] ?? AGENT_MATCH_COLORS.NONE
                    const kiosk = kioskMap.get(agent.employeeId)
                    return (
                      <div key={agent.employeeId} style={{
                        background: mc.bg, border: `1px solid ${mc.color}44`,
                        borderRadius: 8, padding: "9px 12px", minWidth: 130,
                        display: "flex", flexDirection: "column", gap: 4,
                      }}>
                        <div style={{ display: "flex", alignItems: "center", gap: 5 }}>
                          {kiosk?.attendance_status === "ACTIVE" && (
                            <span style={{
                              width: 7, height: 7, borderRadius: "50%",
                              background: "rgb(var(--signal-live))", display: "inline-block", flexShrink: 0,
                              animation: "pulse-green 1.5s ease-in-out infinite",
                            }} />
                          )}
                          {kiosk?.attendance_status === "DONE" && (
                            <span style={{
                              width: 7, height: 7, borderRadius: "50%",
                              background: "rgb(var(--st-ink-soft))", display: "inline-block", flexShrink: 0,
                            }} />
                          )}
                          <span className="text-ink" style={{ fontSize: 12, fontWeight: 600 }}>{agent.name}</span>
                        </div>
                        <div style={{ display: "flex", gap: 4, alignItems: "center" }}>
                          <span className={agent.isMain ? "text-info-fg" : "text-ink-soft"} style={{
                            fontSize: 9, fontWeight: 600, textTransform: "uppercase" as const,
                            background: agent.isMain ? "rgb(var(--st-info-bg))" : "rgb(var(--st-neutral-bg))",
                            padding: "1px 5px", borderRadius: 3,
                          }}>
                            {agent.isMain ? t("attendance.agents.main") : t("attendance.agents.backup")}
                          </span>
                          <span style={{ fontSize: 9, color: mc.color, textTransform: "uppercase" as const }}>
                            {agent.coverageMatch === "FULL"
                              ? t("attendance.agents.full")
                              : agent.coverageMatch === "PARTIAL"
                              ? t("attendance.agents.partial")
                              : t("attendance.agents.absent")}
                          </span>
                        </div>
                        {(agent.shiftStart || agent.shiftEnd) && (
                          <div className="text-ink-soft font-mono" style={{ fontSize: 9 }}>
                            {agent.shiftStart === "SICK" || agent.shiftEnd === "SICK"
                              ? "SL"
                              : agent.shiftStart === "AL" || agent.shiftEnd === "AL"
                              ? "AL"
                              : `${agent.shiftStart ?? ""}–${agent.shiftEnd ?? ""}`}
                          </div>
                        )}
                        {kiosk?.attendance_status === "ACTIVE" && (
                          <div className="font-mono" style={{ fontSize: 9, color: "rgb(var(--signal-live))" }}>
                            {kiosk.checkin_time ? kiosk.checkin_time.slice(11, 16) : "checked in"}
                          </div>
                        )}
                        {kiosk?.attendance_status === "DONE" && (
                          <div className="text-ink-soft font-mono" style={{ fontSize: 9 }}>
                            {kiosk.checkout_time ? kiosk.checkout_time.slice(11, 16) : "done"}
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              )}
            </SectionCard>

            {/* Opening-hours schedule editor */}
            {selectedLocationCode && <WicScheduleEditor locationCode={selectedLocationCode} />}

            {/* Per-weekday required headcount editor */}
            {selectedLocationCode && <MinRequiredEditor locationCode={selectedLocationCode} />}

            {/* N-day forecast mini-calendar, grouped by week */}
            <SectionCard
              title={t("attendance.horizon", { n: horizonDays })}
              icon={<Clock size={13} />}
              style={{ marginTop: 14 }}
              action={
                <div style={{ display: "flex", gap: 4 }}>
                  {HORIZON_OPTIONS.map(n => (
                    <button
                      key={n}
                      onClick={() => setHorizonDays(n)}
                      className={horizonDays === n
                        ? "bg-info-solid border border-info-bd text-white"
                        : "bg-sunken border border-line-subtle text-ink-muted"}
                      style={{ borderRadius: 5, padding: "3px 9px", fontSize: 10, fontWeight: 600, cursor: "pointer" }}
                    >
                      {n}d
                    </button>
                  ))}
                </div>
              }
            >
              {forecastLoading ? (
                <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                  {Array.from({ length: Math.ceil(horizonDays / 7) }).map((_, w) => (
                    <div key={w} style={{ display: "flex", gap: 6 }}>
                      {Array.from({ length: 7 }).map((_, i) => <Skeleton key={i} width={78} height={68} />)}
                    </div>
                  ))}
                </div>
              ) : !(selectedForecast?.forecast?.length) ? (
                <div className="text-ink-soft" style={{ fontSize: 12 }}>—</div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                  {chunkWeeks(selectedForecast.forecast ?? []).map((week, wi) => (
                    <div key={wi} style={{ display: "flex", gap: 6 }}>
                      {week.map(day => (
                        <div
                          key={day.date}
                          onClick={() => { setSelectedDate(day.date); if (day.isAtRisk) openSub(day.date) }}
                          className={`${day.date === selectedDate ? "border border-info-bd" : "bg-sunken border border-line-subtle"}`}
                          style={{
                            background: day.date === selectedDate ? "rgb(var(--st-info-bg))" : undefined,
                            borderRadius: 8, padding: "8px 4px",
                            flex: "1 1 0", minWidth: 0, cursor: "pointer",
                            transition: "all 0.1s",
                            display: "flex", flexDirection: "column", gap: 4, alignItems: "center",
                          }}
                        >
                          <div className="text-ink-soft font-mono" style={{ fontSize: 10 }}>
                            {new Date(day.date + "T00:00:00").toLocaleDateString(undefined, { weekday: "short" })}
                          </div>
                          <div className="text-ink-muted font-mono" style={{ fontSize: 11 }}>
                            {day.date.slice(5)}
                          </div>
                          <div className={`w-full h-6 rounded-[3px] border ${
                            day.status === "COVERED"   ? "bg-good-mid border-good-solid/25" :
                            day.status === "PARTIAL"   ? "bg-warn-mid border-warn-solid/25" :
                            day.status === "UNCOVERED" ? "bg-crit-mid border-crit-solid/25" :
                                                         "bg-neutralst-mid border-neutralst-solid/25"
                          }`} />
                          {day.isAtRisk && <AlertTriangle size={10} className="text-crit-fg" />}
                        </div>
                      ))}
                    </div>
                  ))}
                </div>
              )}
            </SectionCard>
          </>
        )}
      </div>

      {/* ── SUBSTITUTE SHEET ──────────────────────────────────────────────────── */}
      <Sheet
        isOpen={isSheetOpen}
        onClose={() => setIsSheetOpen(false)}
        title={`${t("attendance.substitute.title")} · ${subs?.displayName ?? selectedForecast?.displayName ?? "…"}`}
      >
        {subsLoading ? (
          <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
            <Skeleton height={11} width="40%" />
            {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} style={{ marginTop: 8 }}>
                <Skeleton height={18} />
                <div style={{ marginTop: 4 }}><Skeleton height={11} width="60%" /></div>
              </div>
            ))}
          </div>
        ) : !subsDay ? (
          <div className="text-ink-soft" style={{ fontSize: 13, marginTop: 10 }}>
            {t("attendance.substitute.loading")}
          </div>
        ) : subsDay.candidates.length === 0 ? (
          <div className="text-ink-soft" style={{ fontSize: 13, textAlign: "center", marginTop: 20 }}>
            {t("attendance.substitute.noCandidates")}
          </div>
        ) : (
          <>
            <div className="text-ink-soft font-mono" style={{ fontSize: 11, marginBottom: 14 }}>
              {sheetDate} · {t("attendance.risk.effectiveCoverage", { n: subsDay.present })} · gap {subsDay.gap}
            </div>
            {acceptError && (
              <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 8 }}>
                {acceptError}
              </div>
            )}
            {acceptedSubId && (
              <div className="bg-good-bg border border-good-bd text-good-fg" style={{ borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 8 }}>
                {t("attendance.substitute.confirmed", {
                  name: acceptedSubName,
                  wic: subs?.displayName ?? selectedForecast?.displayName ?? ""
                })}
              </div>
            )}
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {subsDay.candidates.map((c, i) => {
                const sc = SOURCE_COLORS[c.sourceType] ?? SOURCE_COLORS.CALL_IN
                return (
                  <div key={c.employeeId}
                    className={acceptedSubId === c.employeeId
                      ? "bg-good-bg border border-good-bd"
                      : "bg-raised border border-line-subtle"}
                    style={{ borderRadius: 8, padding: "12px 14px" }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <span className="text-ink-soft font-mono" style={{ fontSize: 11, minWidth: 18 }}>
                          {i + 1}.
                        </span>
                        <span className="text-ink" style={{ fontSize: 13, fontWeight: 600 }}>{c.fullName}</span>
                      </div>
                      {(c.sourceType === "BACKUP" || c.sourceType === "REGIONAL") ? (
                        <StatusBadge tone={c.sourceType === "REGIONAL" ? "wic" : "learn"} variant="outline">
                          {c.sourceType}
                        </StatusBadge>
                      ) : (
                        <span style={{
                          fontSize: 9, fontWeight: 700, textTransform: "uppercase" as const,
                          background: sc.bg, color: sc.color, padding: "2px 7px", borderRadius: 4,
                        }}>
                          {c.sourceType}
                        </span>
                      )}
                    </div>
                    <div className="text-ink-soft" style={{ marginTop: 6, display: "flex", gap: 12, fontSize: 11, flexWrap: "wrap" }}>
                      <span>{c.homeLocationName}</span>
                      <span className="font-mono">
                        {t("attendance.substitute.distance", { km: (c.distanceKm ?? 0).toFixed(0) })}
                      </span>
                      {c.loadScore > 0 && (
                        <span className="text-warn-fg font-mono">
                          {t("attendance.substitute.lastUsed", { n: c.loadScore })}
                        </span>
                      )}
                    </div>
                    {!acceptedSubId && (
                      <div style={{ marginTop: 10, display: "flex", justifyContent: "flex-end" }}>
                        <button
                          onClick={() => handleAcceptSub(c)}
                          disabled={acceptingId === c.employeeId}
                          className="bg-good-solid text-white"
                          style={{
                            border: "none",
                            borderRadius: 5, padding: "5px 14px", fontSize: 11, fontWeight: 600,
                            cursor: acceptingId === c.employeeId ? "not-allowed" : "pointer",
                            opacity: acceptingId === c.employeeId ? 0.6 : 1,
                          }}
                        >
                          {t("attendance.substitute.accept")}
                        </button>
                      </div>
                    )}
                  </div>
                )
              })}
            </div>
          </>
        )}
      </Sheet>

      {/* ── KIOSK STATUS DRAWER ──────────────────────────────────────────────── */}
      <Sheet
        isOpen={kioskDrawerFilter !== null}
        onClose={() => setKioskDrawerFilter(null)}
        title={
          kioskDrawerFilter === "checkedIn" ? t("attendance.kioskDrawer.checkedIn", { n: checkedInCount }) :
          kioskDrawerFilter === "expected"  ? t("attendance.kioskDrawer.expected",  { n: expectedCount })  :
          kioskDrawerFilter === "notYetIn"  ? t("attendance.kioskDrawer.notYetIn",  { n: notYetInCount })  : ""
        }
      >
        {kioskError ? (
          <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: 16, padding: "32px 0", textAlign: "center" }}>
            <AlertTriangle size={28} style={{ color: "rgb(var(--st-warn-solid))" }} />
            <p className="text-sm font-medium text-ink">{t("attendance.kiosk.offline")}</p>
            <button
              onClick={() => queryClient.refetchQueries({ queryKey: ["kiosk-attendance"] })}
              className="bg-raised border border-line-subtle text-ink"
              style={{ display: "flex", alignItems: "center", gap: 6, padding: "6px 14px", borderRadius: 6, cursor: "pointer", fontSize: 12 }}
            >
              <RefreshCw size={12} />
              Retry
            </button>
          </div>
        ) : kioskDrawerRows.length === 0 ? (
          <EmptyState title={
            kioskDrawerFilter === "checkedIn" ? t("attendance.kioskDrawer.empty.checkedIn") :
            kioskDrawerFilter === "expected"  ? t("attendance.kioskDrawer.empty.expected")  :
                                                t("attendance.kioskDrawer.empty.notYetIn")
          } />
        ) : (
          <div style={{ margin: "0 -20px" }}>
            {kioskDrawerRows.map(row => (
              <div key={row.key} className="border-b border-line-subtle" style={{ display: "flex", alignItems: "center", gap: 10, height: 44, padding: "0 20px" }}>
                {/* live dot */}
                <div style={{ width: 12, flexShrink: 0, display: "flex", justifyContent: "center" }}>
                  {row.status === "ACTIVE" && (
                    <span style={{ width: 7, height: 7, borderRadius: "50%", background: "rgb(var(--signal-live))", display: "inline-block", animation: "pulse-green 1.5s ease-in-out infinite" }} />
                  )}
                </div>
                {/* name + location */}
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="text-sm font-medium text-ink truncate">{row.name}</div>
                  <div className="text-xs text-ink-soft truncate">{row.location || "—"}</div>
                </div>
                {/* since */}
                <div className="font-mono text-sm text-ink" style={{ flexShrink: 0, minWidth: 36, textAlign: "right" }}>
                  {row.checkinTime ? row.checkinTime.slice(11, 16) : row.shiftStart ?? "—"}
                </div>
                {/* duration */}
                <div className="font-mono text-sm text-ink-muted" style={{ flexShrink: 0, minWidth: 48, textAlign: "right" }}>
                  {checkinDuration(row.checkinTime)}
                </div>
                {/* status badge */}
                <StatusBadge tone={row.status === "ACTIVE" ? "good" : "mutedst"}>
                  {row.status === "ACTIVE"
                    ? t("attendance.kioskDrawer.status.active")
                    : row.status === "DONE"
                    ? t("attendance.kioskDrawer.status.done")
                    : t("attendance.kioskDrawer.status.notIn")}
                </StatusBadge>
              </div>
            ))}
          </div>
        )}
      </Sheet>

      <ALPlanningModal isOpen={alPlanningOpen} onClose={() => setAlPlanningOpen(false)} />
      <AssignAgentModal
        isOpen={assignAgentOpen}
        onClose={() => setAssignAgentOpen(false)}
        defaultLocationCode={selectedLocationCode}
        defaultDate={selectedDate}
      />
      <ManualCheckinModal isOpen={manualCheckinOpen} onClose={() => setManualCheckinOpen(false)} />
    </div>
  )
}
