import { useState, useRef, useEffect } from "react"
import { useMutation } from "@tanstack/react-query"
import { MessageCircle, X, Send, Bot, RefreshCw, HelpCircle } from "lucide-react"
import { apiFetch } from "../api/client"

// ─── Types ────────────────────────────────────────────────────────────────────

interface TableRow {
  employee: string
  employeeId: string
  start: string
  end: string
  workDays?: number
  wicLocation: string
  role: string
}

interface AssistantResponse {
  answerText: string
  dateRangeChecked: string
  table?: TableRow[]
  error?: string
  hint?: string
}

interface ChatMessage {
  id: number
  role: "user" | "assistant"
  text: string
  table?: TableRow[]
  dateRange?: string
  hint?: string
  isError?: boolean
}

// ─── Constants ────────────────────────────────────────────────────────────────

let _id = 0
const nextId = () => ++_id

const GROUPED_ACTIONS = [
  {
    domain: "WIC Leave",
    actions: [
      { label: "Leave next 2 weeks", q: "Who is on WIC leave in the next two weeks?" },
      { label: "Lowest coverage",    q: "Which day has the lowest WIC coverage?" },
    ],
  },
  {
    domain: "Sick Leave",
    actions: [
      { label: "Sick today", q: "Who is sick today?" },
    ],
  },
  {
    domain: "All Employees",
    actions: [
      { label: "All vacation", q: "Show all employee vacation next week" },
      { label: "AL balance",   q: "Show AL balance" },
    ],
  },
  {
    domain: "Ops",
    actions: [
      { label: "Dashboard today", q: "Show dashboard summary today" },
      { label: "Pipeline",        q: "Show pipeline events" },
      { label: "Training",        q: "What training sessions are scheduled?" },
    ],
  },
  {
    domain: "WIC Coverage",
    actions: [
      { label: "WIC forecast",  q: "WIC coverage forecast" },
      { label: "Employee list", q: "Show employee list" },
    ],
  },
]

const WHAT_CAN_I_ASK = "What can you help me with?"

const WELCOME: ChatMessage = {
  id: nextId(),
  role: "assistant",
  text: "Ask me about the GSD dashboard — WIC leave, sick leave, AL balance, pipeline, training, employees, WIC coverage, or today's summary. EN or DE.",
}

// ─── Result table ─────────────────────────────────────────────────────────────

function ResultTable({ rows }: { rows: TableRow[] }) {
  return (
    <div style={{ marginTop: 8, overflowX: "auto", borderRadius: 6, border: "1px solid var(--border)" }}>
      <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 11, fontFamily: "IBM Plex Mono" }}>
        <thead>
          <tr style={{ background: "var(--card2)" }}>
            {["Employee", "ID", "Start", "End", "Days", "Location", "Role"].map(h => (
              <th key={h} style={{ padding: "5px 7px", textAlign: "left", fontWeight: 600, color: "var(--text2)", whiteSpace: "nowrap", fontSize: 10 }}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} style={{ borderTop: "1px solid var(--border)" }}>
              <td style={{ padding: "4px 7px", whiteSpace: "nowrap", color: "var(--text)" }}>{row.employee}</td>
              <td style={{ padding: "4px 7px", color: "var(--text3)" }}>{row.employeeId}</td>
              <td style={{ padding: "4px 7px", color: "var(--text)" }}>{row.start}</td>
              <td style={{ padding: "4px 7px", color: "var(--text)" }}>{row.end}</td>
              <td style={{ padding: "4px 7px", textAlign: "center", color: "var(--text2)" }}>{row.workDays ?? "–"}</td>
              <td style={{ padding: "4px 7px", color: "var(--text)" }}>{row.wicLocation}</td>
              <td style={{ padding: "4px 7px", color: "var(--text3)" }}>{row.role}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ─── Chat panel (shared between floating widget and full page) ────────────────

interface ChatPanelProps {
  messages: ChatMessage[]
  isPending: boolean
  input: string
  onInput: (v: string) => void
  onSend: (q: string) => void
  bottomRef: React.RefObject<HTMLDivElement | null>
}

export function ChatPanel({ messages, isPending, input, onInput, onSend, bottomRef }: ChatPanelProps) {
  const [showAllGroups, setShowAllGroups] = useState(false)
  const visibleGroups = showAllGroups ? GROUPED_ACTIONS : GROUPED_ACTIONS.slice(0, 2)

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      {/* Messages */}
      <div style={{ flex: 1, overflowY: "auto", padding: "12px 14px", display: "flex", flexDirection: "column", gap: 10 }}>
        {messages.map(msg => (
          <div key={msg.id} style={{ alignSelf: msg.role === "user" ? "flex-end" : "flex-start", maxWidth: "94%" }}>
            <div style={{
              padding: "8px 12px",
              background: msg.role === "user"
                ? "var(--accent)"
                : msg.isError ? "hsla(350,100%,50%,.1)" : "var(--card2)",
              color: msg.role === "user" ? "#fff"
                : msg.isError ? "var(--danger)" : "var(--text)",
              borderRadius: msg.role === "user" ? "12px 12px 4px 12px" : "12px 12px 12px 4px",
              fontSize: 13, lineHeight: 1.5,
            }}>
              {msg.text}
              {msg.hint && (
                <div style={{
                  marginTop: 6, fontSize: 11, fontStyle: "italic",
                  color: msg.role === "user" ? "rgba(255,255,255,.75)" : "var(--text3)",
                  borderTop: "1px solid var(--border)", paddingTop: 5,
                }}>
                  {msg.hint}
                </div>
              )}
              {msg.dateRange && (
                <div style={{
                  marginTop: 4, fontSize: 10, fontFamily: "IBM Plex Mono",
                  color: msg.role === "user" ? "rgba(255,255,255,.7)" : "var(--text3)",
                }}>
                  {msg.dateRange}
                </div>
              )}
            </div>
            {msg.table && msg.table.length > 0 && <ResultTable rows={msg.table} />}
          </div>
        ))}

        {isPending && (
          <div style={{ alignSelf: "flex-start" }}>
            <div style={{ padding: "8px 12px", background: "var(--card2)", borderRadius: "12px 12px 12px 4px", fontSize: 13, color: "var(--text3)", display: "flex", alignItems: "center", gap: 6 }}>
              <RefreshCw size={12} className="spin" />
              Checking live data…
            </div>
          </div>
        )}
        <div ref={bottomRef} />
      </div>

      {/* Quick actions — grouped */}
      <div style={{ padding: "6px 12px 4px", borderTop: "1px solid var(--border)" }}>
        {/* "What can I ask?" + toggle */}
        <div style={{ display: "flex", gap: 6, marginBottom: 4, alignItems: "center" }}>
          <button
            onClick={() => onSend(WHAT_CAN_I_ASK)}
            disabled={isPending}
            style={{
              background: "var(--accent)", color: "#fff", border: "none",
              borderRadius: 6, padding: "3px 9px",
              fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono",
              display: "flex", alignItems: "center", gap: 4,
              opacity: isPending ? 0.5 : 1,
            }}
          >
            <HelpCircle size={10} /> What can I ask?
          </button>
          <button
            onClick={() => setShowAllGroups(v => !v)}
            style={{
              background: "none", border: "1px solid var(--border)",
              color: "var(--text3)", borderRadius: 6, padding: "3px 9px",
              fontSize: 10, cursor: "pointer", fontFamily: "IBM Plex Mono",
            }}
          >
            {showAllGroups ? "Less" : "More…"}
          </button>
        </div>

        {visibleGroups.map(group => (
          <div key={group.domain} style={{ marginBottom: 4 }}>
            <div style={{ fontSize: 9, color: "var(--text3)", fontFamily: "IBM Plex Mono", marginBottom: 3, textTransform: "uppercase", letterSpacing: "0.05em" }}>
              {group.domain}
            </div>
            <div style={{ display: "flex", gap: 5, flexWrap: "wrap" }}>
              {group.actions.map(a => (
                <button
                  key={a.label}
                  onClick={() => onSend(a.q)}
                  disabled={isPending}
                  style={{
                    background: "var(--card2)", border: "1px solid var(--border)",
                    color: "var(--text2)", borderRadius: 6, padding: "3px 8px",
                    fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono",
                    opacity: isPending ? 0.5 : 1,
                  }}
                >
                  {a.label}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* Input */}
      <div style={{ padding: "8px 12px", borderTop: "1px solid var(--border)", display: "flex", gap: 8 }}>
        <input
          value={input}
          onChange={e => onInput(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); onSend(input) } }}
          placeholder="Ask about leave, sick, pipeline, training…"
          disabled={isPending}
          style={{
            flex: 1, background: "var(--card2)", border: "1px solid var(--border)",
            borderRadius: 8, padding: "7px 11px", fontSize: 13,
            color: "var(--text)", outline: "none", fontFamily: "IBM Plex Sans",
          }}
        />
        <button
          onClick={() => onSend(input)}
          disabled={isPending || !input.trim()}
          style={{
            background: "var(--accent)", color: "#fff", border: "none",
            borderRadius: 8, width: 36, height: 36, cursor: "pointer",
            display: "flex", alignItems: "center", justifyContent: "center",
            opacity: isPending || !input.trim() ? 0.5 : 1, flexShrink: 0,
          }}
        >
          <Send size={14} />
        </button>
      </div>
    </div>
  )
}

// ─── Shared mutation hook ─────────────────────────────────────────────────────

export function useAssistantAsk(
  onSuccess: (data: AssistantResponse) => void,
  onError:   (err: Error) => void
) {
  return useMutation({
    mutationFn: async (question: string): Promise<AssistantResponse> => {
      const ctrl  = new AbortController()
      const timer = setTimeout(() => ctrl.abort(), 15000)
      try {
        return await apiFetch<AssistantResponse>("/api/assistant/ask", {
          method:  "POST",
          headers: { "Content-Type": "application/json" },
          body:    JSON.stringify({ question }),
          signal:  ctrl.signal,
        })
      } catch (err: unknown) {
        if (err instanceof DOMException && err.name === "AbortError")
          throw new Error("Request timed out after 15 s. Please try again.")
        throw err
      } finally {
        clearTimeout(timer)
      }
    },
    onSuccess,
    onError,
  })
}

// Keep alias for backward compatibility
export const useWicAsk = useAssistantAsk

// ─── Floating widget ──────────────────────────────────────────────────────────

export function WicChatWidget() {
  const [open, setOpen]         = useState(false)
  const [messages, setMessages] = useState<ChatMessage[]>([WELCOME])
  const [input, setInput]       = useState("")
  const bottomRef               = useRef<HTMLDivElement>(null)

  const push = (msg: ChatMessage) => setMessages(prev => [...prev, msg])

  const mutation = useAssistantAsk(
    data => push({
      id: nextId(), role: "assistant",
      text:      data.error ?? data.answerText,
      table:     data.table,
      dateRange: data.dateRangeChecked,
      hint:      data.error ? undefined : data.hint,
      isError:   !!data.error,
    }),
    err => push({
      id: nextId(), role: "assistant",
      text:    err.message || "Could not reach the assistant. Check the API connection.",
      isError: true,
    })
  )

  useEffect(() => { bottomRef.current?.scrollIntoView({ behavior: "smooth" }) }, [messages])

  const send = (q: string) => {
    const question = q.trim()
    if (!question || mutation.isPending) return
    push({ id: nextId(), role: "user", text: question })
    setInput("")
    mutation.mutate(question)
  }

  return (
    <>
      {/* Floating button */}
      {!open && (
        <button
          onClick={() => setOpen(true)}
          title="GSD Assistant"
          style={{
            position: "fixed", bottom: 24, right: 24, zIndex: 1000,
            background: "var(--accent)", color: "#fff",
            border: "none", borderRadius: "50%",
            width: 52, height: 52, cursor: "pointer",
            display: "flex", alignItems: "center", justifyContent: "center",
            boxShadow: "0 4px 16px rgba(0,0,0,.25)",
          }}
        >
          <MessageCircle size={22} />
        </button>
      )}

      {/* Chat window */}
      {open && (
        <div style={{
          position: "fixed", bottom: 24, right: 24, zIndex: 1000,
          width: 460, height: 620,
          background: "var(--card)", border: "1px solid var(--border)",
          borderRadius: 12, display: "flex", flexDirection: "column",
          boxShadow: "0 8px 32px rgba(0,0,0,.20)",
          overflow: "hidden",
        }}>
          {/* Header */}
          <div style={{
            padding: "12px 16px", borderBottom: "1px solid var(--border)",
            background: "var(--sidebar)",
            display: "flex", alignItems: "center", gap: 8, flexShrink: 0,
          }}>
            <Bot size={15} style={{ color: "var(--accent)" }} />
            <span style={{ flex: 1, fontSize: 13, fontWeight: 600, color: "var(--text)" }}>
              GSD Assistant
            </span>
            <button
              onClick={() => setOpen(false)}
              style={{ background: "none", border: "none", cursor: "pointer", color: "var(--text3)", display: "flex", padding: 4 }}
            >
              <X size={16} />
            </button>
          </div>

          <ChatPanel
            messages={messages}
            isPending={mutation.isPending}
            input={input}
            onInput={setInput}
            onSend={send}
            bottomRef={bottomRef}
          />
        </div>
      )}
    </>
  )
}
