// Single source of truth for how far ahead shifts/absences can be planned.
// AL is routinely requested many months in advance, so this must stay generous.
// Mirrors Backend/ScheduleLimits.cs — keep both in sync.
export const MAX_FUTURE_DAYS = 365

export function maxFutureDateStr(): string {
  const d = new Date()
  d.setDate(d.getDate() + MAX_FUTURE_DAYS)
  return d.toISOString().split("T")[0]
}
