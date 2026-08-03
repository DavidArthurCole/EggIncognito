const EOCD_SIG = 0x06054b50;
const CEN_SIG = 0x02014b50;
const LOC_SIG = 0x04034b50;

async function bytes(blob, start, end) {
  return new Uint8Array(await blob.slice(start, end).arrayBuffer());
}

async function findEocd(blob) {
  const size = blob.size;
  if (size < 22) return null;
  const back = Math.min(size, 65557);
  const buf = await bytes(blob, size - back, size);
  const dv = new DataView(buf.buffer);
  for (let i = buf.length - 22; i >= 0; i--) {
    if (dv.getUint32(i, true) === EOCD_SIG) {
      const count = dv.getUint16(i + 10, true);
      const cdSize = dv.getUint32(i + 12, true);
      const cdOffset = dv.getUint32(i + 16, true);
      if (cdOffset === 0xffffffff || count === 0xffff || cdSize === 0xffffffff) return null;
      return { cdOffset, cdSize };
    }
  }
  return null;
}

async function centralDir(blob, eocd) {
  const buf = await bytes(blob, eocd.cdOffset, eocd.cdOffset + eocd.cdSize);
  const dv = new DataView(buf.buffer);
  const dec = new TextDecoder();
  const entries = [];
  let o = 0;
  while (o + 46 <= buf.length && dv.getUint32(o, true) === CEN_SIG) {
    const method = dv.getUint16(o + 10, true);
    const compSize = dv.getUint32(o + 20, true);
    const nameLen = dv.getUint16(o + 28, true);
    const extraLen = dv.getUint16(o + 30, true);
    const commentLen = dv.getUint16(o + 32, true);
    const localOffset = dv.getUint32(o + 42, true);
    const name = dec.decode(buf.subarray(o + 46, o + 46 + nameLen));
    entries.push({ name, method, compSize, localOffset });
    o += 46 + nameLen + extraLen + commentLen;
  }
  return entries;
}

async function entryBlob(blob, entry) {
  const lh = await bytes(blob, entry.localOffset, entry.localOffset + 30);
  const dv = new DataView(lh.buffer);
  if (dv.getUint32(0, true) !== LOC_SIG) return null;
  const nameLen = dv.getUint16(26, true);
  const extraLen = dv.getUint16(28, true);
  const dataStart = entry.localOffset + 30 + nameLen + extraLen;
  const comp = blob.slice(dataStart, dataStart + entry.compSize);
  if (entry.method === 0) return comp;
  if (entry.method === 8) {
    if (typeof DecompressionStream === "undefined") return null;
    const stream = comp.stream().pipeThrough(new DecompressionStream("deflate-raw"));
    return await new Response(stream).blob();
  }
  return null;
}

function lower(e) {
  return e.name.toLowerCase();
}

function pickBundle(entries) {
  return entries.find(e => lower(e).endsWith(".apk") && lower(e).includes("arm64"))
    || entries.find(e => lower(e).endsWith(".apk")
      && (lower(e).includes("armeabi") || lower(e).includes("_v7a")))
    || null;
}

function pickAndroid(entries) {
  const preds = [
    e => lower(e).endsWith("/libegginc.so") && lower(e).includes("arm64"),
    e => lower(e).endsWith("/libegginc.so"),
    e => lower(e).includes("arm64") && lower(e).endsWith(".so"),
    e => lower(e).endsWith(".so")
  ];
  for (const p of preds) {
    const m = entries.find(p);
    if (m) return m;
  }
  return null;
}

function pickIos(entries) {
  const exec = entries.find(e => {
    const f = e.name;
    const fl = f.toLowerCase();
    if (!fl.startsWith("payload/")) return false;
    const i = fl.indexOf(".app/");
    if (i < 0) return false;
    const rest = f.slice(i + 5);
    return rest.length > 0 && !rest.includes("/") && !rest.includes(".");
  });
  if (exec) return exec;
  return entries.find(e => {
    const fl = e.name.toLowerCase();
    const base = e.name.split("/").pop();
    return fl.startsWith("payload/") && fl.includes(".framework/")
      && !fl.endsWith("/") && !base.includes(".");
  }) || null;
}

async function strip(blob) {
  try {
    const eocd = await findEocd(blob);
    if (!eocd) return blob;
    const entries = await centralDir(blob, eocd);
    if (entries.length === 0) return blob;

    const bundle = pickBundle(entries);
    if (bundle) {
      const inner = await entryBlob(blob, bundle);
      if (!inner) return blob;
      const stripped = await strip(inner);
      return stripped || inner;
    }

    const pick = pickAndroid(entries) || pickIos(entries);
    if (!pick) return blob;
    const out = await entryBlob(blob, pick);
    return out || blob;
  } catch {
    return blob;
  }
}

function inputFiles(inputId) {
  const el = document.getElementById(inputId);
  return el?.files ? Array.from(el.files) : [];
}

async function postForm(endpoint, form) {
  const res = await fetch(endpoint, { method: "POST", body: form, credentials: "same-origin" });
  let json = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  if (!res.ok) return { error: json?.error ? json.error : `HTTP ${res.status}` };
  return json ?? { error: "empty response" };
}

export async function analyze(inputId, endpoint) {
  const files = inputFiles(inputId);
  if (files.length === 0) return { ok: false, diagnostics: "no file selected" };
  const file = files[0];
  const body = await strip(file);
  const form = new FormData();
  form.append("file", body, file.name);
  const r = await postForm(endpoint, form);
  if (r?.error) return { ok: false, diagnostics: r.error };
  return r;
}

export async function uploadBatch(inputId, endpoint) {
  const files = inputFiles(inputId);
  if (files.length === 0) return { error: "no files selected" };
  const form = new FormData();
  for (const file of files) {
    const body = await strip(file);
    form.append("files", body, file.name);
  }
  return await postForm(endpoint, form);
}
