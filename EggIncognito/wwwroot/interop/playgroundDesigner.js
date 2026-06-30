// The environment-designer overlay for the playground: a three.js TransformControls gizmo for free-placing
// elements, selection, and two-way transform sync with the Blazor side. It drives the SAME scene the engine
// (playground.js) owns, via that module's accessors, so there is one renderer/scene/camera. The gizmo edits a
// group's transform; every change is pushed back to .NET so the numeric inspector fields stay in sync, and
// .NET can push edits the other way via applyTransform.

// The engine (playground.js) publishes its live accessors on window.__pgEngine. We read THAT instance rather
// than importing playground.js here: the Razor side loads the engine with a ?v= cache-bust query, so a static
// import would resolve to a DIFFERENT (uninitialized) module instance whose renderer is undefined.
function engine() { return globalThis.__pgEngine; }

const TC_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/TransformControls.js';
const THREE_URL = 'https://esm.sh/three@0.169.0';

let THREE, TransformControls, gizmo, dotnet, selectedId = null, suppress = false;
let raycaster, pointer, domEl, onPointerDown, onPointerUp, downXY = null, dragging = false;
let onKeyDown;
let proxy, centerOffset;

export async function initDesigner(dotnetRef) {
  dotnet = dotnetRef;
  THREE = await import(THREE_URL);
  ({ TransformControls } = await import(TC_URL));

  const e = engine();
  if (!e || !e.renderer()) throw new Error('playground engine not initialized');
  const cam = e.camera(), dom = e.renderer().domElement, controls = e.controls();
  domEl = dom;
  gizmo = new TransformControls(cam, dom);
  gizmo.setMode('translate');

  // The gizmo attaches to a PROXY object sitting at the selected element's visual center (not the group root,
  // which is an off-origin authored corner for self-placing meshes). On a drag we map the proxy's motion back
  // to the group's base. centerOffset = the mesh-center minus the placement point (= bbox center * scale).
  proxy = new THREE.Object3D();
  e.scene().add(proxy);
  centerOffset = new THREE.Vector3(0, 0, 0);

  // Dragging the gizmo must not also orbit the camera. On the drag START edge, snapshot history once so a whole
  // drag is a single undo step (objectChange fires per frame; we do NOT push there).
  gizmo.addEventListener('dragging-changed', ev => {
    controls.enabled = !ev.value;
    dragging = ev.value;
    if (ev.value && dotnet) dotnet.invokeMethodAsync('OnGizmoDragStart');
    // On drag END: run the authoritative placement solver (floor clamp + grid snap + overlap) and snap the
    // element to the corrected spot. Per-frame objectChange only previews; the legal resolve happens on drop.
    if (!ev.value) solvePlacement();
  });

  // Click-to-select: raycast from a click that was NOT a drag (orbit) onto the element group roots. Hit ->
  // select that element + notify .NET; miss -> deselect.
  raycaster = new THREE.Raycaster();
  pointer = new THREE.Vector2();
  onPointerDown = ev => { downXY = [ev.clientX, ev.clientY]; };
  onPointerUp = ev => {
    if (dragging || !downXY) { downXY = null; return; }
    const moved = Math.abs(ev.clientX - downXY[0]) + Math.abs(ev.clientY - downXY[1]);
    downXY = null;
    if (moved > 5) return; // it was an orbit drag, not a click
    const rect = domEl.getBoundingClientRect();
    pointer.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    pointer.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(pointer, cam);
    const hits = raycaster.intersectObjects(e.groupRoots(), true);
    const id = hits.length ? e.groupIdOf(hits[0].object) : null;
    if (id) { selectElement(id); dotnet.invokeMethodAsync('OnPickElement', id); }
    else { deselect(); dotnet.invokeMethodAsync('OnPickElement', null); }
  };
  dom.addEventListener('pointerdown', onPointerDown);
  dom.addEventListener('pointerup', onPointerUp);

  // Arrow keys nudge the selected element along the GROUND, relative to the current view angle: Up pushes it
  // away from the camera, Down toward, Left/Right screen-left/right. Shift = a bigger step. Ignored while
  // typing in an input so the inspector fields still work.
  onKeyDown = ev => {
    const t = ev.target;
    const typing = t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
    // Undo / redo: ctrl+z, ctrl+y (and ctrl+shift+z). Works with nothing selected. Ignored while typing.
    if ((ev.ctrlKey || ev.metaKey) && !typing) {
      const key = ev.key.toLowerCase();
      if (key === 'z' && !ev.shiftKey) { ev.preventDefault(); dotnet?.invokeMethodAsync('OnUndo'); return; }
      if (key === 'y' || (key === 'z' && ev.shiftKey)) { ev.preventDefault(); dotnet?.invokeMethodAsync('OnRedo'); return; }
    }
    if (!selectedId) return;
    const k = ev.key;
    if (k !== 'ArrowUp' && k !== 'ArrowDown' && k !== 'ArrowLeft' && k !== 'ArrowRight') return;
    if (typing) return;
    ev.preventDefault();
    nudge(k, ev.shiftKey ? 2.0 : 0.5);
  };
  window.addEventListener('keydown', onKeyDown);

  // On a gizmo drag: write the new transform into the group's base immediately (so the anim loop does not
  // revert it next frame), then notify .NET so the inspector fields follow.
  gizmo.addEventListener('objectChange', () => {
    if (suppress || !selectedId || !gizmo.object) return;
    const o = gizmo.object; // the proxy at the mesh center; map back to the group base by removing the offset.
    const pos = [o.position.x - centerOffset.x, o.position.y - centerOffset.y, o.position.z - centerOffset.z];
    const rotDeg = [deg(o.rotation.x), deg(o.rotation.y), deg(o.rotation.z)];
    e.setGroupTransform(selectedId, pos, rotDeg, o.scale.x);
    dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, rotDeg, o.scale.x);
  });

  // r0.169: TransformControls is no longer an Object3D. Its rendered + interactive form is the helper; adding
  // the controls object itself silently does nothing (that left the old gizmo invisible, so Move/Rotate looked
  // dead).
  e.scene().add(gizmo.getHelper());
  // expose the gizmo-hide toggle for the recorder (which cannot import this module without forking it).
  globalThis.__pgDesigner = { setGizmoVisible };
}

// Hide/show the gizmo around a capture so it does not appear in the recorded frames. Keeps the attachment so
// selection is unchanged after.
export function setGizmoVisible(on) {
  if (gizmo) gizmo.getHelper().visible = !!on;
}

function deg(rad) { return rad * 180 / Math.PI; }
function rad(d) { return d * Math.PI / 180; }

// Moves the selected element by `step` along the ground, in the screen direction of the pressed arrow. The
// camera's forward + right vectors are flattened onto the XZ plane so Up always means "deeper into the scene"
// from the current view, regardless of orbit angle.
function nudge(key, step) {
  const e = engine();
  if (!e || !selectedId) return;
  const base = e.getGroupBase(selectedId);
  if (!base) return;
  // One undo step per nudge keypress (nudge does not fire dragging-changed, so push here).
  if (dotnet) dotnet.invokeMethodAsync('OnGizmoDragStart');
  const cam = e.camera();

  // forward = where the camera looks, flattened to the ground. right = the camera's own +X axis (screen
  // right), also flattened, so left/right match what the user sees regardless of orbit.
  const f = new THREE.Vector3();
  cam.getWorldDirection(f);
  f.y = 0;
  if (f.lengthSq() < 1e-6) return;
  f.normalize();
  const right = new THREE.Vector3().setFromMatrixColumn(cam.matrixWorld, 0); // camera local +X = screen right
  right.y = 0;
  right.normalize();

  let dx = 0, dz = 0;
  switch (key) {
    case 'ArrowUp': dx = f.x * step; dz = f.z * step; break; // away from camera
    case 'ArrowDown': dx = -f.x * step; dz = -f.z * step; break; // toward camera
    case 'ArrowRight': dx = right.x * step; dz = right.z * step; break;
    case 'ArrowLeft': dx = -right.x * step; dz = -right.z * step; break;
  }

  const pos = [base.pos[0] + dx, base.pos[1], base.pos[2] + dz];
  e.setGroupTransform(selectedId, pos, base.rotDeg, base.scale);
  // the gizmo is attached to the group root, which the anim loop repositions from the new base, so it follows.
  dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, base.rotDeg, base.scale);
  // re-solve so a nudge into a neighbor / the floor is corrected just like a drag.
  solvePlacement();
}

// Run the C# placement solver on the selected element's current transform: gather its local footprint + every
// other element's world rect, ask .NET to correct it (floor clamp + grid snap + overlap push), then snap the
// element to the legal spot. .NET returns [x, y, z, adjusted]. No-op if the element has no footprint.
async function solvePlacement() {
  const e = engine();
  if (!e || !selectedId || !dotnet) return;
  const base = e.getGroupBase(selectedId);
  const foot = e.getGroupFootprint(selectedId);
  if (!base || !foot) return;
  const others = e.getOtherFootprints(selectedId);
  let res;
  try {
    res = await dotnet.invokeMethodAsync('OnPlaceSolve', selectedId,
      base.pos, base.rotDeg, base.scale, foot, others);
  } catch { return; }
  if (!res || res.length < 3) return;
  const pos = [res[0], res[1], res[2]];
  suppress = true;
  e.setGroupTransform(selectedId, pos, base.rotDeg, base.scale);
  if (selectedId) selectElement(selectedId); // re-sync the gizmo proxy to the corrected spot
  suppress = false;
  // keep the inspector fields + autosave in step with the corrected transform.
  dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, base.rotDeg, base.scale);
}

// Live gizmo snapping while dragging: translate snaps to the grid cell, rotate to 15deg. size 0 = free.
export function setGridSnap(cellSize) {
  if (!gizmo) return;
  const s = Number(cellSize) > 0 ? Number(cellSize) : null;
  gizmo.setTranslationSnap(s);
  gizmo.setRotationSnap(s ? rad(15) : null);
}

export function selectElement(id) {
  const e = engine();
  const root = e?.getGroupRoot(id);
  if (!root) { deselect(); return; }
  if (selectedId && selectedId !== id) e.setSelectionOutline(selectedId, false);
  selectedId = id;
  // Place the proxy at the element's visual center (so the gizmo lands on the building, not the group origin),
  // mirroring its base rotation + scale. centerOffset = centerWorld - basePos, removed on drag to recover base.
  const base = e.getGroupBase(id) || { pos: [0, 0, 0], rotDeg: [0, 0, 0], scale: 1 };
  const cw = e.getGroupCenterWorld(id) || [base.pos[0], base.pos[1], base.pos[2]];
  centerOffset.set(cw[0] - base.pos[0], cw[1] - base.pos[1], cw[2] - base.pos[2]);
  proxy.position.set(cw[0], cw[1], cw[2]);
  proxy.rotation.set(rad(base.rotDeg[0]), rad(base.rotDeg[1]), rad(base.rotDeg[2]));
  proxy.scale.setScalar(base.scale || 1);
  gizmo.attach(proxy);
  e.setSelectionOutline(id, true);
}

export function deselect() {
  if (selectedId) engine()?.setSelectionOutline(selectedId, false);
  selectedId = null;
  if (gizmo) gizmo.detach();
}

export function setGizmoMode(mode) {
  if (gizmo && (mode === 'translate' || mode === 'rotate')) gizmo.setMode(mode);
}

// Apply a transform from .NET (numeric fields). Sets the group's BASE (via the engine) so the per-frame anim
// loop composes spin on top of it instead of reverting the gizmo. suppress stops the objectChange echo.
export function applyTransform(id, pos, rotDeg, scale) {
  const e = engine();
  if (!e) return;
  suppress = true;
  e.setGroupTransform(id, pos, rotDeg, scale);
  // re-sync the gizmo proxy to the new transform so it tracks the building after a numeric-field edit.
  if (id === selectedId) selectElement(id);
  suppress = false;
}

export function disposeDesigner() {
  if (selectedId) engine()?.setSelectionOutline(selectedId, false);
  if (domEl) {
    if (onPointerDown) domEl.removeEventListener('pointerdown', onPointerDown);
    if (onPointerUp) domEl.removeEventListener('pointerup', onPointerUp);
  }
  if (onKeyDown) { window.removeEventListener('keydown', onKeyDown); onKeyDown = null; }
  if (gizmo) {
    gizmo.detach();
    engine()?.scene()?.remove(gizmo.getHelper());
    gizmo.dispose?.();
    gizmo = null;
  }
  if (proxy) { engine()?.scene()?.remove(proxy); proxy = null; }
  selectedId = null; domEl = null; dotnet = null;
  globalThis.__pgDesigner = null;
}
