import { useState, useRef, useEffect, type CSSProperties } from "react"

export interface SearchableSelectOption {
  value: string
  label: string
}

interface SearchableSelectProps {
  options: SearchableSelectOption[]
  value: string
  onChange: (value: string) => void
  placeholder?: string
  required?: boolean
  className?: string
  style?: CSSProperties
}

export function SearchableSelect({ options, value, onChange, placeholder, className, style }: SearchableSelectProps) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState("")
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const h = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) { setOpen(false); setSearch("") } }
    document.addEventListener("mousedown", h)
    return () => document.removeEventListener("mousedown", h)
  }, [])

  const sorted = [...options].sort((a, b) => a.label.localeCompare(b.label))
  const filtered = sorted.filter(o => o.label.toLowerCase().includes(search.toLowerCase()))
  const selected = options.find(o => o.value === value)

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className={className}
        style={{
          ...style,
          textAlign: "left",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          gap: 6,
          cursor: "pointer",
        }}
      >
        <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap", color: selected ? undefined : "rgb(var(--text-secondary))" }}>
          {selected ? selected.label : placeholder}
        </span>
        <span style={{ flexShrink: 0, opacity: 0.6 }}>▾</span>
      </button>

      {open && (
        <div
          style={{
            position: "absolute", left: 0, right: 0, top: "calc(100% + 4px)", zIndex: 500,
            background: "rgb(var(--surface-raised))", border: "1px solid rgb(var(--border-default))",
            borderRadius: 8, overflow: "hidden", boxShadow: "0 8px 32px rgba(0,0,0,.5)",
          }}
        >
          <div style={{ padding: 8 }}>
            <input
              autoFocus
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search…"
              style={{
                width: "100%", background: "rgb(var(--surface-sunken))", border: "1px solid rgb(var(--border-default))",
                color: "rgb(var(--text-primary))", padding: "6px 8px", borderRadius: 5, fontSize: 12, outline: "none", boxSizing: "border-box",
              }}
            />
          </div>
          <div style={{ maxHeight: 220, overflowY: "auto" }}>
            {filtered.length === 0 && (
              <div style={{ padding: "8px 12px", fontSize: 12 }} className="text-ink-soft">No match</div>
            )}
            {filtered.map(o => (
              <div
                key={o.value}
                onClick={() => { onChange(o.value); setOpen(false); setSearch("") }}
                className="hover:bg-hovered"
                style={{
                  padding: "7px 12px", fontSize: 12, cursor: "pointer",
                  color: o.value === value ? "rgb(var(--st-info-fg))" : "rgb(var(--text-primary))",
                }}
              >
                {o.label}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
