// Europe/Berlin display time for client-side stamps.
//
// The requirement (2026-10-12, Pulse part of the timezone task): every time the user
// sees is Europe/Berlin, while the database keeps UTC. Server-side values go through
// Backend/Services/BerlinTime.cs; this is the same rule for the few stamps that are
// produced in the browser (the printed shift plan's "Generated:" line).
//
// `new Date().toLocaleString()` is NOT that: it renders in the viewer's own time zone,
// which can be anywhere, and changes silently when the laptop travels. Pinning the
// IANA zone makes the text identical for every viewer, DST included.

export const BERLIN_TIME_ZONE = "Europe/Berlin"

/**
 * "YYYY-MM-DD HH:mm:ss" in Europe/Berlin for the given instant (default: now).
 *
 * Composed from formatToParts instead of a locale's dateStyle, so the output never
 * depends on the ICU default locale or on how a locale happens to order the fields.
 */
export function berlinStamp(date: Date = new Date()): string {
  const parts = new Intl.DateTimeFormat("en-GB", {
    timeZone: BERLIN_TIME_ZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  }).formatToParts(date)

  const get = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find(p => p.type === type)?.value ?? "00"

  return `${get("year")}-${get("month")}-${get("day")} ${get("hour")}:${get("minute")}:${get("second")}`
}
