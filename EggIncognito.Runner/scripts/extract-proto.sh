#!/usr/bin/env bash
# Extract the cleaned ei.proto from a pulled arm split APK.
#
# There is no on-device ei.proto file. The protos are descriptors embedded in the
# arm split; pbtk decompiles them and protocleanup merges common.proto into ei.proto.
# This wraps the proto-extract toolchain, the same chain the extractor shells.
#
# Usage: extract-proto.sh <arm.apk> [EXTRACTOR_REPO]
# EXTRACTOR_REPO defaults to ../proto-extract relative to this repo.
set -euo pipefail
APK="${1:?usage: extract-proto.sh <arm.apk> [EXTRACTOR_REPO]}"
REPO="${2:-$(cd "$(dirname "$0")/.." && pwd)/proto-extract}"
PY="$REPO/.venv/bin/python3"

if [ ! -x "$PY" ]; then
  echo "no venv python at $PY; build it per proto-extract README" >&2
  exit 1
fi

OUTDIR=$(mktemp -d)
trap 'rm -rf "$OUTDIR"' EXIT

( cd "$REPO" && "$PY" -W ignore pbtk/extractors/jar_extract.py "$APK" "$OUTDIR" )
( cd "$REPO" && "$PY" -W ignore protocleanup.py "$OUTDIR" )

cp "$OUTDIR/ei.proto" ./ei.proto
echo "extracted ei.proto (sha256: $(sha256sum ei.proto | cut -d' ' -f1))"
