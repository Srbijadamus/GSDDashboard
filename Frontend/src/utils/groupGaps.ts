// Pure grouping for the WIC coverage-gaps list (Overview V2).
// Merges consecutive gap entries (same location, time window, present/min, status
// and the set of people with reasons) into one decision row. "Consecutive" =
// consecutive entries in the date-sorted list for a location — days without a
// requirement (weekend / closed) are simply absent and never break a group.

export interface GapAgent {
  employeeId: string | null
  fullName: string
  assignmentType: string // MAIN | BACKUP | REGIONAL
  status: string         // absence reason (raw ShiftType) or "NONE"
  taskLabel: string
}

export interface CoverageGap {
  date: string          // "yyyy-MM-dd"
  weekday: string
  locationCode: string
  displayName: string
  city: string
  category: string      // UNCOVERED_ABSENCE | UNCOVERED_ASSIGNED
  present: number
  required: number
  openTime: string | null
  closeTime: string | null
  openTime2: string | null
  closeTime2: string | null
  assignedAgents: GapAgent[]
  availableFloaters: GapAgent[]
}

export type GapStatus = "UNCOVERED" | "PARTIAL"

export interface FloaterSuggestion {
  employeeId: string
  fullName: string
  /** Assignment type of the floater (e.g. REGIONAL) — used as the accept SourceType. */
  assignmentType: string
  /** Dates within the group on which this floater is available. */
  availableDates: string[]
}

export interface GapGroup {
  locationCode: string
  displayName: string
  city: string
  from: string
  to: string
  days: string[]
  dayCount: number
  time: string
  present: number
  required: number
  status: GapStatus
  agents: GapAgent[]
  suggestions: FloaterSuggestion[]
}

function statusOf(g: CoverageGap): GapStatus {
  return g.present === 0 ? "UNCOVERED" : "PARTIAL"
}

function windowText(g: CoverageGap): string {
  const parts: string[] = []
  if (g.openTime && g.closeTime) parts.push(`${g.openTime}-${g.closeTime}`)
  if (g.openTime2 && g.closeTime2) parts.push(`${g.openTime2}-${g.closeTime2}`)
  return parts.join(", ")
}

/** Stable, order-insensitive key for the people-with-reasons set (assigned only —
 *  available floaters are suggestions, not part of the grouping identity). */
function peopleKey(g: CoverageGap): string {
  return g.assignedAgents
    .map(a => `${a.employeeId ?? a.fullName}|${a.assignmentType}|${a.status}`)
    .sort()
    .join(",")
}

function groupKey(g: CoverageGap): string {
  return [
    g.locationCode,
    windowText(g),
    g.present,
    g.required,
    statusOf(g),
    peopleKey(g),
  ].join("~")
}

export function groupGaps(gaps: CoverageGap[]): GapGroup[] {
  if (gaps.length === 0) return []

  const sorted = [...gaps].sort((a, b) =>
    a.locationCode === b.locationCode
      ? a.date.localeCompare(b.date)
      : a.locationCode.localeCompare(b.locationCode))

  const groups: GapGroup[] = []
  let prevKey: string | null = null
  for (const g of sorted) {
    const k = groupKey(g)
    const last = groups[groups.length - 1]
    if (last && prevKey === k) {
      // Same key as the previous entry → extend the group.
      last.days.push(g.date)
      last.to = g.date
      last.dayCount = last.days.length
    } else {
      groups.push({
        locationCode: g.locationCode,
        displayName: g.displayName,
        city: g.city,
        from: g.date,
        to: g.date,
        days: [g.date],
        dayCount: 1,
        time: windowText(g),
        present: g.present,
        required: g.required,
        status: statusOf(g),
        agents: g.assignedAgents,
        suggestions: [],
      })
    }
    prevKey = k
  }

  // Per-group floater availability (which dates of the group they can cover).
  return groups.map(grp => {
    const byDate = new Map(sorted.filter(x => x.locationCode === grp.locationCode && grp.days.includes(x.date)).map(x => [x.date, x]))
    const availability = new Map<string, string[]>()
    const names = new Map<string, string>()
    const types = new Map<string, string>()
    for (const date of grp.days) {
      const g = byDate.get(date)!
      for (const f of g.availableFloaters) {
        const id = f.employeeId ?? f.fullName
        names.set(id, f.fullName)
        types.set(id, f.assignmentType || "REGIONAL")
        if (!availability.has(id)) availability.set(id, [])
        availability.get(id)!.push(date)
      }
    }
    return {
      ...grp,
      suggestions: [...availability.entries()].map(([id, dates]) => ({
        employeeId: id,
        fullName: names.get(id) ?? id,
        assignmentType: types.get(id) ?? "REGIONAL",
        availableDates: dates,
      })),
    }
  })
}
