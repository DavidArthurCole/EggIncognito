export function setCss(text, nonce) {
  let el = document.getElementById("egi-theme-preview");
  if (!el) {
    el = document.createElement("style");
    el.id = "egi-theme-preview";
    if (nonce) el.nonce = nonce;
    document.head.appendChild(el);
  }
  el.textContent = text || "";
}

export function clearCss() {
  document.getElementById("egi-theme-preview")?.remove();
}

export function applyTokens(tokens) {
  const nameRe = /^[a-z0-9]+$/;
  const valueRe = /^(#[0-9a-f]{6}|oklch\([0-9. %]+\))$/;
  for (const [name, value] of Object.entries(tokens || {})) {
    if (nameRe.test(name) && typeof value === "string" && valueRe.test(value)) {
      document.documentElement.style.setProperty("--color-" + name, value);
    }
  }
}

export function clearTokens(names) {
  for (const name of names || []) {
    document.documentElement.style.removeProperty("--color-" + name);
  }
}
