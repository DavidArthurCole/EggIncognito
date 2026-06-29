// Survives a server redeploy: when the SignalR circuit drops and the server returns at a DIFFERENT build
// version, reload the page so a fresh circuit attaches to the new build (the old circuit is gone and cannot
// be rejoined). A same-version return is a transient blip, so we do nothing and let Blazor's normal reconnect
// resume the existing circuit. Loaded once from App.razor after blazor.web.js.
//
// Pages that want to survive the reload (the playground) persist their own state to localStorage and restore
// it on load; this watcher only decides WHEN to reload.

(function () {
  const VERSION_URL = '/api/app/version';
  const POLL_MS = 2000;
  const MAX_POLLS = 90; // ~3 minutes of polling, then give up to the manual reconnect dialog
  let loadedVersion = null;
  let polling = false;
  let polls = 0;

  async function fetchVersion() {
    try {
      const r = await fetch(VERSION_URL, { cache: 'no-store' });
      if (!r.ok) return null;
      const j = await r.json();
      return j && typeof j.version === 'string' ? j.version : null;
    } catch {
      return null;
    }
  }

  async function captureLoadedVersion() {
    loadedVersion = await fetchVersion();
  }

  function startPolling() {
    if (polling) return;
    polling = true;
    polls = 0;
    tick();
  }

  function stopPolling() {
    polling = false;
  }

  async function tick() {
    if (!polling) return;
    polls += 1;
    const current = await fetchVersion();
    if (current) {
      // Server is reachable again. A different build => reload onto it. Same build => a blip; let Blazor's
      // own reconnect resume the circuit (stop polling, do not reload).
      if (loadedVersion && current !== loadedVersion) {
        location.reload();
        return;
      }
      stopPolling();
      return;
    }
    if (polls >= MAX_POLLS) { stopPolling(); return; }
    setTimeout(tick, POLL_MS);
  }

  // Hook the Blazor reconnection lifecycle. Blazor.start lets us provide a reconnectionHandler; we start
  // polling on the first reconnect attempt. autostart="false" on the blazor.web.js script makes this the
  // primary start path.
  function hookBlazor() {
    if (!window.Blazor || !window.Blazor.start) { setTimeout(hookBlazor, 50); return; }
    window.Blazor.start({
      circuit: {
        reconnectionHandler: {
          onConnectionDown: () => startPolling(),
          onConnectionUp: () => stopPolling(),
        },
      },
    }).catch(() => { /* Blazor may have auto-started; the observer below is the fallback */ });
  }

  // Fallback: if Blazor auto-started (so our start() is a no-op), observe the reconnect dialog's visibility,
  // which Blazor toggles via the components-reconnect-*-visible classes on #components-reconnect-modal.
  function observeDialog() {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) { setTimeout(observeDialog, 200); return; }
    const obs = new MutationObserver(() => {
      const cls = modal.className || '';
      if (/components-reconnect-(show|failed)/.test(cls) || modal.open) startPolling();
    });
    obs.observe(modal, { attributes: true, attributeFilter: ['class', 'open'] });
  }

  captureLoadedVersion();
  hookBlazor();
  observeDialog();
})();
