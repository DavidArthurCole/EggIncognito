// Browser-side memory for the Inspector Blazor tab. The one place a little JS is allowed (like
// resize.js): the signing salt, remembered EIDs, the custom proxy URL, and the BasicRequestInfo
// defaults all live in this browser's localStorage, never server-side. The salt is client-owned: it is
// read here and handed to the build call only; it is never persisted or encrypted on the server.

const SALT_KEY = "inspector.salt";
const RINFO_KEY = "inspector.rinfoDefaults";
const CUSTOM_TARGET_KEY = "inspector.customTarget";
const EIDS_KEY = "inspector.recentEids";
const LIVE_CONSENT_KEY = "egi:liveApiConsent";
const HISTORY_KEY = "inspector.history";
const HISTORY_ENABLED_KEY = "inspector.historyEnabled";
const HISTORY_SEEN_KEY = "inspector.historySeenNotice";
const HISTORY_MAX = 50;

const EID_RE = /^EI\d{10,}$/;
const EID_MAX = 12;

// Seed defaults for BasicRequestInfo: the standard client constants a real request carries.
const RINFO_SEED = {
  eiUserId: "",
  clientVersion: 72,
  version: "1.35.7",
  build: "111343",
  platform: "DROID",
  country: "US",
  language: "en",
  debug: false,
};

function getRaw(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function setRaw(key, val) {
  try { localStorage.setItem(key, val); } catch { /* ignore */ }
}

export function getSalt() { return getRaw(SALT_KEY) || ""; }
export function setSalt(value) { setRaw(SALT_KEY, String(value ?? "")); }

export function getCustomTarget() { return getRaw(CUSTOM_TARGET_KEY) || ""; }
export function setCustomTarget(value) { setRaw(CUSTOM_TARGET_KEY, String(value ?? "").trim()); }

export function getLiveConsent() { return getRaw(LIVE_CONSENT_KEY) === "1"; }
export function setLiveConsent() { setRaw(LIVE_CONSENT_KEY, "1"); }

// The rinfo defaults: the seed merged with any saved overrides. Always returns all seven keys.
export function getRinfoDefaults() {
  try {
    const saved = JSON.parse(getRaw(RINFO_KEY) || "{}");
    return { ...RINFO_SEED, ...(saved && typeof saved === "object" ? saved : {}) };
  } catch {
    return { ...RINFO_SEED };
  }
}
export function setRinfoDefaults(obj) { setRaw(RINFO_KEY, JSON.stringify(obj || {})); }

// Remembered EIDs, stored as [{ eid, order }] with a monotonic order counter (recency only, no clock).
function loadEids() {
  try {
    const raw = JSON.parse(getRaw(EIDS_KEY) || "[]");
    return Array.isArray(raw) ? raw.filter((e) => e && EID_RE.test(e.eid)) : [];
  } catch { return []; }
}

// Remember one EID (no-op for non-EID strings). Returns the refreshed most-recent-first list.
export function rememberEid(value) {
  const eid = String(value || "").trim();
  if (EID_RE.test(eid)) {
    const list = loadEids().filter((e) => e.eid !== eid);
    const nextOrder = list.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
    list.push({ eid, order: nextOrder });
    list.sort((a, b) => b.order - a.order);
    setRaw(EIDS_KEY, JSON.stringify(list.slice(0, EID_MAX)));
  }
  return recentEids();
}

// Remember several at once; returns the refreshed list.
export function rememberEids(values) {
  for (const v of values || []) {
    const eid = String(v || "").trim();
    if (!EID_RE.test(eid)) continue;
    const list = loadEids().filter((e) => e.eid !== eid);
    const nextOrder = list.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
    list.push({ eid, order: nextOrder });
    list.sort((a, b) => b.order - a.order);
    setRaw(EIDS_KEY, JSON.stringify(list.slice(0, EID_MAX)));
  }
  return recentEids();
}

export function recentEids() {
  return loadEids().sort((a, b) => b.order - a.order).map((e) => e.eid);
}
export function forgetEids() { setRaw(EIDS_KEY, "[]"); return []; }

// --- Inspector request history (client-side "quick swap"). Default ON. Each entry is the builder state
// needed to restore a request: { id, path, summary, env, fieldsJson, pathParam, target, order }. ---

export function getHistoryEnabled() {
  const raw = getRaw(HISTORY_ENABLED_KEY);
  return raw === null ? true : raw === "1"; // default ON
}
export function setHistoryEnabled(on) { setRaw(HISTORY_ENABLED_KEY, on ? "1" : "0"); }

// True the first time history is ever saved (so the page can show the one-time "you can turn this off"
// notice exactly once). Marks itself seen on read.
export function historyNoticeUnseen() {
  if (getRaw(HISTORY_SEEN_KEY) === "1") return false;
  setRaw(HISTORY_SEEN_KEY, "1");
  return true;
}

function loadHistory() {
  try {
    const raw = JSON.parse(getRaw(HISTORY_KEY) || "[]");
    return Array.isArray(raw) ? raw : [];
  } catch { return []; }
}

export function getHistory() {
  return loadHistory().sort((a, b) => (b.order || 0) - (a.order || 0));
}

// Save an entry (no-op when history is off). De-dupes by (path + fieldsJson + pathParam) so re-building the
// same request bumps it to the top instead of piling duplicates. Returns the refreshed list.
export function saveHistory(entry) {
  if (!getHistoryEnabled() || !entry || !entry.path) return getHistory();
  const list = loadHistory().filter(
    (e) => !(e.path === entry.path && e.fieldsJson === entry.fieldsJson && (e.pathParam || "") === (entry.pathParam || "")));
  const nextOrder = list.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
  list.push({ ...entry, order: nextOrder });
  list.sort((a, b) => (b.order || 0) - (a.order || 0));
  setRaw(HISTORY_KEY, JSON.stringify(list.slice(0, HISTORY_MAX)));
  return getHistory();
}

export function deleteHistory(id) {
  setRaw(HISTORY_KEY, JSON.stringify(loadHistory().filter((e) => e.id !== id)));
  return getHistory();
}
export function clearHistory() { setRaw(HISTORY_KEY, "[]"); return []; }

// Browser-direct POST to the user's own proxy in Custom send mode. This server is bypassed: the request
// goes straight from the browser to the proxy, which relays the same form body to auxbrain. Returns the
// trimmed response text or throws on a network/CORS failure.
export async function postForm(url, formBody) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: formBody,
  });
  return (await resp.text()).trim();
}
