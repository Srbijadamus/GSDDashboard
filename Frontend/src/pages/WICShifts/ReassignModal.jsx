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
    if (s === "SL")       return "rgb(var(--st-crit-solid))"
    if (s === "AL")       return "rgb(var(--st-warn-solid))"
    if (s === "Training") return "rgb(var(--st-learn-solid))"
    if (s === "OFF")      return "rgb(var(--text-secondary))"
    return "rgb(var(--st-info-solid))"
  }

  const specialBg = s => {
    if (s === "SL")       return "rgb(var(--st-crit-bg))"
    if (s === "AL")       return "rgb(var(--st-warn-bg))"
    if (s === "Training") return "rgb(var(--st-learn-bg))"
    if (s === "OFF")      return "rgb(var(--st-neutral-bg))"
    return "rgb(var(--st-info-bg))"
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
      <div style={{ background:"rgb(var(--surface-sunken))", border:"1px solid rgb(var(--line-subtle))", borderRadius:12, width:400, overflow:"hidden" }}>
        <div style={{ padding:"16px 20px", borderBottom:"1px solid rgb(var(--line-subtle))", display:"flex", justifyContent:"space-between", alignItems:"center" }}>
          <div>
            <div style={{ fontSize:14, fontWeight:700, color:"rgb(var(--text-primary))" }}>{agent.name}</div>
            <div style={{ fontSize:11, color:"rgb(var(--text-secondary))", marginTop:2 }}>Currently: {currentLocation}</div>
          </div>
          <button onClick={onClose} style={{ background:"none", border:"none", color:"rgb(var(--text-secondary))", fontSize:18, cursor:"pointer" }}>X</button>
        </div>
        <div style={{ padding:20, display:"flex", flexDirection:"column", gap:14 }}>
          <div>
            <label style={{ fontSize:11, color:"rgb(var(--text-secondary))", textTransform:"uppercase", letterSpacing:".07em", display:"block", marginBottom:6 }}>New Location / Status</label>
            <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Search location or status..."
              style={{ width:"100%", background:"rgb(var(--surface-sunken))", border:"1px solid rgb(var(--line-subtle))", color:"rgb(var(--text-primary))", padding:"7px 10px", borderRadius:6, fontSize:12, outline:"none", marginBottom:6, boxSizing:"border-box" }} />
            <div style={{ maxHeight:220, overflowY:"auto", border:"1px solid rgb(var(--line-subtle))", borderRadius:6, background:"rgb(var(--surface-sunken))" }}>
              {filteredSpecial.length > 0 && (
                <div style={{ padding:"4px 8px", fontSize:10, color:"rgb(var(--text-secondary))", textTransform:"uppercase", letterSpacing:".07em", borderBottom:"1px solid rgb(var(--line-subtle))" }}>Status</div>
              )}
              {filteredSpecial.map(s => (
                <div key={s} onClick={() => setNewLoc(s)}
                  className={newLoc !== s ? "hover:bg-white/[.03]" : ""}
                  style={{ padding:"8px 12px", fontSize:12, cursor:"pointer", color: newLoc === s ? specialColor(s) : "rgb(var(--text-primary))", background: newLoc === s ? specialBg(s) : undefined, display:"flex", alignItems:"center", gap:8 }}>
                  <span style={{ width:8, height:8, borderRadius:"50%", background:specialColor(s), flexShrink:0 }} />
                  {s}
                </div>
              ))}
              {filteredLocs.length > 0 && (
                <div style={{ padding:"4px 8px", fontSize:10, color:"rgb(var(--text-secondary))", textTransform:"uppercase", letterSpacing:".07em", borderBottom:"1px solid rgb(var(--line-subtle))", borderTop: filteredSpecial.length > 0 ? "1px solid rgb(var(--line-subtle))" : "none" }}>WIC Locations</div>
              )}
              {filteredLocs.map(l => (
                <div key={l.id} onClick={() => setNewLoc(l.name)}
                  className={newLoc !== l.name ? "hover:bg-white/[.03]" : ""}
                  style={{ padding:"8px 12px", fontSize:12, cursor:"pointer", color: newLoc === l.name ? "rgb(var(--st-info-solid))" : "rgb(var(--text-primary))", background: newLoc === l.name ? "rgb(var(--st-info-bg))" : undefined, display:"flex", justifyContent:"space-between" }}>
                  <span>{l.name}</span>
                  <span className="font-mono" style={{ fontSize:10, color:"rgb(var(--text-secondary))" }}>{l.required}</span>
                </div>
              ))}
            </div>
          </div>
          {!SPECIAL.includes(newLoc) && (
            <div>
              <label style={{ fontSize:11, color:"rgb(var(--text-secondary))", textTransform:"uppercase", letterSpacing:".07em", display:"block", marginBottom:6 }}>Role</label>
              <div style={{ display:"flex", gap:8 }}>
                {["primary","backup"].map(r => (
                  <button key={r} onClick={() => setRole(r)} style={{ flex:1, padding:"7px 0", borderRadius:6, border:"1px solid rgb(var(--line-subtle))", background: role === r ? (r === "primary" ? "rgb(var(--st-info-bg))" : "rgb(var(--st-learn-bg))") : "transparent", color: role === r ? (r === "primary" ? "rgb(var(--st-info-solid))" : "rgb(var(--st-learn-solid))") : "rgb(var(--text-secondary))", cursor:"pointer", fontSize:12, fontWeight:600, textTransform:"uppercase" }}>{r}</button>
                ))}
              </div>
            </div>
          )}
          {error && <div style={{ fontSize:12, color:"rgb(var(--st-crit-solid))" }}>{error}</div>}
        </div>
        <div style={{ display:"flex", gap:8, padding:"14px 20px", borderTop:"1px solid rgb(var(--line-subtle))", justifyContent:"flex-end" }}>
          <button onClick={onClose} style={{ padding:"8px 16px", borderRadius:6, border:"1px solid rgb(var(--line-subtle))", background:"transparent", color:"rgb(var(--text-secondary))", cursor:"pointer", fontSize:12 }}>Cancel</button>
          <button onClick={save} disabled={saving} style={{ padding:"8px 16px", borderRadius:6, border:"none", background:"rgb(var(--st-info-solid))", color:"#fff", cursor:"pointer", fontSize:12, fontWeight:600, opacity: saving ? .6 : 1 }}>{saving ? "Saving..." : "Save"}</button>
        </div>
      </div>
    </div>
  )
}
