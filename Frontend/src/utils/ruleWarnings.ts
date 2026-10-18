// Rule-warning (Features:RulesEngine) text helper.
//
// The backend returns a code + machine params + an English fallback message. This
// module turns code+params into the i18n key the UI renders, so the Schichtplan
// "!" tooltip and the pre-save warning read EN or DE exactly like every other
// string — never the bare code, never a mixed-language sentence.

export interface RuleWarning {
  code: string
  severity: string
  date: string
  params: Record<string, string | number>
  ref: string
  message: string
}

export interface PersonWarnings {
  employeeId: string
  warnings: RuleWarning[]
}

export interface RulesResponse {
  from: string
  to: string
  people: PersonWarnings[]
  summary: { people: number; warnings: Record<string, number> }
}

export type TranslateFn = (key: string, options?: Record<string, unknown>) => string

/** code + params -> translated sentence (EN/DE). Falls back to the server message. */
export function ruleWarningText(w: RuleWarning, t: TranslateFn): string {
  const params = w.params ?? {}
  if (w.code === "R2") {
    const type = params.type === "SL" ? "SL" : "AL"
    return t(`rules.R2.${type}`)
  }
  if (w.code === "DATA_INVALID") {
    const field = params.field === "end" ? t("rules.field.end") : t("rules.field.start")
    return t("rules.DATA_INVALID", { field })
  }
  return t(`rules.${w.code}`, params as Record<string, unknown>)
}

/** Build a lookup keyed by employeeId then date for fast cell placement. */
export function warningsByEmployeeDate(res: RulesResponse | undefined): Record<string, Record<string, RuleWarning[]>> {
  const map: Record<string, Record<string, RuleWarning[]>> = {}
  for (const p of res?.people ?? []) {
    const byDate: Record<string, RuleWarning[]> = {}
    for (const w of p.warnings) {
      ;(byDate[w.date] ??= []).push(w)
    }
    map[p.employeeId] = byDate
  }
  return map
}
