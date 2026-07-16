
const warned = new Set();
function warnOnce(key, msg) { if (!warned.has(key)) { warned.add(key); console.warn('effectEval:', msg); } }
const FIELD_BINDINGS = {
  'x8@80': (env) => env.t,
  'x19@36': (env) => env.particleIndex,
};

function fieldKey(node) { return (node.base || '') + '@' + (node.offset || 0); }

export function evalExpr(node, env) {
  if (!node) return 0;
  switch (node.op) {
    case 'Const': return node.v;
    case 'Input': return env[node.name] ?? 0;
    case 'Field': {
      const k = fieldKey(node);
      const bind = FIELD_BINDINGS[k];
      if (bind) return bind(env);
      warnOnce('field:' + k, 'unresolved struct field ' + k + ' -> 0');
      return 0;
    }
    case 'Neg': return -evalExpr(node.x, env);
    case 'Sin': return Math.sin(evalExpr(node.x, env));
    case 'Cos': return Math.cos(evalExpr(node.x, env));
    case 'Sqrt': return Math.sqrt(evalExpr(node.x, env));
    case 'Abs': return Math.abs(evalExpr(node.x, env));
    case 'Floor': return Math.floor(evalExpr(node.x, env));
    case 'Add': return evalExpr(node.a, env) + evalExpr(node.b, env);
    case 'Sub': return evalExpr(node.a, env) - evalExpr(node.b, env);
    case 'Mul': return evalExpr(node.a, env) * evalExpr(node.b, env);
    case 'Div': { const d = evalExpr(node.b, env); return d === 0 ? 0 : evalExpr(node.a, env) / d; }
    case 'Min': return Math.min(evalExpr(node.a, env), evalExpr(node.b, env));
    case 'Max': return Math.max(evalExpr(node.a, env), evalExpr(node.b, env));
    case 'Mod': { const d = evalExpr(node.b, env); return d === 0 ? 0 : evalExpr(node.a, env) % d; }
    case 'Select': return evalExpr(node.cond, env) ? evalExpr(node.a, env) : evalExpr(node.b, env);
    case 'Index': { const v = node.vec; return v && v.op === 'Vec' ? evalExpr(v.lanes[node.lane], env) : 0; }
    case 'Opaque': warnOnce('op:' + node.call, 'opaque call ' + node.call + ' -> 0'); return 0;
    default: warnOnce('unk:' + node.op, 'unknown op ' + node.op + ' -> 0'); return 0;
  }
}
export function evalMatrix(node, env) {
  const m = new Float32Array(16);
  m[0] = m[5] = m[10] = m[15] = 1;
  if (node && node.op === 'MatrixBuild' && Array.isArray(node.cells) && node.cells.length === 16) {
    for (let i = 0; i < 16; i++) m[i] = evalExpr(node.cells[i], env);
  }
  return m;
}
