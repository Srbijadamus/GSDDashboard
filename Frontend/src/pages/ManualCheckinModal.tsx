import { useState, useEffect } from "react"
import { useTranslation } from "react-i18next"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Clock } from "lucide-react"

const KIOSK_BASE = "https://ssr7tm2l-8000.euw.devtunnels.ms"
const KIOSK_KEY  = "FbHCS6VuEflvTKCgLUvvf-eEyJKxgnbd3McQzq4l0PM"

interface KioskAgent {
  id: number
  full_name: string
  employee_id: string
  team_leader: string | null
  location: string | null
  active: number
}

interface ManualCheckinModalProps {
  isOpen: boolean
  onClose: () => void
}

export function ManualCheckinModal({ isOpen, onClose }: ManualCheckinModalProps) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const p = (k: string, opts?: Record<string, unknown>): string =>
    String(t(`attendance.manualCheckin.${k}`, opts as never))

  const [agentId, setAgentId] = useState<number | "">("")
  const [action, setAction] = useState<"checkin" | "logout">("checkin")
  const [performedBy, setPerformedBy] = useState("Supervisor")
  const [submitting, setSubmitting] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isOpen) {
      setAgentId("")
      setAction("checkin")
      setPerformedBy("Supervisor")
      setSuccess(null)
      setError(null)
    }
  }, [isOpen])

  const { data: agents = [] } = useQuery<KioskAgent[]>({
    queryKey: ["kiosk-agents"],
    queryFn: () =>
      fetch(`${KIOSK_BASE}/api/agents`, {
        headers: { "X-API-Key": KIOSK_KEY },
      }).then(r => r.json()),
    staleTime: 5 * 60 * 1000,
    enabled: isOpen,
  })

  const inputStyle: React.CSSProperties = {
    width: "100%",
    background: "rgb(var(--surface-sunken))",
    border: "1px solid rgb(var(--border-subtle))",
    color: "rgb(var(--text-primary))",
    padding: "6px 10px",
    borderRadius: 6,
    fontSize: 12,
    fontFamily: "IBM Plex Sans",
    outline: "none",
    boxSizing: "border-box",
    marginTop: 4,
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (agentId === "") return
    setSubmitting(true)
    setError(null)
    setSuccess(null)
    const endpoint = action === "checkin" ? "/api/manual-checkin" : "/api/manual-logout"
    try {
      const res = await fetch(`${KIOSK_BASE}${endpoint}`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-API-Key": KIOSK_KEY,
        },
        body: JSON.stringify({
          agent_id: agentId,
          performed_by: performedBy || "Supervisor",
          note: "",
        }),
      })
      if (!res.ok) {
        const body = await res.text().catch(() => "")
        throw new Error(`HTTP ${res.status}${body ? ": " + body : ""}`)
      }
      const selected = agents.find(a => a.id === agentId)
      const actionLabel = action === "checkin" ? p("checkin").toLowerCase() : p("logout").toLowerCase()
      setSuccess(p("success", { name: selected?.full_name ?? String(agentId), action: actionLabel }))
      queryClient.invalidateQueries({ queryKey: ["kiosk-attendance"] })
      setTimeout(() => { setSuccess(null); onClose() }, 1800)
    } catch (err) {
      setError(String(err))
    } finally {
      setSubmitting(false)
    }
  }

  if (!isOpen) return null

  return (
    <div
      style={{
        position: "fixed", inset: 0, zIndex: 1000,
        background: "rgba(0,0,0,0.55)",
        display: "flex", alignItems: "center", justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        className="bg-raised border border-line-subtle"
        style={{
          borderRadius: 12, padding: 24, width: 380, maxWidth: "90vw",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
          <Clock size={16} className="text-info-fg" />
          <span className="text-ink" style={{ fontSize: 15, fontWeight: 600 }}>{p("title")}</span>
        </div>

        {success && (
          <div className="bg-good-bg border border-good-bd text-good-fg" style={{
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }}>
            {success}
          </div>
        )}
        {error && (
          <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{
            borderRadius: 8, padding: "10px 14px", fontSize: 12, marginBottom: 14,
          }}>
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("agent")}
            <select
              value={agentId}
              onChange={e => setAgentId(e.target.value === "" ? "" : Number(e.target.value))}
              required
              style={inputStyle}
            >
              <option value="">{p("selectAgent")}</option>
              {agents.map(a => (
                <option key={a.id} value={a.id}>{a.full_name}</option>
              ))}
            </select>
          </label>

          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("action")}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8, marginTop: 4 }}>
              {(["checkin", "logout"] as const).map(act => (
                <button
                  key={act}
                  type="button"
                  onClick={() => setAction(act)}
                  className={
                    action === act
                      ? act === "checkin"
                        ? "bg-good-bg border border-good-bd text-good-fg"
                        : "bg-crit-bg border border-crit-bd text-crit-fg"
                      : "bg-sunken border border-line-subtle text-ink-soft"
                  }
                  style={{
                    padding: "7px 0",
                    borderRadius: 6,
                    fontSize: 12,
                    fontWeight: action === act ? 600 : 400,
                    cursor: "pointer",
                  }}
                >
                  {p(act)}
                </button>
              ))}
            </div>
          </label>

          <label className="text-ink-soft" style={{ fontSize: 11, display: "block" }}>
            {p("performedBy")}
            <input
              type="text"
              value={performedBy}
              onChange={e => setPerformedBy(e.target.value)}
              style={inputStyle}
            />
          </label>

          <div style={{ display: "flex", gap: 10, marginTop: 6 }}>
            <button
              type="button"
              onClick={onClose}
              className="bg-sunken border border-line-subtle text-ink"
              style={{
                flex: 1, borderRadius: 6, padding: "8px 0",
                fontSize: 12, cursor: "pointer",
              }}
            >
              {p("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting || agentId === ""}
              className="bg-info-solid"
              style={{
                flex: 2, border: "none", color: "#fff",
                borderRadius: 6, padding: "8px 0", fontSize: 12, fontWeight: 600,
                cursor: submitting || agentId === "" ? "not-allowed" : "pointer",
                opacity: submitting || agentId === "" ? 0.6 : 1,
              }}
            >
              {submitting ? p("saving") : p("save")}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
