// 3D playground: a three.js scene that loads .glb ship meshes and plays their embedded animations. three.js
// + GLTFLoader + OrbitControls are pulled as ES modules from a CDN at runtime (the app has no JS bundler;
// every other interop module here is hand-authored ESM too). The Blazor page owns the canvas + UI; this owns
// the WebGL scene. One scene per page instance; dispose() tears it down when the circuit ends.
//
// API (called from Playground.razor via JS interop):
//   init(canvas)              -> set up renderer/scene/camera/controls + start the render loop
//   loadGlbBase64(b64)        -> replace the current model with a .glb decoded from base64, play its animation
//   setPlaying(bool)          -> pause/resume animation playback
//   resetView()               -> frame the camera on the model bounds
//   dispose()                 -> stop the loop, free GL resources

const THREE_URL = 'https://esm.sh/three@0.169.0';
const GLTF_URL = 'https://esm.sh/three@0.169.0/examples/jsm/loaders/GLTFLoader.js';
const ORBIT_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/OrbitControls.js';

let THREE, GLTFLoader, OrbitControls;
let renderer, scene, camera, controls, clock, mixer, current, raf;

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

  // resize after the camera exists (it sets camera.aspect); the renderer is enough for the size read.
  resize();

  // Lighting that shows EI's per-vertex emission (COLOR_0) without washing it out: a soft hemisphere fill
  // plus one key light. Emission is vertex color, so it reads even in shadow.
  scene.add(new THREE.HemisphereLight(0xffffff, 0x404050, 1.1));
  const key = new THREE.DirectionalLight(0xffffff, 1.4);
  key.position.set(3, 5, 2);
  scene.add(key);

  clock = new THREE.Clock();
  window.addEventListener('resize', resize);
  loop();
}

function loop() {
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  if (mixer) mixer.update(dt);
  controls.update();
  renderer.render(scene, camera);
}

export async function loadGlbBase64(b64) {
  await ensureLibs();
  const buf = Uint8Array.from(atob(b64), c => c.charCodeAt(0)).buffer;
  const loader = new GLTFLoader();
  const gltf = await loader.parseAsync(buf, '');

  if (current) { scene.remove(current); disposeObject(current); }
  current = gltf.scene;
  scene.add(current);

  // Play every embedded clip (the baked spin/hover) on a fresh mixer.
  if (mixer) mixer.stopAllAction();
  mixer = new THREE.AnimationMixer(current);
  for (const clip of gltf.animations) mixer.clipAction(clip).play();

  frameModel();
  return gltf.animations.map(c => c.name);
}

export function setPlaying(playing) {
  if (mixer) mixer.timeScale = playing ? 1 : 0;
}

export function resetView() {
  if (current) frameModel();
}

// Frame the camera so the model fills the view, using its bounding sphere.
function frameModel() {
  const box = new THREE.Box3().setFromObject(current);
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
  if (mixer) mixer.stopAllAction();
  if (current) { scene?.remove(current); disposeObject(current); current = null; }
  renderer?.dispose();
  renderer = scene = camera = controls = mixer = null;
}
