// Stats panel, cert pill, empty-state copy, toasts, and the running/stopped status pill.
//
/** @typedef {import('./types.d.ts').CaptureStats} CaptureStats */

import {
  emptyState, flowCount, statusPill, pauseBtn, toastContainer,
} from "./dom.js";
import { flows, getLatestStats, setLatestStats, setRunningState } from "./state.js";
import { formatBytes } from "./helpers.js";
import { setIcon } from "/icons.js";

export function updateCount() {
  const n = flows.size;
  flowCount.textContent = String(n);
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
      "No device connected yet." +
      "<div class=\"empty-hint\">Point the device's Wi-Fi proxy at this machine and install the CA cert. " +
      "The device only registers once an app sends a request through the proxy - the Settings app " +
      "alone usually sends nothing, so open a browser or Egg, Inc. to generate traffic.</div>";
  }
}

// Cert/trust state as a labeled value (not a jammed-in pill). The dd id is `certState`.
function updateCertState(state, activeConnections) {
  const el = document.getElementById("certState");
  if (!el) return;
  let cls = "cert-waiting", text = "Waiting for device";
  let title = "No device traffic yet - point the device's Wi-Fi proxy here and generate traffic.";
  if (state === "Trusted") {
    cls = "cert-trusted"; text = "Trusted - capturing";
    title = "Cert trusted - auxbrain traffic is decrypting.";
  } else if (state === "Untrusted") {
    cls = "cert-untrusted"; text = "Not trusted";
    title = "Device connected but the CA is not trusted - install + trust the CA on the device.";
  } else if (activeConnections > 0) {
    text = "Connected, awaiting traffic";
    title = "Device connected - waiting for the first auxbrain request to decrypt.";
  }
  el.className = cls;
  el.textContent = text;
  el.title = title;
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
  card.className = "device-card" + (d.online ? "" : " device-offline");

  // Head: online/offline dot + OS badge (when known) + IP + optional reverse-DNS hostname.
  const head = document.createElement("div");
  head.className = "device-head";
  const dot = document.createElement("span");
  dot.className = "device-dot " + (d.online ? "online" : "offline");
  dot.title = d.online ? "Connected now" : "Seen in a previous session (offline)";
  head.appendChild(dot);
  if (d.os) {
    const os = d.os.toLowerCase();
    let osClass = "os-other";
    if (os === "ios") osClass = "os-ios";
    else if (os === "android") osClass = "os-android";
    const badge = document.createElement("span");
    badge.className = "device-os " + osClass;
    badge.textContent = d.os;
    head.appendChild(badge);
  }
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

  // Labeled stat rows - no unlabeled dash/dot-separated blobs.
  const rows = document.createElement("dl");
  rows.className = "device-rows";
  const row = (label, value) => {
    const dt = document.createElement("dt"); dt.textContent = label;
    const dd = document.createElement("dd"); dd.textContent = value;
    rows.append(dt, dd);
  };
  row("Status", d.online ? "Connected" : "Offline");
  if (d.online) row("Connections", String(d.activeConnections));
  row("Seen", `${d.totalConnections} time${d.totalConnections === 1 ? "" : "s"}`);
  row("First seen", d.firstSeen);
  row("Last seen", d.lastSeen);
  if (d.gameVersion) row("Egg, Inc. version", d.gameVersion);
  card.appendChild(rows);

  return card;
}

/** @param {CaptureStats | null} stats */
export function applyStats(stats) {
  if (!stats) return;
  setLatestStats(stats);

  // The stats stream carries the live proxy running-state, so the pill stays correct even if the
  // page loaded mid-startup (the one-shot /status poll could have raced the proxy coming up).
  setRunning({ running: stats.running, port: stats.port });

  updateCertState(stats.certState, stats.activeConnections);

  const devices = Array.isArray(stats.devices) ? stats.devices : [];
  setText("statDeviceCount", devices.filter((d) => d.online).length);
  setText("statKnownCount", devices.length);
  setText("statActiveConns", stats.activeConnections ?? 0);
  renderDeviceCards(devices);

  setText("statCaptured", stats.capturedAuxbrain ?? 0);
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

// Reflect the proxy's running state. Accepts the /api/capture/status object (or a bare bool).
// Drives the status pill, the Session port/clients line, and the toolbar toggle button.
export function setRunning(status) {
  const running = typeof status === "boolean" ? status : !!status?.running;
  setRunningState(running);

  // Fold the listen port into the pill so the Session group is all pills, no stray text line.
  const port = (running && typeof status === "object") ? status?.port : null;
  let pillText = "STOPPED";
  if (running) pillText = port ? `RUNNING : ${port}` : "RUNNING";
  statusPill.textContent = pillText;
  statusPill.className = running ? "pill live" : "pill paused";

  // The toggle button is "stop" (pause icon) while running, "start" (play icon) while stopped.
  setIcon(pauseBtn, running ? "pause" : "play");
  pauseBtn.title = running ? "Stop capture" : "Start capture";
  pauseBtn.setAttribute("aria-label", running ? "Stop capture" : "Start capture");
  pauseBtn.classList.toggle("active", running);
}
