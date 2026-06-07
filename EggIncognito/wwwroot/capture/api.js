// Thin backend call wrappers for /api/capture/*. Orchestration (what to render with the results)
// lives in the consuming modules; this file only does the fetch + JSON plumbing.

export async function postJson(url, body) {
  try {
    const res = await fetch(url, {
      method: "POST",
      headers: body ? { "Content-Type": "application/json" } : {},
      body: body ? JSON.stringify(body) : undefined,
    });
    return { ok: res.ok, status: res.status, data: await res.json().catch(() => ({})) };
  } catch (e) {
    console.warn("POST failed", url, e);
    return { ok: false, status: 0, data: {} };
  }
}

// GET helper returning parsed JSON, or null on any failure.
export async function getJson(url) {
  try {
    const res = await fetch(url);
    if (!res.ok) return null;
    return await res.json();
  } catch (e) {
    console.warn("GET failed", url, e);
    return null;
  }
}

// Runtime capture lifecycle control (the proxy is off by default; toggled from the Capture tab).
export const startCapture = () => postJson("/api/capture/start");
export const stopCapture = () => postJson("/api/capture/stop");
export const captureStatus = () => getJson("/api/capture/status");
