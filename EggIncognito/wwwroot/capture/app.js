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
import { isPaused, setSelectedId, registerRenderDetail, renderDetail as renderSelectedDetail } from "./state.js";
import {
  setRedactionMode, reflectRedactionMode, getShowHeaders, setShowHeaders,
  getAutoScroll, setAutoScroll, getDefaultFormat, setDefaultFormat,
} from "./redaction.js";
import { setPaused, updateCount } from "./stats.js";
import { renderDetail } from "./detail.js";
import { clearFlows } from "./flowlist.js";
import { postJson, startCapture, stopCapture, captureStatus } from "./api.js";
import { loadSnapshot, loadStats, loadSensitiveKeys } from "./loaders.js";
import { openStream } from "./sse.js";
import { setIcon } from "./icons.js";
import { initNotifications, markAllRead } from "./notifications.js";

// Let state.renderSelected / flowlist.selectFlow drive the detail pane without importing detail.js
// directly (which would create an import cycle).
registerRenderDetail(renderDetail);

// Toolbar SVG icons (pause is set by setPaused). Clear/export/settings here.
setIcon(clearBtn, "trash");
setIcon(exportBtn, "download");
setIcon(settingsBtn, "gear");
initNotifications();

pauseBtn.addEventListener("click", async () => {
  const { ok, data } = await postJson(isPaused() ? "/api/capture/resume" : "/api/capture/pause");
  if (ok) setPaused(!!data.paused);
});

clearBtn.addEventListener("click", async () => {
  const { ok } = await postJson("/api/capture/clear");
  if (ok) {
    clearFlows();
    setSelectedId(null);
    renderSelectedDetail(null);
  }
});

// Settings popover: toggle on the gear, pick a mode, close on outside click / Escape.
settingsBtn.addEventListener("click", (e) => {
  e.stopPropagation();
  const open = settingsMenu.classList.toggle("hidden");
  settingsBtn.setAttribute("aria-expanded", String(!open));
});
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

// Default data format (persisted) - the view request/response sections open in.
defaultFormatSelect.value = getDefaultFormat();
defaultFormatSelect.addEventListener("change", () => setDefaultFormat(defaultFormatSelect.value));

// Export popover: toggle on the button, close on outside click / Escape.
exportBtn.addEventListener("click", (e) => {
  e.stopPropagation();
  const open = exportMenu.classList.toggle("hidden");
  exportBtn.setAttribute("aria-expanded", String(!open));
});

// Notification popover: toggle on the bell; opening it clears the unread badge.
notifBtn.addEventListener("click", (e) => {
  e.stopPropagation();
  const open = notifMenu.classList.toggle("hidden");
  notifBtn.setAttribute("aria-expanded", String(!open));
  if (!open) markAllRead();
});

function closeMenu(menu, btn) {
  menu.classList.add("hidden");
  btn.setAttribute("aria-expanded", "false");
}

document.addEventListener("click", (e) => {
  if (!settingsMenu.classList.contains("hidden") && !e.target.closest("#settingsWrap")) {
    closeMenu(settingsMenu, settingsBtn);
  }
  if (!exportMenu.classList.contains("hidden") && !e.target.closest("#exportWrap")) {
    closeMenu(exportMenu, exportBtn);
  }
  if (!notifMenu.classList.contains("hidden") && !e.target.closest("#notifWrap")) {
    closeMenu(notifMenu, notifBtn);
  }
});
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  if (!settingsMenu.classList.contains("hidden")) closeMenu(settingsMenu, settingsBtn);
  if (!exportMenu.classList.contains("hidden")) closeMenu(exportMenu, exportBtn);
  if (!notifMenu.classList.contains("hidden")) closeMenu(notifMenu, notifBtn);
});

// Capture lifecycle control (the proxy is off by default; Start/Stop toggle it at runtime).
const captureStartBtn = document.getElementById("captureStartBtn");
const captureStopBtn = document.getElementById("captureStopBtn");
const captureStatusEl = document.getElementById("captureStatus");

function reflectCaptureStatus(status) {
  const running = !!(status && status.running);
  if (captureStatusEl) {
    captureStatusEl.textContent = running
      ? `running (port ${status.port}, ${status.activeClients} client${status.activeClients === 1 ? "" : "s"})`
      : "stopped";
  }
  if (captureStartBtn) captureStartBtn.disabled = running;
  if (captureStopBtn) captureStopBtn.disabled = !running;
}

async function refreshCaptureStatus() {
  reflectCaptureStatus(await captureStatus());
}

if (captureStartBtn) {
  captureStartBtn.addEventListener("click", async () => {
    captureStartBtn.disabled = true;
    await startCapture();
    await refreshCaptureStatus();
  });
}
if (captureStopBtn) {
  captureStopBtn.addEventListener("click", async () => {
    captureStopBtn.disabled = true;
    await stopCapture();
    await refreshCaptureStatus();
  });
}

setPaused(false);
updateCount();
reflectRedactionMode();
refreshCaptureStatus();
Promise.all([loadSnapshot(), loadStats(), loadSensitiveKeys()]).then(openStream);
