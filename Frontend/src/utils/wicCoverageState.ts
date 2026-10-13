// Rule 3 (2026-10-13 Overview fix): a failed /api/wic/coverage-gaps call
// (401, 403, network, 5xx) must NEVER render as "no WIC coverage gaps".
//
// The banner and the Coverage Risk list both derive their state from this one
// function, so an error is a neutral "couldn't load" state in both — never a green
// all-clear and never an empty 0-gap list.

export type WicCoverageBannerState = "disabled" | "loading" | "error" | "clear" | "gaps"

export interface WicCoverageStateInput {
  /** false when the caller is not allowed to read the endpoint (query never fired). */
  enabled: boolean
  isLoading: boolean
  isError: boolean
  hasData: boolean
  gapCount: number
}

/**
 * Single decision for the whole WIC coverage surface. Error wins over any cached
 * data: an all-clear is only ever shown when a successful response is actually in
 * hand, so a 401 can never be mistaken for "no gaps".
 */
export function coverageBannerState(input: WicCoverageStateInput): WicCoverageBannerState {
  if (!input.enabled) return "disabled"
  if (input.isLoading) return "loading"
  if (input.isError || !input.hasData) return "error"
  return input.gapCount > 0 ? "gaps" : "clear"
}
