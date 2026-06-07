// Redaction mode state + the value/path rendering that honors it.
// Three modes: "off" (raw json), "blur" (raw json, sensitive values blurred), "redact"
// (pre-redacted json with "redacted-xxxx" tokens). Persisted in localStorage; default "blur".

import { settingsMenu } from "./dom.js";
import { EID_RE, renderSelected } from "./state.js";
import { makeBlurred } from "./tree.js";

const REDACTION_KEY = "capture.redaction";
const REDACTION_MODES = ["off", "blur", "redact"];

// Field names whose values are blurred in "blur" mode (any depth). Seeded with the EID fields;
// the rest come from /api/capture/sensitive-keys.
export const sensitiveKeys = new Set(["eiUserId", "userId"]);

function loadRedactionMode() {
  const stored = localStorage.getItem(REDACTION_KEY);
  return REDACTION_MODES.includes(stored) ? stored : "blur";
}
let redactionMode = loadRedactionMode();

export function setRedactionMode(mode) {
  if (!REDACTION_MODES.includes(mode)) return;
  redactionMode = mode;
  localStorage.setItem(REDACTION_KEY, mode);
  reflectRedactionMode();
  // Re-render the currently selected flow so the change applies immediately.
  renderSelected();
}

// Sync the segmented-control active state with the current mode.
export function reflectRedactionMode() {
  for (const btn of settingsMenu.querySelectorAll(".seg-btn")) {
    btn.classList.toggle("active", btn.dataset.mode === redactionMode);
  }
}

// Pick which JSON string to render for a flow side given the current mode.
// "redact" => pre-redacted; "off"/"blur" => raw (fall back to redacted if null).
// The blur of individual values happens later, per-key, in the tree builder.
export function pickJson(redacted, raw) {
  if (redactionMode === "redact") return redacted ?? null;
  return raw ?? redacted ?? null;
}

// Apply the current redaction mode to a single string value (path/query param). EID-looking values
// are redacted ("redacted-eid") or blurred; others pass through.
export function redactParamValue(value, span) {
  const s = String(value);
  if (redactionMode === "redact" && EID_RE.test(s)) {
    span.textContent = "redacted-eid";
    return;
  }
  span.textContent = s;
  if (redactionMode === "blur" && EID_RE.test(s)) makeBlurred(span);
}

// Render a full path string into `span`, tokenizing on '/' so any EID-looking segment respects the
// current redaction mode while the rest of the path stays raw. "redact" => "redacted-eid";
// "blur" => blurred (hover/click) span; "off" => raw.
export function renderRedactedPath(path, span) {
  const s = String(path);
  // Split keeping the separators so the path renders exactly as-is.
  const parts = s.split(/(\/)/);
  for (const part of parts) {
    if (part && EID_RE.test(part)) {
      if (redactionMode === "redact") {
        span.appendChild(document.createTextNode("redacted-eid"));
        continue;
      }
      const seg = document.createElement("span");
      seg.textContent = part;
      if (redactionMode === "blur") makeBlurred(seg);
      span.appendChild(seg);
    } else if (part) {
      span.appendChild(document.createTextNode(part));
    }
  }
}

// Returns true when a value under `keyName` should be blurred: blur mode is active and the field
// name is in the sensitive set.
export function isSensitiveKey(keyName) {
  return redactionMode === "blur" && keyName != null && sensitiveKeys.has(keyName);
}

// --- show-headers preference (default off) --------------------------------

const SHOW_HEADERS_KEY = "capture.showHeaders";
let showHeaders = localStorage.getItem(SHOW_HEADERS_KEY) === "true";

export function getShowHeaders() { return showHeaders; }

export function setShowHeaders(value) {
  showHeaders = !!value;
  localStorage.setItem(SHOW_HEADERS_KEY, String(showHeaders));
  // Re-render so the headers section appears/disappears immediately.
  renderSelected();
}

// True when blur mode is active (headers carry their own Sensitive flag, computed server-side).
export function isBlurMode() {
  return redactionMode === "blur";
}

// Whether to render the unredacted header value: only in Off mode (mirrors body raw behavior).
export function showRawHeaders() {
  return redactionMode === "off";
}

// --- auto-scroll preference (default on) ----------------------------------

const AUTOSCROLL_KEY = "capture.autoScroll";
let autoScroll = localStorage.getItem(AUTOSCROLL_KEY) !== "false"; // default true
export function getAutoScroll() { return autoScroll; }
export function setAutoScroll(value) {
  autoScroll = !!value;
  localStorage.setItem(AUTOSCROLL_KEY, String(autoScroll));
}

// --- default data format preference (default JSON tree) -------------------

const DEFAULT_FORMAT_KEY = "capture.defaultFormat";
let defaultFormat = localStorage.getItem(DEFAULT_FORMAT_KEY) || "json-tree";
export function getDefaultFormat() { return defaultFormat; }
export function setDefaultFormat(value) {
  defaultFormat = value;
  localStorage.setItem(DEFAULT_FORMAT_KEY, value);
  renderSelected();
}
