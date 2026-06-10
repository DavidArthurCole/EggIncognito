// Client-owned Inspector settings (per-browser, localStorage). The signing salt lives here, not on
// the server: each operator supplies their own and it is sent only in the build request they make.
// The BasicRequestInfo defaults are seeded here too (formerly GET /api/inspector/env-defaults).
// EIDs and salts can be identifying/secret, so this stays browser-local.

const SALT_KEY = "inspector.salt";
const RINFO_KEY = "inspector.rinfoDefaults";
const CUSTOM_TARGET_KEY = "inspector.customTarget";

// Seed defaults for BasicRequestInfo - the standard client constants a real request carries. These
// were previously the server's DefaultRInfo; they are not secret, just sensible starting values.
export const RINFO_SEED = {
  clientVersion: 72,
  version: "1.35.7",
  build: "111343",
  platform: "DROID",
  country: "US",
  language: "en",
  debug: false,
};

export function getSalt() {
  try { return localStorage.getItem(SALT_KEY) || ""; } catch { return ""; }
}

export function setSalt(value) {
  try { localStorage.setItem(SALT_KEY, String(value ?? "")); } catch { /* ignore */ }
}

export function hasSalt() {
  return getSalt().length > 0;
}

// The rinfo defaults: the seed merged with any saved overrides. Always returns all seven keys.
export function getRinfoDefaults() {
  try {
    const saved = JSON.parse(localStorage.getItem(RINFO_KEY) || "{}");
    return { ...RINFO_SEED, ...(saved && typeof saved === "object" ? saved : {}) };
  } catch {
    return { ...RINFO_SEED };
  }
}

export function setRinfoDefaults(obj) {
  try { localStorage.setItem(RINFO_KEY, JSON.stringify(obj || {})); } catch { /* ignore */ }
}

// The Custom send target: a user-owned proxy URL the browser posts the built request to directly
// (the server is bypassed - zero egress from this instance). Browser-local like the salt.
export function getCustomTarget() {
  try { return localStorage.getItem(CUSTOM_TARGET_KEY) || ""; } catch { return ""; }
}

export function setCustomTarget(value) {
  try { localStorage.setItem(CUSTOM_TARGET_KEY, String(value ?? "").trim()); } catch { /* ignore */ }
}
