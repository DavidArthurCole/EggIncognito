const refs = new Set();
const pathRefs = new Set();
let wired = false;

function notify() {
  for (const ref of refs) ref.invokeMethodAsync("OnHashChanged", location.hash || "");
}

function notifyPath() {
  for (const entry of pathRefs) entry.ref.invokeMethodAsync("OnPathChanged", location.pathname);
}

function notifyAll() {
  notifyPath();
  notify();
}

function owned(pathname) {
  for (const entry of pathRefs) {
    const prefix = entry.prefix.endsWith("/") ? entry.prefix.slice(0, -1) : entry.prefix;
    if (pathname === prefix || pathname.startsWith(prefix + "/")) return true;
  }

  return false;
}

function internal(a) {
  if (!a || a.hasAttribute("download")) return null;
  const target = a.getAttribute("target");
  if (target && target !== "_self") return null;
  let url;
  try {
    url = new URL(a.href, location.href);
  } catch {
    return null;
  }

  return url.origin === location.origin ? url : null;
}

function onClick(e) {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const url = internal(e.target.closest?.("a[href]"));
  if (url === null) return;
  const samePage = url.pathname === location.pathname && url.search === location.search;
  if (!samePage && !owned(url.pathname)) return;
  e.preventDefault();
  e.stopPropagation();
  if (!samePage || url.hash !== location.hash) history.pushState(null, "", url.pathname + url.search + url.hash);
  if (samePage) notify();
  else notifyAll();
}

function wire() {
  if (wired) return;
  window.addEventListener("hashchange", notify);
  window.addEventListener("popstate", notifyAll);
  document.addEventListener("click", onClick, true);
  wired = true;
}

function unwire() {
  if (!wired || refs.size > 0 || pathRefs.size > 0) return;
  window.removeEventListener("hashchange", notify);
  window.removeEventListener("popstate", notifyAll);
  document.removeEventListener("click", onClick, true);
  wired = false;
}

export function read() {
  return location.hash || "";
}

export function path() {
  return location.pathname;
}

export function write(hash) {
  const url = location.pathname + location.search + (hash ? "#" + hash : "");
  history.replaceState(null, "", url);
}

export function replacePath(path) {
  history.replaceState(null, "", path + location.search + location.hash);
}

export function pushPath(path) {
  history.pushState(null, "", path + location.search + location.hash);
}

export function push(hash) {
  const url = location.pathname + location.search + (hash ? "#" + hash : "");
  history.pushState(null, "", url);
}

export function listen(ref) {
  refs.add(ref);
  wire();
  return {
    dispose: () => {
      refs.delete(ref);
      unwire();
    }
  };
}

export function listenPath(ref, prefix) {
  const entry = { ref, prefix };
  pathRefs.add(entry);
  wire();
  return {
    dispose: () => {
      pathRefs.delete(entry);
      unwire();
    }
  };
}
