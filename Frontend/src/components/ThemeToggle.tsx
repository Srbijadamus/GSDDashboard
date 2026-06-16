import { useTheme } from "next-themes"
import { Sun, Moon } from "lucide-react"

export function ThemeToggle() {
  const { resolvedTheme, setTheme } = useTheme()
  const isDark = resolvedTheme === "dark"

  return (
    <button
      onClick={() => setTheme(isDark ? "light" : "dark")}
      title={isDark ? "Switch to light mode" : "Switch to dark mode"}
      style={{
        background: "var(--card2)",
        border: "1px solid var(--border)",
        color: "var(--text2)",
        padding: "4px 8px",
        borderRadius: 6,
        fontSize: 11,
        cursor: "pointer",
        display: "flex",
        alignItems: "center",
        gap: 4,
        transition: "color 0.15s, background 0.15s",
      }}
    >
      {isDark ? <Sun size={13} /> : <Moon size={13} />}
    </button>
  )
}
