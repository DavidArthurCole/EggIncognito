// Inspector nav-toolbar wiring: the toolbar icons (Postman export, Settings), the Settings popover
// open/close behaviour, the Endpoint-status check (now inside Settings), and the in-pane "Save to DB"
// affordance. Salt / custom-target / request-defaults field values are loaded + saved by app.js's
// initSettings (it owns the settings model); this file owns the chrome around them.
import { setIcon } from "/icons.js";

// Toolbar icons (shared SVG set). Postman is an <a download>; Settings opens the popover.
setIcon(document.getElementById("postmanBtn"), "download");
setIcon(document.getElementById("settingsBtn"), "gear");
setIcon(document.getElementById("refreshEndpoints"), "refresh");

// Settings popover: a click-toggle panel (matches the Capture toolbar). Opening toggles `.open`;
// a click outside the wrap, or Escape, closes it.
const settingsBtn = document.getElementById("settingsBtn");
const settingsMenu = document.getElementById("settingsMenu");
function setSettings(open) {
  settingsMenu.classList.toggle("open", open);
  settingsBtn.setAttribute("aria-expanded", String(open));
}
settingsBtn.addEventListener("click", (e) => {
  e.stopPropagation();
  setSettings(!settingsMenu.classList.contains("open"));
});
// These bind to document (outside the swapped <main>), so register cleanups with the router to avoid
// stacking listeners across in-app navigations.
const onDocClick = (e) => {
  if (settingsMenu.classList.contains("open") && !e.target.closest("#settingsWrap")) setSettings(false);
};
const onDocKey = (e) => {
  if (e.key === "Escape" && settingsMenu.classList.contains("open")) setSettings(false);
};
document.addEventListener("click", onDocClick);
document.addEventListener("keydown", onDocKey);
window.__router?.onCleanup(() => {
  document.removeEventListener("click", onDocClick);
  document.removeEventListener("keydown", onDocKey);
});

// Endpoint status (inside Settings): how many mapped routes have an ok / empty / missing fixture.
const statusBtn = document.getElementById("statusBtn");
const statusOut = document.getElementById("statusOut");
const statusCounts = document.getElementById("statusCounts");
statusBtn?.addEventListener("click", async () => {
  statusOut.textContent = "Loading...";
  try {
    const r = await fetch("/api/tools/endpoint-status").then(x => x.json());
    statusCounts.textContent = `(ok ${r.ok.length} / empty ${r.empty.length} / missing ${r.missing.length})`;
    const lines = [];
    if (r.empty.length) lines.push("empty:", ...r.empty.map(p => "  " + p));
    if (r.missing.length) lines.push("missing:", ...r.missing.map(p => "  " + p));
    statusOut.textContent = lines.length ? lines.join("\n") : "All mapped routes have a fixture.";
  } catch (e) {
    statusOut.textContent = `Request failed: ${e}`;
  }
});

// Save the last decoded response to the shared DB as a stored endpoint. The button is revealed only
// for contributor+ users (app.js does that from /api/app/mode), so this is the authoritative-write
// path; we still handle a 403 defensively. app.js sets window.__lastDecoded on each decode
// ({ path, eid, responseJson, responseType }).
const saveBtn = document.getElementById("saveDbBtn");
const saveOut = document.getElementById("saveOut");
saveBtn?.addEventListener("click", async () => {
  const d = window.__lastDecoded;
  if (!d || !d.path || !d.responseJson) { saveOut.textContent = "Build + send a request first."; return; }
  saveOut.textContent = "Saving...";
  try {
    const res = await fetch("/api/db/endpoint", {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path: d.path, eid: d.eid ?? null, responseJson: d.responseJson, responseType: d.responseType }),
    });
    if (res.ok) {
      saveOut.textContent = `Saved ${d.path}${d.eid ? " (" + d.eid + ")" : ""} to the shared DB.`;
      return;
    }
    if (res.status === 403) { saveOut.textContent = "Not authorized to write to the shared DB."; return; }
    saveOut.textContent = `Save failed: HTTP ${res.status}`;
  } catch (e) {
    saveOut.textContent = `Request failed: ${e}`;
  }
});
