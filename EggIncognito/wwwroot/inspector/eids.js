// Remembered EIDs for the Inspector. Stored in localStorage (per-browser convenience, like the
// capture settings) so you do not retype your EID every session. EIDs can be player-identifying, so
// this stays browser-local and is never sent to the server.

const KEY = "inspector.recentEids";
const MAX = 12;
const EID_RE = /^EI\d{10,}$/;

// Stored as [{ eid, order }] where order is a monotonic counter (not a wall-clock - we only need
// recency ordering, and avoids any reliance on the clock). Highest order = most recent.
function load() {
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) || "[]");
    return Array.isArray(raw) ? raw.filter((e) => e && EID_RE.test(e.eid)) : [];
  } catch {
    return [];
  }
}

function save(list) {
  try { localStorage.setItem(KEY, JSON.stringify(list)); } catch { /* ignore */ }
}

// Remember an EID (no-op for non-EID strings). Moves an existing one to the front. Returns true if
// the list changed.
export function rememberEid(value) {
  const eid = String(value || "").trim();
  if (!EID_RE.test(eid)) return false;
  const list = load().filter((e) => e.eid !== eid);
  const nextOrder = list.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
  list.push({ eid, order: nextOrder });
  list.sort((a, b) => b.order - a.order);
  save(list.slice(0, MAX));
  return true;
}

// Recent EIDs, most-recent-first.
export function recentEids() {
  return load().sort((a, b) => b.order - a.order).map((e) => e.eid);
}

export function mostRecentEid() {
  return recentEids()[0] ?? null;
}

export function forgetEids() {
  save([]);
}
