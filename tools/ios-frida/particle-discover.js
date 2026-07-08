// One-shot runtime discovery of the per-particle function on the stripped device build: Stalker.follows the
// main thread for a short window, counts BLOCK entry hits per function-start in the egginc module, and dumps
// the top hits as module-relative offsets for the host to disassemble.
//
// Run with frida-server live, universe farm on screen, no --runtime=v8:
//   frida -H <device-ip> -p <PID> -l particle-discover.js
// Hold ~3s on the hatchery view, then Ctrl-C. Reads the top offsets back over the frida channel.

const WINDOW_MS = 2500;
const TOP = 40;

const m = Process.getModuleByName('egginc');
const lo = m.base;
const hi = m.base.add(m.size);
send({ kind: 'start', base: m.base.toString(), size: m.size });

const counts = new Map();
let total = 0;

const mainThread = Process.enumerateThreads()[0]; // main/render thread is thread 0 on iOS
send({ kind: 'thread', id: mainThread.id });

Stalker.follow(mainThread.id, {
    events: { call: false, ret: false, exec: false, block: true, compile: false },
    onReceive(events) {
        const parsed = Stalker.parse(events, { annotate: false, stringify: false });
        for (const ev of parsed) {
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
