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
import { getAutoScroll } from "./redaction.js";

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

  const method = document.createElement("span");
  method.className = "fmethod";
  method.textContent = flow.method || "";

  const path = document.createElement("span");
  path.className = "fpath";
  path.textContent = flow.path || "";
  path.title = flow.path || "";

  if (flow.known) {
    const dot = document.createElement("span");
    dot.className = "known-dot";
    dot.title = "Known endpoint" + (flow.responseType ? " (" + flow.responseType + ")" : "");
    top.appendChild(dot);
  }

  const badge = document.createElement("span");
  badge.className = "status-badge " + statusClass(flow.status);
  badge.textContent = String(flow.status);

  const outcomeTag = buildOutcomeTag(flow.outcome, flow.diffAdded, flow.diffRemoved);

  if (outcomeTag) top.append(time, method, path, outcomeTag, badge);
  else top.append(time, method, path, badge);

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

// Insert a flow, newest at the bottom. Existing ids are ignored (snapshot replay).
/** @param {DashboardFlow} flow */
export function addFlow(flow, { allowAutoScroll = true } = {}) {
  if (flows.has(flow.id)) return;
  // Stick to the bottom only when the user opted in (setting) AND is already near the bottom.
  const stick = allowAutoScroll && getAutoScroll() && isAtBottom();
  const row = buildRow(flow);
  flowList.appendChild(row);
  flows.set(flow.id, { flow, row });
  updateCount();
  if (getSelectedId() === flow.id) row.classList.add("active");
  if (stick) flowList.scrollTop = flowList.scrollHeight;
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
