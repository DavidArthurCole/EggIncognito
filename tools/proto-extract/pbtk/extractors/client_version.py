#!/usr/bin/env python3
# Extract the Egg Inc clientVersion from the arm split's libegginc.so.
#
# The value (BasicRequestInfo.client_version) is a hardcoded uint32 the client reports in every request.
# It is not a proto default, so it is not in the .proto text; it is a compiled-in constant. We find it by
# disassembling .text and locating small ints written to a struct field offset from several request-builder
# functions, then disambiguate with the previous known clientVersion (it increments by 0-1 per build).
#
# Usage: client_version.py <arm.apk|libegginc.so> <prevClientVersion>
# Prints JSON: {"clientVersion": <int>} or {"clientVersion": null, "candidates": [...]} when undetermined.
import json
import struct
import sys
import zipfile

try:
    from capstone import Cs, CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN
except ImportError:
    print(json.dumps({"clientVersion": None, "error": "capstone not installed"}))
    sys.exit(0)

LIB_PATH = "lib/arm64-v8a/libegginc.so"


def load_so(path):
    if path.endswith(".so"):
        return open(path, "rb").read()
    with zipfile.ZipFile(path) as z:
        return z.read(LIB_PATH)


def text_section(data):
    sh_off = struct.unpack_from("<Q", data, 0x28)[0]
    sh_entsize = struct.unpack_from("<H", data, 0x3A)[0]
    sh_num = struct.unpack_from("<H", data, 0x3C)[0]
    sh_strndx = struct.unpack_from("<H", data, 0x3E)[0]

    def sh(i):
        return data[sh_off + i * sh_entsize: sh_off + (i + 1) * sh_entsize]

    str_off = struct.unpack_from("<Q", sh(sh_strndx), 0x18)[0]

    def name(n):
        end = data.index(b"\0", str_off + n)
        return data[str_off + n: end].decode()

    for i in range(sh_num):
        n, _, _, addr, off, size, *_ = struct.unpack_from("<IIQQQQIIQQ", sh(i), 0)
        if name(n) == ".text":
            return addr, off, size
    raise ValueError("no .text section")


def candidates(insns):
    # value -> max distinct call-site count among offsets it is written to.
    # A candidate is a small int written to the SAME struct offset from >= 3 distinct sites.
    pair = {}
    for i, ins in enumerate(insns):
        if ins.mnemonic != "str" or "#0x" not in ins.op_str or "]" not in ins.op_str:
            continue
        reg = ins.op_str.split(",")[0].strip()
        if not reg.startswith("w"):
            continue
        try:
            off = ins.op_str.split("#")[1].rstrip("]")
        except IndexError:
            continue
        val = _prev_imm(insns, i, reg)
        if val is None or not (2 <= val <= 255):
            continue
        pair.setdefault((off, val), set()).add(ins.address)

    out = {}
    for (off, val), sites in pair.items():
        if len(sites) >= 3:
            out[val] = max(out.get(val, 0), len(sites))
    return out


def _prev_imm(insns, i, reg):
    for j in range(i - 1, max(i - 5, 0) - 1, -1):
        nx = insns[j]
        if nx.mnemonic in ("mov", "movz") and nx.op_str.startswith(reg + ",") and "#" in nx.op_str:
            v = nx.op_str.split("#")[-1]
            try:
                return int(v, 16) if v.startswith("0x") else int(v)
            except ValueError:
                return None
    return None


def pick(cands, prev):
    # clientVersion increments by 0-1 (rarely 2) per build, so it sits in {prev, prev+1, prev+2}.
    # Among in-range candidates, nearest to prev, breaking ties by site-count.
    in_range = [v for v in cands if prev <= v <= prev + 2]
    if not in_range:
        return None
    return sorted(in_range, key=lambda v: (abs(v - prev), -cands[v]))[0]


def main():
    if len(sys.argv) < 3:
        print(json.dumps({"clientVersion": None, "error": "usage: client_version.py <apk|so> <prev>"}))
        return
    try:
        prev = int(sys.argv[2])
    except ValueError:
        print(json.dumps({"clientVersion": None, "error": "prev must be an int"}))
        return
    try:
        data = load_so(sys.argv[1])
        addr, off, size = text_section(data)
        md = Cs(CS_ARCH_ARM64, CS_MODE_LITTLE_ENDIAN)
        md.skipdata = True
        insns = list(md.disasm(data[off: off + size], addr))
        cands = candidates(insns)
        chosen = pick(cands, prev)
        print(json.dumps({
            "clientVersion": chosen,
            "candidates": sorted(cands.keys()),
        }))
    except Exception as ex:
        print(json.dumps({"clientVersion": None, "error": str(ex)}))


if __name__ == "__main__":
    main()
