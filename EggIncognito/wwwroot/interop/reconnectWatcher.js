// Survives a server redeploy: when the SignalR circuit drops and the server returns at a different build
// version, reload the page so a fresh circuit attaches to the new build. A same-version return is a
// transient blip, left to Blazor's normal reconnect. Loaded once from App.razor after blazor.web.js.

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
      // A different build reloads onto it; same build is a blip, let Blazor's own reconnect resume.
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

  // Blazor.start provides a reconnectionHandler; polling starts on the first reconnect attempt.
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

  // Fallback for an already-auto-started Blazor: observe the reconnect dialog's visibility classes.
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
