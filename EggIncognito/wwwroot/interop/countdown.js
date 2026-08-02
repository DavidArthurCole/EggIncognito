let timer = null;

function fmt(s) {
  if (s <= 0) return "Ended";
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = Math.floor(s % 60);
  const pad = n => String(n).padStart(2, "0");
  return `${pad(h)}:${pad(m)}:${pad(sec)}`;
}

function tick() {
  const els = document.querySelectorAll("[data-countdown]");
  if (els.length === 0) return;
  const now = Date.now() / 1000;
  for (const el of els) {
    const s = Number(el.dataset.countdown) - now;
    el.textContent = fmt(s);
    el.closest(".event-pill")?.classList.toggle("ended", s <= 0);
  }
}

export function start() {
  if (timer) return;
  tick();
  timer = setInterval(tick, 1000);
}

export function stop() {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
}
