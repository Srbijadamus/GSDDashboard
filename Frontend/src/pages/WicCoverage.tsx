import { useState } from "react"
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { MapPin, Users, Car, Search, ChevronRight, Shield, ShieldCheck, ShieldAlert, Globe } from "lucide-react"
import { apiFetch, api } from "../api/client"
import { Sheet } from "../components/Sheet"
import { NppBadge } from "../components/NppBadge"

// ─── Types ────────────────────────────────────────────────────────────────────

interface AgentTierDto {
  name:       string
  hasCar:     boolean | null
  employeeId: string | null
  primaryKid: string | null
}

interface WicRoleDto {
  locationCode:   string
  displayName:    string
  assignmentType: string
}

interface AgentCoverageDto {
  employeeId:      string
  primaryKid:      string | null
  secondaryKid:    string | null
  fullName:        string | null
  infosysEmail:    string | null
  eonEmail:        string | null
  hasCar:          boolean | null
  groupRegion:     string | null
  reachableCities: string[]
  wicRoles:        WicRoleDto[]
}

interface WicListItemDto {
  locationCode: string
  displayName:  string
  city:         string | null
  bundesland:   string | null
  openingDay:   string | null
  isNpp:        boolean
  mainCount:    number
  backupACount: number
}

interface WicCoverageDto {
  locationCode: string
  displayName:  string
  city:         string | null
  bundesland:   string | null
  openingDay:   string | null
  comment:      string | null
  fullAddress:  string | null
  isNpp:        boolean
  main:         AgentTierDto[]
  backupA:      AgentTierDto[]
  backupB:      AgentTierDto[]
  backupC:      AgentTierDto[]
}

// ─── Small helpers ────────────────────────────────────────────────────────────

function initials(name: string | null) {
  if (!name) return "?"
  return name.split(" ").map(p => p[0]).slice(0, 2).join("").toUpperCase()
}

function AgentAvatar({ name }: { name: string | null }) {
  return (
    <div className="w-9 h-9 rounded-full flex-shrink-0 bg-[var(--accent)] text-white flex items-center justify-center text-sm font-bold">
      {initials(name)}
    </div>
  )
}

function CarBadge({ hasCar }: { hasCar: boolean | null }) {
  const { t } = useTranslation()
  if (hasCar === true)
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-green-100 text-green-600 dark:bg-green-900/30 dark:text-green-400">
        <Car size={9} /> {t("wicCoverage.hasCarYes")}
      </span>
    )
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[10px] font-semibold bg-[var(--card2)] text-[var(--text3)]">
      <Car size={9} /> {hasCar === false ? t("wicCoverage.hasCarNo") : t("wicCoverage.hasCarUnknown")}
    </span>
  )
}

function TypeBadge({ type }: { type: string }) {
  const cls: Record<string, string> = {
    MAIN:     "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
    BACKUP:   "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
    REGIONAL: "bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300",
  }
  return (
    <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded-full ${cls[type] ?? "bg-[var(--card2)] text-[var(--text2)]"}`}>
      {type}
    </span>
  )
}

function Pill({ label }: { label: string }) {
  return (
    <span className="bg-[var(--card2)] text-[var(--text2)] border border-[var(--border)] rounded-full px-2 py-0.5 text-[10px]">
      {label}
    </span>
  )
}

// ─── Agent card ───────────────────────────────────────────────────────────────

function AgentCard({ agent, onClick }: { agent: AgentCoverageDto; onClick: () => void }) {
  return (
    <div
      onClick={onClick}
      className="bg-[var(--card)] border border-[var(--border)] rounded-xl p-4 cursor-pointer flex flex-col gap-2.5 hover:border-[var(--accent)] transition-colors"
    >
      <div className="flex items-center gap-2.5">
        <AgentAvatar name={agent.fullName} />
        <div className="flex-1 min-w-0">
          <div className="font-semibold text-[13px] text-[var(--text)] truncate">
            {agent.fullName ?? agent.employeeId}
          </div>
          <div className="text-[10px] text-[var(--text3)] font-mono">
            {agent.primaryKid ?? "—"} · {agent.secondaryKid ?? "—"}
          </div>
        </div>
        <CarBadge hasCar={agent.hasCar} />
      </div>

      {agent.groupRegion && (
        <div className="flex items-center gap-1 text-[11px] text-[var(--text2)]">
          <Globe size={11} />
          {agent.groupRegion}
        </div>
      )}

      {agent.reachableCities.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {agent.reachableCities.map(c => <Pill key={c} label={c} />)}
        </div>
      )}

      {agent.wicRoles.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {agent.wicRoles.map(r => (
            <span key={r.locationCode + r.assignmentType} className="flex items-center gap-1">
              <TypeBadge type={r.assignmentType} />
              <span className="text-[10px] text-[var(--text2)]">{r.displayName}</span>
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

// ─── Agent detail (Sheet content) ────────────────────────────────────────────

function AgentDetail({ kid }: { kid: string }) {
  const { t } = useTranslation()
  const qc = useQueryClient()

  const { data: agent } = useQuery<AgentCoverageDto>({
    queryKey: ["wic-coverage-agent", kid],
    queryFn:  () => apiFetch<AgentCoverageDto>(`/api/wic-coverage/agents/${encodeURIComponent(kid)}`),
  })

  const [regionEdit, setRegionEdit] = useState("")

  const patch = useMutation({
    mutationFn: (body: { hasCar?: boolean | null; groupRegion?: string }) =>
      api.wicCoverage.patchAgent(kid, body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["wic-coverage-agent", kid] })
      qc.invalidateQueries({ queryKey: ["wic-coverage-agents"] })
    },
  })

  if (!agent)
    return <div className="text-[var(--text3)] text-sm">{t("wicCoverage.loading")}</div>

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2.5">
        <AgentAvatar name={agent.fullName} />
        <div className="flex-1">
          <div className="font-bold text-[15px] text-[var(--text)]">{agent.fullName}</div>
          <div className="text-[11px] text-[var(--text3)] font-mono">
            {agent.primaryKid} · {agent.secondaryKid}
          </div>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2 text-xs">
        <div>
          <div className="text-[var(--text3)] mb-0.5">Infosys Email</div>
          <div className="text-[var(--text)] break-all">{agent.infosysEmail ?? "—"}</div>
        </div>
        <div>
          <div className="text-[var(--text3)] mb-0.5">EON Email</div>
          <div className="text-[var(--text)] break-all">{agent.eonEmail ?? "—"}</div>
        </div>
      </div>

      <div>
        <div className="text-[11px] text-[var(--text3)] mb-1.5">{t("wicCoverage.hasCarLabel")}</div>
        <div className="flex gap-1.5">
          {(["true", "false", "null"] as const).map(v => {
            const label = v === "true" ? t("wicCoverage.hasCarYes") : v === "false" ? t("wicCoverage.hasCarNo") : t("wicCoverage.hasCarUnknown")
            const active = String(agent.hasCar) === v
            return (
              <button
                key={v}
                onClick={() => patch.mutate({ hasCar: v === "null" ? null : v === "true" })}
                className={`px-2.5 py-1 rounded-md text-[11px] cursor-pointer border border-[var(--border)] transition-colors ${active ? "bg-[var(--accent)] text-white" : "bg-[var(--card2)] text-[var(--text2)]"}`}
              >{label}</button>
            )
          })}
        </div>
      </div>

      <div>
        <div className="text-[11px] text-[var(--text3)] mb-1.5">{t("wicCoverage.region")}</div>
        <div className="flex gap-1.5">
          <input
            className="flex-1 px-2 py-1.5 rounded-md border border-[var(--border)] bg-[var(--card2)] text-[var(--text)] text-xs outline-none"
            placeholder={agent.groupRegion ?? t("wicCoverage.regionPlaceholder")}
            value={regionEdit}
            onChange={e => setRegionEdit(e.target.value)}
            onKeyDown={e => { if (e.key === "Enter") { patch.mutate({ groupRegion: regionEdit }); setRegionEdit("") } }}
          />
          <button
            onClick={() => { patch.mutate({ groupRegion: regionEdit }); setRegionEdit("") }}
            className="px-2.5 py-1.5 rounded-md text-[11px] cursor-pointer bg-[var(--accent)] text-white border-none"
          >{t("wicCoverage.save")}</button>
        </div>
      </div>

      {agent.reachableCities.length > 0 && (
        <div>
          <div className="text-[11px] text-[var(--text3)] mb-1.5">{t("wicCoverage.reachableCities")}</div>
          <div className="flex flex-wrap gap-1">
            {agent.reachableCities.map(c => <Pill key={c} label={c} />)}
          </div>
        </div>
      )}

      {agent.wicRoles.length > 0 && (
        <div>
          <div className="text-[11px] text-[var(--text3)] mb-1.5">{t("wicCoverage.wicRoles")}</div>
          <div className="flex flex-col gap-1">
            {agent.wicRoles.map(r => (
              <div key={r.locationCode + r.assignmentType}
                className="flex items-center gap-2 px-2.5 py-1.5 bg-[var(--card2)] rounded-md text-xs">
                <TypeBadge type={r.assignmentType} />
                <span className="text-[var(--text)]">{r.displayName}</span>
                <span className="text-[var(--text3)] text-[10px] ml-auto">{r.locationCode}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

// ─── WIC card (list item) ─────────────────────────────────────────────────────

function WicCard({ wic, onClick }: { wic: WicListItemDto; onClick: () => void }) {
  const { t } = useTranslation()
  return (
    <div
      onClick={onClick}
      className="bg-[var(--card)] border border-[var(--border)] rounded-xl p-4 cursor-pointer flex flex-col gap-2 hover:border-[var(--accent)] transition-colors"
    >
      <div className="flex items-start gap-2.5">
        <div className="w-8 h-8 rounded-lg flex-shrink-0 bg-[var(--accent)]/10 text-[var(--accent)] flex items-center justify-center">
          <MapPin size={16} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="font-semibold text-[13px] text-[var(--text)] flex items-center gap-1.5">
            {wic.displayName}
            {wic.isNpp && <NppBadge />}
          </div>
          <div className="text-[10px] text-[var(--text3)]">
            {[wic.city, wic.bundesland].filter(Boolean).join(" · ")}
          </div>
        </div>
        <ChevronRight size={14} className="text-[var(--text3)] flex-shrink-0" />
      </div>

      {wic.openingDay && (
        <div className="text-[11px] text-[var(--text2)]">{wic.openingDay}</div>
      )}

      <div className="flex gap-2 text-[11px] text-[var(--text2)]">
        <span className="flex items-center gap-1">
          <ShieldCheck size={11} className="text-green-600" />
          {t("wicCoverage.mainCount", { n: wic.mainCount })}
        </span>
        <span className="flex items-center gap-1">
          <Shield size={11} />
          {t("wicCoverage.backupACount", { n: wic.backupACount })}
        </span>
      </div>
    </div>
  )
}

// ─── Tier section ─────────────────────────────────────────────────────────────

function TierSection({ title, agents, accent, icon }: {
  title:   string
  agents:  AgentTierDto[]
  accent:  string
  icon:    React.ReactNode
}) {
  if (agents.length === 0) return null
  return (
    <div>
      <div className="flex items-center gap-1.5 mb-2">
        {icon}
        <span className={`text-[11px] font-semibold uppercase tracking-wide ${accent}`}>{title}</span>
      </div>
      <div className="flex flex-col gap-1">
        {agents.map(a => (
          <div key={a.name} className="flex items-center gap-2 px-2.5 py-1.5 bg-[var(--card2)] rounded-md">
            <AgentAvatar name={a.name} />
            <div className="flex-1 min-w-0">
              <div className="text-xs font-medium text-[var(--text)] truncate">{a.name}</div>
              {a.primaryKid && (
                <div className="text-[10px] text-[var(--text3)] font-mono">{a.primaryKid}</div>
              )}
            </div>
            <CarBadge hasCar={a.hasCar} />
          </div>
        ))}
      </div>
    </div>
  )
}

// ─── WIC detail (Sheet content) ───────────────────────────────────────────────

function WicDetail({ locationCode }: { locationCode: string }) {
  const { t } = useTranslation()
  const qc = useQueryClient()

  const { data: wic } = useQuery<WicCoverageDto>({
    queryKey: ["wic-coverage-wic", locationCode],
    queryFn:  () => apiFetch<WicCoverageDto>(`/api/wic-coverage/wics/${encodeURIComponent(locationCode)}`),
  })

  const { data: reachable } = useQuery<AgentTierDto[]>({
    queryKey: ["wic-coverage-reachable", locationCode],
    queryFn:  () => apiFetch<AgentTierDto[]>(`/api/wic-coverage/wics/${encodeURIComponent(locationCode)}/reachable-agents`),
  })

  const pin = useMutation({
    mutationFn: (name: string) =>
      api.wicCoverage.pinBackupB(locationCode, { employeeName: name }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["wic-coverage-wic", locationCode] })
      qc.invalidateQueries({ queryKey: ["wic-coverage-wics"] })
    },
  })

  if (!wic)
    return <div className="text-[var(--text3)] text-sm">{t("wicCoverage.loading")}</div>

  const backupBExtra = reachable?.filter(
    r => !wic.backupB.some(b => b.name.toLowerCase() === r.name.toLowerCase())
  ) ?? []

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-start gap-2.5">
        <div className="w-9 h-9 rounded-lg flex-shrink-0 bg-[var(--accent)]/10 text-[var(--accent)] flex items-center justify-center">
          <MapPin size={18} />
        </div>
        <div className="flex-1">
          <div className="font-bold text-[15px] text-[var(--text)] flex items-center gap-2">
            {wic.displayName}
            {wic.isNpp && <NppBadge />}
          </div>
          <div className="text-[11px] text-[var(--text3)]">
            {[wic.city, wic.bundesland].filter(Boolean).join(" · ")}
          </div>
        </div>
      </div>

      {wic.openingDay && (
        <div className="px-3 py-2 bg-[var(--card2)] rounded-lg text-xs text-[var(--text)]">
          <span className="text-[var(--text3)] mr-1.5">{t("wicCoverage.openingDays")}:</span>
          {wic.openingDay}
        </div>
      )}

      {wic.fullAddress && (
        <div className="text-[11px] text-[var(--text2)]">{wic.fullAddress}</div>
      )}

      <TierSection
        title={t("wicCoverage.main")}
        agents={wic.main}
        accent="text-green-600 dark:text-green-400"
        icon={<ShieldCheck size={13} className="text-green-600 dark:text-green-400" />}
      />

      <TierSection
        title={t("wicCoverage.backupA")}
        agents={wic.backupA}
        accent="text-blue-600 dark:text-blue-400"
        icon={<Shield size={13} className="text-blue-600 dark:text-blue-400" />}
      />

      {(wic.backupB.length > 0 || backupBExtra.length > 0) && (
        <div>
          <div className="flex items-center gap-1.5 mb-2">
            <ShieldAlert size={13} className="text-amber-600 dark:text-amber-400" />
            <span className="text-[11px] font-semibold uppercase tracking-wide text-amber-600 dark:text-amber-400">
              {t("wicCoverage.backupB")}
            </span>
          </div>
          <div className="flex flex-col gap-1">
            {wic.backupB.map(a => (
              <div key={a.name} className="flex items-center gap-2 px-2.5 py-1.5 bg-[var(--card2)] rounded-md">
                <AgentAvatar name={a.name} />
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-medium text-[var(--text)] truncate">{a.name}</div>
                </div>
                <CarBadge hasCar={a.hasCar} />
                <button
                  onClick={() => pin.mutate(a.name)}
                  className="px-2 py-1 text-[10px] rounded cursor-pointer bg-[var(--accent)] text-white border-none flex-shrink-0"
                >{t("wicCoverage.pinNote")}</button>
              </div>
            ))}
            {backupBExtra.map(a => (
              <div key={a.name} className="flex items-center gap-2 px-2.5 py-1.5 bg-[var(--card2)] rounded-md opacity-70">
                <AgentAvatar name={a.name} />
                <div className="flex-1 min-w-0">
                  <div className="text-xs font-medium text-[var(--text)] truncate">{a.name}</div>
                </div>
                <CarBadge hasCar={a.hasCar} />
                <button
                  onClick={() => pin.mutate(a.name)}
                  className="px-2 py-1 text-[10px] rounded cursor-pointer bg-[var(--accent)] text-white border-none flex-shrink-0"
                >{t("wicCoverage.pinNote")}</button>
              </div>
            ))}
          </div>
        </div>
      )}

      <TierSection
        title={t("wicCoverage.backupC")}
        agents={wic.backupC}
        accent="text-purple-700 dark:text-purple-400"
        icon={<Shield size={13} className="text-purple-700 dark:text-purple-400" />}
      />

      {wic.comment && (
        <div className="px-3 py-2.5 rounded-lg bg-amber-50 border border-amber-200 dark:bg-amber-900/20 dark:border-amber-800/50 text-xs text-[var(--text)]">
          <span className="font-semibold text-amber-700 dark:text-amber-400 mr-1.5">{t("wicCoverage.note")}:</span>
          {wic.comment}
        </div>
      )}
    </div>
  )
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function WicCoverage() {
  const { t } = useTranslation()
  const [tab, setTab] = useState<"agents" | "wics">("wics")
  const [search, setSearch] = useState("")
  const [selectedAgent, setSelectedAgent] = useState<string | null>(null)
  const [selectedWic, setSelectedWic] = useState<string | null>(null)
  const [filterCar, setFilterCar] = useState(false)

  const { data: agents = [], isLoading: loadingAgents } = useQuery<AgentCoverageDto[]>({
    queryKey: ["wic-coverage-agents", search],
    queryFn:  () => apiFetch<AgentCoverageDto[]>(`/api/wic-coverage/agents?search=${encodeURIComponent(search)}`),
    enabled:  tab === "agents",
  })

  const { data: wics = [], isLoading: loadingWics } = useQuery<WicListItemDto[]>({
    queryKey: ["wic-coverage-wics", search],
    queryFn:  () => apiFetch<WicListItemDto[]>(`/api/wic-coverage/wics?search=${encodeURIComponent(search)}`),
    enabled:  tab === "wics",
  })

  const filteredAgents = filterCar ? agents.filter(a => a.hasCar === true) : agents

  function openAgent(kid: string) {
    setSelectedAgent(kid)
    setSelectedWic(null)
  }

  function openWic(code: string) {
    setSelectedWic(code)
    setSelectedAgent(null)
  }

  return (
    <div className="flex flex-col h-[calc(100vh-60px)] overflow-hidden">

      {/* Header */}
      <div className="flex flex-col gap-3 pb-4">
        <h2 className="m-0 text-[17px] font-bold text-[var(--text)]">{t("wicCoverage.title")}</h2>

        {/* Tab toggle */}
        <div className="flex gap-1 bg-[var(--card2)] p-0.5 rounded-lg w-fit">
          {(["wics", "agents"] as const).map(tabId => (
            <button
              key={tabId}
              onClick={() => { setTab(tabId); setSelectedAgent(null); setSelectedWic(null) }}
              className={`flex items-center gap-1.5 px-3.5 py-1.5 rounded-md text-xs font-semibold border-none cursor-pointer transition-colors ${tab === tabId ? "bg-[var(--accent)] text-white" : "bg-transparent text-[var(--text2)]"}`}
            >
              {tabId === "wics"
                ? <><MapPin size={12} />{t("wicCoverage.tabWics")}</>
                : <><Users size={12} />{t("wicCoverage.tabAgents")}</>}
            </button>
          ))}
        </div>

        {/* Search + filters */}
        <div className="flex gap-2">
          <div className="flex-1 relative">
            <Search size={13} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--text3)] pointer-events-none" />
            <input
              className="w-full pl-7 pr-2.5 py-1.5 rounded-lg border border-[var(--border)] bg-[var(--card2)] text-[var(--text)] text-xs outline-none box-border"
              placeholder={tab === "wics" ? t("wicCoverage.searchWics") : t("wicCoverage.searchAgents")}
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          {tab === "agents" && (
            <button
              onClick={() => setFilterCar(f => !f)}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] cursor-pointer border border-[var(--border)] transition-colors ${filterCar ? "bg-[var(--accent)] text-white" : "bg-[var(--card2)] text-[var(--text2)]"}`}
            >
              <Car size={12} /> {t("wicCoverage.filterCar")}
            </button>
          )}
        </div>
      </div>

      {/* Grid */}
      <div className="flex-1 overflow-y-auto">
        {tab === "wics" && (
          loadingWics
            ? <div className="p-5 text-sm text-[var(--text3)]">{t("wicCoverage.loading")}</div>
            : <div className="grid gap-3" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))" }}>
                {wics.map(w => (
                  <WicCard key={w.locationCode} wic={w} onClick={() => openWic(w.locationCode)} />
                ))}
              </div>
        )}

        {tab === "agents" && (
          loadingAgents
            ? <div className="p-5 text-sm text-[var(--text3)]">{t("wicCoverage.loading")}</div>
            : <div className="grid gap-3" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(260px, 1fr))" }}>
                {filteredAgents.map(a => (
                  <AgentCard
                    key={a.employeeId}
                    agent={a}
                    onClick={() => openAgent(a.primaryKid ?? a.employeeId)}
                  />
                ))}
              </div>
        )}
      </div>

      {/* Agent detail — Sheet */}
      <Sheet
        isOpen={selectedAgent !== null}
        onClose={() => setSelectedAgent(null)}
        title={t("wicCoverage.agentDetail")}
      >
        {selectedAgent && <AgentDetail kid={selectedAgent} />}
      </Sheet>

      {/* WIC detail — Sheet */}
      <Sheet
        isOpen={selectedWic !== null}
        onClose={() => setSelectedWic(null)}
        title={t("wicCoverage.wicDetail")}
      >
        {selectedWic && <WicDetail locationCode={selectedWic} />}
      </Sheet>
    </div>
  )
}
