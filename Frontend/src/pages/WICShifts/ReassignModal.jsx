import { useState } from "react"

const SPECIAL = ["SL", "AL", "Training", "OFF", "GSD"]

export default function ReassignModal({ agent, currentLocation, locations, onSave, onClose }) {
  const [newLoc, setNewLoc] = useState(currentLocation)
  const [role, setRole] = useState(agent.role || "primary")
  const [error, setError] = useState("")
  const [saving, setSaving] = useState(false)
  const [search, setSearch] = useState("")

  const filteredLocs = locations.filter(l => l.name.toLowerCase().includes(search.toLowerCase()))
  const filteredSpecial = SPECIAL.filter(s => s.toLowerCase().includes(search.toLowerCase()))

  const specialColor = s => {
    if (s === "SL")       return "#ef4444"
    if (s === "AL")       return "#f97316"
    if (s === "Training") return "#c084fc"
    if (s === "OFF")      return "#8892a4"
    return "#60a5fa"
  }

  const save = async () => {
    setSaving(true)
    setError("")
    try {
      await onSave(agent.id, newLoc, role, newLoc === "AL")
    } catch {
      setError("Failed to save. Please try again.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <div style={{ position:"fixed", inset:0, background:"rgba(0,0,0,.7)", zIndex:2000, display:"flex", alignItems:"center", justifyContent:"center" }}
      onClick={e => e.target === e.currentTarget && onClose()}>
      <div style={{ background:"#181e2e", border:"1px solid rgba(255,255,255,.1)", borderRadius:12, width:400, overflow:"hidden" }}>
        <div style={{ padding:"16px 20px", borderBottom:"1px solid rgba(255,255,255,.07)", display:"flex", justifyContent:"space-between", alignItems:"center" }}>
          <div>
            <div style={{ fontSize:14, fontWeight:700, color:"#e2e8f0" }}>{agent.name}</div>
            <div style={{ fontSize:11, color:"#8892a4", marginTop:2 }}>Currently: {currentLocation}</div>
          </div>
          <button onClick={onClose} style={{ background:"none", border:"none", color:"#8892a4", fontSize:18, cursor:"pointer" }}>X</button>
        </div>
        <div style={{ padding:20, display:"flex", flexDirection:"column", gap:14 }}>
          <div>
            <label style={{ fontSize:11, color:"#8892a4", textTransform:"uppercase", letterSpacing:".07em", display:"block", marginBottom:6 }}>New Location / Status</label>
            <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search location or status..."
              style={{ width:"100%", background:"#0f1117", border:"1px solid rgba(255,255,255,.1)", color:"#e2e8f0", padding:"7px 10px", borderRadius:6, fontSize:12, outline:"none", marginBottom:6, boxSizing:"border-box" }} />
            <div style={{ maxHeight:220, overflowY:"auto", border:"1px solid rgba(255,255,255,.07)", borderRadius:6, background:"#0f1117" }}>
              {filteredSpecial.length > 0 && (
                <div style={{ padding:"4px 8px", fontSize:10, color:"#8892a4", textTransform:"uppercase", letterSpacing:".07em", borderBottom:"1px solid rgba(255,255,255,.05)" }}>Status</div>
              )}
              {filteredSpecial.map(s => (
                <div key={s} onClick={() => setNewLoc(s)}
                  style={{ padding:"8px 12px", fontSize:12, cursor:"pointer", color: newLoc === s ? specialColor(s) : "#e2e8f0", background: newLoc === s ? specialColor(s) + "18" : "transparent", display:"flex", alignItems:"center", gap:8 }}
                  onMouseEnter={e => { if (newLoc !== s) e.currentTarget.style.background = "rgba(255,255,255,.03)" }}
                  onMouseLeave={e => { if (newLoc !== s) e.currentTarget.style.background = "transparent" }}>
                  <span style={{ width:8, height:8, borderRadius:"50%", background:specialColor(s), flexShrink:0 }} />
                  {s}
                </div>
              ))}
              {filteredLocs.length > 0 && (
                <div style={{ padding:"4px 8px", fontSize:10, color:"#8892a4", textTransform:"uppercase", letterSpacing:".07em", borderBottom:"1px solid rgba(255,255,255,.05)", borderTop: filteredSpecial.length > 0 ? "1px solid rgba(255,255,255,.05)" : "none" }}>WIC Locations</div>
              )}
              {filteredLocs.map(l => (
                <div key={l.id} onClick={() => setNewLoc(l.name)}
                  style={{ padding:"8px 12px", fontSize:12, cursor:"pointer", color: newLoc === l.name ? "#60a5fa" : "#e2e8f0", background: newLoc === l.name ? "rgba(96,165,250,.1)" : "transparent", display:"flex", justifyContent:"space-between" }}
                  onMouseEnter={e => { if (newLoc !== l.name) e.currentTarget.style.background = "rgba(255,255,255,.03)" }}
                  onMouseLeave={e => { if (newLoc !== l.name) e.currentTarget.style.background = "transparent" }}>
                  <span>{l.name}</span>
                  <span style={{ fontSize:10, color:"#8892a4", fontFamily:"IBM Plex Mono" }}>{l.required}</span>
                </div>
              ))}
            </div>
          </div>
          {!SPECIAL.includes(newLoc) && (
            <div>
              <label style={{ fontSize:11, color:"#8892a4", textTransform:"uppercase", letterSpacing:".07em", display:"block", marginBottom:6 }}>Role</label>
              <div style={{ display:"flex", gap:8 }}>
                {["primary","backup"].map(r => (
                  <button key={r} onClick={() => setRole(r)} style={{ flex:1, padding:"7px 0", borderRadius:6, border:"1px solid rgba(255,255,255,.1)", background: role === r ? (r === "primary" ? "rgba(96,165,250,.15)" : "rgba(192,132,252,.15)") : "transparent", color: role === r ? (r === "primary" ? "#60a5fa" : "#c084fc") : "#8892a4", cursor:"pointer", fontSize:12, fontWeight:600, textTransform:"uppercase" }}>{r}</button>
                ))}
              </div>
            </div>
          )}
          {error && <div style={{ fontSize:12, color:"#ef4444" }}>{error}</div>}
        </div>
        <div style={{ display:"flex", gap:8, padding:"14px 20px", borderTop:"1px solid rgba(255,255,255,.07)", justifyContent:"flex-end" }}>
          <button onClick={onClose} style={{ padding:"8px 16px", borderRadius:6, border:"1px solid rgba(255,255,255,.1)", background:"transparent", color:"#8892a4", cursor:"pointer", fontSize:12 }}>Cancel</button>
          <button onClick={save} disabled={saving} style={{ padding:"8px 16px", borderRadius:6, border:"none", background:"#3b7eff", color:"#fff", cursor:"pointer", fontSize:12, fontWeight:600, opacity: saving ? .6 : 1 }}>{saving ? "Saving..." : "Save"}</button>
        </div>
      </div>
    </div>
  )
}
