import { useState, useRef, useEffect } from "react"
import { Bot } from "lucide-react"
import { ChatPanel, useAssistantAsk, type ChatMessage } from "../components/WicChatWidget"

let _pid = 1000
const nextId = () => ++_pid

const WELCOME: ChatMessage = {
  id: nextId(),
  role: "assistant",
  text: "Hello! I can answer questions about WorkForce Pulse. Examples:\n• \"Who is on WIC leave next week?\"\n• \"Who is sick today?\"\n• \"Show AL balance\"\n• \"Show pipeline events\"\n• \"What training is scheduled?\"\n• \"Show dashboard summary today\"",
}

export default function WicAssistant() {
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
    <div style={{ maxWidth: 860, margin: "0 auto", height: "calc(100vh - 100px)", display: "flex", flexDirection: "column" }}>
      {/* Page header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 16 }}>
        <div
          className="bg-info-solid"
          style={{ width: 34, height: 34, borderRadius: 8, display: "flex", alignItems: "center", justifyContent: "center" }}
        >
          <Bot size={18} color="#fff" />
        </div>
        <div>
          <h1 className="text-ink" style={{ margin: 0, fontSize: 17, fontWeight: 700 }}>Pulse Assistant</h1>
          <p className="text-ink-soft" style={{ margin: 0, fontSize: 12 }}>
            Ask questions about leave, sick, AL balance, pipeline, training, employees, WIC coverage — live data, EN or DE.
          </p>
        </div>
      </div>

      {/* Chat card */}
      <div
        className="bg-raised border border-line-subtle"
        style={{ flex: 1, borderRadius: 12, display: "flex", flexDirection: "column", overflow: "hidden", minHeight: 0 }}
      >
        <ChatPanel
          messages={messages}
          isPending={mutation.isPending}
          input={input}
          onInput={setInput}
          onSend={send}
          bottomRef={bottomRef}
        />
      </div>
    </div>
  )
}
