
export function makeDraggable(el, handle) {
  if (!el || !handle) return { dispose() {} };

  let startX = 0, startY = 0, baseLeft = 0, baseTop = 0, dragging = false;

  const onMove = ev => {
    if (!dragging) return;
    const left = baseLeft + (ev.clientX - startX);
    const top = baseTop + (ev.clientY - startY);
   
    const maxLeft = window.innerWidth - 40;
    const maxTop = window.innerHeight - 40;
    el.style.left = Math.max(-el.offsetWidth + 80, Math.min(maxLeft, left)) + 'px';
    el.style.top = Math.max(0, Math.min(maxTop, top)) + 'px';
  };

  const onUp = () => {
    dragging = false;
    document.body.style.userSelect = '';
  };

  const onDown = ev => {
    dragging = true;
    const rect = el.getBoundingClientRect();
   
    el.style.transform = 'none';
    el.style.left = rect.left + 'px';
    el.style.top = rect.top + 'px';
    baseLeft = rect.left;
    baseTop = rect.top;
    startX = ev.clientX;
    startY = ev.clientY;
    document.body.style.userSelect = 'none';
    ev.preventDefault();
  };

  handle.addEventListener('pointerdown', onDown);
  window.addEventListener('pointermove', onMove);
  window.addEventListener('pointerup', onUp);

  return {
    dispose() {
      handle.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
    },
  };
}
