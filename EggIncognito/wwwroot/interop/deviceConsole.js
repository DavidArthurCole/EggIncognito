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
