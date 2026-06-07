// Snapshot/stats/sensitive-keys loaders: fetch the backend state and apply it to the UI. Kept
// separate from app.js so sse.js can re-trigger a resync on (re)connect without importing app.js.

import { flowList } from "./dom.js";
import { flows, getSelectedId, setSelectedId, renderDetail } from "./state.js";
import { getJson } from "./api.js";
import { addFlow, clearFlows } from "./flowlist.js";
import { applyStats } from "./stats.js";
import { sensitiveKeys } from "./redaction.js";

export async function loadSnapshot() {
  const list = await getJson("/api/capture/flows");
  if (!list) return;
  clearFlows();
  // Snapshot is oldest first; render without per-row auto-scroll, then jump to bottom once.
  for (const flow of list) addFlow(flow, { allowAutoScroll: false });
  flowList.scrollTop = flowList.scrollHeight;
  if (getSelectedId() !== null && !flows.has(getSelectedId())) {
    setSelectedId(null);
    renderDetail(null);
  }
}

export async function loadStats() {
  const stats = await getJson("/api/capture/stats");
  if (stats) applyStats(stats);
}

// Fetch the sensitive field-name set once on load (used by "blur" mode).
export async function loadSensitiveKeys() {
  const data = await getJson("/api/capture/sensitive-keys");
  if (data && Array.isArray(data.keys)) for (const k of data.keys) sensitiveKeys.add(k);
}
