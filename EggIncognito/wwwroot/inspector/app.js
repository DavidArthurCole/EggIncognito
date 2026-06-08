// Transport Inspector SPA. Plain ES module, no build step.
// Talks to /api/inspector/* on the same origin.

const API = "/api/inspector";

const state = {
  endpoints: [],
  selected: null,        // EndpointInfo
  schemaCache: new Map(),// typeName -> SchemaMessage
  envDefaults: {},
  lastBuild: null,       // { finalBase64, finalFormBody }
  logsSince: 0,          // last log sequence number seen
  logsLevel: "basic",
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
  state.envDefaults = await getJson(`${API}/env-defaults`);
  renderEndpoints();
  renderEnvPanel();
  await checkSign();

  $("endpointFilter").addEventListener("input", renderEndpoints);
  $("buildBtn").addEventListener("click", build);
  $("sendBtn").addEventListener("click", send);
  $("rawToggle").addEventListener("change", toggleRaw);

  initLogs();
}

// logs panel

function initLogs() {
  for (const r of document.querySelectorAll('input[name="logLevel"]')) {
    r.addEventListener("change", () => {
      state.logsLevel = document.querySelector('input[name="logLevel"]:checked').value;
      state.logsSince = 0;
      $("logs").innerHTML = "";
      pollLogs();
    });
  }
  $("logsClear").addEventListener("click", () => { $("logs").innerHTML = ""; });
  pollLogs();
  setInterval(pollLogs, 1500);
}

async function pollLogs() {
  let entries;
  try {
    entries = await getJson(`${API}/logs?level=${state.logsLevel}&since=${state.logsSince}`);
  } catch { return; } // transient - try again next tick
  if (!entries.length) return;

  const host = $("logs");
  const advanced = state.logsLevel === "advanced";
  for (const e of entries) {
    state.logsSince = Math.max(state.logsSince, e.seq);
    const row = document.createElement("div");
    row.className = `log log-${e.level.toLowerCase()}`;
    let line = `<span class="log-time">${e.time}</span>` +
      `<span class="log-level">${e.level}</span>`;
    if (advanced && e.category) line += `<span class="log-cat">${escapeHtml(e.category)}</span>`;
    line += `<span class="log-msg">${escapeHtml(e.message)}</span>`;
    row.innerHTML = line;
    if (advanced && e.exception) {
      const ex = document.createElement("pre");
      ex.className = "log-exc";
      ex.textContent = e.exception;
      row.appendChild(ex);
    }
    host.appendChild(row);
  }
  if ($("logsAutoScroll").checked) host.scrollTop = host.scrollHeight;
}

async function checkSign() {
  // Build an empty request just to learn canSign without committing to an endpoint.
  const probe = state.endpoints.find((e) => e.request);
  if (!probe) return;
  const res = await postJson(`${API}/build`, {
    path: probe.path, requestType: probe.request, wrap: false, fields: {}, env: null,
  });
  const badge = $("signStatus");
  if (res.canSign) { badge.textContent = "signing: ready"; badge.className = "badge signed"; }
  else { badge.textContent = "signing: EGG_INC_API_SALT not set"; badge.className = "badge unsigned"; }
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
    g.innerHTML = `<div class="ep-ns">${ns}/</div>`;
    for (const e of groups[ns]) {
      const div = document.createElement("div");
      div.className = "ep" + (state.selected?.path === e.path ? " active" : "");
      div.innerHTML = e.path.slice(ns.length + 1) +
        (e.wrap ? '<span class="wrap-flag" title="wrapped in AuthenticatedMessage">&#128274;</span>' : "");
      div.addEventListener("click", () => selectEndpoint(e));
      g.appendChild(div);
    }
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
    el = document.createElement("input");
    el.placeholder = f.type;
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
    const inp = document.createElement("input");
    inp.placeholder = f.type;
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
  });
  if (res.error) { renderError($("stages"), res); return; }
  state.lastBuild = res;
  renderStages(res.stages);
  $("sendBtn").disabled = false;
}

async function send() {
  const e = state.selected;
  if (!state.lastBuild || !e) return;
  const target = $("target").value;
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

  const respHost = $("response");
  respHost.innerHTML = `<span class="muted">sending to ${url} ...</span>`;

  const res = await postJson(`${API}/send`, {
    url,
    formBody: state.lastBuild.finalFormBody,
    responseType: e.response,
  });
  renderResponse(res);
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

init().catch((e) => { document.body.insertAdjacentHTML("beforeend", `<pre class="error-box">init failed: ${e.message}</pre>`); });
