import { useTranslation } from "react-i18next"
import { Download } from "lucide-react"

interface Props {
  onToday: () => void
  on7Days: () => void
  on30Days: () => void
}

export function DownloadButtons({ onToday, on7Days, on30Days }: Props) {
  const { t } = useTranslation()

  const btnStyle = {
    background: "var(--card)", border: "1px solid var(--border)",
    color: "var(--text2)", padding: "5px 12px", borderRadius: 6,
    fontSize: 11, cursor: "pointer", fontFamily: "IBM Plex Mono",
    display: "flex", alignItems: "center", gap: 4, transition: "all .15s"
  }

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
      <Download size={13} color="var(--text3)" />
      <button style={btnStyle} onClick={onToday}
        onMouseEnter={e => (e.currentTarget.style.borderColor = "var(--accent)")}
        onMouseLeave={e => (e.currentTarget.style.borderColor = "var(--border)")}>
        {t("download.today")}
      </button>
      <button style={btnStyle} onClick={on7Days}
        onMouseEnter={e => (e.currentTarget.style.borderColor = "var(--accent)")}
        onMouseLeave={e => (e.currentTarget.style.borderColor = "var(--border)")}>
        {t("download.last7")}
      </button>
      <button style={btnStyle} onClick={on30Days}
        onMouseEnter={e => (e.currentTarget.style.borderColor = "var(--accent)")}
        onMouseLeave={e => (e.currentTarget.style.borderColor = "var(--border)")}>
        {t("download.last30")}
      </button>
    </div>
  )
}
