import { useTranslation } from "react-i18next"

type CoverageStatus = "COVERED" | "PARTIAL" | "UNCOVERED" | "CLOSED" | string

interface BadgeConfig {
  bg: string
  color: string
  dot: string
  i18nKey: string
}

const CONFIG: Record<string, BadgeConfig> = {
  COVERED:   { bg: "rgba(34, 208, 122, 0.14)", color: "#22d07a", dot: "#22d07a",  i18nKey: "attendance.status.covered"   },
  PARTIAL:   { bg: "rgba(255, 124,  59, 0.14)", color: "#ff7c3b", dot: "#ff7c3b", i18nKey: "attendance.status.partial"   },
  UNCOVERED: { bg: "rgba(255,  59,  92, 0.14)", color: "#ff3b5c", dot: "#ff3b5c", i18nKey: "attendance.status.uncovered" },
  CLOSED:    { bg: "rgba(122, 143, 168, 0.10)", color: "#7a8fa8", dot: "#4a5f7a", i18nKey: "attendance.status.closed"    },
}

const FALLBACK: BadgeConfig = CONFIG.CLOSED

interface Props {
  status: CoverageStatus
  compact?: boolean
}

export function CoverageBadge({ status, compact = false }: Props) {
  const { t } = useTranslation()
  const cfg = CONFIG[status] ?? FALLBACK

  if (compact) {
    return (
      <span style={{
        display: "inline-block",
        width: 8, height: 8,
        borderRadius: "50%",
        background: cfg.dot,
        flexShrink: 0,
      }} />
    )
  }

  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: 5,
      background: cfg.bg,
      border: `1px solid ${cfg.color}44`,
      color: cfg.color,
      borderRadius: 5, padding: "3px 8px",
      fontSize: 10, fontWeight: 700, textTransform: "uppercase",
      letterSpacing: ".04em", whiteSpace: "nowrap",
    }}>
      <span style={{ width: 5, height: 5, borderRadius: "50%", background: cfg.dot, flexShrink: 0 }} />
      {t(cfg.i18nKey, { defaultValue: status })}
    </span>
  )
}
