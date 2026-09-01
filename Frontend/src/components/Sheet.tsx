import { useEffect } from "react"
import { X } from "lucide-react"

interface SheetProps {
  isOpen: boolean
  onClose: () => void
  title: string
  children: React.ReactNode
}

export function Sheet({ isOpen, onClose, title, children }: SheetProps) {
  useEffect(() => {
    const handler = (e: KeyboardEvent) => { if (e.key === "Escape") onClose() }
    if (isOpen) document.addEventListener("keydown", handler)
    return () => document.removeEventListener("keydown", handler)
  }, [isOpen, onClose])

  return (
    <>
      <div
        onClick={onClose}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0, 0, 0, 0.45)",
          zIndex: 40,
          opacity: isOpen ? 1 : 0,
          pointerEvents: isOpen ? "auto" : "none",
          transition: "opacity 0.2s ease",
        }}
      />
      <div
        className="bg-raised border-l border-line-subtle"
        style={{
          position: "fixed", top: 0, right: 0, bottom: 0,
          width: 440, maxWidth: "92vw",
          zIndex: 50,
          transform: isOpen ? "translateX(0)" : "translateX(100%)",
          transition: "transform 0.25s ease",
          display: "flex", flexDirection: "column",
          overflowY: "hidden",
        }}
      >
        <div className="border-b border-line-subtle" style={{
          display: "flex", alignItems: "center", justifyContent: "space-between",
          padding: "14px 20px",
          flexShrink: 0,
        }}>
          <span className="text-ink" style={{ fontSize: 14, fontWeight: 600 }}>{title}</span>
          <button
            onClick={onClose}
            className="text-ink-soft"
            style={{
              background: "none", border: "none", cursor: "pointer",
              display: "flex", alignItems: "center",
              padding: 4, borderRadius: 4,
            }}
          >
            <X size={16} />
          </button>
        </div>
        <div style={{ flex: 1, overflowY: "auto", padding: "16px 20px" }}>
          {children}
        </div>
      </div>
    </>
  )
}
