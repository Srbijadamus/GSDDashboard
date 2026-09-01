export function tokenColor(name: string): string {
  const v = getComputedStyle(document.documentElement)
    .getPropertyValue(name).trim()           // e.g. "18 158  99" (space-separated channels)
  if (!v) return 'rgb(152, 162, 179)'
  // SVG 1.1 path attributes require comma-separated rgb() — CSS4 space form fails in some parsers.
  const parts = v.split(/\s+/).filter(Boolean)
  return parts.length === 3
    ? `rgb(${parts[0]}, ${parts[1]}, ${parts[2]})`
    : 'rgb(152, 162, 179)'
}

export const statusColor = (s: string): string => tokenColor(
  s === 'COVERED'   ? '--st-good-solid'    :
  s === 'PARTIAL'   ? '--st-warn-solid'    :
  s === 'UNCOVERED' ? '--st-crit-solid'    :
                      '--st-neutral-solid'
)
