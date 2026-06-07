// EventSource wiring for the live capture stream (flow / stats / notice events).

import { addFlow } from "./flowlist.js";
import { applyStats, showToast } from "./stats.js";
import { loadSnapshot, loadStats } from "./loaders.js";
import { pushNotice } from "./notifications.js";

// Map a server notice kind to a UI severity. The server sends event-name kinds like "certTrusted"
// / "decryptError"; everything unknown is treated as info.
function noticeSeverity(kind) {
  if (kind === "decryptError") return "error";
  if (kind === "certTrusted") return "ok";
  return "info";
}

export function openStream() {
  const es = new EventSource("/api/capture/stream");
  // On (re)connect, resync from the snapshot to avoid gaps; snapshot replay dedupes by id.
  es.addEventListener("open", () => { loadSnapshot(); loadStats(); });
  es.addEventListener("flow", (ev) => {
    try {
      addFlow(JSON.parse(ev.data));
    } catch (e) {
      console.warn("bad flow event", e);
    }
  });
  es.addEventListener("stats", (ev) => {
    try {
      applyStats(JSON.parse(ev.data));
    } catch (e) {
      console.warn("bad stats event", e);
    }
  });
  es.addEventListener("notice", (ev) => {
    try {
      const n = JSON.parse(ev.data);
      const severity = noticeSeverity(n.kind);
      // Transient toast (auto-dismiss) + a permanent entry in the notification center.
      showToast(severity, n.message, n.timestamp);
      pushNotice(severity, n.message, n.timestamp);
    } catch (e) {
      console.warn("bad notice event", e);
    }
  });
  es.addEventListener("error", () => {
    // EventSource auto-reconnects; nothing to do but stay alive.
    console.warn("capture stream error (will retry)");
  });
}
