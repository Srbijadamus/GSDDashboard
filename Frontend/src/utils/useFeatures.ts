import { useQuery } from "@tanstack/react-query"
import { apiFetch } from "../api/client"
import { useAuth } from "../auth/AuthContext"

interface FeaturesResponse {
  overviewV2: boolean
  whatsNew: boolean
}

function readLocal(key: string): string | null {
  try { return window.localStorage.getItem(key) } catch { return null }
}

export function setOverviewV2Override(kid: string, value: "old" | "new") {
  try { window.localStorage.setItem(`pulse.overviewV2.${kid}`, value) } catch { /* unavailable */ }
}

/**
 * Feature flags from GET /api/features, combined with the per-user "revert to old
 * design" override (localStorage, no DB). When localStorage is unavailable the
 * server flag alone decides (the "default design").
 */
export function useFeatures() {
  const { session } = useAuth()
  const { data } = useQuery({
    queryKey: ["features"],
    queryFn: () => apiFetch<FeaturesResponse>("/api/features"),
  })

  const serverOverviewV2 = data?.overviewV2 ?? false
  const whatsNew = data?.whatsNew ?? false

  const kid = session?.kid ?? "anon"
  const override = readLocal(`pulse.overviewV2.${kid}`)
  const overviewV2 = serverOverviewV2 && override !== "old"

  return { overviewV2, whatsNew, serverOverviewV2 }
}
