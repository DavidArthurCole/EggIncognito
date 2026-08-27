import { engine } from './playgroundShared.js';

const GIF_URL = 'https://cdn.jsdelivr.net/npm/gif.js@0.2.0/dist/gif.js';
const GIF_WORKER_URL = 'https://cdn.jsdelivr.net/npm/gif.js@0.2.0/dist/gif.worker.js';

let GIFClass = null;

async function ensureGif() {
  if (GIFClass) return GIFClass;

  await new Promise((resolve, reject) => {
    if (globalThis.GIF) {
      resolve();
      return;
    }
    const s = document.createElement('script');
    s.src = GIF_URL;
    s.onload = resolve;
    s.onerror = () => reject(new Error('failed to load gif.js'));
    document.head.appendChild(s);
  });
  GIFClass = globalThis.GIF;
  if (!GIFClass) throw new Error('gif.js did not register');
  return GIFClass;
}

async function workerUrl() {
  const resp = await fetch(GIF_WORKER_URL);
  if (!resp.ok) throw new Error('failed to fetch gif.worker.js (' + resp.status + ')');
  const src = await resp.text();
  return URL.createObjectURL(new Blob([src], { type: 'application/javascript' }));
}

function rnd(x) { return Math.sign(x) * Math.round(Math.abs(x)); }

export async function record(dotnetRef, opts) {
  const e = engine();
  if (!e) throw new Error('engine not ready');
  if (!e.anyAnimated()) throw new Error('nothing is animated');

  const fps = Math.min(30, Math.max(5, opts?.fps || 20));
  const maxWidth = opts?.maxWidth || 0;
  const fallback = opts?.fallbackColor || '#1a1a1f';

  const period = e.animPeriod();
  const N = Math.max(1, rnd(fps * period));
  const delay = Math.max(1, rnd((period / N) * 1000));

  const srcCanvas = e.canvas();
  if (!srcCanvas) throw new Error('renderer canvas not available');
  const srcW = srcCanvas.width, srcH = srcCanvas.height;
  const outW = maxWidth && maxWidth < srcW ? maxWidth : srcW;
  const outH = Math.max(1, Math.round(srcH * (outW / srcW)));

  const off = document.createElement('canvas');
  off.width = outW; off.height = outH;
  const ctx = off.getContext('2d');

  const GIF = await ensureGif();
  const workerScript = await workerUrl();
  try {
    const gif = new GIF({ workers: 2, quality: 10, width: outW, height: outH, workerScript, repeat: 0 });

    e.captureBegin();
    try {
      for (let i = 0; i < N; i++) {
        const t = (i / N) * period;
        e.renderAtPhase(t);
        ctx.fillStyle = fallback;
        ctx.fillRect(0, 0, outW, outH);
        ctx.drawImage(srcCanvas, 0, 0, outW, outH);
        gif.addFrame(ctx, { copy: true, delay });
        if (dotnetRef) dotnetRef.invokeMethodAsync('OnRecordProgress', 'capturing', (i + 1) / N);
      }
    } finally {
      e.captureEnd();
    }

    await new Promise((resolve, reject) => {
      gif.on('progress', p => { if (dotnetRef) dotnetRef.invokeMethodAsync('OnRecordProgress', 'encoding', p); });
      gif.on('finished', blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'playground-loop.gif';
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
        resolve();
      });
      gif.on('abort', () => reject(new Error('gif encoding aborted')));
      gif.render();
    });
  } finally {
    URL.revokeObjectURL(workerScript);
  }
}
