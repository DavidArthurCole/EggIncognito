// Admin SPA: lists users + DB contributions and lets an admin change roles / delete contributions.
// All data comes from /api/admin/* + /api/db/* (admin-gated server-side; the page also hides itself
// for non-admins).
const log = (m) => { document.getElementById("log").textContent = m; };

(async () => {
  const mode = await fetch("/api/app/mode").then(r => r.json()).catch(() => ({}));
  const isAdmin = mode.user && mode.user.role === "admin";
  document.getElementById("denied").hidden = isAdmin;
  document.getElementById("panel").hidden = !isAdmin;
  if (!isAdmin) return;

  const ROLES = ["viewer", "contributor", "admin"];

  async function loadUsers() {
    const users = await fetch("/api/admin/users").then(r => r.json());
    document.getElementById("users").innerHTML = users.map(u => {
      const opts = ROLES.map(r => `<option value="${r}" ${r === u.role ? "selected" : ""}>${r}</option>`).join("");
      return `<tr><td>${u.username}</td><td>${u.discordId}</td>` +
        `<td><select data-id="${u.discordId}">${opts}</select></td>` +
        `<td>${new Date(u.lastLoginAt).toLocaleString()}</td></tr>`;
    }).join("");
    document.querySelectorAll("#users select").forEach(sel =>
      sel.addEventListener("change", async () => {
        const res = await fetch(`/api/admin/users/${sel.dataset.id}/role`, {
          method: "POST", headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ role: sel.value }),
        });
        const d = await res.json().catch(() => ({}));
        log(res.ok ? `set ${sel.dataset.id} -> ${d.role}` : (d.error || `HTTP ${res.status}`));
      }));
  }

  async function loadContributions() {
    const eps = await fetch("/api/db/endpoints").then(r => r.json());
    document.getElementById("endpoints").innerHTML = eps.map(e =>
      `<tr><td>${e.path}</td><td>${e.eid ?? ""}</td><td>${e.responseType}</td>` +
      `<td><button class="danger" data-del-ep="${e.id ?? ""}">delete</button></td></tr>`).join("");
    const routes = await fetch("/api/db/routes").then(r => r.json());
    document.getElementById("routes").innerHTML = routes.map(r =>
      `<tr><td>${r.path}</td><td>${r.responseType ?? ""}</td>` +
      `<td><button class="danger" data-del-route="${r.id ?? ""}">delete</button></td></tr>`).join("");
    document.querySelectorAll("[data-del-ep]").forEach(b => b.addEventListener("click", () => del("endpoint", b.dataset.delEp)));
    document.querySelectorAll("[data-del-route]").forEach(b => b.addEventListener("click", () => del("route", b.dataset.delRoute)));
  }

  async function del(kind, id) {
    if (!id) { log("this row has no id"); return; }
    const res = await fetch(`/api/admin/${kind}/${id}`, { method: "DELETE" });
    log(res.ok ? `deleted ${kind} ${id}` : `HTTP ${res.status}`);
    loadContributions();
  }

  await loadUsers();
  await loadContributions();
})();
