import assert from 'node:assert';
import { splineLength, sampleSpline, tangentAt } from './playgroundMotion.js';

// A straight 10-unit segment along X.
const straight = [[0, 0, 0], [10, 0, 0]];
assert.ok(Math.abs(splineLength(straight) - 10) < 0.5, 'straight length ~10');

const start = sampleSpline(straight, 0);
assert.ok(Math.abs(start[0] - 0) < 0.01, 't=0 at start x');
const end = sampleSpline(straight, splineLength(straight));
assert.ok(Math.abs(end[0] - 10) < 0.5, 't=len at end x');
const mid = sampleSpline(straight, splineLength(straight) / 2);
assert.ok(mid[0] > 2 && mid[0] < 8, 'midpoint between');

const tan = tangentAt(straight, splineLength(straight) / 2);
assert.ok(Math.abs(tan[0] - 1) < 0.1 && Math.abs(tan[2]) < 0.1, 'tangent points +X');

// Clamp out of range.
assert.deepStrictEqual(sampleSpline(straight, -5).map(Math.round), [0, 0, 0], 'clamp low');
assert.deepStrictEqual(sampleSpline(straight, 999).map(Math.round), [10, 0, 0], 'clamp high');

console.log('playgroundMotion OK');
