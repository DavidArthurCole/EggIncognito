// A single localStorage slot for the playground's in-progress design, so a redeploy reload (or a crash) can
// restore unsaved work. SEPARATE from the named DB designs (those are explicit Save/Load). The record carries
// the design JSON, the app version it was saved under, and a savedAt timestamp; the C# side decides recency.

const KEY = 'playground.autosave';

export function save(json, version, savedAtMs) {
  try {
    localStorage.setItem(KEY, JSON.stringify({ json, version: version || '', savedAt: savedAtMs || 0 }));
  } catch { /* quota / private mode: skip */ }
}

export function load() {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const rec = JSON.parse(raw);
    if (!rec || typeof rec.json !== 'string') return null;
    return rec; // { json, version, savedAt }
  } catch {
    return null;
  }
}

export function clear() {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
}
