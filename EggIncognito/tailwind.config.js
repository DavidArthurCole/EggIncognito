/** @type {import('tailwindcss').Config} */
// Design tokens for EggIncognito, mirroring the current CSS-var palette so a later markup migration to
// utility classes produces the SAME colors. `content` is scanned for class names; unused utilities are
// dropped from the output (the compile-time "did you use it" win).
module.exports = {
  content: [
    "./Components/**/*.razor",
    "./wwwroot/**/*.html",
    "./wwwroot/**/*.js",
  ],
  // Preflight (Tailwind's CSS reset) is OFF for Phase 1: the compiled sheet is ADDITIVE alongside the
  // legacy per-tab CSS, and a reset would restyle existing elements (a visual regression). Re-enable in
  // a later phase once the legacy CSS is being removed.
  corePlugins: { preflight: false },
  theme: {
    extend: {
      colors: {
        bg: "#1b1b1f",
        panel: "#25252b",
        panel2: "#2e2e36",
        fg: "#e7e7ea",
        muted: "#9a9aa5",
        accent: "#ef7559",
        accent2: "#5aa9e6",
        info: "#5aa9e6", // alias of accent2 (Capture's --info); kept so component classes read clearly
        ok: "#5ec27e",
        err: "#e0685f",
        border: "#3a3a44",
      },
      fontFamily: {
        mono: ['"Cascadia Code"', '"Fira Code"', "Consolas", "monospace"],
      },
      spacing: {
        nav: "48px", // --nav-h
      },
      borderRadius: {
        pill: "999px",
      },
    },
  },
  plugins: [],
};
