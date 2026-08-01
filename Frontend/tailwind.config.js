/** @type {import('tailwindcss').Config} */
const rgb = (v) => ({ opacityValue }) =>
  opacityValue === undefined ? `rgb(var(${v}))` : `rgb(var(${v}) / ${opacityValue})`;

export default {
  content: ["./index.html", "./src/**/*.{js,ts,jsx,tsx}"],
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        page:      rgb('--surface-page'),
        raised:    rgb('--surface-raised'),
        sunken:    rgb('--surface-sunken'),
        overlay:   rgb('--surface-overlay'),
        hovered:   rgb('--surface-hover'),
        actived:   rgb('--surface-active'),
        scrim:     rgb('--surface-scrim'),
        line:      { subtle: rgb('--border-subtle'), DEFAULT: rgb('--border-default'), strong: rgb('--border-strong') },
        ink:       { DEFAULT: rgb('--text-primary'), muted: rgb('--text-secondary'), soft: rgb('--text-tertiary'), disabled: rgb('--text-disabled'), inverse: rgb('--text-inverse') },
        action:    { DEFAULT: rgb('--action-solid'), hover: rgb('--action-solid-hover'), fg: rgb('--action-fg') },
        focusring: rgb('--focus-ring'),
        eon:       { DEFAULT: rgb('--brand-eon'), fg: rgb('--brand-eon-ink') },
        good:      { fg: rgb('--st-good-fg'),    bg: rgb('--st-good-bg'),    bd: rgb('--st-good-bd'),    solid: rgb('--st-good-solid') },
        warn:      { fg: rgb('--st-warn-fg'),    bg: rgb('--st-warn-bg'),    bd: rgb('--st-warn-bd'),    solid: rgb('--st-warn-solid') },
        crit:      { fg: rgb('--st-crit-fg'),    bg: rgb('--st-crit-bg'),    bd: rgb('--st-crit-bd'),    solid: rgb('--st-crit-solid') },
        info:      { fg: rgb('--st-info-fg'),    bg: rgb('--st-info-bg'),    bd: rgb('--st-info-bd'),    solid: rgb('--st-info-solid') },
        wic:       { fg: rgb('--st-wic-fg'),     bg: rgb('--st-wic-bg'),     bd: rgb('--st-wic-bd'),     solid: rgb('--st-wic-solid') },
        learn:     { fg: rgb('--st-learn-fg'),   bg: rgb('--st-learn-bg'),   bd: rgb('--st-learn-bd'),   solid: rgb('--st-learn-solid') },
        holiday:   { fg: rgb('--st-holiday-fg'), bg: rgb('--st-holiday-bg'), bd: rgb('--st-holiday-bd'), solid: rgb('--st-holiday-solid') },
        neutralst: { fg: rgb('--st-neutral-fg'), bg: rgb('--st-neutral-bg'), bd: rgb('--st-neutral-bd'), solid: rgb('--st-neutral-solid') },
        mutedst:   { fg: rgb('--st-muted-fg'),   bg: rgb('--st-muted-bg'),   bd: rgb('--st-muted-bd'),   solid: rgb('--st-muted-solid') },
      },
      fontFamily: {
        sans: ['"Segoe UI Variable Text"', '"Segoe UI"', 'system-ui', 'sans-serif'],
        mono: ['"Cascadia Mono"', 'Consolas', 'ui-monospace', 'monospace'],
      },
      fontSize: {
        '2xs': ['11px', { lineHeight: '16px', letterSpacing: '0.04em' }],
        xs:    ['12px', { lineHeight: '18px', letterSpacing: '0' }],
        sm:    ['13px', { lineHeight: '20px', letterSpacing: '0' }],
        base:  ['14px', { lineHeight: '22px', letterSpacing: '0' }],
        md:    ['16px', { lineHeight: '24px', letterSpacing: '0' }],
        lg:    ['18px', { lineHeight: '26px', letterSpacing: '-0.004em' }],
        xl:    ['20px', { lineHeight: '28px', letterSpacing: '-0.006em' }],
        '2xl': ['24px', { lineHeight: '32px', letterSpacing: '-0.008em' }],
        '3xl': ['30px', { lineHeight: '38px', letterSpacing: '-0.011em' }],
        '4xl': ['36px', { lineHeight: '44px', letterSpacing: '-0.013em' }],
      },
      borderRadius: { xs: '4px', sm: '6px', md: '8px', lg: '12px', xl: '16px', '2xl': '20px' },
      boxShadow: {
        xs: 'var(--shadow-xs)', sm: 'var(--shadow-sm)', md: 'var(--shadow-md)',
        lg: 'var(--shadow-lg)', xl: 'var(--shadow-xl)',
      },
      spacing: { 4.5: '18px', 13: '52px', 15: '60px', 18: '72px', 88: '352px' },
      zIndex: { nav: '30', topbar: '40', dropdown: '50', drawer: '60', modal: '70', toast: '80', palette: '90' },
      transitionTimingFunction: { out: 'var(--ease-out)' },
    },
  },
  plugins: [],
};
