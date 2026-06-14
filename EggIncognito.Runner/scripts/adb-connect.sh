#!/usr/bin/env bash
# Connect to the Bliss VM over network ADB. Pass the VM IP, default 192.168.122.2.
set -euo pipefail
TARGET="${1:-192.168.122.2}:5555"
adb connect "$TARGET"
adb devices
