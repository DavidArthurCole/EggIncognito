// The Documentation view for the Inspector. Owns the left-pane "Objects" list (proto message types),
// the #objectView panel (rendered doc + editor + tags + "used by" endpoints), and the tag chips shown
// on both objects and endpoints. Talks to /api/docs/* and /api/inspector/messages.
//
// Coupling to app.js is via the small window.__inspector bridge + a few CustomEvents, so the two
// modules don't import each other (no cycle). Everything degrades when there's no DB: the message list
// still renders (proto enumeration needs no DB), docs/tags simply come back empty.
import { renderMarkdown, makeMarkdownEditor } from "./md.js";

const API_DOCS = "/api/docs";
const API_INSPECTOR = "/api/inspector";
const MAX_CHIPS = 2; // chips shown before a "+N" overflow

const $ = (id) => document.getElementById(id);

const docState = {
  messages: [],          // proto type short names
  selected: null,        // selected message type name (object mode)
  tags: [],              // tag catalog [{id,slug,label,color}]
  tagsMap: {},           // "kind:key" -> [tag...]
  hasDocs: {},           // "kind:key" -> true
  endpoints: [],         // from the bridge (for "used by" + endpoint chips)
  canWrite: false,       // contributor+ (edit docs/tags)
};

async function getJson(url, fallback) {
  try { const r = await fetch(url); return r.ok ? await r.json() : fallback; }
  catch { return fallback; }
}
async function postJson(url, body) {
  const r = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
  return { ok: r.ok, status: r.status, data: await r.json().catch(() => null) };
}

const subjKey = (kind, key) => `${kind}:${key}`;

// tag chips

// Render a chip row (max MAX_CHIPS, then a "+N" overflow that expands on click) into `host`.
function renderChips(host, tags) {
  host.innerHTML = "";
  if (!tags || !tags.length) return;
  const shown = tags.slice(0, MAX_CHIPS);
  const rest = tags.slice(MAX_CHIPS);
  for (const t of shown) host.appendChild(chipEl(t));
  if (rest.length) {
    const more = document.createElement("button");
    more.type = "button"; more.className = "tag-chip tag-chip-more";
    more.textContent = `+${rest.length}`;
    more.title = rest.map(t => t.label).join(", ");
    more.addEventListener("click", () => {
      more.remove();
      for (const t of rest) host.appendChild(chipEl(t));
    });
    host.appendChild(more);
  }
}
function chipEl(t) {
  const c = document.createElement("span");
  c.className = "tag-chip";
  c.textContent = t.label;
  if (t.color) { c.style.borderColor = t.color; c.style.color = t.color; }
  return c;
}

// objects list

function renderObjects(filter = "") {
  const host = $("objects");
  host.innerHTML = "";
  const f = filter.toLowerCase();
  // Which message types are referenced by at least one endpoint (request or response).
  const used = referencedTypes();
  for (const name of docState.messages) {
    if (f && !name.toLowerCase().includes(f)) continue;
    const row = document.createElement("div");
    row.className = "ep obj-row" + (docState.selected === name ? " active" : "");
    const tags = docState.tagsMap[subjKey("message", name)];
    const hasDoc = docState.hasDocs[subjKey("message", name)];
    row.innerHTML = `<span class="obj-name">${name}</span>`;
    if (hasDoc) row.insertAdjacentHTML("beforeend", '<span class="obj-flag" title="has documentation">doc</span>');
    if (!used.has(name)) row.classList.add("obj-unused");
    const chips = document.createElement("span");
    chips.className = "tag-chips";
    renderChips(chips, tags);
    row.appendChild(chips);
    row.addEventListener("click", () => selectObject(name));
    host.appendChild(row);
  }
}

// Map of message type name -> set is overkill; we just need the set of referenced type names.
function referencedTypes() {
  const set = new Set();
  for (const e of docState.endpoints) {
    if (e.request) set.add(e.request);
    if (e.response) set.add(e.response);
  }
  return set;
}

// Endpoints that use a given message type (as request or response).
function endpointsUsing(name) {
  return docState.endpoints.filter(e => e.request === name || e.response === name);
}

// object (documentation) panel

async function selectObject(name) {
  docState.selected = name;
  renderObjects($("endpointFilter").value);
  const view = $("objectView");
  view.innerHTML = '<div class="muted">Loading...</div>';

  const [docResp, subjectTags] = await Promise.all([
    getJson(`${API_DOCS}/doc/message/${encodeURIComponent(name)}`, { bodyMd: null }),
    getJson(`${API_DOCS}/subject-tags/message/${encodeURIComponent(name)}`, []),
  ]);

  view.innerHTML = "";

  const head = document.createElement("div");
  head.className = "doc-head";
  head.innerHTML = `<h2 class="doc-title">${name}</h2>`;
  const chips = document.createElement("span");
  chips.className = "tag-chips";
  renderChips(chips, subjectTags);
  head.appendChild(chips);
  if (docState.canWrite) {
    const editTags = document.createElement("button");
    editTags.type = "button"; editTags.className = "btn-mini";
    editTags.textContent = "Edit tags";
    editTags.addEventListener("click", () => openTagEditor("message", name, subjectTags, chips));
    head.appendChild(editTags);
  }
  view.appendChild(head);

  // Rendered doc (or an empty-state) + edit affordance.
  const docBox = document.createElement("div");
  docBox.className = "doc-body";
  const renderDoc = (md) => {
    docBox.innerHTML = md ? renderMarkdown(md) : '<div class="muted">No documentation yet.</div>';
  };
  renderDoc(docResp.bodyMd);
  view.appendChild(docBox);

  if (docState.canWrite) {
    const editBtn = document.createElement("button");
    editBtn.type = "button"; editBtn.className = "btn-secondary doc-edit-btn";
    editBtn.textContent = docResp.bodyMd ? "Edit documentation" : "Add documentation";
    editBtn.addEventListener("click", () => openDocEditor("message", name, docResp.bodyMd ?? "", docBox, editBtn));
    view.appendChild(editBtn);
  }

  // "Used by" endpoints.
  const using = endpointsUsing(name);
  const usedWrap = document.createElement("div");
  usedWrap.className = "doc-usedby";
  usedWrap.innerHTML = `<div class="doc-section-title">Used by ${using.length} endpoint${using.length === 1 ? "" : "s"}</div>`;
  if (using.length) {
    const list = document.createElement("div");
    list.className = "doc-usedby-list";
    for (const e of using) {
      const role = e.request === name && e.response === name ? "req+resp"
        : e.request === name ? "request" : "response";
      const a = document.createElement("button");
      a.type = "button"; a.className = "doc-usedby-item";
      a.innerHTML = `<span class="mono">${e.path}</span><span class="doc-usedby-role">${role}</span>`;
      a.addEventListener("click", () => window.__inspector?.selectEndpointByPath(e.path));
      list.appendChild(a);
    }
    usedWrap.appendChild(list);
  }
  view.appendChild(usedWrap);
}

// Inline markdown editor for a doc. Replaces the rendered doc with the editor; Save persists + re-renders.
function openDocEditor(kind, key, initial, docBox, editBtn) {
  editBtn.disabled = true;
  const editor = makeMarkdownEditor({ initial });
  const actions = document.createElement("div");
  actions.className = "doc-edit-actions";
  const save = document.createElement("button");
  save.type = "button"; save.className = "btn-primary"; save.textContent = "Save";
  const cancel = document.createElement("button");
  cancel.type = "button"; cancel.className = "btn-secondary"; cancel.textContent = "Cancel";
  const out = document.createElement("span"); out.className = "muted doc-edit-out";
  actions.append(save, cancel, out);

  docBox.replaceWith(editor.root);
  editBtn.after(actions);

  const close = (newMd) => {
    actions.remove();
    const fresh = document.createElement("div");
    fresh.className = "doc-body";
    fresh.innerHTML = newMd ? renderMarkdown(newMd) : '<div class="muted">No documentation yet.</div>';
    editor.root.replaceWith(fresh);
    editBtn.disabled = false;
    editBtn.textContent = newMd ? "Edit documentation" : "Add documentation";
    // Rebind to the new docBox for a subsequent edit.
    const clone = editBtn.cloneNode(true); editBtn.replaceWith(clone);
    clone.addEventListener("click", () => openDocEditor(kind, key, newMd, fresh, clone));
    // Refresh the has-doc marker in the list.
    docState.hasDocs[subjKey(kind, key)] = !!newMd || undefined;
    if (!newMd) delete docState.hasDocs[subjKey(kind, key)];
    renderObjects($("endpointFilter").value);
  };

  cancel.addEventListener("click", () => close(initial));
  save.addEventListener("click", async () => {
    out.textContent = "Saving...";
    const md = editor.getValue();
    const res = await postJson(`${API_DOCS}/doc`, { subjectKind: kind, subjectKey: key, bodyMd: md });
    if (res.ok) { close(md.trim()); return; }
    out.textContent = res.status === 403 ? "Not authorized." : `Save failed (HTTP ${res.status}).`;
  });
}

// A simple tag picker: checkboxes for the catalog, Save replaces the subject's tag set.
function openTagEditor(kind, key, current, chipHost) {
  const picked = new Set(current.map(t => t.id));
  const pop = document.createElement("div");
  pop.className = "tag-editor";
  for (const t of docState.tags) {
    const lab = document.createElement("label");
    lab.className = "tag-editor-row";
    const cb = document.createElement("input");
    cb.type = "checkbox"; cb.checked = picked.has(t.id);
    cb.addEventListener("change", () => cb.checked ? picked.add(t.id) : picked.delete(t.id));
    lab.append(cb, document.createTextNode(" " + t.label));
    pop.appendChild(lab);
  }
  const actions = document.createElement("div");
  actions.className = "doc-edit-actions";
  const save = document.createElement("button");
  save.type = "button"; save.className = "btn-primary"; save.textContent = "Save tags";
  const cancel = document.createElement("button");
  cancel.type = "button"; cancel.className = "btn-secondary"; cancel.textContent = "Cancel";
  const out = document.createElement("span"); out.className = "muted doc-edit-out";
  actions.append(save, cancel, out);
  pop.appendChild(actions);
  chipHost.after(pop);

  cancel.addEventListener("click", () => pop.remove());
  save.addEventListener("click", async () => {
    out.textContent = "Saving...";
    const tagIds = [...picked];
    const res = await postJson(`${API_DOCS}/subject-tags`, { subjectKind: kind, subjectKey: key, tagIds });
    if (!res.ok) { out.textContent = res.status === 403 ? "Not authorized." : `Failed (HTTP ${res.status}).`; return; }
    const newTags = docState.tags.filter(t => picked.has(t.id));
    docState.tagsMap[subjKey(kind, key)] = newTags;
    renderChips(chipHost, newTags);
    pop.remove();
    renderObjects($("endpointFilter").value); // refresh list chips
  });
}

// endpoint chips: object mode is separate; this paints the endpoint view's chips

function paintEndpointChips(path) {
  const host = $("endpointTags");
  if (host) renderChips(host, docState.tagsMap[subjKey("endpoint", path)]);
}

// bootstrap

async function boot() {
  const bridge = window.__inspector;
  docState.endpoints = bridge?.getEndpoints?.() ?? [];
  const role = bridge?.appMode?.user?.role;
  // Mirror app.js's save-gate logic: contributor+ may edit; fail open when mode is absent (local dev).
  docState.canWrite = !bridge?.appMode || role === "contributor" || role === "admin";

  // Proto type list needs no DB; docs/tags degrade to empty without one.
  [docState.messages, docState.tags, docState.tagsMap, docState.hasDocs] = await Promise.all([
    getJson(`${API_INSPECTOR}/messages`, []),
    getJson(`${API_DOCS}/tags`, []),
    getJson(`${API_DOCS}/tags-map`, {}),
    getJson(`${API_DOCS}/has`, {}),
  ]);

  renderObjects();

  // Filter input is shared; when Objects mode is active, filter the objects list too.
  $("endpointFilter").addEventListener("input", (e) => {
    if (!$("objects").classList.contains("hidden")) renderObjects(e.target.value);
  });

  // Paint endpoint chips when app.js selects an endpoint.
  const onEndpointSelected = (e) => paintEndpointChips(e.detail.path);
  // When switching INTO objects mode with nothing selected yet, show a hint.
  const onListChange = (e) => {
    if (e.detail.which === "objects" && !docState.selected) {
      $("objectView").innerHTML = '<div class="muted">Select an object on the left to see its documentation.</div>';
    }
  };
  window.addEventListener("inspector:endpointselected", onEndpointSelected);
  window.addEventListener("inspector:listchange", onListChange);
  // These bind to window (outside the swapped DOM); tear them down on in-app navigation.
  window.__router?.onCleanup(() => {
    window.removeEventListener("inspector:endpointselected", onEndpointSelected);
    window.removeEventListener("inspector:listchange", onListChange);
  });
}

// app.js fires inspector:ready once its init (incl. the bridge) is done.
if (window.__inspector) boot();
else window.addEventListener("inspector:ready", boot, { once: true });
