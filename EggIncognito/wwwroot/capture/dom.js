// Shared DOM element references for the capture dashboard. One lookup site so every module
// addresses the same nodes. Imported wherever a module touches the page chrome.

export const flowList = document.getElementById("flowList");
export const emptyState = document.getElementById("emptyState");
export const detail = document.getElementById("detail");
export const statusPill = document.getElementById("statusPill");
export const flowCount = document.getElementById("flowCount");
export const pauseBtn = document.getElementById("pauseBtn");
export const clearBtn = document.getElementById("clearBtn");
export const toastContainer = document.getElementById("toastContainer");
export const settingsBtn = document.getElementById("settingsBtn");
export const settingsMenu = document.getElementById("settingsMenu");
export const showHeadersToggle = document.getElementById("showHeadersToggle");
export const autoScrollToggle = document.getElementById("autoScrollToggle");
export const defaultFormatSelect = document.getElementById("defaultFormatSelect");
export const exportBtn = document.getElementById("exportBtn");
export const exportMenu = document.getElementById("exportMenu");
export const notifBtn = document.getElementById("notifBtn");
export const notifMenu = document.getElementById("notifMenu");
export const notifIcon = document.getElementById("notifIcon");
export const notifBadge = document.getElementById("notifBadge");
export const notifList = document.getElementById("notifList");
export const notifEmpty = document.getElementById("notifEmpty");
export const notifClearAll = document.getElementById("notifClearAll");
