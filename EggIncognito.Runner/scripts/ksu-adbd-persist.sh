#!/usr/bin/env bash
set -euo pipefail
setprop service.adb.tcp.port 5555
stop adbd
start adbd
echo "network adbd enabled on 5555"
