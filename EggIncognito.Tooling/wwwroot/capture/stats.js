// Stats panel, cert pill, empty-state copy, toasts, and the live/paused status pill.
//
/** @typedef {import('./types.d.ts').CaptureStats} CaptureStats */

import {
  emptyState, certPill, flowCount, statusPill, pauseBtn, toastContainer,
} from "./dom.js";
import { flows, getLatestStats, setLatestStats, setPausedState } from "./state.js";
import { formatBytes } from "./helpers.js";
import { setIcon } from "./icons.js";

export function updateCount() {
  const n = flows.size;
  flowCount.textContent = `${n} ${n === 1 ? "request" : "requests"}`;
  emptyState.classList.toggle("hidden", n > 0);
  if (n === 0) updateEmptyState();
}

// Empty-state text depends on whether a device is connected (uses latest stats).
export function updateEmptyState() {
  const s = getLatestStats();
  const connected = s && (s.activeConnections > 0 || s.deviceCount > 0);
  if (connected) {
    emptyState.textContent = "No requests captured yet.";
  } else {
    emptyState.innerHTML =
      "No device connected." +
      "<div class=\"empty-hint\">Install the proxy CA cert on the device and point its Wi-Fi proxy at this machine.</div>";
  }
}

function updateCertPill(state, activeConnections) {
  if (state === "Trusted") {
    certPill.className = "cert-pill trusted";
    certPill.textContent = "Cert trusted - capturing";
  } else if (state === "Untrusted") {
    certPill.className = "cert-pill untrusted";
    certPill.textContent = "Device connected - cert NOT trusted (trust the CA on the device)";
  } else if (activeConnections > 0) {
    // Device is connected but no flow has decrypted yet - we genuinely do not know trust status.
    certPill.className = "cert-pill waiting";
    certPill.textContent = "Device connected - waiting for first request";
  } else {
    certPill.className = "cert-pill waiting";
    certPill.textContent = "Waiting for device";
  }
}

function setText(id, value) {
  const el = document.getElementById(id);
  if (el) el.textContent = value;
}

// A data-rich card per connected device: IP (+ reverse-DNS hostname if it resolved), active
// connection count, first/last-seen, and the User-Agent when a single device is connected.
/** @param {import('./types.d.ts').DeviceInfo[]} devices */
function renderDeviceCards(devices) {
  const box = document.getElementById("deviceCards");
  if (!box) return;
  box.replaceChildren();
  for (const d of devices) box.appendChild(buildDeviceCard(d));
}

/** @param {import('./types.d.ts').DeviceInfo} d */
function buildDeviceCard(d) {
  const card = document.createElement("div");
  card.className = "device-card";

  const head = document.createElement("div");
  head.className = "device-head";
  const ip = document.createElement("span");
  ip.className = "device-ip";
  ip.textContent = d.ip;
  head.appendChild(ip);
  if (d.hostname) {
    const host = document.createElement("span");
    host.className = "device-host";
    host.textContent = d.hostname;
    host.title = "Reverse-DNS hostname";
    head.appendChild(host);
  }
  card.appendChild(head);

  const meta = document.createElement("div");
  meta.className = "device-meta";
  meta.textContent = `${d.activeConnections} conn · ${d.firstSeen}–${d.lastSeen}`;
  card.appendChild(meta);

  if (d.userAgent) {
    const ua = document.createElement("div");
    ua.className = "device-ua";
    ua.textContent = d.userAgent;
    ua.title = d.userAgent;
    card.appendChild(ua);
  }
  return card;
}

/** @param {CaptureStats | null} stats */
export function applyStats(stats) {
  if (!stats) return;
  setLatestStats(stats);

  updateCertPill(stats.certState, stats.activeConnections);

  setText("statDeviceCount", stats.deviceCount ?? 0);
  setText("statActiveConns", stats.activeConnections ?? 0);
  renderDeviceCards(Array.isArray(stats.devices) ? stats.devices : []);

  setText("statCaptured", stats.capturedAuxbrain ?? 0);
  setText("statPassthrough", stats.passthrough ?? 0);
  setText("statEndpoints", stats.uniqueEndpoints ?? 0);

  setText("statDecryptOk", stats.decryptOk ?? 0);
  setText("statDecryptErrors", stats.decryptErrors ?? 0);
  const lastErr = document.getElementById("statLastError");
  if (lastErr) {
    if (stats.lastError) {
      // Single ellipsized line (CSS clamps it); full text on hover. Avoids the text wall.
      lastErr.textContent = "last: " + stats.lastError;
      lastErr.title = stats.lastError;
      lastErr.classList.remove("hidden");
    } else {
      lastErr.textContent = "";
      lastErr.title = "";
      lastErr.classList.add("hidden");
    }
  }

  setText("statBytes", formatBytes(stats.bytesCaptured));
  const biggest = document.getElementById("statBiggest");
  if (biggest) {
    if (stats.biggestEndpoint) {
      biggest.textContent =
        "biggest: " + stats.biggestEndpoint + " (" + formatBytes(stats.biggestEndpointBytes) + ")";
      biggest.classList.remove("hidden");
    } else {
      biggest.textContent = "";
      biggest.classList.add("hidden");
    }
  }

  // Empty-state copy depends on connection state.
  if (flows.size === 0) updateEmptyState();
}

export function showToast(kind, message, timestamp) {
  const toast = document.createElement("div");
  toast.className = "toast toast-" + (kind || "info");

  const msg = document.createElement("div");
  msg.className = "toast-msg";
  msg.textContent = message || "";
  toast.appendChild(msg);

  if (timestamp) {
    const ts = document.createElement("div");
    ts.className = "toast-time";
    ts.textContent = timestamp;
    toast.appendChild(ts);
  }

  toastContainer.appendChild(toast);
  // Trigger slide-in on next frame.
  requestAnimationFrame(() => toast.classList.add("show"));

  const remove = () => {
    toast.classList.remove("show");
    toast.classList.add("leaving");
    setTimeout(() => toast.remove(), 300);
  };
  setTimeout(remove, 5000);
  toast.addEventListener("click", remove);
}

export function setPaused(value) {
  setPausedState(value);
  statusPill.textContent = value ? "PAUSED" : "LIVE";
  statusPill.className = value ? "pill paused" : "pill live";
  // Swap the SVG icon (play vs pause) - do NOT write a text label, which would clobber the icon.
  setIcon(pauseBtn, value ? "play" : "pause");
  pauseBtn.title = value ? "Resume capturing" : "Pause capturing";
  pauseBtn.setAttribute("aria-label", value ? "Resume capturing" : "Pause capturing");
  pauseBtn.classList.toggle("active", value);
}
