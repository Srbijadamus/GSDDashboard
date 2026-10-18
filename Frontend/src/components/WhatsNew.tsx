import { useCallback, useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { useAuth } from "../auth/AuthContext"
import { useFeatures } from "../utils/useFeatures"

// Bump when the content changes so every user sees it once more.
const WHATS_NEW_VERSION = "v3"
export const WHATS_NEW_OPEN_EVENT = "pulse:whats-new-open"

function readLocal(key: string): string | null {
  try { return window.localStorage.getItem(key) } catch { return null }
}
function writeLocal(key: string, value: string) {
  try { window.localStorage.setItem(key, value) } catch { /* unavailable */ }
}

/** "What's new" modal: auto-shows once per user per content version (localStorage),
 *  and can always be reopened from the header menu. Esc closes, focus returns. */
export function WhatsNew() {
  const { t } = useTranslation()
  const { session } = useAuth()
  const { whatsNew } = useFeatures()
  const [dismissed, setDismissed] = useState(false)
  const [forcedOpen, setForcedOpen] = useState(false)
  const restoreFocusRef = useRef<HTMLElement | null>(null)

  const kid = session?.kid
  const seenKey = `pulse.whatsNew.${kid ?? "anon"}.${WHATS_NEW_VERSION}`
  const autoShow = whatsNew && kid != null && !dismissed && readLocal(seenKey) !== "seen"
  const show = autoShow || forcedOpen

  useEffect(() => {
    const h = () => setForcedOpen(true)
    window.addEventListener(WHATS_NEW_OPEN_EVENT, h)
    return () => window.removeEventListener(WHATS_NEW_OPEN_EVENT, h)
  }, [])

  const close = useCallback(() => {
    setForcedOpen(false)
    setDismissed(true)
    if (kid) writeLocal(seenKey, "seen")
    restoreFocusRef.current?.focus()
  }, [kid, seenKey])

  useEffect(() => {
    if (!show) return
    restoreFocusRef.current = document.activeElement as HTMLElement | null
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") close() }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [show, close])

  if (!show) return null

  return (
    <div role="dialog" aria-modal="true" aria-label={t("whatsNew.title")} style={{ position: "fixed", inset: 0, background: "rgb(var(--surface-scrim) / .5)", display: "grid", placeItems: "center", zIndex: 100 }}>
      <div style={{ background: "rgb(var(--surface-overlay))", border: "1px solid rgb(var(--border-default))", borderRadius: 12, padding: 20, maxWidth: 440, width: "100%" }}>
        <h3 style={{ fontSize: 16, margin: "0 0 8px" }}>{t("whatsNew.title")}</h3>
        <ul style={{ fontSize: 13, color: "rgb(var(--text-secondary))", margin: "0 0 14px", paddingLeft: 18, lineHeight: 1.5, maxHeight: 320, overflowY: "auto" }}>
          {(t("whatsNew.body", { returnObjects: true }) as unknown as string[]).map((item, i) => (
            <li key={i} style={{ marginBottom: 6 }}>{item}</li>
          ))}
        </ul>
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <button onClick={close} aria-label={t("whatsNew.dismiss")} style={{ padding: "6px 14px", borderRadius: 6, fontSize: 13, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--action-solid))", color: "rgb(var(--action-fg))" }}>{t("whatsNew.dismiss")}</button>
        </div>
      </div>
    </div>
  )
}
