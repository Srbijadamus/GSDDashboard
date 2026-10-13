// Per-DATE WIC opening hours for the Assign Agent dialog.
//
// The dialog used to read the weekly pattern (/api/wicschedule/opening-hours) and pick the
// row by weekday, so a manual exception day (or a Rollout day) was invisible: an OPEN
// exception still showed the red "closed" banner, and a CLOSED exception still showed the
// pattern's hours with no banner at all. The hours for the selected DATE must come from the
// same resolver every other surface uses — GET /api/wic/open?date=...&horizon=1, which
// projects WicHoursResolver (exception row before the weekday pattern, then IsClosed, plus
// public holidays). This module is the single pure translation of that payload into the
// dialog's closed flag and pre-filled times, so it is directly testable without a DOM.
//
// No React import here on purpose: this file is compiled on its own by
// `npm run test:wic-open-hours`.

export interface WicOpenInterval {
  openTime: string
  closeTime: string
}

export interface WicOpenLocationStatus {
  locationCode: string
  isOpen: boolean
  openIntervals?: WicOpenInterval[] | null
}

export interface WicOpenDay {
  date: string
  locations?: WicOpenLocationStatus[] | null
}

export interface ResolvedDayHours {
  /** True only when the centre is really closed on that DATE (or the date is unknown). */
  closed: boolean
  /** Duty window: first interval's open through the last interval's close. "" when open without times. */
  start: string
  end: string
}

const CLOSED: ResolvedDayHours = { closed: true, start: "", end: "" }

/**
 * The centre's answer for one date, from the /api/wic/open payload.
 *
 * A location missing from the day (or a day missing from the payload) is treated as closed,
 * exactly like the previous weekly lookup treated a missing weekday: the dialog only ever
 * clears the times and shows the banner, it never writes anything by itself.
 */
export function resolveDayHours(
  days: WicOpenDay[] | null | undefined,
  date: string,
  locationCode: string,
): ResolvedDayHours {
  if (!days || days.length === 0 || !date || !locationCode) return CLOSED

  const day = days.find(d => d.date === date) ?? days[0]
  const loc = day?.locations?.find(l => l.locationCode === locationCode)
  if (!loc || !loc.isOpen) return CLOSED

  const intervals = (loc.openIntervals ?? []).filter(
    i => i && (i.openTime || i.closeTime),
  )
  if (intervals.length === 0) return { closed: false, start: "", end: "" }

  // A centre with a second interval is open either side of a break; the duty window spans
  // first open through last close, the same WicHoursResolver.DutyWindow rule the backend
  // writes to ShiftEntries.
  return {
    closed: false,
    start: intervals[0].openTime ?? "",
    end: intervals[intervals.length - 1].closeTime ?? "",
  }
}
