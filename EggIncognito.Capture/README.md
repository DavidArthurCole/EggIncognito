# EggIncognito.Capture

The capture engine. A TLS-intercepting proxy that records real auxbrain.com traffic and turns it into mock-server endpoints. Part of [EggIncognito](../README.md); the web app hosts the dashboard and API in front of it.

## Selective decrypt

Only auxbrain hosts are decrypted. Everything else, including Apple, Google, and certificate-pinned apps, tunnels through untouched, so the rest of the phone keeps working during a capture.

## Manual capture

Start the proxy from the API workbench (`/protos#api`, or launch with `--capture`), point the phone's Wi-Fi proxy at the computer, install and trust the printed root CA, and play. Each captured flow becomes a redacted endpoint on disk, the route map self-repairs, and the session is written to a HAR.

## Device farm

Separately, the engine drives a fixed farm of rooted Android and jailbroken iOS devices with zero on-device taps: per-device persistent listeners, proxy config and CA trust automated over `adb`/`ssh`. The farm path harvests wire metadata only; the authoritative client version comes from the device binary itself. Off by default.
