// The detail pane: header, type lines, URL/params section, request/response JSON tree viewers,
// the known-endpoint card, and the save-as-endpoint action.
//
/** @typedef {import('./types.d.ts').DashboardFlow} DashboardFlow */
/** @typedef {import('./types.d.ts').DashboardHeader} DashboardHeader */

import { detail } from "./dom.js";
import { EID_RE } from "./state.js";
import { statusClass, outcomeMeta, hasDiffCounts, appendDiffCounts } from "./helpers.js";
import { buildTreeViewer } from "./tree.js";
import {
  pickJson, redactParamValue, renderRedactedPath,
  getShowHeaders, isBlurMode, showRawHeaders, getDefaultFormat,
} from "./redaction.js";
import { postJson } from "./api.js";
import { icon } from "./icons.js";
import { JSON_FORMATS, BYTE_FORMATS, FORMAT_LABELS, jsonToText, bytesToText } from "./format.js";

// Remembered format per section label (Request / Response), so switching flows keeps your choice.
const formatChoice = new Map();

// True when a parsed JSON value is an empty object/array (no keys/items) - we skip the explorer
// for these since "{...} 0 keys" is noise.
function isEmptyContainer(v) {
  if (v === null || typeof v !== "object") return false;
  return (Array.isArray(v) ? v.length : Object.keys(v).length) === 0;
}

// Copy `text` to the clipboard, flashing the button label.
function wireCopy(btn, getText) {
  btn.addEventListener("click", () => {
    navigator.clipboard.writeText(getText()).then(
      () => {
        const prev = btn.dataset.label || "Copy";
        btn.replaceChildren(icon("check", "icon-sm"), document.createTextNode(" Copied"));
        setTimeout(() => btn.replaceChildren(icon("copy", "icon-sm"), document.createTextNode(" " + prev)), 1200);
      },
      () => console.warn("clipboard write failed"),
    );
  });
}

// Render one request/response data section with a format selector (JSON tree/raw, YAML, XML, JS,
// Hex, Bin), an always-present copy button, and a filter box. `emptyNote` shows when there is no
// decoded JSON. `rawB64` powers the Hex/Bin views (and is available even when JSON failed).
function buildDataSection(label, jsonStr, rawB64, emptyNote = "(no decoded JSON)") {
  const wrap = document.createElement("div");
  const head = document.createElement("div");
  head.className = "section-head";
  const title = document.createElement("h3");
  title.textContent = label;
  head.appendChild(title);
  wrap.appendChild(head);

  // Body-less / empty cases: note only, no controls.
  let parsed = null;
  let parseOk = false;
  if (jsonStr) {
    try { parsed = JSON.parse(jsonStr); parseOk = true; } catch (e) { console.warn("JSON parse failed", e); }
  }
  if (!jsonStr && !rawB64) { wrap.appendChild(note(emptyNote)); return wrap; }
  if (jsonStr && parseOk && isEmptyContainer(parsed)) {
    wrap.appendChild(note("(empty - all default values)"));
    return wrap;
  }

  // Which formats are available: JSON-derived ones only when we have decodable JSON; byte ones
  // whenever we have the raw base64.
  const formats = [];
  if (jsonStr && parseOk) formats.push(...JSON_FORMATS);
  if (rawB64) formats.push(...BYTE_FORMATS);
  if (formats.length === 0) {
    // Have a body but could not decode it - show the raw base64 + the note.
    wrap.appendChild(note(emptyNote));
    const box = document.createElement("div");
    box.className = "raw-box";
    box.textContent = rawB64;
    wrap.appendChild(box);
    return wrap;
  }

  // Toolbar: format select + filter + copy, all the same height.
  const tools = document.createElement("div");
  tools.className = "data-tools";

  const select = document.createElement("select");
  select.className = "data-format";
  for (const f of formats) {
    const opt = document.createElement("option");
    opt.value = f;
    opt.textContent = FORMAT_LABELS[f] ?? f;
    select.appendChild(opt);
  }
  // Prefer the per-section remembered choice; fall back to the user's default-format setting; then
  // the first available format for this data.
  let current = formatChoice.get(label) ?? getDefaultFormat();
  if (!formats.includes(current)) current = formats[0];
  select.value = current;

  const filter = document.createElement("input");
  filter.type = "search";
  filter.className = "data-filter";
  filter.placeholder = "Filter...";

  const copy = document.createElement("button");
  copy.className = "btn-mini data-copy";
  copy.dataset.label = "Copy";
  copy.append(icon("copy", "icon-sm"), document.createTextNode(" Copy"));

  tools.append(select, filter, copy);
  head.appendChild(tools);

  const body = document.createElement("div");
  body.className = "data-body";
  wrap.appendChild(body);

  // Current text for the active text format (null when the tree view is active).
  const currentText = () => {
    const fmt = select.value;
    if (fmt === "json-tree") return jsonStr; // copy gives pretty JSON for the tree
    if (BYTE_FORMATS.includes(fmt)) return bytesToText(rawB64, fmt);
    return jsonToText(jsonStr, fmt) ?? jsonStr;
  };
  wireCopy(copy, currentText);

  const render = () => {
    const fmt = select.value;
    formatChoice.set(label, fmt);
    body.replaceChildren();
    if (fmt === "json-tree") {
      // The tree has its own search; the section filter is redundant there.
      filter.classList.add("hidden");
      body.appendChild(buildTreeViewer(parsed));
    } else {
      filter.classList.remove("hidden");
      const pre = document.createElement("pre");
      pre.className = "data-text";
      applyTextFilter(pre, currentText(), filter.value);
      body.appendChild(pre);
    }
  };

  select.addEventListener("change", render);
  let deb = null;
  filter.addEventListener("input", () => {
    if (deb) clearTimeout(deb);
    deb = setTimeout(() => {
      if (select.value === "json-tree") return;
      const pre = body.querySelector(".data-text");
      if (pre) applyTextFilter(pre, currentText(), filter.value);
    }, 120);
  });

  render();
  return wrap;
}

function note(text) {
  const n = document.createElement("div");
  n.className = "no-json";
  n.textContent = text;
  return n;
}

// Filter a text view to lines containing `needle` (case-insensitive); empty needle shows all.
function applyTextFilter(pre, text, needle) {
  const q = needle.trim().toLowerCase();
  if (!q) { pre.textContent = text; return; }
  const kept = text.split("\n").filter((l) => l.toLowerCase().includes(q));
  pre.textContent = kept.length ? kept.join("\n") : "(no lines match)";
}

// Split a flow URL into { path, query, pathParams }. `path` is everything after the host; `query`
// is an array of [k, v]; `pathParams` are trailing/extra path segments beyond the known endpoint
// path (e.g. a trailing EID).
function parseFlowUrl(flow) {
  const raw = flow.url || "";
  if (!raw) return null;
  let pathAndQuery = raw;
  // Strip scheme+host if present, keeping the leading slash of the path.
  const schemeIdx = raw.indexOf("://");
  if (schemeIdx >= 0) {
    const rest = raw.slice(schemeIdx + 3);
    const slash = rest.indexOf("/");
    pathAndQuery = slash >= 0 ? rest.slice(slash) : "/";
  }

  let path = pathAndQuery;
  const query = [];
  const qIdx = pathAndQuery.indexOf("?");
  if (qIdx >= 0) {
    path = pathAndQuery.slice(0, qIdx);
    const qs = pathAndQuery.slice(qIdx + 1);
    for (const pair of qs.split("&")) {
      if (!pair) continue;
      const eq = pair.indexOf("=");
      const k = eq >= 0 ? pair.slice(0, eq) : pair;
      const v = eq >= 0 ? pair.slice(eq + 1) : "";
      try {
        query.push([decodeURIComponent(k), decodeURIComponent(v)]);
      } catch {
        query.push([k, v]);
      }
    }
  }

  // Path params: any URL segments beyond the known endpoint path (flow.path). Falls back to
  // detecting a trailing EID segment when the endpoint is unknown.
  const pathParams = [];
  const segments = path.split("/").filter(Boolean);
  const known = (flow.path || "").split("/").filter(Boolean);
  let extra = [];
  if (known.length && segments.length > known.length) {
    extra = segments.slice(known.length);
  } else {
    const last = segments[segments.length - 1];
    if (last && EID_RE.test(last)) extra = [last];
  }
  for (const seg of extra) pathParams.push(seg);

  return { path, query, pathParams };
}

// "URL / Params" section: the request path plus any path or query params, with EID-bearing values
// blurred/redacted per the current mode. Returns null when there is nothing useful to show.
function buildParamsSection(flow) {
  const parsed = parseFlowUrl(flow);
  if (!parsed) return null;
  const { path, query, pathParams } = parsed;

  // When the request path is exactly the endpoint (no extra path segments, no query string), the
  // section is redundant - the endpoint already shows in the detail header. Skip rendering entirely.
  if (pathParams.length === 0 && query.length === 0) return null;

  const wrap = document.createElement("div");
  const head = document.createElement("div");
  head.className = "section-head";
  const title = document.createElement("h3");
  title.textContent = "URL";
  head.appendChild(title);
  wrap.appendChild(head);

  const box = document.createElement("div");
  box.className = "params-box";

  const addRow = (label, valueSetup) => {
    const row = document.createElement("div");
    row.className = "param-row";
    const l = document.createElement("span");
    l.className = "param-label";
    l.textContent = label;
    const v = document.createElement("span");
    v.className = "param-val";
    valueSetup(v);
    row.append(l, v);
    box.appendChild(row);
  };

  addRow("path", (v) => renderRedactedPath(path, v));
  for (const p of pathParams) addRow("path param", (v) => redactParamValue(p, v));
  for (const [k, val] of query) addRow(k, (v) => redactParamValue(val, v));

  wrap.appendChild(box);
  return wrap;
}

async function saveEndpoint(id, resultEl) {
  resultEl.className = "save-result muted";
  resultEl.textContent = "Saving...";
  const { ok, data } = await postJson("/api/capture/save-endpoint", { id });
  if (ok && data.saved) {
    resultEl.className = "save-result ok";
    resultEl.textContent = "Saved " + data.saved;
  } else {
    resultEl.className = "save-result err";
    resultEl.textContent = data.error || "Save failed";
  }
}

// Info card shown in place of the Save button for flows we already understand. Lists both request
// and response types plus the endpoint-write outcome.
function buildKnownCard(flow) {
  const card = document.createElement("div");
  card.className = "known-card";

  const title = document.createElement("div");
  title.className = "known-card-title";
  title.textContent = "Known endpoint";
  card.appendChild(title);

  const addRow = (label, value, unknown) => {
    const line = document.createElement("div");
    line.className = "known-card-line";
    const l = document.createElement("span");
    l.className = "known-card-label";
    l.textContent = label;
    const v = document.createElement("span");
    v.className = "known-card-val" + (unknown ? " unknown" : "");
    v.textContent = value;
    line.append(l, v);
    card.appendChild(line);
  };

  addRow("Request type", flow.requestType || "none", !flow.requestType);
  addRow("Response type", flow.responseType || "none", !flow.responseType);

  const meta = outcomeMeta(flow.outcome);
  if (meta) {
    const line = document.createElement("div");
    line.className = "known-card-line";
    const l = document.createElement("span");
    l.className = "known-card-label";
    l.textContent = "Endpoint";
    const v = document.createElement("span");
    v.className = "known-card-val outcome-text outcome-" + meta.kind;
    if (hasDiffCounts(flow.outcome, flow.diffAdded, flow.diffRemoved)) {
      // e.g. "diff (+12 -3)" with the +/- colored green/red.
      v.append(meta.label + " (");
      appendDiffCounts(v, flow.diffAdded, flow.diffRemoved);
      v.append(")");
    } else {
      v.textContent = meta.label;
    }
    line.append(l, v);
    card.appendChild(line);
  }

  return card;
}

// Pick which header list to show (raw only in Off mode) and serialize it to a copyable string.
function headersForDisplay(redacted, raw) {
  return showRawHeaders() ? (raw ?? redacted ?? []) : (redacted ?? []);
}

function headersToText(headers) {
  return headers.map((h) => `${h.name}: ${h.value}`).join("\n");
}

// A headers section: a small table of name/value rows + a "Copy headers" button. Sensitive values
// blur in blur mode (the server already redacted them in the redacted copy). Returns null when
// there are no headers for this side.
function buildHeadersSection(label, redacted, raw) {
  const headers = headersForDisplay(redacted, raw);
  if (!headers.length) return null;

  const wrap = document.createElement("div");
  const head = document.createElement("div");
  head.className = "section-head";
  const title = document.createElement("h3");
  title.textContent = label;
  head.appendChild(title);

  const copy = document.createElement("button");
  copy.className = "btn-mini";
  copy.textContent = "Copy headers";
  copy.title = "Copy the " + label;
  copy.addEventListener("click", () => {
    navigator.clipboard.writeText(headersToText(headers)).then(
      () => { copy.textContent = "Copied"; setTimeout(() => (copy.textContent = "Copy headers"), 1200); },
      () => console.warn("clipboard write failed"),
    );
  });
  head.appendChild(copy);
  wrap.appendChild(head);

  const box = document.createElement("div");
  box.className = "params-box";
  for (const h of headers) {
    const row = document.createElement("div");
    row.className = "param-row";
    const l = document.createElement("span");
    l.className = "param-label";
    l.textContent = h.name;
    const v = document.createElement("span");
    v.className = "param-val";
    v.textContent = h.value;
    // In blur mode, blur the value of a sensitive header (reveal on click), matching body blur.
    if (isBlurMode() && h.sensitive) makeBlurred(v);
    row.append(l, v);
    box.appendChild(row);
  }
  wrap.appendChild(box);
  return wrap;
}

/** @param {DashboardFlow | null} flow */
export function renderDetail(flow) {
  detail.innerHTML = "";
  if (!flow) {
    detail.className = "muted";
    detail.textContent = "Select a request to inspect it.";
    return;
  }
  detail.className = "";

  const head = document.createElement("div");
  head.className = "detail-head";
  const m = document.createElement("span");
  m.textContent = flow.method + " ";
  const p = document.createElement("span");
  p.className = "dpath";
  p.textContent = flow.path;
  const s = document.createElement("span");
  s.className = "dstatus status-badge " + statusClass(flow.status);
  s.textContent = String(flow.status);
  head.append(m, p, s);
  detail.appendChild(head);

  // For known flows the types live in the info card below, so we skip the header type lines to
  // avoid duplicating the same info twice.
  if (!flow.known) {
    const typeInfo = document.createElement("div");
    typeInfo.className = "detail-types";

    const reqLine = document.createElement("div");
    reqLine.className = "detail-type-line";
    const reqLabel = document.createElement("span");
    reqLabel.className = "detail-type-label";
    reqLabel.textContent = "Request type: ";
    const reqVal = document.createElement("span");
    reqVal.className = "detail-type-val" + (flow.requestType ? "" : " unknown");
    reqVal.textContent = flow.requestType || "unknown";
    reqLine.append(reqLabel, reqVal);

    const respLine = document.createElement("div");
    respLine.className = "detail-type-line";
    const respLabel = document.createElement("span");
    respLabel.className = "detail-type-label";
    respLabel.textContent = "Response type: ";
    const respVal = document.createElement("span");
    respVal.className = "detail-type-val" + (flow.responseType ? "" : " unknown");
    respVal.textContent = flow.responseType || "unknown";
    respLine.append(respLabel, respVal);

    typeInfo.append(reqLine, respLine);
    detail.appendChild(typeInfo);
  }

  const paramsSection = buildParamsSection(flow);
  if (paramsSection) detail.appendChild(paramsSection);

  // A null requestDataB64 means the endpoint posts no request proto at all (body-less endpoint),
  // which is expected - distinguish that from a decode failure on a body that WAS sent.
  const reqEmptyNote = flow.requestDataB64 ? "(no decoded JSON)" : "(no request body)";
  detail.appendChild(buildDataSection("Request", pickJson(flow.requestJson, flow.requestJsonRaw), flow.requestDataB64, reqEmptyNote));
  detail.appendChild(buildDataSection("Response", pickJson(flow.responseJson, flow.responseJsonRaw), flow.responseB64));

  // Headers, behind the default-off "Show headers" option. Guarded so a header-rendering glitch
  // can never abort renderDetail before the save bar below is appended.
  if (getShowHeaders()) {
    try {
      const reqH = buildHeadersSection("Request headers", flow.requestHeaders, flow.requestHeadersRaw);
      if (reqH) detail.appendChild(reqH);
      const respH = buildHeadersSection("Response headers", flow.responseHeaders, flow.responseHeadersRaw);
      if (respH) detail.appendChild(respH);
    } catch (e) {
      console.warn("header section render failed", e);
    }
  }

  const bar = document.createElement("div");
  bar.className = "save-bar";
  if (flow.known) {
    bar.appendChild(buildKnownCard(flow));
  } else {
    const saveBtn = document.createElement("button");
    saveBtn.id = "saveBtn";
    saveBtn.textContent = "Save as endpoint";
    const result = document.createElement("span");
    result.className = "save-result";
    saveBtn.addEventListener("click", () => saveEndpoint(flow.id, result));
    bar.append(saveBtn, result);
  }
  detail.appendChild(bar);
}
