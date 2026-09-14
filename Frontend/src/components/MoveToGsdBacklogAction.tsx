import { useState, useEffect, type ReactNode } from "react"
import { useTranslation } from "react-i18next"
import { useQueryClient } from "@tanstack/react-query"
import { AlertTriangle } from "lucide-react"
import { apiFetch } from "../api/client"

interface WicCard {
  locationCode: string
  displayName: string
  assignedAgents?: { employeeId: string; coverageMatch?: string; shiftStart?: string }[]
}

interface MoveToGsdBacklogActionProps {
  employeeId: string
  agentName: string
  shiftDate: string
  onSuccess?: () => void
  children: (state: { onClick: () => void; isPending: boolean }) => ReactNode
}

export function MoveToGsdBacklogAction({ employeeId, agentName, shiftDate, onSuccess, children }: MoveToGsdBacklogActionProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const m = (k: string, opts?: Record<string, unknown>): string =>
    String(t(`wicShifts.moveToBacklog.${k}`, opts as never))

  const [isPending, setIsPending] = useState(false)
  const [confirm, setConfirm] = useState<{ location: string } | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!error || confirm) return
    const id = setTimeout(() => setError(null), 4000)
    return () => clearTimeout(id)
  }, [error, confirm])

  const doMove = async () => {
    setIsPending(true)
    setError(null)
    try {
      await apiFetch("/api/wic/move-to-gsd", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ employeeId, date: shiftDate }),
      })
      queryClient.invalidateQueries({ queryKey: ["wic-cards", shiftDate], exact: true })
      queryClient.invalidateQueries({ queryKey: ["wic-cards-v2"] })
      queryClient.invalidateQueries({ queryKey: ["wic-forecast"] })
      queryClient.invalidateQueries({ queryKey: ["wic-attendance-forecast"] })
      queryClient.invalidateQueries({ queryKey: ["wic-conflicts"] })
      onSuccess?.()
    } catch {
      setError(m("error"))
    } finally {
      setIsPending(false)
    }
  }

  const onClick = async () => {
    setError(null)
    try {
      const cards = await queryClient.fetchQuery<WicCard[]>({
        queryKey: ["wic-cards", shiftDate],
        queryFn: () => apiFetch<WicCard[]>(`/api/wic/cards?date=${shiftDate}`),
      })
      const sourceCard = cards.find(c => c.assignedAgents?.some(a => a.employeeId === employeeId))
      if (sourceCard) {
        const otherAgents = (sourceCard.assignedAgents ?? []).filter(
          a => a.employeeId !== employeeId &&
          a.coverageMatch !== "NONE" &&
          a.shiftStart !== "SICK" && a.shiftStart !== "AL"
        )
        if (otherAgents.length === 0) {
          setConfirm({ location: sourceCard.displayName })
          return
        }
      }
    } catch {
      // If the coverage lookup fails, fall through and let the move proceed —
      // the backend is the source of truth and this is only a UX warning.
    }
    await doMove()
  }

  return (
    <>
      {children({ onClick, isPending })}
      {error && !confirm && (
        <div
          role="alert"
          style={{ position: "fixed", bottom: 24, right: 24, zIndex: 4000, background: "rgb(var(--st-crit-solid))", color: "#fff", padding: "10px 18px", borderRadius: 8, fontSize: 12, boxShadow: "0 4px 16px rgba(0,0,0,0.35)" }}
        >
          {error}
        </div>
      )}
      {confirm && (
        <div
          style={{ position: "fixed", inset: 0, zIndex: 3000, background: "rgba(0,0,0,0.6)", display: "flex", alignItems: "center", justifyContent: "center" }}
          onClick={e => e.target === e.currentTarget && setConfirm(null)}
        >
          <div className="bg-raised border border-warn-bd" style={{ borderRadius: 12, padding: 24, width: 400, maxWidth: "90vw" }}>
            <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 14 }}>
              <AlertTriangle size={16} className="text-warn-fg" />
              <span className="text-ink" style={{ fontWeight: 600, fontSize: 14 }}>{m("confirmTitle")}</span>
            </div>
            <p className="text-ink-muted" style={{ fontSize: 13, marginBottom: 20 }}>
              {m("confirmBody", { agent: agentName, location: confirm.location, date: shiftDate })}
            </p>
            {error && (
              <div className="text-crit-fg" style={{ fontSize: 12, marginBottom: 14 }}>{error}</div>
            )}
            <div style={{ display: "flex", gap: 10 }}>
              <button
                onClick={() => setConfirm(null)}
                className="bg-info-solid"
                style={{ flex: 2, border: "none", color: "#fff", borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600, cursor: "pointer" }}
              >
                {m("cancel")}
              </button>
              <button
                onClick={() => { setConfirm(null); doMove() }}
                disabled={isPending}
                className="bg-transparent border border-warn-bd text-warn-fg"
                style={{ flex: 1, borderRadius: 6, padding: "8px 0", fontSize: 12, cursor: isPending ? "not-allowed" : "pointer", opacity: isPending ? 0.6 : 1 }}
              >
                {m("moveAnyway")}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
