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
  sourceLocation?: string | null
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
  /** Candidate source type (from the floater's assignment type) — used as the accept SourceType. Empty means "no source type" (assign disabled). */
  sourceType: string
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
        types.set(id, f.assignmentType ?? "")
        if (!availability.has(id)) availability.set(id, [])
        availability.get(id)!.push(date)
      }
    }
    return {
      ...grp,
      suggestions: [...availability.entries()].map(([id, dates]) => ({
        employeeId: id,
        fullName: names.get(id) ?? id,
        sourceType: types.get(id) ?? "",
        availableDates: dates,
      })),
    }
  })
}

// ── v2.1 — grouping that only breaks on status / suggested-candidate change ──

export type ReasonCode = "annual-leave" | "bo" | "other-wic" | "not-rostered" | "no-task" | "other"

export function reasonOf(a: GapAgent): ReasonCode {
  const s = (a.status ?? "").toUpperCase()
  const task = (a.taskLabel ?? "").toUpperCase()
  if (["AL", "HALF_AL", "UL", "OL", "SL", "PH", "LPH", "OFF", "OFF_WEEKEND", "RESIGNED"].includes(s)) return "annual-leave"
  if (s === "BO" || task === "BO") return "bo"
  if (s === "WIC_DUTY" || task.startsWith("WIC")) return "other-wic"
  if (s === "NONE" || s === "") return "not-rostered"
  if (s === "WORKING" && task === "") return "no-task"
  return "other"
}

export interface GapDayV21 {
  date: string
  time: string
  present: number
  required: number
  agents: GapAgent[]
  candidateAvailable: boolean
}

export interface GapAgentV21 {
  employeeId: string | null
  fullName: string
  assignmentType: string
  reason: string   // a ReasonCode, or "varies" when the reason differs across days
  varies: boolean
}

/** A suggested (available) candidate for a group, with where they are now and
 *  the days of the group they can cover. */
export interface CandidateV21 {
  employeeId: string
  fullName: string
  source: string      // "WIC Recklinghausen", "rostered, no task", "BO", …
  sourceType: string  // assignmentType, used as the accept SourceType ("" = disabled)
  availableDates: string[]
}

/** Where the candidate currently is ("odakle"): the task label when present,
 *  else the home WIC centre, else "rostered, no task". */
export function sourceOf(f: GapAgent): string {
  const task = (f.taskLabel ?? "").trim()
  if (task) return task
  if (f.sourceLocation) return `WIC ${f.sourceLocation}`
  return "rostered, no task"
}

/** ISO 8601 week number (Mon–Sun weeks). */
export function isoWeek(date: Date): number {
  const d = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const dayNum = d.getUTCDay() || 7
  d.setUTCDate(d.getUTCDate() + 4 - dayNum)
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1))
  return Math.ceil((((d.getTime() - yearStart.getTime()) / 86400000) + 1) / 7)
}

/** One calendar-week sub-group of a (long) gap group. */
export interface GapWeekV21 {
  week: number   // ISO week number (for the "KW 45" label)
  from: string
  to: string
  days: GapDayV21[]
}

export interface GapGroupV21 {
  locationCode: string
  displayName: string
  city: string
  from: string
  to: string
  dayCount: number
  status: GapStatus
  time: string       // same time for all days, or "" when timeVaries
  timeVaries: boolean
  days: GapDayV21[]
  agents: GapAgentV21[]
  candidateId: string | null   // first candidate (the grouping key)
  candidates: CandidateV21[]   // ALL available candidates, in API order
  weeks: GapWeekV21[]          // days split into calendar weeks (Mon–Sun)
}

/** v2.1 grouping key: location + status + first suggested candidate (or none).
 *  Hours and people reasons no longer break a group; a day without a gap,
 *  a status change or a candidate change does. */
export function groupGapsV21(gaps: CoverageGap[]): GapGroupV21[] {
  if (gaps.length === 0) return []

  const sorted = [...gaps].sort((a, b) =>
    a.locationCode === b.locationCode
      ? a.date.localeCompare(b.date)
      : a.locationCode.localeCompare(b.locationCode))

  const firstCandidate = (g: CoverageGap): string => {
    const f = g.availableFloaters?.[0]
    return f ? (f.employeeId ?? f.fullName) : ""
  }

  const groups: CoverageGap[][] = []
  let prevKey: string | null = null
  for (const g of sorted) {
    const k = [g.locationCode, statusOf(g), firstCandidate(g)].join("~")
    if (prevKey === k && groups.length > 0) {
      groups[groups.length - 1].push(g)
    } else {
      groups.push([g])
    }
    prevKey = k
  }

  return groups.map(items => {
    const first = items[0]
    const times = new Set(items.map(windowText))
    const timeVaries = times.size > 1
    const time = timeVaries ? "" : (times.values().next().value ?? "")

    const personMap = new Map<string, { fullName: string; assignmentType: string; reasons: Set<string> }>()
    for (const g of items) {
      for (const a of g.assignedAgents) {
        const id = a.employeeId ?? a.fullName
        if (!personMap.has(id)) personMap.set(id, { fullName: a.fullName, assignmentType: a.assignmentType, reasons: new Set() })
        personMap.get(id)!.reasons.add(reasonOf(a))
      }
    }
    const agents: GapAgentV21[] = [...personMap.entries()].map(([id, v]) => ({
      employeeId: id,
      fullName: v.fullName,
      assignmentType: v.assignmentType,
      reason: v.reasons.size > 1 ? "varies" : [...v.reasons][0],
      varies: v.reasons.size > 1,
    }))

    const firstFloater = first.availableFloaters?.[0] ?? null
    const candidateId = firstFloater ? (firstFloater.employeeId ?? firstFloater.fullName) : null

    // ALL candidates: union of availableFloaters across days, in first-seen order
    // (the same order the API / old screen already returns them in).
    const candMap = new Map<string, CandidateV21>()
    for (const g of items) {
      for (const f of g.availableFloaters ?? []) {
        const id = f.employeeId ?? f.fullName
        if (!candMap.has(id)) {
          candMap.set(id, { employeeId: id, fullName: f.fullName, source: sourceOf(f), sourceType: f.assignmentType ?? "", availableDates: [] })
        }
        candMap.get(id)!.availableDates.push(g.date)
      }
    }
    const candidates = [...candMap.values()]

    const days: GapDayV21[] = items.map(g => ({
      date: g.date,
      time: windowText(g),
      present: g.present,
      required: g.required,
      agents: g.assignedAgents,
      candidateAvailable: firstFloater ? g.availableFloaters.some(f => (f.employeeId ?? f.fullName) === candidateId) : false,
    }))

    // Cut into calendar weeks (Mon–Sun) — a group spans at most one week per sub-group.
    const weeks: GapWeekV21[] = []
    for (const d of days) {
      const wk = isoWeek(new Date(d.date))
      const last = weeks[weeks.length - 1]
      if (last && last.week === wk) {
        last.days.push(d)
        last.to = d.date
      } else {
        weeks.push({ week: wk, from: d.date, to: d.date, days: [d] })
      }
    }

    return {
      locationCode: first.locationCode,
      displayName: first.displayName,
      city: first.city,
      from: first.date,
      to: items[items.length - 1].date,
      dayCount: items.length,
      status: statusOf(first),
      time,
      timeVaries,
      days,
      agents,
      candidateId,
      candidates,
      weeks,
    }
  })
}
