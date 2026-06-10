// Browser-side memory for the Capture Blazor tab. The small slice of JS that survives the port: the
// view preferences (redaction mode, show-headers, auto-scroll, compare-to-known, default format) live
// in this browser's localStorage exactly as the old redaction.js kept them. Server holds none of it.

const REDACTION_KEY = "capture.redaction";
const SHOW_HEADERS_KEY = "capture.showHeaders";
const AUTOSCROLL_KEY = "capture.autoScroll";
const COMPARE_KEY = "capture.compareToKnown";
const DEFAULT_FORMAT_KEY = "capture.defaultFormat";

const REDACTION_MODES = new Set(["off", "blur", "redact"]);

function getRaw(key) {
  try { return localStorage.getItem(key); } catch { return null; }
}
function setRaw(key, val) {
  try { localStorage.setItem(key, val); } catch { /* ignore */ }
}

export function getRedactionMode() {
  const stored = getRaw(REDACTION_KEY);
  return REDACTION_MODES.has(stored) ? stored : "blur";
}
export function setRedactionMode(mode) {
  if (REDACTION_MODES.has(mode)) setRaw(REDACTION_KEY, mode);
}

export function getShowHeaders() { return getRaw(SHOW_HEADERS_KEY) === "true"; }
export function setShowHeaders(value) { setRaw(SHOW_HEADERS_KEY, String(!!value)); }

// Default on.
export function getAutoScroll() { return getRaw(AUTOSCROLL_KEY) !== "false"; }
export function setAutoScroll(value) { setRaw(AUTOSCROLL_KEY, String(!!value)); }

export function getCompareToKnown() { return getRaw(COMPARE_KEY) === "true"; }
export function setCompareToKnown(value) { setRaw(COMPARE_KEY, String(!!value)); }

export function getDefaultFormat() { return getRaw(DEFAULT_FORMAT_KEY) || "json-tree"; }
export function setDefaultFormat(value) { setRaw(DEFAULT_FORMAT_KEY, String(value || "json-tree")); }
