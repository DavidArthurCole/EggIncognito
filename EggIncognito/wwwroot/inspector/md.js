// Tiny, dependency-free Markdown renderer + a lightweight editor, for the Documentation feature.
//
// SECURITY: docs are contributor-authored and stored, then rendered for everyone. To avoid stored
// XSS, renderMarkdown ESCAPES all HTML first, then applies a small, fixed set of Markdown -> HTML
// transforms over the escaped text. Authors cannot inject raw HTML/script; only the markdown syntax
// we explicitly support produces tags. Links/images are emitted only for http(s)/relative URLs.
//
// Supported: # h1-### h3, **bold**, *italic*, `code`, ```fenced code```, > blockquote (gotchas),
// - / * / 1. lists, [text](url), ![alt](url), --- rule, and paragraphs. Deliberately small.

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

// Only allow safe URL schemes in links/images (no javascript:, data:, etc.). Relative URLs are fine.
function safeUrl(url) {
  const u = url.trim();
  if (/^(https?:\/\/|\/|\.\/|#)/i.test(u)) return u;
  return "#";
}

// Inline spans: code first (so its contents are not further parsed), then images, links, bold, italic.
// Operates on already-HTML-escaped text.
function inline(text) {
  // `code`
  text = text.replace(/`([^`]+)`/g, (_, c) => `<code>${c}</code>`);
  // ![alt](url)
  text = text.replace(/!\[([^\]]*)\]\(([^)]+)\)/g, (_, alt, url) =>
    `<img src="${safeUrl(url)}" alt="${alt}" />`);
  // [text](url)
  text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) =>
    `<a href="${safeUrl(url)}" target="_blank" rel="noopener noreferrer">${label}</a>`);
  // **bold**
  text = text.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
  // *italic* (avoid matching the inside of **bold** which is already consumed)
  text = text.replace(/(^|[^*])\*([^*]+)\*/g, "$1<em>$2</em>");
  return text;
}

// Render markdown source to a safe HTML string.
export function renderMarkdown(src) {
  const lines = escapeHtml(src ?? "").split(/\r?\n/);
  const out = [];
  let i = 0;
  let listType = null; // "ul" | "ol" | null

  const closeList = () => { if (listType) { out.push(`</${listType}>`); listType = null; } };

  while (i < lines.length) {
    let line = lines[i];

    // Fenced code block ```
    if (/^```/.test(line.trim())) {
      closeList();
      const body = [];
      i++;
      while (i < lines.length && !/^```/.test(lines[i].trim())) { body.push(lines[i]); i++; }
      i++; // skip closing fence
      out.push(`<pre class="md-code"><code>${body.join("\n")}</code></pre>`);
      continue;
    }

    // Horizontal rule
    if (/^\s*---+\s*$/.test(line)) { closeList(); out.push('<hr class="md-rule" />'); i++; continue; }

    // Headings
    const h = line.match(/^(#{1,3})\s+(.*)$/);
    if (h) { closeList(); const n = h[1].length; out.push(`<h${n} class="md-h${n}">${inline(h[2])}</h${n}>`); i++; continue; }

    // Blockquote (gotchas / callouts) - collect consecutive > lines.
    if (/^\s*>\s?/.test(line)) {
      closeList();
      const body = [];
      while (i < lines.length && /^\s*>\s?/.test(lines[i])) { body.push(lines[i].replace(/^\s*>\s?/, "")); i++; }
      out.push(`<blockquote class="md-quote">${inline(body.join("<br/>"))}</blockquote>`);
      continue;
    }

    // Unordered list
    if (/^\s*[-*]\s+/.test(line)) {
      if (listType !== "ul") { closeList(); out.push('<ul class="md-list">'); listType = "ul"; }
      out.push(`<li>${inline(line.replace(/^\s*[-*]\s+/, ""))}</li>`); i++; continue;
    }
    // Ordered list
    if (/^\s*\d+\.\s+/.test(line)) {
      if (listType !== "ol") { closeList(); out.push('<ol class="md-list">'); listType = "ol"; }
      out.push(`<li>${inline(line.replace(/^\s*\d+\.\s+/, ""))}</li>`); i++; continue;
    }

    // Blank line
    if (line.trim() === "") { closeList(); i++; continue; }

    // Paragraph (merge consecutive non-empty, non-special lines).
    {
      closeList();
      const body = [line];
      i++;
      while (i < lines.length && lines[i].trim() !== ""
        && !/^(#{1,3}\s|\s*[-*]\s|\s*\d+\.\s|\s*>|```|\s*---+\s*$)/.test(lines[i])) {
        body.push(lines[i]); i++;
      }
      out.push(`<p>${inline(body.join("<br/>"))}</p>`);
    }
  }
  closeList();
  return out.join("\n");
}

// A small markdown editor: a textarea with a formatting toolbar and a live preview pane. Returns an
// object with getValue()/setValue() and the root element. `onChange` (optional) fires on edits.
export function makeMarkdownEditor({ initial = "", onChange } = {}) {
  const root = document.createElement("div");
  root.className = "md-editor";

  const toolbar = document.createElement("div");
  toolbar.className = "md-toolbar";

  const ta = document.createElement("textarea");
  ta.className = "md-source";
  ta.spellcheck = false;
  ta.value = initial;

  const preview = document.createElement("div");
  preview.className = "md-preview";

  const panes = document.createElement("div");
  panes.className = "md-panes";
  panes.append(ta, preview);

  const renderPreview = () => { preview.innerHTML = renderMarkdown(ta.value); };

  // Wrap/insert helpers for the toolbar buttons.
  const surround = (before, after = before, placeholder = "text") => {
    const s = ta.selectionStart, e = ta.selectionEnd;
    const sel = ta.value.slice(s, e) || placeholder;
    ta.value = ta.value.slice(0, s) + before + sel + after + ta.value.slice(e);
    ta.focus();
    ta.selectionStart = s + before.length;
    ta.selectionEnd = s + before.length + sel.length;
    fire();
  };
  const prefixLine = (prefix) => {
    const s = ta.selectionStart;
    const lineStart = ta.value.lastIndexOf("\n", s - 1) + 1;
    ta.value = ta.value.slice(0, lineStart) + prefix + ta.value.slice(lineStart);
    ta.focus();
    fire();
  };

  // Insert markdown text at the current cursor (replacing any selection).
  const insertAtCursor = (text) => {
    const s = ta.selectionStart, e = ta.selectionEnd;
    ta.value = ta.value.slice(0, s) + text + ta.value.slice(e);
    ta.focus();
    ta.selectionStart = ta.selectionEnd = s + text.length;
    fire();
  };

  // Upload an image File to the server and insert a markdown image referencing the stored URL. While
  // uploading, drop a placeholder and swap it for the real ref (or an error note) when done.
  const uploadImage = async (fileObj) => {
    if (!fileObj || !fileObj.type?.startsWith("image/")) return;
    const token = `![uploading ${fileObj.name || "image"}...]()`;
    insertAtCursor(token + "\n");
    const replaceToken = (withText) => { ta.value = ta.value.replace(token, withText); fire(); };
    try {
      const fd = new FormData();
      fd.append("file", fileObj, fileObj.name || "image");
      const r = await fetch("/api/docs/image", { method: "POST", body: fd });
      if (!r.ok) {
        const msg = r.status === 403 ? "not authorized" : r.status === 503 ? "no database" : `HTTP ${r.status}`;
        replaceToken(`*(image upload failed: ${msg})*`);
        return;
      }
      const { url } = await r.json();
      replaceToken(`![${fileObj.name || "image"}](${url})`);
    } catch (err) {
      replaceToken(`*(image upload failed: ${err.message})*`);
    }
  };

  // Hidden file input for the toolbar image button.
  const fileInput = document.createElement("input");
  fileInput.type = "file"; fileInput.accept = "image/png,image/jpeg,image/gif,image/webp"; fileInput.hidden = true;
  fileInput.addEventListener("change", () => {
    for (const f of fileInput.files) uploadImage(f);
    fileInput.value = "";
  });

  const buttons = [
    ["B", "Bold", () => surround("**")],
    ["I", "Italic", () => surround("*")],
    ["</>", "Code", () => surround("`", "`", "code")],
    ["H", "Heading", () => prefixLine("## ")],
    ["-", "List item", () => prefixLine("- ")],
    [">", "Callout / gotcha", () => prefixLine("> ")],
    ["link", "Link", () => surround("[", "](https://)", "text")],
    ["img url", "Image by URL", () => surround("![", "](https://)", "alt")],
    ["upload", "Upload image (or paste / drag-drop a file)", () => fileInput.click()],
  ];
  for (const [label, title, fn] of buttons) {
    const b = document.createElement("button");
    b.type = "button"; b.className = "md-tb-btn"; b.textContent = label; b.title = title;
    b.addEventListener("click", fn);
    toolbar.appendChild(b);
  }

  function fire() { renderPreview(); onChange?.(ta.value); }
  ta.addEventListener("input", fire);

  // Paste an image from the clipboard -> upload + insert.
  ta.addEventListener("paste", (e) => {
    const items = [...(e.clipboardData?.items ?? [])];
    const imgItem = items.find(it => it.kind === "file" && it.type.startsWith("image/"));
    if (imgItem) { e.preventDefault(); uploadImage(imgItem.getAsFile()); }
  });
  // Drag-drop an image file -> upload + insert. preventDefault on dragover so drop fires.
  ta.addEventListener("dragover", (e) => { if ([...e.dataTransfer.types].includes("Files")) e.preventDefault(); });
  ta.addEventListener("drop", (e) => {
    const files = [...(e.dataTransfer?.files ?? [])].filter(f => f.type.startsWith("image/"));
    if (files.length) { e.preventDefault(); for (const f of files) uploadImage(f); }
  });

  root.append(toolbar, fileInput, panes);
  renderPreview();

  return {
    root,
    getValue: () => ta.value,
    setValue: (v) => { ta.value = v ?? ""; renderPreview(); },
  };
}
