// Tiny scroll bridge for the Capture flow list's auto-scroll setting: newest flows append at the
// bottom, so "scroll to newest" means scroll the container to its bottom.

export function scrollToBottom(el) {
  if (el) el.scrollTop = el.scrollHeight;
}
