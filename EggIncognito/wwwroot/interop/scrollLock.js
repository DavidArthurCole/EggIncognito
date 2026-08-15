let locks = 0;
let saved = null;

export function lock() {
  locks += 1;
  if (locks > 1) return;
  const body = document.body;
  saved = {
    scrollX: window.scrollX,
    scrollY: window.scrollY,
    overflow: body.style.overflow,
    paddingRight: body.style.paddingRight
  };
  const gap = window.innerWidth - document.documentElement.clientWidth;
  if (gap > 0) body.style.paddingRight = `${gap}px`;
  body.style.overflow = "hidden";
}

export function unlock() {
  if (locks === 0) return;
  locks -= 1;
  if (locks > 0 || saved === null) return;
  const body = document.body;
  body.style.overflow = saved.overflow;
  body.style.paddingRight = saved.paddingRight;
  window.scrollTo(saved.scrollX, saved.scrollY);
  saved = null;
}
