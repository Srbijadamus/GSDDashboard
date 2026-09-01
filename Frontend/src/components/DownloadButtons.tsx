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
    padding: "5px 12px", borderRadius: 6,
    fontSize: 11, cursor: "pointer",
    display: "flex", alignItems: "center", gap: 4, transition: "all .15s"
  }

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
      <Download size={13} className="text-ink-soft" />
      <button className="bg-raised border border-line-subtle text-ink-muted font-mono hover:border-info-bd" style={btnStyle} onClick={onToday}>
        {t("download.today")}
      </button>
      <button className="bg-raised border border-line-subtle text-ink-muted font-mono hover:border-info-bd" style={btnStyle} onClick={on7Days}>
        {t("download.last7")}
      </button>
      <button className="bg-raised border border-line-subtle text-ink-muted font-mono hover:border-info-bd" style={btnStyle} onClick={on30Days}>
        {t("download.last30")}
      </button>
    </div>
  )
}
