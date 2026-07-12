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

  // App.razor lets blazor.web.js auto-start (calling Blazor.start manually here fed it a
  // legacy single-circuit options shape it doesn't expect in Blazor Web App hybrid mode and
  // corrupted the StartCircuit handshake - 2026-07-11 outage). Observe the reconnect dialog instead.
  function observeDialog() {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) { setTimeout(observeDialog, 200); return; }
    const obs = new MutationObserver(() => {
      const cls = modal.className || '';
      if (/components-reconnect-(show|failed)/.test(cls) || modal.open) startPolling();
      else stopPolling();
    });
    obs.observe(modal, { attributes: true, attributeFilter: ['class', 'open'] });
  }

  captureLoadedVersion();
  observeDialog();
})();
