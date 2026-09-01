import type { CSSProperties } from 'react'

export type ShiftStatus =
  | 'WORKING' | 'OFF' | 'OFF_WEEKEND'
  | 'WIC_DUTY' | 'AL' | 'HALF_AL'
  | 'SL' | 'UL' | 'TRAINING'
  | 'PH' | 'LPH' | 'CD' | 'CO' | 'OL'
  | 'RESIGNED'

// Pill label — same in DE/EN (shared operational abbreviations).
const CODE: Record<ShiftStatus, string> = {
  WORKING:     'WORKING',
  OFF:         'OFF',
  OFF_WEEKEND: 'OFF',
  WIC_DUTY:    'WIC',
  AL:          'AL',
  HALF_AL:     '½AL',
  SL:          'SL',
  UL:          'UL',
  TRAINING:    'TR',
  PH:          'PH',
  LPH:         'LPH',
  CD:          'CD',
  CO:          'CO',
  OL:          'OL',
  RESIGNED:    'OFF',
}

// ── Tailwind class strings written in full — no string concatenation ──────────
// Quiet (bg-{t}-bg text-{t}-fg border border-{t}-bd): WORKING, OFF, OFF_WEEKEND, RESIGNED
const QUIET: Record<string, string> = {
  good:     'bg-good-bg text-good-fg border border-good-bd',
  neutralst:'bg-neutralst-bg text-neutralst-fg border border-neutralst-bd',
  mutedst:  'bg-mutedst-bg text-mutedst-fg border border-mutedst-bd',
}

// Loud (bg-{t}-solid text-white border border-transparent): WIC_DUTY, AL, SL, UL, TRAINING, PH
// CONTRAST NOTE — holiday-solid (#EAAA00): white is ~2.26:1 at 11px → FAILS WCAG AA.
// text-ink used on holiday-solid (dark text passes at 9.3:1). Verified per spec §3.
const LOUD: Record<string, string> = {
  wic:     'bg-wic-solid text-white border border-transparent',
  info:    'bg-info-solid text-white border border-transparent',
  warn:    'bg-warn-solid text-white border border-transparent',
  mutedst: 'bg-mutedst-solid text-white border border-transparent',
  learn:   'bg-learn-solid text-white border border-transparent',
  holiday: 'bg-holiday-solid text-ink border border-transparent',
}

// Outline-only (bg-transparent text-{t}-fg border border-{t}-bd): LPH, CD, CO, OL
const OUTLINE: Record<string, string> = {
  holiday: 'bg-transparent text-holiday-fg border border-holiday-bd',
  mutedst: 'bg-transparent text-mutedst-fg border border-mutedst-bd',
}

// ── Tone + variant per type — locked from Piece 1 §2.2 ───────────────────────
type Variant = 'quiet' | 'loud' | 'outline'
type Special = 'half-al' | 'off-weekend'

const TYPES: Record<ShiftStatus, { tone: string; variant: Variant; special?: Special }> = {
  WORKING:     { tone: 'good',     variant: 'quiet' },
  OFF:         { tone: 'neutralst',variant: 'quiet' },
  OFF_WEEKEND: { tone: 'neutralst',variant: 'quiet', special: 'off-weekend' },
  WIC_DUTY:    { tone: 'wic',      variant: 'loud'  },
  AL:          { tone: 'info',     variant: 'loud'  },
  HALF_AL:     { tone: 'info',     variant: 'loud',  special: 'half-al' },
  SL:          { tone: 'warn',     variant: 'loud'  },
  UL:          { tone: 'mutedst',  variant: 'loud'  },
  TRAINING:    { tone: 'learn',    variant: 'loud'  },
  PH:          { tone: 'holiday',  variant: 'loud'  },
  LPH:         { tone: 'holiday',  variant: 'outline'},
  CD:          { tone: 'mutedst',  variant: 'outline'},
  CO:          { tone: 'mutedst',  variant: 'outline'},
  OL:          { tone: 'mutedst',  variant: 'outline'},
  RESIGNED:    { tone: 'mutedst',  variant: 'quiet' },
}

function pillClass(tone: string, variant: Variant): string {
  if (variant === 'quiet')   return QUIET[tone]   ?? ''
  if (variant === 'loud')    return LOUD[tone]    ?? ''
  if (variant === 'outline') return OUTLINE[tone] ?? ''
  return ''
}

function specialStyle(special?: Special): CSSProperties | undefined {
  if (special === 'half-al') return {
    // Left half solid info-tone, right half subtle — diagonal split at 50%
    background: 'linear-gradient(135deg, rgb(var(--st-info-solid)) 50%, rgb(var(--st-info-bg)) 50%)',
  }
  if (special === 'off-weekend') return {
    backgroundImage: 'repeating-linear-gradient(45deg, transparent, transparent 3px, rgb(var(--st-neutralst-bd)) 3px, rgb(var(--st-neutralst-bd)) 4px)',
  }
  return undefined
}

const PILL_BASE = 'px-2 h-5 rounded-[4px] text-[11px] font-semibold tracking-wide grid place-items-center min-w-14 select-none'

interface ShiftBadgeProps {
  status: ShiftStatus
  className?: string
}

export function ShiftBadge({ status, className }: ShiftBadgeProps) {
  const cfg = TYPES[status] ?? TYPES.OFF

  // HALF_AL: the gradient spans solid→bg diagonally. A single text colour is invisible on
  // one of the two halves. Label sits on a bg-info-bg inset so it's always readable at 11px.
  if (cfg.special === 'half-al') {
    return (
      <span
        className={[
          'h-5 rounded-[4px] grid place-items-center min-w-14 border border-info-bd select-none',
          className ?? '',
        ].filter(Boolean).join(' ')}
        style={{ background: 'linear-gradient(135deg, rgb(var(--st-info-solid)) 50%, rgb(var(--st-info-bg)) 50%)' }}
      >
        <span className="inline-block px-1 rounded-[3px] bg-info-bg text-info-fg text-[11px] font-semibold leading-none tracking-wide">
          {CODE.HALF_AL}
        </span>
      </span>
    )
  }

  return (
    <span
      className={[PILL_BASE, pillClass(cfg.tone, cfg.variant), className ?? ''].filter(Boolean).join(' ')}
      style={specialStyle(cfg.special)}
    >
      {CODE[status] ?? status}
    </span>
  )
}
