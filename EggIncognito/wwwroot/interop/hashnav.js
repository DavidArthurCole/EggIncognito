const refs = new Set();
let wired = false;

function notify() {
  for (const ref of refs) ref.invokeMethodAsync("OnHashChanged", location.hash || "");
}

function sameDocument(a) {
  if (!a || a.hasAttribute("download")) return null;
  const target = a.getAttribute("target");
  if (target && target !== "_self") return null;
  let url;
  try {
    url = new URL(a.href, location.href);
  } catch {
    return null;
  }

  if (url.origin !== location.origin) return null;
  return url.pathname === location.pathname && url.search === location.search ? url : null;
}

function onClick(e) {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const url = sameDocument(e.target.closest?.("a[href]"));
  if (url === null) return;
  e.preventDefault();
  e.stopPropagation();
  if (url.hash !== location.hash) history.pushState(null, "", url.pathname + url.search + url.hash);
  notify();
}

function wire() {
  if (wired) return;
  window.addEventListener("hashchange", notify);
  window.addEventListener("popstate", notify);
  document.addEventListener("click", onClick, true);
  wired = true;
}

function unwire() {
  if (!wired || refs.size > 0) return;
  window.removeEventListener("hashchange", notify);
  window.removeEventListener("popstate", notify);
  document.removeEventListener("click", onClick, true);
  wired = false;
}

export function read() {
  return location.hash || "";
}

export function write(hash) {
  const url = location.pathname + location.search + (hash ? "#" + hash : "");
  history.replaceState(null, "", url);
}

export function replacePath(path, hash) {
  const url = path + location.search + (hash ? "#" + hash : "");
  history.replaceState(null, "", url);
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
