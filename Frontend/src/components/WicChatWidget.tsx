import { useState, useRef, useEffect } from "react"
import { useMutation } from "@tanstack/react-query"
import { useTranslation } from "react-i18next"
import { MessageCircle, X, Send, Bot, RefreshCw, ChevronDown } from "lucide-react"
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
  follow_up?: string | null
}

export interface ChatMessage {
  id: number
  role: "user" | "assistant"
  text: string
  table?: TableRow[]
  dateRange?: string
  hint?: string
  isError?: boolean
  follow_up?: string | null
}

// ─── Constants ────────────────────────────────────────────────────────────────

let _id = 0
const nextId = () => ++_id

const GROUPED_ACTIONS = [
  {
    key: "coverage",
    dotClass: "bg-wic-solid",
    chips: [
      { chipKey: "assistant.chips.uncoveredToday", q: "WIC forecast today" },
      { chipKey: "assistant.chips.atRisk7Days",    q: "Which locations are at risk in the next 7 days?" },
      { chipKey: "assistant.chips.coverHamburg",   q: "Who covers Hamburg?" },
    ],
  },
  {
    key: "shifts",
    dotClass: "bg-good-solid",
    chips: [
      { chipKey: "assistant.chips.workingToday", q: "Who is working today?" },
      { chipKey: "assistant.chips.wicDutyWeek",  q: "Who is on WIC duty this week?" },
    ],
  },
  {
    key: "absence",
    dotClass: "bg-warn-solid",
    chips: [
      { chipKey: "assistant.chips.absentToday",   q: "Who is absent today and why?" },
      { chipKey: "assistant.chips.openSickLeave", q: "Which sick leaves are still open?" },
      { chipKey: "assistant.chips.longSick",      q: "Who has been sick longer than 21 days?" },
      { chipKey: "assistant.chips.alNextWeek",    q: "Who is on annual leave next week?" },
    ],
  },
  {
    key: "balance",
    dotClass: "bg-info-solid",
    chips: [
      { chipKey: "assistant.chips.lowLeave", q: "Who has the lowest AL balance?" },
    ],
  },
  {
    key: "planning",
    dotClass: "bg-learn-solid",
    chips: [
      { chipKey: "assistant.chips.pipelineEvents",   q: "What pipeline events are coming up?" },
      { chipKey: "assistant.chips.eventsNeedAgents", q: "Which pipeline events still need agents assigned?" },
      { chipKey: "assistant.chips.trainingSlot",     q: "When is the next good slot for a training session?" },
    ],
  },
]

const WELCOME: ChatMessage = {
  id: nextId(),
  role: "assistant",
  text: "Ask me about the GSD dashboard — WIC leave, sick leave, AL balance, pipeline, training, employees, WIC coverage, or today's summary. EN or DE.",
}

// ─── Result table ─────────────────────────────────────────────────────────────

function ResultTable({ rows }: { rows: TableRow[] }) {
  return (
    <div className="border border-line-subtle mt-2" style={{ overflowX: "auto", borderRadius: 6 }}>
      <table className="font-mono w-full" style={{ borderCollapse: "collapse", fontSize: 11 }}>
        <thead>
          <tr className="bg-sunken">
            {["Employee", "ID", "Start", "End", "Days", "Location", "Role"].map(h => (
              <th key={h} className="text-ink-muted" style={{ padding: "5px 7px", textAlign: "left", fontWeight: 600, whiteSpace: "nowrap", fontSize: 10 }}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-t border-line-subtle">
              <td className="text-ink"      style={{ padding: "4px 7px", whiteSpace: "nowrap" }}>{row.employee}</td>
              <td className="text-ink-soft" style={{ padding: "4px 7px" }}>{row.employeeId}</td>
              <td className="text-ink"      style={{ padding: "4px 7px" }}>{row.start}</td>
              <td className="text-ink"      style={{ padding: "4px 7px" }}>{row.end}</td>
              <td className="text-ink-muted" style={{ padding: "4px 7px", textAlign: "center" }}>{row.workDays ?? "–"}</td>
              <td className="text-ink"      style={{ padding: "4px 7px" }}>{row.wicLocation}</td>
              <td className="text-ink-soft" style={{ padding: "4px 7px" }}>{row.role}</td>
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
  const { t } = useTranslation()
  const [search, setSearch] = useState("")
  const [suggestionsVisible, setSuggestionsVisible] = useState(true)
  const [lastQuestion, setLastQuestion] = useState("")
  const autoCollapsed = useRef(false)

  // Auto-collapse suggestions after the first user message arrives
  useEffect(() => {
    if (!autoCollapsed.current && messages.length > 1) {
      setSuggestionsVisible(false)
      autoCollapsed.current = true
    }
  }, [messages.length])

  const handleSend = (q: string) => {
    const trimmed = q.trim()
    if (trimmed) setLastQuestion(trimmed)
    onSend(q)
  }

  // Filter groups by search term (matches chip label or question text)
  const filteredGroups = GROUPED_ACTIONS.map(group => ({
    ...group,
    chips: search.trim()
      ? group.chips.filter(c =>
          t(c.chipKey).toLowerCase().includes(search.toLowerCase()) ||
          c.q.toLowerCase().includes(search.toLowerCase())
        )
      : group.chips,
  })).filter(g => g.chips.length > 0)

  return (
    <div className="flex flex-col h-full">

      {/* ── Messages ── */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        <div className="max-w-[860px] mx-auto flex flex-col gap-3">

          {messages.map(msg => (
            <div key={msg.id}>
              {msg.isError ? (
                /* Error bubble */
                <div className="text-crit-fg text-sm flex items-center gap-2">
                  <span>{msg.text}</span>
                  {lastQuestion && (
                    <button
                      onClick={() => handleSend(lastQuestion)}
                      disabled={isPending}
                      className="flex-shrink-0 opacity-70 hover:opacity-100 transition-opacity"
                      title="Retry"
                    >
                      <RefreshCw size={12} />
                    </button>
                  )}
                </div>
              ) : msg.role === "user" ? (
                /* User bubble */
                <div className="bg-action text-action-fg rounded-lg rounded-br-sm px-3.5 py-2.5 max-w-[75%] ml-auto text-sm">
                  {msg.text}
                </div>
              ) : (
                /* Assistant bubble */
                <div className="max-w-[85%]">
                  <div className="bg-raised border border-line-subtle rounded-lg rounded-bl-sm px-3.5 py-2.5 text-sm">
                    {msg.text}
                    {msg.hint && (
                      <div className="mt-1.5 text-xs italic text-ink-soft border-t border-line-subtle pt-1">
                        {msg.hint}
                      </div>
                    )}
                    {msg.dateRange && (
                      <div className="font-mono mt-1 text-[10px] text-ink-soft">
                        {msg.dateRange}
                      </div>
                    )}
                  </div>
                  {msg.table && msg.table.length > 0 && <ResultTable rows={msg.table} />}
                  {msg.follow_up && (
                    <div className="mt-2">
                      <button
                        onClick={() => handleSend(msg.follow_up!)}
                        disabled={isPending}
                        className="px-3 h-8 rounded-full border border-line-default bg-raised text-sm text-ink-muted hover:border-line-strong hover:text-ink hover:bg-hovered transition-colors duration-fast text-left disabled:opacity-50"
                      >
                        {msg.follow_up}
                      </button>
                    </div>
                  )}
                </div>
              )}
            </div>
          ))}

          {/* Loading — three bouncing dots */}
          {isPending && (
            <div className="max-w-[85%]">
              <div className="bg-raised border border-line-subtle rounded-lg rounded-bl-sm px-3.5 py-2.5">
                <div className="flex gap-1 items-center py-1">
                  <div className="w-1.5 h-1.5 rounded-full bg-ink-soft animate-bounce" style={{ animationDelay: "0ms" }} />
                  <div className="w-1.5 h-1.5 rounded-full bg-ink-soft animate-bounce" style={{ animationDelay: "150ms" }} />
                  <div className="w-1.5 h-1.5 rounded-full bg-ink-soft animate-bounce" style={{ animationDelay: "300ms" }} />
                </div>
              </div>
            </div>
          )}

          <div ref={bottomRef} />
        </div>
      </div>

      {/* ── Suggestions panel ── */}
      <div className="border-t border-line-subtle px-3 pt-3 pb-2">
        {!suggestionsVisible ? (
          <button
            onClick={() => setSuggestionsVisible(true)}
            className="text-2xs font-semibold uppercase tracking-[0.06em] text-ink-soft hover:text-ink transition-colors duration-fast flex items-center gap-1.5"
          >
            <ChevronDown size={10} />
            {t("assistant.showSuggestions")}
          </button>
        ) : (
          <>
            {/* Search */}
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder={t("assistant.search")}
              className="w-full bg-sunken border border-line-subtle text-ink text-sm rounded-lg px-3 py-1.5 mb-3 outline-none"
            />

            {/* Groups */}
            {filteredGroups.map(group => (
              <div key={group.key}>
                <div className="text-2xs font-semibold uppercase tracking-[0.06em] text-ink-soft mb-2 flex items-center gap-1.5">
                  <span className={`inline-block w-1.5 h-1.5 rounded-full flex-shrink-0 ${group.dotClass}`} />
                  {t(`assistant.suggestions.${group.key}`)}
                </div>
                <div className="flex flex-wrap gap-2 mb-4">
                  {group.chips.map(chip => (
                    <button
                      key={chip.chipKey}
                      onClick={() => handleSend(chip.q)}
                      disabled={isPending}
                      className="px-3 h-8 rounded-full border border-line-default bg-raised text-sm text-ink-muted hover:border-line-strong hover:text-ink hover:bg-hovered transition-colors duration-fast text-left disabled:opacity-50"
                    >
                      {t(chip.chipKey)}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </>
        )}
      </div>

      {/* ── Input ── */}
      <div className="px-3 pb-3 pt-2 border-t border-line-subtle flex gap-2">
        <input
          value={input}
          onChange={e => onInput(e.target.value)}
          onKeyDown={e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); handleSend(input) } }}
          placeholder="Ask about leave, sick, pipeline, training…"
          disabled={isPending}
          className="flex-1 bg-sunken border border-line-subtle text-ink text-sm rounded-lg px-3 py-1.5 outline-none"
        />
        <button
          onClick={() => handleSend(input)}
          disabled={isPending || !input.trim()}
          className="bg-action text-action-fg rounded-lg w-9 h-9 flex items-center justify-center flex-shrink-0 disabled:opacity-50"
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
      follow_up: data.follow_up,
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
          className="bg-info-solid"
          style={{
            position: "fixed", bottom: 24, right: 24, zIndex: 1000,
            color: "#fff",
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
        <div className="bg-raised border border-line-subtle" style={{
          position: "fixed", bottom: 24, right: 24, zIndex: 1000,
          width: 460, height: 620,
          borderRadius: 12, display: "flex", flexDirection: "column",
          boxShadow: "0 8px 32px rgba(0,0,0,.20)",
          overflow: "hidden",
        }}>
          {/* Header */}
          <div style={{
            padding: "12px 16px", borderBottom: "1px solid rgb(var(--border-subtle))",
            background: "var(--sidebar)",
            display: "flex", alignItems: "center", gap: 8, flexShrink: 0,
          }}>
            <Bot size={15} className="text-info-fg" />
            <span className="text-ink" style={{ flex: 1, fontSize: 13, fontWeight: 600 }}>
              GSD Assistant
            </span>
            <button
              onClick={() => setOpen(false)}
              className="text-ink-soft"
              style={{ background: "none", border: "none", cursor: "pointer", display: "flex", padding: 4 }}
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
