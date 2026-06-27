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

export async function initDesigner(dotnetRef) {
  dotnet = dotnetRef;
  THREE = await import(THREE_URL);
  ({ TransformControls } = await import(TC_URL));

  const e = engine();
  if (!e || !e.renderer()) throw new Error('playground engine not initialized');
  const cam = e.camera(), dom = e.renderer().domElement, controls = e.controls();
  gizmo = new TransformControls(cam, dom);
  gizmo.setMode('translate');

  // Dragging the gizmo must not also orbit the camera.
  gizmo.addEventListener('dragging-changed', e => { controls.enabled = !e.value; });

  // Push the live transform back to .NET so the inspector fields follow the gizmo (unless we set it ourselves).
  gizmo.addEventListener('objectChange', () => {
    if (suppress || !selectedId || !gizmo.object) return;
    const o = gizmo.object;
    dotnet.invokeMethodAsync('OnGizmoTransform', selectedId,
      [o.position.x, o.position.y, o.position.z],
      [deg(o.rotation.x), deg(o.rotation.y), deg(o.rotation.z)],
      o.scale.x);
  });

  e.scene().add(gizmo);
}

function deg(rad) { return rad * 180 / Math.PI; }
function rad(d) { return d * Math.PI / 180; }

export function selectElement(id) {
  const root = engine()?.getGroupRoot(id);
  if (!root) { deselect(); return; }
  selectedId = id;
  gizmo.attach(root);
}

export function deselect() {
  selectedId = null;
  if (gizmo) gizmo.detach();
}

export function setGizmoMode(mode) {
  if (gizmo && (mode === 'translate' || mode === 'rotate')) gizmo.setMode(mode);
}

// Apply a transform from .NET (numeric fields). suppress stops the objectChange echo back to .NET.
export function applyTransform(id, pos, rotDeg, scale) {
  const root = engine()?.getGroupRoot(id);
  if (!root) return;
  suppress = true;
  root.position.set(pos[0] || 0, pos[1] || 0, pos[2] || 0);
  root.rotation.set(rad(rotDeg[0] || 0), rad(rotDeg[1] || 0), rad(rotDeg[2] || 0));
  const s = scale || 1;
  root.scale.set(s, s, s);
  suppress = false;
}

export function disposeDesigner() {
  if (gizmo) {
    gizmo.detach();
    engine()?.scene()?.remove(gizmo);
    gizmo.dispose?.();
    gizmo = null;
  }
  selectedId = null;
  dotnet = null;
}
