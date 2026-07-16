
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
    return rec;
  } catch {
    return null;
  }
}

export function clear() {
  try { localStorage.removeItem(KEY); } catch { /* ignore */ }
}
