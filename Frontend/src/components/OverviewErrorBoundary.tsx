import React from "react"
import { useTranslation } from "react-i18next"
import { AlertTriangle } from "lucide-react"

interface CoreProps {
  children: React.ReactNode
  t: (key: string) => string
}
interface State { crashed: boolean }

// Class component required — hooks are not available in error boundaries.
// Passes t() in from the functional wrapper below.
class OverviewErrorBoundaryCore extends React.Component<CoreProps, State> {
  state: State = { crashed: false }

  static getDerivedStateFromError(): State {
    return { crashed: true }
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {
    const msg = error.message
    const stack = error.stack ?? ""
    const cs = info.componentStack ?? ""
    console.error("[Overview crash]", msg, "\n", cs.slice(0, 600))
    fetch("/api/debug/client-error", {
      method: "POST",
      headers: { "Content-Type": "text/plain" },
      body: `message: ${msg}\n\nstack:\n${stack}\n\ncomponentStack:\n${cs.slice(0, 3000)}`,
    }).catch(() => {})
  }

  render() {
    if (!this.state.crashed) return this.props.children
    const { t } = this.props
    return (
      <div
        role="alert"
        style={{
          margin: "24px 0",
          display: "flex", flexDirection: "column", alignItems: "center",
          gap: 12, padding: "40px 24px", textAlign: "center",
          background: "rgb(var(--st-crit-bg))",
          border: "1px solid rgb(var(--st-crit-bd))",
          borderRadius: 8,
          color: "rgb(var(--st-crit-solid))",
        }}
      >
        <AlertTriangle size={30} />
        <div style={{ fontWeight: 700, fontSize: 15 }}>
          {t("overview.error.title")}
        </div>
        <div style={{ fontSize: 13, color: "rgb(var(--text-secondary))", maxWidth: 440, lineHeight: 1.5 }}>
          {t("overview.error.detail")}
        </div>
      </div>
    )
  }
}

export function OverviewErrorBoundary({ children }: { children: React.ReactNode }) {
  const { t } = useTranslation()
  return <OverviewErrorBoundaryCore t={t}>{children}</OverviewErrorBoundaryCore>
}
