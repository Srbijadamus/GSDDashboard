import { useState, useEffect, useRef } from "react"
import { useNavigate } from "react-router-dom"
import { useQuery } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { Search } from "lucide-react"
import { apiFetch } from "../api/client"

interface WicLocation {
  locationCode: string
  displayName: string
  city: string
  country: string
}

interface Props {
  isOpen: boolean
  onClose: () => void
}

export function CommandPalette({ isOpen, onClose }: Props) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [query, setQuery] = useState("")
  const [cursor, setCursor] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const { data: locations } = useQuery({
    queryKey: ["cmd-locations"],
    queryFn: () => apiFetch<WicLocation[]>("/api/wic/locations"),
    staleTime: 10 * 60 * 1000,
    enabled: isOpen,
  })

  const filtered = (locations ?? [])
    .filter(l =>
      !query ||
      l.displayName?.toLowerCase().includes(query.toLowerCase()) ||
      l.locationCode?.toLowerCase().includes(query.toLowerCase()) ||
      l.city?.toLowerCase().includes(query.toLowerCase())
    )
    .slice(0, 9)

  useEffect(() => {
    if (isOpen) {
      setQuery("")
      setCursor(0)
      setTimeout(() => inputRef.current?.focus(), 50)
    }
  }, [isOpen])

  useEffect(() => { setCursor(0) }, [query])

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!isOpen) return
      if (e.key === "Escape") { onClose(); return }
      if (e.key === "ArrowDown") { e.preventDefault(); setCursor(c => Math.min(c + 1, filtered.length - 1)) }
      if (e.key === "ArrowUp")   { e.preventDefault(); setCursor(c => Math.max(c - 1, 0)) }
      if (e.key === "Enter" && filtered[cursor]) {
        navigate(`/wic-attendance?location=${encodeURIComponent(filtered[cursor].locationCode)}`)
        onClose()
      }
    }
    document.addEventListener("keydown", handler)
    return () => document.removeEventListener("keydown", handler)
  }, [isOpen, cursor, filtered, navigate, onClose])

  if (!isOpen) return null

  const goTo = (code: string) => {
    navigate(`/wic-attendance?location=${encodeURIComponent(code)}`)
    onClose()
  }

  return (
    <>
      <div
        onClick={onClose}
        style={{ position: "fixed", inset: 0, background: "rgba(0,0,0,0.55)", zIndex: 100 }}
      />
      <div style={{
        position: "fixed", top: "18%", left: "50%", transform: "translateX(-50%)",
        width: 520, zIndex: 101,
        background: "var(--sidebar)", border: "1px solid var(--border)", borderRadius: 12,
        overflow: "hidden", boxShadow: "0 20px 60px rgba(0,0,0,0.4)"
      }}>
        <div style={{
          display: "flex", alignItems: "center", gap: 10,
          padding: "12px 16px", borderBottom: "1px solid var(--border)"
        }}>
          <Search size={15} style={{ color: "var(--text3)", flexShrink: 0 }} />
          <input
            ref={inputRef}
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder={t("nav.cmdPlaceholder")}
            style={{
              flex: 1, background: "none", border: "none", outline: "none",
              fontSize: 14, color: "var(--text)", fontFamily: "IBM Plex Sans",
            }}
          />
          <kbd style={{
            fontSize: 10, color: "var(--text3)", fontFamily: "IBM Plex Mono",
            background: "var(--card2)", border: "1px solid var(--border)",
            borderRadius: 4, padding: "2px 5px"
          }}>Esc</kbd>
        </div>

        <div style={{ maxHeight: 340, overflowY: "auto" }}>
          {filtered.length === 0 ? (
            <div style={{ padding: "20px 16px", textAlign: "center", fontSize: 12, color: "var(--text3)" }}>
              {t("nav.cmdNoResults")}
            </div>
          ) : filtered.map((loc, i) => (
            <div
              key={loc.locationCode}
              onClick={() => goTo(loc.locationCode)}
              onMouseEnter={() => setCursor(i)}
              style={{
                padding: "10px 16px", cursor: "pointer",
                background: i === cursor ? "rgba(59,126,255,0.1)" : "transparent",
                borderBottom: "1px solid var(--border)",
                display: "flex", justifyContent: "space-between", alignItems: "center",
              }}
            >
              <div>
                <div style={{
                  fontSize: 13, fontWeight: i === cursor ? 600 : 400,
                  color: i === cursor ? "var(--accent)" : "var(--text)"
                }}>
                  {loc.displayName}
                </div>
                <div style={{ fontSize: 11, color: "var(--text3)", marginTop: 2 }}>
                  {loc.city} · {loc.country}
                </div>
              </div>
              <span style={{
                fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono",
                padding: "2px 6px", border: "1px solid var(--border)", borderRadius: 3
              }}>
                {loc.locationCode.split("~")[0]}
              </span>
            </div>
          ))}
        </div>

        <div style={{
          padding: "7px 16px", borderTop: "1px solid var(--border)",
          fontSize: 10, color: "var(--text3)",
          display: "flex", gap: 14
        }}>
          <span><kbd style={{ fontFamily: "IBM Plex Mono" }}>↑↓</kbd> {t("nav.cmdNav")}</span>
          <span><kbd style={{ fontFamily: "IBM Plex Mono" }}>↵</kbd> {t("nav.cmdOpen")}</span>
          <span><kbd style={{ fontFamily: "IBM Plex Mono" }}>Esc</kbd> {t("nav.cmdClose")}</span>
        </div>
      </div>
    </>
  )
}
