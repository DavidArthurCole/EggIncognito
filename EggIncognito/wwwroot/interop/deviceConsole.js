const sessions = new Map();

function detach(img) {
  const s = sessions.get(img);
  if (!s) return null;
  img.removeEventListener("load", s.onLoad);
  img.removeEventListener("error", s.onError);
  sessions.delete(img);
  return s;
}

function halt(img) {
  if (!img) return;
  detach(img);
  img.removeAttribute("src");
}

function report(img, s) {
  const w = img.naturalWidth;
  const h = img.naturalHeight;
  const rw = img.clientWidth;
  const rh = img.clientHeight;
  if (w === s.lastW && h === s.lastH && rw === s.lastRw && rh === s.lastRh) return;
  s.lastW = w;
  s.lastH = h;
  s.lastRw = rw;
  s.lastRh = rh;
  s.dotnet.invokeMethodAsync("OnFrameSize", w, h, rw, rh);
}

function attach(img, dotnet, note, once) {
  const s = { dotnet, lastW: -1, lastH: -1, lastRw: -1, lastRh: -1 };
  s.onLoad = () => {
    if (once) detach(img);
    report(img, s);
  };
  s.onError = () => {
    detach(img);
    img.removeAttribute("src");
    dotnet.invokeMethodAsync("OnFrameFailed", 0, note);
  };
  img.addEventListener("load", s.onLoad);
  img.addEventListener("error", s.onError);
  sessions.set(img, s);
  return s;
}

function bust(url) {
  return url + (url.indexOf("?") >= 0 ? "&" : "?") + "t=" + Date.now();
}

export function start(img, streamUrl, dotnet) {
  if (!img) return false;
  halt(img);
  attach(img, dotnet, "device stopped sending frames", false);
  img.src = bust(streamUrl);
  return true;
}

export function stop(img) {
  halt(img);
}

export function once(img, url, dotnet) {
  if (!img) return false;
  halt(img);
  attach(img, dotnet, "no frame came back", true);
  img.src = bust(url);
  return true;
}

export function measure(img) {
  if (!img) return [0, 0, 0, 0];
  return [img.naturalWidth, img.naturalHeight, img.clientWidth, img.clientHeight];
}

export function clear(img) {
  halt(img);
}

const videoSessions = new WeakMap();
const CHUNK_US = 33333;
const PENDING_CAP = 64;

export function supportsVideo() {
  return typeof window !== "undefined" && typeof window.VideoDecoder === "function" && typeof window.EncodedVideoChunk === "function";
}

export function splitNals(bytes) {
  const nals = [];
  const n = bytes.length;
  let payloadStart = -1;
  let lastCode = -1;
  let i = 0;
  while (i + 2 < n) {
    if (bytes[i] === 0 && bytes[i + 1] === 0 && bytes[i + 2] === 1) {
      if (payloadStart >= 0) {
        let end = i;
        while (end > payloadStart && bytes[end - 1] === 0) end--;
        if (end > payloadStart) nals.push(bytes.slice(payloadStart, end));
      }
      lastCode = i;
      payloadStart = i + 3;
      i += 3;
      continue;
    }
    i++;
  }
  const rest = lastCode >= 0 ? bytes.slice(lastCode) : bytes.slice(0);
  return { nals, rest };
}

function concatBytes(a, b) {
  if (!a || a.length === 0) return b;
  if (!b || b.length === 0) return a;
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
}

function annexB(parts) {
  let len = 0;
  for (const p of parts) len += 4 + p.length;
  const out = new Uint8Array(len);
  let o = 0;
  for (const p of parts) {
    out[o + 3] = 1;
    o += 4;
    out.set(p, o);
    o += p.length;
  }
  return out;
}

function unescapeRbsp(nal) {
  const out = [];
  let zeros = 0;
  for (let i = 0; i < nal.length; i++) {
    const b = nal[i];
    if (zeros >= 2 && b === 3) {
      zeros = 0;
      continue;
    }
    out.push(b);
    zeros = b === 0 ? zeros + 1 : 0;
  }
  return Uint8Array.from(out);
}

class BitReader {
  constructor(bytes) {
    this.b = bytes;
    this.pos = 0;
  }

  bit() {
    const i = this.pos >> 3;
    if (i >= this.b.length) throw new RangeError("sps truncated");
    const v = (this.b[i] >> (7 - (this.pos & 7))) & 1;
    this.pos++;
    return v;
  }

  bits(n) {
    let v = 0;
    for (let k = 0; k < n; k++) v = v * 2 + this.bit();
    return v;
  }

  ue() {
    let zeros = 0;
    while (this.bit() === 0) {
      zeros++;
      if (zeros > 31) throw new RangeError("bad exp-golomb");
    }
    return zeros === 0 ? 0 : 2 ** zeros - 1 + this.bits(zeros);
  }

  se() {
    const k = this.ue();
    return k & 1 ? (k + 1) / 2 : -(k / 2);
  }
}

function skipScalingList(br, size) {
  let last = 8;
  let next = 8;
  for (let j = 0; j < size; j++) {
    if (next !== 0) next = (last + br.se() + 256) % 256;
    last = next === 0 ? last : next;
  }
}

const HIGH_PROFILES = new Set([100, 110, 122, 244, 44, 83, 86, 118, 128, 138, 139, 134, 135]);

function hex2(v) {
  return v.toString(16).toUpperCase().padStart(2, "0");
}

function parseSps(nal) {
  const rbsp = unescapeRbsp(nal.subarray(1));
  const br = new BitReader(rbsp);
  const profileIdc = br.bits(8);
  const constraints = br.bits(8);
  const levelIdc = br.bits(8);
  br.ue();
  let chromaFormatIdc = 1;
  let separateColourPlane = 0;
  if (HIGH_PROFILES.has(profileIdc)) {
    chromaFormatIdc = br.ue();
    if (chromaFormatIdc === 3) separateColourPlane = br.bit();
    br.ue();
    br.ue();
    br.bit();
    if (br.bit()) {
      const count = chromaFormatIdc !== 3 ? 8 : 12;
      for (let i = 0; i < count; i++) {
        if (br.bit()) skipScalingList(br, i < 6 ? 16 : 64);
      }
    }
  }
  br.ue();
  const pocType = br.ue();
  if (pocType === 0) {
    br.ue();
  } else if (pocType === 1) {
    br.bit();
    br.se();
    br.se();
    const cycle = br.ue();
    for (let i = 0; i < cycle; i++) br.se();
  }
  br.ue();
  br.bit();
  const widthMbs = br.ue() + 1;
  const heightMapUnits = br.ue() + 1;
  const frameMbsOnly = br.bit();
  if (!frameMbsOnly) br.bit();
  br.bit();
  let cropLeft = 0;
  let cropRight = 0;
  let cropTop = 0;
  let cropBottom = 0;
  if (br.bit()) {
    cropLeft = br.ue();
    cropRight = br.ue();
    cropTop = br.ue();
    cropBottom = br.ue();
  }
  let subW = 1;
  let subH = 1;
  if (!separateColourPlane) {
    if (chromaFormatIdc === 1) {
      subW = 2;
      subH = 2;
    } else if (chromaFormatIdc === 2) {
      subW = 2;
      subH = 1;
    }
  }
  const cropUnitX = subW;
  const cropUnitY = subH * (2 - frameMbsOnly);
  const width = widthMbs * 16 - (cropLeft + cropRight) * cropUnitX;
  const height = (2 - frameMbsOnly) * heightMapUnits * 16 - (cropTop + cropBottom) * cropUnitY;
  return { codec: "avc1." + hex2(profileIdc) + hex2(constraints) + hex2(levelIdc), width, height };
}

function safeInvoke(s, method, ...args) {
  if (!s.dotnet) return;
  try {
    const p = s.dotnet.invokeMethodAsync(method, ...args);
    if (p && typeof p.catch === "function") p.catch(() => {});
  } catch {
  }
}

function closeFrame(s) {
  if (!s.frame) return;
  try {
    s.frame.close();
  } catch {
  }
  s.frame = null;
}

function closeDecoder(s) {
  const d = s.decoder;
  s.decoder = null;
  if (!d) return;
  try {
    if (d.state !== "closed") d.close();
  } catch {
  }
}

function finish(s, reason) {
  if (s.ended) return;
  s.ended = true;
  s.stopped = true;
  if (s.raf) {
    cancelAnimationFrame(s.raf);
    s.raf = 0;
  }
  if (s.statsTimer) {
    clearInterval(s.statsTimer);
    s.statsTimer = 0;
  }
  try {
    s.ctrl.abort();
  } catch {
  }
  if (s.reader) {
    const r = s.reader;
    s.reader = null;
    try {
      const p = r.cancel();
      if (p && typeof p.catch === "function") p.catch(() => {});
    } catch {
    }
  }
  closeDecoder(s);
  closeFrame(s);
  s.pending.clear();
  if (videoSessions.get(s.canvas) === s) videoSessions.delete(s.canvas);
  blank(s.canvas);
  safeInvoke(s, "OnVideoEnded", reason);
}

function blank(canvas) {
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  if (!ctx) return;
  ctx.fillStyle = "#000";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
}

function onFrame(s, frame) {
  if (s.ended) {
    try {
      frame.close();
    } catch {
    }
    return;
  }
  closeFrame(s);
  s.frame = frame;
  const arrival = s.pending.get(frame.timestamp);
  s.pending.delete(frame.timestamp);
  s.frameArrival = arrival === undefined ? -1 : arrival;
}

function draw(s) {
  s.raf = 0;
  if (s.ended) return;
  const frame = s.frame;
  if (frame) {
    s.frame = null;
    const w = frame.displayWidth || frame.codedWidth;
    const h = frame.displayHeight || frame.codedHeight;
    try {
      if (s.canvas.width !== w || s.canvas.height !== h) {
        s.canvas.width = w;
        s.canvas.height = h;
      }
      s.ctx.drawImage(frame, 0, 0, w, h);
      s.frameW = w;
      s.frameH = h;
      s.drawn++;
      if (s.frameArrival >= 0) {
        s.latencySum += performance.now() - s.frameArrival;
        s.latencyCount++;
      }
    } catch {
    }
    try {
      frame.close();
    } catch {
    }
  }
  s.raf = requestAnimationFrame(() => draw(s));
}

function tickStats(s) {
  if (s.ended) return;
  const now = performance.now();
  const dt = Math.max(1, now - s.statsAt);
  const fps = Math.round((s.drawn * 1000) / dt);
  const latency = s.latencyCount > 0 ? Math.round(s.latencySum / s.latencyCount) : 0;
  const kbps = Math.round((s.bytesSince * 8) / dt);
  s.statsAt = now;
  s.drawn = 0;
  s.latencySum = 0;
  s.latencyCount = 0;
  s.bytesSince = 0;
  safeInvoke(s, "OnVideoStats", fps, latency, s.frameW, s.frameH, kbps);
}

function configure(s) {
  let info;
  try {
    info = parseSps(s.sps);
  } catch {
    return;
  }
  if (s.decoder && s.decoder.state === "configured" && info.codec === s.codec && info.width === s.width && info.height === s.height) return;
  s.codec = info.codec;
  s.width = info.width;
  s.height = info.height;
  closeDecoder(s);
  try {
    const d = new VideoDecoder({
      output: (frame) => onFrame(s, frame),
      error: (e) => finish(s, "error: " + (e && e.message ? e.message : String(e)))
    });
    d.configure({ codec: info.codec, optimizeForLatency: true });
    s.decoder = d;
    s.needKey = true;
  } catch (e) {
    finish(s, "error: " + (e && e.message ? e.message : String(e)));
  }
}

function submit(s, nal, isKey, now) {
  const d = s.decoder;
  if (!d || d.state !== "configured") return;
  if (isKey) {
    if (!s.sps || !s.pps) return;
    s.needKey = false;
  } else if (s.needKey) {
    return;
  } else if (d.decodeQueueSize > s.opts.maxQueue) {
    s.needKey = true;
    return;
  }
  const data = isKey ? annexB([s.sps, s.pps, nal]) : annexB([nal]);
  const timestamp = s.ts;
  s.ts += CHUNK_US;
  s.pending.set(timestamp, now);
  while (s.pending.size > PENDING_CAP) s.pending.delete(s.pending.keys().next().value);
  try {
    d.decode(new EncodedVideoChunk({ type: isKey ? "key" : "delta", timestamp, data }));
  } catch {
    s.pending.delete(timestamp);
    s.needKey = true;
  }
}

function handleNal(s, nal, now) {
  if (s.ended || nal.length === 0) return;
  const type = nal[0] & 0x1f;
  if (type === 7) {
    s.sps = nal;
    configure(s);
  } else if (type === 8) {
    s.pps = nal;
  } else if (type === 5 || type === 1) {
    submit(s, nal, type === 5, now);
  }
}

async function pump(s) {
  let reason = "ended";
  try {
    const response = await fetch(s.url, { cache: "no-store", credentials: "same-origin", signal: s.ctrl.signal });
    if (!response.ok) throw new Error("http " + response.status);
    if (!response.body) throw new Error("no response body");
    s.reader = response.body.getReader();
    let rest = new Uint8Array(0);
    for (;;) {
      const { done, value } = await s.reader.read();
      if (done || s.stopped) break;
      s.bytesSince += value.byteLength;
      const split = splitNals(concatBytes(rest, value));
      rest = split.rest;
      const now = performance.now();
      for (const nal of split.nals) handleNal(s, nal, now);
    }
  } catch (e) {
    const aborted = s.stopped || (e && e.name === "AbortError");
    reason = aborted ? "stopped" : "error: " + (e && e.message ? e.message : String(e));
  }
  finish(s, s.stopped ? "stopped" : reason);
}

export function startVideo(canvas, url, dotnet, opts) {
  if (!canvas || !supportsVideo()) return false;
  stopVideo(canvas);
  const ctx = canvas.getContext("2d", { alpha: false, desynchronized: true });
  if (!ctx) return false;
  const s = {
    canvas,
    ctx,
    url,
    dotnet,
    opts: Object.assign({ maxQueue: 2 }, opts || {}),
    ctrl: new AbortController(),
    reader: null,
    decoder: null,
    sps: null,
    pps: null,
    codec: null,
    width: 0,
    height: 0,
    needKey: true,
    ts: 0,
    pending: new Map(),
    frame: null,
    frameArrival: -1,
    frameW: 0,
    frameH: 0,
    raf: 0,
    drawn: 0,
    latencySum: 0,
    latencyCount: 0,
    bytesSince: 0,
    statsAt: performance.now(),
    statsTimer: 0,
    stopped: false,
    ended: false
  };
  videoSessions.set(canvas, s);
  s.statsTimer = setInterval(() => tickStats(s), 1000);
  s.raf = requestAnimationFrame(() => draw(s));
  pump(s);
  return true;
}

export function stopVideo(canvas) {
  if (!canvas) return;
  const s = videoSessions.get(canvas);
  if (!s) return;
  s.stopped = true;
  finish(s, "stopped");
}

export function measureCanvas(canvas) {
  if (!canvas) return { w: 0, h: 0, frameW: 0, frameH: 0 };
  const s = videoSessions.get(canvas);
  return {
    w: canvas.clientWidth,
    h: canvas.clientHeight,
    frameW: s ? s.frameW : canvas.width,
    frameH: s ? s.frameH : canvas.height
  };
}
