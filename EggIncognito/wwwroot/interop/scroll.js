
export function scrollToBottom(el) {
  if (el) el.scrollTop = el.scrollHeight;
}

export function scrollToFraction(el, fraction) {
  if (!el) return;
  const span = el.scrollWidth - el.clientWidth;
  if (span <= 0) return;
  el.scrollLeft = Math.max(0, Math.min(span, span * fraction));
}
