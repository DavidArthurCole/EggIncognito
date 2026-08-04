


import { engine, rad } from './playgroundShared.js';

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

 
 
  proxy = new THREE.Object3D();
  e.scene().add(proxy);
  centerOffset = new THREE.Vector3(0, 0, 0);

 
 
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

 
 
  onKeyDown = ev => {
    const t = ev.target;
    const typing = t && (t.tagName === 'INPUT' || t.tagName === 'SELECT' || t.tagName === 'TEXTAREA' || t.isContentEditable);
   
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

 
  e.scene().add(gizmo.getHelper());
  globalThis.__pgDesigner = { setGizmoVisible };
}
export function setGizmoVisible(on) {
  if (gizmo) gizmo.getHelper().visible = !!on;
}

function deg(r) { return r * 180 / Math.PI; }

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
