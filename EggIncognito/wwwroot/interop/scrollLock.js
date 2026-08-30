let locks = 0;
let savedOverflow = null;

export function lock() {
  locks += 1;
  if (locks > 1) return;
  const body = document.body;
  savedOverflow = body.style.overflow;
  body.style.overflow = "hidden";
}

export function unlock() {
  if (locks === 0) return;
  locks -= 1;
  if (locks > 0 || savedOverflow === null) return;
  document.body.style.overflow = savedOverflow;
  savedOverflow = null;
}
