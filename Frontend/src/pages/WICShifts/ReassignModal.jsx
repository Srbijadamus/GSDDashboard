import { useState } from "react"

export default function ReassignModal({ agent, currentLocation, locations, onSave, onClose }) {
  const [newLoc, setNewLoc] = useState(currentLocation)
  const [role, setRole] = useState(agent.role || "primary")
  const [isAL, setIsAL] = useState(agent.al || false)
  const [error, setError] = useState("")
  const [saving, setSaving] = useState(false)
  const [search, setSearch] = useState("")

  const filteredLocs = locations.filter(l => l.name.toLowerCase().includes(search.toLowerCase()))

  const save = async () => {
    setSaving(true)
    setError("")
    try {
      await onSave(agent.id, newLoc, role, isAL)
    } catch {
      setError("Failed to save. Please try again.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <div style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,.7)", zIndex: 2000, display: "flex", alignItems: "center", justifyContent: "center" }}
      onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{ background: "#181e2e", border: "1px solid rgba(255,255,255,.1)", borderRadius: 12, width: 400, overflow: "hidden" }}>
        <div style={{ padding: "16px 20px", borderBottom: "1px solid rgba(255,255,255,.07)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <div style={{ fontSize: 14, fontWeight: 700, color: "#e2e8f0" }}>{agent.name}</div>
            <div style={{ fontSize: 11, color: "#8892a4", marginTop: 2 }}>Currently: {currentLocation}</div>
          </div>
          <button onClick={onClose} style={{ background: "none", border: "none", color: "#8892a4", fontSize: 18, cursor: "pointer" }}>✕</button>
        </div>

        <div style={{ padding: 20, display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={{ fontSize: 11, color: "#8892a4", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>New Location</label>
            <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search location..."
              style={{ width: "100%", background: "#0f1117", border: "1px solid rgba(255,255,255,.1)", color: "#e2e8f0", padding: "7px 10px", borderRadius: 6, fontSize: 12, outline: "none", marginBottom: 6 }} />
            <div style={{ maxHeight: 160, overflowY: "auto", border: "1px solid rgba(255,255,255,.07)", borderRadius: 6, background: "#0f1117" }}>
              {filteredLocs.map(l => (
                <div key={l.id} onClick={() => setNewLoc(l.name)}
                  style={{ padding: "7px 12px", fontSize: 12, cursor: "pointer", color: newLoc === l.name ? "#60a5fa" : "#e2e8f0", background: newLoc === l.name ? "rgba(96,165,250,.1)" : "transparent", display: "flex", justifyContent: "space-between" }}
                  onMouseEnter={e => { if (newLoc !== l.name) e.currentTarget.style.background = "rgba(255,255,255,.03)" }}
                  onMouseLeave={e => { if (newLoc !== l.name) e.currentTarget.style.background = "transparent" }}>
                  <span>{l.name}</span>
                  <span style={{ fontSize: 10, color: "#8892a4", fontFamily: "IBM Plex Mono" }}>{l.required}</span>
                </div>
              ))}
            </div>
          </div>

          <div>
            <label style={{ fontSize: 11, color: "#8892a4", textTransform: "uppercase", letterSpacing: ".07em", display: "block", marginBottom: 6 }}>Role</label>
            <div style={{ display: "flex", gap: 8 }}>
              {["primary", "backup"].map(r => (
                <button key={r} onClick={() => setRole(r)} style={{ flex: 1, padding: "7px 0", borderRadius: 6, border: "1px solid rgba(255,255,255,.1)", background: role === r ? (r === "primary" ? "rgba(96,165,250,.15)" : "rgba(192,132,252,.15)") : "transparent", color: role === r ? (r === "primary" ? "#60a5fa" : "#c084fc") : "#8892a4", cursor: "pointer", fontSize: 12, fontWeight: 600, textTransform: "uppercase", letterSpacing: ".06em" }}>{r}</button>
              ))}
            </div>
          </div>

          <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
            <div onClick={() => setIsAL(!isAL)} style={{ width: 36, height: 20, borderRadius: 10, background: isAL ? "#f97316" : "rgba(255,255,255,.1)", position: "relative", transition: "background .2s", cursor: "pointer" }}>
              <div style={{ position: "absolute", top: 2, left: isAL ? 18 : 2, width: 16, height: 16, borderRadius: "50%", background: "#fff", transition: "left .2s" }} />
            </div>
            <span style={{ fontSize: 12, color: isAL ? "#f97316" : "#8892a4" }}>Mark as Annual Leave</span>
          </label>

          {error && <div style={{ fontSize: 12, color: "#ef4444" }}>{error}</div>}
        </div>

        <div style={{ display: "flex", gap: 8, padding: "14px 20px", borderTop: "1px solid rgba(255,255,255,.07)", justifyContent: "flex-end" }}>
          <button onClick={onClose} style={{ padding: "8px 16px", borderRadius: 6, border: "1px solid rgba(255,255,255,.1)", background: "transparent", color: "#8892a4", cursor: "pointer", fontSize: 12 }}>Cancel</button>
          <button onClick={save} disabled={saving} style={{ padding: "8px 16px", borderRadius: 6, border: "none", background: "#3b7eff", color: "#fff", cursor: "pointer", fontSize: 12, fontWeight: 600, opacity: saving ? .6 : 1 }}>{saving ? "Saving..." : "Save"}</button>
        </div>
      </div>
    </div>
  )
}
