// Roster generator HTTP error helper.
//
// The server answers a refused preview/generate with { error, code }. This module
// keeps that body so the UI can show its own EN/DE wording for known codes instead of
// the server's English sentence — and never loses the reason for an unknown one.
//
// Writer #1 (Backend/RosterService.cs ValidateAsync): a WIC batch without an explicit
// shift time is refused with code WIC_SHIFT_TIME_REQUIRED, because the centre's opening
// hours must never be stored as the agent's ShiftStart/ShiftEnd.

export interface RosterErrorBody {
  code?: string | null
  error?: string | null
}

export class RosterApiError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(status: number, body: RosterErrorBody) {
    super(body.error ?? `API ${status}`)
    this.name = "RosterApiError"
    this.status = status
    this.code = body.code ?? null
  }
}

/** Parse a failed roster response body. A non-JSON body is still kept as the reason. */
export function parseRosterError(status: number, text: string): RosterErrorBody {
  try {
    const parsed = JSON.parse(text)
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as RosterErrorBody
    }
  } catch { /* body was not JSON */ }
  const trimmed = (text ?? "").trim()
  return { error: trimmed.length > 0 ? trimmed : `API ${status}` }
}

/** Machine code -> i18n key. A code with no key falls back to the server's own text. */
export const ROSTER_ERROR_KEYS: Record<string, string> = {
  WIC_SHIFT_TIME_REQUIRED: "roster.errors.wicShiftTimeRequired",
}

export type TranslateFn = (key: string, options?: Record<string, unknown>) => string

/** The text the roster page shows for a failed preview/generate/delete/load. */
export function rosterErrorText(err: unknown, t: TranslateFn): string {
  if (err instanceof RosterApiError) {
    if (err.status === 401) return t("roster.errors.unauthenticated")
    if (err.status === 403) return t("roster.errors.forbidden")
    const key = err.code ? ROSTER_ERROR_KEYS[err.code] : undefined
    if (key) return t(key)
    return err.message
  }
  const m = err instanceof Error ? err.message : String(err)
  // Non-JSON failure shapes kept for compatibility with the old fmtErr behaviour.
  if (m === "Forbidden" || m.includes("403")) return t("roster.errors.forbidden")
  if (m.includes("401")) return t("roster.errors.unauthenticated")
  return m
}
