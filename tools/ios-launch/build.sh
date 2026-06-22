#!/usr/bin/env bash
# Build + ldid-sign + push the sbslaunch headless launcher to the capture iPhone.
# Run on the capture host (frame). Needs: clang with an iOS arm64 target (Xcode CLT, theos, or a darwin
# cross-toolchain), ldid, and scp access to the phone. See sbslaunch.m for WHY this binary exists.
#
# Env overrides:
#   PHONE_SSH_HOST (default 192.168.1.175)  PHONE_SSH_PORT (default 2222)
#   PHONE_SSH_KEY  (default ~/.ssh/id_phone_ed25519)
#   SDK            (path to iPhoneOS sdk; auto-detected via xcrun if unset)
#   DEST           (on-phone install path, default /usr/bin/sbslaunch)
#   NO_PUSH=1      build + sign only, skip scp
set -euo pipefail
cd "$(dirname "$0")"

PHONE_SSH_HOST=${PHONE_SSH_HOST:-192.168.1.175}
PHONE_SSH_PORT=${PHONE_SSH_PORT:-2222}
PHONE_SSH_KEY=${PHONE_SSH_KEY:-$HOME/.ssh/id_phone_ed25519}
DEST=${DEST:-/usr/bin/sbslaunch}
OUT=sbslaunch

# Locate an iOS SDK + clang. macOS: xcrun. Linux: theos (the capture-host case - clang is the LLVM clang,
# not Apple's driver, so it needs an explicit -target).
THEOS=${THEOS:-/home/david/theos}
if command -v xcrun >/dev/null 2>&1; then
  SDK=${SDK:-$(xcrun --sdk iphoneos --show-sdk-path)}
  CC=${CC:-$(xcrun --sdk iphoneos -f clang)}
  LDID=${LDID:-ldid}
  TARGET=()
elif [ -d "$THEOS/toolchain/linux/iphone/bin" ]; then
  TC="$THEOS/toolchain/linux/iphone/bin"
  CC=${CC:-$TC/clang}
  LDID=${LDID:-$TC/ldid}
  SDK=${SDK:-$(ls -d "$THEOS"/sdks/iPhoneOS*.sdk 2>/dev/null | sort | tail -1)}
  TARGET=(-target arm64-apple-ios14.0)
else
  echo "build.sh: no xcrun and no theos toolchain at $THEOS. Set THEOS, or CC/LDID/SDK manually." >&2
  exit 1
fi
[ -n "${SDK:-}" ] && [ -d "$SDK" ] || { echo "build.sh: SDK not found ($SDK)" >&2; exit 1; }

echo "build.sh: compiling $OUT (arm64, cc=$CC, sdk=$SDK)"
"$CC" "${TARGET[@]}" -arch arm64 -isysroot "$SDK" -framework Foundation -framework CoreFoundation \
  -mios-version-min=14.0 -O2 -o "$OUT" sbslaunch.m

echo "build.sh: ldid-signing with sbslaunch.entitlements"
"$LDID" -Ssbslaunch.entitlements "$OUT"

if [ "${NO_PUSH:-0}" = "1" ]; then
  echo "build.sh: NO_PUSH set, built ./$OUT only"; exit 0
fi

SSH=(ssh -p "$PHONE_SSH_PORT" -i "$PHONE_SSH_KEY" -o StrictHostKeyChecking=no -o BatchMode=yes "root@$PHONE_SSH_HOST")
SCP=(scp -P "$PHONE_SSH_PORT" -i "$PHONE_SSH_KEY" -o StrictHostKeyChecking=no -o BatchMode=yes)

echo "build.sh: pushing to $DEST on $PHONE_SSH_HOST"
"${SCP[@]}" "$OUT" "root@$PHONE_SSH_HOST:$DEST"
"${SSH[@]}" "chmod 755 $DEST"

echo "build.sh: smoke test (launch com.auxbrain.egginc)"
"${SSH[@]}" "$DEST com.auxbrain.egginc; echo exit=\$?; sleep 3; ps ax | grep -i egg | grep -v grep || echo 'not running'"
echo "build.sh: done"
