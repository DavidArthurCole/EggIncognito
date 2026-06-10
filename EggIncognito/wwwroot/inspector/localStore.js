// Viewer/anonymous local fallback: when a shared-DB save is refused (403), keep the endpoint in the
// browser so the user still has their data. Namespaced; survives reload. Best-effort overlay on the
// decoded response view when a matching path is shown.
const KEY = "egi:localEndpoints";

export function saveLocal(path, eid, responseJson) {
  const all = JSON.parse(localStorage.getItem(KEY) || "{}");
  all[`${path}::${eid || ""}`] = { path, eid: eid || null, responseJson, savedAt: Date.now() };
  localStorage.setItem(KEY, JSON.stringify(all));
}

export function getLocal(path, eid) {
  const all = JSON.parse(localStorage.getItem(KEY) || "{}");
  return all[`${path}::${eid || ""}`]?.responseJson ?? null;
}

export function listLocal() {
  return Object.values(JSON.parse(localStorage.getItem(KEY) || "{}"));
}
