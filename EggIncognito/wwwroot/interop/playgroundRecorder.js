// Records one perfect loop of the playground's procedural animation and exports a looping GIF, in the browser.
// Drives the engine's deterministic capture (engine.renderAtPhase) over one period, reads each frame off the
// canvas onto an offscreen 2D canvas (compositing onto a solid bg + optional downscale), and encodes with
// gif.js in a worker. Talks to the engine via window.__pgEngine (the same bridge the designer uses); reaches
// the designer's gizmo toggle via globalThis.__pgDesigner (a ?v=-busted import would fork a second instance).

function engine() { return globalThis.__pgEngine; }
function designer() { return globalThis.__pgDesigner; }

const GIF_URL = 'https://cdn.jsdelivr.net/npm/gif.js@0.2.0/dist/gif.js';
const GIF_WORKER_URL = 'https://cdn.jsdelivr.net/npm/gif.js@0.2.0/dist/gif.worker.js';

let GIFClass = null;

async function ensureGif() {
  if (GIFClass) return GIFClass;
  // gif.js is a UMD bundle that sets window.GIF. Load it via a script tag (it is not an ES module).
  await new Promise((resolve, reject) => {
    if (globalThis.GIF) { resolve(); return; }
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

// round half away from zero, matching the C# LoopFrames contract.
function rnd(x) { return Math.sign(x) * Math.round(Math.abs(x)); }

export async function record(dotnetRef, opts) {
  const e = engine();
  if (!e) throw new Error('engine not ready');
  if (!e.anyAnimated()) throw new Error('nothing is animated');

  const fps = Math.min(30, Math.max(5, (opts && opts.fps) || 20));
  const maxWidth = (opts && opts.maxWidth) || 0; // 0 = full canvas width
  const fallback = (opts && opts.fallbackColor) || '#1a1a1f';

  const period = e.animPeriod();
  const N = Math.max(1, rnd(fps * period));
  const delay = Math.max(1, rnd((period / N) * 1000));

  const srcCanvas = e.renderer().domElement;
  const srcW = srcCanvas.width, srcH = srcCanvas.height;
  const outW = maxWidth && maxWidth < srcW ? maxWidth : srcW;
  const outH = Math.max(1, Math.round(srcH * (outW / srcW)));

  // offscreen 2D canvas: pre-fill the fallback bg, then draw the (possibly downscaled) frame onto it. This
  // both flattens a transparent scene onto a solid color and applies the size cap.
  const off = document.createElement('canvas');
  off.width = outW; off.height = outH;
  const ctx = off.getContext('2d');

  const GIF = await ensureGif();
  const gif = new GIF({ workers: 2, quality: 10, width: outW, height: outH, workerScript: GIF_WORKER_URL, repeat: 0 });

  // clean frames: drop the selection outline + hide the gizmo for the duration of the capture.
  e.captureCleanOutline(true);
  if (designer()) designer().setGizmoVisible(false);
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
    e.captureCleanOutline(false);
    if (designer()) designer().setGizmoVisible(true);
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
}
