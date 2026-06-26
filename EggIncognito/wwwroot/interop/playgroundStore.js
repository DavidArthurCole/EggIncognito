// Browser-side storage for saved playground "configurations": the full scene state (active widgets, their
// selections, per-group offsets, env preset, hab, animation, background) under a user-given name. Lives in
// this browser's localStorage, never server-side (like inspectorStore.js). One key holds a name -> config map.

const KEY = "playground.configs";

function loadMap() {
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) || "{}");
    return raw && typeof raw === "object" ? raw : {};
  } catch { return {}; }
}

function saveMap(map) {
  try { localStorage.setItem(KEY, JSON.stringify(map)); } catch { /* ignore quota */ }
}

// The saved config names, sorted.
export function listConfigs() {
  return Object.keys(loadMap()).sort((a, b) => a.localeCompare(b));
}

// Save (or overwrite) a config under name. configJson is a JSON string of the scene state. Returns the names.
export function saveConfig(name, configJson) {
  const n = String(name || "").trim();
  if (!n) return listConfigs();
  const map = loadMap();
  map[n] = configJson;
  saveMap(map);
  return listConfigs();
}

// The stored config JSON string for a name, or null.
export function getConfig(name) {
  const map = loadMap();
  return Object.prototype.hasOwnProperty.call(map, name) ? map[name] : null;
}

export function deleteConfig(name) {
  const map = loadMap();
  delete map[name];
  saveMap(map);
  return listConfigs();
}
