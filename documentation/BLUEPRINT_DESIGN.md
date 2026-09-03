# GSDDashboard — Design System Reference (Section 5)

**Part of the Project Blueprint.** See [PROJECT_BLUEPRINT.md](./PROJECT_BLUEPRINT.md) for the index.

---

## Overview

The design system is a token-first system built on CSS custom properties with space-separated RGB channels (so Tailwind opacity modifiers work: `bg-raised/60` → `rgb(var(--surface-raised) / 0.60)`). All tokens are defined in `Frontend/src/styles/tokens.css` and wired into Tailwind in `tailwind.config.js`. The `index.css` imports the tokens file and adds global base styles.

**Non-negotiable rule:** Never hardcode hex colours in component code. Always use design tokens via Tailwind classes or `rgb(var(--token-name))` inline styles.

---

## 1. Token Architecture

Tokens are grouped into families:

| Family | Prefix | Purpose |
|---|---|---|
| Surfaces | `--surface-*` | Background fills |
| Borders | `--border-*` | Outlines and hairlines |
| Text | `--text-*` | Foreground/ink colours |
| Interactive | `--action-*`, `--focus-ring` | Buttons, links, focus indicators |
| Brand | `--brand-*` | E.ON red — logo chip only |
| Status families | `--st-{name}-{scale}` | Semantic colour groups |
| Nav chrome | `--nav-*` | Sidebar/navigation independent of app theme |
| Elevation | `--shadow-*` | Box shadows |
| Motion | `--ease-out`, `--dur-*` | Transitions |
| Signals | `--signal-live` | Live kiosk heartbeat — not a status colour |

---

## 2. Surface Tokens

Values are space-separated RGB channels.

| Token | Light | Dark | Usage |
|---|---|---|---|
| `--surface-page` | `247 248 250` (#F7F8FA) | `11 14 20` (#0B0E14) | App background body |
| `--surface-raised` | `255 255 255` (#FFFFFF) | `19 24 34` (#131822) | Cards, tables, sidebar panels |
| `--surface-sunken` | `238 240 244` (#EEF0F4) | `15 19 27` (#0F131B) | Table headers, wells, inset areas |
| `--surface-overlay` | `255 255 255` (#FFFFFF) | `26 33 48` (#1A2130) | Modals, popovers, drawers |
| `--surface-hover` | `244 245 248` (#F4F5F8) | `26 33 48` (#1A2130) | Row and nav item hover state |
| `--surface-active` | `235 238 243` (#EBEEF3) | `34 42 60` (#222A3C) | Row/nav pressed and selected state |
| `--surface-scrim` | `16 24 40` (#101828) | `0 0 0` | Modal backdrop; used at /50 opacity |

Tailwind aliases: `bg-page`, `bg-raised`, `bg-sunken`, `bg-overlay`, `bg-hovered`, `bg-actived`, `bg-scrim`.

---

## 3. Border Tokens

| Token | Light | Dark | Usage |
|---|---|---|---|
| `--border-subtle` | `228 231 236` (#E4E7EC) | `32 40 57` (#202839) | Hairlines, table grid lines |
| `--border-default` | `208 213 221` (#D0D5DD) | `44 53 71` (#2C3547) | Input borders, card outlines |
| `--border-strong` | `152 162 179` (#98A2B3) | `71 84 103` (#475467) | Emphasis borders, drag targets |

Tailwind aliases: `border-line-subtle`, `border-line-default`, `border-line-strong`.

---

## 4. Text Tokens

| Token | Light | Dark | Usage |
|---|---|---|---|
| `--text-primary` | `16 24 40` (#101828) | `232 236 243` (#E8ECF3) | Body text, headings |
| `--text-secondary` | `71 84 103` (#475467) | `152 162 179` (#98A2B3) | Supporting text, labels |
| `--text-tertiary` | `102 112 133` (#667085) | `102 112 133` (#667085) | Placeholders, hints |
| `--text-disabled` | `152 162 179` (#98A2B3) | `71 84 103` (#475467) | Disabled states |
| `--text-inverse` | `255 255 255` (#FFFFFF) | `11 14 20` (#0B0E14) | Text on dark/action backgrounds |

Tailwind aliases: `text-ink`, `text-ink-muted`, `text-ink-soft`, `text-ink-disabled`, `text-ink-inverse`.

---

## 5. Interactive Tokens

**Design rationale:** Action buttons use a colourless/hueless token (`--action-solid` = near-black in light, near-white in dark) because the brand has no single-colour mandate and status colours must be reserved for semantic use. Never use a status colour for a primary action button.

| Token | Light | Dark | Usage |
|---|---|---|---|
| `--action-solid` | `22 24 29` (#16181D) | `232 236 243` (#E8ECF3) | Primary button fill |
| `--action-solid-hover` | `47 51 60` (#2F333C) | `255 255 255` (#FFFFFF) | Primary button hover |
| `--action-fg` | `255 255 255` | `11 14 20` | Text on action buttons |
| `--focus-ring` | `91 141 239` (#5B8DEF) | `132 202 255` (#84CAFF) | Focus outlines only — never used for status |

Tailwind aliases: `bg-action`, `bg-action-hover`, `text-action-fg`, `ring-focusring`.

---

## 6. Status Families

Each status has five scales: `fg` (foreground), `bg` (background tint), `bd` (border), `solid` (solid fill), `mid` (dense data marker). Use:
- `bg` + `fg` for text badges with tinted backgrounds
- `solid` for icon fills and chart bars
- `mid` for dot/bar markers in dense data tables
- `bd` for the border of a status badge

| Family | Tailwind Prefix | Semantic meaning |
|---|---|---|
| good | `good-*` | Covered / OK / positive |
| warn | `warn-*` | Partial / caution / near-limit |
| crit | `crit-*` | Uncovered / error / over-limit |
| info | `info-*` | Informational / selected / neutral action |
| wic | `wic-*` | WIC duty specifically |
| learn | `learn-*` | Training / learning |
| holiday | `holiday-*` | Public or regional holidays |
| neutralst | `neutralst-*` | Closed / no-data / inactive |
| mutedst | `mutedst-*` | Resigned / deprecated / very low emphasis |

### Light-mode values (RGB channels)

| Family | fg | bg | bd | solid | mid |
|---|---|---|---|---|---|
| good | 6 118 71 | 236 253 243 | 171 239 198 | 18 158 99 | 108 233 166 |
| warn | 181 71 8 | 255 250 235 | 254 223 137 | 247 144 9 | 254 200 75 |
| crit | 180 35 24 | 254 243 242 | 254 205 202 | 240 68 56 | 253 162 155 |
| info | 23 92 211 | 239 248 255 | 178 221 255 | 46 144 250 | 132 202 255 |
| wic | 14 112 144 | 236 253 255 | 165 240 252 | 6 174 212 | 103 227 249 |
| learn | 105 65 198 | 244 243 255 | 217 214 254 | 127 86 217 | 182 164 253 |
| holiday | 133 74 14 | 254 251 232 | 254 238 149 | 234 170 0 | 253 226 114 |
| neutralst | 71 84 103 | 242 244 247 | 208 213 221 | 152 162 179 | 208 213 221 |
| mutedst | 152 162 179 | 248 249 251 | 228 231 236 | 208 213 221 | 234 236 240 |

Dark-mode values are defined in `tokens.css` in the `.dark` block (see file for raw values).

---

## 7. Shift Type → Colour Mapping

From `Shifts.tsx` `shiftColor()` function. These are the canonical mappings used across all shift displays:

| ShiftType | Background | Foreground |
|---|---|---|
| WORKING | `--st-good-bg` | `--st-good-fg` |
| WIC_DUTY | `--st-wic-bg` | `--st-wic-fg` |
| AL | `--st-info-bg` | `--st-info-fg` |
| HALF_AL | `--st-info-bg` | `--st-info-fg` |
| SL | `--st-warn-bg` | `--st-warn-fg` |
| UL | `--st-crit-bg` | `--st-crit-fg` |
| OL | `--surface-raised` | `--text-secondary` |
| TRAINING | `--st-learn-bg` | `--st-learn-fg` |
| OFF | `--st-neutral-bg` | `--text-tertiary` |
| OFF_WEEKEND | `--surface-sunken` | `--text-tertiary` |
| PH | `--st-holiday-bg` | `--st-holiday-fg` |
| LPH | `--st-holiday-bg` | `--st-holiday-fg` |
| CD | `--surface-raised` | `--text-secondary` |
| CO | `--surface-raised` | `--text-secondary` |
| RESIGNED | `--st-neutral-bg` | `--text-tertiary` |
| EMPTY | transparent | `--text-tertiary` |

Coverage status → colour:

| Status | Colour family |
|---|---|
| COVERED | good |
| PARTIAL | warn |
| UNCOVERED | crit |
| CLOSED | neutralst |

Pipeline status → colour: PLANNED → neutralst, CONFIRMED → good, CANCELLED → crit.

---

## 8. Nav Chrome Tokens

The navigation sidebar uses its own independent token family — it stays dark by default regardless of the app theme (light/dark toggle does not affect the nav).

Controlled by `[data-nav-theme="dark"]` (default) or `[data-nav-theme="light"]` on `<html>`.

| Token | Dark nav value | Light nav value | Usage |
|---|---|---|---|
| `--nav-surface` | `14 17 26` (#0E111A) | `245 246 250` (#F5F6FA) | Sidebar background |
| `--nav-hover` | `22 27 40` (#161B28) | `234 236 242` | Nav item hover |
| `--nav-active` | `28 33 48` (#1C2130) | `224 227 236` | Active nav item fill |
| `--nav-border` | `30 36 52` (#1E2434) | `218 221 230` | Sidebar hairlines |
| `--nav-rail` | `255 255 255` | `22 24 29` | Active indicator pill |
| `--nav-ink` | `220 225 238` (#DCE1EE) | `20 24 40` | Primary nav text |
| `--nav-muted` | `130 140 165` (#828CA5) | `95 105 130` | Secondary nav text |
| `--nav-soft` | `80 88 110` (#50586E) | `130 140 160` | Group labels |

Tailwind aliases: `bg-nav-surface`, `bg-nav-hover`, `text-nav-ink`, `text-nav-muted`, etc.

---

## 9. Special Signals

| Token | Value (both themes) | Usage |
|---|---|---|
| `--signal-live` | `34 208 122` (#22D07A) | Live kiosk heartbeat animation in WicAttendance. **Not a status colour** — do not use for COVERED status. Prominence is a hard constraint. |

The `pulse-live` keyframe in `index.css` uses this for the glowing ring animation. An alias `pulse-green` exists for legacy callers in WicAttendance.tsx — it is deprecated and should be removed after callers are updated.

---

## 10. Elevation

| Token | Light shadow | Dark shadow |
|---|---|---|
| `--shadow-xs` | `0 1px 2px rgb(16 24 40/.05)` | `0 1px 2px rgb(0 0 0/.40)` |
| `--shadow-sm` | `0 1px 3px/.10 + 0 1px 2px/.06` | stronger |
| `--shadow-md` | `0 4px 8px -2px/.10 + 0 2px 4px -2px/.06` | stronger |
| `--shadow-lg` | `0 12px 16px -4px/.08 + 0 4px 6px -2px/.03` | stronger |
| `--shadow-xl` | `0 20px 24px -4px/.10 + 0 8px 8px -4px/.04` | stronger |

Tailwind aliases: `shadow-xs`, `shadow-sm`, `shadow-md`, `shadow-lg`, `shadow-xl`.

---

## 11. Motion

| Token | Value | Tailwind |
|---|---|---|
| `--ease-out` | `cubic-bezier(.2, .8, .2, 1)` | `ease-out` |
| `--dur-fast` | `120ms` | `duration-fast` |
| `--dur-base` | `180ms` | `duration-base` |
| `--dur-slow` | `240ms` | `duration-slow` |

---

## 12. Font Stack

```css
font-sans: '"Segoe UI Variable Text"', '"Segoe UI"', 'system-ui', 'sans-serif'
font-mono: '"Cascadia Mono"', 'Consolas', 'ui-monospace', 'monospace'
```

**Windows-only by design.** Segoe UI Variable Text and Cascadia Mono are Windows 11 system fonts. The dashboard is an internal Windows-only tool; no fallback to Roboto or Inter is needed or intended.

---

## 13. Typography Scale

From `tailwind.config.js`:

| Class | Size | Line height | Letter spacing |
|---|---|---|---|
| text-2xs | 11px | 16px | +0.04em |
| text-xs | 12px | 18px | 0 |
| text-sm | 13px | 20px | 0 |
| text-base | 14px | 22px | 0 |
| text-md | 16px | 24px | 0 |
| text-lg | 18px | 26px | -0.004em |
| text-xl | 20px | 28px | -0.006em |
| text-2xl | 24px | 32px | -0.008em |
| text-3xl | 30px | 38px | -0.011em |
| text-4xl | 36px | 44px | -0.013em |

Default body text is `text-base` (14px/22px). Table data uses `text-xs` (12px). Stat tiles use `text-3xl` or `text-2xl` for the number.

---

## 14. Spacing Extensions

Beyond Tailwind defaults, these custom spacing values are added:

| Class | Value |
|---|---|
| `4.5` | 18px |
| `13` | 52px |
| `15` | 60px |
| `18` | 72px |
| `88` | 352px |

---

## 15. Border Radius

| Class | Value |
|---|---|
| `rounded-xs` | 4px |
| `rounded-sm` | 6px |
| `rounded-md` | 8px |
| `rounded-lg` | 12px |
| `rounded-xl` | 16px |
| `rounded-2xl` | 20px |

---

## 16. z-Index Ladder

| Layer | z-index | Elements |
|---|---|---|
| nav | 30 | Sidebar navigation |
| topbar | 40 | Top bar |
| dropdown | 50 | Dropdowns, autocomplete lists |
| drawer | 60 | Side drawers / sheets |
| modal | 70 | Modal dialogs |
| toast | 80 | Toast notifications |
| palette | 90 | Command palette overlay |

---

## 17. Deprecated Tokens

`index.css` contains an aliased set of old var names that existing inline styles may still reference. They map to new tokens:

| Old var | Maps to |
|---|---|
| `--bg` | `rgb(var(--surface-page))` |
| `--sidebar` | `rgb(var(--surface-raised))` |
| `--card` | `rgb(var(--surface-raised))` |
| `--card2` | `rgb(var(--surface-sunken))` |
| `--border` | `rgb(var(--border-default))` |
| `--accent` | `rgb(var(--st-info-solid))` |
| `--accent2` | `rgb(var(--st-wic-solid))` |
| `--warn` | `rgb(var(--st-warn-solid))` |
| `--danger` | `rgb(var(--st-crit-solid))` |
| `--green` | `rgb(var(--st-good-solid))` |
| `--status-covered` | `rgb(var(--st-good-solid))` |
| `--status-partial` | `rgb(var(--st-warn-solid))` |
| `--status-uncovered` | `rgb(var(--st-crit-solid))` |
| `--status-closed` | `rgb(var(--st-neutral-solid))` |

**Do not use any of these in new code.** Remove each entry once all callers in the codebase have been migrated.

---

## 18. Global Base Styles

From `index.css`:

- `body`: `bg-page text-ink font-sans text-base` — 14px Segoe UI on the page background
- `font-variant-numeric: tabular-nums` on `table`, `input[type="number"]`, `time`, and `.tnum` — numbers line up in columns
- Focus-visible: 2px solid `--focus-ring` at 2px offset, 6px border-radius on all interactive elements
- `::selection`: 25% opacity `--focus-ring`
- Scrollbars: 6×6px, thumb uses `--border-subtle` → `--border-default` on hover
- Nav scrollbar: 4px wide, uses `--nav-border`
- `.skeleton` class: sunken background + `skeleton-pulse` animation at 1.5s for loading states
- `.spin` class: 0.8s linear spin for loading spinners
