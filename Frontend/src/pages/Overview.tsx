import "leaflet/dist/leaflet.css"
import L from "leaflet"
import infosys1 from '../assets/infosys-1.webp'
import infosys2 from '../assets/infosys-2.webp'
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { useState, useEffect, type CSSProperties } from "react"
import { useSearchParams, Link } from "react-router-dom"
import { useTheme } from "next-themes"
import { MapContainer, TileLayer, CircleMarker, Popup, useMap } from "react-leaflet"
import { AlertTriangle, Users } from "lucide-react"
import { useQueryClient } from "@tanstack/react-query"
import { api, apiFetch } from "../api/client"
import { postSubstituteAccept, substituteAcceptErrorText } from "../utils/substituteAccept"
import { coverageBannerState, type WicCoverageBannerState } from "../utils/wicCoverageState"
import { parseHorizon, nextHorizon, MAX_HORIZON } from "../utils/wicHorizon"
import { useAuth } from "../auth/AuthContext"
import { statusColor } from "../lib/tokenColor"
import { Sheet } from "../components/Sheet"
import { NppBadge } from "../components/NppBadge"
import { Panel } from "../components/Panel"
import { DayStrip } from "./overview/DayStrip"
import { ExceptionList } from "./overview/ExceptionList"
import NewShiftModal from "./WICShifts/NewShiftModal"

// ── Types ─────────────────────────────────────────────────────────────────────

interface DayForecast {
  date: string; status: string; effectiveCoverage: number; minRequired: number
}
interface LocationForecast {
  locationCode: string; displayName: string; city: string; country: string
  coordinates: string | null; isNpp: boolean; forecast: DayForecast[]
  atRiskDays: number; todayStatus: string
}
interface BriefingGap {
  locationCode: string; displayName: string
  present: number; required: number; gap: number; status: string
  bestSubstituteId: string | null
  bestSubstituteName: string | null; bestSubstituteSource: string | null
  // no gapDate — gaps are always for today
  // no bestSubstituteDistanceKm — not in API
}
interface BriefingAbsence {
  employeeId: string; fullName: string | null; teamLead: string | null
  shiftType: string; wicLocation: string | null
  firstDay: string | null; lastDay: string | null
  totalDays: number; daysSoFar: number
}
interface BriefingAtRisk {
  date: string; dayOfWeek: string; locationCode: string; displayName: string
  status: string; present: number; required: number
}
interface Briefing {
  date: string; totalAbsences: number; totalGaps: number
  absences: BriefingAbsence[]
  gaps: BriefingGap[]
  nextAtRiskDays?: BriefingAtRisk[]
}
interface WicLocation {
  locationCode: string; displayName: string; city: string
  country: string; coordinates: string | null
}

// ── /api/wic/cards — verified from live response 2026-08-01 (Saturday, all CLOSED) ──────────
// assignedAgents structure confirmed from render code only; verify on an open weekday.
// mainAgents/backupAgents are static name lists — not today's schedule, not rendered.
// coveragePercent and todaySchedule are in the API but not currently rendered.
interface WicCardTodaySchedule {
  isClosed: boolean; rawSchedule: string; totalOpenMinutes: number
  openTime: string | null; closeTime: string | null
  openTime2: string | null; closeTime2: string | null
}
interface WicCardAssignedAgent {
  employeeId: string; name: string
  shiftStart: string   // "06:00" or absence marker "SL" | "SICK" | "AL" | "HALF_AL" | "UL" | "OL" | "PH" | "LPH" | "OFF" | "OFF_WEEKEND" | "RESIGNED" | "GSD"
  shiftEnd: string; coverageMatch: "FULL" | "PARTIAL"
}
interface WicCardDto {
  locationCode: string; displayName: string; city: string
  country: string; address: string | null; isNpp: boolean
  todaySchedule: WicCardTodaySchedule
  assignedAgents: WicCardAssignedAgent[]
  mainAgents: string[]     // names only — static assignment, not today's roster
  backupAgents: string[]   // names only — static assignment, not today's roster
  coverageStatus: "COVERED" | "PARTIAL" | "UNCOVERED" | "CLOSED"
  coveragePercent: number
}

interface WicConflictEntry {
  entryId: number; supportLocation: string; locationCode: string
  locationDisplayName: string; assignmentRole: string
  openTime: string | null; closeTime: string | null
}
interface WicConflict {
  employeeId: string; fullName: string | null; shiftDate: string
  conflictType: string   // "OVERLAP" | "SPLIT_SHIFT" | "CLOSED_LOCATION"
  locations: WicConflictEntry[]
}

// ── Color maps ────────────────────────────────────────────────────────────────

// Leaflet marker colors come from statusColor() in lib/tokenColor — reads CSS custom properties at
// render time so they follow the active theme. No hardcoded hex.

// CSS inline-style contexts (HTML elements, not SVG attributes)
const STATUS_TOKEN_FG: Record<string, string> = {
  COVERED:   "rgb(var(--st-good-solid))",
  PARTIAL:   "rgb(var(--st-warn-solid))",
  UNCOVERED: "rgb(var(--st-crit-solid))",
  CLOSED:    "rgb(var(--st-neutral-solid))",
}
const STATUS_TOKEN_BG: Record<string, string> = {
  COVERED:   "rgb(var(--st-good-bg))",
  PARTIAL:   "rgb(var(--st-warn-bg))",
  UNCOVERED: "rgb(var(--st-crit-bg))",
  CLOSED:    "rgb(var(--st-neutral-bg))",
}

// Tailwind classes for legend swatches — §1.4: CLOSED was "#7a8fa8" (blue-grey hex), now bg-neutralst-solid
const LEGEND_CLASS: Record<string, string> = {
  COVERED:   "bg-good-solid",
  PARTIAL:   "bg-warn-solid",
  UNCOVERED: "bg-crit-solid",
  CLOSED:    "bg-neutralst-solid",
}

// -mid tokens for dense heatmap cells — full class strings (no concatenation, tree-shaking requires this).
const HEATMAP_CELL_CLASS: Record<string, string> = {
  COVERED:   "bg-good-mid border border-good-solid/25",
  PARTIAL:   "bg-warn-mid border border-warn-solid/25",
  UNCOVERED: "bg-crit-mid border border-crit-solid/30",
  CLOSED:    "bg-neutralst-mid border border-neutralst-solid/20",
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function parseCoords(s: string | null | undefined): [number, number] | null {
  if (!s) return null
  const p = s.split(",").map(x => Number(x.trim()))
  if (p.length !== 2 || p.some(isNaN)) return null
  return [p[0], p[1]]
}

// ── Sub-components ────────────────────────────────────────────────────────────

function KpiCard({ label, value, color, isLoading }: { label: string; value: string | number | null; color?: string; isLoading?: boolean }) {
  const { t } = useTranslation()
  return (
    <div className="bg-raised border border-line-subtle rounded-md" style={{
      padding: "16px 20px",
      borderTop: `3px solid ${color ?? "rgb(var(--border-subtle))"}`,
    }}>
      <div className="text-[10px] font-medium uppercase tracking-[.08em] text-ink-soft mb-1.5">
        {label}
      </div>
      {isLoading
        ? <div className="skeleton" style={{ height: 34, width: 64 }} />
        : value === null
          ? <>
              <div style={{ fontSize: 32, fontWeight: 700, fontFamily: "monospace", color: "rgb(var(--text-tertiary))" }}>—</div>
              <div className="text-[10px] text-ink-soft mt-0.5">{t("common.notAvailable")}</div>
            </>
          : <div style={{ fontSize: 32, fontWeight: 700, fontFamily: "monospace", color: color ?? "rgb(var(--text-primary))" }}>{value}</div>
      }
    </div>
  )
}

function WarningBanner({ msg }: { msg: string }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 8,
      background: STATUS_TOKEN_BG["PARTIAL"], border: `1px solid rgb(var(--st-warn-bd))`,
      borderRadius: 6, padding: "8px 14px", fontSize: 12, color: STATUS_TOKEN_FG["PARTIAL"],
    }}>
      <AlertTriangle size={13} style={{ flexShrink: 0 }} />
      {msg}
    </div>
  )
}

// ── Missing-roster warning ────────────────────────────────────────────────────
// Mirrors the records in Backend/RosterService.cs (GET /api/roster/missing).
// RTM / TEAM_LEAD / DEV only — the query is not even fired for AGENT, and the
// server enforces the same gate (RosterAccess → 403) regardless of the global
// Auth:EnforceAuthorization rollout flag.
// i18n exception: operational warning, same hardcoded-EN approach as Roster.tsx.
interface MissingRosterAgent {
  employeeId: string; fullName: string | null; teamLeadName: string | null
  primaryRole: string | null; lastShiftDate: string | null
}
interface OrphanShiftEntryGroup {
  employeeId: string; rowCount: number; firstDate: string; lastDate: string
  hasInactiveEmployeeRow: boolean
}
interface MissingRosterResult {
  days: number; from: string; to: string
  missingRoster: MissingRosterAgent[]; orphanEntries: OrphanShiftEntryGroup[]
}

const rosterPill: CSSProperties = {
  display: "inline-block", padding: "3px 10px", borderRadius: 999,
  background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--st-warn-bd))",
  color: "inherit", textDecoration: "none", fontWeight: 600, whiteSpace: "nowrap",
}

// ── AL Tracker vs Shift Plan conflicts ───────────────────────────────────────
// OWNER RULE (2026-10-16): the Shift Plan wins over the AL Tracker. When RTM removes an
// AL in the Shift Plan and puts a duty on the day, the AL Tracker ("Annual Leave" tab,
// Vacations table) keeps its entry and every reader used to show the agent as ABSENT (AL).
// That entry is now IGNORED (see Backend/Services/AlTrackerOverrides.cs) and this banner is
// the warning that tells whoever maintains the AL Tracker which entry to correct.
// Deliberately shows the EmployeeId and the AL entry id ONLY — no names.
interface AlTrackerConflict {
  date: string
  employeeId: string
  alTrackerEntryId: number
  trackerFirstDay: string
  trackerLastDay: string
  shiftType: string
  agentTask: string | null
  shiftStart: string | null
  shiftEnd: string | null
  manualChangeBy: string | null
  manualChangeAt: string | null
}
interface AlTrackerConflictResult {
  from: string; to: string; count: number; conflicts: AlTrackerConflict[]
}

function AlTrackerConflictBanner() {
  const { t } = useTranslation()
  const { session } = useAuth()
  const allowed = session != null && ["RTM", "TEAM_LEAD", "DEV"].includes(session.role)
  // Rolling window: today → +30 days is where an RTM actually edits the Shift Plan.
  const today = new Date()
  const from = today.toISOString().slice(0, 10)
  const to = new Date(today.getTime() + 30 * 86400000).toISOString().slice(0, 10)
  const { data } = useQuery({
    queryKey: ["al-tracker-conflicts", from, to],
    queryFn: () => apiFetch<AlTrackerConflictResult>(
      `/api/wic/al-tracker-conflicts?from=${from}&to=${to}`),
    staleTime: 5 * 60 * 1000,
    enabled: allowed,
    retry: false,
  })
  if (!allowed || !data || data.count === 0) return null

  return (
    <div className="bg-warn-bg border border-warn-bd rounded-[8px] px-4 py-3 text-[12px] text-warn-fg">
      <div className="flex items-center gap-2 font-bold mb-2">
        <AlertTriangle size={14} style={{ flexShrink: 0 }} />
        {t("overview.alTrackerConflict.title", { count: data.count })}
      </div>
      <div className="mb-2 text-[11px]">{t("overview.alTrackerConflict.hint")}</div>
      <div className="flex flex-col gap-[4px] font-mono text-[11px]">
        {data.conflicts.slice(0, 12).map(c => (
          <div key={`${c.date}-${c.employeeId}-${c.alTrackerEntryId}`}>
            {c.date} · {c.employeeId} · AL #{c.alTrackerEntryId} ({c.trackerFirstDay} → {c.trackerLastDay})
            {" · "}{t("overview.alTrackerConflict.shiftPlan")}: {c.shiftType}
            {c.shiftStart && c.shiftEnd ? ` ${c.shiftStart}–${c.shiftEnd}` : ""}
            {c.agentTask ? ` · ${c.agentTask}` : ""}
          </div>
        ))}
        {data.conflicts.length > 12 && (
          <div className="text-ink-soft">… {data.conflicts.length - 12} more</div>
        )}
      </div>
    </div>
  )
}

function MissingRosterBanner() {
  const { session } = useAuth()
  const allowed = session != null && ["RTM", "TEAM_LEAD", "DEV"].includes(session.role)
  const { data, error } = useQuery({
    queryKey: ["roster-missing"],
    queryFn: () => apiFetch<MissingRosterResult>("/api/roster/missing?days=14"),
    staleTime: 5 * 60 * 1000,
    enabled: allowed,
    retry: false,
  })
  if (error) console.warn("[Overview] missing-roster check failed:", error)
  if (!allowed || !data) return null
  if (data.missingRoster.length === 0 && data.orphanEntries.length === 0) return null

  return (
    <div style={{
      background: STATUS_TOKEN_BG["PARTIAL"], border: `1px solid rgb(var(--st-warn-bd))`,
      borderRadius: 8, padding: "12px 16px", fontSize: 12, color: STATUS_TOKEN_FG["PARTIAL"],
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, fontWeight: 700, marginBottom: 8 }}>
        <AlertTriangle size={14} style={{ flexShrink: 0 }} />
        Roster gaps ({data.from} → {data.to})
      </div>

      {data.missingRoster.length > 0 && (
        <div style={{ marginBottom: data.orphanEntries.length > 0 ? 10 : 0 }}>
          <div style={{ fontWeight: 600, marginBottom: 5 }}>
            {data.missingRoster.length} active employee(s) with no roster in the next {data.days} days
            — click a name to fix it in the Roster Generator:
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
            {data.missingRoster.map(a => (
              <Link key={a.employeeId}
                to={`/roster?employeeId=${encodeURIComponent(a.employeeId)}`}
                title={a.lastShiftDate ? `Last rostered: ${a.lastShiftDate}` : "Never rostered"}
                style={rosterPill}>
                {a.fullName ?? a.employeeId}{a.teamLeadName ? ` (${a.teamLeadName})` : ""}
              </Link>
            ))}
          </div>
        </div>
      )}

      {data.orphanEntries.length > 0 && (
        <div>
          <div style={{ fontWeight: 600, marginBottom: 5 }}>
            {data.orphanEntries.length} employee ID(s) have ShiftEntries but no active employee row:
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
            {data.orphanEntries.map(o => (
              <Link key={o.employeeId} to="/employees"
                title={o.hasInactiveEmployeeRow
                  ? "Employee row exists but is inactive — reactivate or clean up on the Employees page"
                  : "No Employees row at all — check the Employees page"}
                style={rosterPill}>
                {o.employeeId} · {o.rowCount} row(s) · {o.firstDate} → {o.lastDate}
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// ── WIC coverage-gaps warning + Coverage Risk list ───────────────────────────
// ONE source for both: GET /api/wic/coverage-gaps (see Backend/WicCoverageGapService.cs),
// RTM/TEAM_LEAD/DEV-only gate. A gap is a day an ACTIVE WIC location is OPEN
// (WicHoursResolver, manual exceptions included) and FEWER agents are present than that
// day's MinRequired (see the backend's WicPresenceResolver reading). The banner count and
// the Coverage Risk rows are therefore the same number by construction — the 2026-10-13
// defect was the list reading the forecast while the banner read this endpoint, so a
// location-level NOT_RECORDED summary silenced the banner while the list still showed
// the days as UNCOVERED.
// UNCOVERED_ABSENCE (critical): the location's own MAIN/BACKUP agent is on
// AL/SL/etc. that day. UNCOVERED_ASSIGNED (warning): nobody's absent, they're
// just not placed at this location. REGIONAL floaters never drive the category
// — they're shown as an "available" hint only, since one floater's leave
// shouldn't flag every location they float for.
interface WicGapAgent {
  employeeId: string | null; fullName: string; assignmentType: string; status: string; taskLabel: string
  // Consequence of the move (owner decision 2026-10-16). "Available" stays a HINT: nobody
  // is hidden and nothing is assigned automatically. These four say what taking this agent
  // out of their OWN centre would do to it — present−1 against that centre's MinRequired,
  // measured only over the time the gap and the source centre share. All null / false when
  // the move has no measurable consequence (BO, "rostered, no task", source centre closed
  // at the gap time), and the row then renders exactly as before.
  sourceLocation: string | null
  sourcePresentAfter: number | null
  sourceRequired: number | null
  sourceBelowMinimum: boolean
}
interface WicCoverageGapEntry {
  date: string; weekday: string; locationCode: string; displayName: string; city: string
  category: "UNCOVERED_ABSENCE" | "UNCOVERED_ASSIGNED"
  present: number; required: number
  openTime: string | null; closeTime: string | null
  openTime2: string | null; closeTime2: string | null
  assignedAgents: WicGapAgent[]; availableFloaters: WicGapAgent[]
}
interface WicCoverageSummaryEntry {
  locationCode: string; displayName: string; reason: "NOT_RECORDED" | "NO_ASSIGNMENT"; openDaysInWindow: number
}
interface WicCoverageGapResult {
  from: string; to: string
  gaps: WicCoverageGapEntry[]; notRecorded: WicCoverageSummaryEntry[]
  criticalCount: number; warningCount: number
}

// Same absence definition as Backend/WicCoverageGapService.cs (AbsenceTypes) plus
// RESIGNED: a coverage-gap row must not offer "Assign" for a centre whose only own
// agent has already left (the backend excludes them from the candidate list, so an
// offering button here would open an empty picker). "is there a sensible candidate"
// must agree with "why is this agent greyed out" or the button/link choice below
// would contradict the row's own data.
const GAP_ABSENCE_SET = new Set(["AL", "HALF_AL", "SL", "UL", "OL", "PH", "LPH", "OFF", "CD", "RESIGNED"])
const hasSensibleCandidate = (g: WicCoverageGapEntry) =>
  g.availableFloaters.length > 0 || g.assignedAgents.some(a => !GAP_ABSENCE_SET.has(a.status))

// A 90-day window can list many gap rows; both the banner and the Coverage Risk list
// below show the first few and offer the rest behind one click, so the page cannot be
// pushed into an unreadable scroll by the wide horizon.
const GAP_ROW_CAP = 8

function GapRow({ g, onAssign }: { g: WicCoverageGapEntry; onAssign: (g: WicCoverageGapEntry) => void }) {
  const { t } = useTranslation()
  const isCritical = g.category === "UNCOVERED_ABSENCE"
  const tone = isCritical ? "UNCOVERED" : "PARTIAL"
  const hours = g.openTime && g.closeTime
    ? `${g.openTime}–${g.closeTime}${g.openTime2 ? `, ${g.openTime2}–${g.closeTime2}` : ""}`
    : "—"
  const canAssign = hasSensibleCandidate(g)

  return (
    <div
      style={{
        display: "flex", alignItems: "center", gap: 12, padding: "8px 14px",
        fontSize: 12, borderLeft: `3px solid ${STATUS_TOKEN_FG[tone]}`,
        background: "rgb(var(--surface-raised))",
      }}
      className="border-b border-line-subtle"
    >
      <span style={{
        fontSize: 9, fontWeight: 700, fontFamily: "monospace", whiteSpace: "nowrap",
        color: STATUS_TOKEN_FG[tone], background: STATUS_TOKEN_BG[tone],
        padding: "2px 6px", borderRadius: 4, minWidth: 64, textAlign: "center",
      }}>
        {isCritical ? t("overview.coverageBanner.absence") : t("overview.coverageBanner.unstaffed")}
      </span>
      <Link to={`/wic-shifts?date=${g.date}`} style={{ fontFamily: "monospace", minWidth: 96, color: "rgb(var(--text-secondary))", textDecoration: "none" }}>
        {g.weekday.slice(0, 3)} {g.date}
      </Link>
      <Link to={`/wic-shifts?date=${g.date}`} className="font-semibold text-ink" style={{ minWidth: 140, textDecoration: "none" }}>
        {g.displayName}
      </Link>
      <span style={{ fontFamily: "monospace", color: "rgb(var(--text-tertiary))", minWidth: 74 }}>{g.present} / {g.required}</span>
      <span style={{ fontFamily: "monospace", color: "rgb(var(--text-tertiary))", minWidth: 96 }}>{hours}</span>
      <span style={{ flex: 1, color: isCritical ? STATUS_TOKEN_FG[tone] : "rgb(var(--text-secondary))" }}>
        {g.assignedAgents.map((a, i) => (
          <span key={a.employeeId ?? a.fullName + i}>
            {i > 0 && ", "}
            {a.fullName} <span className="text-ink-soft" style={{ fontSize: 10 }}>({a.assignmentType})</span>: {a.taskLabel}
          </span>
        ))}
      </span>
      {g.availableFloaters.length > 0 && (
        <span style={{ color: STATUS_TOKEN_FG["COVERED"], fontSize: 11, whiteSpace: "nowrap" }}>
          {t("overview.coverageBanner.availableLabel")}{" "}
          {g.availableFloaters.map((a, i) => (
            <span key={a.employeeId ?? a.fullName + i}>
              {i > 0 && ", "}
              {/* The move's CONSEQUENCE, only when it drops the agent's own centre below
                  its minimum: "WIC Helmstedt → 0/1", in the row's own tone (red for a
                  critical row, orange for an unstaffed one). Otherwise the floater reads
                  exactly as before. */}
              <span
                title={a.sourceBelowMinimum && a.sourceLocation
                  ? t("overview.coverageBanner.availableWarningHint", { centre: a.sourceLocation })
                  : undefined}
                style={a.sourceBelowMinimum
                  ? { color: STATUS_TOKEN_FG[tone], fontWeight: 700 }
                  : undefined}
              >
                {a.fullName} ({a.taskLabel}
                {a.sourceBelowMinimum && a.sourcePresentAfter != null ? ` → ${a.sourcePresentAfter}/${a.sourceRequired}` : ""})
              </span>
            </span>
          ))}
        </span>
      )}
      {canAssign ? (
        <button
          onClick={() => onAssign(g)}
          style={{
            background: STATUS_TOKEN_FG["COVERED"], border: "none", color: "white",
            borderRadius: 5, padding: "4px 12px", fontSize: 11, fontWeight: 600,
            cursor: "pointer", whiteSpace: "nowrap", flexShrink: 0,
          }}
        >
          {t("overview.coverageBanner.assign")}
        </button>
      ) : (
        <Link
          to={`/wic-coverage?location=${encodeURIComponent(g.locationCode)}`}
          style={{
            fontSize: 11, fontWeight: 600, whiteSpace: "nowrap", flexShrink: 0,
            color: "rgb(var(--text-secondary))", textDecoration: "underline",
          }}
        >
          {t("overview.coverageBanner.fixAssignment")}
        </Link>
      )}
    </div>
  )
}

// Presentational banner. The query itself is hoisted into Overview so the banner and
// the Coverage Risk list read the SAME /api/wic/coverage-gaps response for the same
// window — one source, one number. `state` comes from coverageBannerState(), which is
// what keeps a 401/error from ever rendering as a green "no gaps".
function WicCoverageGapsBanner({
  horizon, state, data, onAssigned, onRefresh, onHorizonChange,
}: {
  horizon: number
  state: WicCoverageBannerState
  data: WicCoverageGapResult | undefined
  onAssigned: () => void
  onRefresh: () => void
  onHorizonChange: (horizon: number) => void
}) {
  const { t } = useTranslation()
  const [notRecordedOpen, setNotRecordedOpen] = useState(false)
  const [gapsExpanded, setGapsExpanded] = useState(false)
  const [assignTarget, setAssignTarget] = useState<{ locationCode: string; displayName: string; date: string } | null>(null)

  if (state === "disabled") return null

  const gaps = data?.gaps ?? []
  const notRecorded = data?.notRecorded ?? []
  // Neutral (CLOSED) tone for loading/error: an all-clear colour must be backed by a
  // successful response, never by a failure.
  const tone = state === "gaps" ? "UNCOVERED" : state === "clear" ? "COVERED" : "CLOSED"

  return (
    <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
      <div style={{
        display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8,
        flexWrap: "wrap",
        padding: "10px 14px", fontSize: 12, fontWeight: 700,
        background: STATUS_TOKEN_BG[tone], color: STATUS_TOKEN_FG[tone],
      }}>
        <span style={{ display: "flex", alignItems: "center", gap: 8 }}>
          {state === "gaps" && (
            <><AlertTriangle size={14} style={{ flexShrink: 0 }} />
              {t("overview.coverageBanner.gaps", {
                count: gaps.length, from: data?.from, to: data?.to,
                critical: data?.criticalCount, warning: data?.warningCount,
              })}</>
          )}
          {state === "clear" && t("overview.coverageBanner.clear", { n: horizon })}
          {state === "loading" && <span className="skeleton" style={{ height: 16, width: 240, borderRadius: 4 }} />}
          {state === "error" && (
            <><AlertTriangle size={14} style={{ flexShrink: 0 }} />
              {t("overview.coverageBanner.couldNotLoad")}</>
          )}
        </span>
        {state === "error" && (
          <button
            onClick={onRefresh}
            style={{
              background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--border-subtle))",
              color: STATUS_TOKEN_FG[tone], borderRadius: 6, padding: "3px 10px",
              fontSize: 11, fontWeight: 600, cursor: "pointer", whiteSpace: "nowrap",
            }}
          >
            {t("overview.coverageBanner.retry")}
          </button>
        )}
        {/* Horizon pair: this button does not own a horizon of its own — it moves the
            same ?horizon= the topbar writes, so banner, day strip and list always
            answer for ONE window. It stays available in every non-disabled state
            (loading/clear/gaps/error) so the 90-day view is reachable and reversible
            without a reload. */}
        <button
          onClick={() => onHorizonChange(nextHorizon(horizon))}
          style={{
            background: "rgb(var(--surface-raised))",
            border: `1px solid ${state === "clear" ? "rgb(var(--st-good-bd))" : "rgb(var(--border-subtle))"}`,
            color: STATUS_TOKEN_FG[tone], borderRadius: 6, padding: "3px 10px",
            fontSize: 11, fontWeight: 600, cursor: "pointer", whiteSpace: "nowrap",
          }}
        >
          {horizon >= MAX_HORIZON
            ? t("overview.coverageBanner.view14")
            : t("overview.coverageBanner.view90")}
        </button>
      </div>

      {state === "gaps" && (
        <div>
          {(gapsExpanded ? gaps : gaps.slice(0, GAP_ROW_CAP)).map(g => (
            <GapRow key={`${g.locationCode}-${g.date}`} g={g}
              onAssign={gap => setAssignTarget({ locationCode: gap.locationCode, displayName: gap.displayName, date: gap.date })} />
          ))}
          {gaps.length > GAP_ROW_CAP && (
            <button
              onClick={() => setGapsExpanded(v => !v)}
              className="border-t border-line-subtle"
              style={{
                display: "block", width: "100%", background: "none", border: "none",
                padding: "7px 14px", fontSize: 11, cursor: "pointer", textAlign: "left",
                color: "rgb(var(--text-secondary))",
              }}
            >
              {gapsExpanded
                ? t("overview.exceptionList.collapse")
                : t("overview.exceptionList.showAll", { n: gaps.length })}
            </button>
          )}
        </div>
      )}

      {state !== "error" && notRecorded.length > 0 && (
        <div className="border-t border-line-subtle">
          <button
            onClick={() => setNotRecordedOpen(v => !v)}
            style={{
              display: "flex", alignItems: "center", gap: 6, width: "100%",
              background: "none", border: "none", padding: "7px 14px", fontSize: 11,
              cursor: "pointer", textAlign: "left", color: "rgb(var(--text-secondary))",
            }}
          >
            {notRecordedOpen ? "▲" : "▼"} {t("overview.coverageBanner.recordKeeping", { count: notRecorded.length })}
          </button>
          {notRecordedOpen && (
            <div style={{ padding: "0 14px 10px", display: "flex", flexWrap: "wrap", gap: 6 }}>
              {notRecorded.map(n => (
                <span key={n.locationCode}
                  title={n.reason === "NO_ASSIGNMENT"
                    ? t("overview.coverageBanner.recordKeepingNoAssignmentHint")
                    : t("overview.coverageBanner.recordKeepingNotRecordedHint")}
                  style={{
                    fontSize: 11, fontFamily: "monospace", padding: "3px 10px", borderRadius: 999,
                    background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--border-subtle))",
                    color: "rgb(var(--text-secondary))",
                  }}>
                  {n.reason === "NO_ASSIGNMENT"
                    ? t("overview.coverageBanner.recordKeepingNoAssignment", { name: n.displayName })
                    : t("overview.coverageBanner.recordKeepingOpenDays", { name: n.displayName, count: n.openDaysInWindow })}
                </span>
              ))}
            </div>
          )}
        </div>
      )}

      <NewShiftModal
        isOpen={assignTarget != null}
        onClose={() => setAssignTarget(null)}
        prefill={assignTarget}
        onSuccess={onAssigned}
      />
    </div>
  )
}

const cartoKey = import.meta.env.VITE_CARTO_KEY
let cartoKeyWarned = false

function ThemedTileLayer() {
  const { resolvedTheme } = useTheme()
  const isDark = resolvedTheme === "dark"

  useEffect(() => {
    if (!cartoKey && !cartoKeyWarned) {
      cartoKeyWarned = true
      console.warn("VITE_CARTO_KEY is not set — CARTO basemap tiles will show a watermark.")
    }
  }, [])

  return (
    <TileLayer
      key={isDark ? "dark" : "light"}
      url={`https://{s}.basemaps.cartocdn.com/${isDark ? "dark_all" : "light_all"}/{z}/{x}/{y}{r}.png?key=${cartoKey}`}
      subdomains="abcd"
      maxZoom={19}
      attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>'
    />
  )
}

function BoundsLock({ points }: { points: [number, number][] }) {
  const map = useMap()

  useEffect(() => {
    if (points.length === 0) { map.setView([51.1657, 10.4515], 6); return }
    const lats = points.map(p => p[0])
    const lngs = points.map(p => p[1])
    const sw: [number, number] = [Math.min(...lats), Math.min(...lngs)]
    const ne: [number, number] = [Math.max(...lats), Math.max(...lngs)]
    const latPad = Math.max(0.5, (ne[0] - sw[0]) * 0.35)
    const lngPad = Math.max(0.5, (ne[1] - sw[1]) * 0.35)
    const padded: [[number, number], [number, number]] = [
      [sw[0] - latPad, sw[1] - lngPad],
      [ne[0] + latPad, ne[1] + lngPad],
    ]
    map.fitBounds([sw, ne], { padding: [24, 24], maxZoom: 8 })
    map.setMaxBounds(padded)
    map.setMinZoom(Math.max(4, map.getBoundsZoom(padded) - 1))
    setTimeout(() => map.invalidateSize(), 100)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, JSON.stringify(points)])

  // Re-invalidate + re-fit when the container is resized (sidebar collapse, breakpoint change).
  // window.resize alone misses sidebar toggle — ResizeObserver catches it.
  useEffect(() => {
    const el = map.getContainer()
    let t: number
    const ro = new ResizeObserver(() => {
      clearTimeout(t)
      t = window.setTimeout(() => {
        map.invalidateSize()
        if (points.length) {
          const lats = points.map(p => p[0])
          const lngs = points.map(p => p[1])
          const sw: [number, number] = [Math.min(...lats), Math.min(...lngs)]
          const ne: [number, number] = [Math.max(...lats), Math.max(...lngs)]
          map.fitBounds([sw, ne], { padding: [24, 24], maxZoom: 8 })
        }
      }, 100)
    })
    ro.observe(el)
    return () => { ro.disconnect(); clearTimeout(t) }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [map, JSON.stringify(points)])

  return null
}

function MapResizer({ points }: { points: [number,number][] }) {
  const map = useMap()
  useEffect(() => {
    const el = map.getContainer()
    let t: number
    const ro = new ResizeObserver(() => {
      clearTimeout(t)
      t = window.setTimeout(() => {
        map.invalidateSize()
        if (points.length) map.fitBounds(L.latLngBounds(points), { padding: [24, 24], maxZoom: 8 })
      }, 100)
    })
    ro.observe(el)
    return () => { ro.disconnect(); clearTimeout(t) }
  }, [map, points])
  return null
}

interface WicMapProps {
  locations: WicLocation[]
  forecast: LocationForecast[]
  onPinClick: (locationCode: string) => void
}
function WicMapView({ locations, forecast, onPinClick }: WicMapProps) {
  const { t } = useTranslation()
  // Subscribe to theme so CircleMarker pathOptions re-read CSS custom props when theme changes.
  // statusColor() calls getComputedStyle at render time — stale only if WicMapView doesn't re-render.
  const { resolvedTheme } = useTheme()
  const today = new Date().toISOString().split("T")[0]
  const statusMap   = new Map(forecast.map(lf => [lf.locationCode, lf.todayStatus]))
  const forecastMap = new Map(forecast.map(lf => [lf.locationCode, lf]))
  const withCoords  = locations.filter(l => parseCoords(l.coordinates))
  const mapPoints   = withCoords.map(l => parseCoords(l.coordinates)!)

  if (withCoords.length === 0) {
    return (
      <div className="w-full h-full flex items-center justify-center bg-raised border border-line-subtle rounded-md p-4">
        <WarningBanner msg={t("overview.map.noCoordinates")} />
      </div>
    )
  }

  return (
    <div className="w-full h-full" style={{ overflow: "hidden", isolation: "isolate" }}>
      <MapContainer
        key={resolvedTheme}
        center={[51.1657, 10.4515]} zoom={6} minZoom={4} maxZoom={12}
        maxBoundsViscosity={1.0}
        style={{ height: "100%", width: "100%", borderRadius: 0 }}
        scrollWheelZoom={false}
      >
        <BoundsLock points={mapPoints} />
        <MapResizer points={mapPoints} />
        <ThemedTileLayer />
        {withCoords.map(loc => {
          const coords  = parseCoords(loc.coordinates)!
          const status  = statusMap.get(loc.locationCode) ?? "CLOSED"
          const color   = statusColor(status)
          const isAtRisk = status === "UNCOVERED" || status === "PARTIAL"
          const lf      = forecastMap.get(loc.locationCode)
          const todayDF = lf?.forecast?.find(df => df.date === today)
          return (
            <CircleMarker
              key={loc.locationCode} center={coords} radius={isAtRisk ? 9 : 7}
              pathOptions={{ color, fillColor: color, fillOpacity: 0.85, weight: isAtRisk ? 2.5 : 1.5 }}
            >
              <Popup>
                <div style={{ fontSize: 12, minWidth: 170 }}>
                  <div style={{ fontWeight: 700, marginBottom: 2 }}>{loc.displayName}</div>
                  <div className="text-ink-soft" style={{ fontSize: 11, marginBottom: 8 }}>{loc.city}, {loc.country}</div>
                  <span style={{
                    background: STATUS_TOKEN_BG[status], color: STATUS_TOKEN_FG[status],
                    padding: "2px 8px", borderRadius: 4, fontSize: 11, fontWeight: 600,
                  }}>{status}</span>
                  {todayDF != null && (
                    <div className="text-ink-soft" style={{ marginTop: 8, fontSize: 11 }}>
                      <span style={{ fontFamily: "monospace" }}>{todayDF.effectiveCoverage} / {todayDF.minRequired}</span>
                      {" agents today"}
                    </div>
                  )}
                  {isAtRisk && (
                    <button
                      onClick={() => onPinClick(loc.locationCode)}
                      style={{
                        marginTop: 10, width: "100%",
                        background: STATUS_TOKEN_BG["UNCOVERED"],
                        border: `1px solid ${STATUS_TOKEN_FG["UNCOVERED"]}`,
                        color: STATUS_TOKEN_FG["UNCOVERED"], borderRadius: 4,
                        padding: "5px 0", fontSize: 11, cursor: "pointer",
                      }}
                    >
                      {t("attendance.substitute.find")}
                    </button>
                  )}
                </div>
              </Popup>
            </CircleMarker>
          )
        })}
      </MapContainer>
    </div>
  )
}

function SubstituteDrawer({ locationCode, date, displayName }: { locationCode: string; date: string; displayName: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [acceptedId, setAcceptedId]     = useState<string | null>(null)
  const [acceptedName, setAcceptedName] = useState<string | null>(null)
  const [accepting, setAccepting]       = useState<string | null>(null)
  const [acceptError, setAcceptError]   = useState<string | null>(null)

  const { data: subs, isLoading } = useQuery({
    queryKey: ["subs-drawer", locationCode, date],
    queryFn: async () => {
      const r = await apiFetch<{ days: Array<{ date: string; candidates: any[] }> }>(
        `/api/wic/substitutes?locationCode=${encodeURIComponent(locationCode)}&date=${date}&horizon=1`
      )
      return r.days?.[0]?.candidates ?? []
    },
    staleTime: 2 * 60 * 1000,
  })

  const handleAccept = async (s: any) => {
    setAccepting(s.employeeId)
    setAcceptError(null)
    try {
      await postSubstituteAccept({
        employeeId: s.employeeId,
        locationCode,
        date,
        shiftStart: null,
        shiftEnd: null,
        sourceType: s.sourceType,
      })
      setAcceptedId(s.employeeId)
      setAcceptedName(s.fullName ?? s.name)
      queryClient.invalidateQueries({ queryKey: ["wic-forecast"] })
      queryClient.invalidateQueries({ queryKey: ["subs-drawer", locationCode, date] })
    } catch (err) {
      console.error("Accept substitute failed:", err)
      // Show the server's exact reason (localized where the code is known). Never
      // replace it with a generic sentence — that is what hid the real cause on
      // 2026-10-12.
      setAcceptError(substituteAcceptErrorText(err, t))
    } finally {
      setAccepting(null)
    }
  }

  const sourceColor = (src: string) => {
    if (src === "BACKUP")    return { bg: "rgb(var(--st-learn-bg))", color: "rgb(var(--st-learn-solid))" }
    if (src === "SSP")       return { bg: STATUS_TOKEN_BG["COVERED"],  color: STATUS_TOKEN_FG["COVERED"] }
    if (src === "WIC_DONOR") return { bg: "rgb(var(--st-wic-bg))",   color: "rgb(var(--st-wic-solid))" }
    return { bg: STATUS_TOKEN_BG["PARTIAL"], color: STATUS_TOKEN_FG["PARTIAL"] }
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div>
        <div className="text-[13px] font-semibold text-ink">{displayName}</div>
        <div className="text-[11px] text-ink-soft" style={{ fontFamily: "monospace" }}>{date}</div>
      </div>
      {acceptedId && (
        <div style={{
          background: STATUS_TOKEN_BG["COVERED"], border: `1px solid ${STATUS_TOKEN_FG["COVERED"]}`,
          borderRadius: 8, padding: "10px 14px", fontSize: 12, color: STATUS_TOKEN_FG["COVERED"],
        }}>
          {t("attendance.substitute.confirmed", { name: acceptedName, wic: displayName })}
        </div>
      )}
      {acceptError && (
        <div style={{
          background: "rgb(var(--st-crit-bg))", border: "1px solid rgb(var(--st-crit-solid))",
          borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "rgb(var(--st-crit-solid))",
          display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8,
        }}>
          <span>{acceptError}</span>
          <button onClick={() => setAcceptError(null)} style={{ background: "none", border: "none", cursor: "pointer", color: "inherit", fontSize: 14, lineHeight: 1 }}>✕</button>
        </div>
      )}
      {isLoading ? (
        <>{[1, 2, 3].map(i => <div key={i} className="skeleton" style={{ height: 60, borderRadius: 8 }} />)}</>
      ) : (subs ?? []).length === 0 ? (
        <div className="text-ink-soft text-xs" style={{ padding: 20, textAlign: "center" }}>
          {t("attendance.substitute.noCandidates")}
        </div>
      ) : (subs ?? []).map((s: any, i: number) => {
        const sc = sourceColor(s.sourceType)
        const isAccepted = acceptedId === s.employeeId
        return (
          <div key={i} style={{
            background: isAccepted ? STATUS_TOKEN_BG["COVERED"] : "rgb(var(--surface-sunken))",
            border: `1px solid ${isAccepted ? STATUS_TOKEN_FG["COVERED"] : "rgb(var(--border-subtle))"}`,
            borderRadius: 8, padding: "12px 14px",
          }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
              <div className="font-semibold text-xs text-ink">{s.name ?? s.fullName}</div>
              <span style={{ ...sc, padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "monospace", fontWeight: 600 }}>
                {s.sourceType}
              </span>
            </div>
            <div className="text-ink-soft" style={{ display: "flex", gap: 12, marginTop: 6, fontSize: 11 }}>
              {s.distanceKm != null && <span>{t("attendance.substitute.distance", { km: s.distanceKm.toFixed(1) })}</span>}
              {s.reachabilityTier && <span>Tier {s.reachabilityTier}</span>}
              {s.score != null && <span className="text-ink-muted" style={{ fontFamily: "monospace" }}>Score: {s.score.toFixed(0)}</span>}
            </div>
            {!acceptedId && (
              <div style={{ marginTop: 10, display: "flex", justifyContent: "flex-end" }}>
                <button
                  onClick={() => handleAccept(s)}
                  disabled={accepting === s.employeeId}
                  style={{
                    background: STATUS_TOKEN_FG["COVERED"], border: "none", color: "white",
                    borderRadius: 5, padding: "5px 14px", fontSize: 11, fontWeight: 600,
                    cursor: accepting === s.employeeId ? "not-allowed" : "pointer",
                    opacity: accepting === s.employeeId ? 0.6 : 1,
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
  )
}

function TLCard({ tl }: { tl: any }) {
  const { t } = useTranslation()
  const nullData = tl.totalAgents == null

  const chips: Array<{ key: string; label: string; value: number; cls: string }> = [
    { key: "WORKING",  label: "Working",  value: tl.working     ?? 0, cls: "bg-good-bg text-good-fg" },
    { key: "WIC_DUTY", label: "WIC",      value: tl.wicAssigned ?? 0, cls: "bg-wic-bg text-wic-fg" },
    { key: "AL",       label: "AL",       value: tl.onAL        ?? 0, cls: "bg-info-bg text-info-fg" },
    { key: "SL",       label: "SL",       value: tl.onSL        ?? 0, cls: "bg-warn-bg text-warn-fg" },
    { key: "TRAINING", label: "Training", value: tl.training    ?? 0, cls: "bg-learn-bg text-learn-fg" },
  ]

  const allZero      = !nullData && chips.every(c => c.value === 0)
  const activeAgents = (tl.working ?? 0) + (tl.wicAssigned ?? 0)

  return (
    <div className="bg-raised border border-line-subtle rounded-md" style={{ padding: "12px 14px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 8 }}>
        <div className="font-semibold text-xs text-ink" style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", flex: 1, minWidth: 0 }}>{tl.teamLeadName}</div>
        {!nullData && (
          <span className="text-xs text-ink-soft font-mono ml-2" style={{ flexShrink: 0 }}>{activeAgents} / {tl.totalAgents}</span>
        )}
      </div>
      {nullData ? (
        <div className="text-xs text-ink-muted">{t("overview.tlSummary.notAvailable")}</div>
      ) : allZero ? (
        <div className="text-xs text-ink-soft">{t("overview.tlSummary.allOff")}</div>
      ) : (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 4 }}>
          {chips.filter(c => c.value !== 0).map(c => (
            <span key={c.key} className={`font-mono font-semibold ${c.cls}`} style={{
              padding: "2px 7px", borderRadius: 4, fontSize: 10,
            }}>{c.label} {c.value}</span>
          ))}
        </div>
      )}
    </div>
  )
}

function WicCard({ card }: { card: WicCardDto }) {
  const st = card.coverageStatus

  // Absence markers sent by /api/wic/cards (WicCardsService): the shift fields carry the
  // absence type instead of times. Agents with a marker render as absent, never as covering.
  const ABSENCE_LABELS: Record<string, string> = {
    SICK: "SL", SL: "SL", AL: "AL", HALF_AL: "½AL", UL: "UL", OL: "OL",
    PH: "PH", LPH: "PH", OFF: "OFF", OFF_WEEKEND: "OFF", RESIGNED: "RESIGNED", GSD: "GSD",
  }
  const getAgentReason = (a: WicCardAssignedAgent): string | null =>
    ABSENCE_LABELS[a.shiftStart ?? ""] ?? ABSENCE_LABELS[a.shiftEnd ?? ""] ?? null

  const activeAgents   = card.assignedAgents?.filter(a => getAgentReason(a) === null) ?? []
  const inactiveAgents = card.assignedAgents?.filter(a => getAgentReason(a) !== null) ?? []

  return (
    <div className="bg-raised border border-line-subtle rounded-md" style={{ padding: "12px 14px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 6 }}>
        <div>
          <div className="font-semibold text-[13px] text-ink">{card.displayName}</div>
          <div className="text-[11px] text-ink-soft mt-0.5">{card.city}</div>
        </div>
        <span style={{
          fontSize: 10, fontWeight: 600, fontFamily: "monospace", whiteSpace: "nowrap",
          color: STATUS_TOKEN_FG[st] ?? "rgb(var(--text-tertiary))",
          background: STATUS_TOKEN_BG[st] ?? "transparent",
          padding: "2px 8px", borderRadius: 4,
        }}>{st}</span>
      </div>
      {(activeAgents.length > 0 || inactiveAgents.length > 0) && (
        <div className="border-t border-line-subtle" style={{ paddingTop: 6, marginTop: 4 }}>
          {activeAgents.map((a) => (
            <div key={a.employeeId} className="text-ink-muted" style={{ display: "flex", justifyContent: "space-between", fontSize: 11, marginBottom: 2 }}>
              <span>{a.name}</span>
              <span style={{ fontFamily: "monospace", color: a.coverageMatch === "FULL" ? STATUS_TOKEN_FG["COVERED"] : STATUS_TOKEN_FG["PARTIAL"] }}>
                {`${a.shiftStart}–${a.shiftEnd}`}
              </span>
            </div>
          ))}
          {inactiveAgents.map((a) => {
            const reason = getAgentReason(a)!
            return (
              <div key={a.employeeId} style={{ display: "flex", justifyContent: "space-between", fontSize: 11, marginBottom: 2, opacity: 0.45 }}>
                <span className="text-ink-soft">{a.name}</span>
                <span style={{ fontFamily: "monospace", fontSize: 10, color: "rgb(var(--text-tertiary))" }}>{reason}</span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function SectionHeader({ title, count, color }: { title: string; count?: number; color?: string }) {
  const c = color ?? "rgb(var(--text-secondary))"
  return (
    <div style={{ fontSize: 11, fontWeight: 600, color: c, textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 10, display: "flex", alignItems: "center", gap: 8 }}>
      {title}
      {count !== undefined && (
        <span style={{ fontSize: 10, background: `${c}22`, color: c, padding: "1px 7px", borderRadius: 10, fontFamily: "monospace" }}>{count}</span>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function Overview() {
  const { t } = useTranslation()
  const { session } = useAuth()
  const queryClient = useQueryClient()
  const [searchParams, setSearchParams] = useSearchParams()
  // byte-identical to original line 400, except the ceiling: 90 is a first-class
  // horizon (topbar "90d" and the banner's "View 90 days" both set it). The old
  // Math.min(30, …) silently collapsed the restored 90-day view back to 30.
  const horizon = parseHorizon(searchParams.get("horizon"))
  const today = new Date().toISOString().split("T")[0]
  // The ONLY WIC coverage-gap source on this page: the banner and the Coverage Risk
  // list below both read this one response, so their numbers cannot diverge.
  const coverageAllowed = session != null && ["RTM", "TEAM_LEAD", "DEV"].includes(session.role)

  const [sheetOpen, setSheetOpen]   = useState(false)
  const [sheetCell, setSheetCell]   = useState<{ locationCode: string; date: string; displayName: string } | null>(null)
  const [showClosed, setShowClosed] = useState(false)
  const [selectedDay, setSelectedDay] = useState<string | null>(null)
  // Banner button -> same URL parameter the topbar writes, so banner, day strip and
  // exception list re-read ONE coverage-gaps window. The day filter is dropped: a day
  // selected in the 14-day strip may not exist in the 90-day window.
  const setCoverageHorizon = (next: number) => {
    const params = new URLSearchParams(searchParams)
    params.set("horizon", String(next))
    setSelectedDay(null)
    setSearchParams(params, { replace: true })
  }
  // localStorage-persisted (VI) — was useState(false)
  const [conflictsOpen, setConflictsOpen] = useState(false)
  const [showDetailGrid, setShowDetailGrid] = useState<boolean>(() => {
    try { return localStorage.getItem("gsd.overview.detail") === "true" } catch { return false }
  })
  const toggleDetailGrid = () => setShowDetailGrid(prev => {
    const next = !prev
    try { localStorage.setItem("gsd.overview.detail", String(next)) } catch {}
    return next
  })

  // ── Queries ──────────────────────────────────────────────────────────────────
  const { data: forecast, isLoading: forecastLoading } = useQuery({
    queryKey: ["wic-forecast", horizon],
    queryFn: async () => {
      const r = await apiFetch<{ locations: LocationForecast[] }>(`/api/wic/forecast?horizon=${horizon}`)
      return r.locations ?? []
    },
    staleTime: 5 * 60 * 1000, refetchInterval: 10 * 60 * 1000,
  })

  const { data: briefing, isLoading: briefingLoading } = useQuery({
    queryKey: ["briefing"],
    queryFn: () => apiFetch<Briefing>("/api/wic/briefing"),
    staleTime: 3 * 60 * 1000,
  })

  const { data: locations } = useQuery({
    queryKey: ["wic-locations"],
    queryFn: () => apiFetch<WicLocation[]>("/api/wic/locations"),
    staleTime: 15 * 60 * 1000,
  })

  const { data: tls } = useQuery({
    queryKey: ["tls", today],
    queryFn: () => api.dashboard.teamleadSummary(today),
    staleTime: 5 * 60 * 1000,
  })

  const { data: wicCards, isLoading: cardsLoading } = useQuery({
    queryKey: ["wic-cards-v2", today],
    queryFn: () => apiFetch<WicCardDto[]>(`/api/wic/cards?date=${today}`),
    staleTime: 3 * 60 * 1000,
    enabled: showDetailGrid,
  })

  const conflictTo = (() => { const d = new Date(today); d.setDate(d.getDate() + horizon - 1); return d.toISOString().split("T")[0] })()
  const { data: conflicts } = useQuery({
    queryKey: ["wic-conflicts", today, horizon],
    queryFn: () => apiFetch<WicConflict[]>(`/api/wic/conflicts?from=${today}&to=${conflictTo}`),
    staleTime: 10 * 60 * 1000,
  })

  // RTM / TEAM_LEAD / DEV only — same gate as the server (RosterAccess → 401/403).
  // The AGENT role never reaches the Overview page, so this is the page's only viewer set.
  const {
    data: coverage,
    isLoading: coverageLoading,
    isError: coverageError,
    refetch: refetchCoverage,
  } = useQuery({
    queryKey: ["wic-coverage-gaps", horizon],
    queryFn: () => apiFetch<WicCoverageGapResult>(`/api/wic/coverage-gaps?days=${horizon}`),
    staleTime: 5 * 60 * 1000,
    enabled: coverageAllowed,
    retry: false,
  })
  if (coverageError) console.warn("[Overview] wic-coverage-gaps check failed — showing neutral state, never a green banner:", coverageError)
  const coverageState = coverageBannerState({
    enabled: coverageAllowed,
    isLoading: coverageLoading,
    isError: coverageError,
    hasData: coverage != null,
    gapCount: coverage?.gaps.length ?? 0,
  })
  const coverageGaps = coverage?.gaps ?? []

  // ── KPI derivations ───────────────────────────────────────────────────────────
  // null = data not yet loaded (render "—" + "Not available"); 0 = loaded, genuinely zero.
  // All five KPI cards describe TODAY only — they read lf.todayStatus, not the
  // horizon the Coverage Risk strip and the coverage-gaps banner below cover. A
  // location closed today (e.g. a Tuesday-only centre) is excluded from totalOpen,
  // so "Coverage % Today" can read 100% while the banner lists a gap later in the
  // window. Both are correct; the labels have to say "Today" or they look like a
  // contradiction. Do not widen these to the horizon without relabelling them.
  const totalOpen    = forecast != null ? forecast.filter(lf => lf.todayStatus !== "CLOSED").length    : null
  const atRiskToday  = forecast != null ? forecast.filter(lf => lf.todayStatus === "UNCOVERED" || lf.todayStatus === "PARTIAL").length : null
  const coveredToday = forecast != null ? forecast.filter(lf => lf.todayStatus === "COVERED").length   : null
  const coveragePct  = totalOpen != null
    ? (totalOpen > 0 ? `${(coveredToday! / totalOpen * 100).toFixed(1)}%` : "—")
    : null
  const absentToday  = briefing != null ? briefing.totalAbsences : null
  const closureRisk  = briefing?.nextAtRiskDays?.length ?? null

  // ── nextAtRiskDays cross-check (Condition 4 / answer III) ────────────────────
  // Logs to console if briefing.nextAtRiskDays and forecast disagree on future at-risk days.
  // Do not reconcile — report discrepancy.
  useEffect(() => {
    if (!forecast || !briefing?.nextAtRiskDays?.length) return
    const forecastAtRisk = new Map<string, Set<string>>()
    for (const lf of forecast) {
      for (const df of lf.forecast ?? []) {
        if (df.date <= today || (df.status !== "UNCOVERED" && df.status !== "PARTIAL")) continue
        if (!forecastAtRisk.has(df.date)) forecastAtRisk.set(df.date, new Set())
        forecastAtRisk.get(df.date)!.add(lf.locationCode)
      }
    }
    for (const nard of briefing.nextAtRiskDays) {
      if (!forecastAtRisk.get(nard.date)?.has(nard.locationCode)) {
        console.warn("[Overview] nextAtRiskDays/forecast discrepancy — briefing reports at-risk but forecast does not:", nard)
      }
    }
  }, [forecast, briefing, today])

  // ── Heatmap dates (unchanged) ─────────────────────────────────────────────────
  const heatDates = Array.from({ length: horizon }, (_, i) => {
    const d = new Date(today); d.setDate(d.getDate() + i); return d.toISOString().split("T")[0]
  })

  const dayLabel = (d: string) => {
    const dt = new Date(d)
    const dow = ["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]
    return { dow, day: dt.getDate().toString().padStart(2, "0") }
  }
  const isWeekend = (d: string) => { const dt = new Date(d); return dt.getDay() === 0 || dt.getDay() === 6 }

  // ── Detail grid status groups ─────────────────────────────────────────────────
  const uncovered = wicCards?.filter(c => c.coverageStatus === "UNCOVERED") ?? []
  const partial   = wicCards?.filter(c => c.coverageStatus === "PARTIAL") ?? []
  const covered   = wicCards?.filter(c => c.coverageStatus === "COVERED") ?? []
  const closed    = wicCards?.filter(c => c.coverageStatus === "CLOSED") ?? []
  // When every open-category is empty the toggle button is misleading — show closed directly.
  const allClosed = !cardsLoading && wicCards != null && uncovered.length === 0 && partial.length === 0 && covered.length === 0 && closed.length > 0

  // Opens substitute drawer — used by heatmap grid cells (§1.3, preserved verbatim)
  const openSub = (lf: LocationForecast, date: string) => {
    setSheetCell({ locationCode: lf.locationCode, date, displayName: lf.displayName })
    setSheetOpen(true)
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>

      {/* ── KPI Row ── */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 12 }}>
        <KpiCard label={t("overview.kpi.openToday")}   value={totalOpen}    color="rgb(var(--st-info-solid))"    isLoading={forecastLoading} />
        <KpiCard label={t("overview.kpi.atRiskToday")} value={atRiskToday}  color="rgb(var(--st-warn-solid))"    isLoading={forecastLoading} />
        <KpiCard label={t("overview.kpi.closureRisk")} value={closureRisk}  color="rgb(var(--st-crit-solid))"    isLoading={briefingLoading} />
        <KpiCard label={t("overview.kpi.absentToday")} value={absentToday}  color="rgb(var(--st-holiday-solid))" isLoading={briefingLoading} />
        <KpiCard
          label={t("overview.kpi.coveragePct")}
          color="rgb(var(--st-good-solid))"
          isLoading={forecastLoading}
          value={coveragePct}
        />
      </div>

      {/* ── Missing-roster warning (RTM / TEAM_LEAD / DEV only) ── */}
      <MissingRosterBanner />

      {/* ── AL Tracker vs Shift Plan conflict warning (RTM / TEAM_LEAD / DEV only) ── */}
      <AlTrackerConflictBanner />

      {/* ── WIC coverage-gaps warning + Coverage Risk (RTM / TEAM_LEAD / DEV only) ── */}
      <WicCoverageGapsBanner
        horizon={horizon}
        state={coverageState}
        data={coverage}
        onAssigned={() => queryClient.invalidateQueries({ queryKey: ["wic-coverage-gaps"] })}
        onRefresh={() => void refetchCoverage()}
        onHorizonChange={setCoverageHorizon}
      />

      {/* ── Coverage Risk — §1.1 day strip + §1.2 exception list ── */}
      <Panel title={t("overview.coverageRisk.title")}>
        <DayStrip
          gaps={coverageGaps}
          dates={heatDates}
          today={today}
          selectedDay={selectedDay}
          onSelectDay={setSelectedDay}
        />
        <div className="border-t border-line-subtle">
          <ExceptionList
            gaps={coverageGaps}
            dates={heatDates}
            briefingGaps={briefing?.gaps ?? []}
            today={today}
            selectedDay={selectedDay}
            onClearDayFilter={() => setSelectedDay(null)}
            onOpenSub={args => { setSheetCell(args); setSheetOpen(true) }}
            horizon={horizon}
            state={coverageState}
          />
        </div>
        {(() => {
          const overlapList  = (conflicts ?? []).filter(c => c.conflictType === "OVERLAP")
          const closedList   = (conflicts ?? []).filter(c => c.conflictType === "CLOSED_LOCATION")
          const expandedList = conflictsOpen ? [...overlapList, ...closedList] : []
          if (overlapList.length === 0 && closedList.length === 0) return null
          return (
            <div className="border-t border-line-subtle">
              {/* OVERLAP banner */}
              {overlapList.length > 0 && (
                <button
                  onClick={() => setConflictsOpen(v => !v)}
                  style={{
                    display: "flex", alignItems: "center", gap: 8, width: "100%",
                    background: STATUS_TOKEN_BG["PARTIAL"], border: "none",
                    padding: "8px 14px", fontSize: 12, cursor: "pointer", textAlign: "left",
                    color: STATUS_TOKEN_FG["PARTIAL"],
                  }}
                >
                  <AlertTriangle size={13} style={{ flexShrink: 0 }} />
                  <span style={{ fontWeight: 600 }}>
                    {t("overview.conflicts.count", { count: overlapList.length })}
                  </span>
                </button>
              )}
              {/* CLOSED_LOCATION banner — separate, no fold toggle (shares the same expand state) */}
              {closedList.length > 0 && (
                <button
                  onClick={() => setConflictsOpen(v => !v)}
                  style={{
                    display: "flex", alignItems: "center", gap: 8, width: "100%",
                    background: STATUS_TOKEN_BG["UNCOVERED"], border: "none",
                    borderTop: overlapList.length > 0 ? `1px solid rgb(var(--st-crit-bd))` : "none",
                    padding: "6px 14px", fontSize: 11, cursor: "pointer", textAlign: "left",
                    color: STATUS_TOKEN_FG["UNCOVERED"],
                  }}
                >
                  <AlertTriangle size={12} style={{ flexShrink: 0 }} />
                  <span style={{ fontWeight: 600 }}>
                    {t("overview.conflicts.closedCount", { count: closedList.length })}
                  </span>
                </button>
              )}
              {/* Expanded table — shows both types with type badge */}
              {conflictsOpen && expandedList.length > 0 && (
                <div style={{ overflowX: "auto" }}>
                  <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
                    <thead>
                      <tr>
                        <th style={{ padding: "6px 14px", textAlign: "left", fontWeight: 600, color: "rgb(var(--text-secondary))", borderBottom: "1px solid rgb(var(--st-warn-bd))" }}>{t("overview.conflicts.agent")}</th>
                        <th style={{ padding: "6px 14px", textAlign: "left", fontWeight: 600, color: "rgb(var(--text-secondary))", borderBottom: "1px solid rgb(var(--st-warn-bd))" }}>{t("overview.conflicts.date")}</th>
                        <th style={{ padding: "6px 14px", textAlign: "left", fontWeight: 600, color: "rgb(var(--text-secondary))", borderBottom: "1px solid rgb(var(--st-warn-bd))" }}>{t("overview.conflicts.locations")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {expandedList.map((c, i) => {
                        const isClosed = c.conflictType === "CLOSED_LOCATION"
                        const rowBg    = isClosed ? STATUS_TOKEN_BG["UNCOVERED"] : undefined
                        return (
                          <tr key={i} className="border-b border-line-subtle" style={{ background: rowBg }}>
                            <td style={{ padding: "6px 14px", fontWeight: 500, color: isClosed ? STATUS_TOKEN_FG["UNCOVERED"] : undefined }}>{c.fullName ?? c.employeeId}</td>
                            <td style={{ padding: "6px 14px", fontFamily: "monospace", color: isClosed ? STATUS_TOKEN_FG["UNCOVERED"] : undefined }}>{c.shiftDate}</td>
                            <td style={{ padding: "6px 14px" }}>
                              {c.locations.map((l, j) => {
                                const hours = l.openTime ? ` ${l.openTime}–${l.closeTime}` : ""
                                return (
                                  <span key={j} style={{
                                    display: "inline-block", marginRight: 5, marginBottom: 2,
                                    padding: "1px 7px", borderRadius: 4, fontSize: 11, fontFamily: "monospace",
                                    background: isClosed ? STATUS_TOKEN_BG["UNCOVERED"]
                                      : l.assignmentRole === "NONE" ? "rgb(var(--bg-subtle))" : STATUS_TOKEN_BG["PARTIAL"],
                                    color: isClosed ? STATUS_TOKEN_FG["UNCOVERED"]
                                      : l.assignmentRole === "NONE" ? "rgb(var(--text-secondary))" : STATUS_TOKEN_FG["PARTIAL"],
                                  }}>
                                    {l.locationDisplayName}{hours}{l.assignmentRole !== "NONE" ? ` (${l.assignmentRole})` : ""}
                                  </span>
                                )
                              })}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          )
        })()}
      </Panel>

      {/* ── Recommendations ── */}
      {(briefing?.gaps?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.recommendations.title")} count={briefing!.gaps.length} color={STATUS_TOKEN_FG["UNCOVERED"]} />
          <div className="bg-raised border border-line-subtle rounded-md overflow-hidden">
            {briefing!.gaps.map((gap, i) => (
              <div key={i} className="border-b border-line-subtle" style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "10px 14px" }}>
                <div style={{ flex: 1 }}>
                  <div className="font-semibold text-xs text-ink">{gap.displayName}</div>
                  <div className="text-[11px] text-ink-soft mt-0.5" style={{ fontFamily: "monospace" }}>
                    {gap.present}/{gap.required} agents
                  </div>
                </div>
                {gap.bestSubstituteName && (
                  <div style={{ flex: 1, fontSize: 11, textAlign: "center" }}>
                    <span className="text-ink-soft">{t("overview.recommendations.best")}: </span>
                    <span style={{ color: STATUS_TOKEN_FG["COVERED"], fontWeight: 600 }}>{gap.bestSubstituteName}</span>
                  </div>
                )}
                <button
                  onClick={() => { setSheetCell({ locationCode: gap.locationCode, date: today, displayName: gap.displayName }); setSheetOpen(true) }}
                  style={{
                    background: STATUS_TOKEN_BG["UNCOVERED"], border: `1px solid ${STATUS_TOKEN_FG["UNCOVERED"]}`,
                    color: STATUS_TOKEN_FG["UNCOVERED"], padding: "5px 12px", borderRadius: 6,
                    fontSize: 11, cursor: "pointer", whiteSpace: "nowrap",
                  }}
                >
                  {t("attendance.substitute.find")}
                </button>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Absence Feed ── */}
      {(briefing?.absences?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.absences.title")} count={briefing!.absences.length} color="rgb(var(--st-holiday-solid))" />
          <div className="bg-raised border border-line-subtle rounded-md overflow-hidden" style={{ marginTop: 8 }}>
            {briefing!.absences.map((ab: any, i: number) => {
              const typeColors: Record<string, [string, string]> = {
                SL:      [STATUS_TOKEN_BG["PARTIAL"],   STATUS_TOKEN_FG["PARTIAL"]],
                AL:      ["rgb(var(--st-info-bg))",     "rgb(var(--st-info-solid))"],
                HALF_AL: ["rgb(var(--st-info-bg))",     "rgb(var(--st-info-fg))"],
                UL:      [STATUS_TOKEN_BG["CLOSED"],    STATUS_TOKEN_FG["CLOSED"]],
                CD:      [STATUS_TOKEN_BG["COVERED"],   STATUS_TOKEN_FG["COVERED"]],
                PH:      [STATUS_TOKEN_BG["CLOSED"],    STATUS_TOKEN_FG["CLOSED"]],
                LPH:     [STATUS_TOKEN_BG["CLOSED"],    STATUS_TOKEN_FG["CLOSED"]],
              }
              const typeLabel: Record<string, string> = { SL:"SL", AL:"AL", HALF_AL:"½AL", UL:"UL", CD:"CD", PH:"PH", LPH:"PH" }
              const key = (ab.shiftType ?? "").toUpperCase()
              const [bg, fg] = typeColors[key] ?? [STATUS_TOKEN_BG["CLOSED"], STATUS_TOKEN_FG["CLOSED"]]
              const label = typeLabel[key] ?? ab.shiftType
              const showRange = ab.firstDay && ab.lastDay && ab.firstDay !== ab.lastDay
              return (
                <div key={i} className="border-b border-line-subtle" style={{ display: "flex", alignItems: "center", gap: 12, padding: "9px 14px", fontSize: 12 }}>
                  <Users size={12} className="text-ink-soft flex-shrink-0" />
                  <span className="font-medium text-ink" style={{ minWidth: 160 }}>{ab.fullName}</span>
                  <span className="text-ink-soft" style={{ fontSize: 10, fontFamily: "monospace", minWidth: 72 }}>{ab.employeeId}</span>
                  <span style={{ background: bg, color: fg, padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "monospace", fontWeight: 700, minWidth: 32, textAlign: "center" }}>{label}</span>
                  <span className="text-ink-muted" style={{ fontFamily: "monospace", fontSize: 11, flex: 1 }}>
                    {ab.firstDay}
                    {showRange && <span className="text-ink-soft"> → {ab.lastDay}</span>}
                  </span>
                  <span className="text-ink-soft" style={{ fontFamily: "monospace", fontSize: 11, whiteSpace: "nowrap" }}>
                    {ab.lastDay >= "2099-01-01"
                      ? <span style={{ color: STATUS_TOKEN_FG["PARTIAL"], fontSize: 10 }}>open ({ab.daysSoFar} {ab.daysSoFar === 1 ? "day" : "days"} so far)</span>
                      : <>{ab.totalDays}d
                          {ab.daysSoFar > 0 && ab.daysSoFar < ab.totalDays &&
                            <span className="text-ink-soft" style={{ fontSize: 10 }}> ({ab.daysSoFar} so far)</span>}
                        </>}
                  </span>
                  {ab.wicLocation && (
                    <span className="text-ink-soft" style={{ fontSize: 10, fontFamily: "monospace" }}>{ab.wicLocation}</span>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* ── Team Lead Summary ── */}
      {(tls?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.teamLeads")} />
          <div className="grid gap-3 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
            {tls?.map((tl: any) => <TLCard key={tl.teamLeadName} tl={tl} />)}
          </div>
        </div>
      )}

      {/* ── Media row — photo | map | photo ── */}
      <div>
        <div className="grid gap-4 grid-cols-1 md:grid-cols-[1fr_2fr_1fr] md:h-80">
          <div className="rounded-lg overflow-hidden border border-line-subtle h-40 md:h-full relative">
            <img src={infosys1} alt="Infosys GSD team collaborating at a WIC location" className="w-full h-full object-cover" />
          </div>
          <div className="rounded-lg overflow-hidden border border-line-subtle h-72 md:h-full" style={{ isolation: "isolate" }}>
            {(locations?.length ?? 0) > 0 ? (
              <WicMapView
                locations={locations ?? []}
                forecast={forecast ?? []}
                onPinClick={(code) => {
                  const lf = forecast?.find(f => f.locationCode === code)
                  if (lf) { setSheetCell({ locationCode: code, date: today, displayName: lf.displayName }); setSheetOpen(true) }
                }}
              />
            ) : (
              <div className="skeleton w-full h-full" style={{ borderRadius: 0 }} />
            )}
          </div>
          <div className="rounded-lg overflow-hidden border border-line-subtle h-40 md:h-full relative">
            <img src={infosys2} alt="Infosys GSD team members supporting WIC operations" className="w-full h-full object-cover" />
          </div>
        </div>
        {/* i18n exception (approved): fixed attribution, identical in EN and DE. */}
        <p className="text-sm text-ink-muted text-center pt-3">All of this is made possible thanks to Infosys.</p>
      </div>

      {/* ── Full Coverage Grid — §1.3 moved to bottom, collapsible, closed by default ── */}
      {/* §1.5: inline [7d/14d/28d] toggle removed — topbar is sole control              */}
      <Panel
        title={t("overview.grid.title")}
        collapsible
        storageKey="gsd.overview.grid"
        defaultOpen={false}
      >
        {/* §1.4: legend swatches — CLOSED was STATUS_HEX "#7a8fa8" (blue-grey), now bg-neutralst-solid */}
        <div className="border-b border-line-subtle" style={{ padding: "8px 16px", display: "flex", gap: 16, alignItems: "center", flexWrap: "wrap" }}>
          {["COVERED","PARTIAL","UNCOVERED","CLOSED"].map(s => (
            <span key={s} style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 10, color: "rgb(var(--text-tertiary))" }}>
              <span className={["w-2 h-2 rounded-xs inline-block", LEGEND_CLASS[s]].join(' ')} />
              {s.toLowerCase()}
            </span>
          ))}
        </div>
        {forecastLoading ? (
          <div style={{ padding: 24, display: "flex", flexDirection: "column", gap: 8 }}>
            {[1,2,3,4,5].map(i => <div key={i} className="skeleton" style={{ height: 28 }} />)}
          </div>
        ) : (
          <div className="overflow-auto max-h-[520px]">
            <table style={{ borderCollapse: "collapse", fontSize: 11 }} className="min-w-max w-full">
              <thead>
                <tr>
                  <th style={{ padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, color: "rgb(var(--text-tertiary))", borderBottom: "1px solid rgb(var(--border-subtle))", minWidth: 180, position: "sticky", left: 0, top: 0, background: "rgb(var(--surface-sunken))", zIndex: 3 }}>
                    {t("overview.heatmap.location")}
                  </th>
                  {heatDates.map(d => {
                    const { dow, day } = dayLabel(d)
                    const we = isWeekend(d)
                    const isToday = d === today
                    return (
                      <th key={d} style={{
                        padding: "6px 2px", textAlign: "center", minWidth: 38, maxWidth: 38,
                        borderBottom: "1px solid rgb(var(--border-subtle))",
                        borderLeft: we ? "1px solid rgb(var(--border-subtle))" : undefined,
                        background: isToday ? "rgb(var(--st-info-bg))" : "rgb(var(--surface-sunken))",
                        color: isToday ? "rgb(var(--st-info-solid))" : "rgb(var(--text-tertiary))",
                        position: "sticky", top: 0,
                        zIndex: 1,
                      }}>
                        <div style={{ fontSize: 9, fontFamily: "monospace" }}>{dow}</div>
                        <div style={{ fontSize: 10, fontWeight: isToday ? 700 : 400 }}>{day}</div>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {/* §1.3: grid internals preserved verbatim (behaviour unchanged) */}
                {(forecast ?? []).map(lf => (
                  <tr key={lf.locationCode} style={{ borderBottom: "1px solid rgb(var(--border-subtle))" }}>
                    <td style={{
                      padding: "6px 12px", position: "sticky", left: 0,
                      background: "rgb(var(--surface-raised))", zIndex: 1, fontWeight: 500, fontSize: 11,
                      display: "flex", alignItems: "center", gap: 5,
                    }} className="text-ink-muted">
                      {lf.displayName}
                      {lf.isNpp && <NppBadge />}
                    </td>
                    {heatDates.map(d => {
                      // §1.3: cell status computation unchanged
                      const cell = lf.forecast?.find(df => df.date === d)
                      const status = cell?.status ?? "CLOSED"
                      const we = isWeekend(d)
                      return (
                        <td
                          key={d}
                          // §1.3: PARTIAL/UNCOVERED open substitute drawer; COVERED/CLOSED inert
                          onClick={() => (status === "UNCOVERED" || status === "PARTIAL") && openSub(lf, d)}
                          title={`${lf.displayName} — ${d} — ${status}`}
                          style={{
                            padding: "2px",
                            borderLeft: we ? "1px solid rgb(var(--border-subtle))" : undefined,
                            cursor: (status === "UNCOVERED" || status === "PARTIAL") ? "pointer" : "default",
                          }}
                        >
                          <div className={["w-full h-6 rounded-[3px] flex items-center justify-center", HEATMAP_CELL_CLASS[status] ?? "bg-neutralst-mid"].join(" ")}>
                            {d === today && <span style={{ width: 6, height: 6, borderRadius: "50%", background: STATUS_TOKEN_FG[status] ?? "transparent", display: "inline-block" }} />}
                          </div>
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      {/* ── WIC Detail Grid — localStorage-persisted (VI) ── */}
      <div>
        <button
          onClick={toggleDetailGrid}
          className="bg-raised border border-line-subtle text-ink-soft rounded-sm text-[11px] cursor-pointer"
          style={{ padding: "7px 14px", fontFamily: "monospace" }}
        >
          {showDetailGrid ? "▲" : "▼"} {t("overview.detailGrid")}
        </button>
        {showDetailGrid && (
          <div className="overflow-auto max-h-[520px]" style={{ marginTop: 12, display: "flex", flexDirection: "column", gap: 16 }}>
            {cardsLoading && <div className="text-ink-soft text-xs" style={{ padding: 16 }}>Loading...</div>}
            {uncovered.length > 0 && (
              <div>
                <SectionHeader title={`Uncovered (${uncovered.length})`} color={STATUS_TOKEN_FG["UNCOVERED"]} />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10 }}>
                  {uncovered.map(c => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            {partial.length > 0 && (
              <div>
                <SectionHeader title={`Partial (${partial.length})`} color={STATUS_TOKEN_FG["PARTIAL"]} />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10 }}>
                  {partial.map(c => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            {covered.length > 0 && (
              <div>
                <SectionHeader title={`Covered (${covered.length})`} color={STATUS_TOKEN_FG["COVERED"]} />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10 }}>
                  {covered.map(c => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            {allClosed ? (
              <div>
                <SectionHeader title={`Closed (${closed.length})`} color={STATUS_TOKEN_FG["CLOSED"]} />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10 }}>
                  {closed.map(c => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            ) : (
              <div>
                <button
                  onClick={() => setShowClosed(!showClosed)}
                  className="bg-raised border border-line-subtle text-ink-soft rounded-sm text-[11px] cursor-pointer"
                  style={{ padding: "5px 12px", fontFamily: "monospace" }}
                >
                  {showClosed ? "Hide" : "Show"} Closed ({closed.length})
                </button>
                {showClosed && (
                  <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(200px, 1fr))", gap: 10, marginTop: 10 }}>
                    {closed.map(c => <WicCard key={c.locationCode} card={c} />)}
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* ── Substitute Sheet ── */}
      <Sheet isOpen={sheetOpen} onClose={() => setSheetOpen(false)} title={`${sheetCell?.displayName ?? ""} – ${sheetCell?.date ?? ""}`}>
        <div style={{ padding: 20 }}>
          <div className="text-[15px] font-semibold text-ink" style={{ marginBottom: 16 }}>
            {t("attendance.substitute.title")}
          </div>
          {sheetCell && (
            <SubstituteDrawer
              locationCode={sheetCell.locationCode}
              date={sheetCell.date}
              displayName={sheetCell.displayName}
            />
          )}
        </div>
      </Sheet>
    </div>
  )
}
