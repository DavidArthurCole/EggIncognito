/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Components/**/*.razor",
    "./wwwroot/**/*.html",
    "./wwwroot/**/*.js",
  ],
 
 
  safelist: ["console-get", "console-post", "console-put", "console-patch", "console-delete"],
 
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
        info: "#5aa9e6",
        ok: "#5ec27e",
        err: "#e0685f",
        border: "#3a3a44",
      },
      fontFamily: {
        mono: ['"Cascadia Code"', '"Fira Code"', "Consolas", "monospace"],
      },
      spacing: {
        nav: "48px",
      },
      borderRadius: {
        pill: "999px",
      },
    },
  },
  plugins: [],
};
