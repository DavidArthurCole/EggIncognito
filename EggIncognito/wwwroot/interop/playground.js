// 3D playground: a three.js scene that composes several named groups (device meshes, a chicken wearing a
// hat, shells, static models) into one view at once. three.js + GLTFLoader + OrbitControls are pulled as ES
// modules from a CDN at runtime (the app has no JS bundler). The Blazor page owns the canvas + the widget UI;
// this owns the WebGL scene and the group registry. One scene per page instance; dispose() tears it down.
//
// A "group" is one rendered source, keyed by a string id (the widget name). Each group is a THREE.Group with
// its own mixer for embedded clips, an auto-offset (laid out so groups do not overlap), and an optional
// manual offset (user X/Y/Z, overrides the auto-offset once set). A chicken group may carry a hat parented at
// the game anchor so the two move + animate as one.
//
// API (called from Playground.razor via JS interop):
//   init(canvas)
//   addGroup(groupId, glbBase64, opts)  -> { hatBase64?, anchor? } composes a chicken+hat; returns clip names
//   removeGroup(groupId)
//   setGroupOffset(groupId, x, y, z)    -> live manual offset
//   relayoutGroups()                    -> recompute auto-offsets + frame camera
//   setPlaying(bool) / resetView() / dispose()

const THREE_URL = 'https://esm.sh/three@0.169.0';
const GLTF_URL = 'https://esm.sh/three@0.169.0/examples/jsm/loaders/GLTFLoader.js';
const ORBIT_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/OrbitControls.js';

let THREE, GLTFLoader, OrbitControls;
let renderer, scene, camera, controls, clock, raf;
let sun, ambient, resizeObserver;
let designMode = false;

// procedural animation clock + global play/pause. Each group carries its OWN anim kind (per-element spin),
// composed on top of that element's placed transform. Distinct from a group's mixer (baked mesh clips).
let animClock = 0;
let animPlaying = true;
const ANIM_PERIOD = 6;

// groupId -> { root, mixer, hatMixer, autoOffset, manual, pinned, anim, base }
//   anim = 'none'|'SpinY'|'SpinZ'|'HoverSpin' (per-element procedural)
//   base = { pos:[x,y,z], rotDeg:[x,y,z], scale } the element's placed transform (design mode), spin rides it
const groups = new Map();

async function ensureLibs() {
  if (THREE) return;
  THREE = await import(THREE_URL);
  ({ GLTFLoader } = await import(GLTF_URL));
  ({ OrbitControls } = await import(ORBIT_URL));
}

export async function init(canvas) {
  await ensureLibs();
  if (renderer) dispose();

  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(45, aspect(), 0.01, 1000);
  camera.position.set(0, 1.2, 3);

  controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;

  resize();

  // Adjustable sun (directional) + a fixed soft ambient fill so emissive meshes never go fully black. Fog is
  // optional (density 0 = off). A default keeps a fresh scene lit.
  sun = new THREE.DirectionalLight(0xffffff, 1.0);
  ambient = new THREE.AmbientLight(0xffffff, 0.6);
  scene.add(sun);
  scene.add(ambient);
  setLighting({ sun: { azimuthDeg: 45, elevationDeg: 55, color: '#ffffff', intensity: 1.0 },
                fog: { color: '#1a1a1f', density: 0 } });

  clock = new THREE.Clock();
  window.addEventListener('resize', resize);
  // Track the canvas element's actual size, not just window resize: a layout change (e.g. a control column
  // appearing) resizes the canvas without a window event, which otherwise leaves the camera aspect stale and
  // stretches the render.
  resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(canvas);
  // Publish the live engine accessors on a single global so the designer module reaches THIS instance. A
  // cache-bust query (?v=) on the module URL would otherwise fork a second, uninitialized engine instance.
  window.__pgEngine = { scene: _scene, camera: _camera, renderer: _renderer, controls: _controls, getGroupRoot, getGroupBase, setGroupTransform, groupIdOf, groupRoots, setSelectionOutline };
  loop();
}

function loop() {
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  if (animPlaying) animClock += dt;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.update(dt);
    if (g.hatMixer) g.hatMixer.update(dt);
    applyAnim(g);
  }
  controls.update();
  renderer.render(scene, camera);
}

// Per-element procedural animation (spin / hover), composed on top of that element's base transform. Only the
// animated element moves; everything else holds still. The chicken + its hat share the group root, so they
// ride as one rigid unit.
function applyAnim(g) {
  const phase = (animClock / ANIM_PERIOD) * Math.PI * 2;
  let addRy = 0, addRz = 0, bob = 0;
  switch (g.anim) {
    case 'SpinY': addRy = phase; break;
    case 'SpinZ': addRz = phase; break;
    case 'HoverSpin': addRy = phase; bob = Math.sin(phase) * 0.15; break;
  }
  // base: an explicit placed transform (design mode) takes precedence; else the offset (view mode).
  if (g.base) {
    const b = g.base;
    g.root.position.set(b.pos[0], b.pos[1] + bob, b.pos[2]);
    g.root.rotation.set(rad(b.rotDeg[0]), rad(b.rotDeg[1]) + addRy, rad(b.rotDeg[2]) + addRz);
    const s = b.scale || 1;
    g.root.scale.set(s, s, s);
    return;
  }
  const o = g.manual || g.autoOffset;
  g.root.position.set(o.x, o.y + bob, o.z);
  g.root.rotation.set(0, addRy, addRz);
}

function rad(d) { return (d || 0) * Math.PI / 180; }

async function parseGlb(b64) {
  const buf = Uint8Array.from(atob(b64), c => c.charCodeAt(0)).buffer;
  return new GLTFLoader().parseAsync(buf, '');
}

export async function addGroup(groupId, glbBase64, opts) {
  await ensureLibs();
  opts = opts || {};
  // remember anim + placement before removeGroup wipes the entry, so a re-render keeps them.
  const carried = groups.get(groupId);
  removeGroup(groupId);

  const gltf = await parseGlb(glbBase64);
  const root = new THREE.Group();
  root.add(gltf.scene);

  const mixer = new THREE.AnimationMixer(root);
  for (const clip of gltf.animations) mixer.clipAction(clip).play();
  const clipNames = gltf.animations.map(c => c.name);

  let hatMixer = null;
  // chicken + hat: parent the hat under a node at the anchor, scaled, so it rides the chicken.
  if (opts.hatBase64) {
    const hat = await parseGlb(opts.hatBase64);
    const a = opts.anchor || [0, 0, 0, 1];
    const anchor = new THREE.Group();
    anchor.position.set(a[0] || 0, a[1] || 0, a[2] || 0);
    const s = a[3] || 1;
    anchor.scale.set(s, s, s);
    anchor.add(hat.scene);
    root.add(anchor);
    hatMixer = new THREE.AnimationMixer(anchor);
    for (const clip of hat.animations) hatMixer.clipAction(clip).play();
  }

  // pinned groups (the env backdrop) hold a fixed placement offset (default origin): no auto-layout, no
  // procedural spin, not framed. opts.offset = [x,y,z].
  const off = opts.offset || [0, 0, 0];
  groups.set(groupId, {
    root, mixer, hatMixer,
    autoOffset: { x: 0, y: 0, z: 0 },
    manual: opts.pinned ? { x: off[0] || 0, y: off[1] || 0, z: off[2] || 0 } : null,
    pinned: !!opts.pinned,
    // preserve anim + placement across a re-render (reshell / hat change keeps spin + position).
    anim: carried?.anim || 'none',
    base: carried?.base || null,
  });
  scene.add(root);
  relayoutGroups();
  return clipNames;
}

export function removeGroup(groupId) {
  const g = groups.get(groupId);
  if (!g) return;
  if (g.mixer) g.mixer.stopAllAction();
  if (g.hatMixer) g.hatMixer.stopAllAction();
  scene.remove(g.root);
  disposeObject(g.root);
  groups.delete(groupId);
  relayoutGroups();
}

export function setGroupOffset(groupId, x, y, z) {
  const g = groups.get(groupId);
  if (!g) return;
  g.manual = { x: +x || 0, y: +y || 0, z: +z || 0 };
  applyOffset(g);
}

// Space the model groups along X by the widest group's bbox so they do not overlap. Pinned env groups stay
// at world origin and are excluded from the layout. Groups with a manual offset keep it. Then frame.
// Recomputes group positions. Does NOT move the camera: re-framing on every add/reshell/transform yanked the
// view. The camera is framed only on explicit resetView() (and the first load, via frameOnce).
export function relayoutGroups() {
  const all = [...groups.values()];
  if (all.length === 0) return;
  // design mode: every group holds its own transform; no auto-offset layout.
  if (designMode) { maybeFrameOnce(); return; }
  for (const g of all.filter(g => g.pinned)) applyOffset(g);

  const list = all.filter(g => !g.pinned);
  let maxW = 0;
  for (const g of list) {
    g.root.position.set(0, 0, 0); // measure unoffset
    const box = new THREE.Box3().setFromObject(g.root);
    const w = box.max.x - box.min.x;
    if (Number.isFinite(w)) maxW = Math.max(maxW, w);
  }
  const spacing = (maxW || 1) * 1.4;
  const n = list.length;
  list.forEach((g, i) => {
    const x = (i - (n - 1) / 2) * spacing;
    g.autoOffset = { x, y: 0, z: 0 };
    applyOffset(g);
  });
  maybeFrameOnce();
}

// Frames the camera exactly once after the scene first gets content, so an empty-start scene still gets a
// sensible initial view without re-framing on every later change.
let framedOnce = false;
function maybeFrameOnce() {
  if (framedOnce || groups.size === 0) return;
  framedOnce = true;
  frameScene();
}

function applyOffset(g) {
  const o = g.manual || g.autoOffset;
  g.root.position.set(o.x, o.y, o.z);
}

export function setPlaying(playing) {
  animPlaying = playing;
  const t = playing ? 1 : 0;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.timeScale = t;
    if (g.hatMixer) g.hatMixer.timeScale = t;
  }
}

// Sets ONE element's procedural animation (spin / hover). Per-element: only that group moves. Live.
export function setGroupAnimation(id, kind) {
  const g = groups.get(id);
  if (g) g.anim = kind || 'none';
}

// Sets ONE element's placed transform (the designer's gizmo / numeric fields). Stored as the group's base so
// a per-element spin rides on top of it. pos + rotDeg are 3-arrays, scale a number.
export function setGroupTransform(id, pos, rotDeg, scale) {
  const g = groups.get(id);
  if (!g) return;
  g.base = { pos: [pos[0] || 0, pos[1] || 0, pos[2] || 0], rotDeg: [rotDeg[0] || 0, rotDeg[1] || 0, rotDeg[2] || 0], scale: scale || 1 };
}

export function resetView() { frameScene(); }

// Sets the scene background to a solid color, or clears it (transparent canvas) when null/empty.
export function setBackground(hex) {
  if (!scene) return;
  if (hex) { scene.background = new THREE.Color(hex); }
  else { scene.background = null; }
}

// Positions the sun on a unit dome from azimuth (around Y) + elevation (up from the horizon), and sets the
// Sun (positioned on a dome from azimuth+elevation, color, intensity) + scene fog (color + density). Ambient
// stays a fixed soft fill so emissive meshes never go fully black. Live; safe before any element loads.
export function setLighting(opts) {
  if (!sun || !ambient || !scene) return;
  const s = (opts && opts.sun) || {};
  const f = (opts && opts.fog) || {};
  const az = (s.azimuthDeg || 0) * Math.PI / 180;
  const el = (s.elevationDeg || 0) * Math.PI / 180;
  const r = 10;
  sun.position.set(r * Math.cos(el) * Math.sin(az), r * Math.sin(el), r * Math.cos(el) * Math.cos(az));
  if (s.color) sun.color.set(s.color);
  if (typeof s.intensity === 'number') sun.intensity = s.intensity;

  // Fog: exponential, density 0 = off. Color defaults to white.
  const density = typeof f.density === 'number' ? f.density : 0;
  if (density > 0) {
    if (!scene.fog) scene.fog = new THREE.FogExp2(0xffffff, density);
    scene.fog.density = density;
    if (f.color) scene.fog.color.set(f.color);
  } else {
    scene.fog = null;
  }
}

// In design mode, groups hold their own transform (no auto-offset layout on add/remove).
export function setDesignMode(on) { designMode = !!on; }

export function getGroupRoot(id) {
  const g = groups.get(id);
  return g ? g.root : null;
}

// The group's placed base transform (pos/rotDeg/scale), or null. Reads the stored base (not the live root,
// which a per-element spin animates), so a nudge composes correctly. Defaults to the root position if no base
// was set yet (an element placed without a design transform).
export function getGroupBase(id) {
  const g = groups.get(id);
  if (!g) return null;
  if (g.base) return { pos: g.base.pos.slice(), rotDeg: g.base.rotDeg.slice(), scale: g.base.scale };
  const o = g.manual || g.autoOffset || { x: 0, y: 0, z: 0 };
  return { pos: [o.x, o.y, o.z], rotDeg: [0, 0, 0], scale: 1 };
}

export function listGroupIds() { return [...groups.keys()]; }

// The group id whose root is an ancestor of a clicked object3d, or null. Lets a raycast hit on any child mesh
// resolve back to the element it belongs to.
export function groupIdOf(obj) {
  for (const [id, g] of groups) {
    let o = obj;
    while (o) { if (o === g.root) return id; o = o.parent; }
  }
  return null;
}

// The group roots, as raycast targets for click-to-select.
export function groupRoots() { return [...groups.values()].map(g => g.root); }

// Faint selection outline: add/remove a wireframe overlay on a group's meshes so the selected element reads
// without obscuring it. Stored on the group so it can be cleared.
export function setSelectionOutline(id, on) {
  for (const [gid, g] of groups) {
    const want = on && gid === id;
    if (want && !g._outline) g._outline = addOutline(g.root);
    else if (!want && g._outline) { removeOutline(g.root, g._outline); g._outline = null; }
  }
}

function addOutline(root) {
  const added = [];
  root.traverse(o => {
    if (!o.isMesh || !o.geometry) return;
    const wire = new THREE.LineSegments(
      new THREE.EdgesGeometry(o.geometry, 30),
      new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.12, depthTest: false }));
    wire.renderOrder = 999;
    o.add(wire);
    added.push(wire);
  });
  return added;
}

function removeOutline(root, added) {
  for (const w of added) {
    w.parent?.remove(w);
    w.geometry?.dispose();
    w.material?.dispose();
  }
}

// Internal accessors for the designer module (gizmo needs the live camera/renderer/controls).
export function _scene() { return scene; }
export function _camera() { return camera; }
export function _renderer() { return renderer; }
export function _controls() { return controls; }

function frameScene() {
  if (groups.size === 0) return;
  // frame on the model groups; fall back to everything (env-only scene) so the camera still has a target.
  const models = [...groups.values()].filter(g => !g.pinned);
  const framed = models.length > 0 ? models : [...groups.values()];
  const box = new THREE.Box3();
  for (const g of framed) box.expandByObject(g.root);
  if (box.isEmpty()) return;
  const sphere = box.getBoundingSphere(new THREE.Sphere());
  const r = sphere.radius || 1;
  controls.target.copy(sphere.center);
  const dist = r / Math.sin((camera.fov * Math.PI / 180) / 2) * 1.3;
  camera.position.set(sphere.center.x, sphere.center.y + r * 0.4, sphere.center.z + dist);
  camera.near = r / 100;
  camera.far = r * 100;
  camera.updateProjectionMatrix();
  controls.update();
}

function aspect() {
  const c = renderer.domElement;
  return (c.clientWidth || 1) / (c.clientHeight || 1);
}

function resize() {
  if (!renderer) return;
  const c = renderer.domElement;
  const w = c.clientWidth, h = c.clientHeight;
  if (w === 0 || h === 0) return;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

function disposeObject(obj) {
  obj.traverse(o => {
    if (o.geometry) o.geometry.dispose();
    if (o.material) {
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) m.dispose();
    }
  });
}

export function dispose() {
  if (raf) cancelAnimationFrame(raf);
  raf = null;
  window.removeEventListener('resize', resize);
  if (resizeObserver) { resizeObserver.disconnect(); resizeObserver = null; }
  framedOnce = false;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.stopAllAction();
    if (g.hatMixer) g.hatMixer.stopAllAction();
    scene?.remove(g.root);
    disposeObject(g.root);
  }
  groups.clear();
  renderer?.dispose();
  renderer = scene = camera = controls = sun = ambient = null;
}
