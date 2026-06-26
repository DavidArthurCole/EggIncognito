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

// groupId -> { root: THREE.Group, mixer, hatMixer, autoOffset: {x,y,z}, manual: {x,y,z} | null }
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

  // Even, multi-directional lighting so spinning models have no swinging dark side. Hemisphere + four fills.
  scene.add(new THREE.HemisphereLight(0xffffff, 0x3a3a44, 1.4));
  scene.add(new THREE.AmbientLight(0xffffff, 0.35));
  const dirs = [[4, 6, 4], [-4, 4, -4], [4, 2, -4], [-4, 2, 4]];
  for (const [x, y, z] of dirs) {
    const d = new THREE.DirectionalLight(0xffffff, 0.5);
    d.position.set(x, y, z);
    scene.add(d);
  }

  clock = new THREE.Clock();
  window.addEventListener('resize', resize);
  loop();
}

function loop() {
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.update(dt);
    if (g.hatMixer) g.hatMixer.update(dt);
  }
  controls.update();
  renderer.render(scene, camera);
}

async function parseGlb(b64) {
  const buf = Uint8Array.from(atob(b64), c => c.charCodeAt(0)).buffer;
  return new GLTFLoader().parseAsync(buf, '');
}

export async function addGroup(groupId, glbBase64, opts) {
  await ensureLibs();
  opts = opts || {};
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

  groups.set(groupId, { root, mixer, hatMixer, autoOffset: { x: 0, y: 0, z: 0 }, manual: null });
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

// Space the groups along X by the widest group's bbox so they do not overlap. Groups with a manual offset
// keep it; only auto offsets are recomputed. Then frame the camera on everything.
export function relayoutGroups() {
  const list = [...groups.values()];
  if (list.length === 0) return;

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
  frameScene();
}

function applyOffset(g) {
  const o = g.manual || g.autoOffset;
  g.root.position.set(o.x, o.y, o.z);
}

export function setPlaying(playing) {
  const t = playing ? 1 : 0;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.timeScale = t;
    if (g.hatMixer) g.hatMixer.timeScale = t;
  }
}

export function resetView() { frameScene(); }

function frameScene() {
  if (groups.size === 0) return;
  const box = new THREE.Box3();
  for (const g of groups.values()) box.expandByObject(g.root);
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
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.stopAllAction();
    if (g.hatMixer) g.hatMixer.stopAllAction();
    scene?.remove(g.root);
    disposeObject(g.root);
  }
  groups.clear();
  renderer?.dispose();
  renderer = scene = camera = controls = null;
}
