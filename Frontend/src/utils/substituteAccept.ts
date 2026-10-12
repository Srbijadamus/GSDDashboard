// Accept-substitute HTTP helper.
//
// The server answers every rejection with { code, error, details }. This module keeps
// that body instead of collapsing it to "API error 409", so the UI can show the exact
// reason — localized for known codes, the server's own sentence otherwise.
//
// Live 2026-10-12: the Overview drawer showed only the hard-coded
// "Failed to accept substitute — please refresh and try again." while the server had
// already answered 409 with the real cause (a protected manual entry). The reason must
// never be thrown away again.

export interface SubstituteAcceptErrorBody {
  code?: string | null
  error?: string | null
  detail?: string | null
  details?: Record<string, unknown> | null
}

export class SubstituteAcceptError extends Error {
  readonly status: number
  readonly body: SubstituteAcceptErrorBody

  constructor(status: number, body: SubstituteAcceptErrorBody) {
    super(body.error ?? `API error ${status}`)
    this.name = "SubstituteAcceptError"
    this.status = status
    this.body = body
  }
}

export interface SubstituteAcceptRequest {
  employeeId: string
  locationCode: string
  date: string
  shiftStart?: string | null
  shiftEnd?: string | null
  sourceType?: string | null
}

/** Parse a failed accept response body. Never loses the server text: a non-JSON body
 *  is still returned as `error` rather than replaced by a generic sentence. */
export function parseSubstituteAcceptError(status: number, text: string): SubstituteAcceptErrorBody {
  try {
    const parsed = JSON.parse(text)
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as SubstituteAcceptErrorBody
    }
  } catch { /* body was not JSON */ }
  const trimmed = (text ?? "").trim()
  return { error: trimmed.length > 0 ? trimmed : `API error ${status}` }
}

export async function postSubstituteAccept(req: SubstituteAcceptRequest): Promise<unknown> {
  const res = await fetch("/api/wic/substitutes/accept", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(req),
  })
  const text = await res.text().catch(() => "")
  if (!res.ok) throw new SubstituteAcceptError(res.status, parseSubstituteAcceptError(res.status, text))
  return text ? JSON.parse(text) : null
}

/** Machine code -> i18n key. A code with no key falls back to the server's own text. */
export const SUBSTITUTE_ACCEPT_ERROR_KEYS: Record<string, string> = {
  MANUAL_ENTRY_PROTECTED: "attendance.substitute.acceptErrors.manualProtected",
  ALREADY_HAS_WIC_DUTY: "attendance.substitute.acceptErrors.alreadyHasWicDuty",
  ACCEPT_FAILED: "attendance.substitute.acceptErrors.failed",
}

export type TranslateFn = (key: string, options?: Record<string, unknown>) => string

/** The text to show the user for a failed accept. */
export function substituteAcceptErrorText(err: unknown, t: TranslateFn): string {
  if (err instanceof SubstituteAcceptError) {
    const code = err.body.code ?? ""
    const key = SUBSTITUTE_ACCEPT_ERROR_KEYS[code]
    if (key) {
      const params = code === "ACCEPT_FAILED"
        ? { message: err.body.detail ?? err.body.error ?? "" }
        : (err.body.details ?? {})
      return t(key, params)
    }
    if (err.body.error) return err.body.error
  }
  if (err instanceof Error) return err.message
  return String(err)
}
