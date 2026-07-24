#!/usr/bin/env bash
set -euo pipefail
PKG="${1:-com.auxbrain.egginc}"
OUT="${2:-arm.apk}"
ARM=$(adb shell pm path "$PKG" \
  | sed -n 's/^package://p' \
  | grep arm \
  | head -1 \
  | tr -d '\r')
if [ -z "$ARM" ]; then
  echo "no arm split found for $PKG" >&2
  exit 1
fi
adb pull "$ARM" "$OUT"
echo "pulled $OUT"
