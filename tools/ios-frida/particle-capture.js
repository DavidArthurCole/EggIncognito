// particle-capture.js - live particle observation for Egg Inc on a jailbroken iPhone.
//
// Goal: capture the universe hatchery's floating particle effect, which is data-driven and not statically
// extractable (the static call graph can't attribute the particle asset to the building; see the binding-wall
// memory). Every visible particle's per-frame world transform + size passes through ParticleBatchedMesh::
// addParticle, so we hook that and log each call. The host clusters by mesh pointer to isolate one effect.
//
// Run on the phone (frida-server live): frida -U -l particle-capture.js -n "Egg, Inc."
// Writes NDJSON to OUT_PATH on the phone; the host scp's it back. Self-detaches after DURATION_MS.
//
// Hook target (resolved by symbol at runtime, NOT a fixed offset, since the device build differs from the
// analysis fixture): ParticleBatchedMesh::addParticle(Eigen::Transform<f,3,2>, float).
//   mangled: __ZN19ParticleBatchedMesh11addParticleEN5Eigen9TransformIfLi3ELi2ELi0EEEf
// arm64 AAPCS for this signature (verified by the static galaxy analysis: the matrix is stack-built and passed
// by pointer): x0 = this (the ParticleBatchedMesh*), x1 = pointer to the Transform (12 contiguous floats,
// column-major 3x4 affine), s0 = the float (size/alpha). If x1 turns out to hold the first floats by value the
// host fitter still sees garbage clearly, so we log raw and decide host-side.

const SYMBOL = '__ZN19ParticleBatchedMesh11addParticleEN5Eigen9TransformIfLi3ELi2ELi0EEEf';
const MODULE = 'egginc';
const OUT_PATH = '/var/root/particle-capture.ndjson';
const DURATION_MS = 5000; // capture window; the host triggers at the hatchery view
const MAX_RECORDS = 60000; // hard cap so a runaway frame rate can't fill the disk

// The device build is STRIPPED of the C++ symbol table (only ~3.4k SDK export stubs survive), so addParticle is
// not resolvable by name on-device (verified on egginc 1.36 stripped). Its file offset is recovered host-side by
// content-hash symbol recovery against an adjacent symbolized build (/api/decomp/resolve-va), passed in here as
// ADDR_OFFSET = the bytes from the __text vmaddr to the function = (recovered VA - text vmaddr). The runtime
// address is then module.base + ADDR_OFFSET. Set via the capturer (-P key=value) or hardcode after resolve-va.
const ADDR_OFFSET = (typeof addrOffset !== 'undefined' && addrOffset) ? addrOffset : null;

function resolveAddParticle() {
    // recovered-offset path (stripped device build): module.base + the host-recovered text offset.
    if (ADDR_OFFSET) {
        const m = Process.getModuleByName(MODULE);
        if (m) return m.base.add(ptr(ADDR_OFFSET));
    }
    // symbol path (only if a symbolized build is ever on-device): exact name, then a Contains scan.
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
    // 12 floats, column-major 3x4 (Eigen Transform<f,3,2> = AffineCompact = a 3x4 matrix). Read defensively;
    // a bad pointer yields NaNs the host filters out rather than crashing the app.
    const out = new Array(12);
    try {
        for (let i = 0; i < 12; i++) out[i] = ptrTransform.add(i * 4).readFloat();
    } catch (e) {
        return null;
    }
    return out;
}

// PROBE mode (set `const probe = true;` injected by the capturer, or default true on first run): on the first few
// hits, dump the raw arg registers as pointers + their first floats + s0-s3, so the host can SEE where the
// per-particle transform actually sits (addParticle takes it by pointer in x1; a lambda takes its closure in x0
// with the transform at an unknown field offset). Once the layout is known, bulk mode reads it directly.
const PROBE = (typeof probe !== 'undefined') ? probe : true;
const PROBE_HITS = 5;

// Defensive float-array read at a pointer; null on a bad read so a wrong guess never crashes the app.
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
                // dump the candidate locations so the host can identify the transform's home.
                const ctx = this.context;
                const rec = {
                    probe: count,
                    x0: args[0].toString(), x1: args[1].toString(),
                    x2: args[2].toString(), x3: args[3].toString(),
                    f_x0: floatsAt(args[0], 20), // closure/this candidate
                    f_x1: floatsAt(args[1], 16), // transform-by-pointer candidate (12 used)
                    f_x2: floatsAt(args[2], 16),
                    s: [ctx.s0, ctx.s1, ctx.s2, ctx.s3].map(function (v) { return v === undefined ? null : v; }),
                };
                out.write(JSON.stringify(rec) + '\n');
                count++;
                return;
            }

            // bulk mode: the transform is by pointer in x1 (addParticle's signature). The host overrides this read
            // offset once the probe confirms the real home; until then x1 is the documented contract.
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
