// The environment-designer overlay for the playground: a three.js TransformControls gizmo for free-placing
// elements, selection, and two-way transform sync with the Blazor side. Drives the same scene the engine
// (playground.js) owns via that module's accessors.

// Reads the engine's live accessors off window.__pgEngine rather than importing playground.js: the Razor
// side loads the engine with a ?v= cache-bust query, so a static import would resolve to a different
// (uninitialized) module instance.
function engine() { return globalThis.__pgEngine; }

const TC_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/TransformControls.js';
const THREE_URL = 'https://esm.sh/three@0.169.0';

let THREE, TransformControls, gizmo, dotnet, selectedId = null, suppress = false;
let raycaster, pointer, domEl, onPointerDown, onPointerUp, downXY = null, dragging = false;
let onKeyDown;
let proxy, centerOffset;
let dragStartPos = null;

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

  // The gizmo attaches to a proxy object at the selected element's visual center rather than the group root,
  // which is an off-origin authored corner for self-placing meshes.
  proxy = new THREE.Object3D();
  e.scene().add(proxy);
  centerOffset = new THREE.Vector3(0, 0, 0);

  // Dragging the gizmo must not also orbit the camera. objectChange fires per frame, so history is snapshotted
  // once on the drag-start edge instead, keeping a whole drag as a single undo step.
  gizmo.addEventListener('dragging-changed', ev => {
    controls.enabled = !ev.value;
    dragging = ev.value;
    if (ev.value) {
      if (dotnet) dotnet.invokeMethodAsync('OnGizmoDragStart');
      const base = e.getGroupBase(selectedId);
      if (base) dragStartPos = [...base.pos];
    } else {
      e.clearCellHighlight?.();
      commitGridDrop();
    }
  });

  // Click-to-select: raycast from a click that was not a drag onto the element group roots.
  raycaster = new THREE.Raycaster();
  pointer = new THREE.Vector2();
  onPointerDown = ev => { downXY = [ev.clientX, ev.clientY]; };
  onPointerUp = ev => {
    if (dragging || !downXY) { downXY = null; return; }
    const moved = Math.abs(ev.clientX - downXY[0]) + Math.abs(ev.clientY - downXY[1]);
    downXY = null;
    if (moved > 5) return;
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

  // Arrow keys nudge the selected element along the ground, relative to the current view angle. Ignored
  // while typing in an input.
  onKeyDown = ev => {
    const t = ev.target;
    const typing = t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
    // Undo/redo works with nothing selected.
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

  // Writes the new transform into the group's base immediately, so the anim loop does not revert it next
  // frame, then notifies .NET so the inspector fields follow.
  gizmo.addEventListener('objectChange', () => {
    if (suppress || !selectedId || !gizmo.object) return;
    const o = gizmo.object;
    const pos = [o.position.x - centerOffset.x, o.position.y - centerOffset.y, o.position.z - centerOffset.z];
    const rotDeg = [deg(o.rotation.x), deg(o.rotation.y), deg(o.rotation.z)];
    e.setGroupTransform(selectedId, pos, rotDeg, o.scale.x);
    dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, rotDeg, o.scale.x);
    if (e.gridCellSize && e.gridCellSize() > 0) {
      const snap = e.gridSnapBlock(selectedId, pos[0], pos[2]);
      e.highlightCells(snap.cells, snap.valid);
    }
  });

  // r0.169: TransformControls is no longer an Object3D; its rendered/interactive form is the helper.
  e.scene().add(gizmo.getHelper());
  globalThis.__pgDesigner = { setGizmoVisible };
}

// Hide/show the gizmo around a capture so it does not appear in the recorded frames.
export function setGizmoVisible(on) {
  if (gizmo) gizmo.getHelper().visible = !!on;
}

function deg(rad) { return rad * 180 / Math.PI; }
function rad(d) { return d * Math.PI / 180; }

// Moves the selected element by `step` along the ground, in the screen direction of the pressed arrow: the
// camera's forward/right vectors are flattened onto the XZ plane so Up always means deeper into the scene.
function nudge(key, step) {
  const e = engine();
  if (!e || !selectedId) return;
  const base = e.getGroupBase(selectedId);
  if (!base) return;
  if (dotnet) dotnet.invokeMethodAsync('OnGizmoDragStart');
  const cam = e.camera();

  const f = new THREE.Vector3();
  cam.getWorldDirection(f);
  f.y = 0;
  if (f.lengthSq() < 1e-6) return;
  f.normalize();
  const right = new THREE.Vector3().setFromMatrixColumn(cam.matrixWorld, 0);
  right.y = 0;
  right.normalize();

  let dx = 0, dz = 0;
  switch (key) {
    case 'ArrowUp': dx = f.x * step; dz = f.z * step; break;
    case 'ArrowDown': dx = -f.x * step; dz = -f.z * step; break;
    case 'ArrowRight': dx = right.x * step; dz = right.z * step; break;
    case 'ArrowLeft': dx = -right.x * step; dz = -right.z * step; break;
  }

  const pos = [base.pos[0] + dx, base.pos[1], base.pos[2] + dz];
  e.setGroupTransform(selectedId, pos, base.rotDeg, base.scale);
  dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, base.rotDeg, base.scale);
  dragStartPos = [...base.pos];
  commitGridDrop();
}

// Snaps the selected element's block to the grid and accepts the drop only if every target cell is free,
// otherwise reverts to dragStartPos. Replaces the overlap-push solver for grid mode.
function commitGridDrop() {
  const e = engine();
  if (!e || !selectedId) return;
  const base = e.getGroupBase(selectedId);
  if (!base) return;
  const snap = e.gridSnapBlock(selectedId, base.pos[0], base.pos[2]);
  let target;
  if (snap.valid) {
    const y = e.surfaceYAt ? e.surfaceYAt(snap.centerX, snap.centerZ, selectedId) : 0;
    target = [snap.centerX, y, snap.centerZ];
  } else if (dragStartPos) target = [dragStartPos[0], base.pos[1], dragStartPos[2]];
  else target = base.pos;
  suppress = true;
  e.setGroupTransform(selectedId, target, base.rotDeg, base.scale);
  if (selectedId) selectElement(selectedId);
  suppress = false;
  dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, target, base.rotDeg, base.scale);
  if (!snap.valid) dotnet.invokeMethodAsync('OnPlacementBlocked', snap.reason || 'blocked');
}

// Live gizmo snapping while dragging: translate snaps to the grid cell, rotate to 15deg; size 0 is free.
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
  // Places the proxy at the element's visual center. centerOffset = centerWorld - basePos, removed on drag.
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

// Applies a transform from .NET's numeric fields; suppress stops the objectChange echo.
export function applyTransform(id, pos, rotDeg, scale) {
  const e = engine();
  if (!e) return;
  suppress = true;
  e.setGroupTransform(id, pos, rotDeg, scale);
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
