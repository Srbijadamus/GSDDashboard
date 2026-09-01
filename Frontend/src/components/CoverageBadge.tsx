import { useTranslation } from "react-i18next"

type CoverageStatus = "COVERED" | "PARTIAL" | "UNCOVERED" | "CLOSED" | string

interface BadgeConfig {
  bg: string
  color: string
  dot: string
  border: string
  i18nKey: string
}

const CONFIG: Record<string, BadgeConfig> = {
  COVERED:   { bg: "rgb(var(--st-good-bg))",    color: "rgb(var(--st-good-fg))",    dot: "rgb(var(--st-good-solid))",    border: "rgb(var(--st-good-bd))",    i18nKey: "attendance.status.covered"   },
  PARTIAL:   { bg: "rgb(var(--st-warn-bg))",    color: "rgb(var(--st-warn-fg))",    dot: "rgb(var(--st-warn-solid))",    border: "rgb(var(--st-warn-bd))",    i18nKey: "attendance.status.partial"   },
  UNCOVERED: { bg: "rgb(var(--st-crit-bg))",    color: "rgb(var(--st-crit-fg))",    dot: "rgb(var(--st-crit-solid))",    border: "rgb(var(--st-crit-bd))",    i18nKey: "attendance.status.uncovered" },
  CLOSED:    { bg: "rgb(var(--st-neutral-bg))", color: "rgb(var(--st-neutral-fg))", dot: "rgb(var(--st-neutral-solid))", border: "rgb(var(--st-neutral-bd))", i18nKey: "attendance.status.closed"    },
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
      border: `1px solid ${cfg.border}`,
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
