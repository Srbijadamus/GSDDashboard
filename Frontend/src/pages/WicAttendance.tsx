import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Search, AlertTriangle, UserCheck, Users, Clock, Calendar, Settings } from "lucide-react"
import { CoverageBadge } from "../components/CoverageBadge"
import { NppBadge } from "../components/NppBadge"
import { Sheet } from "../components/Sheet"
import { ALPlanningModal } from "./ALPlanningModal"
import { AssignAgentModal } from "./AssignAgentModal"
import { ManualCheckinModal } from "./ManualCheckinModal"

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
  sourceType: "BACKUP" | "SSP" | "WIC_DONOR" | "CALL_IN"
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
  BACKUP:    { bg: "rgba(124,58,237,0.15)",  color: "#a78bfa" },
  SSP:       { bg: "rgba(59,126,255,0.15)",  color: "#60a5fa" },
  WIC_DONOR: { bg: "rgba(0,210,160,0.15)",   color: "#34d399" },
  CALL_IN:   { bg: "rgba(255,124,59,0.15)",  color: "#fb923c" },
}

const AGENT_MATCH_COLORS: Record<string, { bg: string; color: string }> = {
  FULL:    { bg: "rgba(34,208,122,0.12)",  color: "#22d07a" },
  PARTIAL: { bg: "rgba(255,124,59,0.12)",  color: "#ff7c3b" },
  NONE:    { bg: "rgba(255,59,92,0.12)",   color: "#ff3b5c" },
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function chunkWeeks(days: ForecastDay[]): ForecastDay[][] {
  const weeks: ForecastDay[][] = []
  for (let i = 0; i < days.length; i += 7) weeks.push(days.slice(i, i + 7))
  return weeks
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
    <div style={{
      background: "var(--card)", border: "1px solid var(--border)",
      borderRadius: 8, padding: "12px 16px",
    }}>
      <div style={{
        fontSize: 10, textTransform: "uppercase" as const, letterSpacing: ".07em",
        color: "var(--text3)", marginBottom: 8,
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
    <div style={{
      background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8,
      ...style,
    }}>
      <div style={{
        padding: "10px 16px", borderBottom: "1px solid var(--border)",
        display: "flex", alignItems: "center", justifyContent: "space-between", gap: 6,
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span style={{ color: "var(--text3)" }}>{icon}</span>
          <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>{title}</span>
        </div>
        {action}
      </div>
      <div style={{ padding: 16 }}>{children}</div>
    </div>
  )
}

const DOW_NAMES = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]

function MinRequiredEditor({ locationCode }: { locationCode: string }) {
  const qc = useQueryClient()
  const { data: hoursData } = useQuery({
    queryKey: ["wic-opening-hours"],
    queryFn: () => fetch("/api/wicschedule/opening-hours").then(r => r.json()),
    staleTime: 60000,
  })

  const locHours = (hoursData ?? []).find((l: any) => l.locationCode === locationCode)
  const weeklyHours: any[] = locHours?.weeklyHours ?? []

  const [editing, setEditing] = useState<Record<number, string>>({})
  const [saving, setSaving] = useState<number | null>(null)

  const saveMin = async (dow: number, value: string) => {
    const parsed = value === "" ? null : parseInt(value, 10)
    if (value !== "" && isNaN(parsed!)) return
    setSaving(dow)
    try {
      const res = await fetch(`/api/wicschedule/opening-hours/${encodeURIComponent(locationCode)}/${dow}/min-required`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ value: parsed }),
      })
      if (!res.ok) throw new Error("Save failed")
      qc.invalidateQueries({ queryKey: ["wic-opening-hours"] })
      qc.invalidateQueries({ queryKey: ["wic-forecast"] })
      setEditing(prev => { const n = { ...prev }; delete n[dow]; return n })
    } catch (e: any) {
      alert(e?.message ?? "Save failed")
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
              <div key={h.dayOfWeek} style={{
                background: h.isClosed ? "rgba(30,45,69,.3)" : "var(--card2)",
                border: "1px solid var(--border)", borderRadius: 6,
                padding: "8px 6px", textAlign: "center", opacity: h.isClosed ? 0.5 : 1,
              }}>
                <div style={{ fontSize: 9, color: "var(--text3)", textTransform: "uppercase", marginBottom: 4 }}>
                  {DOW_NAMES[h.dayOfWeek]}
                </div>
                {h.isClosed ? (
                  <div style={{ fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>—</div>
                ) : (
                  <>
                    <input
                      type="number" min={0} max={20}
                      value={val}
                      onChange={e => setEditing(prev => ({ ...prev, [h.dayOfWeek]: e.target.value }))}
                      onKeyDown={e => { if (e.key === "Enter") saveMin(h.dayOfWeek, String(val)) }}
                      disabled={saving === h.dayOfWeek}
                      style={{
                        width: "100%", boxSizing: "border-box" as const,
                        background: "var(--card)", border: "1px solid var(--border)",
                        color: "var(--text)", padding: "3px 4px", borderRadius: 4,
                        fontSize: 12, fontFamily: "IBM Plex Mono", textAlign: "center",
                        outline: "none",
                      }}
                    />
                    {editing[h.dayOfWeek] !== undefined && (
                      <button
                        onClick={() => saveMin(h.dayOfWeek, String(val))}
                        disabled={saving === h.dayOfWeek}
                        style={{
                          marginTop: 4, width: "100%", background: "var(--accent)",
                          border: "none", color: "#fff", padding: "2px 0",
                          borderRadius: 3, fontSize: 9, cursor: "pointer",
                        }}
                      >
                        {saving === h.dayOfWeek ? "…" : "Save"}
                      </button>
                    )}
                    {editing[h.dayOfWeek] === undefined && h.minRequired == null && (
                      <div style={{ fontSize: 9, color: "var(--text3)", marginTop: 2 }}>default</div>
                    )}
                  </>
                )}
              </div>
            )
          })}
        </div>
        <div style={{ marginTop: 8, fontSize: 10, color: "var(--text3)" }}>
          Blank = inherit from location default. Press Enter or Save after editing.
        </div>
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

  const { data: kioskData = [] } = useQuery<KioskRecord[]>({
    queryKey: ["kiosk-attendance"],
    queryFn: (): Promise<KioskRecord[]> =>
      fetch(`${import.meta.env.VITE_KIOSK_API_URL ?? "https://ssr7tm2l-8000.euw.devtunnels.ms"}/api/attendance`).then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status}`)
        return r.json()
      }),
    staleTime: 30 * 1000,
    refetchInterval: 60 * 1000,
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
  const liveCount = kioskData.filter(r => r.attendance_status === "ACTIVE").length
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

  const inputStyle: React.CSSProperties = {
    width: "100%", background: "var(--card2)", border: "1px solid var(--border)",
    color: "var(--text)", padding: "6px 10px", borderRadius: 6,
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

  return (
    <div style={{ display: "flex", margin: -20, height: "calc(100vh - 45px)", overflow: "hidden" }}>

      {/* ── LEFT SIDEBAR ──────────────────────────────────────────────────────── */}
      <aside style={{
        width: 260, flexShrink: 0,
        borderRight: "1px solid var(--border)",
        background: "var(--sidebar)",
        display: "flex", flexDirection: "column",
        overflow: "hidden",
      }}>
        <div style={{ padding: "14px 12px 10px", borderBottom: "1px solid var(--border)", flexShrink: 0 }}>
          <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text)", marginBottom: 10 }}>
            {t("attendance.title")}
          </div>
          <div style={{ position: "relative", marginBottom: 8 }}>
            <Search size={12} style={{
              position: "absolute", left: 8, top: "50%",
              transform: "translateY(-50%)", color: "var(--text3)",
            }} />
            <input
              placeholder={t("attendance.filter.search")}
              value={search}
              onChange={e => setSearch(e.target.value)}
              style={{ ...inputStyle, paddingLeft: 26 }}
            />
          </div>
          <select value={countryFilter} onChange={e => setCountryFilter(e.target.value)} style={inputStyle}>
            <option value="">{t("attendance.filter.allCountries")}</option>
            {countries.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </div>

        <div style={{ padding: "8px 12px", borderBottom: "1px solid var(--border)", flexShrink: 0 }}>
          <input
            type="date"
            value={selectedDate}
            onChange={e => setSelectedDate(e.target.value)}
            style={inputStyle}
          />
        </div>

        {/* ── CHECK-IN STATUS PANEL ────────────────────────────────────── */}
        {cards && (
          <div style={{ borderBottom: "1px solid var(--border)", flexShrink: 0 }}>
            <div style={{ padding: "7px 12px 5px", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
              <span style={{ fontSize: 10, fontWeight: 700, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".07em" }}>
                Check-in Status
              </span>
              <span style={{ fontSize: 9, color: "var(--text3)" }}>Anmeldungsstatus</span>
            </div>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", borderTop: "1px solid var(--border)" }}>
              <div style={{ padding: "7px 6px", textAlign: "center" }}>
                <div style={{ fontSize: 17, fontWeight: 700, fontFamily: "IBM Plex Mono", color: "var(--text)" }}>{expectedCount}</div>
                <div style={{ fontSize: 8, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Expected</div>
              </div>
              <div style={{ padding: "7px 6px", textAlign: "center", borderLeft: "1px solid var(--border)", borderRight: "1px solid var(--border)" }}>
                <div style={{ fontSize: 17, fontWeight: 700, fontFamily: "IBM Plex Mono", color: "#22d07a" }}>{checkedInCount}</div>
                <div style={{ fontSize: 8, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Checked In</div>
              </div>
              <div style={{ padding: "7px 6px", textAlign: "center" }}>
                <div style={{ fontSize: 17, fontWeight: 700, fontFamily: "IBM Plex Mono", color: notYetInCount > 0 ? "var(--warn)" : "var(--text3)" }}>{notYetInCount}</div>
                <div style={{ fontSize: 8, color: "var(--text3)", textTransform: "uppercase", letterSpacing: ".06em", marginTop: 1 }}>Not Yet In</div>
              </div>
            </div>
            {notYetInList.length > 0 && (
              <div style={{ maxHeight: 130, overflowY: "auto", borderTop: "1px solid var(--border)" }}>
                {notYetInList.map(agent => (
                  <div key={agent.employeeId} style={{
                    padding: "5px 12px",
                    borderBottom: "1px solid rgba(30,45,69,0.3)",
                    display: "flex", justifyContent: "space-between", alignItems: "center",
                  }}>
                    <div>
                      <div style={{ fontSize: 10, fontWeight: 500, color: "var(--text)" }}>{agent.name}</div>
                      {agent.teamLead && (
                        <div style={{ fontSize: 8, color: "var(--text3)" }}>{agent.teamLead}</div>
                      )}
                    </div>
                    <div style={{ fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono", flexShrink: 0 }}>
                      {agent.shiftStart}–{agent.shiftEnd}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        <div style={{ flex: 1, overflowY: "auto" }}>
          {forecastLoading
            ? Array.from({ length: 10 }).map((_, i) => (
                <div key={i} style={{ padding: "10px 12px", borderBottom: "1px solid rgba(30,45,69,0.4)" }}>
                  <Skeleton height={13} width="75%" />
                  <div style={{ marginTop: 5 }}><Skeleton height={9} width="45%" /></div>
                </div>
              ))
            : filteredLocations.length === 0
            ? <div style={{ padding: 20, textAlign: "center", fontSize: 12, color: "var(--text3)" }}>
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
                      background: isSelected ? "rgba(59,126,255,0.1)" : "transparent",
                      borderLeft: isSelected ? "2px solid var(--accent)" : "2px solid transparent",
                      borderBottom: "1px solid rgba(30,45,69,0.3)",
                      transition: "background 0.1s",
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 6 }}>
                      <span style={{ display: "flex", alignItems: "center", gap: 4, flex: 1, minWidth: 0 }}>
                        <span style={{
                          fontSize: 12, fontWeight: isSelected ? 600 : 400,
                          color: isSelected ? "var(--accent)" : "var(--text)",
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
                              background: "#22d07a", display: "inline-block",
                            }} />
                            <span style={{ fontSize: 9, color: "#22d07a", fontFamily: "IBM Plex Mono" }}>
                              {locActiveCount}
                            </span>
                          </span>
                        )}
                        <CoverageBadge status={status} compact />
                      </div>
                    </div>
                    <div style={{ display: "flex", justifyContent: "space-between", marginTop: 3 }}>
                      <span style={{ fontSize: 10, color: "var(--text3)" }}>{loc.city}</span>
                      {loc.atRiskDays > 0 && (
                        <span style={{
                          fontSize: 9, color: "var(--danger)",
                          fontFamily: "IBM Plex Mono",
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
          <div style={{
            padding: "7px 12px", borderTop: "1px solid var(--border)",
            fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono", flexShrink: 0,
          }}>
            {forecast.locationCount} loc · {forecast.totalAtRiskDays} at-risk
          </div>
        )}
      </aside>

      {/* ── RIGHT CONTENT ─────────────────────────────────────────────────────── */}
      <div style={{ flex: 1, overflowY: "auto", padding: 20, minWidth: 0 }}>
        {!selectedLocationCode ? (
          <div style={{
            display: "flex", alignItems: "center", justifyContent: "center",
            height: 200, color: "var(--text3)", fontSize: 13,
          }}>
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
                <div style={{ fontSize: 20, fontWeight: 600, color: "var(--text)", display: "flex", alignItems: "center", gap: 8 }}>
                  {selectedForecast?.displayName ?? selectedCard?.displayName ?? "—"}
                  {selectedForecast?.isNpp && <NppBadge />}
                </div>
                <div style={{ fontSize: 12, color: "var(--text3)", marginTop: 2 }}>
                  {selectedForecast?.city} · {selectedForecast?.country}
                </div>
              </div>
              <div style={{ display: "flex", alignItems: "center", gap: 8, flexShrink: 0 }}>
                {liveCount > 0 && (
                  <span style={{
                    display: "flex", alignItems: "center", gap: 5,
                    background: "rgba(34,208,122,0.15)", color: "#22d07a",
                    border: "1px solid rgba(34,208,122,0.35)",
                    padding: "4px 10px", borderRadius: 20,
                    fontSize: 11, fontWeight: 600,
                  }}>
                    <span style={{
                      width: 7, height: 7, borderRadius: "50%",
                      background: "#22d07a", display: "inline-block",
                      animation: "pulse-green 1.5s ease-in-out infinite",
                    }} />
                    Live {liveCount}
                  </span>
                )}
                <button
                  onClick={() => setAlPlanningOpen(true)}
                  style={{
                    background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
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
                  style={{
                    background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
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
                  style={{
                    background: "var(--card)", border: "1px solid var(--border)", color: "var(--text)",
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
                  style={{
                    background: "var(--accent)", border: "none", color: "#fff",
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
                background: "rgba(34,208,122,0.07)",
                border: "1px solid rgba(34,208,122,0.25)",
                borderRadius: 8, padding: "12px 16px", marginBottom: 14,
              }}>
                <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 10 }}>
                  <span style={{
                    width: 8, height: 8, borderRadius: "50%",
                    background: "#22d07a", display: "inline-block",
                    animation: "pulse-green 1.5s ease-in-out infinite",
                  }} />
                  <span style={{ fontSize: 12, fontWeight: 600, color: "#22d07a" }}>
                    Currently Present · {presentAgents.length}
                  </span>
                </div>
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  {presentAgents.map(r => (
                    <div key={r.employee_id} style={{
                      display: "flex", alignItems: "center", gap: 6,
                      background: "rgba(34,208,122,0.1)", border: "1px solid rgba(34,208,122,0.2)",
                      borderRadius: 6, padding: "5px 10px",
                    }}>
                      <span style={{
                        width: 6, height: 6, borderRadius: "50%",
                        background: "#22d07a", display: "inline-block",
                        animation: "pulse-green 1.5s ease-in-out infinite",
                      }} />
                      <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>{r.full_name}</span>
                      {r.checkin_time && (
                        <span style={{ fontSize: 10, color: "#22d07a", fontFamily: "IBM Plex Mono" }}>
                          {r.checkin_time.slice(11, 16)}
                        </span>
                      )}
                      {r.minutes_on_shift > 0 && (
                        <span style={{ fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
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
                    <div key={i} style={{
                      background: "var(--card)", border: "1px solid var(--border)",
                      borderRadius: 8, padding: "12px 16px",
                    }}>
                      <Skeleton height={10} width="55%" />
                      <div style={{ marginTop: 8 }}><Skeleton height={22} width="40%" /></div>
                    </div>
                  ))
                : <>
                    <StatCard label={t("status.occupied")}>
                      <CoverageBadge status={selectedCard?.coverageStatus ?? selectedDay?.status ?? "CLOSED"} />
                    </StatCard>
                    <StatCard label={t("attendance.agents.title")}>
                      <div style={{
                        fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono",
                        color: selectedDay?.isAtRisk ? "var(--danger)" : "var(--green)",
                      }}>
                        {selectedDay?.effectiveCoverage ?? "—"}
                      </div>
                    </StatCard>
                    <StatCard label={t("attendance.risk.minRequired", { n: selectedDay?.minRequired ?? "?" })}>
                      <div style={{ fontSize: 26, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--text2)" }}>
                        {selectedDay?.minRequired ?? "—"}
                      </div>
                    </StatCard>
                    <StatCard label={selectedCard?.todaySchedule.isClosed ? t("status.closed") : t("attendance.today")}>
                      <div style={{ fontSize: 14, fontWeight: 600, fontFamily: "IBM Plex Mono", color: "var(--text2)", marginTop: 4 }}>
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

            {/* Agent chips */}
            <SectionCard title={t("attendance.agents.title")} icon={<Users size={13} />}>
              {cardsLoading ? (
                <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
                  {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} width={130} height={64} />)}
                </div>
              ) : !selectedCard || selectedCard.assignedAgents.length === 0 ? (
                <div style={{ color: "var(--text3)", fontSize: 12 }}>
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
                              background: "#22d07a", display: "inline-block", flexShrink: 0,
                              animation: "pulse-green 1.5s ease-in-out infinite",
                            }} />
                          )}
                          {kiosk?.attendance_status === "DONE" && (
                            <span style={{
                              width: 7, height: 7, borderRadius: "50%",
                              background: "var(--text3)", display: "inline-block", flexShrink: 0,
                            }} />
                          )}
                          <span style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>{agent.name}</span>
                        </div>
                        <div style={{ display: "flex", gap: 4, alignItems: "center" }}>
                          <span style={{
                            fontSize: 9, fontWeight: 600, textTransform: "uppercase" as const,
                            background: agent.isMain ? "rgba(59,126,255,0.2)" : "rgba(122,143,168,0.2)",
                            color: agent.isMain ? "var(--accent)" : "var(--text3)",
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
                          <div style={{ fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                            {agent.shiftStart === "SICK" || agent.shiftEnd === "SICK"
                              ? "SL"
                              : agent.shiftStart === "AL" || agent.shiftEnd === "AL"
                              ? "AL"
                              : `${agent.shiftStart ?? ""}–${agent.shiftEnd ?? ""}`}
                          </div>
                        )}
                        {kiosk?.attendance_status === "ACTIVE" && (
                          <div style={{ fontSize: 9, color: "#22d07a", fontFamily: "IBM Plex Mono" }}>
                            {kiosk.checkin_time ? kiosk.checkin_time.slice(11, 16) : "checked in"}
                          </div>
                        )}
                        {kiosk?.attendance_status === "DONE" && (
                          <div style={{ fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                            {kiosk.checkout_time ? kiosk.checkout_time.slice(11, 16) : "done"}
                          </div>
                        )}
                      </div>
                    )
                  })}
                </div>
              )}
            </SectionCard>

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
                      style={{
                        background: horizonDays === n ? "var(--accent)" : "var(--card2)",
                        border: `1px solid ${horizonDays === n ? "var(--accent)" : "var(--border)"}`,
                        color: horizonDays === n ? "#fff" : "var(--text2)",
                        borderRadius: 5, padding: "3px 9px", fontSize: 10, fontWeight: 600, cursor: "pointer",
                      }}
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
                <div style={{ color: "var(--text3)", fontSize: 12 }}>—</div>
              ) : (
                <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                  {chunkWeeks(selectedForecast.forecast ?? []).map((week, wi) => (
                    <div key={wi} style={{ display: "flex", gap: 6 }}>
                      {week.map(day => (
                        <div
                          key={day.date}
                          onClick={() => { setSelectedDate(day.date); if (day.isAtRisk) openSub(day.date) }}
                          style={{
                            background: day.date === selectedDate ? "rgba(59,126,255,0.12)" : "var(--card2)",
                            border: `1px solid ${day.date === selectedDate ? "var(--accent)" : "var(--border)"}`,
                            borderRadius: 8, padding: "8px 4px",
                            flex: "1 1 0", minWidth: 0, cursor: "pointer",
                            transition: "all 0.1s",
                            display: "flex", flexDirection: "column", gap: 4, alignItems: "center",
                          }}
                        >
                          <div style={{ fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>
                            {new Date(day.date + "T00:00:00").toLocaleDateString(undefined, { weekday: "short" })}
                          </div>
                          <div style={{ fontSize: 11, color: "var(--text2)", fontFamily: "IBM Plex Mono" }}>
                            {day.date.slice(5)}
                          </div>
                          <CoverageBadge status={day.status} compact />
                          {day.isAtRisk && <AlertTriangle size={10} color="var(--danger)" />}
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
          <div style={{ color: "var(--text3)", fontSize: 13, marginTop: 10 }}>
            {t("attendance.substitute.loading")}
          </div>
        ) : subsDay.candidates.length === 0 ? (
          <div style={{ color: "var(--text3)", fontSize: 13, textAlign: "center", marginTop: 20 }}>
            {t("attendance.substitute.noCandidates")}
          </div>
        ) : (
          <>
            <div style={{ fontSize: 11, color: "var(--text3)", marginBottom: 14, fontFamily: "IBM Plex Mono" }}>
              {sheetDate} · {t("attendance.risk.effectiveCoverage", { n: subsDay.present })} · gap {subsDay.gap}
            </div>
            {acceptError && (
              <div style={{
                background: "rgba(255,59,92,.12)", border: "1px solid rgba(255,59,92,.3)",
                borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--danger)", marginBottom: 8
              }}>
                {acceptError}
              </div>
            )}
            {acceptedSubId && (
              <div style={{
                background: "rgba(34,208,122,.12)", border: "1px solid rgba(34,208,122,.3)",
                borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--green)", marginBottom: 8
              }}>
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
                  <div key={c.employeeId} style={{
                    background: acceptedSubId === c.employeeId ? "rgba(34,208,122,.06)" : "var(--card)",
                    border: `1px solid ${acceptedSubId === c.employeeId ? "rgba(34,208,122,.3)" : "var(--border)"}`,
                    borderRadius: 8, padding: "12px 14px",
                  }}>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                        <span style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono", minWidth: 18 }}>
                          {i + 1}.
                        </span>
                        <span style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>{c.fullName}</span>
                      </div>
                      <span style={{
                        fontSize: 9, fontWeight: 700, textTransform: "uppercase" as const,
                        background: sc.bg, color: sc.color, padding: "2px 7px", borderRadius: 4,
                      }}>
                        {c.sourceType}
                      </span>
                    </div>
                    <div style={{ marginTop: 6, display: "flex", gap: 12, fontSize: 11, color: "var(--text3)", flexWrap: "wrap" }}>
                      <span>{c.homeLocationName}</span>
                      <span style={{ fontFamily: "IBM Plex Mono" }}>
                        {t("attendance.substitute.distance", { km: (c.distanceKm ?? 0).toFixed(0) })}
                      </span>
                      {c.loadScore > 0 && (
                        <span style={{ color: "var(--warn)", fontFamily: "IBM Plex Mono" }}>
                          {t("attendance.substitute.lastUsed", { n: c.loadScore })}
                        </span>
                      )}
                    </div>
                    {!acceptedSubId && (
                      <div style={{ marginTop: 10, display: "flex", justifyContent: "flex-end" }}>
                        <button
                          onClick={() => handleAcceptSub(c)}
                          disabled={acceptingId === c.employeeId}
                          style={{
                            background: "var(--green)", border: "none", color: "#fff",
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
