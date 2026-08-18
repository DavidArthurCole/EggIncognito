export function attach(view) {
  if (!view) return null;
  const scroll = view.querySelector(".cdiff-scroll");
  const map = view.querySelector(".cdiff-map");
  if (!scroll || !map) return null;

  let frame = 0;

  function paint() {
    frame = 0;
    const total = scroll.scrollHeight;
    if (total <= 0) return;
    const height = Math.min(100, scroll.clientHeight * 100 / total);
    const top = Math.min(100 - height, scroll.scrollTop * 100 / total);
    map.style.setProperty("--vp-top", top + "%");
    map.style.setProperty("--vp-h", height + "%");
  }

  function queue() {
    if (frame) return;
    frame = requestAnimationFrame(paint);
  }

  scroll.addEventListener("scroll", queue, { passive: true });
  const observer = new ResizeObserver(queue);
  observer.observe(scroll);
  if (scroll.firstElementChild) observer.observe(scroll.firstElementChild);
  paint();

  return {
    dispose: () => {
      if (frame) cancelAnimationFrame(frame);
      scroll.removeEventListener("scroll", queue);
      observer.disconnect();
    }
  };
}
