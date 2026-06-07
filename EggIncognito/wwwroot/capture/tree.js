// Collapsible, searchable JSON tree viewer. Children render lazily on first expand so large
// responses (get_config has thousands of nodes) stay responsive. Self-contained except for
// isSensitiveKey (redaction.js) - resolved at call time, so the redaction <-> tree import cycle
// is harmless.

import { isSensitiveKey } from "./redaction.js";

// Levels at and below this depth start expanded; deeper levels start collapsed.
const TREE_DEFAULT_DEPTH = 1;
// Containers with fewer than this many children start expanded regardless of depth, so tiny
// objects/arrays don't hide a single key behind a click.
const TREE_SMALL_CHILD_LIMIT = 5;
const SEARCH_DEBOUNCE_MS = 150;
// Long leaf strings are clamped; click (or title tooltip) reveals the rest.
const VALUE_CLAMP = 200;

function valueType(v) {
  if (v === null) return "null";
  if (Array.isArray(v)) return "array";
  return typeof v; // "string" | "number" | "boolean" | "object"
}

function isContainer(v) {
  return v !== null && typeof v === "object";
}

// Number of direct children of a container value (array items or object keys).
function childCount(v) {
  return Array.isArray(v) ? v.length : Object.keys(v).length;
}

// Initial expand state for a container at `depth`: expand when it is within the default depth OR
// it is small (fewer than the small-child limit), so tiny nested objects/arrays open automatically
// while large ones stay collapsed.
function shouldDefaultExpand(value, depth) {
  return depth <= TREE_DEFAULT_DEPTH || childCount(value) < TREE_SMALL_CHILD_LIMIT;
}

function containerSummary(v) {
  if (Array.isArray(v)) {
    const n = v.length;
    return "[...] " + n + " " + (n === 1 ? "item" : "items");
  }
  const n = Object.keys(v).length;
  return "{...} " + n + " " + (n === 1 ? "key" : "keys");
}

// Wrap a leaf value span so it blurs (CSS) and reveals on click. Used in "blur" mode for values
// whose key is sensitive, and for EID-bearing path/query params (redaction.js).
export function makeBlurred(span) {
  span.classList.add("blurred");
  span.title = "Click to reveal";
  span.addEventListener("click", (e) => {
    e.stopPropagation();
    span.classList.toggle("revealed");
  });
  return span;
}

// Render a primitive leaf value into a colored span. Strings are quoted.
// `sensitive` marks the value for blurring (only honored in "blur" mode).
function renderLeafValue(v, sensitive) {
  const span = document.createElement("span");
  const t = valueType(v);
  span.className = "jv jv-" + t;
  if (t === "string") {
    const full = '"' + v + '"';
    if (v.length > VALUE_CLAMP) {
      span.textContent = '"' + v.slice(0, VALUE_CLAMP) + '..."';
      span.title = "Click to expand";
      span.classList.add("jv-clamped");
      span.dataset.full = full;
      span.dataset.clamped = full.slice(0, VALUE_CLAMP + 4);
      span.addEventListener("click", (e) => {
        e.stopPropagation();
        const expanded = span.classList.toggle("jv-open");
        span.textContent = expanded ? span.dataset.full : span.dataset.clamped;
        span.title = expanded ? "Click to collapse" : "Click to expand";
      });
    } else {
      span.textContent = full;
    }
  } else {
    span.textContent = String(v);
  }
  if (sensitive) return makeBlurred(span);
  return span;
}

// Build one tree node. `keyText` is the object key or array index label (or null for the root).
// `depth` controls default expansion. `keyName` is the JSON field name governing sensitivity
// (array items inherit their parent's). Container children are not built until first expanded.
function buildTreeNode(keyText, value, depth, keyName) {
  const node = document.createElement("div");
  node.className = "jtree-node";

  const row = document.createElement("div");
  row.className = "jtree-row";
  node.appendChild(row);

  const container = isContainer(value);

  const caret = document.createElement("span");
  caret.className = "jtree-caret";
  if (!container) caret.classList.add("jtree-caret-leaf");
  row.appendChild(caret);

  if (keyText !== null) {
    const keyEl = document.createElement("span");
    keyEl.className = "jtree-key";
    keyEl.textContent = keyText;
    node._keyEl = keyEl;
    node._keyText = String(keyText);
    row.appendChild(keyEl);
    const colon = document.createElement("span");
    colon.className = "jtree-colon";
    colon.textContent = ": ";
    row.appendChild(colon);
  }

  if (container) {
    const summary = document.createElement("span");
    summary.className = "jtree-summary";
    summary.textContent = containerSummary(value);
    row.appendChild(summary);
    node._summary = summary;

    const childWrap = document.createElement("div");
    childWrap.className = "jtree-children";
    node.appendChild(childWrap);

    node._value = value;
    node._depth = depth;
    node._keyName = keyName;
    node._built = false;
    node._expanded = false;

    const setExpanded = (want) => {
      if (want && !node._built) {
        buildChildren(node, childWrap, value, depth);
        node._built = true;
      }
      node._expanded = want;
      node.classList.toggle("jtree-expanded", want);
    };
    node._setExpanded = setExpanded;

    row.addEventListener("click", () => setExpanded(!node._expanded));

    if (shouldDefaultExpand(value, depth)) setExpanded(true);
  } else {
    const valEl = renderLeafValue(value, isSensitiveKey(keyName));
    node._valEl = valEl;
    node._leafValue = value;
    row.appendChild(valEl);
  }

  return node;
}

function buildChildren(node, childWrap, value, depth) {
  const frag = document.createDocumentFragment();
  if (Array.isArray(value)) {
    // Array items have no key of their own; inherit the array's field name so e.g. a list under a
    // sensitive key still blurs.
    for (let i = 0; i < value.length; i++) {
      frag.appendChild(buildTreeNode(String(i), value[i], depth + 1, node._keyName));
    }
  } else {
    for (const k of Object.keys(value)) {
      frag.appendChild(buildTreeNode(k, value[k], depth + 1, k));
    }
  }
  childWrap.appendChild(frag);
}

// Recursively force every container node built and expanded/collapsed.
function setTreeExpansion(rootNode, expand) {
  const stack = [rootNode];
  while (stack.length) {
    const n = stack.pop();
    if (n._setExpanded) {
      n._setExpanded(expand);
      // After expanding, children exist; queue them. After collapsing we leave built children in
      // the DOM (hidden) so re-expand is instant.
      const childWrap = n.querySelector(":scope > .jtree-children");
      if (childWrap) {
        for (const c of childWrap.children) stack.push(c);
      }
    }
  }
}

function clearTreeHighlights(rootNode) {
  rootNode.classList.remove("jtree-searching");
  const hit = rootNode.querySelectorAll(".jtree-match, .jtree-dim, mark");
  for (const el of hit) {
    if (el.tagName === "MARK") {
      const text = el.textContent;
      el.replaceWith(document.createTextNode(text));
    }
  }
  rootNode.querySelectorAll(".jtree-match").forEach((e) => e.classList.remove("jtree-match"));
  rootNode.querySelectorAll(".jtree-dim").forEach((e) => e.classList.remove("jtree-dim"));
  // Normalize text nodes split by previous <mark> insertion.
  rootNode.querySelectorAll(".jtree-key, .jv").forEach((e) => e.normalize());
  // Re-clamp any long strings that search forced fully open.
  rootNode.querySelectorAll(".jv-clamped").forEach((e) => {
    if (!e.classList.contains("jv-open") && e.dataset.clamped) {
      e.textContent = e.dataset.clamped;
    }
  });
}

// Wrap matched substrings of `el`'s text in <mark>. Case-insensitive.
function highlightText(el, needle) {
  const text = el.textContent;
  const lower = text.toLowerCase();
  let idx = lower.indexOf(needle);
  if (idx < 0) return false;
  el.textContent = "";
  let pos = 0;
  while (idx >= 0) {
    if (idx > pos) el.appendChild(document.createTextNode(text.slice(pos, idx)));
    const mark = document.createElement("mark");
    mark.textContent = text.slice(idx, idx + needle.length);
    el.appendChild(mark);
    pos = idx + needle.length;
    idx = lower.indexOf(needle, pos);
  }
  if (pos < text.length) el.appendChild(document.createTextNode(text.slice(pos)));
  return true;
}

// Apply a search: builds the whole tree (so nothing is hidden by lazy load), then walks it
// bottom-up marking matches and auto-expanding ancestor chains. Non-matching branches are dimmed.
// Returns the match count.
function applySearch(rootNode, query) {
  clearTreeHighlights(rootNode);
  const needle = query.trim().toLowerCase();
  if (!needle) {
    // Restore default view: collapse to default depth.
    resetTreeToDefault(rootNode);
    return 0;
  }

  // Ensure the full tree exists so search can reach every node.
  forceBuildAll(rootNode);
  rootNode.classList.add("jtree-searching");

  let matches = 0;

  // Depth-first; returns true if the node or any descendant matched.
  const visit = (node) => {
    let selfMatch = false;
    if (node._keyEl && node._keyText.toLowerCase().includes(needle)) {
      highlightText(node._keyEl, needle);
      selfMatch = true;
    }
    if (node._valEl) {
      const raw = node._valEl.dataset.full || node._valEl.textContent;
      if (String(raw).toLowerCase().includes(needle)) {
        // Make sure clamped strings show full text while highlighting.
        if (node._valEl.dataset.full) {
          node._valEl.textContent = node._valEl.dataset.full;
        }
        highlightText(node._valEl, needle);
        selfMatch = true;
      }
    }

    let childMatch = false;
    const childWrap = node.querySelector(":scope > .jtree-children");
    if (childWrap) {
      for (const c of childWrap.children) {
        if (visit(c)) childMatch = true;
      }
    }

    const matched = selfMatch || childMatch;
    if (selfMatch) {
      matches++;
      node.classList.add("jtree-match");
    }
    if (childMatch && node._setExpanded) node._setExpanded(true);
    node.classList.toggle("jtree-dim", !matched);
    return matched;
  };

  const childWrap = rootNode.querySelector(":scope > .jtree-children");
  if (childWrap) {
    for (const c of childWrap.children) visit(c);
  } else {
    visit(rootNode);
  }
  // Keep the root visible.
  rootNode.classList.remove("jtree-dim");
  if (rootNode._setExpanded) rootNode._setExpanded(true);
  return matches;
}

function forceBuildAll(rootNode) {
  const stack = [rootNode];
  while (stack.length) {
    const n = stack.pop();
    if (n._value !== undefined && !n._built) {
      const childWrap = n.querySelector(":scope > .jtree-children");
      buildChildren(n, childWrap, n._value, n._depth);
      n._built = true;
    }
    const childWrap = n.querySelector(":scope > .jtree-children");
    if (childWrap) for (const c of childWrap.children) stack.push(c);
  }
}

function resetTreeToDefault(rootNode) {
  const stack = [{ node: rootNode, depth: rootNode._depth ?? 0 }];
  while (stack.length) {
    const { node, depth } = stack.pop();
    if (node._setExpanded) node._setExpanded(shouldDefaultExpand(node._value, depth));
    node.classList.remove("jtree-dim", "jtree-match");
    const childWrap = node.querySelector(":scope > .jtree-children");
    if (childWrap && node._built) {
      for (const c of childWrap.children) stack.push({ node: c, depth: (node._depth ?? 0) + 1 });
    }
  }
}

// Build the controls toolbar (expand/collapse/search) plus the tree itself.
export function buildTreeViewer(value) {
  const wrap = document.createElement("div");
  wrap.className = "jtree-viewer";

  const tools = document.createElement("div");
  tools.className = "jtree-tools";

  const expandBtn = document.createElement("button");
  expandBtn.className = "btn-mini";
  expandBtn.textContent = "Expand all";

  const collapseBtn = document.createElement("button");
  collapseBtn.className = "btn-mini";
  collapseBtn.textContent = "Collapse all";

  const search = document.createElement("input");
  search.type = "search";
  search.className = "jtree-search";
  search.placeholder = "Filter keys / values...";

  const count = document.createElement("span");
  count.className = "jtree-search-count";

  tools.append(expandBtn, collapseBtn, search, count);
  wrap.appendChild(tools);

  const treeRoot = buildTreeNode(null, value, 0);
  treeRoot.classList.add("jtree-root");
  wrap.appendChild(treeRoot);

  expandBtn.addEventListener("click", () => {
    if (search.value) { search.value = ""; count.textContent = ""; }
    forceBuildAll(treeRoot);
    setTreeExpansion(treeRoot, true);
    clearTreeHighlights(treeRoot);
  });
  collapseBtn.addEventListener("click", () => {
    if (search.value) { search.value = ""; count.textContent = ""; }
    clearTreeHighlights(treeRoot);
    setTreeExpansion(treeRoot, false);
    if (treeRoot._setExpanded) treeRoot._setExpanded(true); // keep root open
  });

  let debounce = null;
  search.addEventListener("input", () => {
    if (debounce) clearTimeout(debounce);
    debounce = setTimeout(() => {
      const q = search.value;
      const n = applySearch(treeRoot, q);
      if (q.trim()) {
        count.textContent = n + (n === 1 ? " match" : " matches");
      } else {
        count.textContent = "";
      }
    }, SEARCH_DEBOUNCE_MS);
  });

  return wrap;
}
