import { rad } from './playgroundShared.js';

const THREE_URL = 'https://esm.sh/three@0.169.0';
const GLTF_URL = 'https://esm.sh/three@0.169.0/examples/jsm/loaders/GLTFLoader.js';
const ORBIT_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/OrbitControls.js';

const LOAD_CONCURRENCY = 6;
const ANIM_PERIOD_FALLBACK = 6;

const ROAD_Z = 13.33;
const ROAD_Y = 0;
const ROAD_SPAWN_X = 48.0;
const ROAD_DESPAWN_X = -35.0;
const ROAD_SPAWN_GAP = 5.0;
const DEPOT_STOP_X = 7.1;
const DEPOT_ENTER_DIST = 3.0;
const DEPOT_ARRIVE_EPS = 0.01;
const VEHICLE_SPEED_BASE = 1.5;
const VEHICLE_FOLLOW_GAP = 2.5;
const VEHICLE_DWELL_BASE = 2.0;
const VEHICLE_ROUND_TRIP_BASE = 100.0;
const VEHICLE_SLOT_WRAP = 30;
const VEHICLE_TYPE_TRAIN = 11;
const VEHICLE_TYPE_EMPTY = 12;
const VEHICLE_CLOCK_START = 1000.0;
const VEHICLE_PREWARM_STEPS = 50;
const VEHICLE_PREWARM_DT = 0.1;

const CHICKEN_MAX_SPEED = 9.0;
const CHICKEN_ACCEL = 15.0;
const CHICKEN_ARRIVE_FRAC = 0.95;
const CHICKEN_DESPAWN_FRAC = 0.8;
const CHICKEN_ALIGN_MIN = 0.2;
const CHICKEN_REVERSE_DOT = -0.4;
const CHICKEN_DRAG = 3.0;
const CHICKEN_REVERSE_DRAG = 15.0;
const CHICKEN_SPAWN_SPEED = 0.1;
const CHICKEN_SPAWN_SPREAD = 0.6;
const CHICKEN_MODEL_YAW = -Math.PI / 2;
const CHICKEN_PROBE_HEIGHT = 10.0;

const CHICKEN_ANIMATIONS = {
  STANDARD_RUN: 0,
  WOBBLE: 1,
  SMOOTH: 2,
  HOVER: 3,
  SIDEWAYS_SMOOTH: 4,
  WOBBLE_LEAN: 5,
  SMOOTH_LEAN: 6,
  SLOWMO: 7,
  SIDEWAYS_LEAN: 8,
};

let THREE, GLTFLoader, OrbitControls;
let renderer, scene, camera, controls, clock, raf, resizeObserver;
let sun, ambient;
let vertexColorMat = null;
let plainMat = null;

let animClock = 0;
let animPlaying = true;
let capturing = false;
let captureClock = 0;
let savedClock = 0;
let framedOnce = false;
let lightDir = [0.5, 0.8, 0.5];

const slots = new Map();
const meshCache = new Map();
let farmGen = 0;

let motionCfg = null;
let motionRoot = null;
let motionClock = 0;
let vehicleActors = [];
let vehicleCursor = 0;
let vehicleNextSpawn = 0;
let vehicleSlotClock = [];
let chickenActors = [];
let rngState = 1;

let _axisY, _axisX, _axisZ, _qBase, _qAnim, _qPart, _probeRay, _probeOrigin, _probeDown;

async function ensureLibs() {
  if (THREE) return;
  THREE = await import(THREE_URL);
  ({ GLTFLoader } = await import(GLTF_URL));
  ({ OrbitControls } = await import(ORBIT_URL));
}

function ensureScratch() {
  if (_axisY) return;
  _axisY = new THREE.Vector3(0, 1, 0);
  _axisX = new THREE.Vector3(1, 0, 0);
  _axisZ = new THREE.Vector3(0, 0, 1);
  _qBase = new THREE.Quaternion();
  _qAnim = new THREE.Quaternion();
  _qPart = new THREE.Quaternion();
  _probeRay = new THREE.Raycaster();
  _probeOrigin = new THREE.Vector3();
  _probeDown = new THREE.Vector3(0, -1, 0);
}

export async function init(canvas) {
  await ensureLibs();
  if (renderer) dispose();

  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
  } catch {
    renderer = null;
    return false;
  }
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.NoToneMapping;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(45, aspect(), 0.05, 4000);
  camera.position.set(0, 14, 38);
  controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;
  controls.target.set(0, 0, 0);
  resize();

  sun = new THREE.DirectionalLight(0xffffff, 1.0);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0002;
  sun.shadow.normalBias = 0.08;
  ambient = new THREE.AmbientLight(0xffffff, 0.55);
  scene.add(sun);
  scene.add(sun.target);
  scene.add(ambient);

  motionRoot = new THREE.Group();
  scene.add(motionRoot);

  ensureScratch();
  fitShadow();

  clock = new THREE.Clock();
  animClock = 0;
  animPlaying = true;
  capturing = false;
  framedOnce = false;

  window.addEventListener('resize', resize);
  resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(canvas);

  window.__pgEngine = { canvas: rendererCanvas, captureBegin, renderAtPhase, captureEnd, anyAnimated, animPeriod };
  loop();
  return true;
}

function rendererCanvas() {
  return renderer ? renderer.domElement : null;
}

function loop() {
  if (!renderer) return;
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  if (!capturing) {
    const step = animPlaying ? dt : 0;
    animClock += step;
    for (const slot of slots.values()) if (slot.mixer) slot.mixer.update(step);
    stepMotion(step);
  }
  controls.update();
  renderer.render(scene, camera);
}

export function setPlaying(playing) {
  animPlaying = playing !== false;
}

function meshUrlOf(placement) {
  const url = placement?.meshUrl;
  return typeof url === 'string' && url.length > 0 ? url : null;
}

function loadMesh(url) {
  const cached = meshCache.get(url);
  if (cached) return cached;
  const pending = fetchMesh(url);
  meshCache.set(url, pending);
  pending.catch(() => {
    if (meshCache.get(url) === pending) meshCache.delete(url);
  });
  return pending;
}

async function fetchMesh(url) {
  const resp = await fetch(url, { credentials: 'same-origin' });
  if (!resp.ok) throw new Error('mesh fetch failed (' + resp.status + ')');
  const buf = await resp.arrayBuffer();
  const gltf = await new GLTFLoader().parseAsync(buf, '');
  const src = gltf.scene;
  src.traverse(o => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.receiveShadow = true;
    o.material = o.geometry?.attributes?.color ? vertexColorMaterial() : plainMaterial();
  });
  return { scene: src, animations: gltf.animations || [] };
}

function vertexColorMaterial() {
  if (!vertexColorMat) vertexColorMat = new THREE.MeshStandardMaterial({ vertexColors: true, metalness: 0, roughness: 1 });
  return vertexColorMat;
}

function plainMaterial() {
  if (!plainMat) plainMat = new THREE.MeshStandardMaterial({ color: 0xcccccc, metalness: 0, roughness: 1 });
  return plainMat;
}

async function runPool(items, limit, worker) {
  if (items.length === 0) return;
  let cursor = 0;
  const lanes = [];
  const width = Math.min(limit, items.length);
  for (let i = 0; i < width; i++) {
    lanes.push((async () => {
      while (true) {
        const index = cursor++;
        if (index >= items.length) return;
        await worker(items[index]);
      }
    })());
  }
  await Promise.all(lanes);
}

export async function setFarm(placements) {
  await ensureLibs();
  const result = { loaded: 0, skipped: [] };
  if (!scene) return result;

  const gen = ++farmGen;
  const list = Array.isArray(placements) ? placements : [];
  const wanted = new Map();
  for (const p of list) {
    if (p && typeof p.key === 'string' && p.key.length > 0) wanted.set(p.key, p);
  }

  for (const [key, slot] of slots) {
    const p = wanted.get(key);
    if (!p || meshUrlOf(p) !== slot.url) removeSlot(key);
  }

  const pending = [];
  for (const [key, p] of wanted) {
    const url = meshUrlOf(p);
    if (!url) {
      result.skipped.push(skipEntry(key, p, 'no-mesh'));
      continue;
    }
    const existing = slots.get(key);
    if (existing) {
      applyPlacement(existing, p);
      continue;
    }
    pending.push({ key, placement: p, url });
  }

  await runPool(pending, LOAD_CONCURRENCY, async item => {
    let src;
    try {
      src = await loadMesh(item.url);
    } catch {
      result.skipped.push(skipEntry(item.key, item.placement, 'fetch-failed'));
      return;
    }
    if (gen !== farmGen) return;
    addSlot(item.key, item.placement, item.url, src);
  });

  if (gen !== farmGen) return result;

  result.loaded = slots.size;
  fitShadow();
  rebuildChickenRoutes();
  if (!framedOnce && slots.size > 0) {
    framedOnce = true;
    resetView();
  }
  return result;
}

function skipEntry(key, placement, reason) {
  return {
    key,
    element: placement?.element ?? null,
    stem: placement?.stem ?? null,
    reason,
  };
}

function addSlot(key, placement, url, src) {
  const root = new THREE.Group();
  const model = src.scene.clone(true);
  root.add(model);
  let mixer = null;
  const durations = [];
  if (src.animations.length > 0) {
    mixer = new THREE.AnimationMixer(model);
    for (const clip of src.animations) {
      mixer.clipAction(clip).play();
      durations.push(clip.duration);
    }
  }
  const slot = { root, model, mixer, url, placement, durations };
  applyPlacement(slot, placement);
  scene.add(root);
  slots.set(key, slot);
}

function applyPlacement(slot, placement) {
  const pos = readVec3(placement.pos);
  const rot = readVec3(placement.rotDeg);
  const scale = numOr(placement.scale, 1);
  slot.root.position.set(pos[0], pos[1], pos[2]);
  slot.root.rotation.set(rad(rot[0]), rad(rot[1]), rad(rot[2]));
  slot.root.scale.set(scale, scale, scale);
  slot.placement = placement;
}

function removeSlot(key) {
  const slot = slots.get(key);
  if (!slot) return;
  if (slot.mixer) slot.mixer.stopAllAction();
  scene.remove(slot.root);
  slots.delete(key);
}

export function clearFarm() {
  for (const key of slots.keys()) removeSlot(key);
  clearMotionActors();
  motionCfg = null;
  farmGen++;
  framedOnce = false;
}

export function setBackground(hex) {
  if (!scene) return;
  scene.background = hex ? new THREE.Color(hex) : null;
}

export function setLighting(config) {
  if (!sun || !ambient || !scene) return;
  const cfg = config || {};

  const dir = readVecOrNull(field(cfg, 'light_dir', 'lightDir'));
  if (dir) {
    const len = Math.hypot(dir[0], dir[1], dir[2]);
    if (len > 1e-5) lightDir = [dir[0] / len, dir[1] / len, dir[2] / len];
  }

  const directColor = readVecOrNull(field(cfg, 'light_direct_color', 'lightDirectColor'));
  if (directColor) sun.color.setRGB(clamp01(directColor[0]), clamp01(directColor[1]), clamp01(directColor[2]));
  const directIntensity = numOrNull(field(cfg, 'light_direct_intensity', 'lightDirectIntensity'));
  if (directIntensity !== null) sun.intensity = directIntensity;

  const ambientColor = readVecOrNull(field(cfg, 'light_ambient_color', 'lightAmbientColor'));
  if (ambientColor) ambient.color.setRGB(clamp01(ambientColor[0]), clamp01(ambientColor[1]), clamp01(ambientColor[2]));
  const ambientIntensity = numOrNull(field(cfg, 'light_ambient_intensity', 'lightAmbientIntensity'));
  if (ambientIntensity !== null) ambient.intensity = ambientIntensity;

  const fogColor = readVecOrNull(field(cfg, 'fog_color', 'fogColor'));
  const fogNear = numOr(field(cfg, 'fog_near', 'fogNear'), 0);
  const fogFar = numOr(field(cfg, 'fog_far', 'fogFar'), 0);
  const fogDensity = numOr(field(cfg, 'fog_density', 'fogDensity'), 0);
  applyFog(fogColor, fogNear, fogFar, fogDensity);

  fitShadow();
}

function applyFog(color, near, far, density) {
  const rgb = color ? new THREE.Color(clamp01(color[0]), clamp01(color[1]), clamp01(color[2])) : new THREE.Color(0xffffff);
  if (density > 0) scene.fog = new THREE.FogExp2(rgb.getHex(), density);
  else if (far > near && far > 0) scene.fog = new THREE.Fog(rgb.getHex(), near, far);
  else scene.fog = null;
}

function fitShadow() {
  if (!sun) return;
  const box = farmBox();
  const center = box ? box.getCenter(new THREE.Vector3()) : new THREE.Vector3(0, 0, 0);
  const sphere = box ? box.getBoundingSphere(new THREE.Sphere()) : null;
  const radius = Math.min(sphere ? Math.max(sphere.radius, 1) : 40, 90);
  const dist = Math.max(radius * 2.5, 80);
  sun.target.position.copy(center);
  sun.target.updateMatrixWorld();
  sun.position.set(center.x + lightDir[0] * dist, center.y + lightDir[1] * dist, center.z + lightDir[2] * dist);
  const cam = sun.shadow.camera;
  cam.left = -radius;
  cam.right = radius;
  cam.top = radius;
  cam.bottom = -radius;
  cam.near = 0.5;
  cam.far = dist + radius * 2;
  cam.updateProjectionMatrix();
  sun.shadow.needsUpdate = true;
}

function farmBox() {
  if (slots.size === 0) return null;
  const box = new THREE.Box3();
  for (const slot of slots.values()) box.expandByObject(slot.root);
  return box.isEmpty() ? null : box;
}

export function setCamera(focus, distance, height) {
  if (!camera || !controls) return false;
  const f = readVec3(focus);
  const d = numOr(distance, 20);
  const h = numOr(height, 5);
  controls.target.set(f[0], f[1], f[2]);
  camera.position.set(f[0], f[1] + h, f[2] + d);
  camera.updateProjectionMatrix();
  controls.update();
  framedOnce = true;
  return true;
}

export function focusOn(key) {
  const slot = slots.get(key);
  if (!slot) return false;
  const box = new THREE.Box3().setFromObject(slot.root);
  if (box.isEmpty()) return false;
  frameBox(box);
  framedOnce = true;
  return true;
}

export function resetView() {
  const box = farmBox();
  if (!box) return false;
  frameBox(box);
  framedOnce = true;
  return true;
}

function frameBox(box) {
  const sphere = box.getBoundingSphere(new THREE.Sphere());
  const radius = Math.max(sphere.radius, 0.5);
  const dist = radius / Math.sin(rad(camera.fov) / 2) * 1.05;
  const dir = new THREE.Vector3().subVectors(camera.position, controls.target);
  if (dir.lengthSq() < 1e-6) dir.set(0, 0.5, 1);
  dir.normalize();
  controls.target.copy(sphere.center);
  camera.position.copy(sphere.center).addScaledVector(dir, dist);
  camera.near = Math.max(0.05, radius / 200);
  camera.far = Math.max(2000, radius * 200);
  camera.updateProjectionMatrix();
  controls.update();
}

export async function setMotion(motion) {
  await ensureLibs();
  const summary = { vehicles: 0, chickens: 0, skipped: [] };
  if (!scene) return summary;

  clearMotionActors();
  if (!motion) {
    motionCfg = null;
    return summary;
  }

  const cfg = normalizeMotion(motion, summary);
  const urls = new Set();
  for (const slot of cfg.vehicles.slots) if (slot.meshUrl) urls.add(slot.meshUrl);
  if (cfg.chickens.meshUrl) urls.add(cfg.chickens.meshUrl);

  const sources = new Map();
  await runPool([...urls], LOAD_CONCURRENCY, async url => {
    try {
      sources.set(url, await loadMesh(url));
    } catch {
      summary.skipped.push({ url, reason: 'fetch-failed' });
    }
  });
  cfg.sources = sources;

  motionCfg = cfg;
  rebuildChickenRoutes();
  resetMotion();
  summary.vehicles = cfg.vehicles.slots.length;
  summary.chickens = chickenActors.length;
  return summary;
}

function normalizeRoad(raw) {
  const r = raw || {};
  return {
    z: numOr(r.roadZ, ROAD_Z),
    y: numOr(r.roadY, ROAD_Y),
    spawnX: numOr(r.spawnX, ROAD_SPAWN_X),
    despawnX: numOr(r.despawnX, ROAD_DESPAWN_X),
    depotStopX: numOr(r.depotStopX, DEPOT_STOP_X),
    followGap: numOr(r.followGap, VEHICLE_FOLLOW_GAP),
    speedBase: numOr(r.maxSpeedMult, VEHICLE_SPEED_BASE),
    roundTrip: numOr(r.roundTripSeconds, VEHICLE_ROUND_TRIP_BASE),
    trainType: Math.trunc(numOr(r.hyperloopVehicleIndex, VEHICLE_TYPE_TRAIN)),
    emptyType: Math.trunc(numOr(r.emptyVehicleIndex, VEHICLE_TYPE_EMPTY)),
  };
}

function normalizeMotion(motion, summary) {
  const mult = motion.multipliers || {};
  const speedMult = numOr(mult.speed, 1);
  const loadingMult = numOr(mult.loadingTime, 1);
  const roundTripMult = numOr(mult.roundTrip, 1);
  const road = normalizeRoad(motion.road);

  const rawVehicles = Array.isArray(motion.vehicles) ? motion.vehicles : [];
  const roadSlots = [];
  for (let i = 0; i < rawVehicles.length; i++) {
    const v = rawVehicles[i] || {};
    const type = Math.trunc(numOr(v.type, road.emptyType));
    if (type === road.trainType || type === road.emptyType || type < 0) continue;
    const meshUrl = meshUrlOf(v);
    if (!meshUrl) summary.skipped.push({ key: 'VEHICLE:' + i, reason: 'no-mesh' });
    roadSlots.push({
      index: numOr(v.index, i),
      type,
      length: numOr(v.length, 2.1),
      meshUrl,
      speedMult: numOr(v.speedMult, speedMult),
    });
  }

  const count = roadSlots.length;
  const vehicles = {
    slots: roadSlots,
    road,
    dwell: VEHICLE_DWELL_BASE * loadingMult,
    spawnInterval: count > 0 ? (roundTripMult * road.roundTrip) / count : 0,
    slotCooldown: roundTripMult * road.roundTrip,
  };

  const rawChickens = motion.chickens || {};
  const chickens = {
    count: Math.max(0, Math.min(200, Math.trunc(numOr(rawChickens.count, 0)))),
    meshUrl: meshUrlOf(rawChickens),
    animation: readAnimation(rawChickens.animation),
    habs: normalizeHabs(rawChickens.habs),
    routes: [],
  };
  if (chickens.count > 0 && !chickens.meshUrl) summary.skipped.push({ key: 'CHICKEN', reason: 'no-mesh' });

  return { vehicles, chickens, sources: new Map() };
}

function normalizeHabs(habs) {
  const out = [];
  if (!Array.isArray(habs)) return out;
  for (let i = 0; i < habs.length; i++) {
    const h = habs[i] || {};
    out.push({
      key: typeof h.key === 'string' ? h.key : null,
      index: Math.trunc(numOr(h.index, i)),
      pos: readVec3(h.pos),
      depth: numOr(h.depth, 2.2),
    });
  }
  return out;
}

function readAnimation(value) {
  if (typeof value === 'number' && Number.isFinite(value)) return Math.trunc(value);
  if (typeof value === 'string') {
    const mapped = CHICKEN_ANIMATIONS[value.toUpperCase()];
    if (mapped !== undefined) return mapped;
  }
  return CHICKEN_ANIMATIONS.STANDARD_RUN;
}

function rebuildChickenRoutes() {
  if (!motionCfg) return;
  const cfg = motionCfg.chickens;
  const r0 = { pos: [8.7, 0, 3], radius: 0.6 };
  const r1 = { pos: [0.6, 0, 3.3], radius: 1.5 };
  const r2 = { pos: [0, 0, -7.5], radius: 1.5 };
  const routes = [];
  for (const hab of cfg.habs) {
    const a = { pos: [hab.pos[0], hab.pos[1], hab.pos[2] + 1.2], radius: 2.2 };
    const b = { pos: [hab.pos[0], hab.pos[1], hab.pos[2] - hab.depth], radius: 0.55 };
    const slot = hab.key ? slots.get(hab.key) : null;
    routes.push({ nodes: [r0, r1, r2, a, b], surface: slot ? slot.root : null });
  }
  if (routes.length === 0) routes.push({ nodes: [r0, r1, r2], surface: null });
  cfg.routes = routes;
  for (const chicken of chickenActors) {
    chicken.route = routes[chicken.routeIndex % routes.length];
    if (chicken.node >= chicken.route.nodes.length) chicken.node = chicken.route.nodes.length - 1;
  }
}

function resetMotion() {
  clearMotionActors();
  rngState = 20260811;
  motionClock = VEHICLE_CLOCK_START;
  vehicleCursor = 0;
  vehicleNextSpawn = 0;
  vehicleSlotClock = motionCfg ? motionCfg.vehicles.slots.map(() => 0) : [];
  if (!motionCfg) return;
  for (let i = 0; i < motionCfg.chickens.count; i++) spawnChicken();
  for (let i = 0; i < VEHICLE_PREWARM_STEPS; i++) stepMotion(VEHICLE_PREWARM_DT);
}

function clearMotionActors() {
  if (motionRoot) {
    for (const v of vehicleActors) if (v.obj) motionRoot.remove(v.obj);
    for (const c of chickenActors) if (c.obj) motionRoot.remove(c.obj);
  }
  vehicleActors = [];
  chickenActors = [];
}

function stepMotion(dt) {
  if (!motionCfg || dt <= 0) return;
  motionClock += dt;
  stepVehicles(dt);
  stepChickens(dt);
}

function stepVehicles(dt) {
  const cfg = motionCfg.vehicles;
  const road = cfg.road;
  if (cfg.slots.length === 0) return;
  if (cfg.spawnInterval > 0 && motionClock >= vehicleNextSpawn) {
    spawnVehicle();
    vehicleNextSpawn = motionClock + cfg.spawnInterval;
  }
  vehicleActors.sort((a, b) => a.x - b.x);
  for (let i = vehicleActors.length - 1; i >= 0; i--) {
    const v = vehicleActors[i];
    const ahead = i > 0 ? vehicleActors[i - 1] : null;
    if (v.dwell > 0) {
      v.dwell -= dt;
      v.speed = 0;
    } else {
      const prev = v.speed;
      const toDepot = v.x - road.depotStopX;
      if (!v.stopped && toDepot > 0 && toDepot < DEPOT_ENTER_DIST) {
        v.speed = Math.max(Math.pow(toDepot / DEPOT_ENTER_DIST, 0.25) * v.maxSpeed, prev / (1 + 5 * dt));
      } else {
        v.speed = Math.min(v.maxSpeed, v.speed + v.maxSpeed * dt);
      }
      if (ahead) {
        const gap = Math.abs(ahead.x - v.x);
        const half = (ahead.length + v.length) * 0.5;
        if (gap < half + road.followGap) v.speed = Math.max(0, v.speed - (5 * dt) / Math.max(gap - half, 0.1));
      }
      v.x -= v.speed * dt;
      const arrived = !v.stopped && (Math.abs(v.x - road.depotStopX) < DEPOT_ARRIVE_EPS || v.x < road.depotStopX);
      if (arrived) {
        v.x = road.depotStopX;
        v.speed = 0;
        v.stopped = true;
        v.dwell = motionCfg.vehicles.dwell;
      }
    }
    if (v.x <= road.despawnX) {
      if (v.obj) motionRoot.remove(v.obj);
      vehicleActors.splice(i, 1);
      continue;
    }
    if (v.obj) {
      v.obj.position.set(v.x, road.y, road.z);
      v.obj.rotation.set(0, 0, 0);
    }
  }
}

function spawnVehicle() {
  const cfg = motionCfg.vehicles;
  const road = cfg.road;
  const slotIndex = vehicleCursor % cfg.slots.length;
  vehicleCursor = (vehicleCursor + 1) % VEHICLE_SLOT_WRAP;
  const slot = cfg.slots[slotIndex];
  if (motionClock - vehicleSlotClock[slotIndex] < cfg.slotCooldown) return;
  vehicleSlotClock[slotIndex] = motionClock;
  if (!slot.meshUrl) return;
  const src = motionCfg.sources.get(slot.meshUrl);
  if (!src) return;

  let x = road.spawnX;
  for (const v of vehicleActors) x = Math.max(x, v.x + ROAD_SPAWN_GAP);

  const obj = new THREE.Group();
  obj.add(src.scene.clone(true));
  obj.position.set(x, road.y, road.z);
  motionRoot.add(obj);
  vehicleActors.push({
    obj,
    x,
    speed: 0,
    length: slot.length,
    maxSpeed: slot.speedMult * road.speedBase,
    stopped: false,
    dwell: 0,
  });
}

function spawnChicken() {
  const cfg = motionCfg.chickens;
  if (cfg.routes.length === 0) return;
  const routeIndex = Math.trunc(rng() * cfg.routes.length) % cfg.routes.length;
  const route = cfg.routes[routeIndex];
  const spawn = route.nodes[0];
  const ax = rng() - 0.5;
  const az = rng() - 0.5;
  const len = Math.hypot(ax, az) || 1;
  const radius = spawn.radius * CHICKEN_SPAWN_SPREAD;
  let obj = null;
  const src = cfg.meshUrl ? motionCfg.sources.get(cfg.meshUrl) : null;
  if (src) {
    obj = new THREE.Group();
    obj.add(src.scene.clone(true));
    motionRoot.add(obj);
  }
  chickenActors.push({
    obj,
    routeIndex,
    route,
    node: Math.min(1, route.nodes.length - 1),
    pos: [spawn.pos[0] + (ax / len) * radius, 0, spawn.pos[2] + (az / len) * radius],
    dir: [-1, 0, 0],
    heading: Math.PI,
    turnRate: 0,
    speed: CHICKEN_SPAWN_SPEED,
    phase: 0,
  });
}

function stepChickens(dt) {
  const cfg = motionCfg.chickens;
  if (cfg.count === 0) return;
  for (let i = chickenActors.length - 1; i >= 0; i--) {
    const c = chickenActors[i];
    const node = c.route.nodes[c.node];
    const last = c.node === c.route.nodes.length - 1;
    const tx = node.pos[0] - c.pos[0];
    const tz = node.pos[2] - c.pos[2];
    const dist = Math.hypot(tx, tz);
    if (dist < node.radius * (last ? CHICKEN_DESPAWN_FRAC : CHICKEN_ARRIVE_FRAC)) {
      if (last) {
        if (c.obj) motionRoot.remove(c.obj);
        chickenActors.splice(i, 1);
        continue;
      }
      c.node++;
      continue;
    }
    const inv = dist > 1e-6 ? 1 / dist : 0;
    const nx = tx * inv;
    const nz = tz * inv;
    const dot = c.dir[0] * nx + c.dir[2] * nz;
    const cross = c.dir[0] * nz - c.dir[2] * nx;

    if (dot >= CHICKEN_ALIGN_MIN) c.speed = Math.min(CHICKEN_MAX_SPEED, c.speed + CHICKEN_ACCEL * Math.sqrt(Math.abs(dot)) * dt);
    c.speed /= 1 + CHICKEN_DRAG * dt;
    if (dot < CHICKEN_REVERSE_DOT) c.speed /= 1 + CHICKEN_REVERSE_DRAG * dt;

    c.pos[0] += c.dir[0] * c.speed * dt;
    c.pos[2] += c.dir[2] * c.speed * dt;
    c.phase += c.speed * dt;

    c.turnRate += (cross >= 0 ? 1 : -1) * Math.PI * dt / Math.max(Math.abs(cross), 0.01);
    c.turnRate /= 1 + c.speed * 7 * clamp(2 * dt, 0.25, 1.1);
    c.heading += c.turnRate * dt;
    c.dir[0] = Math.cos(c.heading);
    c.dir[2] = Math.sin(c.heading);

    c.pos[1] = surfaceY(c);
    applyChickenTransform(c, cfg.animation);
  }
  if (cfg.routes.length === 0) return;
  while (chickenActors.length < cfg.count) spawnChicken();
}

function surfaceY(c) {
  if (c.node < 3 || !c.route.surface) return 0;
  _probeOrigin.set(c.pos[0], c.pos[1] + CHICKEN_PROBE_HEIGHT, c.pos[2]);
  _probeRay.set(_probeOrigin, _probeDown);
  const hits = _probeRay.intersectObject(c.route.surface, true);
  if (hits.length === 0) return 0;
  return c.pos[1] + CHICKEN_PROBE_HEIGHT - hits[0].distance;
}

function applyChickenTransform(c, style) {
  if (!c.obj) return;
  const lift = chickenAnimation(style, c.phase, c.speed);
  _qBase.setFromAxisAngle(_axisY, Math.atan2(c.dir[0], c.dir[2]) + CHICKEN_MODEL_YAW);
  _qBase.multiply(_qAnim);
  c.obj.quaternion.copy(_qBase);
  c.obj.position.set(c.pos[0], c.pos[1] + lift, c.pos[2]);
}

function chickenAnimation(style, d, v) {
  _qAnim.identity();
  switch (style) {
    case CHICKEN_ANIMATIONS.WOBBLE:
      rollPitch(Math.cos(2 * d) * 0.15, 0);
      return 0;
    case CHICKEN_ANIMATIONS.SMOOTH:
      return 0;
    case CHICKEN_ANIMATIONS.HOVER:
      return 0.06 + 0.03 * Math.cos(2 * d);
    case CHICKEN_ANIMATIONS.SIDEWAYS_SMOOTH:
      _qAnim.setFromAxisAngle(_axisY, Math.PI / 2);
      return 0;
    case CHICKEN_ANIMATIONS.WOBBLE_LEAN:
      rollPitch(Math.cos(2 * d) * 0.1, 0.1 - 0.2 * Math.sqrt(v));
      return 0;
    case CHICKEN_ANIMATIONS.SMOOTH_LEAN:
      rollPitch(0, 0.1 - 0.25 * Math.sqrt(v));
      return 0;
    case CHICKEN_ANIMATIONS.SLOWMO: {
      const c1 = Math.cos(d);
      rollPitch(0.1 * c1, 0.05 - 0.15 * c1 * c1);
      return 0.001 + 0.008 * Math.cos(2 * d);
    }
    case CHICKEN_ANIMATIONS.SIDEWAYS_LEAN:
      rollPitch(0, 0.1 - 0.15 * Math.sqrt(v));
      _qPart.setFromAxisAngle(_axisY, Math.PI / 2);
      _qAnim.premultiply(_qPart);
      return 0;
    default: {
      const c4 = Math.cos(4 * d);
      rollPitch(0.08 * c4, 0.05 - 0.15 * c4 * c4);
      return -0.005 + 0.005 * Math.cos(8 * d);
    }
  }
}

function rollPitch(rollZ, pitchX) {
  _qAnim.setFromAxisAngle(_axisZ, rollZ);
  _qPart.setFromAxisAngle(_axisX, pitchX);
  _qAnim.multiply(_qPart);
}

function rng() {
  rngState = (rngState * 1664525 + 1013904223) % 4294967296;
  return rngState / 4294967296;
}

export function captureBegin() {
  savedClock = animClock;
  capturing = true;
  captureClock = 0;
  resetMotion();
}

export function renderAtPhase(t) {
  if (!renderer) return;
  animClock = t;
  for (const slot of slots.values()) if (slot.mixer) slot.mixer.setTime(t);
  advanceMotionTo(t);
  renderer.render(scene, camera);
}

export function captureEnd() {
  capturing = false;
  animClock = savedClock;
  captureClock = 0;
  resetMotion();
}

function advanceMotionTo(t) {
  if (!motionCfg) return;
  if (t < captureClock) {
    resetMotion();
    captureClock = 0;
  }
  while (captureClock < t) {
    const dt = Math.min(1 / 60, t - captureClock);
    stepMotion(dt);
    captureClock += dt;
  }
}

export function anyAnimated() {
  for (const slot of slots.values()) if (slot.durations.length > 0) return true;
  return !!motionCfg && (vehicleActors.length > 0 || chickenActors.length > 0);
}

export function animPeriod() {
  let longest = 0;
  for (const slot of slots.values()) {
    for (const d of slot.durations) if (d > longest) longest = d;
  }
  return longest > 0 ? longest : ANIM_PERIOD_FALLBACK;
}

function aspect() {
  const c = renderer.domElement;
  return (c.clientWidth || 1) / (c.clientHeight || 1);
}

function resize() {
  if (!renderer) return;
  const c = renderer.domElement;
  const w = c.clientWidth;
  const h = c.clientHeight;
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
  if (resizeObserver) {
    resizeObserver.disconnect();
    resizeObserver = null;
  }
  for (const slot of slots.values()) {
    if (slot.mixer) slot.mixer.stopAllAction();
    if (scene) scene.remove(slot.root);
  }
  slots.clear();
  clearMotionActors();
  motionCfg = null;
  if (motionRoot && scene) scene.remove(motionRoot);
  motionRoot = null;
  for (const pending of meshCache.values()) pending.then(src => disposeObject(src.scene), () => {});
  meshCache.clear();
  if (vertexColorMat) vertexColorMat.dispose();
  if (plainMat) plainMat.dispose();
  vertexColorMat = null;
  plainMat = null;
  if (renderer) renderer.dispose();
  renderer = null;
  scene = null;
  camera = null;
  controls = null;
  sun = null;
  ambient = null;
  framedOnce = false;
  capturing = false;
  window.__pgEngine = null;
}

function field(obj, snake, camel) {
  if (!obj) return undefined;
  const v = obj[snake];
  return v === undefined ? obj[camel] : v;
}

function numOr(value, fallback) {
  const n = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function numOrNull(value) {
  if (value === undefined || value === null || value === '') return null;
  const n = Number(value);
  return Number.isFinite(n) ? n : null;
}

function clamp(v, lo, hi) {
  if (v < lo) return lo;
  if (v > hi) return hi;
  return v;
}

function clamp01(v) {
  return clamp(numOr(v, 0), 0, 1);
}

function readVec3(value) {
  if (Array.isArray(value)) return [numOr(value[0], 0), numOr(value[1], 0), numOr(value[2], 0)];
  if (value && typeof value === 'object') return [numOr(value.x, 0), numOr(value.y, 0), numOr(value.z, 0)];
  if (typeof value === 'number') return [value, 0, 0];
  return [0, 0, 0];
}

function readVecOrNull(value) {
  if (value === undefined || value === null) return null;
  return readVec3(value);
}
