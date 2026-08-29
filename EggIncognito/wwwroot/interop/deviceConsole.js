const sessions = new Map();

function revoke(url) {
  if (url) {
    try {
      URL.revokeObjectURL(url);
    } catch {
    }
  }
}

function teardown(img) {
  const s = sessions.get(img);
  if (!s) return;
  s.stopped = true;
  if (s.timer) clearTimeout(s.timer);
  s.timer = 0;
  revoke(s.objectUrl);
  s.objectUrl = null;
  sessions.delete(img);
}

async function failure(res) {
  let note = "http " + res.status;
  try {
    const body = await res.json();
    if (body && typeof body.error === "string") note = body.error;
  } catch {
  }
  return note;
}

async function tick(img, s) {
  if (s.stopped) return;

  if (document.visibilityState !== "visible" || !img.isConnected) {
    s.timer = setTimeout(() => tick(img, s), Math.max(s.intervalMs, 1000));
    return;
  }

  let stop = false;
  try {
    const sep = s.url.indexOf("?") >= 0 ? "&" : "?";
    const res = await fetch(s.url + sep + "t=" + Date.now(), { credentials: "same-origin", cache: "no-store" });
    if (!res.ok) {
      const note = await failure(res);
      stop = true;
      teardown(img);
      await s.dotnet.invokeMethodAsync("OnFrameFailed", res.status, note);
    } else {
      const blob = await res.blob();
      const next = URL.createObjectURL(blob);
      const previous = s.objectUrl;
      img.src = next;
      s.objectUrl = next;
      revoke(previous);
      try {
        await img.decode();
      } catch {
      }
      const w = img.naturalWidth;
      const h = img.naturalHeight;
      const rw = img.clientWidth;
      const rh = img.clientHeight;
      if (w !== s.lastW || h !== s.lastH || rw !== s.lastRw || rh !== s.lastRh) {
        s.lastW = w;
        s.lastH = h;
        s.lastRw = rw;
        s.lastRh = rh;
        await s.dotnet.invokeMethodAsync("OnFrameSize", w, h, rw, rh);
      }
    }
  } catch (e) {
    stop = true;
    teardown(img);
    await s.dotnet.invokeMethodAsync("OnFrameFailed", 0, String(e?.message ?? e));
  }

  if (stop || s.stopped) return;
  s.timer = setTimeout(() => tick(img, s), s.intervalMs);
}

export function start(img, url, intervalMs, dotnet) {
  if (!img) return false;
  teardown(img);
  const s = {
    stopped: false,
    url,
    intervalMs: Math.max(Math.trunc(intervalMs), 100),
    dotnet,
    timer: 0,
    objectUrl: null,
    lastW: -1,
    lastH: -1,
    lastRw: -1,
    lastRh: -1
  };
  sessions.set(img, s);
  tick(img, s);
  return true;
}

export function stop(img) {
  teardown(img);
}

export async function once(img, url, dotnet) {
  if (!img) return false;
  const sep = url.indexOf("?") >= 0 ? "&" : "?";
  try {
    const res = await fetch(url + sep + "t=" + Date.now(), { credentials: "same-origin", cache: "no-store" });
    if (!res.ok) {
      await dotnet.invokeMethodAsync("OnFrameFailed", res.status, await failure(res));
      return false;
    }

    const blob = await res.blob();
    const next = URL.createObjectURL(blob);
    const s = sessions.get(img);
    const previous = s ? s.objectUrl : img.dataset.egiFrame;
    img.src = next;
    if (s) {
      s.objectUrl = next;
    } else {
      img.dataset.egiFrame = next;
    }
    revoke(previous);
    try {
      await img.decode();
    } catch {
    }
    await dotnet.invokeMethodAsync("OnFrameSize", img.naturalWidth, img.naturalHeight, img.clientWidth, img.clientHeight);
    return true;
  } catch (e) {
    await dotnet.invokeMethodAsync("OnFrameFailed", 0, String(e?.message ?? e));
    return false;
  }
}

export function measure(img) {
  if (!img) return [0, 0, 0, 0];
  return [img.naturalWidth, img.naturalHeight, img.clientWidth, img.clientHeight];
}

export function clear(img) {
  teardown(img);
  if (!img) return;
  revoke(img.dataset.egiFrame);
  delete img.dataset.egiFrame;
  img.removeAttribute("src");
}
