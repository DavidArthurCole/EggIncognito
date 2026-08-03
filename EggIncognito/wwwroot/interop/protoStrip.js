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

function axmlReadUtf8Len(b, pos) {
  const first = b[pos];
  if ((first & 0x80) !== 0) return [((first & 0x7f) << 8) | b[pos + 1], pos + 2];
  return [first, pos + 1];
}

function axmlReadUtf8String(b, pos) {
  const [, p] = axmlReadUtf8Len(b, pos);
  const [byteLen, next] = axmlReadUtf8Len(b, p);
  return new TextDecoder().decode(b.subarray(next, next + byteLen));
}

function axmlReadUtf16String(b, dv, pos) {
  let len = u16(dv, pos);
  let p = pos + 2;
  if ((len & 0x8000) !== 0) {
    len = ((len & 0x7fff) << 16) | u16(dv, p);
    p += 2;
  }
  return new TextDecoder("utf-16le").decode(b.subarray(p, p + len * 2));
}

function axmlStringPool(b, dv, chunkPos) {
  const stringCount = u32(dv, chunkPos + 8);
  const flags = u32(dv, chunkPos + 16);
  const stringsStart = u32(dv, chunkPos + 20);
  const isUtf8 = (flags & 0x100) !== 0;
  const offsetsBase = chunkPos + 28;
  const dataBase = chunkPos + stringsStart;
  const result = [];
  for (let i = 0; i < stringCount; i++) {
    const strPos = dataBase + u32(dv, offsetsBase + i * 4);
    result.push(isUtf8 ? axmlReadUtf8String(b, strPos) : axmlReadUtf16String(b, dv, strPos));
  }
  return result;
}

function axmlStartElementVersions(dv, chunkPos, headerSize, strings, len) {
  const ext = chunkPos + headerSize;
  if (ext + 20 > len) return null;
  const attrStart = u16(dv, ext + 8);
  const attrCount = u16(dv, ext + 12);
  const baseAttr = ext + attrStart;
  let versionName = null, versionCode = null;
  for (let a = 0; a < attrCount; a++) {
    const rec = baseAttr + a * 20;
    if (rec + 20 > len) break;
    const nameIdx = u32(dv, rec + 4);
    const rawValueIdx = u32(dv, rec + 8);
    const typedValue = u32(dv, rec + 12);
    const dataType = (typedValue >>> 24) & 0xff;
    const dataVal = u32(dv, rec + 16);
    const name = nameIdx < strings.length ? strings[nameIdx] : null;
    if (name === "versionName" && rawValueIdx < strings.length) versionName = strings[rawValueIdx];
    if (name === "versionCode" && dataType === 0x10) versionCode = String(dataVal);
  }
  return { versionName, versionCode };
}

function parseAxml(b) {
  const none = { versionName: null, versionCode: null };
  try {
    if (b.length < 8) return none;
    const dv = new DataView(b.buffer, b.byteOffset, b.byteLength);
    if (u16(dv, 0) !== 0x0003) return none;
    let pos = 8;
    let strings = null;
    let versionName = null, versionCode = null;
    while (pos + 8 <= b.length) {
      const type = u16(dv, pos);
      const headerSize = u16(dv, pos + 2);
      const size = u32(dv, pos + 4);
      if (size < 8 || pos + size > b.length) break;
      if (type === 0x0001) {
        strings = axmlStringPool(b, dv, pos);
      } else if (type === 0x0102 && strings) {
        const r = axmlStartElementVersions(dv, pos, headerSize, strings, b.length);
        if (r) {
          if (versionName === null) versionName = r.versionName;
          if (versionCode === null) versionCode = r.versionCode;
          if (versionName !== null && versionCode !== null) break;
        }
      }
      pos += size;
    }
    return { versionName, versionCode };
  } catch {
    return none;
  }
}

function plistShortVersion(text) {
  const keyTag = "<key>CFBundleShortVersionString</key>";
  const ki = text.indexOf(keyTag);
  if (ki < 0) return null;
  const open = text.indexOf("<string>", ki + keyTag.length);
  if (open < 0) return null;
  const start = open + "<string>".length;
  const close = text.indexOf("</string>", start);
  if (close < 0) return null;
  const val = text.slice(start, close).trim();
  return val.length === 0 ? null : val;
}

async function archiveMeta(blob) {
  const none = { appVersion: null, build: null };
  try {
    const eocd = await findEocd(blob);
    if (!eocd) return none;
    const entries = await centralDir(blob, eocd);
    if (entries.length === 0) return none;

    const bundle = pickBundle(entries);
    if (bundle) {
      const base = entries.find(e => lower(e) === "base.apk" || lower(e).endsWith("/base.apk"));
      const inner = await entryBlob(blob, base || bundle);
      if (!inner) return none;
      return await archiveMeta(inner);
    }

    const plist = entries.find(e => lower(e).startsWith("payload/") && lower(e).endsWith(".app/info.plist"));
    if (plist) {
      const data = await entryBlob(blob, plist);
      if (!data) return none;
      const text = new TextDecoder().decode(new Uint8Array(await data.arrayBuffer()));
      return { appVersion: plistShortVersion(text), build: null };
    }

    const manifest = entries.find(e => e.name === "AndroidManifest.xml");
    if (manifest) {
      const data = await entryBlob(blob, manifest);
      if (!data) return none;
      const r = parseAxml(new Uint8Array(await data.arrayBuffer()));
      return { appVersion: r.versionName, build: r.versionCode };
    }

    return none;
  } catch {
    return none;
  }
}

function isElf64Le(b) {
  return b.length >= 64 && b[0] === 0x7f && b[1] === 0x45 && b[2] === 0x4c && b[3] === 0x46
    && b[4] === 2 && b[5] === 1;
}

function isElf32Le(b) {
  return b.length >= 52 && b[0] === 0x7f && b[1] === 0x45 && b[2] === 0x4c && b[3] === 0x46
    && b[4] === 1 && b[5] === 1;
}

function u16(dv, p) {
  return dv.getUint16(p, true);
}

function u32(dv, p) {
  return dv.getUint32(p, true);
}

function u64(dv, p) {
  return dv.getUint32(p + 4, true) * 0x100000000 + dv.getUint32(p, true);
}

function cstrEnd(b, o) {
  let e = o;
  while (e < b.length && b[e] !== 0) e++;
  return e;
}

function indexOf(hay, needle) {
  const first = needle[0], n = needle.length;
  let i = hay.indexOf(first);
  while (i >= 0 && i + n <= hay.length) {
    let ok = true;
    for (let j = 1; j < n; j++) {
      if (hay[i + j] !== needle[j]) { ok = false; break; }
    }
    if (ok) return i;
    i = hay.indexOf(first, i + 1);
  }
  return -1;
}

function findAnchor(bytes, name) {
  const nb = new TextEncoder().encode(name);
  const pat = new Uint8Array(nb.length + 2);
  pat[0] = 0x0a;
  pat[1] = nb.length;
  pat.set(nb, 2);
  return indexOf(bytes, pat);
}

function readVarint(bytes, pos) {
  let shift = 0, result = 0, p = pos;
  while (true) {
    const by = bytes[p++];
    if (by === undefined) throw new Error("eof");
    result += (by & 0x7f) * Math.pow(2, shift);
    if ((by & 0x80) === 0) break;
    shift += 7;
    if (shift > 63) throw new Error("varint too long");
  }
  return [result, p];
}

function wireWalkLength(bytes, start) {
  let pos = start, lastGood = start;
  try {
    while (pos < bytes.length) {
      const [tag, p] = readVarint(bytes, pos);
      const fieldNum = Math.floor(tag / 8), wire = tag % 8;
      if (fieldNum < 1 || fieldNum > 12 || wire !== 2) break;
      const [len, q] = readVarint(bytes, p);
      if (q + len > bytes.length) break;
      pos = q + len;
      lastGood = pos;
    }
  } catch {
  }
  return lastGood - start;
}

function carveDescriptor(bytes, name) {
  const at = findAnchor(bytes, name);
  if (at < 0) return null;
  const len = wireWalkLength(bytes, at);
  if (len <= 0) return null;
  return bytes.subarray(at, at + len);
}

function elf64Sections(dv, len) {
  const shoff = u64(dv, 0x28), shentsize = u16(dv, 0x3a), shnum = u16(dv, 0x3c);
  const out = [];
  if (shentsize < 64 || shnum <= 0) return out;
  for (let i = 0; i < shnum; i++) {
    const h = shoff + i * shentsize;
    if (h < 0 || h + 64 > len) break;
    const flags = u64(dv, h + 0x08);
    if (flags % 4 < 2) continue;
    out.push({ addr: u64(dv, h + 0x10), off: u64(dv, h + 0x18), sz: u64(dv, h + 0x20) });
  }
  return out;
}

function elf64Segments(dv, len) {
  const phoff = u64(dv, 0x20), phentsize = u16(dv, 0x36), phnum = u16(dv, 0x38);
  const out = [];
  if (phentsize < 56 || phnum <= 0) return out;
  for (let i = 0; i < phnum; i++) {
    const p = phoff + i * phentsize;
    if (p < 0 || p + 56 > len) break;
    if (u32(dv, p + 0x00) !== 1) continue;
    out.push({ vaddr: u64(dv, p + 0x10), off: u64(dv, p + 0x08), filesz: u64(dv, p + 0x20) });
  }
  return out;
}

function elf32Sections(dv, len) {
  const shoff = u32(dv, 0x20), shentsize = u16(dv, 0x2e), shnum = u16(dv, 0x30);
  const out = [];
  if (shentsize < 40 || shnum <= 0) return out;
  for (let i = 0; i < shnum; i++) {
    const h = shoff + i * shentsize;
    if (h < 0 || h + 40 > len) break;
    const flags = u32(dv, h + 8);
    if ((flags & 2) === 0) continue;
    out.push({ addr: u32(dv, h + 12), off: u32(dv, h + 16), sz: u32(dv, h + 20) });
  }
  return out;
}

function elf32Segments(dv, len) {
  const phoff = u32(dv, 0x1c), phentsize = u16(dv, 0x2a), phnum = u16(dv, 0x2c);
  const out = [];
  if (phentsize < 32 || phnum <= 0) return out;
  for (let i = 0; i < phnum; i++) {
    const p = phoff + i * phentsize;
    if (p < 0 || p + 32 > len) break;
    if (u32(dv, p + 0) !== 1) continue;
    out.push({ vaddr: u32(dv, p + 8), off: u32(dv, p + 4), filesz: u32(dv, p + 16) });
  }
  return out;
}

function vaToOffset(sections, segments, va) {
  for (const s of sections) {
    if (s.sz > 0 && va >= s.addr && va < s.addr + s.sz) return s.off + (va - s.addr);
  }
  for (const s of segments) {
    if (s.filesz > 0 && va >= s.vaddr && va < s.vaddr + s.filesz) return s.off + (va - s.vaddr);
  }
  return -1;
}

function elf64Symbols(dv, bytes, len) {
  const shoff = u64(dv, 0x28), shentsize = u16(dv, 0x3a), shnum = u16(dv, 0x3c);
  const syms = [];
  if (shentsize < 64 || shnum <= 0) return syms;
  const dec = new TextDecoder();
  for (let i = 0; i < shnum; i++) {
    const h = shoff + i * shentsize;
    if (h < 0 || h + 64 > len) break;
    const type = u32(dv, h + 0x04);
    if (type !== 2 && type !== 11) continue;
    const tabOff = u64(dv, h + 0x18), tabSize = u64(dv, h + 0x20), link = u32(dv, h + 0x28);
    let entsize = u64(dv, h + 0x38);
    if (entsize < 24) entsize = 24;
    if (link >= shnum) continue;
    const lh = shoff + link * shentsize;
    if (lh < 0 || lh + 64 > len) continue;
    const strOff = u64(dv, lh + 0x18), strSize = u64(dv, lh + 0x20);
    const count = Math.floor(tabSize / entsize);
    for (let s = 0; s < count; s++) {
      const e = tabOff + s * entsize;
      if (e < 0 || e + 24 > len) break;
      const nameOff = u32(dv, e + 0x00);
      const value = u64(dv, e + 8);
      if (nameOff === 0 || nameOff >= strSize) continue;
      const at = strOff + nameOff;
      const name = dec.decode(bytes.subarray(at, cstrEnd(bytes, at)));
      if (name.length === 0) continue;
      syms.push({ name, value });
    }
  }
  return syms;
}

function elf32Symbols(dv, bytes, len) {
  const shoff = u32(dv, 0x20), shentsize = u16(dv, 0x2e), shnum = u16(dv, 0x30);
  const syms = [];
  if (shentsize < 40 || shnum <= 0) return syms;
  for (let i = 0; i < shnum; i++) {
    const h = shoff + i * shentsize;
    if (h < 0 || h + 40 > len) break;
    const type = u32(dv, h + 4);
    if (type !== 2 && type !== 11) continue;
    const tabOff = u32(dv, h + 16), tabSize = u32(dv, h + 20), link = u32(dv, h + 24);
    let entsize = u32(dv, h + 36);
    if (entsize < 16) entsize = 16;
    if (link >= shnum) continue;
    const lh = shoff + link * shentsize;
    if (lh < 0 || lh + 40 > len) continue;
    const strOff = u32(dv, lh + 16), strSize = u32(dv, lh + 20);
    const count = Math.floor(tabSize / entsize);
    for (let s = 0; s < count; s++) {
      const e = tabOff + s * entsize;
      if (e < 0 || e + 16 > len) break;
      const nameOff = u32(dv, e + 0);
      const value = u32(dv, e + 4);
      if (nameOff === 0 || nameOff >= strSize) continue;
      const name = readCstr(bytes, strOff + nameOff);
      if (name.length === 0) continue;
      syms.push({ name, value });
    }
  }
  return syms;
}

function movkInsert(w0, imm16, hw) {
  const factor = Math.pow(2, hw * 16);
  const below = w0 % factor;
  const above = Math.floor(w0 / (factor * 0x10000)) * (factor * 0x10000);
  return above + imm16 * factor + below;
}

function decodeBitMask(n, immr, imms, width) {
  if (width > 32) return null;
  const combined = (n << 6) | (imms ^ 0x3f);
  if (combined === 0) return null;
  const length = 31 - Math.clz32(combined >>> 0);
  const esize = 1 << length;
  if (esize < 2 || esize > 32) return null;
  const levels = esize - 1;
  const s = imms & levels, r = immr & levels;
  const welem = Math.pow(2, s + 1) - 1;
  const emod = Math.pow(2, esize);
  let val;
  if (r === 0) {
    val = welem;
  } else {
    const right = Math.floor(welem / Math.pow(2, r));
    const left = (welem * Math.pow(2, esize - r)) % emod;
    val = (right + left) % emod;
  }
  let out = 0;
  for (let i = 0; i < width; i += esize) out += val * Math.pow(2, i);
  return out % Math.pow(2, 32);
}

function decodeConstReturnArm64(dv, fo, len) {
  let w0 = null;
  for (let p = fo; p + 4 <= fo + len; p += 4) {
    const ins = u32(dv, p);
    if (ins === 0xd65f03c0) break;
    const rd = ins & 0x1f;
    const hw = (ins >>> 21) & 3;
    const imm16 = (ins >>> 5) & 0xffff;
    if ((ins & 0x7f800000) === 0x52800000) {
      if (rd === 0) w0 = imm16 * Math.pow(2, hw * 16);
    } else if ((ins & 0x7f800000) === 0x72800000) {
      if (rd === 0 && w0 !== null) w0 = movkInsert(w0, imm16, hw);
    } else if ((ins & 0x7f800000) === 0x32000000 && rd === 0 && ((ins >>> 5) & 0x1f) === 31) {
      const bm = decodeBitMask((ins >>> 22) & 1, (ins >>> 16) & 0x3f, (ins >>> 10) & 0x3f,
        ((ins >>> 31) & 1) ? 64 : 32);
      if (bm !== null) w0 = bm;
    }
  }
  if (w0 === null || w0 < 0 || w0 > 0x7fffffff) return null;
  return Math.floor(w0);
}

function decodeThumbConstReturn(dv, fo, len) {
  let r0 = null;
  for (let p = fo; p + 2 <= fo + len;) {
    const hw = u16(dv, p);
    if (hw === 0x4770) break;
    if ((hw & 0xf800) === 0x2000) {
      if (((hw >> 8) & 7) === 0) r0 = hw & 0xff;
      p += 2;
      continue;
    }
    if ((hw & 0xfbf0) === 0xf240 && p + 4 <= fo + len) {
      const hw2 = u16(dv, p + 2);
      const i = (hw >> 10) & 1;
      const imm4 = hw & 0xf;
      const imm3 = (hw2 >> 12) & 7;
      const rd = (hw2 >> 8) & 0xf;
      const imm8 = hw2 & 0xff;
      if (rd === 0) r0 = (imm4 << 12) | (i << 11) | (imm3 << 8) | imm8;
      p += 4;
      continue;
    }
    p += 2;
  }
  if (r0 === null || r0 < 0 || r0 > 0x7fffffff) return null;
  return r0;
}

function decodeArmConstReturn(dv, fo, len) {
  let r0 = null;
  for (let p = fo; p + 4 <= fo + len; p += 4) {
    const ins = u32(dv, p);
    if ((ins & 0x0fffffff) === 0x012fff1e) break;
    if ((ins & 0x0fef0000) === 0x03a00000) {
      const rd = (ins >>> 12) & 0xf;
      const rot = ((ins >>> 8) & 0xf) * 2;
      const imm8 = ins & 0xff;
      const val = rot === 0 ? imm8 : (((imm8 >>> rot) | (imm8 << (32 - rot))) >>> 0);
      if (rd === 0) r0 = val;
    } else if ((ins & 0x0ff00000) === 0x03000000) {
      const rd = (ins >>> 12) & 0xf;
      if (rd === 0) r0 = (((ins >>> 16) & 0xf) << 12) | (ins & 0xfff);
    }
  }
  if (r0 === null || r0 < 0 || r0 > 0x7fffffff) return null;
  return r0;
}

const SYM_NEEDLE = "GameController20currentClientVersion";

const NAME_DECODER = new TextDecoder();

function u32be(dv, p) {
  return dv.getUint32(p, false);
}

function readCstr(bytes, at) {
  return NAME_DECODER.decode(bytes.subarray(at, cstrEnd(bytes, at)));
}

function machoBaseOf(dv, bytes) {
  const magic = u32(dv, 0);
  if (magic === 0xcafebabe || magic === 0xbebafeca) {
    const nfat = u32be(dv, 4);
    let e = 8;
    for (let i = 0; i < nfat; i++) {
      if (e + 20 > bytes.length) return -1;
      if (u32be(dv, e) === 0x0100000c) return u32be(dv, e + 8);
      e += 20;
    }
    return -1;
  }
  return magic === 0xfeedfacf || magic === 0xcffaedfe ? 0 : -1;
}

function machoSymbols(dv, bytes, base) {
  const syms = [];
  if (base + 32 > bytes.length || u32(dv, base) !== 0xfeedfacf) return syms;
  const ncmds = u32(dv, base + 16);
  let lc = base + 32;
  for (let c = 0; c < ncmds; c++) {
    if (lc + 8 > bytes.length) break;
    const cmd = u32(dv, lc), cmdsize = u32(dv, lc + 4);
    if (cmdsize < 8 || lc + cmdsize > bytes.length) break;
    if (cmd === 0x02) {
      const symoff = u32(dv, lc + 8) + base;
      const nsyms = u32(dv, lc + 12);
      const stroff = u32(dv, lc + 16) + base;
      const strsize = u32(dv, lc + 20);
      for (let i = 0; i < nsyms; i++) {
        const e = symoff + i * 16;
        if (e + 16 > bytes.length) break;
        const strx = u32(dv, e);
        if (strx === 0 || strx >= strsize) continue;
        const name = readCstr(bytes, stroff + strx);
        if (name.length === 0) continue;
        syms.push({ name, value: u64(dv, e + 8) });
      }
    }
    lc += cmdsize;
  }
  return syms;
}

function machoSections(dv, bytes, base) {
  const out = [];
  if (base + 32 > bytes.length || u32(dv, base) !== 0xfeedfacf) return out;
  const ncmds = u32(dv, base + 16);
  let lc = base + 32;
  for (let c = 0; c < ncmds; c++) {
    if (lc + 8 > bytes.length) break;
    const cmd = u32(dv, lc), cmdsize = u32(dv, lc + 4);
    if (cmdsize < 8 || lc + cmdsize > bytes.length) break;
    if (cmd === 0x19) {
      const nsects = u32(dv, lc + 64);
      let sec = lc + 72;
      for (let s = 0; s < nsects; s++) {
        if (sec + 80 > bytes.length) break;
        out.push({ addr: u64(dv, sec + 32), sz: u64(dv, sec + 40), off: u32(dv, sec + 48) + base });
        sec += 80;
      }
    }
    lc += cmdsize;
  }
  return out;
}

function machoVaToOffset(sections, va) {
  for (const s of sections) {
    if (s.sz > 0 && va >= s.addr && va < s.addr + s.sz) return s.off + (va - s.addr);
  }
  return -1;
}

function elf64Image(bytes, dv) {
  let syms = null, secs = null, segs = null;
  return {
    dv, len: bytes.length, arm32: false,
    symbols() {
      if (syms === null) syms = elf64Symbols(dv, bytes, bytes.length);
      return syms;
    },
    vaToFileOffset(va) {
      if (secs === null) secs = elf64Sections(dv, bytes.length);
      if (segs === null) segs = elf64Segments(dv, bytes.length);
      return vaToOffset(secs, segs, va);
    }
  };
}

function elf32Image(bytes, dv) {
  let syms = null, secs = null, segs = null;
  return {
    dv, len: bytes.length, arm32: true,
    symbols() {
      if (syms === null) syms = elf32Symbols(dv, bytes, bytes.length);
      return syms;
    },
    vaToFileOffset(va) {
      if (secs === null) secs = elf32Sections(dv, bytes.length);
      if (segs === null) segs = elf32Segments(dv, bytes.length);
      return vaToOffset(secs, segs, va);
    }
  };
}

function machoImage(bytes, dv, base) {
  let syms = null, secs = null;
  return {
    dv, len: bytes.length, arm32: false,
    symbols() {
      if (syms === null) syms = machoSymbols(dv, bytes, base);
      return syms;
    },
    vaToFileOffset(va) {
      if (secs === null) secs = machoSections(dv, bytes, base);
      return machoVaToOffset(secs, va);
    }
  };
}

function loadImage(bytes) {
  if (bytes.length < 8) return null;
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (isElf64Le(bytes)) return elf64Image(bytes, dv);
  if (isElf32Le(bytes)) return elf32Image(bytes, dv);
  const base = machoBaseOf(dv, bytes);
  return base >= 0 ? machoImage(bytes, dv, base) : null;
}

function findFuncRange(syms, needle) {
  let hit = null;
  for (const s of syms) {
    if (s.value !== 0 && s.name.includes(needle)) { hit = s; break; }
  }
  if (!hit) return null;
  const start = hit.value;
  let end = Number.MAX_SAFE_INTEGER;
  for (const s of syms) {
    if (s.value > start && s.value < end) end = s.value;
  }
  if (end === Number.MAX_SAFE_INTEGER) end = start + 0x4000;
  return { start, end };
}

function clientVersionFromImage(img) {
  const range = findFuncRange(img.symbols(), SYM_NEEDLE);
  if (!range) return null;
  if (img.arm32) {
    const thumb = (range.start & 1) !== 0;
    const startVa = thumb ? range.start - 1 : range.start;
    const fo = img.vaToFileOffset(startVa);
    if (fo < 0) return null;
    const win = Math.min(64, img.len - fo);
    if (win < 2) return null;
    return thumb ? decodeThumbConstReturn(img.dv, fo, win) : decodeArmConstReturn(img.dv, fo, win);
  }
  const fo = img.vaToFileOffset(range.start);
  if (fo < 0) return null;
  const span = range.end > range.start ? range.end - range.start : 16;
  const declen = Math.min(span, 64);
  if (declen < 4 || fo + declen > img.len) return null;
  return decodeConstReturnArm64(img.dv, fo, declen);
}

function toBase64(u8) {
  let s = "";
  const chunk = 0x8000;
  for (let i = 0; i < u8.length; i += chunk) {
    s += String.fromCodePoint(...u8.subarray(i, i + chunk));
  }
  return btoa(s);
}

async function sha256Hex(u8) {
  const buf = await crypto.subtle.digest("SHA-256", u8);
  const b = new Uint8Array(buf);
  let s = "";
  for (const x of b) s += x.toString(16).padStart(2, "0");
  return s;
}

async function report(dotnetRef, msg) {
  if (!dotnetRef) return;
  try { await dotnetRef.invokeMethodAsync("AnalyzeStep", msg); } catch {}
}

function sizeText(bytes) {
  if (bytes >= 1048576) return (bytes / 1048576).toFixed(1) + " MB";
  return (bytes / 1024).toFixed(1) + " KB";
}

async function prepare(file, onStep) {
  const step = onStep ?? (async () => {});
  await step("extracting binary from archive");
  const so = await strip(file);
  const soBytes = new Uint8Array(await so.arrayBuffer());
  await step("carving proto descriptors");
  const ei = carveDescriptor(soBytes, "ei.proto");
  if (!ei) {
    await step("client carve failed, uploading full file");
    return { blob: file, name: file.name, fileSize: file.size, strippedSize: so.size };
  }
  const common = carveDescriptor(soBytes, "common.proto");
  const img = loadImage(soBytes);
  const cv = img ? clientVersionFromImage(img) : null;
  await step("reading app metadata");
  const meta = await archiveMeta(file);
  const manifest = {
    v: 1,
    fileSha: await sha256Hex(soBytes),
    clientVersion: cv,
    ei: toBase64(ei),
    common: common ? toBase64(common) : null,
    appVersion: meta.appVersion,
    build: meta.build
  };
  return { blob: new Blob([JSON.stringify(manifest)], { type: "application/json" }), name: file.name, fileSize: file.size, strippedSize: so.size };
}

function inputFiles(inputId) {
  const el = document.getElementById(inputId);
  return el?.files ? Array.from(el.files) : [];
}

async function postForm(endpoint, form) {
  let res;
  try {
    res = await fetch(endpoint, {
      method: "POST",
      body: form,
      credentials: "same-origin",
      signal: AbortSignal.timeout(600000)
    });
  } catch (e) {
    return { error: "upload failed or timed out: " + String(e) };
  }
  let json = null;
  try {
    json = await res.json();
  } catch {
    json = null;
  }
  if (!res.ok) return { error: json?.error ? json.error : `HTTP ${res.status}` };
  return json ?? { error: "empty response" };
}

export async function analyze(inputId, endpoint, dotnetRef) {
  try {
    const files = inputFiles(inputId);
    if (files.length === 0) return { ok: false, diagnostics: "no file selected" };
    const prep = await prepare(files[0], msg => report(dotnetRef, msg));
    const form = new FormData();
    form.append("file", prep.blob, prep.name);
    await report(dotnetRef, `uploading (${sizeText(prep.blob.size)})`);
    const r = await postForm(endpoint, form);
    if (r?.error) return { ok: false, diagnostics: r.error };
    return { ...r, fileSize: prep.fileSize, strippedSize: prep.strippedSize, uploadedSize: prep.blob.size };
  } catch (e) {
    return { ok: false, diagnostics: String(e) };
  }
}

export async function uploadBatch(inputId, endpoint, dotnetRef) {
  try {
    const files = inputFiles(inputId);
    if (files.length === 0) return { error: "no files selected" };
    const form = new FormData();
    let total = 0;
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      await report(dotnetRef, `preparing ${i + 1}/${files.length}: ${file.name}`);
      const prep = await prepare(file);
      total += prep.blob.size;
      form.append("files", prep.blob, prep.name);
    }
    await report(dotnetRef, `uploading batch (${files.length} files, ${(total / 1048576).toFixed(1)} MB)`);
    return await postForm(endpoint, form);
  } catch (e) {
    return { error: String(e) };
  }
}
