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
        style={{ background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--border-default))", color: "rgb(var(--text-primary))", padding: "6px 12px", borderRadius: 6, fontSize: 12, cursor: "pointer", display: "flex", alignItems: "center", gap: 6, minWidth: 160 }}>
        {selected.length === 0 ? "All locations" : `${selected.length} selected`}
        <span style={{ marginLeft: "auto", color: "rgb(var(--text-secondary))" }}>▾</span>
      </button>
      {open && (
        <div style={{ position: "absolute", right: 0, top: "calc(100% + 4px)", background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--border-default))", borderRadius: 8, width: 220, zIndex: 500, overflow: "hidden", boxShadow: "0 8px 32px rgba(0,0,0,.5)" }}>
          <div style={{ padding: 8 }}>
            <input autoFocus value={search} onChange={e => setSearch(e.target.value)} placeholder="Search..."
              style={{ width: "100%", background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--border-default))", color: "rgb(var(--text-primary))", padding: "5px 8px", borderRadius: 5, fontSize: 11, outline: "none" }} />
          </div>
          {selected.length > 0 && (
            <div onClick={() => onChange([])} style={{ padding: "5px 12px", fontSize: 11, color: "rgb(var(--st-info-fg))", cursor: "pointer", borderBottom: "1px solid rgb(var(--border-subtle))" }}>Clear all</div>
          )}
          <div style={{ maxHeight: 200, overflowY: "auto" }}>
            {filtered.map(o => (
              <div key={o} onClick={() => toggle(o)}
                className="hover:bg-hovered"
                style={{ padding: "7px 12px", fontSize: 12, cursor: "pointer", display: "flex", alignItems: "center", gap: 8, color: selected.includes(o) ? "rgb(var(--st-info-fg))" : "rgb(var(--text-primary))" }}>
                <div style={{ width: 14, height: 14, borderRadius: 4, border: `1px solid ${selected.includes(o) ? "rgb(var(--st-info-fg))" : "rgb(var(--border-default))"}`, background: selected.includes(o) ? "rgb(var(--st-info-solid))" : "transparent", display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
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
