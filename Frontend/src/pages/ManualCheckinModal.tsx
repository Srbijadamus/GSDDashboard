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
    background: "var(--card2)",
    border: "1px solid var(--border)",
    color: "var(--text)",
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
        style={{
          background: "var(--card)", border: "1px solid var(--border)",
          borderRadius: 12, padding: 24, width: 380, maxWidth: "90vw",
          boxShadow: "0 20px 60px rgba(0,0,0,0.5)",
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 20 }}>
          <Clock size={16} color="var(--accent)" />
          <span style={{ fontSize: 15, fontWeight: 600, color: "var(--text)" }}>{p("title")}</span>
        </div>

        {success && (
          <div style={{
            background: "rgba(34,208,122,.12)", border: "1px solid rgba(34,208,122,.3)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--green)", marginBottom: 14,
          }}>
            {success}
          </div>
        )}
        {error && (
          <div style={{
            background: "rgba(255,59,92,.12)", border: "1px solid rgba(255,59,92,.3)",
            borderRadius: 8, padding: "10px 14px", fontSize: 12, color: "var(--danger)", marginBottom: 14,
          }}>
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
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

          <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
            {p("action")}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8, marginTop: 4 }}>
              {(["checkin", "logout"] as const).map(act => (
                <button
                  key={act}
                  type="button"
                  onClick={() => setAction(act)}
                  style={{
                    padding: "7px 0",
                    borderRadius: 6,
                    fontSize: 12,
                    fontWeight: action === act ? 600 : 400,
                    cursor: "pointer",
                    background: action === act
                      ? act === "checkin" ? "rgba(34,208,122,.2)" : "rgba(255,59,92,.2)"
                      : "var(--card2)",
                    border: action === act
                      ? act === "checkin" ? "1px solid rgba(34,208,122,.5)" : "1px solid rgba(255,59,92,.5)"
                      : "1px solid var(--border)",
                    color: action === act
                      ? act === "checkin" ? "var(--green)" : "var(--danger)"
                      : "var(--text3)",
                  }}
                >
                  {p(act)}
                </button>
              ))}
            </div>
          </label>

          <label style={{ fontSize: 11, color: "var(--text3)", display: "block" }}>
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
              style={{
                flex: 1, background: "var(--card2)", border: "1px solid var(--border)",
                color: "var(--text)", borderRadius: 6, padding: "8px 0",
                fontSize: 12, cursor: "pointer",
              }}
            >
              {p("cancel")}
            </button>
            <button
              type="submit"
              disabled={submitting || agentId === ""}
              style={{
                flex: 2, background: "var(--accent)", border: "none", color: "#fff",
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
