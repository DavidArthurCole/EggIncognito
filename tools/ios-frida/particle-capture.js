// Live particle observation for Egg Inc on a jailbroken iPhone: hooks ParticleBatchedMesh::addParticle to
// log every visible particle's per-frame world transform + size. The host clusters by mesh pointer to
// isolate one effect.
//
// Run on the phone (frida-server live): frida -U -l particle-capture.js -n "Egg, Inc."
// Writes NDJSON to OUT_PATH on the phone; the host scp's it back. Self-detaches after DURATION_MS.
//
// arm64 AAPCS for ParticleBatchedMesh::addParticle(Eigen::Transform<f,3,2>, float): x0 = this, x1 = pointer
// to the Transform (12 contiguous floats, column-major 3x4 affine), s0 = the float (size/alpha).

const SYMBOL = '__ZN19ParticleBatchedMesh11addParticleEN5Eigen9TransformIfLi3ELi2ELi0EEEf';
const MODULE = 'egginc';
const OUT_PATH = '/var/root/particle-capture.ndjson';
const DURATION_MS = 5000;
const MAX_RECORDS = 60000;

// The device build is stripped of the C++ symbol table, so addParticle's address comes from host-side
// content-hash symbol recovery (/api/decomp/resolve-va), passed in as ADDR_OFFSET (bytes from __text vmaddr
// to the function). Runtime address = module.base + ADDR_OFFSET.
const ADDR_OFFSET = (typeof addrOffset !== 'undefined' && addrOffset) ? addrOffset : null;

function resolveAddParticle() {
    if (ADDR_OFFSET) {
        const m = Process.getModuleByName(MODULE);
        if (m) return m.base.add(ptr(ADDR_OFFSET));
    }
    // Symbol path, only useful if a symbolized build is ever on-device: exact name, then a scan.
    let p = null;
    try { p = Module.findGlobalExportByName(SYMBOL); } catch (e) {}
    if (p) return p;
    for (const m of Process.enumerateModules()) {
        if (!/egg/i.test(m.name)) continue;
        let syms;
        try { syms = m.enumerateSymbols(); } catch (e) { continue; }
        for (const s of syms) {
            if (s.name === SYMBOL && s.address && !s.address.isNull()) return s.address;
        }
    }
    return null;
}

function readTransform12(ptrTransform) {
    // 12 floats, column-major 3x4. Reads defensively so a bad pointer never crashes the app.
    const out = new Array(12);
    try {
        for (let i = 0; i < 12; i++) out[i] = ptrTransform.add(i * 4).readFloat();
    } catch (e) {
        return null;
    }
    return out;
}

// PROBE mode dumps the raw arg registers + s0-s3 on the first few hits, so the host can find where the
// per-particle transform actually sits before bulk mode reads it directly.
const PROBE = (typeof probe !== 'undefined') ? probe : true;
const PROBE_HITS = 5;

function floatsAt(p, n) {
    if (!p || p.isNull()) return null;
    const out = [];
    try { for (let i = 0; i < n; i++) out.push(p.add(i * 4).readFloat()); } catch (e) { return null; }
    return out;
}

const target = resolveAddParticle();
if (!target) {
    send({ kind: 'error', msg: 'addParticle not resolved (set addrOffset)' });
} else {
    const out = new File(OUT_PATH, 'w');
    let count = 0;
    let stopped = false;
    send({ kind: 'ready', addr: target.toString(), probe: PROBE });

    const listener = Interceptor.attach(target, {
        onEnter(args) {
            if (stopped || count >= MAX_RECORDS) return;

            if (PROBE && count < PROBE_HITS) {
                const ctx = this.context;
                const rec = {
                    probe: count,
                    x0: args[0].toString(), x1: args[1].toString(),
                    x2: args[2].toString(), x3: args[3].toString(),
                    f_x0: floatsAt(args[0], 20),
                    f_x1: floatsAt(args[1], 16),
                    f_x2: floatsAt(args[2], 16),
                    s: [ctx.s0, ctx.s1, ctx.s2, ctx.s3].map(function (v) { return v === undefined ? null : v; }),
                };
                out.write(JSON.stringify(rec) + '\n');
                count++;
                return;
            }

            const meshPtr = args[0];
            const xform = readTransform12(args[1]);
            if (!xform) return;
            let size = 0;
            try { size = this.context.s0 !== undefined ? this.context.s0 : 0; } catch (e) { size = 0; }
            out.write(JSON.stringify({ t: count, mesh: meshPtr.toString(), x: xform, s: size }) + '\n');
            count++;
        },
    });

    setTimeout(function () {
        stopped = true;
        try { listener.detach(); } catch (e) {}
        try { out.flush(); out.close(); } catch (e) {}
        send({ kind: 'done', records: count, path: OUT_PATH });
    }, DURATION_MS);
}
