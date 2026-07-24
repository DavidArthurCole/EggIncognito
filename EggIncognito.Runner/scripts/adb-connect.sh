#!/usr/bin/env bash
set -euo pipefail
TARGET="${1:?usage: adb-connect.sh <device-ip> [port]}:${2:-5555}"
adb connect "$TARGET"
adb devices
