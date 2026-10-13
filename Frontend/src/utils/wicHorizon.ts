// ONE horizon source for the Overview page and its topbar control.
//
// The topbar control (7d/14d/28d/90d) writes `?horizon=` and every surface on
// Overview reads it back through parseHorizon(): the coverage-gaps banner, the
// Coverage Risk day strip, the exception list, the heatmap and the map. That is
// what makes "View 90 days" and the "90d" topbar button the same action — the
// banner button does not own a horizon of its own, it moves the URL parameter
// the rest of the page already follows.

/** Horizons the topbar offers, in display order. 90 is the widest supported. */
export const HORIZON_OPTIONS = [7, 14, 28, 90] as const

/** Default when no (or an invalid) ?horizon= is present — unchanged since the page existed. */
export const DEFAULT_HORIZON = 28

/** Hard ceiling. The topbar offers 90; anything larger is clamped to it rather than rejected. */
export const MAX_HORIZON = 90

/** Matches the backend clamp on GET /api/wic/coverage-gaps (1..180) for the lower bound. */
export const MIN_HORIZON = 1

/**
 * The horizon every Overview surface uses. Absent/garbage input falls back to
 * DEFAULT_HORIZON instead of rendering a 0-day or NaN-day window, and 90 is a
 * first-class value here: a `Math.min(30, …)` clamp silently turned the restored
 * "View 90 days" button back into 30 days.
 */
export function parseHorizon(raw: string | null | undefined): number {
  // Number("") === 0, not NaN — an empty ?horizon= must fall back, not clamp to 1.
  if (raw == null || String(raw).trim() === "") return DEFAULT_HORIZON
  const n = Number(raw)
  if (!Number.isFinite(n)) return DEFAULT_HORIZON
  return Math.max(MIN_HORIZON, Math.min(MAX_HORIZON, Math.trunc(n)))
}

/** True when the horizon needs the condensed day strip (full day-by-day strip only fits ~a month). */
export function isWideHorizon(horizon: number): boolean {
  return horizon > 31
}

/** The banner button: the OTHER end of the horizon pair, never an independent toggle. */
export function nextHorizon(current: number): number {
  return current >= MAX_HORIZON ? 14 : MAX_HORIZON
}
