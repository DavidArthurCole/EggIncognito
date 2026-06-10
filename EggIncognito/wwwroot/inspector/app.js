// Transport Inspector SPA. Plain ES module, no build step.
// Talks to /api/inspector/* on the same origin.

import { makeResizable } from "/resize.js";
import { rememberEid, recentEids, mostRecentEid, forgetEids } from "./eids.js";
import { getSalt, setSalt, getRinfoDefaults, setRinfoDefaults, getCustomTarget, setCustomTarget } from "./settings.js";

const API = "/api/inspector";

// Clear any stale bridge from a previous in-app navigation (the client router re-execs these modules;
// a leftover window.__inspector would make docs.js boot against old data before this init republishes).
delete window.__inspector;

const state = {
  endpoints: [],
  selected: null,        // EndpointInfo
  schemaCache: new Map(),// typeName -> SchemaMessage
  envDefaults: {},
  lastBuild: null,       // { finalBase64, finalFormBody }
};

const $ = (id) => document.getElementById(id);

async function getJson(url) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${r.status} ${await r.text()}`);
  return r.json();
}
async function postJson(url, body) {
  const r = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return r.json();
}

// init

async function init() {
  state.endpoints = await getJson(`${API}/endpoints`);
  state.envDefaults = getRinfoDefaults();
  renderEndpoints();
  renderEnvPanel();
  checkSign();

  refreshEidDatalist();

  // Live API egresses from the server; when hosted it requires login. Fetch the mode once and use it
  // to (a) gate the Live API toggle for anonymous hosted users, and (b) reveal the "Save to DB" button
  // only for contributor+ users. Fail open if mode is unreachable (local static dev).
  let appMode = null;
  try { appMode = await fetch("/api/app/mode").then(r => r.json()); } catch {}
  if (appMode && appMode.mode === "Hosted" && !appMode.user) {
    const liveBtn = document.querySelector('.target-opt[data-target="real"]');
    if (liveBtn) {
      liveBtn.disabled = true;
      liveBtn.classList.add("target-opt-disabled");
      liveBtn.title = "Log in to use Live API";
    }
  }
  // Writing to the shared DB needs contributor or admin. Only reveal the save affordance to those
  // users; everyone else never sees it (the server ACL is still the real gate). When mode is
  // unreachable (static local dev with no DB), fail open so a local dev can still see the button.
  const role = appMode?.user?.role;
  const canSaveDb = !appMode || role === "contributor" || role === "admin";
  if (canSaveDb) $("saveDbBtn").classList.remove("hidden");

  $("endpointFilter").addEventListener("input", renderEndpoints);
  $("buildBtn").addEventListener("click", build);
  $("sendBtn").addEventListener("click", send);
  $("refreshEndpoints").addEventListener("click", reloadEndpoints);
  document.querySelectorAll(".target-opt").forEach(b => b.addEventListener("click", () => {
    if (b.disabled) return; // gated (e.g. Live API for anonymous hosted users)
    // Switching to Live API sends real requests to auxbrain. Gate the FIRST switch per browser behind
    // an explicit consent (developers not responsible for misuse). Mock never prompts.
    if (b.dataset.target === "real" && localStorage.getItem(LIVE_CONSENT_KEY) !== "1") {
      showLiveConsent(() => { localStorage.setItem(LIVE_CONSENT_KEY, "1"); selectTarget(b); });
      return; // leave the toggle on Mock until accepted
    }
    selectTarget(b);
  }));
  $("signStatus").addEventListener("click", openSaltModal);
  $("saltCancel").addEventListener("click", closeSaltModal);
  $("saltSave").addEventListener("click", saveSaltModal);
  $("saltInput").addEventListener("keydown", (e) => { if (e.key === "Enter") saveSaltModal(); });
  $("liveCancel")?.addEventListener("click", hideLiveConsent);
  $("rawToggle").addEventListener("change", toggleRaw);
  $("forgetEids").addEventListener("click", () => {
    forgetEids();
    refreshEidDatalist();
    $("pathParamValue").value = "";
  });

  initSettings();
  syncCustomConfig(); // reflect the persisted target's custom-config visibility on load

  // Bridge for docs.js (the Documentation view). Kept as a tiny shared object rather than cross-module
  // imports to avoid a cycle: docs.js reads the endpoint list + mode and can drive endpoint selection.
  window.__inspector = {
    appMode,
    getEndpoints: () => state.endpoints,
    selectEndpointByPath: (path) => {
      const e = state.endpoints.find(x => x.path === path);
      if (e) { setActiveList("endpoints"); selectEndpoint(e); }
    },
    setActiveList,
  };
  window.dispatchEvent(new CustomEvent("inspector:ready"));

  // Left-pane Endpoints | Objects switch.
  document.querySelectorAll(".list-switch-opt").forEach(b =>
    b.addEventListener("click", () => setActiveList(b.dataset.list)));
}

// Toggle the left-pane list (and the matching middle-pane view) between Endpoints and Objects. docs.js
// owns the Objects list + the #objectView contents; here we just flip visibility + the filter target.
function setActiveList(which) {
  const isObjects = which === "objects";
  document.querySelectorAll(".list-switch-opt").forEach(b =>
    b.classList.toggle("active", b.dataset.list === which));
  $("endpoints").classList.toggle("hidden", isObjects);
  $("objects").classList.toggle("hidden", !isObjects);
  $("endpointView").classList.toggle("hidden", isObjects);
  $("objectView").classList.toggle("hidden", !isObjects);
  $("endpointFilter").placeholder = isObjects ? "filter objects..." : "filter...";
  window.dispatchEvent(new CustomEvent("inspector:listchange", { detail: { which } }));
}

// Reload the endpoint list (the refresh icon in the Endpoints header). Spins the icon for the
// duration so the user gets feedback even when the fetch is instant.
async function reloadEndpoints() {
  const btn = $("refreshEndpoints");
  btn.classList.add("spinning");
  try {
    // Bypass the browser cache so the Reload button always re-queries the server.
    const r = await fetch(`${API}/endpoints`, { cache: "no-store" });
    state.endpoints = await r.json();
    renderEndpoints();
  } catch (e) {
    // Non-fatal: keep the current list, surface nothing intrusive.
    console.warn("endpoint reload failed", e);
  } finally {
    // Keep the spin visible briefly even on an instant reload.
    setTimeout(() => btn.classList.remove("spinning"), 300);
  }
}

// Show the custom-target config block (and load its value) only when the Custom target is active.
function syncCustomConfig() {
  const cfg = $("customConfig");
  const isCustom = currentTarget() === "custom";
  cfg.classList.toggle("hidden", !isCustom);
  if (isCustom) $("customTargetInput").value = getCustomTarget();
}

function initSettings() {
  // Load current values into the popover inputs. (Salt lives in its own modal opened from the status
  // badge; the custom proxy URL lives in the in-pane #customConfig block - see syncCustomConfig.)
  const d = getRinfoDefaults();
  $("setClientVersion").value = d.clientVersion;
  $("setVersion").value = d.version;
  $("setBuild").value = d.build;
  $("setPlatform").value = d.platform;
  $("setCountry").value = d.country;
  $("setLanguage").value = d.language;
  $("setDebug").value = String(d.debug);

  $("setSave").addEventListener("click", () => {
    setRinfoDefaults({
      clientVersion: parseInt($("setClientVersion").value, 10),
      version: $("setVersion").value,
      build: $("setBuild").value,
      platform: $("setPlatform").value,
      country: $("setCountry").value,
      language: $("setLanguage").value,
      debug: $("setDebug").value === "true",
    });
    // Reflect the new defaults + sign state immediately.
    state.envDefaults = getRinfoDefaults();
    renderEnvPanel();
    checkSign();
    $("settingsMenu").classList.remove("open");
  });

  // Persist the custom proxy URL as it is edited (the in-pane block, not the Settings popover).
  $("customTargetInput").addEventListener("input", () => setCustomTarget($("customTargetInput").value));
}

// The signing salt is client-owned (settings.js); "can sign" is simply "is a salt set", no server probe.
// The badge is a button: short label + an explanatory tooltip; clicking opens the salt modal.
function checkSign() {
  const badge = $("signStatus");
  if (getSalt()) {
    badge.textContent = "Salt Set";
    badge.className = "badge signed";
    badge.title = "Signing salt is set (stored in this browser). Click to change it.";
  } else {
    badge.textContent = "Salt Unset";
    badge.className = "badge unsigned";
    badge.title = "No signing salt set. AuthenticatedMessage requests build unsigned. Click to set it.";
  }
}

// Salt modal: opened from the status badge. The salt stays client-owned (settings.js localStorage).
function openSaltModal() {
  $("saltInput").value = getSalt();
  $("saltModal").classList.remove("hidden");
  $("saltInput").focus();
}
function closeSaltModal() { $("saltModal").classList.add("hidden"); }
function saveSaltModal() {
  setSalt($("saltInput").value);
  checkSign();
  closeSaltModal();
}

// endpoint list

function renderEndpoints() {
  const filter = $("endpointFilter").value.toLowerCase();
  const groups = {};
  for (const e of state.endpoints) {
    if (filter && !e.path.toLowerCase().includes(filter)) continue;
    (groups[e.namespace] ??= []).push(e);
  }
  const host = $("endpoints");
  host.innerHTML = "";
  for (const ns of Object.keys(groups).sort()) {
    const g = document.createElement("div");
    g.className = "ep-group";
    // Header is a click toggle; the .collapsed class on the group drives both the caret rotation
    // (CSS triangle, no emoji) and the body's visibility. No persistence: a fresh render (incl. on
    // filter input) rebuilds every group expanded, so filtering auto-expands.
    const header = document.createElement("div");
    header.className = "ep-ns";
    header.innerHTML = `<span class="ep-caret"></span>${ns.toLowerCase()}/`;
    header.addEventListener("click", () => g.classList.toggle("collapsed"));
    const body = document.createElement("div");
    body.className = "ep-group-body";
    for (const e of groups[ns]) {
      const div = document.createElement("div");
      div.className = "ep" + (state.selected?.path === e.path ? " active" : "");
      div.innerHTML = e.path.slice(ns.length + 1) +
        (e.wrap ? '<span class="wrap-flag" title="wrapped + signed in an AuthenticatedMessage">signed</span>' : "");
      div.addEventListener("click", () => selectEndpoint(e));
      body.appendChild(div);
    }
    g.appendChild(header);
    g.appendChild(body);
    host.appendChild(g);
  }
}

async function selectEndpoint(e) {
  state.selected = e;
  state.lastBuild = null;
  $("sendBtn").disabled = true;
  renderEndpoints();
  $("reqTypeLabel").textContent = `(${reqTypeLabel(e)})`;
  $("pathParamRow").classList.toggle("hidden", !e.pathParam);
  if (e.pathParam) prefillEid($("pathParamValue")); // offer the last EID for path-param endpoints
  // Let docs.js paint this endpoint's tag chips (it owns the tag data + chip rendering).
  window.dispatchEvent(new CustomEvent("inspector:endpointselected", { detail: { path: e.path } }));
  await renderFieldTree();
}

// Human label for the request side: inner type + signing/wrap state, or "no body".
function reqTypeLabel(e) {
  if (e.pathParamOnly) return "no request body - identity via URL path param";
  const t = e.request ?? "no request type";
  return e.requestWrapped ? `${t}, signed (AuthenticatedMessage)` : t;
}

// schema + field tree

async function schema(typeName) {
  if (!typeName) return null; // never fetch schema/undefined
  if (!state.schemaCache.has(typeName)) {
    try { state.schemaCache.set(typeName, await getJson(`${API}/schema/${typeName}`)); }
    catch { state.schemaCache.set(typeName, null); }
  }
  return state.schemaCache.get(typeName);
}

async function renderFieldTree() {
  const host = $("fieldTree");
  host.innerHTML = "";
  $("rawJson").value = "{}";
  const e = state.selected;

  if (e.pathParamOnly) {
    host.innerHTML = `<span class="muted">No request body. Identity (EID) is supplied via the URL path parameter; nothing to fill in here.</span>`;
    return;
  }
  if (!e.request) {
    host.innerHTML = `<span class="muted">No known request type for this endpoint. Add a <code>request:</code> type in routes.yaml.</span>`;
    return;
  }

  if (e.requestWrapped) {
    const note = document.createElement("div");
    note.className = "wrap-note";
    note.textContent = "These are the inner request fields. On Build they will be wrapped and signed in an AuthenticatedMessage.";
    host.appendChild(note);
  }

  const s = await schema(e.request);
  if (!s) { host.insertAdjacentHTML("beforeend", `<span class="muted">no schema for ${e.request}</span>`); return; }
  for (const f of s.fields) host.appendChild(await fieldRow(f, []));
  applyEnvLock();
}

// The Environment panel overrides the message's rinfo (BasicRequestInfo) submessage at build time.
// Mirror each set env value onto the matching rinfo.<key> input in the tree and disable it, so the
// user sees exactly what gets sent and cannot desync the two. An empty env field releases the lock.
// Env keys that aren't rinfo fields (e.g. eiUserId) simply have no matching input and are skipped.
function applyEnvLock() {
  for (const inp of document.querySelectorAll("#envFields [data-env-key]")) {
    const el = document.querySelector(`#fieldTree [data-path="rinfo.${inp.dataset.envKey}"]`);
    if (!el) continue;
    const v = inp.value;
    if (v === "" || v == null) {
      el.disabled = false;
      el.classList.remove("env-locked");
      el.title = "";
    } else {
      el.value = String(v);
      el.disabled = true;
      el.classList.add("env-locked");
      el.title = "Set by the Environment panel above";
    }
  }
}

// Build one editable row. `path` is the field-name chain for collecting values later.
async function fieldRow(f, path, nested = false) {
  const row = document.createElement("div");
  row.className = "field-row" + (nested ? " nested" : "");
  const label = document.createElement("div");
  label.className = "field-name";
  label.innerHTML = `${f.jsonName}<span class="fnum">#${f.number}</span>` +
    `<span class="ftype">${f.repeated ? "repeated " : ""}${f.type}${f.messageType ? " " + f.messageType : ""}</span>`;
  row.appendChild(label);

  const fieldPath = [...path, f.jsonName];

  if (f.type === "message") {
    // Expandable nested message.
    label.style.gridColumn = "1 / -1";
    row.appendChild(document.createElement("span"));
    const wrap = document.createElement("div");
    wrap.style.gridColumn = "1 / -1";
    const sub = await schema(f.messageType);
    if (sub) for (const cf of sub.fields) wrap.appendChild(await fieldRow(cf, fieldPath, true));
    const container = document.createElement("div");
    container.appendChild(row);
    container.appendChild(wrap);
    return container;
  }

  const editor = f.repeated ? repeatedEditor(f, fieldPath) : scalarEditor(f, fieldPath);
  row.appendChild(editor);
  row.appendChild(document.createElement("span"));
  return row;
}

// 32-bit ints + floats can be a native number input. 64-bit ints are STRINGS in protojson and can
// exceed JS number precision, so they stay text with a numeric inputmode + a digit pattern. Anything
// else (string, enum name, bytes-b64) stays free text.
const NUM32 = ["int32", "uint32", "sint32", "fixed32", "sfixed32"];
const NUM64 = ["int64", "uint64", "sint64", "fixed64", "sfixed64"];
const FLOATS = ["double", "float"];

// A text/number input constrained to the proto field's type. Used by both the scalar and the
// repeated-item editors so the rules live in one place.
function typedInput(f) {
  const el = document.createElement("input");
  el.placeholder = f.type;
  if (NUM64.includes(f.type)) {
    el.inputMode = "numeric";
    el.pattern = String.raw`-?\d+`;
  } else if (NUM32.includes(f.type) || FLOATS.includes(f.type)) {
    el.type = "number";
    if (FLOATS.includes(f.type)) el.step = "any";
    if (f.type.startsWith("uint") || f.type.startsWith("fixed")) el.min = "0";
  }
  return el;
}

function scalarEditor(f, fieldPath) {
  let el;
  if (f.type === "enum") {
    el = document.createElement("select");
    el.appendChild(new Option("(unset)", ""));
    for (const v of f.enumValues || []) el.appendChild(new Option(`${v.name} (${v.number})`, v.name));
  } else if (f.type === "bool") {
    el = document.createElement("select");
    for (const v of ["(unset)", "true", "false"]) el.appendChild(new Option(v, v === "(unset)" ? "" : v));
  } else {
    el = typedInput(f);
  }
  el.dataset.path = fieldPath.join(".");
  el.dataset.ptype = f.type;
  el.className = "field-input";
  return el;
}

function repeatedEditor(f, fieldPath) {
  const wrap = document.createElement("div");
  wrap.className = "repeated-items";
  wrap.dataset.path = fieldPath.join(".");
  wrap.dataset.ptype = f.type;
  wrap.dataset.repeated = "1";
  const add = () => {
    const item = document.createElement("div");
    item.className = "repeated-item";
    const inp = typedInput(f);
    inp.className = "rep-input";
    const rm = document.createElement("button");
    rm.className = "btn-mini"; rm.textContent = "x";
    rm.addEventListener("click", () => item.remove());
    item.append(inp, rm);
    wrap.insertBefore(item, addBtn);
  };
  const addBtn = document.createElement("button");
  addBtn.className = "btn-mini"; addBtn.textContent = "+ add";
  addBtn.addEventListener("click", add);
  wrap.appendChild(addBtn);
  return wrap;
}

// Collect the tree into a nested JSON object (Google.Protobuf JSON shape).
function collectFields() {
  const obj = {};
  const setPath = (root, parts, value) => {
    let o = root;
    for (let i = 0; i < parts.length - 1; i++) o = (o[parts[i]] ??= {});
    o[parts[parts.length - 1]] = value;
  };

  // repeated
  for (const wrap of document.querySelectorAll("#fieldTree [data-repeated]")) {
    const vals = [...wrap.querySelectorAll(".rep-input")]
      .map((i) => coerce(i.value, wrap.dataset.ptype))
      .filter((v) => v !== undefined);
    if (vals.length) setPath(obj, wrap.dataset.path.split("."), vals);
  }
  // scalars
  for (const el of document.querySelectorAll("#fieldTree .field-input")) {
    if (el.dataset.repeated) continue;
    const v = coerce(el.value, el.dataset.ptype);
    if (v !== undefined) setPath(obj, el.dataset.path.split("."), v);
  }
  return obj;
}

function coerce(raw, ptype) {
  if (raw === "" || raw == null) return undefined;
  if (ptype === "bool") return raw === "true";
  if (["int32", "uint32", "sint32", "fixed32", "sfixed32"].includes(ptype)) return parseInt(raw, 10);
  if (["int64", "uint64", "sint64", "fixed64", "sfixed64"].includes(ptype)) return raw; // string in protojson
  if (["double", "float"].includes(ptype)) return parseFloat(raw);
  return raw; // string, enum (name), bytes(b64)
}

function toggleRaw() {
  const raw = $("rawToggle").checked;
  if (raw) $("rawJson").value = JSON.stringify(collectFields(), null, 2);
  $("rawJson").classList.toggle("hidden", !raw);
  $("fieldTree").classList.toggle("hidden", raw);
}

// env panel

function renderEnvPanel() {
  const host = $("envFields");
  host.innerHTML = "";
  for (const [k, v] of Object.entries(state.envDefaults)) {
    const row = document.createElement("div");
    row.className = "field-row";
    row.innerHTML = `<div class="field-name">${k}</div>`;
    const inp = document.createElement("input");
    inp.value = v;
    inp.dataset.envKey = k;
    inp.dataset.envType = typeof v;
    // Live-lock the matching rinfo.<key> tree input as this env value changes.
    inp.addEventListener("input", applyEnvLock);
    // EID-bearing env fields get the remembered-EID dropdown + a prefill when empty.
    if (k === "eiUserId" || k === "userId") {
      inp.setAttribute("list", "recentEids");
      prefillEid(inp);
    }
    row.appendChild(inp);
    row.appendChild(document.createElement("span"));
    host.appendChild(row);
  }
}

function collectEnv() {
  const env = {};
  for (const inp of document.querySelectorAll("#envFields [data-env-key]")) {
    let v = inp.value;
    if (inp.dataset.envType === "number") v = parseInt(v, 10);
    else if (inp.dataset.envType === "boolean") v = v === "true";
    env[inp.dataset.envKey] = v;
  }
  return env;
}

// build + send

async function build() {
  const e = state.selected;
  if (!e) return;

  // Path-param-only endpoints carry no request body (EID rides in the URL). There is
  // nothing to encode, so skip the build pipeline and post an empty body.
  if (e.pathParamOnly) {
    state.lastBuild = { finalBase64: "", finalFormBody: "" };
    $("stages").innerHTML = `<span class="muted">No request body to build - this endpoint sends the EID in the URL path. Press Send.</span>`;
    $("sendBtn").disabled = false;
    return;
  }
  if (!e.request) {
    renderError($("stages"), "no request type for this endpoint",
      "Add a `request:` type in routes.yaml before building.");
    return;
  }

  let fields;
  try {
    fields = $("rawToggle").checked ? JSON.parse($("rawJson").value) : collectFields();
  } catch (err) { renderError($("stages"), "invalid raw JSON: " + err.message); return; }

  const res = await postJson(`${API}/build`, {
    path: e.path,
    requestType: e.request,
    wrap: e.requestWrapped,
    fields,
    env: collectEnv(),
    salt: getSalt(),
  });
  if (res.error) { renderError($("stages"), res); return; }
  state.lastBuild = res;
  renderStages(res.stages);
  $("sendBtn").disabled = false;
}

// The send target is a segmented toggle (.target-opt buttons); the active one carries data-target.
// Defaults to mock - the Inspector hits the local mock unless the user explicitly opts into Live API.
function currentTarget() {
  return document.querySelector(".target-opt.active")?.dataset.target ?? "mock";
}

// Live-API consent: required once per browser before the first switch to the real auxbrain target.
const LIVE_CONSENT_KEY = "egi:liveApiConsent";

function selectTarget(btn) {
  document.querySelectorAll(".target-opt").forEach(x => x.classList.remove("active"));
  btn.classList.add("active");
  syncCustomConfig(); // reveal/hide the custom-target config block to match the new target
}

function showLiveConsent(onAccept) {
  const overlay = $("liveConsent");
  overlay.classList.remove("hidden");
  const accept = $("liveAccept");
  // Replace the handler each open so a prior onAccept closure is not retained.
  const handler = () => { accept.removeEventListener("click", handler); hideLiveConsent(); onAccept(); };
  accept.addEventListener("click", handler);
}

function hideLiveConsent() {
  $("liveConsent").classList.add("hidden");
}

async function send() {
  const e = state.selected;
  if (!state.lastBuild || !e) return;
  const target = currentTarget();

  if (target === "custom") {
    const proxy = getCustomTarget();
    if (!proxy) {
      renderError($("response"), "no custom proxy URL set",
        "Open Settings and set your Custom proxy URL first.");
      return;
    }
    // Build the same target path the real API would use, but send it to the user's proxy. The proxy
    // is expected to accept the same form body and relay to auxbrain. Browser-direct: no server egress.
    let proxyUrl = proxy.replace(/\/+$/, "") + "/" + e.path;
    if (e.pathParam) {
      const param = $("pathParamValue").value.trim();
      if (!param) { renderError($("response"), "URL path parameter is required", "Enter the EID above Build."); return; }
      proxyUrl += "/" + encodeURIComponent(param);
    }
    rememberEnteredEids();
    $("response").innerHTML = `<span class="muted">sending to your proxy ${proxyUrl} ...</span>`;
    let rawText;
    try {
      const resp = await fetch(proxyUrl, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: state.lastBuild.finalFormBody,
      });
      rawText = (await resp.text()).trim();
    } catch (err) {
      renderError($("response"), `direct request to your proxy failed: ${err.message}`,
        "Your proxy must be reachable from this browser and allow this origin (CORS).");
      return;
    }
    // Decode the bytes the browser now holds via the egress-free server decoder.
    const dec = await postJson(`${API}/decode-response`, { rawBase64: rawText, responseType: e.response });
    renderResponse({ status: 200, rawBase64: rawText, stages: dec.stages, json: dec.json, error: dec.error });
    if (dec?.json) window.__lastDecoded = { path: e.path, eid: $("pathParamValue")?.value || null, responseJson: dec.json, responseType: e.response };
    return;
  }

  const base = target === "mock"
    ? location.origin
    : (e.path.startsWith("ei_ctx") || e.path.startsWith("ei_srv")
        ? "https://ctx-dot-auxbrainhome.appspot.com"
        : "https://www.auxbrain.com");

  let url = `${base}/${e.path}`;
  if (e.pathParam) {
    const param = $("pathParamValue").value.trim();
    if (!param) {
      renderError($("response"), "URL path parameter is required for this endpoint",
        "Enter the EID (or other path value) in the field above the Build button.");
      return;
    }
    url += `/${encodeURIComponent(param)}`;
  }

  // Remember any EID-shaped values entered (path param + env fields) so they are offered next time.
  rememberEnteredEids();

  const respHost = $("response");
  respHost.innerHTML = `<span class="muted">sending to ${url} ...</span>`;

  const res = await postJson(`${API}/send`, {
    url,
    formBody: state.lastBuild.finalFormBody,
    responseType: e.response,
  });
  renderResponse(res);

  // Expose the last decoded response so the Tools dropdown's "Save to shared store" can offer it
  // (contributor+ writes to the DB; viewers fall back to a browser-local save). Best-effort.
  if (res?.json) {
    window.__lastDecoded = {
      path: e.path,
      eid: $("pathParamValue")?.value || null,
      responseJson: res.json,
      responseType: e.response,
    };
  }
}

// Remembered-EID helpers.

// Collect EID-shaped values from the path-param + env fields and remember them; refresh the list.
function rememberEnteredEids() {
  const candidates = [$("pathParamValue")?.value];
  for (const inp of document.querySelectorAll("#envFields [data-env-key]")) candidates.push(inp.value);
  let changed = false;
  for (const v of candidates) changed = rememberEid(v) || changed;
  if (changed) refreshEidDatalist();
}

// Populate the shared <datalist> from the remembered EIDs (most-recent-first).
function refreshEidDatalist() {
  const dl = $("recentEids");
  if (!dl) return;
  dl.replaceChildren();
  for (const eid of recentEids()) {
    const opt = document.createElement("option");
    opt.value = eid;
    dl.appendChild(opt);
  }
}

// Prefill an empty EID field with the most-recently-used EID. Never clobbers an existing value.
function prefillEid(input) {
  if (input && !input.value) {
    const last = mostRecentEid();
    if (last) input.value = last;
  }
}

// rendering

function renderStages(stages) {
  const host = $("stages");
  host.innerHTML = "";
  stages.forEach((s, i) => {
    const card = document.createElement("div");
    card.className = "stage" + (s.skipped ? " skipped" : "") + (s.role ? ` role-${s.role}` : "");
    const head = document.createElement("div");
    head.className = "stage-head";
    head.innerHTML =
      `<span class="num">${i}</span><span class="sname">${s.name}</span>` +
      (s.role ? `<span class="srole">${s.role}</span>` : "") +
      `<span class="slen">${s.byteLength} bytes</span>`;
    const body = document.createElement("div");
    body.className = "stage-body";
    body.innerHTML = `<div class="stage-desc">${escapeHtml(s.description)}</div>` +
      (s.note ? `<div class="stage-note">${escapeHtml(s.note)}</div>` : "");
    if (s.hex) body.appendChild(bytesBlock("hex", s.hex));
    if (s.base64) body.appendChild(bytesBlock("base64", s.base64));
    // The form-urlencode stage carries its body string in `note`; show it as a block too.
    if (s.role === "encoding" && s.name === "form-urlencode" && s.note)
      body.appendChild(bytesBlock("body", s.note));
    head.addEventListener("click", () => body.classList.toggle("hidden"));
    card.append(head, body);
    host.appendChild(card);
  });
}

function bytesBlock(label, value) {
  const wrap = document.createElement("div");
  wrap.innerHTML = `<div class="bytes-label">${label}</div><div class="bytes">${escapeHtml(value)}</div>`;
  return wrap;
}

function renderResponse(res) {
  const host = $("response");
  host.innerHTML = "";
  if (res.error && !res.stages) { renderError(host, res); return; }

  const status = document.createElement("div");
  const ok = res.status >= 200 && res.status < 300;
  status.className = "resp-status " + (ok ? "ok" : "bad");
  status.textContent = `HTTP ${res.status ?? "?"}`;
  host.appendChild(status);

  if (res.stages?.length) {
    const sub = document.createElement("div");
    host.appendChild(sub);
    const stagesHost = document.createElement("div");
    res.stages.forEach((s, i) => {
      const card = document.createElement("div");
      card.className = "stage";
      card.innerHTML =
        `<div class="stage-head"><span class="num">${i}</span>` +
        `<span class="sname">${s.name}</span><span class="slen">${s.byteLength} bytes</span></div>`;
      const body = document.createElement("div");
      body.className = "stage-body";
      body.innerHTML = `<div class="stage-desc">${s.description}</div>`;
      if (s.hex) body.appendChild(bytesBlock("hex", s.hex.slice(0, 512)));
      card.appendChild(body);
      stagesHost.appendChild(card);
    });
    host.appendChild(stagesHost);
  }

  if (res.error) {
    // Append (do not replace) so the decode stages above stay visible.
    const errHost = document.createElement("div");
    host.appendChild(errHost);
    renderError(errHost, res.error, res.resolution);
  }
  if (res.json) {
    const pre = document.createElement("pre");
    pre.className = "json";
    pre.textContent = prettyJson(res.json);
    host.appendChild(pre);
  }
}

// Accepts a plain string, or an {error, resolution, details} object. Always renders
// the resolution as a distinct "Possible fix" line when present - the app's rule is
// that no error ships without one.
function renderError(host, err, resolution) {
  let msg, fix, details;
  if (err && typeof err === "object") {
    msg = err.error ?? err.message ?? String(err);
    fix = err.resolution ?? resolution;
    details = err.details;
  } else {
    msg = err;
    fix = resolution;
  }
  let html = `<div class="error-box">${escapeHtml(msg)}</div>`;
  if (fix) html += `<div class="error-fix"><span class="fix-label">Possible fix:</span> ${escapeHtml(fix)}</div>`;
  if (details) html += `<pre class="error-details">${escapeHtml(JSON.stringify(details, null, 2))}</pre>`;
  host.innerHTML = html;
}

function prettyJson(s) {
  try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; }
}
function escapeHtml(s) {
  return String(s).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
}

// User-resizable columns (Endpoints | Request | Pipeline), persisted.
// Endpoints (index 0) is fixed-width - there's no value in resizing a name list. Only the
// Request | Pipeline pair gets a gutter.
makeResizable(document.querySelector("main"), { key: "inspector.cols", min: 180, fixed: [0] });

init().catch((e) => { document.body.insertAdjacentHTML("beforeend", `<pre class="error-box">init failed: ${e.message}</pre>`); });
