#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

EXT="pbtk/utils/external"

# CRLF gotcha: a "#!/bin/sh\r" shebang makes the kernel report "required file not found". Strip CR.
find . -name "*.sh" -exec sed -i 's/\r$//' {} \;

# dex2jar: GitHub release zip (DEX -> JAR).
DEX2JAR_VER="2.1"
DEX2JAR_URL="https://github.com/pxb1988/dex2jar/releases/download/v${DEX2JAR_VER}/dex2jar-${DEX2JAR_VER}.zip"
if [ ! -f "$EXT/dex2jar/d2j-dex2jar.sh" ]; then
  echo "dex2jar missing; fetching ${DEX2JAR_VER}..."
  mkdir -p "$EXT"
  tmp="$(mktemp -d)"
  if curl -fsSL "$DEX2JAR_URL" -o "$tmp/dex2jar.zip"; then
    unzip -q "$tmp/dex2jar.zip" -d "$tmp"
    # The zip nests under dex-tools-<ver>/; flatten into external/dex2jar.
    src="$(find "$tmp" -maxdepth 1 -type d -name 'dex-tools*' | head -n1)"
    if [ -n "$src" ]; then
      rm -rf "$EXT/dex2jar"
      mv "$src" "$EXT/dex2jar"
    else
      echo "WARN: dex2jar zip layout unexpected; copy pbtk/utils/external/dex2jar/ from the EggIncProtoExtractor repo instead." >&2
    fi
  else
    echo "WARN: dex2jar fetch failed. Copy pbtk/utils/external/dex2jar/ from the EggIncProtoExtractor repo (or the frame)." >&2
  fi
  rm -rf "$tmp"
fi

# protoc: GitHub release, linux x86_64.
PROTOC_VER="25.1"
PROTOC_URL="https://github.com/protocolbuffers/protobuf/releases/download/v${PROTOC_VER}/protoc-${PROTOC_VER}-linux-x86_64.zip"
if [ ! -f "$EXT/protoc/protoc" ] && [ ! -f "$EXT/protoc/protoc64" ]; then
  echo "protoc missing; fetching ${PROTOC_VER}..."
  mkdir -p "$EXT/protoc"
  tmp="$(mktemp -d)"
  if curl -fsSL "$PROTOC_URL" -o "$tmp/protoc.zip"; then
    unzip -q "$tmp/protoc.zip" -d "$tmp/protoc"
    cp "$tmp/protoc/bin/protoc" "$EXT/protoc/protoc"
    cp "$tmp/protoc/bin/protoc" "$EXT/protoc/protoc64"
  else
    echo "WARN: protoc fetch failed. Copy pbtk/utils/external/protoc/ from the EggIncProtoExtractor repo (or the frame)." >&2
  fi
  rm -rf "$tmp"
fi

# jad: no stable release URL; operator supplies it (EggIncProtoExtractor repo or frame).
if [ ! -f "$EXT/jad/jad" ]; then
  echo "ERROR: jad decompiler missing at $EXT/jad/jad and cannot be fetched (no stable release URL)." >&2
  echo "       Copy pbtk/utils/external/jad/ from the EggIncProtoExtractor repo or the frame, then re-run." >&2
  exit 1
fi

# chmod wrappers + binaries. Tolerant: checks above already gate the required ones.
chmod +x \
  "$EXT/dex2jar/d2j-dex2jar.sh" "$EXT/dex2jar/d2j_invoke.sh" \
  "$EXT/jad/jad" \
  "$EXT/protoc/protoc" "$EXT/protoc/protoc64" 2>/dev/null || true

python3 -m venv .venv
./.venv/bin/pip install -q protobuf requests

echo "proto-extract toolchain ready. Requires java (default-jre-headless) on PATH for dex2jar."
