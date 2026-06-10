// Import tab: POST a chosen HAR to /api/import/har as multipart, render the extractor's write tally.
// The endpoint is local-only (403 in hosted mode); nav.js already removes the Import link there, but
// guard the response anyway so a direct hit reports cleanly.
const fileInput = document.getElementById("harFile");
const overwrite = document.getElementById("overwrite");
const out = document.getElementById("importResult");
const btn = document.getElementById("importBtn");
const dropZone = document.getElementById("dropZone");
const fileName = document.getElementById("fileName");

// Reflect the chosen file: show its name + enable Import. One place so the picker and drag-drop agree.
function reflectFile() {
  const f = fileInput.files[0];
  fileName.textContent = f ? f.name : "No file selected.";
  fileName.classList.toggle("muted", !f);
  btn.disabled = !f;
}
fileInput.addEventListener("change", reflectFile);

// Drag-drop onto the zone: assign the dropped file to the input so the existing flow handles it.
if (dropZone) {
  dropZone.addEventListener("dragover", (e) => {
    if ([...e.dataTransfer.types].includes("Files")) { e.preventDefault(); dropZone.classList.add("dragging"); }
  });
  dropZone.addEventListener("dragleave", () => dropZone.classList.remove("dragging"));
  dropZone.addEventListener("drop", (e) => {
    e.preventDefault();
    dropZone.classList.remove("dragging");
    const f = [...(e.dataTransfer?.files ?? [])][0];
    if (f) { fileInput.files = e.dataTransfer.files; reflectFile(); }
  });
}

btn.addEventListener("click", async () => {
  const f = fileInput.files[0];
  if (!f) { out.textContent = "Choose a .har file first."; return; }

  const fd = new FormData();
  fd.append("file", f);
  btn.disabled = true;
  out.textContent = "Importing...";
  try {
    const res = await fetch(`/api/import/har?overwrite=${overwrite.checked}`, { method: "POST", body: fd });
    const data = await res.json().catch(() => ({}));
    out.textContent = res.ok
      ? `new=${data.wrote}  upd=${data.upd}  diff=${data.diff}  same=${data.same}  loss=${data.loss}  err=${data.err}`
      : (data.error || `HTTP ${res.status}`);
  } catch (e) {
    out.textContent = `Request failed: ${e}`;
  } finally {
    btn.disabled = false;
  }
});
