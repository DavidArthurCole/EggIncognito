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

import { splineLength, sampleSpline, tangentAt } from './playgroundMotion.js';

const THREE_URL = 'https://esm.sh/three@0.169.0';
const GLTF_URL = 'https://esm.sh/three@0.169.0/examples/jsm/loaders/GLTFLoader.js';
const ORBIT_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/OrbitControls.js';

let THREE, GLTFLoader, OrbitControls;
let renderer, scene, camera, controls, clock, raf;
let sun, ambient, hemi, shadowCatcher, resizeObserver;
let designMode = false;

// procedural animation clock + global play/pause. Each group carries its OWN anim kind (per-element spin),
// composed on top of that element's placed transform. Distinct from a group's mixer (baked mesh clips).
let animClock = 0;
let animPlaying = true;
let capturing = false;
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

  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, preserveDrawingBuffer: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  // Tone mapping is OFF by default (punchy, un-faded colors that match the game's flat-shaded meshes). The
  // lighting panel can switch it on (ACES / Reinhard / ...) live via setLighting.
  renderer.toneMapping = THREE.NoToneMapping;
  renderer.toneMappingExposure = 1.0;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(45, aspect(), 0.01, 1000);
  camera.position.set(0, 1.2, 3);

  controls = new OrbitControls(camera, canvas);
  controls.enableDamping = true;

  resize();

  // Adjustable shadow-casting sun + a hemisphere sky/ground fill for a natural gradient + a tiny ambient floor
  // so emissive meshes never go fully black. Fog optional (density 0 = off). A default keeps a fresh scene lit.
  sun = new THREE.DirectionalLight(0xffffff, 1.0);
  sun.castShadow = true;
  sun.shadow.mapSize.set(4096, 4096);
  sun.shadow.bias = -0.0002;
  sun.shadow.normalBias = 0.08;
  // Near-neutral sky/ground so the fill does not tint the scene (a brown ground bounce read as "faded"). The
  // panel drives the intensities; ambient default is a strong-ish fill so flat meshes stay bright.
  hemi = new THREE.HemisphereLight(0xeaf0ff, 0xddd7cc, 0.5);
  ambient = new THREE.AmbientLight(0xffffff, 0.55);
  scene.add(sun);
  scene.add(sun.target);
  scene.add(hemi);
  scene.add(ambient);

  // An invisible ground plane that only receives shadow, so cast shadows land even when no farm-ground mesh is
  // placed. Sits at y=0 under everything.
  shadowCatcher = new THREE.Mesh(
    new THREE.PlaneGeometry(200, 200),
    new THREE.ShadowMaterial({ opacity: 0.35 }));
  shadowCatcher.rotation.x = -Math.PI / 2;
  shadowCatcher.position.y = 0;
  shadowCatcher.receiveShadow = true;
  scene.add(shadowCatcher);

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
  window.__pgEngine = {
    scene: _scene, camera: _camera, renderer: _renderer, controls: _controls,
    getGroupRoot, getGroupBase, setGroupTransform, groupIdOf, groupRoots, setSelectionOutline,
    captureBegin, renderAtPhase, captureEnd, anyAnimated, sceneBackgroundHex, animPeriod, captureCleanOutline,
  };
  loop();
}

let _shadowTick = 0;
function loop() {
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  if (animPlaying && !capturing) animClock += dt;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.update(capturing ? 0 : dt);
    if (g.hatMixer) g.hatMixer.update(capturing ? 0 : dt);
    applyAnim(g);
  }
  // Refit the shadow frustum to the (now positioned) groups a few times a second, so newly placed or moved
  // elements get tight shadows without striping. Cheap: a bbox over ~30 groups. Skipped during a capture.
  if (!capturing && (_shadowTick++ % 20) === 0) refitShadow();
  controls.update();
  renderer.render(scene, camera);
}

// Per-element procedural animation (spin / hover), composed on top of that element's base transform. Only the
// animated element moves; everything else holds still. The chicken + its hat share the group root, so they
// ride as one rigid unit.
function applyAnim(g) {
  if (g.motion) { applyMotion(g); return; }
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
    const s = b.scale || 1;
    g.root.scale.set(s, s, s);
    g.root.rotation.set(rad(b.rotDeg[0]), rad(b.rotDeg[1]) + addRy, rad(b.rotDeg[2]) + addRz);
    const p = pivotCorrected(g, b.pos[0], b.pos[1] + bob, b.pos[2], s);
    g.root.position.copy(p);
    return;
  }
  const o = g.manual || g.autoOffset;
  g.root.rotation.set(0, addRy, addRz);
  const p = pivotCorrected(g, o.x, o.y + bob, o.z, 1);
  g.root.position.copy(p);
}

// Path-follow or launch motion, composed on the group's base transform. Deterministic on animClock so the GIF
// recorder captures it. Path points are world-space, so the sampled point is used directly as the position.
function applyMotion(g) {
  const m = g.motion;
  const b = g.base;
  const baseScale = b && b.scale ? b.scale : 1;
  const baseRy = b ? rad(b.rotDeg[1]) : 0;
  const bx = b ? b.pos[0] : 0, by = b ? b.pos[1] : 0, bz = b ? b.pos[2] : 0;

  if (m.kind === 'launch') {
    const period = m.period || 6;
    const phase = (animClock % period) / period;
    const rise = m.height || 12;
    const up = phase < 0.7 ? easeOut(phase / 0.7) * rise : rise;
    g.root.scale.set(baseScale, baseScale, baseScale);
    g.root.rotation.set(0, baseRy, 0);
    g.root.position.set(bx, by + up, bz);
    return;
  }

  const len = splineLength(m.path);
  if (len <= 0) { g.root.position.set(bx, by, bz); return; }
  const speed = m.speed || 3;
  let d = animClock * speed;
  if (m.loop === 'pingpong') {
    const cycle = d % (2 * len);
    d = cycle <= len ? cycle : 2 * len - cycle;
  } else {
    d = d % len;
  }
  const p = sampleSpline(m.path, d);
  g.root.scale.set(baseScale, baseScale, baseScale);
  let ry = baseRy;
  if (m.facePath) {
    const t = tangentAt(m.path, d);
    ry = Math.atan2(t[0], t[2]);
  }
  g.root.rotation.set(0, ry, 0);
  g.root.position.set(p[0], p[1], p[2]);
}

function easeOut(x) { return 1 - (1 - x) * (1 - x); }

function rad(d) { return (d || 0) * Math.PI / 180; }

// The root position that keeps the mesh's center fixed under the current root rotation, so a spin pivots about
// the visual center instead of the (possibly off-origin) placement point. With no rotation it equals (x,y,z).
// Reuses scratch vectors to avoid per-frame allocation.
let _pivotVec, _scaledC, _rotatedC;
function pivotCorrected(g, x, y, z, scale) {
  if (!_pivotVec) { _pivotVec = new THREE.Vector3(); _scaledC = new THREE.Vector3(); _rotatedC = new THREE.Vector3(); }
  const c = g.center;
  if (!c) return _pivotVec.set(x, y, z);
  // root applies rotation then translation: worldCenter = pos + R*(c*scale). We want worldCenter == pos + c
  // (the unrotated placed center stays put), so pos = (x,y,z) + c - R*(c*scale).
  _scaledC.set(c.x * scale, c.y * scale, c.z * scale);
  _rotatedC.copy(_scaledC).applyEuler(g.root.rotation);
  return _pivotVec.set(x + c.x - _rotatedC.x, y + c.y - _rotatedC.y, z + c.z - _rotatedC.z);
}

async function parseGlb(b64) {
  const buf = Uint8Array.from(atob(b64), c => c.charCodeAt(0)).buffer;
  return new GLTFLoader().parseAsync(buf, '');
}

// A material that renders EI's per-vertex COLOR_0 emission as vibrant flat color while still casting/receiving
// shadow. vertexColors feeds the base color; onBeforeCompile adds that same color to emissive so the mesh
// reads at full saturation regardless of scene lights (the game look), with shadows still landing on it.
// `emissiveBoost` is exposed live so the lighting panel can dial the flat-vs-lit balance.
let _emissiveBoost = 0.85;
function emissiveVertexMaterial() {
  const m = new THREE.MeshStandardMaterial({ vertexColors: true, metalness: 0, roughness: 1 });
  m.onBeforeCompile = shader => {
    shader.uniforms.uEmissiveBoost = { value: _emissiveBoost };
    m.userData.shaderRef = shader;
    shader.fragmentShader = shader.fragmentShader
      .replace('#include <common>', '#include <common>\nuniform float uEmissiveBoost;\nvarying vec3 vEgiColor;')
      .replace('#include <emissivemap_fragment>',
        '#include <emissivemap_fragment>\ntotalEmissiveRadiance += vEgiColor * uEmissiveBoost;');
    shader.vertexShader = shader.vertexShader
      .replace('#include <common>', '#include <common>\nvarying vec3 vEgiColor;')
      .replace('#include <color_vertex>', '#include <color_vertex>\nvEgiColor = vec3(1.0);\n#ifdef USE_COLOR\nvEgiColor = color.rgb;\n#endif');
  };
  m.userData.egiEmissive = true;
  return m;
}

// Live tweak of how strongly the per-vertex emission shows (0 = fully lit/dull, 1 = flat vibrant). Walks every
// loaded mesh's material and updates its shader uniform without a rebuild.
export function setEmissiveBoost(v) {
  _emissiveBoost = typeof v === 'number' ? v : 0.85;
  for (const g of groups.values()) {
    g.root.traverse(o => {
      const mat = o.isMesh ? o.material : null;
      if (mat && mat.userData && mat.userData.egiEmissive && mat.userData.shaderRef)
        mat.userData.shaderRef.uniforms.uEmissiveBoost.value = _emissiveBoost;
    });
  }
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

  // The mesh's bbox center in the group's local space. A procedural spin pivots about THIS, not the group
  // origin, so an off-origin-authored mesh (env buildings sit at their plot offset) spins in place rather
  // than orbiting the placement point.
  const box = new THREE.Box3().setFromObject(gltf.scene);
  const center = box.isEmpty()
    ? new THREE.Vector3(0, 0, 0)
    : box.getCenter(new THREE.Vector3());

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
    center,
    // preserve anim + placement across a re-render (reshell / hat change keeps spin + position).
    anim: carried?.anim || 'none',
    base: carried?.base || null,
    motion: carried?.motion || null,
  });
  // Fix up materials + shadow flags. The decoded glb carries NO material, only a COLOR_0 attribute that is
  // EI's per-vertex EMISSION. GLTFLoader's default material is MeshStandardMaterial(metal=1, rough=1) which
  // ignores that color and renders dark/desaturated ("faded"). Rebuild each mesh's material so the vertex
  // emission shows as vibrant flat color (matches the in-game unlit look) while the surface still takes a
  // little shading + casts/receives shadow.
  root.traverse(o => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.receiveShadow = true;
    o.material = emissiveVertexMaterial();
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
  // design mode: every group holds its own transform; no auto-offset layout AND no auto-framing. The user
  // owns the camera; framing on each add yanked the view (a far-offset env piece zoomed the camera way out).
  // The explicit "Reset view" button (resetView) frames on demand.
  if (designMode) return;
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

// Attaches (or clears) a motion descriptor to a group: a path-follow (chicken / vehicle) or a launch (rocket).
// Motion composes on the group's base transform in applyAnim. null clears it. See applyMotion for the shape.
export function setGroupMotion(id, motion) {
  const g = groups.get(id);
  if (!g) return;
  g.motion = motion && motion.kind ? motion : null;
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
const TONE_MAPS = {
  none: () => THREE.NoToneMapping,
  linear: () => THREE.LinearToneMapping,
  aces: () => THREE.ACESFilmicToneMapping,
  reinhard: () => THREE.ReinhardToneMapping,
  cineon: () => THREE.CineonToneMapping,
};

// The current sun direction (unit vector from target toward the sun) as a plain [x,y,z], kept so refitShadow
// can reposition the sun relative to the scene center. Plain array (not a THREE.Vector3) so it is safe at
// module load before THREE is imported in init().
let _sunDir = [0.5, 0.8, 0.5];

// Fit the directional light + its ortho shadow frustum to the elements actually in the scene. Targets the
// scene center (not origin) and sizes the frustum to the content bbox, clamped so one far outlier (the ~100u
// hyperloop track) does not blow the frustum up and stripe the shadow map. Keeps texel density high -> crisp
// shadows at any zoom. No groups -> a sane default box at the origin.
function refitShadow() {
  if (!sun) return;
  const all = [...groups.values()];
  const box = new THREE.Box3();
  for (const g of all) box.expandByObject(g.root);
  const center = box.isEmpty() ? new THREE.Vector3(0, 0, 0) : box.getCenter(new THREE.Vector3());
  const sphere = box.isEmpty() ? null : box.getBoundingSphere(new THREE.Sphere());
  // Clamp the covered radius: the farm core is ~30u; cap so the hyperloop outlier does not stretch the map.
  const radius = Math.min(sphere ? sphere.radius : 30, 45);

  sun.target.position.copy(center);
  sun.target.updateMatrixWorld();
  const dist = Math.max(radius * 2.5, 60);
  sun.position.set(center.x + _sunDir[0] * dist, center.y + _sunDir[1] * dist, center.z + _sunDir[2] * dist);

  const cam = sun.shadow.camera;
  cam.left = -radius; cam.right = radius; cam.top = radius; cam.bottom = -radius;
  cam.near = 0.5; cam.far = dist + radius * 2;
  cam.updateProjectionMatrix();
  sun.shadow.needsUpdate = true;
}

// Public hook so the element add/remove path can refit the shadow without a full setLighting round-trip.
export function refitShadows() { refitShadow(); }

export function setLighting(opts) {
  if (!sun || !ambient || !scene) return;
  const s = (opts && opts.sun) || {};
  const f = (opts && opts.fog) || {};

  // Renderer tone mapping + exposure (default none = punchy, un-faded). Fill intensities for the flat meshes.
  if (renderer) {
    const tm = TONE_MAPS[opts && opts.toneMapping] || TONE_MAPS.none;
    renderer.toneMapping = tm();
    if (typeof (opts && opts.exposure) === 'number') renderer.toneMappingExposure = opts.exposure;
  }
  if (typeof (opts && opts.ambient) === 'number') ambient.intensity = opts.ambient;
  if (hemi && typeof (opts && opts.hemi) === 'number') hemi.intensity = opts.hemi;
  if (typeof (opts && opts.emissive) === 'number') setEmissiveBoost(opts.emissive);
  const az = (s.azimuthDeg || 0) * Math.PI / 180;
  const el = (s.elevationDeg || 0) * Math.PI / 180;
  // Sun direction from azimuth (around Y) + elevation (up from horizon). refitShadow places the sun at
  // center + dir*dist so the shadow frustum stays tight around the content regardless of where it sits.
  const dx = Math.cos(el) * Math.sin(az), dy = Math.sin(el), dz = Math.cos(el) * Math.cos(az);
  const dl = Math.hypot(dx, dy, dz) || 1;
  _sunDir = [dx / dl, dy / dl, dz / dl];
  if (s.color) sun.color.set(s.color);
  if (typeof s.intensity === 'number') sun.intensity = s.intensity;

  // Fit the shadow frustum to where the elements actually are (refit on lighting + element change). A fixed
  // frustum either clipped shadows or, when sized for the worst case, stretched the 4096 map across a huge area
  // so it striped/acned when zoomed out.
  refitShadow();

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

// Deterministic capture for the GIF recorder. captureBegin freezes the live clock; renderAtPhase sets the
// absolute animation time + renders one frame; captureEnd restores. The recorder steps phases 0..period to
// grab one perfect loop without depending on wall-clock timing.
let _savedClock = 0;
export function captureBegin() {
  _savedClock = animClock;
  capturing = true;
}

export function renderAtPhase(t) {
  if (!renderer) return;
  animClock = t;
  // step every mixer to absolute time t (setTime gives a deterministic pose, unlike incremental update).
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.setTime(t);
    if (g.hatMixer) g.hatMixer.setTime(t);
    applyAnim(g);
  }
  renderer.render(scene, camera);
}

export function captureEnd() {
  capturing = false;
  animClock = _savedClock;
}

// True if any element has a procedural animation set (the Record gate).
export function anyAnimated() {
  for (const g of groups.values()) if ((g.anim && g.anim !== 'none') || g.motion) return true;
  return false;
}

// The scene's solid background as #rrggbb, or null when transparent (so the recorder uses a fallback bg).
export function sceneBackgroundHex() {
  if (!scene || !scene.background || !scene.background.getHexString) return null;
  return '#' + scene.background.getHexString();
}

// One full loop period in seconds (the procedural animation period).
export function animPeriod() { return ANIM_PERIOD; }

// Around a capture, remove the selection outline so the recorded frames are clean. Restores it after.
let _outlinedBeforeCapture = null;
export function captureCleanOutline(on) {
  if (on) {
    _outlinedBeforeCapture = null;
    for (const [id, g] of groups) if (g._outline) { _outlinedBeforeCapture = id; setSelectionOutline(id, false); }
  } else if (_outlinedBeforeCapture) {
    setSelectionOutline(_outlinedBeforeCapture, true);
    _outlinedBeforeCapture = null;
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
  // Clamp the framing radius so one giant outlier (the hyperloop track spans ~100u) does not yank the camera
  // miles back. The farm core is ~25u; cap there so reset/load lands close to the buildings.
  const r = Math.min(sphere.radius || 1, 28);
  controls.target.copy(sphere.center);
  const dist = r / Math.sin((camera.fov * Math.PI / 180) / 2) * 0.9;
  camera.position.set(sphere.center.x, sphere.center.y + r * 0.5, sphere.center.z + dist);
  camera.near = Math.max(0.05, r / 100);
  camera.far = Math.max(2000, r * 100);
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
  if (shadowCatcher) { shadowCatcher.geometry?.dispose(); shadowCatcher.material?.dispose(); }
  renderer?.dispose();
  renderer = scene = camera = controls = sun = ambient = hemi = shadowCatcher = null;
}
