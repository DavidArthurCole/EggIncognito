let wired = false;

function onError(e) {
  const el = e.target;
  if (!(el instanceof HTMLImageElement) || !el.hasAttribute("data-fallback")) return;
  el.style.display = "none";
  const next = el.nextElementSibling;
  if (next && next.hasAttribute("data-fallback-for")) next.style.display = "inline";
}

export function wire() {
  if (wired) return;
  document.addEventListener("error", onError, true);
  wired = true;
}

export function unwire() {
  if (!wired) return;
  document.removeEventListener("error", onError, true);
  wired = false;
}
