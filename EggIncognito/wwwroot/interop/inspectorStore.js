

import { get as getRaw, set as setRaw } from "./uiPrefs.js";

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

function lruUpsert(list, matches, entry, max) {
  const next = list.filter((e) => !matches(e));
  const order = next.reduce((m, e) => Math.max(m, e.order || 0), 0) + 1;
  next.push({ ...entry, order });
  next.sort((a, b) => (b.order || 0) - (a.order || 0));
  return next.slice(0, max);
}

export function getSalt() { return getRaw(SALT_KEY) || ""; }
export function setSalt(value) { setRaw(SALT_KEY, String(value ?? "")); }

export function getCustomTarget() { return getRaw(CUSTOM_TARGET_KEY) || ""; }
export function setCustomTarget(value) { setRaw(CUSTOM_TARGET_KEY, String(value ?? "").trim()); }

export function getLiveConsent() { return getRaw(LIVE_CONSENT_KEY) === "1"; }
export function setLiveConsent() { setRaw(LIVE_CONSENT_KEY, "1"); }
export function getRinfoDefaults() {
  try {
    const saved = JSON.parse(getRaw(RINFO_KEY) || "{}");
    return { ...RINFO_SEED, ...(saved && typeof saved === "object" ? saved : {}) };
  } catch {
    return { ...RINFO_SEED };
  }
}
export function setRinfoDefaults(obj) { setRaw(RINFO_KEY, JSON.stringify(obj || {})); }
function loadEids() {
  try {
    const raw = JSON.parse(getRaw(EIDS_KEY) || "[]");
    return Array.isArray(raw) ? raw.filter((e) => e && EID_RE.test(e.eid)) : [];
  } catch { return []; }
}
export function rememberEid(value) {
  const eid = String(value || "").trim();
  if (EID_RE.test(eid)) {
    setRaw(EIDS_KEY, JSON.stringify(lruUpsert(loadEids(), (e) => e.eid === eid, { eid }, EID_MAX)));
  }
  return recentEids();
}
export function rememberEids(values) {
  let list = loadEids();
  let changed = false;
  for (const v of values || []) {
    const eid = String(v || "").trim();
    if (!EID_RE.test(eid)) continue;
    list = lruUpsert(list, (e) => e.eid === eid, { eid }, EID_MAX);
    changed = true;
  }
  if (changed) setRaw(EIDS_KEY, JSON.stringify(list));
  return recentEids();
}

export function recentEids() {
  return loadEids().sort((a, b) => b.order - a.order).map((e) => e.eid);
}
export function forgetEids() { setRaw(EIDS_KEY, "[]"); return []; }


export function getHistoryEnabled() {
  const raw = getRaw(HISTORY_ENABLED_KEY);
  return raw === null ? true : raw === "1";
}
export function setHistoryEnabled(on) { setRaw(HISTORY_ENABLED_KEY, on ? "1" : "0"); }

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

export function saveHistory(entry) {
  if (!getHistoryEnabled() || !entry || !entry.path) return getHistory();
  const list = lruUpsert(
    loadHistory(),
    (e) => e.path === entry.path && e.fieldsJson === entry.fieldsJson && (e.pathParam || "") === (entry.pathParam || ""),
    entry,
    HISTORY_MAX);
  setRaw(HISTORY_KEY, JSON.stringify(list));
  return getHistory();
}

export function deleteHistory(id) {
  setRaw(HISTORY_KEY, JSON.stringify(loadHistory().filter((e) => e.id !== id)));
  return getHistory();
}
export function clearHistory() { setRaw(HISTORY_KEY, "[]"); return []; }

export async function postForm(url, formBody) {
  const resp = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: formBody,
  });
  return (await resp.text()).trim();
}
