/** @type {import("tailwindcss").Config} */
export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      fontFamily: {
        sans: ["IBM Plex Sans", "sans-serif"],
        mono: ["IBM Plex Mono", "monospace"],
      },
      colors: {
        bg:         "var(--bg)",
        sidebar:    "var(--sidebar)",
        card:       "var(--card)",
        card2:      "var(--card2)",
        border:     "var(--border)",
        accent:     "var(--accent)",
        accent2:    "var(--accent2)",
        warn:       "var(--warn)",
        danger:     "var(--danger)",
        green:      "var(--green)",
        purple:     "var(--purple)",
        yellow:     "var(--yellow)",
        "blue-light": "var(--blue-light)",
        text:       "var(--text)",
        text2:      "var(--text2)",
        text3:      "var(--text3)",
      },
    },
  },
  plugins: [],
}
