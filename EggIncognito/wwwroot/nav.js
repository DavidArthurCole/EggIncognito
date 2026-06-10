// Shared nav gating. Every page's <nav class="app-nav"> includes the Capture + Import links tagged
// with .nav-gated-capture / .nav-gated-write. They start hidden (nav.css) so they never flash for
// users who shouldn't see them; we ask /api/app/mode which capabilities this instance has and REVEAL
// the allowed ones (add .nav-show). In a hosted (public) deploy capture + writes are off, so those
// links simply stay hidden. Any save/update control can opt in by carrying one of these classes.
//
// To avoid the reveal "popping in" on every load, we apply a CACHED mode (localStorage) SYNCHRONOUSLY
// first, then fetch the live mode and reconcile. On repeat visits the links are already correct before
// paint, so there's no flash; the fetch only corrects a stale cache (e.g. after a login/logout).
(() => {
  const CACHE_KEY = "egi:appMode";

  function applyMode(mode) {
    document.querySelectorAll(".nav-gated-capture").forEach(el => el.classList.toggle("nav-show", !!mode.canCapture));
    document.querySelectorAll(".nav-gated-write").forEach(el => el.classList.toggle("nav-show", !!mode.canWrite));
    document.querySelectorAll(".nav-gated-admin").forEach(el => el.classList.toggle("nav-show", mode.user?.role === "admin"));

    const authNav = document.getElementById("authNav");
    if (!authNav || !mode.authEnabled) return;
    if (mode.user) {
      const avatar = mode.user.avatar
        ? `<img class="auth-avatar" src="https://cdn.discordapp.com/avatars/${mode.user.discordId}/${mode.user.avatar}.png" alt="">`
        : "";
      authNav.innerHTML =
        `${avatar}<span class="auth-name">${mode.user.username}</span>` +
        `<button type="button" class="auth-btn" id="logoutBtn">log out</button>`;
      document.getElementById("logoutBtn").addEventListener("click", async () => {
        await fetch("/logout", { method: "POST" });
        try { localStorage.removeItem(CACHE_KEY); } catch { /* ignore */ }
        location.reload();
      });
    } else {
      authNav.innerHTML = `<a class="auth-btn" href="/login?returnUrl=${encodeURIComponent(location.pathname)}">log in with Discord</a>`;
    }
  }

  // 1) Optimistically apply the last-seen mode so repeat visits don't flash the gated links in.
  try {
    const cached = JSON.parse(localStorage.getItem(CACHE_KEY) || "null");
    if (cached && typeof cached === "object") applyMode(cached);
  } catch { /* ignore */ }

  // 2) Fetch the live mode and reconcile (+ refresh the cache).
  fetch("/api/app/mode")
    .then(r => r.json())
    .then(mode => {
      applyMode(mode);
      try { localStorage.setItem(CACHE_KEY, JSON.stringify(mode)); } catch { /* ignore */ }
    })
    .catch(() => {
      // Mode endpoint unreachable (e.g. static-only local dev): fail open for capture + write links,
      // but never reveal admin without a confirmed admin role.
      document.querySelectorAll(".nav-gated-capture, .nav-gated-write").forEach(el => el.classList.add("nav-show"));
    });
})();
