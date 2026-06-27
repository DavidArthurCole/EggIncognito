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

  // Dragging the gizmo must not also orbit the camera.
  gizmo.addEventListener('dragging-changed', ev => { controls.enabled = !ev.value; dragging = ev.value; });

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

  // On a gizmo drag: write the new transform into the group's base immediately (so the anim loop does not
  // revert it next frame), then notify .NET so the inspector fields follow.
  gizmo.addEventListener('objectChange', () => {
    if (suppress || !selectedId || !gizmo.object) return;
    const o = gizmo.object;
    const pos = [o.position.x, o.position.y, o.position.z];
    const rotDeg = [deg(o.rotation.x), deg(o.rotation.y), deg(o.rotation.z)];
    e.setGroupTransform(selectedId, pos, rotDeg, o.scale.x);
    dotnet.invokeMethodAsync('OnGizmoTransform', selectedId, pos, rotDeg, o.scale.x);
  });

  e.scene().add(gizmo);
}

function deg(rad) { return rad * 180 / Math.PI; }
function rad(d) { return d * Math.PI / 180; }

export function selectElement(id) {
  const e = engine();
  const root = e?.getGroupRoot(id);
  if (!root) { deselect(); return; }
  if (selectedId && selectedId !== id) e.setSelectionOutline(selectedId, false);
  selectedId = id;
  gizmo.attach(root);
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
  suppress = false;
}

export function disposeDesigner() {
  if (selectedId) engine()?.setSelectionOutline(selectedId, false);
  if (domEl) {
    if (onPointerDown) domEl.removeEventListener('pointerdown', onPointerDown);
    if (onPointerUp) domEl.removeEventListener('pointerup', onPointerUp);
  }
  if (gizmo) {
    gizmo.detach();
    engine()?.scene()?.remove(gizmo);
    gizmo.dispose?.();
    gizmo = null;
  }
  selectedId = null; domEl = null; dotnet = null;
}
