// Positions the login provider dropdown as a fixed-position element anchored to its trigger button.
// Fixed positioning escapes any overflow:hidden/auto ancestor (e.g. the /protos modal card), which a
// plain absolute child cannot. The requested placement is a preference; if the menu would overflow the
// viewport it flips to the opposite side. Repositions on scroll/resize; closes on outside click.

const GAP = 6;
let active = null;

function place(button, menu, placement) {
  const b = button.getBoundingClientRect();
  const mw = menu.offsetWidth;
  const mh = menu.offsetHeight;
  const vw = window.innerWidth;
  const vh = window.innerHeight;

  let vert = placement.startsWith("Top") ? "top" : placement.startsWith("Bottom") ? "bottom" : "side";
  let horiz = placement;

  let top, left;

  if (vert === "side") {
    // Right / Left: menu sits beside the button, top-aligned.
    let toRight = placement === "Right";
    if (toRight && b.right + GAP + mw > vw && b.left - GAP - mw >= 0) toRight = false;
    if (!toRight && b.left - GAP - mw < 0 && b.right + GAP + mw <= vw) toRight = true;
    left = toRight ? b.right + GAP : b.left - GAP - mw;
    top = b.top;
    if (top + mh > vh) top = Math.max(GAP, vh - mh - GAP);
  } else {
    // Bottom* / Top*: menu drops below or above, aligned to one edge of the button.
    let below = vert === "bottom";
    if (below && b.bottom + GAP + mh > vh && b.top - GAP - mh >= 0) below = false;
    if (!below && b.top - GAP - mh < 0 && b.bottom + GAP + mh <= vh) below = true;
    top = below ? b.bottom + GAP : b.top - GAP - mh;

    const rightAligned = horiz.endsWith("Right");
    left = rightAligned ? b.right - mw : b.left;
    if (left + mw > vw) left = vw - mw - GAP;
    if (left < GAP) left = GAP;
  }

  // Final clamp: whatever the branch chose, keep the whole menu inside the viewport.
  left = Math.min(Math.max(GAP, left), Math.max(GAP, vw - mw - GAP));
  top = Math.min(Math.max(GAP, top), Math.max(GAP, vh - mh - GAP));

  menu.style.position = "fixed";
  menu.style.top = `${Math.round(top)}px`;
  menu.style.left = `${Math.round(left)}px`;
}

export function open(button, menu, placement) {
  close();
  const reposition = () => place(button, menu, placement);
  reposition();
  window.addEventListener("scroll", reposition, true);
  window.addEventListener("resize", reposition);
  active = { reposition };
}

export function close() {
  if (!active) return;
  window.removeEventListener("scroll", active.reposition, true);
  window.removeEventListener("resize", active.reposition);
  active = null;
}
