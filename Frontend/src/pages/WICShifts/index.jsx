import { useState, useEffect, useCallback, useRef } from "react"
import { useTranslation } from "react-i18next"
import UncoveredBanner from "./UncoveredBanner"
import LocationCard from "./LocationCard"
import MultiSelectFilter from "./MultiSelectFilter"
import ReassignModal from "./ReassignModal"
import AvailableHoursPanel from './AvailableHoursPanel'
import NewShiftModal from "./NewShiftModal"

const BASE = ""
const POLL_INTERVAL = 30000

export default function WICShifts() {
  const { t } = useTranslation()
  const [locations, setLocations] = useState([])
  const [filtered, setFiltered] = useState([])
  const [selectedLocs, setSelectedLocs] = useState([])
  const [modalAgent, setModalAgent] = useState(null)
  const [newShiftOpen, setNewShiftOpen] = useState(false)
  const [secondsAgo, setSecondsAgo] = useState(0)
  const [connectionLost, setConnectionLost] = useState(false)
  const [polling, setPolling] = useState(false)
  const [dragAgent, setDragAgent] = useState(null)
  const intervalRef = useRef(null)
  const tickRef = useRef(null)
  const today = new Date().toISOString().split("T")[0]

  const fetchData = useCallback(async () => {
    setPolling(true)
    try {
      const [cardsRes, shiftsRes] = await Promise.all([
        fetch(`${BASE}/api/wic/cards?date=${today}`),
        fetch(`${BASE}/api/wic?from=${today}&to=${today}`)
      ])
      if (!cardsRes.ok || !shiftsRes.ok) throw new Error("fetch failed")
      const cards = await cardsRes.json()
      const shifts = await shiftsRes.json()

      const ABSENT_TYPES = ['SL','AL','UL','OL','PH','LPH','RESIGNED','OFF','OFF_WEEKEND']
      const agentsByLoc = {}
      shifts.forEach(s => {
        if (!s.supportLocation) return
        if (!s.isOnSite) return
        if (!agentsByLoc[s.supportLocation]) agentsByLoc[s.supportLocation] = []
        const absent = ABSENT_TYPES.includes(s.agentStatus) ||
          (s.agentStatus === 'WORKING' && (s.task === 'GSD' || s.task === 'Backlog'))
        agentsByLoc[s.supportLocation].push({
          id: s.id,
          employeeId: s.employeeId ?? null,
          name: s.fullName ?? s.employeeId,
          role: "primary",
          time: s.workingShift ?? null,
          al: s.agentStatus === 'AL',
          agentStatus: absent ? (s.agentStatus || s.task || 'OFF') : null,
          absent,
          assignedTo: s.supportLocation
        })
      })

      const INACTIVE = ['SL','AL','OFF','OFF_WEEKEND','PH']
      const newLocations = cards
        .filter(c => !c.todaySchedule?.isClosed)
        .map(c => {
          const shiftAgents = agentsByLoc[c.displayName] ?? []
          const cardAgents = (c.assignedAgents ?? []).map(a => ({
            id: a.employeeId ?? a.name,
            employeeId: a.employeeId ?? null,
            name: a.name,
            role: a.isMain ? 'primary' : 'backup',
            time: a.shiftStart === 'SICK' ? null : (a.shiftStart && a.shiftEnd ? a.shiftStart + ' - ' + a.shiftEnd : null),
            al: false, agentStatus: a.shiftStart === 'SICK' ? 'SL' : a.shiftStart === 'AL' ? 'AL' : null, assignedTo: c.displayName
          }))
          const agents = shiftAgents.length > 0 ? shiftAgents : cardAgents
          const status = c.coverageStatus?.toLowerCase() === 'covered' ? 'covered'
            : c.coverageStatus?.toLowerCase() === 'partial' ? 'partial' : 'uncovered'
          return {
            id: c.locationCode, name: c.displayName, city: c.city, country: c.country,
            required: c.todaySchedule?.rawSchedule ?? '', status, agents, isNpp: c.isNpp ?? false,
            mainAgents: c.mainAgents ?? [], backupAgents: c.backupAgents ?? []
          }
        })
      setLocations(newLocations)
      setSecondsAgo(0)
      setConnectionLost(false)
    } catch {
      setConnectionLost(true)
    } finally {
      setPolling(false)
    }
  }, [today])

  useEffect(() => {
    fetchData()
    intervalRef.current = setInterval(fetchData, POLL_INTERVAL)
    tickRef.current = setInterval(() => setSecondsAgo(s => s + 1), 1000)
    return () => { clearInterval(intervalRef.current); clearInterval(tickRef.current) }
  }, [fetchData])

  useEffect(() => {
    setFiltered(selectedLocs.length === 0 ? locations : locations.filter(l => selectedLocs.includes(l.name)))
  }, [locations, selectedLocs])

  const covered   = locations.filter(l => l.status === "covered").length
  const partial   = locations.filter(l => l.status === "partial").length
  const uncovered = locations.filter(l => l.status === "uncovered").length

  const SPECIAL_STATUSES = ["SL","AL","Training","OFF","GSD"]

  async function handleReassign(agentId, newLocation, role, isAL) {
    setLocations(prev => prev.map(loc => ({
      ...loc,
      agents: loc.agents.map(a => a.id === agentId ? { ...a, assignedTo: newLocation, role, al: isAL } : a)
    })))
    try {
      await fetch(`${BASE}/api/wic/${agentId}`, {
        method: "PATCH", headers: { "Content-Type": "application/json" },
        body: JSON.stringify(
          SPECIAL_STATUSES.includes(newLocation)
            ? { task: newLocation, supportLocation: modalAgent?.currentLocation }
            : { task: "WIC", supportLocation: newLocation }
        )
      })
    } catch { showToast("Failed to reassign agent"); fetchData() }
    setModalAgent(null)
  }

  async function handleDrop(agentId, newLocationName) {
    if (!agentId || !newLocationName) return
    setLocations(prev => {
      let agent = null
      const updated = prev.map(loc => {
        const found = loc.agents.find(a => a.id === agentId)
        if (found) { agent = { ...found, assignedTo: newLocationName }; return { ...loc, agents: loc.agents.filter(a => a.id !== agentId) } }
        return loc
      })
      if (!agent) return prev
      return updated.map(loc => loc.name === newLocationName ? { ...loc, agents: [...loc.agents, agent] } : loc)
    })
    try {
      await fetch(`${BASE}/api/wic/${agentId}`, {
        method: "PATCH", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ task: "WIC", supportLocation: newLocationName })
      })
    } catch { showToast("Failed to reassign agent"); fetchData() }
  }

  function showToast(msg) {
    const t = document.createElement("div")
    t.textContent = msg
    t.style.cssText = "position:fixed;bottom:24px;right:24px;background:rgb(var(--st-crit-solid));color:#fff;padding:10px 18px;border-radius:8px;font-size:12px;z-index:9999"
    document.body.appendChild(t)
    setTimeout(() => t.remove(), 3000)
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 0, minHeight: "100%" }}>
      <UncoveredBanner locations={locations.filter(l => l.status === "uncovered")} />
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "14px 0 12px", gap: 12, flexWrap: "wrap" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: "rgb(var(--text-primary))", margin: 0, display: "flex", alignItems: "center", gap: 8 }}>
            WIC Shifts
            {polling && <span style={{ width: 6, height: 6, borderRadius: "50%", background: "rgb(var(--st-info-solid))", display: "inline-block", animation: "pulse 1s infinite" }} />}
          </h1>
          <div style={{ display: "flex", gap: 6 }}>
            <Pill status="good" label={`${covered} Covered`} />
            <Pill status="warn" label={`${partial} Partial`} />
            <Pill status="crit" label={`${uncovered} Uncovered`} />
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          {connectionLost && <span style={{ fontSize: 11, color: "rgb(var(--st-warn-solid))", background: "rgb(var(--st-warn-bg))", padding: "3px 10px", borderRadius: 20, border: "1px solid rgb(var(--st-warn-bd))" }}>Connection lost</span>}
          <span className="font-mono" style={{ fontSize: 11, color: "rgb(var(--text-secondary))" }}>Updated {secondsAgo}s ago</span>
          <button
            onClick={() => setNewShiftOpen(true)}
            className="bg-info-solid"
            style={{
              border: "none", color: "#fff",
              padding: "7px 14px", borderRadius: 6, fontSize: 12,
              cursor: "pointer", fontWeight: 600,
            }}
          >
            + {t("wicShifts.newShift.button")}
          </button>
          <MultiSelectFilter options={locations.map(l => l.name)} selected={selectedLocs} onChange={setSelectedLocs} />
        </div>
      </div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))", gap: 10 }}>
        {filtered.map(loc => (
          <LocationCard key={loc.id} location={loc}
            onAgentClick={agent => setModalAgent({ agent, currentLocation: loc.name })}
            onDrop={handleDrop} dragAgent={dragAgent} setDragAgent={setDragAgent}
            onActionSuccess={fetchData} shiftDate={today} />
        ))}
      </div>
      {modalAgent && (
        <ReassignModal agent={modalAgent.agent} currentLocation={modalAgent.currentLocation}
          locations={locations} onSave={handleReassign} onClose={() => setModalAgent(null)} shiftDate={today} />
      )}
      <NewShiftModal isOpen={newShiftOpen} onClose={() => setNewShiftOpen(false)} onSuccess={fetchData} />
      <AvailableHoursPanel />
      <style>{`@keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.3} }`}</style>
    </div>
  )
}

function Pill({ status, label }) {
  return (
    <span className="font-mono" style={{ fontSize: 11, color: `rgb(var(--st-${status}-solid))`, background: `rgb(var(--st-${status}-bg))`, border: `1px solid rgb(var(--st-${status}-bd))`, padding: "3px 10px", borderRadius: 20 }}>{label}</span>
  )
}
