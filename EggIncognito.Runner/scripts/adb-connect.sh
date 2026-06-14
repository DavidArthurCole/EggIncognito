#!/usr/bin/env bash
# Connect to a device over network ADB. Pass the device IP (port defaults to 5555).
set -euo pipefail
TARGET="${1:?usage: adb-connect.sh <device-ip> [port]}:${2:-5555}"
adb connect "$TARGET"
adb devices
