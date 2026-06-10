// Lightweight client-side router for the EggIncognito app shell. Each tab (Inspector / Capture /
// Import / Admin / landing) is still a real, standalone HTML page - this just makes navigating BETWEEN
// them feel seamless: instead of a full browser reload (which flashes the whole UI), it fetches the
// target page, swaps the page body in place, and re-runs that page's scripts.
//
// Loaded as a CLASSIC script (not a module) at the END of <body> on every page, AFTER the page's own
// scripts, so by the time it runs the page is fully initialised.
//
// THE HARD PART - module re-execution + listener cleanup:
//   ES modules execute once per URL; re-importing the same URL does nothing. So to re-init a page we
//   append its <script> tags with a cache-busting query, forcing a fresh execution. But a fresh
//   execution that binds document/window listeners (or opens an SSE stream) would STACK on top of the
//   previous page's bindings, because those live outside the swapped DOM. To prevent leaks, pages
//   register teardown callbacks via window.__router.onCleanup(fn); the router runs them all before
//   each swap. Listeners bound to elements INSIDE <main> need no cleanup - they die with the old DOM.
//
// Falls back to a normal navigation on any error, on cross-origin links, and on modified clicks.

(function () {
  if (window.__router) return; // already installed (e.g. a double-include)

  const cleanups = [];
  window.__router = {
    // Register a teardown to run before the next in-app navigation (and on real unload). Returns the
    // fn so callers can also keep a handle. Pages SHOULD use this for any document/window listener,
    // timer, or open stream they create at module top-level.
    onCleanup(fn) { if (typeof fn === "function") cleanups.push(fn); return fn; },
  };

  function runCleanups() {
    while (cleanups.length) {
      const fn = cleanups.pop();
      try { fn(); } catch (e) { console.warn("router cleanup failed", e); }
    }
  }
  // Real unloads (closing the tab, hard nav) should also tear down (close SSE etc.).
  window.addEventListener("pagehide", runCleanups);

  // The set of in-app shell paths the router handles. A link to anything else is a normal navigation.
  const SHELL_PREFIXES = ["/", "/inspector", "/inspector/", "/capture", "/capture/", "/import", "/import/", "/admin", "/admin/"];
  function isShellPath(pathname) {
    return SHELL_PREFIXES.includes(pathname);
  }

  // Is this an in-app navigation we should intercept?
  function routableLink(a, e) {
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return false;
    if (!a || a.target === "_blank" || a.hasAttribute("download")) return false;
    const url = new URL(a.href, location.href);
    if (url.origin !== location.origin) return false;
    if (url.pathname === location.pathname) return false; // same page
    return isShellPath(url.pathname);
  }

  // Make the document's stylesheets EXACTLY match the target page's set. Compare by ABSOLUTE URL, not
  // the raw href: tabs use relative hrefs (e.g. "styles.css") that resolve to DIFFERENT files per path
  // (inspector/styles.css vs capture/styles.css), so a raw-href union both fails to load the new sheet
  // AND leaves the old one applied. We resolve the target's hrefs against the TARGET url, add any
  // missing, and remove any current page-specific sheet the target doesn't want.
  function syncStyles(targetDoc, targetUrl) {
    const want = [...targetDoc.querySelectorAll('head link[rel="stylesheet"]')]
      .map(l => new URL(l.getAttribute("href"), targetUrl).href);
    const wantSet = new Set(want);

    const current = [...document.querySelectorAll('head link[rel="stylesheet"]')];
    const haveSet = new Set(current.map(l => l.href)); // l.href is already absolute

    // Remove sheets the target page does not use (prevents a prior tab's CSS bleeding through).
    for (const l of current) if (!wantSet.has(l.href)) l.remove();

    // Add the missing ones (resolved absolute so the right per-path file loads), in order.
    const adds = [];
    for (const abs of want) {
      if (haveSet.has(abs)) continue;
      const link = document.createElement("link");
      link.rel = "stylesheet"; link.href = abs;
      document.head.appendChild(link);
      adds.push(linkLoaded(link));
    }

    // Inline <head><style> blocks: a <link> can't carry these, so copy the target page's into our head
    // (and drop any we previously copied). Tagged data-router-style so we only touch our own clones,
    // never a page's own original <style>. Prefer extracting page CSS to a real .css file, but this
    // keeps an inline-styled page from rendering unstyled after a router swap.
    document.head.querySelectorAll("style[data-router-style]").forEach((s) => s.remove());
    targetDoc.querySelectorAll("head style").forEach((s) => {
      const clone = document.createElement("style");
      clone.setAttribute("data-router-style", "");
      clone.textContent = s.textContent;
      document.head.appendChild(clone);
    });

    return Promise.all(adds);
  }
  function linkLoaded(link) {
    return new Promise((res) => {
      link.addEventListener("load", res, { once: true });
      link.addEventListener("error", res, { once: true });
      // Safety timeout so a slow/broken sheet can't hang navigation.
      setTimeout(res, 1500);
    });
  }

  let navSeq = 0;

  async function navigate(url, { push = true } = {}) {
    const target = url.pathname + url.search;
    let html;
    try {
      const r = await fetch(target, { headers: { "X-Router": "1" } });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      html = await r.text();
    } catch {
      location.href = target; // fall back to a real navigation
      return;
    }

    const doc = new DOMParser().parseFromString(html, "text/html");
    const newMain = doc.querySelector("main");
    if (!newMain) { location.href = target; return; } // not a shell page we can swap

    const seq = ++navSeq;
    runCleanups();                 // tear down the current page's global bindings/streams
    await syncStyles(doc, url);    // match the page's CSS set (add target's, drop stale) before showing
    if (seq !== navSeq) return;    // a newer navigation superseded this one

    // Swap the document title + the <main> (and any top-level <body> siblings that aren't the nav or
    // scripts - e.g. modals, toast containers). We replace the whole body content except scripts so the
    // page starts clean, then re-add the nav from the new doc.
    document.title = doc.title;

    // Remove current body children except <script> (we manage scripts explicitly below).
    [...document.body.children].forEach((c) => { if (c.tagName !== "SCRIPT") c.remove(); });
    // Insert the new body's non-script children in order; fade the new <main> in (router nav only).
    const frag = document.createDocumentFragment();
    [...doc.body.children].forEach((c) => {
      if (c.tagName === "SCRIPT") return;
      const node = document.importNode(c, true);
      if (node.tagName === "MAIN") node.classList.add("router-fade");
      frag.appendChild(node);
    });
    document.body.insertBefore(frag, document.body.firstChild);

    // Update the active nav link highlight (nav.js handles gating; we just fix .active for the new path).
    document.querySelectorAll(".app-nav a").forEach((a) => {
      const ap = new URL(a.href, location.href).pathname;
      a.classList.toggle("active", ap === url.pathname);
    });

    if (push) history.pushState({ router: true }, "", target);

    // Re-run the page's scripts. Module scripts are cache-busted so they re-execute; classic scripts
    // (nav.js, this router) are skipped - nav.js re-runs via re-add below, the router persists.
    const scripts = [...doc.body.querySelectorAll("script")];
    for (const s of scripts) {
      const src = s.getAttribute("src");
      // Skip the router itself (already installed + persistent).
      if (src && src.endsWith("/app-router.js")) continue;
      const el = document.createElement("script");
      if (s.type) el.type = s.type;
      if (src) {
        // Resolve against the TARGET url (relative src like "app.js" differs per tab), then cache-bust
        // so the ES module re-executes on every navigation rather than returning the cached instance.
        const abs = new URL(src, url).href;
        el.src = `${abs}${abs.includes("?") ? "&" : "?"}_n=${seq}`;
      } else {
        el.textContent = s.textContent;
      }
      document.body.appendChild(el);
      // Load sequentially-ish for src scripts so order is preserved (nav.js before page modules).
      if (src) await scriptLoaded(el);
    }

    window.scrollTo(0, 0);
  }
  function scriptLoaded(el) {
    return new Promise((res) => {
      el.addEventListener("load", res, { once: true });
      el.addEventListener("error", res, { once: true });
      setTimeout(res, 4000);
    });
  }

  // Intercept clicks on in-app links.
  document.addEventListener("click", (e) => {
    const a = e.target.closest?.("a[href]");
    if (!a || !routableLink(a, e)) return;
    e.preventDefault();
    navigate(new URL(a.href, location.href));
  });

  // Back/forward.
  window.addEventListener("popstate", () => {
    navigate(new URL(location.href), { push: false });
  });
})();
