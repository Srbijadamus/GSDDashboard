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
        bg: "#0b0f1a",
        sidebar: "#0e1320",
        card: "#131928",
        card2: "#1a2235",
        border: "#1e2d45",
        accent: "#3b7eff",
        accent2: "#00d2a0",
        warn: "#ff7c3b",
        danger: "#ff3b5c",
        gtext: "#e2e8f0",
        text2: "#7a8fa8",
        text3: "#4a5f7a",
        green: "#22d07a",
      },
    },
  },
  plugins: [],
}
