// Loads SyncKit's embedded popup login widget script on demand and drives it. Returns the redeemed
// login code (or throws) so the caller can POST it to /auth/redeem-code server-side.

let scriptPromise = null;

function loadScript(identityHostUrl) {
  if (window.SyncKitAuth) return Promise.resolve();
  if (scriptPromise) return scriptPromise;
  scriptPromise = new Promise((resolve, reject) => {
    const el = document.createElement("script");
    el.src = identityHostUrl.replace(/\/$/, "") + "/synckit-login.js";
    el.onload = () => resolve();
    el.onerror = () => reject(new Error("script_load_failed"));
    document.head.appendChild(el);
  });
  return scriptPromise;
}

// Runs the popup flow, then redeems the code against this app's own backend. Returns true on
// success; throws (popup_blocked / popup_closed / script_load_failed / redeem_failed) otherwise.
export async function login(identityHostUrl) {
  await loadScript(identityHostUrl);
  const { code } = await window.SyncKitAuth.login(identityHostUrl);
  const resp = await fetch("/auth/redeem-code", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code }),
  });
  if (!resp.ok) throw new Error("redeem_failed");
  return true;
}

// Runs the inline-iframe flow into containerEl, then redeems the code exactly like login() does.
// Returns true on success; throws (script_load_failed / redeem_failed / whatever
// SyncKitAuth.loginInline itself throws) otherwise.
export async function loginInline(identityHostUrl, containerEl) {
  await loadScript(identityHostUrl);
  const { code } = await window.SyncKitAuth.loginInline(identityHostUrl, containerEl);
  const resp = await fetch("/auth/redeem-code", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code }),
  });
  if (!resp.ok) throw new Error("redeem_failed");
  return true;
}
