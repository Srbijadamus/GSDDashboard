import { useState } from "react"
import { useTranslation } from "react-i18next"
import { useAuth } from "../auth/AuthContext"
import { useFeatures } from "../utils/useFeatures"

function readLocal(key: string): string | null {
  try { return window.localStorage.getItem(key) } catch { return null }
}
function writeLocal(key: string, value: string) {
  try { window.localStorage.setItem(key, value) } catch { /* unavailable */ }
}

/** One-time "What's new" modal, per user (localStorage), behind Features:WhatsNew. */
export function WhatsNew() {
  const { t } = useTranslation()
  const { session } = useAuth()
  const { whatsNew } = useFeatures()
  const [dismissed, setDismissed] = useState(false)

  // Derived during render (no effect): show only once the session is loaded and
  // the user has not dismissed it (localStorage, per KID).
  const kid = session?.kid
  const show = whatsNew && kid != null && !dismissed && readLocal(`pulse.whatsNew.${kid}`) !== "seen"

  if (!show) return null

  const dismiss = () => {
    writeLocal(`pulse.whatsNew.${kid}`, "seen")
    setDismissed(true)
  }

  return (
    <div style={{ position: "fixed", inset: 0, background: "rgb(var(--surface-scrim) / .5)", display: "grid", placeItems: "center", zIndex: 100 }}>
      <div style={{ background: "rgb(var(--surface-overlay))", border: "1px solid rgb(var(--border-default))", borderRadius: 12, padding: 20, maxWidth: 420, width: "100%" }}>
        <h3 style={{ fontSize: 16, margin: "0 0 8px" }}>{t("whatsNew.title")}</h3>
        <p style={{ fontSize: 13, color: "rgb(var(--text-secondary))", margin: "0 0 14px", lineHeight: 1.5 }}>{t("whatsNew.body")}</p>
        <div style={{ display: "flex", justifyContent: "flex-end" }}>
          <button onClick={dismiss} style={{ padding: "6px 14px", borderRadius: 6, fontSize: 13, fontWeight: 600, border: "none", cursor: "pointer", background: "rgb(var(--action-solid))", color: "rgb(var(--action-fg))" }}>{t("whatsNew.dismiss")}</button>
        </div>
      </div>
    </div>
  )
}
