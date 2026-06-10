// EggIncognito - Live Capture dashboard entry point.
// Wires DOM controls and boots the stream. The behavior lives in the imported ES modules:
//   dom/state/helpers   - shared element refs, mutable app state, formatting utilities
//   redaction           - redaction mode + value/path rendering
//   tree                - the collapsible/searchable JSON tree viewer
//   stats               - stats panel, cert pill, empty state, toasts, status pill
//   detail              - the detail pane (types, params, JSON, known card, save)
//   flowlist            - the live flow list (rows, outcome tags, select)
//   loaders/sse/api     - backend fetches + the EventSource stream

import {
  pauseBtn, clearBtn, settingsBtn, settingsMenu, showHeadersToggle, exportBtn, exportMenu,
  notifBtn, notifMenu, autoScrollToggle, defaultFormatSelect,
} from "./dom.js";
import { isRunning, setSelectedId, registerRenderDetail, renderDetail as renderSelectedDetail } from "./state.js";
import {
  setRedactionMode, reflectRedactionMode, getShowHeaders, setShowHeaders,
  getAutoScroll, setAutoScroll, getDefaultFormat, setDefaultFormat,
  getCompareToKnown, setCompareToKnown,
} from "./redaction.js";
import { setRunning, updateCount } from "./stats.js";
import { renderDetail } from "./detail.js";
import { clearFlows, setFilters, rebuildFlowRows } from "./flowlist.js";
import { postJson, startCapture, stopCapture, captureStatus } from "./api.js";
import { loadSnapshot, loadStats, loadSensitiveKeys } from "./loaders.js";
import { openStream } from "./sse.js";
import { setIcon } from "/icons.js";
import { initNotifications, markAllRead } from "./notifications.js";
import { makeResizable } from "/resize.js";

// Let state.renderSelected / flowlist.selectFlow drive the detail pane without importing detail.js
// directly (which would create an import cycle).
registerRenderDetail(renderDetail);

// Toolbar SVG icons (the toggle icon is set by setRunning). Clear/export/settings here.
setIcon(clearBtn, "trash");
setIcon(exportBtn, "download");
setIcon(settingsBtn, "gear");
initNotifications();

// The pause/play button is the single capture toggle: it starts and stops the proxy itself.
pauseBtn.addEventListener("click", async () => {
  pauseBtn.disabled = true;
  if (isRunning()) await stopCapture();
  else await startCapture();
  await refreshCaptureStatus();
  pauseBtn.disabled = false;
});

// Clearing is confirmed via a small popover anchored to the clear button (see the popover registry
// below), not a center-screen browser confirm(). The Confirm button does the actual clear.
const clearMenu = document.getElementById("clearMenu");
document.getElementById("clearConfirm").addEventListener("click", async () => {
  clearMenu.classList.remove("open");
  clearBtn.setAttribute("aria-expanded", "false");
  const { ok } = await postJson("/api/capture/clear");
  if (ok) {
    clearFlows();
    setSelectedId(null);
    renderSelectedDetail(null);
  }
});
document.getElementById("clearCancel").addEventListener("click", () => {
  clearMenu.classList.remove("open");
  clearBtn.setAttribute("aria-expanded", "false");
});

// Request-list filters: API group (select), path substring, request/response type substring.
const filterGroup = document.getElementById("filterGroup");
const filterPath = document.getElementById("filterPath");
const filterType = document.getElementById("filterType");
const applyFilters = () => setFilters({
  group: filterGroup.value,
  path: filterPath.value,
  type: filterType.value,
});
filterGroup.addEventListener("change", applyFilters);
filterPath.addEventListener("input", applyFilters);
filterType.addEventListener("input", applyFilters);
document.getElementById("filterClear").addEventListener("click", () => {
  filterGroup.value = "";
  filterPath.value = "";
  filterType.value = "";
  applyFilters();
});

// Popover registry. Each entry is a button + its menu (which animates open/closed via the `.open`
// class) + the wrap that scopes outside-click + an optional onClose hook. Only ONE popover can be
// open at a time: opening any closes the rest; clicking another button or anywhere outside closes
// the current one.
const popovers = [
  { btn: settingsBtn, menu: settingsMenu, wrap: "#settingsWrap" },
  { btn: exportBtn, menu: exportMenu, wrap: "#exportWrap" },
  { btn: notifBtn, menu: notifMenu, wrap: "#notifWrap", onClose: markAllRead },
  { btn: clearBtn, menu: clearMenu, wrap: "#clearWrap" },
];

function setPopover(target, open) {
  target.menu.classList.toggle("open", open);
  target.btn.setAttribute("aria-expanded", String(open));
  if (!open && target.onClose) target.onClose();
}

function closeAllPopovers(except) {
  for (const p of popovers) {
    if (p !== except && p.menu.classList.contains("open")) setPopover(p, false);
  }
}

for (const p of popovers) {
  p.btn.addEventListener("click", (e) => {
    e.stopPropagation();
    const willOpen = !p.menu.classList.contains("open");
    closeAllPopovers(p);          // mutual exclusion: close any other open popover first
    setPopover(p, willOpen);
  });
}

settingsMenu.addEventListener("click", (e) => {
  const btn = e.target.closest(".seg-btn");
  if (!btn) return;
  setRedactionMode(btn.dataset.mode);
});

// Show-headers toggle (default off, persisted).
showHeadersToggle.checked = getShowHeaders();
showHeadersToggle.addEventListener("change", () => setShowHeaders(showHeadersToggle.checked));

// Auto-scroll toggle (default on, persisted).
autoScrollToggle.checked = getAutoScroll();
autoScrollToggle.addEventListener("change", () => setAutoScroll(autoScrollToggle.checked));

// Compare-to-known toggle (default off, persisted). Re-render the whole list so outcome tags
// appear/disappear on every row, not just the selected detail.
const compareToggle = document.getElementById("compareToggle");
compareToggle.checked = getCompareToKnown();
compareToggle.addEventListener("change", () => {
  setCompareToKnown(compareToggle.checked);
  rebuildFlowRows();
});

// Default data format (persisted) - the view request/response sections open in.
defaultFormatSelect.value = getDefaultFormat();
defaultFormatSelect.addEventListener("change", () => setDefaultFormat(defaultFormatSelect.value));

// Outside click closes whichever popover is open (unless the click was inside its own wrap). These
// bind to document (outside the swapped <main>), so register cleanups with the client router (if
// present) to avoid stacking listeners across in-app navigations.
const onDocClick = (e) => {
  for (const p of popovers) {
    if (p.menu.classList.contains("open") && !e.target.closest(p.wrap)) setPopover(p, false);
  }
};
const onDocKey = (e) => { if (e.key === "Escape") closeAllPopovers(); };
document.addEventListener("click", onDocClick);
document.addEventListener("keydown", onDocKey);
window.__router?.onCleanup(() => {
  document.removeEventListener("click", onDocClick);
  document.removeEventListener("keydown", onDocKey);
});

async function refreshCaptureStatus() {
  setRunning(await captureStatus());
}

// User-resizable columns, persisted. Stats (index 0) is the narrow fixed-width strip - pinning it
// (rather than the data panes) keeps the two large content panes (Requests | Detail) responsive and
// draggable while dropping the low-value gutter beside the stats strip.
makeResizable(document.querySelector("main"), { key: "capture.cols", min: 180, fixed: [0] });

updateCount();
reflectRedactionMode();
refreshCaptureStatus();
// Open the live SSE stream and register its teardown with the client router so it is closed (not
// leaked) when the user navigates to another tab in-app.
Promise.all([loadSnapshot(), loadStats(), loadSensitiveKeys()]).then(() => {
  const es = openStream();
  window.__router?.onCleanup(() => es?.close());
});
