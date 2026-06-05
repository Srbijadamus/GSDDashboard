import { useState, useEffect, useCallback, useRef } from "react"
import UncoveredBanner from "./UncoveredBanner"
import LocationCard from "./LocationCard"
import MultiSelectFilter from "./MultiSelectFilter"
import ReassignModal from "./ReassignModal"
import { mockData } from "./mockData"

const BASE = "https://n8jlr9dr-5000.euw.devtunnels.ms"
const POLL_INTERVAL = 30000

function statusDot(status) {
  if (status === "covered") return "#22c55e"
  if (status === "partial") return "#f97316"
  return "#ef4444"
}

export default function WICShifts() {
  const [locations, setLocations] = useState(mockData)
  const [filtered, setFiltered] = useState([])
  const [selectedLocs, setSelectedLocs] = useState([])
  const [modalAgent, setModalAgent] = useState(null)
  const [lastUpdated, setLastUpdated] = useState(0)
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
      const res = await fetch(`${BASE}/api/wic/cards?date=${today}`)
      if (!res.ok) throw new Error("fetch failed")
      const data = await res.json()
      if (data && data.length > 0) {
        setLocations(prev => mergeData(prev, data))
      }
      setLastUpdated(Date.now())
      setSecondsAgo(0)
      setConnectionLost(false)
    } catch {
      setConnectionLost(true)
    } finally {
      setPolling(false)
    }
  }, [today])

  function mergeData(prev, incoming) {
    return prev.map(loc => {
      const match = incoming.find(i => i.displayName === loc.name || i.locationCode === loc.id)
      if (!match) return loc
      return { ...loc, status: (match.coverageStatus || "uncovered").toLowerCase() }
    })
  }

  useEffect(() => {
    fetchData()
    intervalRef.current = setInterval(fetchData, POLL_INTERVAL)
    tickRef.current = setInterval(() => setSecondsAgo(s => s + 1), 1000)
    return () => {
      clearInterval(intervalRef.current)
      clearInterval(tickRef.current)
    }
  }, [fetchData])

  useEffect(() => {
    if (selectedLocs.length === 0) {
      setFiltered(locations)
    } else {
      setFiltered(locations.filter(l => selectedLocs.includes(l.name)))
    }
  }, [locations, selectedLocs])

  const covered = locations.filter(l => l.status === "covered").length
  const partial = locations.filter(l => l.status === "partial").length
  const uncovered = locations.filter(l => l.status === "uncovered").length
  const uncoveredList = locations.filter(l => l.status === "uncovered")

  async function handleReassign(agentId, newLocation, role, isAL) {
    const body = { task: "WIC", supportLocation: newLocation }
    setLocations(prev => prev.map(loc => ({
      ...loc,
      agents: loc.agents.map(a => a.id === agentId
        ? { ...a, assignedTo: newLocation, role, al: isAL, time: isAL ? null : a.time }
        : a
      )
    })))
    try {
      const res = await fetch(`${BASE}/api/wic/${agentId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      })
      if (!res.ok) throw new Error("patch failed")
    } catch {
      showToast(`Failed to reassign agent`)
      fetchData()
    }
    setModalAgent(null)
  }

  async function handleDrop(agentId, newLocationName) {
    if (!agentId || !newLocationName) return
    const body = { task: "WIC", supportLocation: newLocationName }
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
      const res = await fetch(`${BASE}/api/wic/${agentId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body)
      })
      if (!res.ok) throw new Error()
    } catch {
      showToast(`Failed to reassign agent`)
      fetchData()
    }
  }

  function showToast(msg) {
    const t = document.createElement("div")
    t.textContent = msg
    t.style.cssText = "position:fixed;bottom:24px;right:24px;background:#ef4444;color:#fff;padding:10px 18px;border-radius:8px;font-size:12px;z-index:9999;font-family:IBM Plex Sans,sans-serif"
    document.body.appendChild(t)
    setTimeout(() => t.remove(), 3000)
  }

  const locNames = locations.map(l => l.name)

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 0, minHeight: "100%" }}>
      <UncoveredBanner locations={uncoveredList} />

      {/* TOOLBAR */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "14px 0 12px", gap: 12, flexWrap: "wrap" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: "#e2e8f0", margin: 0, display: "flex", alignItems: "center", gap: 8 }}>
            WIC Shifts
            {polling && <span style={{ width: 6, height: 6, borderRadius: "50%", background: "#60a5fa", display: "inline-block", animation: "pulse 1s infinite" }} />}
          </h1>
          <div style={{ display: "flex", gap: 6 }}>
            <Pill color="#22c55e" label={`${covered} Covered`} />
            <Pill color="#f97316" label={`${partial} Partial`} />
            <Pill color="#ef4444" label={`${uncovered} Uncovered`} />
          </div>
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          {connectionLost && (
            <span style={{ fontSize: 11, color: "#f97316", background: "rgba(249,115,22,.12)", padding: "3px 10px", borderRadius: 20, border: "1px solid rgba(249,115,22,.3)" }}>
              Connection lost
            </span>
          )}
          <span style={{ fontSize: 11, color: "#8892a4", fontFamily: "IBM Plex Mono" }}>
            Updated {secondsAgo}s ago
          </span>
          <MultiSelectFilter options={locNames} selected={selectedLocs} onChange={setSelectedLocs} />
        </div>
      </div>

      {/* CARD GRID */}
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))", gap: 10 }}>
        {filtered.map(loc => (
          <LocationCard
            key={loc.id}
            location={loc}
            onAgentClick={agent => setModalAgent({ agent, currentLocation: loc.name })}
            onDrop={handleDrop}
            dragAgent={dragAgent}
            setDragAgent={setDragAgent}
          />
        ))}
      </div>

      {modalAgent && (
        <ReassignModal
          agent={modalAgent.agent}
          currentLocation={modalAgent.currentLocation}
          locations={locations}
          onSave={handleReassign}
          onClose={() => setModalAgent(null)}
        />
      )}

      <style>{`
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.3} }
      `}</style>
    </div>
  )
}

function Pill({ color, label }) {
  return (
    <span style={{ fontSize: 11, color, background: color + "1a", border: `1px solid ${color}44`, padding: "3px 10px", borderRadius: 20, fontFamily: "IBM Plex Mono" }}>
      {label}
    </span>
  )
}
