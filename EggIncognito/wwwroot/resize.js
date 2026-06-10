// Makes a CSS-grid row of panels user-resizable: inserts a thin drag gutter between adjacent
// RESIZABLE columns and lets the user drag to redistribute width. Smooth (operates on fractional
// weights, no reflow jank), clamped to a per-column minimum, and persisted to localStorage per key.
//
// Usage: makeResizable(mainEl, { key: "capture.cols", min: 160, fixed: [2] })
// The grid's existing `grid-template-columns` seeds the initial sizing. Children are the panels in
// document order; gutters are injected only between two adjacent resizable panels.
//
// `fixed` is a list of panel indices that should NOT be resizable (no adjacent gutter). A fixed panel
// keeps its current rendered width as a fixed px track; the resizable panels share the remaining space
// as fr. This lets a layout pin a side strip (e.g. an endpoint list or a detail pane) while still
// letting the middle panels be dragged. Stored weights only cover the resizable panels.

export function makeResizable(grid, { key, min = 160, fixed = [] } = {}) {
  const panels = Array.from(grid.children).filter((c) => !c.classList.contains("col-gutter"));
  const isFixed = (i) => fixed.includes(i);
  const resizableIdx = panels.map((_, i) => i).filter((i) => !isFixed(i));
  if (resizableIdx.length < 2) {
    // Nothing draggable: just emit a static template (fixed px for fixed panels, 1fr otherwise) so the
    // explicit `fixed` widths still take hold, then bail.
    if (fixed.length) {
      grid.style.gridTemplateColumns = panels
        .map((p, i) => isFixed(i) ? Math.round(p.getBoundingClientRect().width) + "px" : "1fr")
        .join(" ");
    }
    return;
  }

  // Weights track only the resizable panels (index-aligned with resizableIdx). Seed from a stored
  // override, else the resizable panels' current rendered widths.
  let weights = loadWeights(key, resizableIdx.length)
    ?? resizableIdx.map((i) => panels[i].getBoundingClientRect().width);
  normalize(weights);

  // Inject a gutter only between two adjacent resizable panels (i and i+1 both resizable).
  for (let i = 0; i < panels.length - 1; i++) {
    if (isFixed(i) || isFixed(i + 1)) continue;
    const gutter = document.createElement("div");
    gutter.className = "col-gutter";
    gutter.dataset.left = String(i);
    panels[i].after(gutter);
    wireGutter(gutter, resizableIdx.indexOf(i)); // map panel index -> weights index
  }
  apply();

  function apply() {
    // Build the template in document order: fixed panels render as a fixed px track, resizable panels
    // as their fr weight, with a gutter track between adjacent resizable panels.
    const parts = [];
    for (let i = 0; i < panels.length; i++) {
      if (isFixed(i)) {
        parts.push(Math.round(panels[i].getBoundingClientRect().width) + "px");
      } else {
        const w = weights[resizableIdx.indexOf(i)];
        parts.push(w.toFixed(4) + "fr");
      }
      // A gutter track follows panel i only when both i and i+1 are resizable.
      if (i < panels.length - 1 && !isFixed(i) && !isFixed(i + 1)) parts.push("var(--gutter-w)");
    }
    grid.style.gridTemplateColumns = parts.join(" ");
  }

  function wireGutter(gutter, leftW) {
    gutter.addEventListener("pointerdown", (e) => {
      e.preventDefault();
      gutter.setPointerCapture(e.pointerId);
      gutter.classList.add("dragging");

      const startX = e.clientX;
      // Convert the two adjacent resizable panels' fr weights into px so a drag maps 1:1 to pixels.
      const rPanels = resizableIdx.map((i) => panels[i]);
      const totalW = rPanels.reduce((s, p) => s + p.getBoundingClientRect().width, 0);
      const totalFr = weights.reduce((s, w) => s + w, 0);
      const pxPerFr = totalW / totalFr;
      const leftStart = weights[leftW];
      const rightStart = weights[leftW + 1];

      const onMove = (ev) => {
        const dxFr = (ev.clientX - startX) / pxPerFr;
        let left = leftStart + dxFr;
        let right = rightStart - dxFr;
        const minFr = min / pxPerFr;
        // Clamp so neither adjacent panel goes below the minimum; the pair's sum is conserved.
        if (left < minFr) { right -= (minFr - left); left = minFr; }
        if (right < minFr) { left -= (minFr - right); right = minFr; }
        if (left < minFr || right < minFr) return; // pair too small to satisfy both mins
        weights[leftW] = left;
        weights[leftW + 1] = right;
        apply();
      };
      const onUp = (ev) => {
        gutter.releasePointerCapture(ev.pointerId);
        gutter.classList.remove("dragging");
        window.removeEventListener("pointermove", onMove);
        window.removeEventListener("pointerup", onUp);
        saveWeights(key, weights);
      };
      window.addEventListener("pointermove", onMove);
      window.addEventListener("pointerup", onUp);
    });
  }
}

function normalize(weights) {
  const sum = weights.reduce((s, w) => s + w, 0) || 1;
  for (let i = 0; i < weights.length; i++) weights[i] = (weights[i] / sum) * weights.length;
}

function loadWeights(key, n) {
  try {
    const raw = JSON.parse(localStorage.getItem(key) || "null");
    if (Array.isArray(raw) && raw.length === n && raw.every((x) => typeof x === "number" && x > 0)) {
      return raw.slice();
    }
  } catch { /* ignore */ }
  return null;
}

function saveWeights(key, weights) {
  try { localStorage.setItem(key, JSON.stringify(weights)); } catch { /* ignore */ }
}
