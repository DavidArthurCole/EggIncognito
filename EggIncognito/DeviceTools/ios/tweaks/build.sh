#!/usr/bin/env bash
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
tweak=${1:-}
if [ -z "$tweak" ] || [ ! -f "$here/$tweak/Makefile" ]; then
  echo "usage: build.sh <eggupdate|uinav> [MAKE_VAR=value ...]" >&2
  exit 2
fi
shift

image=egi-theos
if ! docker image inspect "$image" >/dev/null 2>&1; then
  docker build -t "$image" "$here"
fi

docker run --rm -u "$(id -u):$(id -g)" -v "$here/$tweak:/src" "$image" clean package "$@"

deb=$(ls -t "$here/$tweak"/packages/*.deb | head -1)
echo "built $deb"

for arg in "$@"; do
  if [ "$arg" = "EGGUPDATE_ARMED=1" ]; then
    hits=$(dpkg-deb --fsys-tarfile "$deb" | tar -xO --wildcards "*/eggupdate.dylib" | strings | grep -c "EGGUPDATE_ARMED=1: firing SSPurchase update" || true)
    if [ "$hits" -gt 0 ]; then
      echo "armed: yes"
    else
      echo "armed: NO, refusing to call this build shippable" >&2
      exit 1
    fi
  fi
done
