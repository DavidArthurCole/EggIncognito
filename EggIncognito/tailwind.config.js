/** @type {import('tailwindcss').Config} */
// Design tokens mirror the existing CSS-var palette. `content` is scanned for class names;
// unused utilities are dropped from the output.
module.exports = {
  content: [
    "./Components/**/*.razor",
    "./wwwroot/**/*.html",
    "./wwwroot/**/*.js",
  ],
  // The API console builds method-chip classes dynamically (console-@(verb)), so the scanner never sees the
  // literal names; safelist them so the @layer-components rules survive the purge.
  safelist: ["console-get", "console-post", "console-put", "console-patch", "console-delete"],
  // Preflight (Tailwind's CSS reset) is off: the compiled sheet is additive alongside legacy per-tab CSS.
  corePlugins: { preflight: false },
  theme: {
    extend: {
      colors: {
        bg: "#1b1b1f",
        panel0: "#202027",
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
