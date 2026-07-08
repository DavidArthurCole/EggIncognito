// Pure motion math for the playground: a Catmull-Rom curve through a list of [x,y,z] waypoints, sampled by
// arc length so movement speed is uniform. No three.js dependency, so it unit-tests in node.

const SUBDIVISIONS = 16;

function catmull(p0, p1, p2, p3, t) {
  const t2 = t * t, t3 = t2 * t;
  const out = [0, 0, 0];
  for (let i = 0; i < 3; i++) {
    out[i] = 0.5 * ((2 * p1[i]) + (-p0[i] + p2[i]) * t
      + (2 * p0[i] - 5 * p1[i] + 4 * p2[i] - p3[i]) * t2
      + (-p0[i] + 3 * p1[i] - 3 * p2[i] + p3[i]) * t3);
  }
  return out;
}

// Control points for segment i (between path[i] and path[i+1]); ends are duplicated so the curve passes
// through the first + last waypoint.
function controls(path, i) {
  const n = path.length;
  return [path[Math.max(0, i - 1)], path[i], path[Math.min(n - 1, i + 1)], path[Math.min(n - 1, i + 2)]];
}

function dist(a, b) {
  const dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
  return Math.sqrt(dx * dx + dy * dy + dz * dz);
}

// A flat list of {d, p} samples along the whole curve. Cached per path identity so repeated sampleSpline
// calls in the render loop do not rebuild it.
let _cachePath = null, _cacheTable = null;
function table(path) {
  if (path === _cachePath && _cacheTable) return _cacheTable;
  const pts = [];
  let d = 0;
  let prev = path[0];
  pts.push({ d: 0, p: prev });
  for (let i = 0; i < path.length - 1; i++) {
    const [p0, p1, p2, p3] = controls(path, i);
    for (let s = 1; s <= SUBDIVISIONS; s++) {
      const p = catmull(p0, p1, p2, p3, s / SUBDIVISIONS);
      d += dist(prev, p);
      pts.push({ d, p });
      prev = p;
    }
  }
  _cachePath = path; _cacheTable = pts;
  return pts;
}

export function splineLength(path) {
  if (!path || path.length < 2) return 0;
  const t = table(path);
  return t[t.length - 1].d;
}

export function sampleSpline(path, dQuery) {
  if (!path || path.length < 2) return path && path[0] ? path[0].slice() : [0, 0, 0];
  const t = table(path);
  const total = t[t.length - 1].d;
  const d = Math.max(0, Math.min(total, dQuery));
  // Linear scan (tables are small) for the bracketing samples, lerp between them.
  for (let i = 1; i < t.length; i++) {
    if (t[i].d >= d) {
      const a = t[i - 1], b = t[i];
      const span = b.d - a.d || 1;
      const f = (d - a.d) / span;
      return [a.p[0] + (b.p[0] - a.p[0]) * f, a.p[1] + (b.p[1] - a.p[1]) * f, a.p[2] + (b.p[2] - a.p[2]) * f];
    }
  }
  return t[t.length - 1].p.slice();
}

export function tangentAt(path, d) {
  const total = splineLength(path);
  const h = Math.max(0.01, total / 200);
  const a = sampleSpline(path, Math.max(0, d - h));
  const b = sampleSpline(path, Math.min(total, d + h));
  const dir = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
  const len = Math.sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]) || 1;
  return [dir[0] / len, dir[1] / len, dir[2] / len];
}
