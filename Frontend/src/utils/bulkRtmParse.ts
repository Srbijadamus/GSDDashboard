// Bulk RTM Entry — "Paste & Parse" rules (2026-10-09).
//
// RTM picks a category and pastes one agent name per line. There is no date and
// no time on a line: every entry applies to the day it is entered (the server's
// local date, Europe/Berlin) as a whole day, and no time is stored.
//
//   * A name is recognised regardless of upper/lower case and repeated spaces.
//   * A name that cannot be recognised is reported as "name not found" and the
//     nearest employees are offered — never chosen automatically.
//   * A stray time or date left on a line is not an error: the name is still
//     recognised, the rest is ignored, and the preview notes it.
//
// Kept free of React so the rules can be tested with plain node (see
// bulkRtmParse.test.ts, npm run test:bulk-rtm).

import { resolveEmployee, type EmployeeBase, type MatchType, type BaseRowStatus, type ResolveResult } from "./resolveEmployee.js"

export type BulkRtmCategory = "AL" | "SL" | "OFF" | "CD" | "NIGHT" | "BO" | "WIC"

export const BULK_RTM_CATEGORIES: BulkRtmCategory[] = ["AL", "SL", "OFF", "CD", "NIGHT", "BO", "WIC"]

/** How a category is written. Shift categories go through the manual shift route. */
export type BulkRtmWriteKind = "shift" | "sickleave" | "bolist" | "wic"

export const BULK_RTM_WRITE_KIND: Record<BulkRtmCategory, BulkRtmWriteKind> = {
  AL: "shift", OFF: "shift", CD: "shift", NIGHT: "shift",
  SL: "sickleave", BO: "bolist", WIC: "wic",
}

/** ShiftEntries.ShiftType for a shift category; NIGHT is a WORKING shift. */
export const BULK_RTM_SHIFT_TYPE: Partial<Record<BulkRtmCategory, string>> = {
  AL: "AL", OFF: "OFF", CD: "CD", NIGHT: "WORKING",
}

/** /api/sickleave is open-ended; 2099-12-31 is the established sentinel. */
export const OPEN_ENDED_END_DATE = "2099-12-31"

let cachedToday = ""
let cachedTodayAt = 0

/**
 * Today in the machine's local time zone (Europe/Berlin) as yyyy-MM-dd.
 * `toISOString()` is deliberately not used: it is UTC, so between 00:00 and
 * 02:00 Berlin time it returns yesterday and the entry would land on the wrong day.
 */
export function localToday(now: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0")
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Same value as localToday(), recomputed when the clock crosses midnight. */
export function todayStable(): string {
  const stamp = Date.now()
  if (!cachedToday || stamp - cachedTodayAt > 60_000) {
    cachedToday = localToday()
    cachedTodayAt = stamp
  }
  return cachedToday
}

const TIME_RE = /\b\d{1,2}:\d{2}(?::\d{2})?\b/g
const DATE_RE = /\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}[./]\d{1,2}[./]\d{2,4}\b|\b\d{1,2}[./]\d{1,2}\.?(?=\s|$)/g

export interface ParsedAgentLine {
  /** The original line, as pasted (trimmed). */
  rawLine: string
  /** The name, with any time/date noise removed and spaces collapsed. */
  rawName: string
  /** True when the pasted line carried extra time/date text that was ignored. */
  ignoredTimeOrDate: boolean
}

/** One pasted line: strip time/date noise anywhere, collapse spacing, keep the name. */
export function parseAgentLine(line: string): ParsedAgentLine {
  const rawLine = line.trim()
  const stripped = rawLine.replace(TIME_RE, " ").replace(DATE_RE, " ")
  const ignoredTimeOrDate = stripped !== rawLine
  const rawName = stripped
    .replace(/[|;,]+/g, " ")                  // separators left behind by a removed time
    .replace(/(^|\s)[-–—]+(?=\s|$)/g, " ")    // dangling dashes
    .replace(/\s+/g, " ")
    .trim()
  return { rawLine, rawName, ignoredTimeOrDate }
}

export type BulkRtmRowStatus = BaseRowStatus

/**
 * A row is only written when the employee was recognised. A typo / unknown / ambiguous
 * row produces no write at all — it is reported and the other rows are saved normally.
 */
export function isWritableStatus(status: BulkRtmRowStatus): boolean {
  return status === "resolved" || status === "resolved-corrected"
}

export interface ResolvedAgentLine<T extends EmployeeBase = EmployeeBase> extends ParsedAgentLine {
  resolved: T | null
  ambiguous: T[]
  status: BulkRtmRowStatus
  matchType?: MatchType
  suggestions?: T[]
}

/**
 * Parse one line and resolve it against the employee list with automatic
 * fuzzy correction switched off: a typo stays "suggest" (or "ambiguous"), so it
 * is shown with a choice instead of being silently rewritten.
 */
export function resolveAgentLine<T extends EmployeeBase>(
  line: string,
  employees: T[]
): ResolvedAgentLine<T> {
  const parsed = parseAgentLine(line)
  const result: ResolveResult<T> = resolveEmployee(parsed.rawName, employees, { autoCorrect: false })
  return { ...parsed, ...result }
}

/** Parse a whole pasted list: one entry per non-empty line, order preserved. */
export function resolveAgentList<T extends EmployeeBase>(
  text: string,
  employees: T[]
): ResolvedAgentLine<T>[] {
  return text
    .split("\n")
    .map(l => l.trim())
    .filter(l => l.length > 0)
    .map(l => resolveAgentLine(l, employees))
}

export interface BulkRtmWriteTarget {
  kind: BulkRtmWriteKind
  url: string
  method: "POST"
  body: Record<string, unknown>
}

export interface BulkRtmWriteInput {
  employeeId: string
  displayName: string
  /** yyyy-MM-dd — the entry day (today). */
  date: string
  locationCode?: string | null
  note?: string | null
}

/**
 * The exact HTTP call(s) a pasted row makes for its category — one place, so
 * "the bulk write equals the Manual Entry write for the same category and day"
 * is testable without a browser.
 *
 *   AL / OFF / CD / NIGHT → /api/shifts/assign/manual, the shift cell's own
 *     manual route (session KID → ManualChangeBy/At, AutoGenerated = false).
 *     Times are null: a whole day, no time is stored.
 *   SL   → /api/sickleave, exactly the SickLeave page's Add action.
 *   BO   → /api/bo-list, exactly the BO Liste page's Add action, without a time.
 *   WIC  → /api/wic/assignments, exactly the WIC "Assign to gap" action; the
 *     centre's own opening hours supply the working window, the paste never does.
 *
 * WIC returns no target when the row has no location (no home WIC and no centre
 * chosen): the row is then reported as skipped, exactly as before.
 */
export function buildBulkRtmWrites(
  category: BulkRtmCategory,
  input: BulkRtmWriteInput
): BulkRtmWriteTarget[] {
  const { employeeId, displayName, date, locationCode, note } = input
  const kind = BULK_RTM_WRITE_KIND[category]

  if (kind === "shift") {
    return [{
      kind, url: "/api/shifts/assign/manual", method: "POST",
      body: {
        employeeId,
        shiftDate: date,
        shiftType: BULK_RTM_SHIFT_TYPE[category],
        shiftStart: null,
        shiftEnd: null,
      },
    }]
  }

  if (kind === "sickleave") {
    return [{
      kind, url: "/api/sickleave", method: "POST",
      body: { employeeId, startDate: date, endDate: OPEN_ENDED_END_DATE, type: "Self", notes: note ?? null },
    }]
  }

  if (kind === "bolist") {
    return [{
      kind, url: "/api/bo-list", method: "POST",
      body: { date, employeeName: displayName, shiftStart: "", shiftEnd: "", note: note ?? null },
    }]
  }

  if (!locationCode) return []
  return [{
    kind, url: "/api/wic/assignments", method: "POST",
    body: { employeeId, locationCode, date, shiftStart: null, shiftEnd: null },
  }]
}

/** Counts the preview header shows. */
export function summarizeRows(rows: Array<{ status: BulkRtmRowStatus }>): {
  recognized: number
  notRecognized: number
} {
  const recognized = rows.filter(r => r.status === "resolved" || r.status === "resolved-corrected").length
  return { recognized, notRecognized: rows.length - recognized }
}
