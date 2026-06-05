import { useState, useRef, useEffect } from "react"

export default function MultiSelectFilter({ options, selected, onChange }) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState("")
  const ref = useRef(null)

  useEffect(() => {
    const h = e => { if (ref.current && !ref.current.contains(e.target)) setOpen(false) }
    document.addEventListener("mousedown", h)
    return () => document.removeEventListener("mousedown", h)
  }, [])

  const filtered = options.filter(o => o.toLowerCase().includes(search.toLowerCase()))

  const toggle = name => {
    if (selected.includes(name)) onChange(selected.filter(s => s !== name))
    else onChange([...selected, name])
  }

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button onClick={() => setOpen(!open)}
        style={{ background: "#181e2e", border: "1px solid rgba(255,255,255,.1)", color: "#e2e8f0", padding: "6px 12px", borderRadius: 6, fontSize: 12, cursor: "pointer", display: "flex", alignItems: "center", gap: 6, minWidth: 160 }}>
        {selected.length === 0 ? "All locations" : `${selected.length} selected`}
        <span style={{ marginLeft: "auto", color: "#8892a4" }}>▾</span>
      </button>
      {open && (
        <div style={{ position: "absolute", right: 0, top: "calc(100% + 4px)", background: "#181e2e", border: "1px solid rgba(255,255,255,.1)", borderRadius: 8, width: 220, zIndex: 500, overflow: "hidden", boxShadow: "0 8px 32px rgba(0,0,0,.5)" }}>
          <div style={{ padding: 8 }}>
            <input autoFocus value={search} onChange={e => setSearch(e.target.value)} placeholder="Search..."
              style={{ width: "100%", background: "#0f1117", border: "1px solid rgba(255,255,255,.1)", color: "#e2e8f0", padding: "5px 8px", borderRadius: 5, fontSize: 11, outline: "none" }} />
          </div>
          {selected.length > 0 && (
            <div onClick={() => onChange([])} style={{ padding: "5px 12px", fontSize: 11, color: "#60a5fa", cursor: "pointer", borderBottom: "1px solid rgba(255,255,255,.05)" }}>Clear all</div>
          )}
          <div style={{ maxHeight: 200, overflowY: "auto" }}>
            {filtered.map(o => (
              <div key={o} onClick={() => toggle(o)}
                style={{ padding: "7px 12px", fontSize: 12, cursor: "pointer", display: "flex", alignItems: "center", gap: 8, color: selected.includes(o) ? "#60a5fa" : "#e2e8f0" }}
                onMouseEnter={e => e.currentTarget.style.background = "rgba(255,255,255,.04)"}
                onMouseLeave={e => e.currentTarget.style.background = "transparent"}>
                <div style={{ width: 14, height: 14, borderRadius: 4, border: `1px solid ${selected.includes(o) ? "#60a5fa" : "rgba(255,255,255,.2)"}`, background: selected.includes(o) ? "#3b7eff" : "transparent", display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
                  {selected.includes(o) && <span style={{ color: "#fff", fontSize: 9 }}>✓</span>}
                </div>
                {o}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
