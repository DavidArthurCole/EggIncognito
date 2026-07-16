

//
//


import { splineLength, sampleSpline, tangentAt } from './playgroundMotion.js';
import { evalExpr, evalMatrix } from './effectEval.js';

const THREE_URL = 'https://esm.sh/three@0.169.0';
const GLTF_URL = 'https://esm.sh/three@0.169.0/examples/jsm/loaders/GLTFLoader.js';
const ORBIT_URL = 'https://esm.sh/three@0.169.0/examples/jsm/controls/OrbitControls.js';

let THREE, GLTFLoader, OrbitControls;
let renderer, scene, camera, controls, clock, raf;
let sun, ambient, hemi, shadowCatcher, resizeObserver;
let designMode = false;

let animClock = 0;
let animPlaying = true;

const effects = new Map();
let capturing = false;
const ANIM_PERIOD = 6;


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

 
 
  sun = new THREE.DirectionalLight(0xffffff, 1.0);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.bias = -0.0002;
  sun.shadow.normalBias = 0.08;
 
  hemi = new THREE.HemisphereLight(0xeaf0ff, 0xddd7cc, 0.5);
  ambient = new THREE.AmbientLight(0xffffff, 0.55);
  scene.add(sun);
  scene.add(sun.target);
  scene.add(hemi);
  scene.add(ambient);

 
 
  shadowCatcher = new THREE.Mesh(
    new THREE.PlaneGeometry(400, 400),
    new THREE.ShadowMaterial({ opacity: 0.35 }));
  shadowCatcher.rotation.x = -Math.PI / 2;
  shadowCatcher.position.y = -0.02;
  shadowCatcher.receiveShadow = true;
  scene.add(shadowCatcher);

  setLighting({ sun: { azimuthDeg: 45, elevationDeg: 55, color: '#ffffff', intensity: 1.0 },
                fog: { color: '#1a1a1f', density: 0 } });

  clock = new THREE.Clock();
  window.addEventListener('resize', resize);
 
  resizeObserver = new ResizeObserver(() => resize());
  resizeObserver.observe(canvas);
 
  window.__pgEngine = {
    scene: _scene, camera: _camera, renderer: _renderer, controls: _controls,
    getGroupRoot, getGroupBase, getGroupCenterWorld, setGroupTransform, groupIdOf, groupRoots, setSelectionOutline,
    getGroupFootprint, getOtherFootprints, gridCellSize, gridSnapBlock, highlightCells, clearCellHighlight, surfaceYAt,
    captureBegin, renderAtPhase, captureEnd, anyAnimated, sceneBackgroundHex, animPeriod, captureCleanOutline,
    attachEffects, attachHatcheryParts, clearHatcheryParts,
  };
  loop();
}

function loop() {
  raf = requestAnimationFrame(loop);
  const dt = clock.getDelta();
  if (animPlaying && !capturing) animClock += dt;
  for (const g of groups.values()) {
    if (g.mixer) g.mixer.update(capturing ? 0 : dt);
    if (g.hatMixer) g.hatMixer.update(capturing ? 0 : dt);
    applyAnim(g);
  }
  updateEffects(animClock);
  orbitHatcheryParts(animClock);
  controls.update();
  renderer.render(scene, camera);
}

function applyAnim(g) {
  if (g.motion) { applyMotion(g); return; }
  const phase = (animClock / ANIM_PERIOD) * Math.PI * 2;
  let addRy = 0, addRz = 0, bob = 0;
  switch (g.anim) {
    case 'SpinY': addRy = phase; break;
    case 'SpinZ': addRz = phase; break;
    case 'HoverSpin': addRy = phase; bob = Math.sin(phase) * 0.15; break;
  }
 
  if (g.base) {
    const b = g.base;
    const s = b.scale || 1;
    g.root.scale.set(s, s, s);
    g.root.rotation.set(rad(b.rotDeg[0]), rad(b.rotDeg[1]) + addRy, rad(b.rotDeg[2]) + addRz);
    const p = pivotCorrected(g, b.pos[0], b.pos[1] + bob, b.pos[2], s);
    g.root.position.copy(p);
    g.root.visible = true;
    return;
  }
  const o = g.manual || g.autoOffset;
  g.root.rotation.set(0, addRy, addRz);
  const p = pivotCorrected(g, o.x, o.y + bob, o.z, 1);
  g.root.position.copy(p);
  g.root.visible = true;
}

function applyMotion(g) {
  g.root.visible = true;
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
  const count = Math.max(1, Math.round(m.count || 1));

 
 
  if (count <= 1 && !m.vehicle) {
    g.root.scale.set(baseScale, baseScale, baseScale);
    placeAlongPath(g.root, m, len, runnerDistance(m, len, 0, 1), baseRy);
    return;
  }

  ensureRunners(g, count, baseScale);

 
 
  if (m.vehicle) {
    const laneM = laneShifted(m, vehicleLaneOffset(m));
    const dists = vehicleConvoyDistances(m, len, count);
    for (let i = 0; i < count; i++) placeAlongPath(g.runners[i], laneM, len, dists[i], baseRy);
    return;
  }

 
  for (let i = 0; i < count; i++) {
    const lane = (i - (count - 1) / 2) * 1.2;
    const laneM = laneShifted(m, lane);
    placeAlongPath(g.runners[i], laneM, len, runnerDistance(m, len, i, count), baseRy);
  }
}

function vehicleLaneOffset(m) {
  if (m.depotZ == null || !isFinite(m.depotZ) || !m.path || m.path.length === 0) return 0;
  const roadZ = m.path[0][2];
  return Math.sign(m.depotZ - roadZ) * 1.5;
}

function vehicleConvoyDistances(m, len, count) {
  const speed = m.speed || 3;
  const dwell = Math.max(0, m.stopSeconds || 0);
  const gap = len / count;
  const minGap = Math.min(gap * 0.8, 4);

  const depotD = depotDistanceAlong(m, len);

  const base = ((animClock * speed) % (len + speed * dwell));
  const lead = depotAdjustedDistance(base, depotD, speed, dwell, len);

  const out = [];
  let prev = Infinity;
  for (let i = 0; i < count; i++) {
    let d = ((lead - i * gap) % len + len) % len;
    if (prev - d < minGap && prev - d >= 0) d = prev - minGap;
    if (d < 0) d += len;
    out.push(d);
    prev = d;
  }
  return out;
}
function depotDistanceAlong(m, len) {
  if (m.depotX == null || !isFinite(m.depotX)) return -1;
  const steps = 64;
  let best = -1, bestDx = Infinity;
  for (let s = 0; s <= steps; s++) {
    const d = (s / steps) * len;
    const p = sampleSpline(m.path, d);
    const dx = Math.abs(p[0] - m.depotX);
    if (dx < bestDx) { bestDx = dx; best = d; }
  }
  return best;
}


function depotAdjustedDistance(rawD, depotD, speed, dwell, len) {
  if (depotD < 0 || dwell <= 0) return rawD % len;
  const rampTime = 0.5;
  const brakeDist = speed * rampTime / 2;
  const dwellDist = speed * dwell;
  const decelStart = depotD - brakeDist;
  if (rawD <= decelStart) return rawD;
  if (rawD <= depotD) {
   
   
    const target = rawD - decelStart;
    const a = speed / (2 * rampTime);
    const disc = Math.max(0, speed * speed - 4 * a * target);
    const ce = (speed - Math.sqrt(disc)) / (2 * a);
    return decelStart + speed * ce - a * ce * ce;
  }
  if (rawD <= depotD + dwellDist) return depotD;
  const after = rawD - dwellDist;
  if (after <= depotD + brakeDist) {
    const ce = Math.sqrt(Math.max(0, (after - depotD) * 2 * rampTime / speed));
    const a = speed / (2 * rampTime);
    return depotD + a * ce * ce;
  }
  return after;
}

function runnerDistance(m, len, index, count) {
  const speed = m.speed || 3;
  const driveTime = len / speed;
  const stop = Math.max(0, m.stopSeconds || 0);
  const cycleTime = driveTime + stop;
  const stagger = cycleTime * (index / count);
  let tt = (animClock + stagger);
  if (m.loop === 'pingpong') {
    const full = 2 * cycleTime;
    let ph = ((tt % full) + full) % full;
    if (ph > cycleTime) ph = full - ph;
    return Math.min(1, ph / driveTime) * len;
  }
  let ph = ((tt % cycleTime) + cycleTime) % cycleTime;
  return Math.min(1, ph / driveTime) * len;
}

function placeAlongPath(obj, m, len, d, baseRy) {
  const p = sampleSpline(m.path, d);
  let ry = baseRy;
  if (m.facePath) {
    const t = tangentAt(m.path, d);
    ry = Math.atan2(t[0], t[2]) + (m.faceOffset || 0);
  }
  obj.rotation.set(0, ry, 0);
  obj.position.set(p[0], p[1], p[2]);
}

function laneShifted(m, laneZ) {
  if (!laneZ) return m;
  return { ...m, path: m.path.map(w => [w[0], w[1], w[2] + laneZ]) };
}

function ensureRunners(g, count, baseScale) {
  if (g.runners && g.runners.length === count) return;
  if (g.runners) for (const r of g.runners) g.root.remove(r);
  const source = g.root.children.find(c => c.type === 'Group' || c.isMesh) || g.root.children[0];
  g.runners = [];
 
  if (source) source.visible = false;
  for (let i = 0; i < count; i++) {
    const clone = source ? source.clone(true) : new THREE.Group();
    clone.visible = true;
    clone.scale.set(baseScale, baseScale, baseScale);
    g.root.add(clone);
    g.runners.push(clone);
  }
  g.root.position.set(0, 0, 0);
  g.root.rotation.set(0, 0, 0);
}

function easeOut(x) { return 1 - (1 - x) * (1 - x); }

function rad(d) { return (d || 0) * Math.PI / 180; }


export function attachEffects(groupId, models) {
  clearEffects(groupId);
  const g = groups.get(groupId);
  if (!g || !models || !models.length) return;
  const built = [];
  for (const model of models) {
    if (!model || !model.ok || !model.placement) continue;
    const count = Math.max(1, Math.min(4000, Math.round(evalExpr(model.count, { t: 0, particleIndex: 0, count: 0 })) || 1));
    const geo = new THREE.PlaneGeometry(0.15, 0.15);
    const mat = new THREE.MeshBasicMaterial({ color: 0xfff2a8, transparent: true, opacity: 0.9, side: THREE.DoubleSide, depthWrite: false });
    const mesh = new THREE.InstancedMesh(geo, mat, count);
    mesh.frustumCulled = false;
    g.root.add(mesh);
    built.push({ model, mesh, count });
  }
  if (built.length) effects.set(groupId, built);
}

function clearEffects(groupId) {
  const list = effects.get(groupId);
  if (!list) return;
  for (const e of list) { e.mesh.parent?.remove(e.mesh); e.mesh.geometry.dispose(); e.mesh.material.dispose(); }
  effects.delete(groupId);
}




const hatcheryParts = new Map();
function hash01(x) { const s = Math.sin(x * 12.9898) * 43758.5453; return s - Math.floor(s); }

function orbitBasis(ha, hb) {
  const phi = ha * Math.PI * 2;
  const ct = hb * 2 - 1, st = Math.sqrt(Math.max(0, 1 - ct * ct));
  const n = new THREE.Vector3(st * Math.cos(phi), st * Math.sin(phi), ct);
  const ref = Math.abs(n.y) < 0.9 ? new THREE.Vector3(0, 1, 0) : new THREE.Vector3(1, 0, 0);
  const u = new THREE.Vector3().crossVectors(n, ref).normalize();
  const v = new THREE.Vector3().crossVectors(n, u).normalize();
  return { u, v };
}

export async function attachHatcheryParts(groupId, model) {
  await ensureLibs();
  clearHatcheryParts(groupId);
  const g = groups.get(groupId);
  if (!g || !model || !Array.isArray(model.pieces)) return;

 
 
 
  g.root.updateWorldMatrix(true, true);
  const worldBox = new THREE.Box3().setFromObject(g.root);
  const rootWorld = new THREE.Vector3();
  g.root.getWorldPosition(rootWorld);
  const bodyBox = worldBox.isEmpty() ? worldBox : worldBox.clone().translate(rootWorld.clone().negate());

  const orb = new THREE.Vector3();
  let bodyHeight = 4;
  if (!bodyBox.isEmpty()) {
    bodyBox.getCenter(orb);
    bodyHeight = bodyBox.max.y - bodyBox.min.y;
    orb.y = bodyBox.min.y + bodyHeight * (model.orbYFrac || 0.78);
  } else if (Array.isArray(model.orb)) {
    orb.set(model.orb[0], model.orb[1], model.orb[2]);
  }

  const bodyCenter = new THREE.Vector3();
  if (!bodyBox.isEmpty()) bodyBox.getCenter(bodyCenter);
  const bodyTopY = bodyBox.isEmpty() ? 0 : bodyBox.max.y;

  const state = { probes: [], beams: [], spinners: [], statics: [], orb };
  const probeCount = Math.max(1, model.probeCount || 1);
  const orbitSpeed = model.orbitSpeed || 0;
  const orbitRadius = Math.max(model.orbitRadius || 0, bodyHeight * 0.42);
  const beam = model.beam || {};

  for (const piece of model.pieces) {
    let pg; try { pg = await parseGlb(piece.glb); } catch (e) { pg = null; }
    if (!pg) continue;
   
    applyHatcheryMaterial(pg.scene);
   
    const pBox = new THREE.Box3().setFromObject(pg.scene);
    if (!pBox.isEmpty()) { const pc = pBox.getCenter(new THREE.Vector3()); pg.scene.position.sub(pc); }
   
    const off = Array.isArray(piece.offset) ? piece.offset : [0, 0, 0];
    const px = bodyCenter.x + off[0], py = bodyCenter.y + off[1], pz = bodyCenter.z + off[2];

    if (piece.role === 'WorldPlaced') {
     
      const obj = new THREE.Group();
      obj.add(pg.scene);
      obj.position.set(px, py, pz);
      g.root.add(obj);
      state.statics.push({ obj, baseY: py, phase: state.statics.length * 0.7, amp: 0.4, speed: 1.2 });
      continue;
    }

    if (piece.role === 'Beam') {
     
      state._beamGlb = piece.glb;
      continue;
    }

    if (piece.role === 'Ring') {
     
     
      const obj = new THREE.Group();
      obj.add(pg.scene);
      obj.position.copy(orb);
      g.root.add(obj);
      const ri = state.spinners.filter(s => s.kind === 'ring').length;
      const ax = new THREE.Vector3(Math.cos(ri * 2.4), 0.5, Math.sin(ri * 2.4)).normalize();
      state.spinners.push({ obj, kind: 'ring', tumbleAxis: ax, speed: (orbitSpeed || 1) * (0.5 + ri * 0.35) });
      continue;
    }

    if (piece.role === 'Shell' || piece.role === 'Orb') {
     
      const obj = new THREE.Group();
      obj.add(pg.scene);
      if (piece.capstone) obj.position.set(bodyCenter.x, bodyTopY + (pBox.isEmpty() ? 0.2 : (pBox.max.y - pBox.min.y) * 0.5), bodyCenter.z);
      else obj.position.copy(orb);
      g.root.add(obj);
      state.spinners.push({ obj, kind: 'shell', axis: 'y', speed: (orbitSpeed || 0.5) * 0.4 });
      continue;
    }

   
    for (let k = 0; k < probeCount; k++) {
      const obj = new THREE.Group();
      obj.add(k === 0 ? pg.scene : pg.scene.clone());
      g.root.add(obj);
     
     
      const h1 = hash01(k * 2.399963 + 0.11), h2 = hash01(k * 5.781 + 0.37), h3 = hash01(k * 9.13 + 0.71);
      const { u, v } = orbitBasis(h1, h2);
      state.probes.push({
        obj, phase: h3 * Math.PI * 2,
        radius: orbitRadius * (0.7 + h1 * 0.6),
        speed: orbitSpeed * (0.6 + h2 * 0.8) * (h3 < 0.5 ? 1 : -1),
        u, v,
      });
    }
  }

 
  if (state._beamGlb && state.probes.length) {
    const beamCount = Math.min(3, state.probes.length);
    for (let i = 0; i < beamCount; i++) {
      let bg; try { bg = await parseGlb(state._beamGlb); } catch (e) { bg = null; }
      if (!bg) break;
      whiteBeamMaterial(bg.scene);
      const obj = new THREE.Group();
      obj.add(bg.scene);
      obj.visible = false;
      obj.matrixAutoUpdate = false;
      g.root.add(obj);
      const interval = beam.fireInterval > 0 ? beam.fireInterval : 2.5;
      state.beams.push({
        obj, probe: state.probes[i * Math.floor(state.probes.length / beamCount)],
        interval, duration: beam.fireDuration || 0.25,
        offset: (i / beamCount) * interval,
        random: !!beam.fireRandom, seed: (i + 1) * 1.6180339887,
      });
    }
  }
  delete state._beamGlb;

  if (state.probes.length || state.beams.length || state.spinners.length || state.statics.length)
    hatcheryParts.set(groupId, state);
}

export function clearHatcheryParts(groupId) {
  const st = hatcheryParts.get(groupId);
  if (!st) return;
  for (const p of st.probes) p.obj.parent?.remove(p.obj);
  for (const b of st.beams) b.obj.parent?.remove(b.obj);
  for (const s of st.spinners) s.obj.parent?.remove(s.obj);
  for (const s of st.statics) s.obj.parent?.remove(s.obj);
  hatcheryParts.delete(groupId);
}

let _beamUp, _beamDir, _beamPos, _beamQuat, _beamScale, _beamMat;
function orbitHatcheryParts(tSeconds) {
  if (hatcheryParts.size === 0) return;
  if (!_beamUp) {
    _beamUp = new THREE.Vector3(0, 1, 0); _beamDir = new THREE.Vector3(); _beamPos = new THREE.Vector3();
    _beamQuat = new THREE.Quaternion(); _beamScale = new THREE.Vector3(1, 1, 1); _beamMat = new THREE.Matrix4();
  }
  for (const st of hatcheryParts.values()) {
    for (const s of st.spinners) {
      if (s.tumbleAxis) s.obj.quaternion.setFromAxisAngle(s.tumbleAxis, tSeconds * s.speed);
      else if (s.axis === 'y') s.obj.rotation.y = tSeconds * s.speed;
      else s.obj.rotation.z = tSeconds * s.speed;
    }
    for (const s of st.statics) {
      if (s.baseY === undefined) continue;
      s.obj.position.y = s.baseY + Math.sin(tSeconds * s.speed + s.phase) * s.amp;
    }
    for (const p of st.probes) {
      const a = p.phase + tSeconds * p.speed;
      const c = Math.cos(a) * p.radius, s = Math.sin(a) * p.radius;
      const x = st.orb.x + p.u.x * c + p.v.x * s;
      const y = st.orb.y + p.u.y * c + p.v.y * s;
      const z = st.orb.z + p.u.z * c + p.v.z * s;
      p.obj.position.set(x, y, z);
      p.obj.lookAt(st.orb);
      p.obj.current = p.obj.current || new THREE.Vector3();
      p.obj.current.set(x, y, z);
    }
    for (const b of st.beams) {
     
      let interval = b.interval;
      if (b.random) {
        const cycle = Math.floor((tSeconds + b.offset) / b.interval);
        const r = (Math.sin((cycle + b.seed) * 12.9898) * 43758.5453) % 1;
        interval = b.interval * (0.5 + Math.abs(r));
      }
      const phase = ((tSeconds + b.offset) % interval);
      const firing = phase < b.duration;
      b.obj.visible = firing;
      if (!firing) continue;
     
      const from = b.probe.obj.current || b.probe.obj.position;
      _beamDir.copy(st.orb).sub(from);
      const len = _beamDir.length();
      if (len < 1e-4) { b.obj.visible = false; continue; }
      _beamDir.normalize();
      _beamPos.copy(from).addScaledVector(_beamDir, len * 0.5);
      _beamQuat.setFromUnitVectors(_beamUp, _beamDir);
      _beamScale.set(1, len / 2, 1);
      _beamMat.compose(_beamPos, _beamQuat, _beamScale);
      b.obj.matrix.copy(_beamMat);
    }
  }
}

let _effectMat;
function updateEffects(tSeconds) {
  if (effects.size === 0) return;
  if (!_effectMat) _effectMat = new THREE.Matrix4();
  for (const list of effects.values()) {
    for (const { model, mesh, count } of list) {
      for (let i = 0; i < count; i++) {
        _effectMat.fromArray(evalMatrix(model.placement, { t: tSeconds, particleIndex: i, count }));
        mesh.setMatrixAt(i, _effectMat);
      }
      mesh.instanceMatrix.needsUpdate = true;
    }
  }
}

let _pivotVec, _scaledC, _rotatedC;
function pivotCorrected(g, x, y, z, scale) {
  if (!_pivotVec) { _pivotVec = new THREE.Vector3(); _scaledC = new THREE.Vector3(); _rotatedC = new THREE.Vector3(); }
  const c = g.center;
  if (!c) return _pivotVec.set(x, y, z);
 
  _scaledC.set(c.x * scale, c.y * scale, c.z * scale);
  _rotatedC.copy(_scaledC).applyEuler(g.root.rotation);
  return _pivotVec.set(x + c.x - _rotatedC.x, y + c.y - _rotatedC.y, z + c.z - _rotatedC.z);
}

async function parseGlb(b64) {
  const buf = Uint8Array.from(atob(b64), c => c.charCodeAt(0)).buffer;
  return new GLTFLoader().parseAsync(buf, '');
}

let _emissiveBoost = 0.25;

function applyHatcheryMaterial(root) {
  root.traverse(o => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.receiveShadow = true;
    o.material = emissiveVertexMaterial();
  });
}
function whiteBeamMaterial(root) {
  root.traverse(o => {
    if (!o.isMesh) return;
    o.castShadow = false;
    o.material = new THREE.MeshBasicMaterial({ color: 0xffffff });
  });
}

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

export function setEmissiveBoost(v) {
  _emissiveBoost = typeof v === 'number' ? v : 0.25;
  for (const g of groups.values()) {
    g.root.traverse(o => {
      const mat = o.isMesh ? o.material : null;
      if (mat && mat.userData && mat.userData.egiEmissive && mat.userData.shaderRef)
        mat.userData.shaderRef.uniforms.uEmissiveBoost.value = _emissiveBoost;
    });
  }
}

let _batching = false;

export async function addGroupsBatch(items) {
  await ensureLibs();
  _batching = true;
  try {
    for (const it of items || []) {
      await addGroup(it.id, it.glbBase64, it.opts || {});
      if (it.transform) {
        const t = it.transform;
        setGroupTransform(it.id, t.pos || [0, 0, 0], t.rotDeg || [0, 0, 0], t.scale || 1);
      }
    }
  } finally {
    _batching = false;
  }
  relayoutGroups();
  refitShadow();
}

export async function addGroup(groupId, glbBase64, opts) {
  await ensureLibs();
  opts = opts || {};
 
  const carried = groups.get(groupId);
  removeGroup(groupId);

  const gltf = await parseGlb(glbBase64);
  const root = new THREE.Group();
  root.add(gltf.scene);
 
 
  root.visible = !!opts.pinned;

 
 
 
 
  const box = new THREE.Box3().setFromObject(gltf.scene);
  const center = box.isEmpty() ? new THREE.Vector3(0, 0, 0) : box.getCenter(new THREE.Vector3());

 
 
 
  if (opts.recenter && !box.isEmpty()) {
   
   
    const anchorX = opts.recenterX === 'min' ? box.min.x : center.x;
    gltf.scene.position.x -= anchorX;
    gltf.scene.position.z -= center.z;
    center.x -= anchorX; center.z = 0;
  }

 
 
  const snapBase = opts.snapBase !== false && !opts.pinned;
  if (snapBase && !box.isEmpty() && Number.isFinite(box.min.y)) {
    gltf.scene.position.y -= box.min.y;
    center.y -= box.min.y;
  }

 
 
  const localBox = new THREE.Box3().setFromObject(root);
  const localFootprint = localBox.isEmpty()
    ? { minX: -0.5, maxX: 0.5, minZ: -0.5, maxZ: 0.5, minY: 0 }
    : { minX: localBox.min.x, maxX: localBox.max.x, minZ: localBox.min.z, maxZ: localBox.max.z, minY: localBox.min.y };

  const mixer = new THREE.AnimationMixer(root);
  for (const clip of gltf.animations) mixer.clipAction(clip).play();
  const clipNames = gltf.animations.map(c => c.name);

  let hatMixer = null;
 
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

 
  const off = opts.offset || [0, 0, 0];
  groups.set(groupId, {
    root, mixer, hatMixer,
    autoOffset: { x: 0, y: 0, z: 0 },
    manual: opts.pinned ? { x: off[0] || 0, y: off[1] || 0, z: off[2] || 0 } : null,
    pinned: !!opts.pinned,
    center,
    localFootprint,
    snapBase,
    terrain: !!opts.terrain,
    recenter: !!opts.recenter,
    anim: carried?.anim || 'none',
    base: carried?.base || null,
    motion: carried?.motion || null,
  });
 
 
  root.traverse(o => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.receiveShadow = true;
    o.material = emissiveVertexMaterial();
  });
  scene.add(root);
 
  if (!_batching) {
    relayoutGroups();
    refitShadow();
  }
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
  refitShadow();
}

export function setGroupOffset(groupId, x, y, z) {
  const g = groups.get(groupId);
  if (!g) return;
  g.manual = { x: +x || 0, y: +y || 0, z: +z || 0 };
  applyOffset(g);
}

export function respaceHabRow(ids, gap, halfStep, z) {
  const g0 = gap == null ? 3 : +gap;
  const hs = halfStep == null ? 0.5 : +halfStep;
  const rowZ = z == null ? -10.5 : +z;
  const rowGroups = ids.map(id => groups.get(id)).filter(Boolean);
 
  const widths = rowGroups.map(g => {
    const f = g.localFootprint;
    const w = f ? (f.maxX - f.minX) * (g.base?.scale || 1) : 1;
    return Number.isFinite(w) && w > 0 ? w : 1;
  });
  const totalW = widths.reduce((a, w) => a + w, 0) + g0 * Math.max(0, widths.length - 1);
  let cursor = -totalW / 2;
  const out = [];
  rowGroups.forEach((g, i) => {
    const w = widths[i];
    const centerX = cursor + w * hs;
    g.base = g.base || { pos: [0, 0, 0], rotDeg: [0, 0, 0], scale: 1 };
    g.base.pos = [centerX, g.base.pos[1] || 0, rowZ];
    g.root.position.set(centerX, g.base.pos[1] || 0, rowZ);
    g.root.visible = true;
    out.push([centerX, rowZ]);
    cursor += w + g0;
  });
  return out;
}

export function repackZoneRow(ids, leftEdgeX, backEdgeZ, gap, rowGap) {
  const g0 = gap == null ? 2.5 : +gap;
  const rg = rowGap == null ? 2.5 : +rowGap;
  const rowGroups = ids.map(id => groups.get(id)).filter(Boolean);
  let cursor = leftEdgeX == null ? 2 : +leftEdgeX;
  const backZ = backEdgeZ == null ? 0 : +backEdgeZ;
  const out = [];
  let maxFrontZ = backZ;
  for (const g of rowGroups) {
    const f = g.localFootprint;
    const scale = g.base?.scale || 1;
    const minX = f ? f.minX * scale : -0.5, maxX = f ? f.maxX * scale : 0.5;
    const minZ = f ? f.minZ * scale : -0.5, maxZ = f ? f.maxZ * scale : 0.5;
    const w = maxX - minX;
    const baseX = cursor - minX;
    const baseZ = backZ - minZ;
    g.base = g.base || { pos: [0, 0, 0], rotDeg: [0, 0, 0], scale: 1 };
    g.base.pos = [baseX, g.base.pos[1] || 0, baseZ];
    g.root.position.set(baseX, g.base.pos[1] || 0, baseZ);
    g.root.visible = true;
    const frontZ = baseZ + maxZ;
    out.push([baseX, baseZ, cursor + w, frontZ]);
    maxFrontZ = Math.max(maxFrontZ, frontZ);
    cursor += w + g0;
  }
  return { positions: out, nextBackEdgeZ: maxFrontZ + rg };
}

export function relayoutGroups() {
  const all = [...groups.values()];
  if (all.length === 0) return;
 
  if (designMode) return;
  for (const g of all.filter(g => g.pinned)) applyOffset(g);

  const list = all.filter(g => !g.pinned);
  let maxW = 0;
  for (const g of list) {
    g.root.position.set(0, 0, 0);
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
export function setGroupAnimation(id, kind) {
  const g = groups.get(id);
  if (g) g.anim = kind || 'none';
}

export function setGroupMotion(id, motion) {
  const g = groups.get(id);
  if (!g) return;
  g.motion = motion && motion.kind ? motion : null;
}

export function setGroupTransform(id, pos, rotDeg, scale) {
  const g = groups.get(id);
  if (!g) return;
  g.base = { pos: [pos[0] || 0, pos[1] || 0, pos[2] || 0], rotDeg: [rotDeg[0] || 0, rotDeg[1] || 0, rotDeg[2] || 0], scale: scale || 1 };
  g.root.visible = true;
}

export function resetView() { frameScene(); }
export function setBackground(hex) {
  if (!scene) return;
  if (hex) { scene.background = new THREE.Color(hex); }
  else { scene.background = null; }
}
const TONE_MAPS = {
  none: () => THREE.NoToneMapping,
  linear: () => THREE.LinearToneMapping,
  aces: () => THREE.ACESFilmicToneMapping,
  reinhard: () => THREE.ReinhardToneMapping,
  cineon: () => THREE.CineonToneMapping,
};

let _sunDir = [0.5, 0.8, 0.5];

function refitShadow() {
  if (!sun) return;
 
 
  const all = [...groups.values()].filter(g => !g.motion && !g.terrain && (!g.anim || g.anim === 'none'));
  const box = new THREE.Box3();
  for (const g of all) box.expandByObject(g.root);
  const center = box.isEmpty() ? new THREE.Vector3(0, 0, 0) : box.getCenter(new THREE.Vector3());
  const sphere = box.isEmpty() ? null : box.getBoundingSphere(new THREE.Sphere());
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
export function refitShadows() { refitShadow(); }

export function setLighting(opts) {
  if (!sun || !ambient || !scene) return;
  const s = (opts && opts.sun) || {};
  const f = (opts && opts.fog) || {};

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
 
  const dx = Math.cos(el) * Math.sin(az), dy = Math.sin(el), dz = Math.cos(el) * Math.cos(az);
  const dl = Math.hypot(dx, dy, dz) || 1;
  _sunDir = [dx / dl, dy / dl, dz / dl];
  if (s.color) sun.color.set(s.color);
  if (typeof s.intensity === 'number') sun.intensity = s.intensity;

  refitShadow();

 
  const density = typeof f.density === 'number' ? f.density : 0;
  if (density > 0) {
    if (!scene.fog) scene.fog = new THREE.FogExp2(0xffffff, density);
    scene.fog.density = density;
    if (f.color) scene.fog.color.set(f.color);
  } else {
    scene.fog = null;
  }
}
export function setDesignMode(on) { designMode = !!on; }

let gridHelper = null;
let gridCell = 0;
const GRID_EXTENT = 80;
const GRID_MAX_LINES = 80;
export function setGrid(cellSize) {
  if (gridHelper) { scene.remove(gridHelper); gridHelper.geometry?.dispose(); gridHelper.material?.dispose(); gridHelper = null; }
  gridCell = Number.isFinite(cellSize) && cellSize > 0 ? cellSize : 0;
  clearCellHighlight();
  if (!scene || gridCell <= 0) return;
 
  const rawDiv = Math.round(GRID_EXTENT / gridCell);
  const divisions = Math.min(GRID_MAX_LINES, rawDiv);
  gridHelper = new THREE.GridHelper(GRID_EXTENT, divisions, 0x5a5a66, 0x3a3a42);
  gridHelper.material.transparent = true;
  gridHelper.material.opacity = 0.5;
  gridHelper.position.y = 0.01;
  scene.add(gridHelper);
}

export function gridCellSize() { return gridCell; }

let _downRay, _downOrigin, _downDir;
export function surfaceYAt(x, z, excludeId) {
  if (!scene) return 0;
  if (!_downRay) {
    _downRay = new THREE.Raycaster();
    _downOrigin = new THREE.Vector3();
    _downDir = new THREE.Vector3(0, -1, 0);
  }
  const targets = [];
  for (const [gid, g] of groups) {
    if (gid === excludeId || g.pinned) continue;
    targets.push(g.root);
  }
  if (targets.length === 0) return 0;
  _downOrigin.set(x, 1000, z);
  _downRay.set(_downOrigin, _downDir);
  const hits = _downRay.intersectObjects(targets, true);
  let topY = 0;
  for (const h of hits) if (h.point.y > topY) topY = h.point.y;
  return topY;
}

function blockCells(local, scale, x, z, cell) {
 
  const minWX = x + local.minX * scale, maxWX = x + local.maxX * scale;
  const minWZ = z + local.minZ * scale, maxWZ = z + local.maxZ * scale;
  const col0 = Math.round(minWX / cell);
  const row0 = Math.round(minWZ / cell);
  const spanC = Math.max(1, Math.ceil((maxWX - minWX) / cell - 1e-3));
  const spanR = Math.max(1, Math.ceil((maxWZ - minWZ) / cell - 1e-3));
  const cells = [];
  for (let dc = 0; dc < spanC; dc++)
    for (let dr = 0; dr < spanR; dr++) cells.push({ c: col0 + dc, r: row0 + dr });
  return { cells, col0, row0, centerX: (col0 + spanC / 2) * cell, centerZ: (row0 + spanR / 2) * cell, spanC, spanR };
}

function occupiedCells(excludeId, cell) {
  const taken = new Set();
  for (const [gid, g] of groups) {
    if (gid === excludeId || g.pinned || !g.recenter || !g.localFootprint) continue;
    const base = g.base || { pos: [g.root.position.x, 0, g.root.position.z], scale: 1 };
    const b = blockCells(g.localFootprint, base.scale || 1, base.pos[0], base.pos[2], cell);
    for (const cc of b.cells) taken.add(cc.c + ',' + cc.r);
  }
  return taken;
}

const ZONES = [
  { anchorX: -35, anchorZ: -2, width: 30, depth: 9 },
  { anchorX: -35, anchorZ: -12.5, width: 70, depth: 4 },
  { anchorX: 2, anchorZ: -4, width: 60, depth: 6 },
  { anchorX: 2, anchorZ: 5, width: 60, depth: 6 },
  { anchorX: 2, anchorZ: 10, width: 60, depth: 6 },
];

function insideAnyZone(x, z) {
  return ZONES.some(z0 => x >= z0.anchorX && x <= z0.anchorX + z0.width && z >= z0.anchorZ && z <= z0.anchorZ + z0.depth);
}

export function gridSnapBlock(id, x, z) {
  const g = groups.get(id);
 
  if (!g || !g.localFootprint || !g.recenter || gridCell <= 0) return { centerX: x, centerZ: z, valid: true, cells: [], reason: 'ok' };
  const scale = g.base?.scale || 1;
  const b = blockCells(g.localFootprint, scale, x, z, gridCell);
  const taken = occupiedCells(id, gridCell);
  const occupied = !b.cells.every(cc => !taken.has(cc.c + ',' + cc.r));
  const outsideZone = !insideAnyZone(b.centerX, b.centerZ);
  const reason = occupied ? 'occupied' : outsideZone ? 'outside-zone' : 'ok';
  return { centerX: b.centerX, centerZ: b.centerZ, valid: reason === 'ok', cells: b.cells, reason };
}
let cellHighlight = null;
export function highlightCells(cells, valid) {
  clearCellHighlight();
  if (!scene || gridCell <= 0 || !cells || cells.length === 0) return;
  const group = new THREE.Group();
  const color = valid ? 0x3fbf5f : 0xc0392b;
  const mat = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.35, depthWrite: false });
  const geo = new THREE.PlaneGeometry(gridCell * 0.94, gridCell * 0.94);
  for (const cc of cells) {
    const cx = (cc.c + 0.5) * gridCell, cz = (cc.r + 0.5) * gridCell;
   
    const y = (surfaceYAt(cx, cz, null) || 0) + 0.03;
    const q = new THREE.Mesh(geo, mat);
    q.rotation.x = -Math.PI / 2;
    q.position.set(cx, y, cz);
    group.add(q);
  }
  group.userData.sharedGeo = geo;
  group.userData.sharedMat = mat;
  scene.add(group);
  cellHighlight = group;
}

export function clearCellHighlight() {
  if (!cellHighlight) return;
  scene?.remove(cellHighlight);
  cellHighlight.userData.sharedGeo?.dispose();
  cellHighlight.userData.sharedMat?.dispose();
  cellHighlight = null;
}

export function gridBoxesForDomino(changedId, cellOverride) {
  const cell = (cellOverride > 0) ? cellOverride : gridCell;
  if (cell <= 0) return { changed: null, others: [] };
  let changed = null;
  const others = [];
  for (const [gid, g] of groups) {
   
    if (g.pinned || !g.recenter || !g.localFootprint) continue;
    const base = g.base || { pos: [g.root.position.x, 0, g.root.position.z], scale: 1 };
    const b = blockCells(g.localFootprint, base.scale || 1, base.pos[0], base.pos[2], cell);
    const box = { id: gid, col: b.col0, row: b.row0, spanC: b.spanC, spanR: b.spanR };
    if (gid === changedId) changed = box; else others.push(box);
  }
  return { changed, others };
}

export function applyDominoMoves(moves, cellOverride) {
  const cell = (cellOverride > 0) ? cellOverride : gridCell;
  const out = [];
  for (const m of moves || []) {
    const g = groups.get(m.id);
    if (!g || g.pinned || !g.recenter) continue;
    const base = g.base || { pos: [g.root.position.x, g.root.position.y, g.root.position.z], rotDeg: [0, 0, 0], scale: 1 };
    const pos = [base.pos[0] + m.deltaCol * cell, base.pos[1], base.pos[2] + m.deltaRow * cell];
    setGroupTransform(m.id, pos, base.rotDeg, base.scale);
    out.push({ id: m.id, pos });
  }
  return out;
}

export function getGroupRoot(id) {
  const g = groups.get(id);
  return g ? g.root : null;
}

export function getGroupBase(id) {
  const g = groups.get(id);
  if (!g) return null;
  if (g.base) return { pos: g.base.pos.slice(), rotDeg: g.base.rotDeg.slice(), scale: g.base.scale };
  const o = g.manual || g.autoOffset || { x: 0, y: 0, z: 0 };
  return { pos: [o.x, o.y, o.z], rotDeg: [0, 0, 0], scale: 1 };
}

export function listGroupIds() { return [...groups.keys()]; }

export function getGroupCenterWorld(id) {
  const g = groups.get(id);
  if (!g) return null;
  const c = g.center || new THREE.Vector3(0, 0, 0);
  const s = g.base?.scale || 1;
  const p = g.root.position;
  return [p.x + c.x * s, p.y + c.y * s, p.z + c.z * s];
}
export function getGroupFootprint(id) {
  const g = groups.get(id);
  return g?.localFootprint ? { ...g.localFootprint, clampFloor: g.snapBase !== false } : null;
}

export function getOtherFootprints(excludeId) {
  const out = [];
  for (const [gid, g] of groups) {
    if (gid === excludeId || g.pinned || !g.localFootprint) continue;
    const base = g.base || { pos: [g.root.position.x, g.root.position.y, g.root.position.z], rotDeg: [0, 0, 0], scale: 1 };
    out.push(worldFootprintOf(g.localFootprint, base.pos[0], base.pos[2], base.rotDeg?.[1] || 0, base.scale || 1));
  }
  return out;
}

function worldFootprintOf(local, x, z, rotYDeg, scale) {
  const hx = (local.maxX - local.minX) * 0.5 * scale;
  const hz = (local.maxZ - local.minZ) * 0.5 * scale;
  const cx = (local.minX + local.maxX) * 0.5 * scale;
  const cz = (local.minZ + local.maxZ) * 0.5 * scale;
  const a = rotYDeg * Math.PI / 180;
  const c = Math.abs(Math.cos(a)), s = Math.abs(Math.sin(a));
  const rhx = c * hx + s * hz, rhz = s * hx + c * hz;
  const rcx = cx * Math.cos(a) - cz * Math.sin(a);
  const rcz = cx * Math.sin(a) + cz * Math.cos(a);
  const ox = x + rcx, oz = z + rcz;
  return { minX: ox - rhx, maxX: ox + rhx, minZ: oz - rhz, maxZ: oz + rhz };
}

export function groupIdOf(obj) {
  for (const [id, g] of groups) {
    let o = obj;
    while (o) { if (o === g.root) return id; o = o.parent; }
  }
  return null;
}
export function groupRoots() { return [...groups.values()].map(g => g.root); }

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

let _savedClock = 0;
export function captureBegin() {
  _savedClock = animClock;
  capturing = true;
}

export function renderAtPhase(t) {
  if (!renderer) return;
  animClock = t;
 
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
export function anyAnimated() {
  for (const g of groups.values()) if ((g.anim && g.anim !== 'none') || g.motion) return true;
  return false;
}
export function sceneBackgroundHex() {
  if (!scene || !scene.background || !scene.background.getHexString) return null;
  return '#' + scene.background.getHexString();
}
export function animPeriod() { return ANIM_PERIOD; }
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
export function _scene() { return scene; }
export function _camera() { return camera; }
export function _renderer() { return renderer; }
export function _controls() { return controls; }

function frameScene() {
  if (groups.size === 0) return;
 
  const models = [...groups.values()].filter(g => !g.pinned);
  const framed = models.length > 0 ? models : [...groups.values()];
  const box = new THREE.Box3();
  for (const g of framed) box.expandByObject(g.root);
  if (box.isEmpty()) return;
  const sphere = box.getBoundingSphere(new THREE.Sphere());
 
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
  if (gridHelper) { gridHelper.geometry?.dispose(); gridHelper.material?.dispose(); gridHelper = null; }
  renderer?.dispose();
  renderer = scene = camera = controls = sun = ambient = hemi = shadowCatcher = null;
}
