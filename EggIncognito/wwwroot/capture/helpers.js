// Small shared formatting/layout helpers for the capture dashboard.

import { flowList } from "./dom.js";

export function statusClass(status) {
  if (status >= 200 && status < 300) return "status-2xx";
  if (status >= 300 && status < 400) return "status-3xx";
  if (status >= 500) return "status-5xx";
  return "status-4xx";
}

// True when the list is scrolled (near) the bottom, so we can respect the
// user's position when they have scrolled up to read older flows.
export function isAtBottom() {
  const slack = 40;
  return flowList.scrollHeight - flowList.scrollTop - flowList.clientHeight <= slack;
}

// Format a byte count as B / KB / MB.
export function formatBytes(bytes) {
  const b = Number(bytes) || 0;
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(1)} KB`;
  return `${(b / (1024 * 1024)).toFixed(1)} MB`;
}

export function truncate(text, max) {
  const s = String(text);
  return s.length > max ? s.slice(0, max - 1) + "…" : s;
}

// endpoint-write outcome helpers (shared by the flow list + the detail known card)

// Map an outcome string to { label, kind }. Returns null for empty/unknown.
export function outcomeMeta(outcome) {
  switch (outcome) {
    case "wrote": return { label: "wrote", kind: "good" };
    case "upd": return { label: "upd", kind: "good" };
    case "diff": return { label: "diff", kind: "warn" };
    case "loss": return { label: "loss", kind: "bad" };
    case "same": return { label: "same", kind: "same" };
    default: return null;
  }
}

// True when this is a "diff" outcome with at least one changed line.
export function hasDiffCounts(outcome, added, removed) {
  return outcome === "diff" && ((Number(added) || 0) > 0 || (Number(removed) || 0) > 0);
}

// Append git-style "+N" (green) / "-N" (red) spans to `el` for diff outcomes. Only non-zero sides
// are shown. Returns true if anything was appended.
export function appendDiffCounts(el, added, removed) {
  const a = Number(added) || 0;
  const r = Number(removed) || 0;
  let appended = false;
  if (a > 0) {
    const plus = document.createElement("span");
    plus.className = "diff-add";
    plus.textContent = "+" + a;
    el.appendChild(plus);
    appended = true;
  }
  if (r > 0) {
    const minus = document.createElement("span");
    minus.className = "diff-del";
    minus.textContent = "-" + r;
    el.appendChild(minus);
    appended = true;
  }
  return appended;
}
