// Pure helpers for the Overview v2 "Assign manually" modal (OverviewV21).
// The per-day status is computed from an employee's existing ShiftEntries row —
// the same data /api/shifts already returns for the Shift Plan — so the modal
// never invents a status, it only labels what the roster already holds.

export type ManualStatus =
  | "free"           // not rostered that day
  | "rosteredNoTask" // WORKING with no AgentTask (idle, can be pulled to WIC)
  | "otherWic"       // already on WIC duty (another centre)
  | "bo"             // back office (WORKING + RawValue "BO")
  | "annualLeave"    // AL / HALF_AL / UL / OL / OFF / OFF_WEEKEND / PH / LPH / RESIGNED
  | "sick"           // SL
  | "training"       // TRAINING

/** A minimal ShiftEntries row as returned by GET /api/shifts (ShiftRowDto). */
export interface ManualShiftRow {
  employeeId: string
  shiftDate: string
  shiftType: string
  isWicDuty?: boolean
  rawValue?: string | null
  agentTask?: string | null
  effectiveTask?: string | null
}

const BLOCKING_LEAVE = ["AL", "HALF_AL", "UL", "OL", "OFF", "OFF_WEEKEND", "PH", "LPH", "RESIGNED"]

/** Label a person's day from an existing ShiftEntries row (`undefined` = not rostered). */
export function manualStatusOf(row: ManualShiftRow | undefined): ManualStatus {
  if (!row) return "free"
  const st = (row.shiftType ?? "").trim().toUpperCase()
  const task = (row.effectiveTask ?? row.agentTask ?? "").trim().toUpperCase()
  const raw = (row.rawValue ?? "").trim().toUpperCase()

  if (st === "SL") return "sick"
  if (st === "TRAINING") return "training"
  if (BLOCKING_LEAVE.includes(st)) return "annualLeave"
  if (st === "WIC_DUTY" || row.isWicDuty === true || task.startsWith("WIC")) return "otherWic"
  if (task === "BO" || raw === "BO") return "bo"
  if (st === "WORKING" && task === "") return "rosteredNoTask"
  if (st === "" || st === "EMPTY" || st === "NONE") return "free"
  // WORKING with a task that is not WIC/BO (Voice, Backlog, …): rostered and busy.
  return "rosteredNoTask"
}

/** The statuses whose assignment needs an explicit "Assign anyway" confirmation.
 *  Sick leave is deliberately NOT here: it is never assignable, with or without
 *  a confirmation — see {@link isSick}. */
export function needsAnyway(status: ManualStatus): boolean {
  return status === "annualLeave" || status === "otherWic" || status === "training"
}

/** Sick leave can never be overwritten (not even "Assign anyway"). */
export function isSick(status: ManualStatus): boolean {
  return status === "sick"
}
