# EggIncognito.Runner deploy

Host-side device runner, containerized, deployed as a stack-56 sidecar via EggIdentity. Watches every configured device (android over adb, ios via a pre-staged binary), extracts the cleaned proto, posts a `NewVersionEvent`, and serves an authed resync API.

## Image

`ghcr.io/davidarthurcole/eggincognito-runner:latest`, built by the release workflow's `docker-runner` job.

## Instance

One `eggincognito-runner` container watches every configured device, both platforms, from `DEVICES_DIR`. `network_mode: host` (android needs the host adb server; ios reads a staged binary). No more per-platform split.

## Env vars

| Key | Purpose |
|---|---|
| `DEVICES_DIR` | directory of `*.egidevice.N` device files; drives the device list. When unset, falls back to legacy single-`PLATFORM` mode. |
| `PACKAGE` | default package when a device file omits `Package` |
| `APK_STASH_DIR` | pulled APK / staged binary / per-device state directory |
| `IOS_BINARY_PATH` | staged Mach-O path used by ios devices this phase (single ios feed) |
| `POLL_INTERVAL` | seconds between full sweeps of all devices |
| `SYNC_EVENT_URL` / `SYNC_EVENT_SECRET` | new-version ingest endpoint + bearer |
| `RUNNER_TRIGGER_SECRET` | bearer for `POST /resync`, `POST /resync/{id}`, and the probe routes; EGI sends it as `RUNNER_AGENT_SECRET` |
| `RUNNER_TRIGGER_URLS` | listener bind, e.g. `http://0.0.0.0:5055` |
| `ConnectionStrings__Postgres` | optional. When set, the runner connects to the same Postgres DB as the main app and owns device probing: a periodic sweep of every enabled device plus the probe API below. When unset, the runner stays Phase-0 version-only (no DB, no probe sweep, no probe routes). |

Legacy `PLATFORM`, `ADB_TARGET`, `STATE_FILE` still work when `DEVICES_DIR` is unset (single-device fallback).

## Trigger routes

- `POST /resync` resyncs all devices, returns per-device results.
- `POST /resync/{id}` resyncs one device by id.
- `POST /extract` ApkPure fallback extract (android).
- `POST /devices/{id}/probe` probes one device (bearer `RUNNER_TRIGGER_SECRET`); only registered when `ConnectionStrings__Postgres` is set.
- `POST /devices/probe-all` probes every enabled device (bearer `RUNNER_TRIGGER_SECRET`); only registered when `ConnectionStrings__Postgres` is set.

## clientVersion extraction

In-process C# (`LibegincClientVersion`) resolves `GameController::currentClientVersion()` in the `libegginc.so` and decodes its constant return. Deterministic, no anchor. Handles arm64 (movz/movk, orr-bitmask MOV alias) and armeabi-v7a ELF32 (ARM/Thumb). Verified across the full 1.0 through 1.37 APK corpus plus iOS.

## Redeploy

No app-callback needed. Runner is a stack-56 service like any other; `eggincognito.yaml`'s `docker-pull`+`portainer-update-stack` pipeline picks up a new `eggincognito-runner:latest` push automatically.
