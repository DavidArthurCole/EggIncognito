// The live flow list: row construction, the endpoint-outcome pill, insert/clear, and selection.
//
/** @typedef {import('./types.d.ts').DashboardFlow} DashboardFlow */

import { flowList } from "./dom.js";
import {
  flows, getSelectedId, setSelectedId,
} from "./state.js";
import {
  statusClass, isAtBottom, outcomeMeta, hasDiffCounts, appendDiffCounts,
} from "./helpers.js";
import { updateCount } from "./stats.js";
import { renderDetail } from "./detail.js";
import { getAutoScroll, getCompareToKnown } from "./redaction.js";

// Tiny color-coded pill for the endpoint-write outcome. Null when no outcome. For "diff" outcomes
// with line changes the pill becomes compact git-style counts (+N green / -N red) inside the amber
// container.
function buildOutcomeTag(outcome, diffAdded, diffRemoved) {
  const meta = outcomeMeta(outcome);
  if (!meta) return null;
  const tag = document.createElement("span");
  tag.className = "outcome-tag outcome-" + meta.kind;
  if (hasDiffCounts(outcome, diffAdded, diffRemoved)) {
    tag.classList.add("outcome-diff-counts");
    appendDiffCounts(tag, diffAdded, diffRemoved);
    const a = Number(diffAdded) || 0;
    const r = Number(diffRemoved) || 0;
    tag.title = "endpoint: diff (+" + a + " -" + r + ")";
  } else {
    tag.textContent = meta.label;
    tag.title = "endpoint: " + meta.label;
  }
  return tag;
}

/** @param {DashboardFlow} flow */
function buildRow(flow) {
  const row = document.createElement("div");
  row.className = "flow-row";
  row.dataset.id = String(flow.id);

  const top = document.createElement("div");
  top.className = "frow-top";

  const time = document.createElement("span");
  time.className = "ftime";
  time.textContent = flow.timestamp || "";

  const path = document.createElement("span");
  path.className = "fpath";
  path.textContent = flow.path || "";
  path.title = flow.path || "";

  if (flow.known || flow.saved) {
    const dot = document.createElement("span");
    dot.className = "known-dot";
    let dotTitle = "Saved as endpoint";
    if (flow.known) dotTitle = "Known endpoint" + (flow.responseType ? " (" + flow.responseType + ")" : "");
    dot.title = dotTitle;
    top.appendChild(dot);
  }

  const badge = document.createElement("span");
  badge.className = "status-badge " + statusClass(flow.status);
  badge.textContent = String(flow.status);

  // Outcome tag only when "Compare to known data" is enabled (off by default).
  const outcomeTag = getCompareToKnown()
    ? buildOutcomeTag(flow.outcome, flow.diffAdded, flow.diffRemoved) : null;

  if (outcomeTag) top.append(time, path, outcomeTag, badge);
  else top.append(time, path, badge);

  const types = document.createElement("div");
  types.className = "frow-types";

  const reqType = flow.requestType || null;
  const respType = flow.responseType || null;
  // A null requestDataB64 means the endpoint sends no request proto at all - show "(none)", a
  // deliberate state, rather than "(unknown)", which reads as a decode failure.
  const noReqBody = !flow.requestDataB64;

  const reqChip = document.createElement("span");
  reqChip.className = "type-chip" + (reqType ? "" : " unknown");
  reqChip.textContent = reqType || (noReqBody ? "(none)" : "(unknown)");
  reqChip.title = reqType || (noReqBody ? "no request body" : "unknown request type");

  const arrow = document.createElement("span");
  arrow.className = "type-arrow";
  arrow.textContent = "›";

  const respChip = document.createElement("span");
  respChip.className = "type-chip" + (respType ? "" : " unknown");
  respChip.textContent = respType || "(unknown)";
  respChip.title = respType || "unknown response type";

  types.append(reqChip, arrow, respChip);

  row.append(top, types);
  row.addEventListener("click", () => selectFlow(flow.id));
  return row;
}

// Active row filters. group = exact API namespace prefix; path/type = case-insensitive substrings.
const filters = { group: "", path: "", type: "" };

// True if a flow passes all active filters.
/** @param {DashboardFlow} flow */
function flowMatchesFilters(flow) {
  const path = (flow.path || "").toLowerCase();
  if (filters.group && (flow.path || "").split("/")[0] !== filters.group) return false;
  if (filters.path && !path.includes(filters.path)) return false;
  if (filters.type) {
    const t = (flow.requestType + " " + flow.responseType).toLowerCase();
    if (!t.includes(filters.type)) return false;
  }
  return true;
}

// Update the active filters and show/hide every row to match.
export function setFilters({ group, path, type }) {
  if (group !== undefined) filters.group = group;
  if (path !== undefined) filters.path = path.trim().toLowerCase();
  if (type !== undefined) filters.type = type.trim().toLowerCase();
  for (const { flow, row } of flows.values()) {
    row.classList.toggle("filtered-out", !flowMatchesFilters(flow));
  }
}

// The set of request/response type names seen this session, mirrored into the #seenTypes datalist
// so the type filter is a searchable bound list, not blind free-text.
const seenTypes = new Set();
function noteTypes(flow) {
  const dl = document.getElementById("seenTypes");
  if (!dl) return;
  let added = false;
  for (const t of [flow.requestType, flow.responseType]) {
    if (t && !seenTypes.has(t)) { seenTypes.add(t); added = true; }
  }
  if (!added) return;
  dl.replaceChildren();
  for (const t of [...seenTypes].sort((a, b) => a.localeCompare(b))) {
    const opt = document.createElement("option");
    opt.value = t;
    dl.appendChild(opt);
  }
}

// Insert a flow, newest at the bottom. An already-known id is treated as an UPDATE (the server
// re-broadcasts a flow when its state changes, e.g. after save-as-endpoint sets Saved=true).
/** @param {DashboardFlow} flow */
export function addFlow(flow, { allowAutoScroll = true } = {}) {
  if (flows.has(flow.id)) { updateFlow(flow); return; }
  noteTypes(flow);
  // Stick to the bottom only when the user opted in (setting) AND is already near the bottom.
  const stick = allowAutoScroll && getAutoScroll() && isAtBottom();
  const row = buildRow(flow);
  if (!flowMatchesFilters(flow)) row.classList.add("filtered-out"); // respect active filters
  flowList.appendChild(row);
  flows.set(flow.id, { flow, row });
  updateCount();
  if (getSelectedId() === flow.id) row.classList.add("active");
  if (stick) flowList.scrollTop = flowList.scrollHeight;
}

// Update an already-buffered flow in place (server re-broadcast). Swaps the stored flow + its row,
// and re-renders the detail pane if this flow is the selected one.
/** @param {DashboardFlow} flow */
function updateFlow(flow) {
  const entry = flows.get(flow.id);
  if (!entry) return;
  entry.flow = flow;
  const fresh = buildRow(flow);
  if (!flowMatchesFilters(flow)) fresh.classList.add("filtered-out");
  if (getSelectedId() === flow.id) { fresh.classList.add("active"); renderDetail(flow); }
  entry.row.replaceWith(fresh);
  entry.row = fresh;
}

// Rebuild every row in place (e.g. when the compare-to-known setting toggles, which changes whether
// the outcome tag is shown). Preserves order, selection, and filter state.
export function rebuildFlowRows() {
  const selected = getSelectedId();
  for (const entry of flows.values()) {
    const fresh = buildRow(entry.flow);
    if (!flowMatchesFilters(entry.flow)) fresh.classList.add("filtered-out");
    if (entry.flow.id === selected) fresh.classList.add("active");
    entry.row.replaceWith(fresh);
    entry.row = fresh;
  }
}

export function clearFlows() {
  for (const { row } of flows.values()) row.remove();
  flows.clear();
  updateCount();
}

export function selectFlow(id) {
  const prevId = getSelectedId();
  if (prevId !== null) {
    const prev = flows.get(prevId);
    if (prev) prev.row.classList.remove("active");
  }
  setSelectedId(id);
  const entry = flows.get(id);
  if (entry) entry.row.classList.add("active");
  renderDetail(entry ? entry.flow : null);
}
