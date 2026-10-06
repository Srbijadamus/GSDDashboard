// ── WIC check-in status: the single definition ────────────────────────────────
//
// Extracted from WicAttendance.tsx so the arithmetic is testable and stated once.
// Deliberately free of React/DOM imports: it compiles and runs standalone under
// `tsc` + `node`, which is how checkinStatus.test.ts runs (no test framework is
// installed in this project).
//
// THE INVARIANT, which the header used to break:
//
//     Expected = CheckedIn + NotYetIn          (always, by construction)
//
// It broke because "Checked in" counted the WHOLE kiosk population while
// "Expected"/"Not yet in" came from the WIC roster. On 2026-10-01 09:08 that read
// 39 Expected / 37 Checked in / 15 Not yet in, and 37 + 15 = 52. The true split was
// 24 expected-and-in + 15 expected-not-in = 39, plus 12 people checked in who were
// never expected and one kiosk row with a blank employee_id counted as a 37th
// "person". CheckedIn is now Expected ∩ kiosk, so the three numbers partition one
// population and cannot disagree. Every other kiosk check-in is reported as one
// neutral figure, "Other staff checked in" — no inference about where they are.
//
// The kiosk's `location` field is NEVER compared with WIC centres. It is the agent's
// assigned location as free text (agents.wic_location, e.g. "Augsburg | Landshut"),
// not a statement about today, so comparing it to a centre classified people wrongly.
// What the agent actually chooses at check-in is `work_location` (the work mode) — see
// workModeOf().
//
// Populations involved (two systems, which is why this has to be explicit):
//   - roster:  GET /api/wic/cards  -> GSDDashboard (WicCardsService)
//   - kiosk:   GET {KIOSK}/api/attendance -> ShiftKioskDB, the WHOLE GSD workforce,
//              not just WIC-assigned agents.

// ── Shapes (structural subsets of the page's API types) ───────────────────────

export interface CheckinAgent {
  employeeId: string
  name: string
  shiftStart: string | null
  shiftEnd?: string | null
  /** Rostered for WIC duty with no on-site check-in record (WicCoverageEvidence.ROSTER_ONLY). */
  checkInNotRecorded?: boolean
  /** A human deliberately set this duty. Neutral "Manual" label; never a warning. */
  isManualChange?: boolean
  manualChangeBy?: string | null
  manualChangeAt?: string | null
}

export interface CheckinLocation {
  locationCode: string
  displayName: string
  city?: string
  country?: string
  todaySchedule: { isClosed: boolean }
  assignedAgents: CheckinAgent[]
}

export interface CheckinKioskRecord {
  employee_id: string
  full_name: string
  attendance_status: string
  /** Server-generated audit note for an explicit supervisor check-in/logout. */
  status_note?: string | null
  checkin_time?: string | null
  checkout_time?: string | null
  minutes_on_shift?: number
  /**
   * Timestamp of the agent's most recent kiosk event today (ShiftKiosk server.py:
   * `sl.agent_timestamp AS last_event_time`), agent-local wall clock like checkin_time.
   * When the status is PAUSE this IS the moment the break started — see breakMinutes().
   */
  last_event_time?: string | null
  /**
   * The agent's own choice at check-in: where they are working FROM. Stored by the kiosk
   * in status_log.work_location and returned by GET /api/attendance
   * (ShiftKiosk server.py: `ISNULL(sl.work_location,'') AS work_location`).
   * Values in use: "WIC" and "HOME_OFFICE"; blank on OFF/AUTO_LOGOUT/EMERGENCY events.
   */
  work_location?: string | null
  /**
   * The agent's ASSIGNED location as free text (agents.wic_location), e.g.
   * "Augsburg | Landshut". Informational only — never compared with a WIC centre.
   */
  location?: string | null
  /**
   * One of WIC/VOICE/VWIC/BO, or null/absent for an agent not yet classified
   * ("Nerazvrstani" in ShiftKiosk). Added by ShiftKiosk server.py's
   * db_get_attendance (agents.agent_group) and passed straight through by
   * KioskProxyService — see buildCheckinStatus() below for why /wic-attendance
   * now requires this field to equal "WIC".
   */
  agent_group?: string | null
}

export interface ExpectedAgent extends CheckinAgent {
  locationCode: string
  displayName: string
  city: string
  country: string
  /**
   * false = checked in at the kiosk but not on today's WIC roster. Still Expected (RTM
   * decision: anyone at work today is expected), but has no centre, so the Not-Checked-In
   * table and the per-centre views ignore them.
   */
  onRoster: boolean
}

export interface CheckinStatus {
  /** Rostered for WIC duty today at an OPEN centre (absences and GSD excluded). */
  rosterExpected: ExpectedAgent[]

  /**
   * rosterExpected PLUS everyone who checked in but is not in rosterExpected.
   * RTM decision 2026-10-01: anyone at work today is expected — ad-hoc WIC deployments,
   * Virtual WIC Support and check-ins with no work mode recorded all count. Adding every
   * checked-in person is what makes the identity below unconditional.
   */
  expected: ExpectedAgent[]

  /**
   * ALL distinct employee_ids with a kiosk ACTIVE/DONE row, regardless of roster, work
   * mode or location. Blank employee_id excluded (see blankKioskIdCount).
   */
  checkedIn: ExpectedAgent[]

  /** Expected minus Checked in — roster people with no ACTIVE/DONE kiosk row. */
  notYetIn: ExpectedAgent[]

  /** Breakdown shown under the Checked in tile: "on WIC roster X · not on roster Y". */
  checkedInOnRoster: number
  checkedInOffRoster: number

  /**
   * Checked in with work mode WIC but not on the roster, so no centre link exists
   * (the kiosk's reliable link, status_log.wic_center_id, is not returned by
   * GET /api/attendance). They keep an "Assign to centre" action and are NOT a problem:
   * an ad-hoc deployment is a legitimate manual decision.
   */
  needsCentreAssignment: CheckinKioskRecord[]

  /** Kiosk rows with no employee_id: excluded from every count, surfaced once. */
  blankKioskIdCount: number
}

// ── Constants ────────────────────────────────────────────────────────────────

/**
 * Markers that /api/wic/cards puts in shiftStart instead of a time. An agent carrying
 * one is not expected at a WIC today.
 *
 * "GSD" is in this list: WicCardsService sets it for an agent moved to GSD backlog
 * (POST /api/wic/move-to-gsd). They are doing non-WIC work and can never check in at a
 * WIC, so counting them as Expected guaranteed a permanent shortfall — and because
 * "GSD" is not HH:MM they were also silently dropped from the Not-Checked-In table,
 * landing in Expected and in no other bucket.
 */
export const ABSENCE_MARKERS: ReadonlySet<string> = new Set([
  "SICK", "SL", "AL", "HALF_AL", "UL", "OL", "PH", "LPH", "OFF", "OFF_WEEKEND", "RESIGNED",
  "GSD",
])

/**
 * Test / non-operational accounts excluded from roster, cards, coverage, forecast
 * and briefing. Explicit manual kiosk events remain visible in global attendance.
 * (GSDDashboard.API.Services.TestAccounts holds the same list).
 * One named list on purpose — never scatter these ids as literals.
 *
 * Intentionally EMPTY as of 2026-10-06: 9074466 (Stojnic Nebojsa) is a normal
 * BO-group agent now and is counted like everyone else in Pulse and ShiftKiosk.
 * The list and isTestEmployee stay in place for any future test account.
 */
export const TEST_EMPLOYEE_IDS: ReadonlySet<string> = new Set<string>()

export function isTestEmployee(employeeId: string | null | undefined): boolean {
  return !!employeeId && TEST_EMPLOYEE_IDS.has(employeeId.trim())
}

function isManualKioskEvent(record: CheckinKioskRecord): boolean {
  return /^Manual (check-in|logout) by /.test(record.status_note ?? "")
}

/**
 * The kiosk status for an agent on a break ("Pause" in the kiosk UI; raw
 * status_log.status = 'PAUSE', passed straight through by ShiftKiosk server.py).
 * A break is time ON shift — the kiosk shows "Pause beenden" and keeps counting
 * minutes on shift — so a paused agent is checked in, never "not yet in".
 */
export const ON_BREAK_STATUS = "PAUSE"

export function isOnBreak(status: string | null | undefined): boolean {
  return status === ON_BREAK_STATUS
}

/**
 * Kiosk statuses that mean "this person has checked in today".
 * PAUSE belongs here: the agent arrived and stepped away for a break, they are not
 * absent. Omitting it put every agent on a break back into "Not yet in" with a
 * growing delay (2026-10-02: Marcus Rusch, checked in 07:18, shown 347 min late).
 */
export const CHECKED_IN_STATUSES: ReadonlySet<string> = new Set(["ACTIVE", "DONE", ON_BREAK_STATUS])

/**
 * Kiosk statuses that mean "this person is at work RIGHT NOW" — per-centre presence,
 * a narrower question than CHECKED_IN_STATUSES answers. DONE is deliberately absent:
 * someone who checked out is no longer present. PAUSE is present — on site, on a break.
 */
export const PRESENT_STATUSES: ReadonlySet<string> = new Set(["ACTIVE", ON_BREAK_STATUS])

/** The work modes the kiosk offers at check-in. */
export type WorkMode = "WIC" | "HOME_OFFICE"

/**
 * i18n keys for the work-mode badge. The wording matches the kiosk's own UI so RTM reads
 * the same words in both places: HOME_OFFICE is shown there as "Virtual WIC Support" /
 * "Virtueller WIC-Support" (ShiftKiosk server.py `loc_home_office`).
 */
export const WORK_MODE_LABEL_KEY: Record<WorkMode, string> = {
  WIC: "attendance.workMode.WIC",
  HOME_OFFICE: "attendance.workMode.HOME_OFFICE",
}

/** i18n key for the neutral "On break" label shown next to a paused agent's badge. */
export const ON_BREAK_LABEL_KEY = "attendance.onBreak"

/** i18n key for the neutral break-duration chip, e.g. "Pause 41m". Takes {{n}}. */
export const ON_BREAK_DURATION_KEY = "attendance.onBreakFor"

/**
 * The agent's work-mode choice, or null when the kiosk recorded none (non-check-in
 * events carry a blank value). Unrecognised values return null rather than being
 * guessed at, so a new kiosk option shows no badge instead of a wrong one.
 */
export function workModeOf(record: CheckinKioskRecord | null | undefined): WorkMode | null {
  const raw = record?.work_location?.trim().toUpperCase()
  if (raw === "WIC" || raw === "HOME_OFFICE") return raw
  return null
}

/**
 * Centre-local timezone. Every centre is CET/CEST today (DE and NL never diverge), but
 * the mapping is explicit so the delay maths is anchored to the CENTRE rather than to
 * whatever timezone the viewer's browser happens to be in.
 */
const COUNTRY_TIMEZONE: Record<string, string> = {
  DE: "Europe/Berlin",
  NL: "Europe/Amsterdam",
}

export function centreTimeZone(country: string | null | undefined): string {
  return (country && COUNTRY_TIMEZONE[country.toUpperCase()]) || "Europe/Berlin"
}

// ── Small helpers ────────────────────────────────────────────────────────────

export function isBlankKioskId(id: string | null | undefined): boolean {
  return id === null || id === undefined || id.trim() === ""
}

export function isAbsenceMarker(shiftStart: string | null | undefined): boolean {
  return !!shiftStart && ABSENCE_MARKERS.has(shiftStart.trim().toUpperCase())
}

/** Minutes from midnight for "HH:MM", or null when the value is not a time. */
export function parseHhMm(value: string | null | undefined): number | null {
  if (!value || !/^\d{1,2}:\d{2}$/.test(value.trim())) return null
  const [h, m] = value.trim().split(":").map(Number)
  if (h > 23 || m > 59) return null
  return h * 60 + m
}

/** Current minutes from midnight IN the given timezone. */
export function nowMinutesInZone(timeZone: string, now: Date = new Date()): number {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone, hour: "2-digit", minute: "2-digit", hourCycle: "h23",
  }).formatToParts(now)
  const h = Number(parts.find(p => p.type === "hour")?.value ?? "0")
  const m = Number(parts.find(p => p.type === "minute")?.value ?? "0")
  return h * 60 + m
}

/**
 * How long the agent has been on a break, in minutes, or null when that cannot be
 * computed (not on a break, no last_event_time, unparseable timestamp, or a clock skew
 * that would make the answer negative — better no figure than a wrong one).
 *
 * The PAUSE event's own timestamp is the start of the break: the kiosk writes one row
 * per state change, so the latest event while paused is the "Pause" press itself.
 * Measured against the CENTRE's local time, exactly like the Zone B delay, because
 * kiosk timestamps are agent-local wall clock and the viewer may sit in another zone.
 */
export function breakMinutes(
  record: CheckinKioskRecord | null | undefined,
  timeZone: string,
  now: Date = new Date(),
): number | null {
  if (!record || !isOnBreak(record.attendance_status)) return null
  const startedAt = parseHhMm(record.last_event_time?.slice(11, 16))
  if (startedAt === null) return null
  const elapsed = nowMinutesInZone(timeZone, now) - startedAt
  return elapsed < 0 ? null : elapsed
}

// ── The header numbers ───────────────────────────────────────────────────────

export function buildCheckinStatus(
  cards: CheckinLocation[] | null | undefined,
  kiosk: CheckinKioskRecord[] | null | undefined,
): CheckinStatus {
  const allCards = cards ?? []
  // Group separation (2026-10-06): ShiftKiosk now serves Voice/VWIC/BO as well as
  // WIC, all from the same /api/attendance feed. Before agent_group existed, this
  // page treated "anyone checked in" as potentially a WIC ad-hoc deployment (the
  // RTM decision above). Now that the kiosk labels every row, a Voice/VWIC/BO/
  // unclassified check-in is no longer ambiguous and must NOT inflate Expected/
  // Checked-in/Not-yet-in here — those agents have their own page
  // (Remote Attendance). Only rows explicitly labelled "WIC" are kept.
  const allKiosk = (kiosk ?? [])
    .filter(r => !isTestEmployee(r.employee_id) || isManualKioskEvent(r))
    .filter(r => (r.agent_group ?? "").trim().toUpperCase() === "WIC")

  // ── Roster side: rostered at an OPEN centre, one row per employee ──────────
  const rosterExpected: ExpectedAgent[] = []
  const seen = new Set<string>()
  for (const card of allCards) {
    if (card.todaySchedule?.isClosed) continue
    for (const agent of card.assignedAgents ?? []) {
      if (isBlankKioskId(agent.employeeId)) continue
      if (isTestEmployee(agent.employeeId)) continue
      if (seen.has(agent.employeeId)) continue
      if (!agent.shiftStart || isAbsenceMarker(agent.shiftStart)) continue
      seen.add(agent.employeeId)
      rosterExpected.push({
        ...agent,
        locationCode: card.locationCode,
        displayName: card.displayName,
        city: card.city ?? "",
        country: card.country ?? "",
        onRoster: true,
      })
    }
  }

  // ── Kiosk side ────────────────────────────────────────────────────────────
  const checkedInRows = allKiosk.filter(r => CHECKED_IN_STATUSES.has(r.attendance_status))
  const blankKioskIdCount = checkedInRows.filter(r => isBlankKioskId(r.employee_id)).length

  // One row per employee; a blank id is nobody.
  const kioskById = new Map<string, CheckinKioskRecord>()
  for (const r of checkedInRows) {
    if (isBlankKioskId(r.employee_id)) continue
    if (!kioskById.has(r.employee_id)) kioskById.set(r.employee_id, r)
  }

  // Everyone checked in but not on the roster becomes Expected too, with no centre.
  const offRoster: ExpectedAgent[] = []
  for (const [id, record] of kioskById) {
    if (seen.has(id)) continue
    offRoster.push({
      employeeId: id,
      name: record.full_name,
      shiftStart: null,
      locationCode: "",
      displayName: "",
      city: "",
      country: "",
      onRoster: false,
    })
  }

  const expected = [...rosterExpected, ...offRoster]
  // checkedIn is every distinct kiosk ACTIVE/DONE id. Because every such id is either in
  // rosterExpected or was just added to offRoster, checkedIn is a subset of expected and
  // the identity below cannot be violated.
  const checkedIn = expected.filter(a => kioskById.has(a.employeeId))
  const notYetIn = expected.filter(a => !kioskById.has(a.employeeId))

  return {
    rosterExpected,
    expected,
    checkedIn,
    notYetIn,
    checkedInOnRoster: checkedIn.filter(a => a.onRoster).length,
    checkedInOffRoster: checkedIn.filter(a => !a.onRoster).length,
    needsCentreAssignment: offRoster
      .map(a => kioskById.get(a.employeeId)!)
      .filter(r => workModeOf(r) === "WIC"),
    blankKioskIdCount,
  }
}

// ── Display rows (Checked in drawer, Currently Present, Other staff) ─────────

/**
 * One person as the UI shows them. Built here rather than inline in the page so the
 * display contract is testable: **location is shown, the id is secondary**.
 */
export interface CheckinDisplayRow {
  key: string
  name: string
  /** Secondary, muted. Shown after the location — never instead of it. */
  employeeId: string
  /**
   * The kiosk `location` text, passed through VERBATIM (e.g. "Hannover | Emmerthal",
   * "Landshut | Essenbach") because RTM needs to see where the kiosk says the agent is
   * assigned.
   *
   * DISPLAY ONLY. It is never parsed, split, normalised, compared with a WIC centre,
   * matched, counted or used to flag anyone — all matching and counting is by
   * employee_id (see buildPresencePair / buildCheckinStatus). Treating this free-text
   * field as data is what produced the wrong per-centre presence numbers.
   */
  location: string | null
  /** The agent's own work-mode choice; drives the badge next to the name. */
  workMode: WorkMode | null
  /** Neutral "Manual" label (who/when in the tooltip). Never a warning. */
  isManualChange: boolean
  manualChangeBy: string | null
  manualChangeAt: string | null
  checkinTime: string | null
  checkoutTime: string | null
  /** Last kiosk event today; the break start when status is PAUSE. See breakMinutes(). */
  lastEventTime: string | null
  status: string
  shiftStart?: string | null
}

/** A roster agent (expected / checked in / not yet in), with their kiosk row if any. */
export function displayRowForAgent(
  agent: ExpectedAgent,
  record: CheckinKioskRecord | null | undefined,
  fallbackStatus: string,
): CheckinDisplayRow {
  return {
    key: agent.employeeId,
    name: agent.name,
    employeeId: agent.employeeId,
    location: record?.location ?? null,
    workMode: workModeOf(record),
    isManualChange: !!agent.isManualChange,
    manualChangeBy: agent.manualChangeBy ?? null,
    manualChangeAt: agent.manualChangeAt ?? null,
    checkinTime: record?.checkin_time ?? null,
    checkoutTime: record?.checkout_time ?? null,
    lastEventTime: record?.last_event_time ?? null,
    status: record?.attendance_status ?? fallbackStatus,
    shiftStart: agent.shiftStart,
  }
}

/** A kiosk check-in with no roster row today (the "other staff" list, presence chips). */
export function displayRowForKioskRecord(record: CheckinKioskRecord): CheckinDisplayRow {
  return {
    key: record.employee_id,
    name: record.full_name,
    employeeId: record.employee_id,
    location: record.location ?? null,
    workMode: workModeOf(record),
    isManualChange: false,
    manualChangeBy: null,
    manualChangeAt: null,
    checkinTime: record.checkin_time ?? null,
    checkoutTime: record.checkout_time ?? null,
    lastEventTime: record.last_event_time ?? null,
    status: record.attendance_status,
  }
}

// ── Not Checked In (Zone B) ──────────────────────────────────────────────────

export type ZoneBKioskStatus = "NO_RECORD" | "NOT_CHECKED_IN" | "UNKNOWN_STATUS"

export interface ZoneBRow {
  employeeId: string
  name: string
  locationCode: string
  locationCity: string
  shiftStart: string
  delayMinutes: number
  shiftStarted: boolean
  /** false when shiftStart is not a time, so no delay can be computed. */
  shiftTimeKnown: boolean
  kioskStatus: ZoneBKioskStatus
  rawKioskStatus?: string
  checkInNotRecorded: boolean
}

/**
 * Built from `status.notYetIn` — the SAME Expected set the header counts — so the table
 * and the "Not yet in" figure can never disagree. Delay is measured against the
 * CENTRE's local time, not the browser's.
 */
export function buildZoneBRows(
  status: CheckinStatus,
  kiosk: CheckinKioskRecord[] | null | undefined,
  now: Date = new Date(),
): ZoneBRow[] {
  const kioskById = new Map((kiosk ?? []).map(r => [r.employee_id, r]))
  const nowByZone = new Map<string, number>()

  // Only roster agents can be "not yet in" against a shift start; an off-roster
  // check-in has no rostered shift, and by definition is already checked in anyway.
  return status.notYetIn.filter(a => a.onRoster).map(agent => {
    const zone = centreTimeZone(agent.country)
    if (!nowByZone.has(zone)) nowByZone.set(zone, nowMinutesInZone(zone, now))
    const nowMin = nowByZone.get(zone)!

    const shiftMin = parseHhMm(agent.shiftStart)
    const shiftTimeKnown = shiftMin !== null
    const shiftStarted = shiftTimeKnown && nowMin >= shiftMin!
    const delayMinutes = shiftStarted ? nowMin - shiftMin! : 0

    const record = kioskById.get(agent.employeeId)
    let kioskStatus: ZoneBKioskStatus = "NO_RECORD"
    let rawKioskStatus: string | undefined
    if (record) {
      if (record.attendance_status === "NOT_CHECKED_IN") {
        kioskStatus = "NOT_CHECKED_IN"
      } else {
        kioskStatus = "UNKNOWN_STATUS"
        rawKioskStatus = record.attendance_status
      }
    }

    return {
      employeeId: agent.employeeId,
      name: agent.name,
      locationCode: agent.locationCode,
      locationCity: agent.city,
      shiftStart: agent.shiftStart ?? "",
      delayMinutes,
      shiftStarted,
      shiftTimeKnown,
      kioskStatus,
      rawKioskStatus,
      checkInNotRecorded: !!agent.checkInNotRecorded,
    }
  })
}

export interface ZoneBGroups {
  emergency: ZoneBRow[]
  main: ZoneBRow[]
  noRecord: ZoneBRow[]
  upcoming: ZoneBRow[]
}

/** Every row lands in exactly one of main/noRecord/upcoming — none is dropped. */
export function groupZoneBRows(rows: ZoneBRow[]): ZoneBGroups {
  return {
    emergency: rows.filter(r => r.rawKioskStatus === "EMERGENCY"),
    main: rows
      .filter(r => r.shiftTimeKnown && r.shiftStarted && r.kioskStatus !== "NO_RECORD")
      .sort((a, b) => b.delayMinutes - a.delayMinutes),
    // Unparseable shift times join the no-kiosk-record panel rather than vanishing.
    noRecord: rows.filter(r => !r.shiftTimeKnown || (r.shiftStarted && r.kioskStatus === "NO_RECORD")),
    upcoming: rows
      .filter(r => r.shiftTimeKnown && !r.shiftStarted)
      .sort((a, b) => a.shiftStart.localeCompare(b.shiftStart)),
  }
}

// ── Per-centre presence: kiosk reality vs roster ─────────────────────────────

export interface PresencePair {
  /**
   * Rostered here today AND kiosk-present (ACTIVE or on a break), by employee_id only.
   * Their work mode still has to be read off each record: a HOME_OFFICE check-in is
   * NOT on-site presence, so the UI shows the badge rather than implying it.
   */
  presentKiosk: CheckinKioskRecord[]
  /** Rostered for WIC duty here today (absences/GSD excluded). */
  rostered: CheckinAgent[]
  /** Rostered here but no present (ACTIVE / PAUSE) kiosk record. */
  rosteredNotPresent: CheckinAgent[]
}

export function buildPresencePair(
  centre: CheckinLocation | null | undefined,
  kiosk: CheckinKioskRecord[] | null | undefined,
): PresencePair {
  const empty: PresencePair = { presentKiosk: [], rostered: [], rosteredNotPresent: [] }
  if (!centre) return empty

  const rostered = (centre.assignedAgents ?? []).filter(
    a => a.shiftStart && !isAbsenceMarker(a.shiftStart),
  )
  const rosteredIds = new Set(rostered.map(a => a.employeeId))

  // employee_id ONLY — BLUEPRINT_SCREENS.md §6: kiosk records are matched to employees
  // by employee_id, "never by location name". The kiosk location is the agent's assigned
  // centres as free text, so it cannot say where anyone is today.
  const active = (kiosk ?? []).filter(
    r => PRESENT_STATUSES.has(r.attendance_status) && !isBlankKioskId(r.employee_id)
      && !isTestEmployee(r.employee_id),
  )
  const presentKiosk = active.filter(r => rosteredIds.has(r.employee_id))
  const presentIds = new Set(presentKiosk.map(r => r.employee_id))

  return {
    presentKiosk,
    rostered,
    rosteredNotPresent: rostered.filter(a => !presentIds.has(a.employeeId)),
  }
}
