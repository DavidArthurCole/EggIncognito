#!/usr/bin/env bash
# KernelSU module service script body to keep network adbd on across reboots.
# Install as a post-fs-data or service.sh entry in a KernelSU module.
set -euo pipefail
setprop service.adb.tcp.port 5555
stop adbd
start adbd
echo "network adbd enabled on 5555"
