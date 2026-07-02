import "leaflet/dist/leaflet.css"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { useState, useEffect } from "react"
import { useSearchParams } from "react-router-dom"
import { useTheme } from "next-themes"
import { MapContainer, TileLayer, CircleMarker, Popup, useMap } from "react-leaflet"
import { AlertTriangle, Users, Calendar } from "lucide-react"
import { useQueryClient } from "@tanstack/react-query"
import { api, apiFetch } from "../api/client"
import { Sheet } from "../components/Sheet"

// ── Types ─────────────────────────────────────────────────────────────────────

interface DayForecast { date: string; status: string; effectiveCoverage: number; minRequired: number }
interface LocationForecast {
  locationCode: string; displayName: string; city: string; country: string
  coordinates: string | null; forecast: DayForecast[]; atRiskDays: number; todayStatus: string
}
interface BriefingGap {
  locationCode: string; displayName: string; gapDate: string
  agentCount: number; minRequired: number
  bestSubstituteName: string | null; bestSubstituteSource: string | null; bestSubstituteDistanceKm: number | null
}
interface BriefingAbsence { employeeId: number; fullName: string; leaveType: string; locationCode: string | null }
interface Briefing {
  absences: BriefingAbsence[]
  gaps: BriefingGap[]
  nextAtRiskDays?: Array<{ locationCode: string; date: string; expectedStatus: string }>
}
interface WicLocation { locationCode: string; displayName: string; city: string; country: string; coordinates: string | null }

// ── Helpers ───────────────────────────────────────────────────────────────────

function parseCoords(s: string | null | undefined): [number, number] | null {
  if (!s) return null
  const p = s.split(",").map(x => Number(x.trim()))
  if (p.length !== 2 || p.some(isNaN)) return null
  return [p[0], p[1]]
}

const STATUS_HEX: Record<string, string> = {
  COVERED: "#22d07a", PARTIAL: "#ff7c3b", UNCOVERED: "#ff3b5c", CLOSED: "#7a8fa8"
}
const STATUS_BG: Record<string, string> = {
  COVERED: "rgba(34,208,122,.12)", PARTIAL: "rgba(255,124,59,.15)",
  UNCOVERED: "rgba(255,59,92,.18)", CLOSED: "rgba(74,95,122,.08)",
}

// ── Sub-components ────────────────────────────────────────────────────────────

function KpiCard({ label, value, color, isLoading }: { label: string; value: any; color?: string; isLoading?: boolean }) {
  const borderTop = color ?? "var(--border)"
  return (
    <div style={{
      background: "var(--card)", borderRadius: 8, padding: "16px 20px",
      border: "1px solid var(--border)", borderTop: `3px solid ${borderTop}`
    }}>
      <div style={{ fontSize: 10, fontWeight: 500, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>
        {label}
      </div>
      {isLoading
        ? <div className="skeleton" style={{ height: 34, width: 64 }} />
        : <div style={{ fontSize: 32, fontWeight: 700, fontFamily: "IBM Plex Mono", color: color ?? "var(--text)" }}>{value ?? "—"}</div>
      }
    </div>
  )
}

function WarningBanner({ msg }: { msg: string }) {
  return (
    <div style={{
      display: "flex", alignItems: "center", gap: 8,
      background: "rgba(255,124,59,.08)", border: "1px solid rgba(255,124,59,.25)",
      borderRadius: 6, padding: "8px 14px", fontSize: 12, color: "var(--warn)"
    }}>
      <AlertTriangle size={13} style={{ flexShrink: 0 }} />
      {msg}
    </div>
  )
}

// Tile layer inside MapContainer — uses theme-aware URL
function ThemedTileLayer() {
  const { resolvedTheme } = useTheme()
  const isDark = resolvedTheme === "dark"
  return (
    <TileLayer
      key={isDark ? "dark" : "light"}
      url={isDark
        ? "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png"
        : "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"}
      attribution={isDark
        ? '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors &copy; <a href="https://carto.com/attributions">CARTO</a>'
        : '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'}
    />
  )
}

const GERMANY_BOUNDS: [[number, number], [number, number]] = [[47.2, 5.8], [55.1, 15.1]]

function GermanyBoundsLock() {
  const map = useMap()
  useEffect(() => {
    map.setMinZoom(6)
    map.setMaxBounds(GERMANY_BOUNDS)
    setTimeout(() => {
      map.invalidateSize()
      map.fitBounds(GERMANY_BOUNDS, { padding: [0, 0] })
    }, 100)
  }, [map])
  return null
}

interface WicMapProps {
  locations: WicLocation[]
  forecast: LocationForecast[]
  onPinClick: (locationCode: string) => void
}
function WicMapView({ locations, forecast, onPinClick }: WicMapProps) {
  const { t } = useTranslation()
  const today = new Date().toISOString().split("T")[0]

  const statusMap  = new Map(forecast.map(lf => [lf.locationCode, lf.todayStatus]))
  const forecastMap = new Map(forecast.map(lf => [lf.locationCode, lf]))
  const withCoords = locations.filter(l => parseCoords(l.coordinates))

  if (withCoords.length === 0) {
    return (
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: 16 }}>
        <WarningBanner msg={t("overview.map.noCoordinates")} />
      </div>
    )
  }

  return (
    // isolation: isolate creates a self-contained stacking context so Leaflet's
    // internal pane z-indexes (200-700) do not escape into the page stacking context
    // and visually overlay sibling sections (KPI row, heatmap, etc.)
    <div style={{ borderRadius: 8, overflow: "hidden", isolation: "isolate" }}>
      <MapContainer
        center={[51.1657, 10.4515]}
        zoom={6}
        minZoom={6}
        maxZoom={12}
        maxBounds={GERMANY_BOUNDS}
        maxBoundsViscosity={1.0}
        style={{ height: 560, maxWidth: 520, margin: "0 auto", borderRadius: 12 }}
        scrollWheelZoom={false}
      >
        <GermanyBoundsLock />
        <ThemedTileLayer />
        {withCoords.map(loc => {
          const coords = parseCoords(loc.coordinates)!
          const status   = statusMap.get(loc.locationCode) ?? "CLOSED"
          const color    = STATUS_HEX[status] ?? "#7a8fa8"
          const isAtRisk = status === "UNCOVERED" || status === "PARTIAL"
          const lf       = forecastMap.get(loc.locationCode)
          const todayDF  = lf?.forecast?.find(df => df.date === today)
          return (
            // No eventHandlers.click — setting React state from a Leaflet (non-React)
            // event handler causes a re-render that closes the Popup before it paints.
            // The Popup child handles all interaction; the "Find substitute" button
            // inside it calls onPinClick, which is a plain DOM click (React-managed).
            <CircleMarker
              key={loc.locationCode}
              center={coords}
              radius={isAtRisk ? 9 : 7}
              pathOptions={{
                color, fillColor: color, fillOpacity: 0.85,
                weight: isAtRisk ? 2.5 : 1.5,
              }}
            >
              <Popup>
                <div style={{ fontFamily: "IBM Plex Sans", fontSize: 12, minWidth: 170 }}>
                  <div style={{ fontWeight: 700, marginBottom: 2 }}>{loc.displayName}</div>
                  <div style={{ color: "#888", fontSize: 11, marginBottom: 8 }}>{loc.city}, {loc.country}</div>
                  <span style={{
                    background: STATUS_BG[status], color,
                    padding: "2px 8px", borderRadius: 4, fontSize: 11, fontWeight: 600
                  }}>{status}</span>
                  {todayDF != null && (
                    <div style={{ marginTop: 8, fontSize: 11, color: "#666" }}>
                      <span style={{ fontFamily: "monospace" }}>{todayDF.effectiveCoverage} / {todayDF.minRequired}</span>
                      {" agents today"}
                    </div>
                  )}
                  {isAtRisk && (
                    <button
                      onClick={() => onPinClick(loc.locationCode)}
                      style={{
                        marginTop: 10, width: "100%",
                        background: "rgba(255,59,92,.1)", border: "1px solid rgba(255,59,92,.3)",
                        color: "#cc3050", borderRadius: 4, padding: "5px 0",
                        fontSize: 11, cursor: "pointer",
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

function SubstituteDrawer({
  locationCode, date, displayName
}: { locationCode: string; date: string; displayName: string }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [acceptedId, setAcceptedId] = useState<string | null>(null)
  const [acceptedName, setAcceptedName] = useState<string | null>(null)
  const [accepting, setAccepting] = useState<string | null>(null)

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
    try {
      await apiFetch("/api/wic/substitutes/accept", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          employeeId: s.employeeId,
          locationCode,
          date,
          shiftStart: null,
          shiftEnd: null,
          sourceType: s.sourceType,
        }),
      })
      setAcceptedId(s.employeeId)
      setAcceptedName(s.fullName ?? s.name)
      queryClient.invalidateQueries({ queryKey: ["forecast-overview"] })
      queryClient.invalidateQueries({ queryKey: ["subs-drawer", locationCode, date] })
    } finally {
      setAccepting(null)
    }
  }

  const sourceColor = (src: string) => {
    if (src === "BACKUP")    return { bg: "rgba(167,139,250,.15)", color: "var(--purple)" }
    if (src === "SSP")       return { bg: "rgba(59,126,255,.15)",  color: "var(--accent)" }
    if (src === "WIC_DONOR") return { bg: "rgba(0,210,160,.15)",   color: "var(--accent2)" }
    return { bg: "rgba(255,124,59,.15)", color: "var(--warn)" }
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
      <div>
        <div style={{ fontSize: 13, fontWeight: 600, color: "var(--text)" }}>{displayName}</div>
        <div style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>{date}</div>
      </div>

      {acceptedId && (
        <div style={{
          background: "rgba(34,208,122,.12)", border: "1px solid rgba(34,208,122,.3)",
          borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--green)"
        }}>
          {t("attendance.substitute.confirmed", { name: acceptedName, wic: displayName })}
        </div>
      )}

      {isLoading ? (
        <>
          {[1, 2, 3].map(i => <div key={i} className="skeleton" style={{ height: 60, borderRadius: 8 }} />)}
        </>
      ) : (subs ?? []).length === 0 ? (
        <div style={{ padding: 20, textAlign: "center", color: "var(--text3)", fontSize: 12 }}>
          {t("attendance.substitute.noCandidates")}
        </div>
      ) : (subs ?? []).map((s: any, i: number) => {
        const sc = sourceColor(s.sourceType)
        const isAccepted = acceptedId === s.employeeId
        return (
          <div key={i} style={{
            background: isAccepted ? "rgba(34,208,122,.06)" : "var(--card2)",
            border: `1px solid ${isAccepted ? "rgba(34,208,122,.3)" : "var(--border)"}`,
            borderRadius: 8, padding: "12px 14px"
          }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
              <div style={{ fontWeight: 600, fontSize: 12, color: "var(--text)" }}>{s.name ?? s.fullName}</div>
              <span style={{ ...sc, padding: "2px 7px", borderRadius: 4, fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 600 }}>
                {s.sourceType}
              </span>
            </div>
            <div style={{ display: "flex", gap: 12, marginTop: 6, fontSize: 11, color: "var(--text3)" }}>
              {s.distanceKm != null && <span>{t("attendance.substitute.distance", { km: s.distanceKm.toFixed(1) })}</span>}
              {s.reachabilityTier && <span>Tier {s.reachabilityTier}</span>}
              {s.score != null && <span style={{ fontFamily: "IBM Plex Mono", color: "var(--text2)" }}>Score: {s.score.toFixed(0)}</span>}
            </div>
            {!acceptedId && (
              <div style={{ marginTop: 10, display: "flex", justifyContent: "flex-end" }}>
                <button
                  onClick={() => handleAccept(s)}
                  disabled={accepting === s.employeeId}
                  style={{
                    background: "var(--green)", border: "none", color: "#fff",
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

// ── Existing stat + WIC card components (kept for detail section) ──────────────

function TLCard({ tl }: { tl: any }) {
  return (
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px" }}>
      <div style={{ fontWeight: 600, fontSize: 12, marginBottom: 10, color: "var(--text)", whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{tl.teamLeadName}</div>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "4px 8px", fontSize: 11 }}>
        <span style={{ color: "var(--text3)" }}>Total</span><span style={{ fontFamily: "IBM Plex Mono", textAlign: "right" }}>{tl.totalAgents}</span>
        <span style={{ color: "var(--text3)" }}>Working</span><span style={{ fontFamily: "IBM Plex Mono", color: "var(--green)", textAlign: "right" }}>{tl.working}</span>
        <span style={{ color: "var(--text3)" }}>On AL</span><span style={{ fontFamily: "IBM Plex Mono", color: "var(--accent)", textAlign: "right" }}>{tl.onAL}</span>
        <span style={{ color: "var(--text3)" }}>On SL</span><span style={{ fontFamily: "IBM Plex Mono", color: "var(--warn)", textAlign: "right" }}>{tl.onSL}</span>
        <span style={{ color: "var(--text3)" }}>WIC</span><span style={{ fontFamily: "IBM Plex Mono", color: "var(--blue-light)", textAlign: "right" }}>{tl.wicAssigned}</span>
      </div>
    </div>
  )
}

function WicCard({ card }: { card: any }) {
  const st = card.coverageStatus
  const color = st === "COVERED" ? "var(--green)" : st === "PARTIAL" ? "var(--warn)" : st === "CLOSED" ? "var(--text3)" : "var(--danger)"
  return (
    <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 6 }}>
        <div>
          <div style={{ fontWeight: 600, fontSize: 13, color: "var(--text)" }}>{card.displayName}</div>
          <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 2 }}>{card.city}</div>
        </div>
        <span style={{ fontSize: 10, fontWeight: 600, color, background: `${color}18`, padding: "2px 8px", borderRadius: 4, fontFamily: "IBM Plex Mono", whiteSpace: "nowrap" }}>{st}</span>
      </div>
      {card.assignedAgents?.length > 0 && (
        <div style={{ borderTop: "1px solid var(--border)", paddingTop: 6, marginTop: 4 }}>
          {card.assignedAgents.map((a: any) => (
            <div key={a.employeeId} style={{ display: "flex", justifyContent: "space-between", fontSize: 11, color: "var(--text2)", marginBottom: 2 }}>
              <span>{a.name}</span>
              <span style={{ fontFamily: "IBM Plex Mono", color: a.coverageMatch === "FULL" ? "var(--green)" : "var(--warn)" }}>
                {a.shiftStart === "SICK" || a.shiftEnd === "SICK" || a.shiftStart === "SL" || a.shiftEnd === "SL"
                  ? "SL"
                  : a.shiftStart === "AL" || a.shiftEnd === "AL"
                  ? "AL"
                  : `${a.shiftStart}–${a.shiftEnd}`}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ── Section header ─────────────────────────────────────────────────────────────

function SectionHeader({ title, count, color }: { title: string; count?: number; color?: string }) {
  return (
    <div style={{ fontSize: 11, fontWeight: 600, color: color ?? "var(--text2)", textTransform: "uppercase", letterSpacing: ".08em", marginBottom: 10, display: "flex", alignItems: "center", gap: 8 }}>
      {title}
      {count !== undefined && (
        <span style={{ fontSize: 10, background: `${color ?? "var(--text2)"}22`, color: color ?? "var(--text2)", padding: "1px 7px", borderRadius: 10, fontFamily: "IBM Plex Mono" }}>{count}</span>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export default function Overview() {
  const { t } = useTranslation()
  const [searchParams] = useSearchParams()
  const horizon = Math.max(1, Math.min(30, Number(searchParams.get("horizon")) || 14))
  const today = new Date().toISOString().split("T")[0]

  const [sheetOpen, setSheetOpen]       = useState(false)
  const [sheetCell, setSheetCell]       = useState<{ locationCode: string; date: string; displayName: string } | null>(null)
  const [showClosed, setShowClosed]     = useState(false)
  const [showDetailGrid, setShowDetailGrid] = useState(false)

  // ── Queries ──────────────────────────────────────────────────────────────────
  const { data: forecast, isLoading: forecastLoading } = useQuery({
    queryKey: ["forecast-overview", horizon],
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
    queryFn: () => apiFetch<any[]>(`/api/wic/cards?date=${today}`),
    staleTime: 3 * 60 * 1000,
    enabled: showDetailGrid,
  })

  // ── KPI derivations ───────────────────────────────────────────────────────────
  const totalOpen   = forecast?.filter(lf => lf.todayStatus !== "CLOSED").length ?? 0
  const atRiskToday = forecast?.filter(lf => lf.todayStatus === "UNCOVERED" || lf.todayStatus === "PARTIAL").length ?? 0
  const coveredToday = forecast?.filter(lf => lf.todayStatus === "COVERED").length ?? 0
  const coveragePct = totalOpen > 0 ? Math.round((coveredToday / totalOpen) * 100) : 0
  const absentToday  = briefing?.absences?.length ?? 0
  const closureRisk  = briefing?.nextAtRiskDays?.length ?? briefing?.gaps?.filter(g => g.gapDate !== today).length ?? 0
  const sickToday    = briefing?.absences?.filter((a: any) => (a.shiftType ?? "").toUpperCase() === "SL").length ?? 0
  const alToday      = briefing?.absences?.filter((a: any) => ["AL","HALF_AL"].includes((a.shiftType ?? "").toUpperCase())).length ?? 0
  const gapsToday    = briefing?.gaps?.filter(g => g.gapDate === today).length ?? 0
  const topAtRisk    = (forecast ?? [])
    .filter(lf => lf.todayStatus === "UNCOVERED" || lf.todayStatus === "PARTIAL")
    .map(lf => {
      const df = lf.forecast?.find(d => d.date === today)
      const gap = df ? Math.max(0, (df.minRequired ?? 0) - (df.effectiveCoverage ?? 0)) : 1
      return { name: lf.displayName, status: lf.todayStatus, gap }
    })
    .slice(0, 5)

  // ── Heatmap dates ─────────────────────────────────────────────────────────────
  const heatDates = Array.from({ length: horizon }, (_, i) => {
    const d = new Date(today); d.setDate(d.getDate() + i); return d.toISOString().split("T")[0]
  })

  const dayLabel = (d: string) => {
    const dt = new Date(d)
    const dow = ["Su","Mo","Tu","We","Th","Fr","Sa"][dt.getDay()]
    return { dow, day: dt.getDate().toString().padStart(2, "0") }
  }
  const isWeekend = (d: string) => { const dt = new Date(d); return dt.getDay() === 0 || dt.getDay() === 6 }

  // ── Risk radar (next 7 days, UNCOVERED only) ──────────────────────────────────
  const sevenOut = new Date(today); sevenOut.setDate(sevenOut.getDate() + 7)
  const sevenStr = sevenOut.toISOString().split("T")[0]
  const riskRadar = (forecast ?? []).flatMap(lf =>
    (lf.forecast ?? [])
      .filter(df => df.date > today && df.date <= sevenStr && (df.status === "UNCOVERED" || df.status === "PARTIAL"))
      .map(df => ({ locationCode: lf.locationCode, displayName: lf.displayName, date: df.date, status: df.status }))
  ).sort((a, b) => a.date.localeCompare(b.date)).slice(0, 20)

  // ── WIC detail grid ───────────────────────────────────────────────────────────
  const uncovered  = wicCards?.filter((c: any) => c.coverageStatus === "UNCOVERED") ?? []
  const partial    = wicCards?.filter((c: any) => c.coverageStatus === "PARTIAL") ?? []
  const covered    = wicCards?.filter((c: any) => c.coverageStatus === "COVERED") ?? []
  const closed     = wicCards?.filter((c: any) => c.coverageStatus === "CLOSED") ?? []

  const openCell = (lf: LocationForecast, date: string) => {
    setSheetCell({ locationCode: lf.locationCode, date, displayName: lf.displayName })
    setSheetOpen(true)
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      {/* ── Header ── */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h1 style={{ fontSize: 22, fontWeight: 600, color: "var(--text)" }}>{t("nav.overview")}</h1>
        <span style={{ fontSize: 11, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>{today}</span>
      </div>

      {/* ── A. KPI Row ── */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: 12 }}>
        <KpiCard label={t("overview.kpi.openToday")}    value={totalOpen}    color="var(--accent)"  isLoading={forecastLoading} />
        <KpiCard label={t("overview.kpi.atRiskToday")}  value={atRiskToday}  color="var(--warn)"    isLoading={forecastLoading} />
        <KpiCard label={t("overview.kpi.closureRisk")}  value={closureRisk}  color="var(--danger)"  isLoading={briefingLoading} />
        <KpiCard label={t("overview.kpi.absentToday")}  value={absentToday}  color="var(--yellow)"  isLoading={briefingLoading} />
        <KpiCard label={t("overview.kpi.coveragePct")}  value={forecastLoading ? null : `${coveragePct}%`} color="var(--green)" isLoading={forecastLoading} />
      </div>

      {/* ── B. Coverage Heatmap ── */}
      <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
        <div style={{ padding: "12px 16px", borderBottom: "1px solid var(--border)", display: "flex", justifyContent: "space-between" }}>
          <SectionHeader title={t("overview.heatmap.title")} />
          <div style={{ fontSize: 10, color: "var(--text3)", display: "flex", gap: 10, alignItems: "center" }}>
            {["COVERED","PARTIAL","UNCOVERED","CLOSED"].map(s => (
              <span key={s} style={{ display: "flex", alignItems: "center", gap: 4 }}>
                <span style={{ width: 8, height: 8, borderRadius: 2, background: STATUS_HEX[s], display: "inline-block" }} />
                {s.toLowerCase()}
              </span>
            ))}
          </div>
        </div>
        {forecastLoading ? (
          <div style={{ padding: 24, display: "flex", flexDirection: "column", gap: 8 }}>
            {[1,2,3,4,5].map(i => <div key={i} className="skeleton" style={{ height: 28 }} />)}
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table style={{ borderCollapse: "collapse", fontSize: 11, minWidth: "100%" }}>
              <thead>
                <tr style={{ background: "var(--card2)" }}>
                  <th style={{ padding: "8px 12px", textAlign: "left", fontSize: 10, fontWeight: 500, color: "var(--text3)", borderBottom: "1px solid var(--border)", minWidth: 180, position: "sticky", left: 0, background: "var(--card2)", zIndex: 2 }}>
                    {t("overview.heatmap.location")}
                  </th>
                  {heatDates.map(d => {
                    const { dow, day } = dayLabel(d)
                    const we = isWeekend(d)
                    const isToday = d === today
                    return (
                      <th key={d} style={{
                        padding: "6px 2px", textAlign: "center", minWidth: 38, maxWidth: 38,
                        borderBottom: "1px solid var(--border)",
                        background: isToday ? "rgba(59,126,255,.08)" : we ? "rgba(30,45,69,.12)" : "transparent",
                        color: isToday ? "var(--accent)" : "var(--text3)",
                      }}>
                        <div style={{ fontSize: 9, fontFamily: "IBM Plex Mono" }}>{dow}</div>
                        <div style={{ fontSize: 10, fontWeight: isToday ? 700 : 400 }}>{day}</div>
                      </th>
                    )
                  })}
                </tr>
              </thead>
              <tbody>
                {(forecast ?? []).map(lf => (
                  <tr key={lf.locationCode} style={{ borderBottom: "1px solid var(--border)" }}>
                    <td style={{
                      padding: "6px 12px", position: "sticky", left: 0,
                      background: "var(--card)", zIndex: 1, fontWeight: 500, fontSize: 11, color: "var(--text2)"
                    }}>
                      {lf.displayName}
                    </td>
                    {heatDates.map(d => {
                      const cell = lf.forecast?.find(df => df.date === d)
                      const status = cell?.status ?? "CLOSED"
                      const we = isWeekend(d)
                      return (
                        <td
                          key={d}
                          onClick={() => (status === "UNCOVERED" || status === "PARTIAL") && openCell(lf, d)}
                          title={`${lf.displayName} — ${d} — ${status}`}
                          style={{
                            padding: "2px",
                            background: we && status === "CLOSED" ? "rgba(30,45,69,.06)" : STATUS_BG[status] ?? "transparent",
                            cursor: (status === "UNCOVERED" || status === "PARTIAL") ? "pointer" : "default",
                          }}
                        >
                          <div style={{ width: 34, height: 22, margin: "0 auto", borderRadius: 3, background: STATUS_BG[status], display: "flex", alignItems: "center", justifyContent: "center" }}>
                            {d === today && <span style={{ width: 6, height: 6, borderRadius: "50%", background: STATUS_HEX[status] ?? "transparent", display: "inline-block" }} />}
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
      </div>

      {/* ── C. WIC Map + Side Panels ── */}
      <div>
        <SectionHeader title={t("overview.map.title")} />
        <div style={{ display: "flex", gap: 14, alignItems: "flex-start", flexWrap: "wrap" }}>

          {/* ── Left: Coverage Status ── */}
          <div style={{ flex: 1, minWidth: 220, display: "flex", flexDirection: "column", gap: 10 }}>
            {/* Big coverage % */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px", textAlign: "center" }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>Coverage</div>
              <div style={{ fontSize: 48, fontWeight: 700, fontFamily: "IBM Plex Mono", lineHeight: 1,
                color: coveragePct >= 80 ? "var(--green)" : coveragePct >= 50 ? "var(--warn)" : "var(--danger)" }}>
                {forecastLoading ? "—" : `${coveragePct}%`}
              </div>
              <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 6 }}>of open locations covered</div>
            </div>
            {/* Open / covered / at-risk counts */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
              {[
                { label: "Open today",   val: totalOpen,    color: "var(--accent)" },
                { label: "Covered",      val: coveredToday, color: "var(--green)"  },
                { label: "At risk",      val: atRiskToday,  color: "var(--danger)" },
              ].map(({ label, val, color }) => (
                <div key={label} style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <span style={{ fontSize: 11, color: "var(--text3)" }}>{label}</span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 16, color }}>{val}</span>
                </div>
              ))}
            </div>
            {/* Top at-risk locations */}
            {topAtRisk.length > 0 && (
              <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px" }}>
                <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--danger)", marginBottom: 8 }}>
                  At-risk today
                </div>
                {topAtRisk.map((loc, i) => (
                  <div key={i} style={{ display: "flex", justifyContent: "space-between", alignItems: "center",
                    padding: "5px 0", borderBottom: i < topAtRisk.length - 1 ? "1px solid var(--border)" : "none" }}>
                    <span style={{ fontSize: 11, color: "var(--text2)", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", maxWidth: 140 }}>
                      {loc.name}
                    </span>
                    <span style={{
                      fontSize: 10, fontFamily: "IBM Plex Mono", fontWeight: 700,
                      padding: "1px 6px", borderRadius: 4,
                      background: loc.status === "UNCOVERED" ? "rgba(255,59,92,.15)" : "rgba(255,124,59,.15)",
                      color: loc.status === "UNCOVERED" ? "var(--danger)" : "var(--warn)",
                    }}>
                      {loc.gap > 0 ? `-${loc.gap}` : "—"}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* ── Centre: Map ── */}
          <div style={{ flexShrink: 0, width: 520 }}>
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
              <div className="skeleton" style={{ height: 480, borderRadius: 8 }} />
            )}
          </div>

          {/* ── Right: Today in Numbers ── */}
          <div style={{ flex: 1, minWidth: 220, display: "flex", flexDirection: "column", gap: 10 }}>
            {/* Absent summary */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "16px", textAlign: "center" }}>
              <div style={{ fontSize: 10, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".08em", color: "var(--text3)", marginBottom: 6 }}>Absent today</div>
              <div style={{ fontSize: 48, fontWeight: 700, fontFamily: "IBM Plex Mono", lineHeight: 1, color: "var(--yellow)" }}>
                {briefingLoading ? "—" : absentToday}
              </div>
              <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 6 }}>agents not on shift</div>
            </div>
            {/* Absence breakdown */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
              {[
                { label: "Sick leave",    val: sickToday,   color: "var(--danger)" },
                { label: "Annual leave",  val: alToday,     color: "var(--accent)"  },
                { label: "Other absence", val: Math.max(0, absentToday - sickToday - alToday), color: "var(--text3)" },
              ].map(({ label, val, color }) => (
                <div key={label} style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <span style={{ fontSize: 11, color: "var(--text3)" }}>{label}</span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 16, color }}>{val}</span>
                </div>
              ))}
            </div>
            {/* WIC gaps + risk radar */}
            <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, padding: "12px 14px", display: "flex", flexDirection: "column", gap: 8 }}>
              {[
                { label: "Gaps today",         val: gapsToday,    color: gapsToday > 0 ? "var(--danger)" : "var(--green)" },
                { label: "At-risk next 7 days", val: riskRadar.length, color: riskRadar.length > 0 ? "var(--warn)" : "var(--green)" },
                { label: "Total WIC locations", val: (locations?.length ?? 0), color: "var(--text2)" },
              ].map(({ label, val, color }) => (
                <div key={label} style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                  <span style={{ fontSize: 11, color: "var(--text3)" }}>{label}</span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontWeight: 700, fontSize: 16, color }}>{val}</span>
                </div>
              ))}
            </div>
          </div>

        </div>
      </div>

      {/* ── D. Recommendations ── */}
      {(briefing?.gaps?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.recommendations.title")} count={briefing!.gaps.length} color="var(--danger)" />
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden" }}>
            {briefing!.gaps.map((gap, i) => (
              <div key={i} style={{
                display: "flex", alignItems: "center", justifyContent: "space-between",
                padding: "10px 14px", borderBottom: "1px solid var(--border)",
              }}>
                <div style={{ flex: 1 }}>
                  <div style={{ fontWeight: 600, fontSize: 12, color: "var(--text)" }}>{gap.displayName}</div>
                  <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 2, fontFamily: "IBM Plex Mono" }}>
                    {gap.agentCount}/{gap.minRequired} · {gap.gapDate}
                  </div>
                </div>
                {gap.bestSubstituteName && (
                  <div style={{ flex: 1, fontSize: 11, color: "var(--text2)", textAlign: "center" }}>
                    <span style={{ color: "var(--text3)" }}>{t("overview.recommendations.best")}: </span>
                    <span style={{ color: "var(--green)", fontWeight: 600 }}>{gap.bestSubstituteName}</span>
                    {gap.bestSubstituteDistanceKm != null && (
                      <span style={{ color: "var(--text3)", marginLeft: 4 }}>({gap.bestSubstituteDistanceKm.toFixed(0)} km)</span>
                    )}
                  </div>
                )}
                <button
                  onClick={() => {
                    setSheetCell({ locationCode: gap.locationCode, date: today, displayName: gap.displayName })
                    setSheetOpen(true)
                  }}
                  style={{
                    background: "rgba(255,59,92,.1)", border: "1px solid rgba(255,59,92,.25)",
                    color: "var(--danger)", padding: "5px 12px", borderRadius: 6,
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

      {/* ── E. Absence Feed ── */}
      {(briefing?.absences?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.absences.title")} count={briefing!.absences.length} color="var(--yellow)" />
          <div style={{ background: "var(--card)", border: "1px solid var(--border)", borderRadius: 8, overflow: "hidden", marginTop: 8 }}>
            {briefing!.absences.map((ab: any, i: number) => {
              const typeColors: Record<string, [string, string]> = {
                SL:      ["rgba(239,68,68,.15)",    "#ef4444"],
                AL:      ["rgba(59,130,246,.15)",   "#60a5fa"],
                HALF_AL: ["rgba(59,130,246,.10)",   "#93c5fd"],
                UL:      ["rgba(148,163,184,.12)",  "#94a3b8"],
                CD:      ["rgba(34,197,94,.15)",    "#22c55e"],
                PH:      ["rgba(148,163,184,.12)",  "#94a3b8"],
                LPH:     ["rgba(148,163,184,.12)",  "#94a3b8"],
              }
              const typeLabel: Record<string, string> = {
                SL:"SL", AL:"AL", HALF_AL:"½AL", UL:"UL", CD:"CD", PH:"PH", LPH:"PH"
              }
              const key = (ab.shiftType ?? "").toUpperCase()
              const [bg, fg] = typeColors[key] ?? ["rgba(148,163,184,.12)", "#94a3b8"]
              const label = typeLabel[key] ?? ab.shiftType
              const showRange = ab.firstDay && ab.lastDay && ab.firstDay !== ab.lastDay
              return (
                <div key={i} style={{
                  display: "flex", alignItems: "center", gap: 12, padding: "9px 14px",
                  borderBottom: "1px solid var(--border)", fontSize: 12
                }}>
                  <Users size={12} style={{ color: "var(--text3)", flexShrink: 0 }} />
                  <span style={{ fontWeight: 500, color: "var(--text)", minWidth: 160 }}>{ab.fullName}</span>
                  <span style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: "var(--text3)", minWidth: 72 }}>{ab.employeeId}</span>
                  <span style={{
                    background: bg, color: fg,
                    padding: "2px 7px", borderRadius: 4, fontSize: 10,
                    fontFamily: "IBM Plex Mono", fontWeight: 700, minWidth: 32, textAlign: "center"
                  }}>{label}</span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text2)", flex: 1 }}>
                    {ab.firstDay}
                    {showRange && <span style={{ color: "var(--text3)" }}> → {ab.lastDay}</span>}
                  </span>
                  <span style={{ fontFamily: "IBM Plex Mono", fontSize: 11, color: "var(--text3)", whiteSpace: "nowrap" }}>
                    {ab.totalDays}d
                    {ab.daysSoFar > 0 && ab.daysSoFar < ab.totalDays &&
                      <span style={{ color: "var(--text3)", fontSize: 10 }}> ({ab.daysSoFar} so far)</span>}
                  </span>
                  {ab.wicLocation && (
                    <span style={{ fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono" }}>{ab.wicLocation}</span>
                  )}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* ── F. Risk Radar ── */}
      {riskRadar.length > 0 && (
        <div>
          <SectionHeader title={t("overview.radar.title")} count={riskRadar.length} color="var(--danger)" />
          <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: 8 }}>
            {riskRadar.map((r, i) => (
              <div
                key={i}
                onClick={() => { setSheetCell({ locationCode: r.locationCode, date: r.date, displayName: r.displayName }); setSheetOpen(true) }}
                style={{
                  background: r.status === "UNCOVERED" ? "rgba(255,59,92,.06)" : "rgba(255,124,59,.06)",
                  border: `1px solid ${r.status === "UNCOVERED" ? "rgba(255,59,92,.25)" : "rgba(255,124,59,.25)"}`,
                  borderRadius: 8, padding: "10px 12px", cursor: "pointer"
                }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: 6, marginBottom: 4 }}>
                  <Calendar size={11} style={{ color: r.status === "UNCOVERED" ? "var(--danger)" : "var(--warn)" }} />
                  <span style={{ fontSize: 10, fontFamily: "IBM Plex Mono", color: r.status === "UNCOVERED" ? "var(--danger)" : "var(--warn)" }}>{r.date}</span>
                </div>
                <div style={{ fontSize: 12, fontWeight: 600, color: "var(--text)" }}>{r.displayName}</div>
                <div style={{ fontSize: 10, color: "var(--text3)", marginTop: 2, fontFamily: "IBM Plex Mono" }}>{r.status}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Team Lead Summary ── */}
      {(tls?.length ?? 0) > 0 && (
        <div>
          <SectionHeader title={t("overview.teamLeads")} />
          <div style={{ display: "grid", gridTemplateColumns: "repeat(6, 1fr)", gap: 10 }}>
            {tls?.map((tl: any) => <TLCard key={tl.teamLeadName} tl={tl} />)}
          </div>
        </div>
      )}

      {/* ── WIC Detail Grid (expandable) ── */}
      <div>
        <button
          onClick={() => setShowDetailGrid(!showDetailGrid)}
          style={{
            background: "var(--card)", border: "1px solid var(--border)", color: "var(--text3)",
            padding: "7px 14px", borderRadius: 6, fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono"
          }}
        >
          {showDetailGrid ? "▲" : "▼"} {t("overview.detailGrid")}
        </button>
        {showDetailGrid && (
          <div style={{ marginTop: 12, display: "flex", flexDirection: "column", gap: 16 }}>
            {cardsLoading && <div style={{ padding: 16, color: "var(--text3)", fontSize: 12 }}>Loading...</div>}
            {uncovered.length > 0 && (
              <div>
                <SectionHeader title={`Uncovered (${uncovered.length})`} color="var(--danger)" />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 10 }}>
                  {uncovered.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            {partial.length > 0 && (
              <div>
                <SectionHeader title={`Partial (${partial.length})`} color="var(--warn)" />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 10 }}>
                  {partial.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            {covered.length > 0 && (
              <div>
                <SectionHeader title={`Covered (${covered.length})`} color="var(--green)" />
                <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 10 }}>
                  {covered.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
                </div>
              </div>
            )}
            <div>
              <button onClick={() => setShowClosed(!showClosed)} style={{ background: "var(--card)", border: "1px solid var(--border)", color: "var(--text3)", padding: "5px 12px", borderRadius: 6, fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono" }}>
                {showClosed ? "Hide" : "Show"} Closed ({closed.length})
              </button>
              {showClosed && (
                <div style={{ display: "grid", gridTemplateColumns: "repeat(5,1fr)", gap: 10, marginTop: 10 }}>
                  {closed.map((c: any) => <WicCard key={c.locationCode} card={c} />)}
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* ── Substitute Sheet Drawer ── */}
      <Sheet isOpen={sheetOpen} onClose={() => setSheetOpen(false)} title={`${sheetCell?.displayName ?? ""} – ${sheetCell?.date ?? ""}`}>
        <div style={{ padding: 20 }}>
          <div style={{ fontSize: 15, fontWeight: 600, color: "var(--text)", marginBottom: 16 }}>
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
