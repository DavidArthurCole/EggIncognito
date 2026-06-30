// particle-discover.js - one-shot runtime discovery of the per-particle function on the STRIPPED device build.
//
// The static path stalled: addParticle's body changed 1.35.6->1.36 so its symbol didn't recover, and the
// recovered "lambda" was a 72-byte std::function dispatch thunk that never fired. Rather than keep guessing
// addresses, OBSERVE what actually executes while the universe farm (with its hatchery particles) renders.
//
// Strategy: Stalker.follow the main thread for a short window, collect every BLOCK entry that lands in the egginc
// module, count hits per function-start. The hottest particle-region functions are the per-frame/per-particle
// work. Dump the top hits as module-relative offsets so the host can disassemble them + pick the transform sink.
//
// Run on frame (frida-server live, universe farm ON SCREEN, NO --runtime=v8):
//   frida -H 192.168.1.175 -p <PID> -l particle-discover.js
// Hold ~3s on the hatchery view, then Ctrl-C. Reads the top offsets back over the frida channel.

const WINDOW_MS = 2500;
const TOP = 40;

const m = Process.getModuleByName('egginc');
const lo = m.base;
const hi = m.base.add(m.size);
send({ kind: 'start', base: m.base.toString(), size: m.size });

const counts = new Map(); // module-relative block-start offset -> hit count
let total = 0;

const mainThread = Process.enumerateThreads()[0]; // the app's main/render thread is thread 0 on iOS
send({ kind: 'thread', id: mainThread.id });

Stalker.follow(mainThread.id, {
    events: { call: false, ret: false, exec: false, block: true, compile: false },
    onReceive(events) {
        const parsed = Stalker.parse(events, { annotate: false, stringify: false });
        for (const ev of parsed) {
            // block event = [start, end]; start is a NativePointer.
            const start = ev[0];
            if (start.compare(lo) < 0 || start.compare(hi) >= 0) continue;
            const off = start.sub(m.base).toString();
            counts.set(off, (counts.get(off) || 0) + 1);
            total++;
        }
    },
});

setTimeout(function () {
    try { Stalker.unfollow(mainThread.id); } catch (e) {}
    Stalker.flush();
    const top = Array.from(counts.entries())
        .sort(function (a, b) { return b[1] - a[1]; })
        .slice(0, TOP)
        .map(function (e) { return { off: e[0], hits: e[1] }; });
    send({ kind: 'done', totalBlocks: total, uniqueBlocks: counts.size, top: top });
}, WINDOW_MS);
