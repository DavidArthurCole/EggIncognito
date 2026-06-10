// The detail pane: header, type lines, URL/params section, request/response JSON tree viewers,
// the known-endpoint card, and the save-as-endpoint action.
//
/** @typedef {import('./types.d.ts').DashboardFlow} DashboardFlow */
/** @typedef {import('./types.d.ts').DashboardHeader} DashboardHeader */

import { detail } from "./dom.js";
import { EID_RE } from "./state.js";
import { statusClass, outcomeMeta, hasDiffCounts, appendDiffCounts } from "./helpers.js";
import { buildTreeViewer, makeBlurred } from "./tree.js";
import {
  pickJson, redactParamValue, renderRedactedPath,
  getShowHeaders, isBlurMode, showRawHeaders, getDefaultFormat,
  collectSensitiveValues, getCompareToKnown,
} from "./redaction.js";
import { postJson } from "./api.js";
import { icon } from "/icons.js";
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

  // Format selector + Copy button. These live INSIDE the box's control row (see render): for the
  // tree view they are injected into the tree's own Expand/Collapse/search row; for text views they
  // sit in a control row at the top of the body alongside the text filter. Keeping every control in
  // one in-box row means switching formats never shifts a control's position.
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

  const copy = document.createElement("button");
  copy.className = "btn-mini data-copy";
  copy.dataset.label = "Copy";
  copy.append(icon("copy", "icon-sm"), document.createTextNode(" Copy"));

  const body = document.createElement("div");
  body.className = "data-body";
  wrap.appendChild(body);

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
      // The tree viewer renders its own control row (Expand all / Collapse all / search). Inject the
      // format select + Copy at the front of that row so all controls share one in-box toolbar.
      const viewer = buildTreeViewer(parsed);
      const treeTools = viewer.querySelector(".jtree-tools");
      if (treeTools) treeTools.prepend(select, copy);
      body.appendChild(viewer);
    } else {
      // Text views: same boxed layout as the tree - a bordered .data-box holding a control row
      // (select + filter + copy) above the text, so the controls stay contained, not loose.
      const boxEl = document.createElement("div");
      boxEl.className = "data-box";

      const tools = document.createElement("div");
      tools.className = "data-tools";
      const filter = document.createElement("input");
      filter.type = "search";
      filter.className = "data-filter";
      filter.placeholder = "Filter...";
      tools.append(select, filter, copy);
      boxEl.appendChild(tools);

      // In blur mode, blur every occurrence of a sensitive value in the serialized text (the same
      // values the tree blurs). Byte views (hex/bin) have no field structure, so nothing to blur.
      const isByte = BYTE_FORMATS.includes(select.value);
      const sensitive = (isBlurMode() && parseOk && !isByte)
        ? collectSensitiveValues(parsed) : null;

      const pre = document.createElement("pre");
      pre.className = "data-text";
      renderText(pre, currentText(), filter.value, sensitive);
      boxEl.appendChild(pre);
      body.appendChild(boxEl);

      let deb = null;
      filter.addEventListener("input", () => {
        if (deb) clearTimeout(deb);
        deb = setTimeout(() => renderText(pre, currentText(), filter.value, sensitive), 120);
      });
    }
  };

  select.addEventListener("change", render);
  render();
  return wrap;
}

function note(text) {
  const n = document.createElement("div");
  n.className = "no-json";
  n.textContent = text;
  return n;
}

// Response section for a fire-and-forget endpoint with no captured body.
function buildAckSection() {
  const wrap = document.createElement("div");
  const head = document.createElement("div");
  head.className = "section-head";
  const title = document.createElement("h3");
  title.textContent = "Response";
  head.appendChild(title);
  wrap.appendChild(head);
  wrap.appendChild(note("Acknowledgement - this endpoint returns a short non-protobuf ack."));
  return wrap;
}

// Response section for a plain-text body: show the literal text the API returned (e.g. "SUCCESS").
function buildTextResponseSection(text, isAck) {
  const wrap = document.createElement("div");
  const head = document.createElement("div");
  head.className = "section-head";
  const title = document.createElement("h3");
  title.textContent = "Response";
  head.appendChild(title);
  if (isAck) {
    const tag = document.createElement("span");
    tag.className = "ack-tag";
    tag.textContent = "acknowledgement";
    head.appendChild(tag);
  }
  wrap.appendChild(head);
  const pre = document.createElement("pre");
  pre.className = "data-text";
  pre.textContent = text;
  wrap.appendChild(pre);
  return wrap;
}

// Render a text view: filter to lines containing `needle` (case-insensitive; empty shows all), and
// when `sensitive` is provided (blur mode) wrap every occurrence of a sensitive value in a blur
// span so XML/JSON/YAML/JS views honor blur the same way the tree does.
function renderText(pre, text, needle, sensitive) {
  const q = needle.trim().toLowerCase();
  let shown = text;
  if (q) {
    const kept = text.split("\n").filter((l) => l.toLowerCase().includes(q));
    shown = kept.length ? kept.join("\n") : "(no lines match)";
  }

  if (!sensitive || sensitive.size === 0) { pre.textContent = shown; return; }

  pre.replaceChildren();
  // Longest values first so a longer match is not pre-empted by a shorter substring of it.
  const needles = [...sensitive].filter(Boolean).sort((a, b) => b.length - a.length);
  let i = 0;
  while (i < shown.length) {
    let matched = null;
    for (const n of needles) {
      if (shown.startsWith(n, i)) { matched = n; break; }
    }
    if (matched) {
      pre.appendChild(makeBlurred(Object.assign(document.createElement("span"), { textContent: matched })));
      i += matched.length;
    } else {
      // Accumulate a run of plain text up to the next match start.
      let j = i + 1;
      while (j < shown.length && !needles.some((n) => shown.startsWith(n, j))) j++;
      pre.appendChild(document.createTextNode(shown.slice(i, j)));
      i = j;
    }
  }
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

  // The endpoint-comparison outcome (wrote/upd/same/diff/loss) only when the user opted in.
  const meta = getCompareToKnown() ? outcomeMeta(flow.outcome) : null;
  if (meta) {
    const line = document.createElement("div");
    line.className = "known-card-line";
    const l = document.createElement("span");
    l.className = "known-card-label";
    l.textContent = "Comparison";
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

    // Explain what the outcome means, since the bare word is opaque.
    const explain = document.createElement("div");
    explain.className = "known-card-explain muted";
    explain.textContent = meta.desc;
    card.appendChild(explain);
  }

  return card;
}

// Pick which header list to show, mirroring the body model:
//   Off    - raw values, shown plainly
//   Blur   - raw values, sensitive ones blurred + revealable on click (so the real value IS there)
//   Redact - the tokenized copy (sensitive values are the literal "redacted")
function headersForDisplay(redacted, raw) {
  if (isBlurMode() || showRawHeaders()) return raw ?? redacted ?? [];
  return redacted ?? [];
}

function headersToText(headers) {
  return headers.map((h) => `${h.name}: ${h.value}`).join("\n");
}

// A headers section: a small table of name/value rows + a "Copy headers" button. In blur mode the
// real sensitive value is shown blurred + click-to-reveal (matching body blur). Returns null when
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

  // Header: path + status. The method is always POST for this API, so it is not shown.
  const head = document.createElement("div");
  head.className = "detail-head";
  const p = document.createElement("span");
  p.className = "dpath";
  p.textContent = flow.path;
  const s = document.createElement("span");
  s.className = "dstatus status-badge " + statusClass(flow.status);
  s.textContent = String(flow.status);
  head.append(p, s);
  detail.appendChild(head);

  // Endpoint summary up top (right after the URL/status): the known-endpoint card for flows we
  // understand, or the save-as-endpoint bar + type lines for ones we do not.
  if (flow.known) {
    detail.appendChild(buildKnownCard(flow));
  } else {
    const typeInfo = document.createElement("div");
    typeInfo.className = "detail-types";
    typeInfo.append(
      typeLine("Request type", flow.requestType),
      typeLine("Response type", flow.responseType),
    );
    detail.appendChild(typeInfo);

    const bar = document.createElement("div");
    bar.className = "save-bar";
    if (flow.saved) {
      // Already saved this session - don't re-prompt; offer a re-save instead.
      const note = document.createElement("span");
      note.className = "save-result ok";
      note.textContent = "Saved as endpoint";
      const resave = document.createElement("button");
      resave.className = "btn-mini";
      resave.textContent = "Save again";
      const result = document.createElement("span");
      result.className = "save-result";
      resave.addEventListener("click", () => saveEndpoint(flow.id, result));
      bar.append(note, resave, result);
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

  // Request and Response each get their own tab: the body (+ URL params for the request) and, when
  // "Show headers" is on, that side's headers right below the body.
  const reqPane = document.createElement("div");
  const paramsSection = buildParamsSection(flow);
  if (paramsSection) reqPane.appendChild(paramsSection);
  const reqEmptyNote = flow.requestDataB64 ? "(no decoded JSON)" : "(no request body)";
  reqPane.appendChild(buildDataSection("Request", pickJson(flow.requestJson, flow.requestJsonRaw), flow.requestDataB64, reqEmptyNote));
  appendHeaders(reqPane, "Request headers", flow.requestHeaders, flow.requestHeadersRaw);

  const respPane = document.createElement("div");
  if (flow.responseText != null) {
    respPane.appendChild(buildTextResponseSection(flow.responseText, flow.responseIsAck));
  } else if (flow.responseIsAck) {
    respPane.appendChild(buildAckSection());
  } else {
    respPane.appendChild(buildDataSection("Response", pickJson(flow.responseJson, flow.responseJsonRaw), flow.responseB64));
  }
  appendHeaders(respPane, "Response headers", flow.responseHeaders, flow.responseHeadersRaw);

  detail.appendChild(buildTabs([
    { label: "Request", pane: reqPane },
    { label: "Response", pane: respPane },
  ]));
}

// A two-tab switcher (Request / Response). Remembers the last-picked tab across flow selections.
let lastDetailTab = 0;
function buildTabs(tabs) {
  const wrap = document.createElement("div");
  wrap.className = "detail-tabs";

  const bar = document.createElement("div");
  bar.className = "tab-bar";
  const body = document.createElement("div");
  body.className = "tab-body";

  const btns = [];
  const select = (i) => {
    lastDetailTab = i;
    btns.forEach((b, j) => b.classList.toggle("active", j === i));
    body.replaceChildren(tabs[i].pane);
  };
  tabs.forEach((t, i) => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = "tab-btn";
    b.textContent = t.label;
    b.addEventListener("click", () => select(i));
    btns.push(b);
    bar.appendChild(b);
  });

  wrap.append(bar, body);
  select(Math.min(lastDetailTab, tabs.length - 1));
  return wrap;
}

// Append a side's headers below its body, only when "Show headers" is enabled. Guarded so a header
// glitch never aborts the render.
function appendHeaders(pane, label, redacted, raw) {
  if (!getShowHeaders()) return;
  try {
    const sec = buildHeadersSection(label, redacted, raw);
    if (sec) pane.appendChild(sec);
  } catch (e) {
    console.warn("header section render failed", e);
  }
}

// One "Request type: X" / "Response type: X" line for the unknown-flow summary.
function typeLine(label, value) {
  const line = document.createElement("div");
  line.className = "detail-type-line";
  const l = document.createElement("span");
  l.className = "detail-type-label";
  l.textContent = label + ": ";
  const v = document.createElement("span");
  v.className = "detail-type-val" + (value ? "" : " unknown");
  v.textContent = value || "unknown";
  line.append(l, v);
  return line;
}
