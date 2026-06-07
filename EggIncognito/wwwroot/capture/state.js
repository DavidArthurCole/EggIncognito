// Shared mutable app state for the capture dashboard. Centralized here so the modules that split
// out of the old single-scope app.js (flow list, detail pane, redaction, sse) read and mutate the
// same state without import cycles. State is exposed via accessor functions, not bare `let` exports
// (those would be read-only snapshots in importers).

// id -> { flow, row } so we can render incrementally and keep selection stable.
export const flows = new Map();

let selectedId = null;
export function getSelectedId() { return selectedId; }
export function setSelectedId(id) { selectedId = id; }

let paused = false;
export function isPaused() { return paused; }
export function setPausedState(v) { paused = v; }

// Latest stats snapshot (from /api/capture/stats and "stats" stream events).
let latestStats = null;
export function getLatestStats() { return latestStats; }
export function setLatestStats(s) { latestStats = s; }

// Matches an EID anywhere in a string (path params, query values, etc).
export const EID_RE = /EI\d{10,}/;

// Late-bound renderer for the selected flow. detail.js registers renderDetail here so redaction.js
// and the flow list can trigger a re-render without importing detail.js (which would create a
// cycle: detail -> tree/redaction -> ... -> detail).
let _renderDetail = () => {};
export function registerRenderDetail(fn) { _renderDetail = fn; }
export function renderSelected() {
  const entry = selectedId !== null ? flows.get(selectedId) : null;
  _renderDetail(entry ? entry.flow : null);
}
export function renderDetail(flow) { _renderDetail(flow); }
