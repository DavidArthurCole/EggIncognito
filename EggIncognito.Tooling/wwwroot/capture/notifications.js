// Notification center: a bell button with an unread badge and a dropdown that STACKS notices over
// time so a timed-out toast is never missed. Each notice can be dismissed individually or all at
// once. Notices still pop as transient toasts (stats.js showToast) AND land here permanently.

import {
  notifBtn, notifIcon, notifBadge, notifList, notifEmpty, notifClearAll,
} from "./dom.js";
import { icon, setIcon } from "./icons.js";

let nextId = 1;
let unread = 0;
const notices = []; // { id, kind, message, timestamp }

export function initNotifications() {
  setIcon(notifIcon, "bell");
  render();
  notifClearAll.addEventListener("click", clearAll);
}

// Add a notice to the center. `kind` is "info" | "ok" | "warn" | "error" (drives the accent).
export function pushNotice(kind, message, timestamp) {
  notices.unshift({ id: nextId++, kind: kind || "info", message: message || "", timestamp: timestamp || "" });
  // Cap retained notices so a long session does not grow unbounded.
  if (notices.length > 200) notices.length = 200;
  unread++;
  render();
}

function dismiss(id) {
  const i = notices.findIndex((n) => n.id === id);
  if (i >= 0) notices.splice(i, 1);
  render();
}

function clearAll() {
  notices.length = 0;
  unread = 0;
  render();
}

// Called when the dropdown is opened - clears the unread count (notices stay listed).
export function markAllRead() {
  unread = 0;
  render();
}

function render() {
  // Badge
  if (unread > 0) {
    notifBadge.textContent = unread > 99 ? "99+" : String(unread);
    notifBadge.classList.remove("hidden");
    notifBtn.classList.add("has-unread");
  } else {
    notifBadge.classList.add("hidden");
    notifBtn.classList.remove("has-unread");
  }

  // List
  notifList.replaceChildren();
  notifEmpty.classList.toggle("hidden", notices.length > 0);
  for (const n of notices) {
    notifList.appendChild(buildItem(n));
  }
}

function buildItem(n) {
  const item = document.createElement("div");
  item.className = "notif-item notif-" + n.kind;

  const body = document.createElement("div");
  body.className = "notif-body";
  const msg = document.createElement("div");
  msg.className = "notif-msg";
  msg.textContent = n.message;
  body.appendChild(msg);
  if (n.timestamp) {
    const ts = document.createElement("div");
    ts.className = "notif-time";
    ts.textContent = n.timestamp;
    body.appendChild(ts);
  }

  const close = document.createElement("button");
  close.className = "notif-dismiss";
  close.title = "Dismiss";
  close.setAttribute("aria-label", "Dismiss notification");
  close.appendChild(icon("x", "icon-sm"));
  close.addEventListener("click", (e) => { e.stopPropagation(); dismiss(n.id); });

  item.append(body, close);
  return item;
}
