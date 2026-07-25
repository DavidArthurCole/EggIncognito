# EggIncognito.Runner deploy

Host-side device runner, containerized, deployed as a stack-56 sidecar via EggIdentity. Polls a device over adb (android) or reads a pre-staged binary (ios), extracts the cleaned proto, posts a `NewVersionEvent`, serves an authed `POST /resync`.

## Image

`ghcr.io/davidarthurcole/eggincognito-runner:latest`, built by the release workflow's `docker-runner` job.

## Instances

One container per platform, same image, `PLATFORM` env differs. Both need `network_mode: host` (android for the host adb server; ios for consistency).

| Instance | State |
|---|---|
| `PLATFORM=android` | proven |
| `PLATFORM=ios` | proven, reads `IOS_BINARY_PATH` (staged by the main `eggincognito` container's `IosBinaryPuller`) |

## Env vars

| Key | Purpose |
|---|---|
| `PLATFORM` | `android` or `ios` |
| `PACKAGE` | `com.auxbrain.egginc` |
| `ADB_TARGET` | device serial (USB) or host:port, android only |
| `IOS_BINARY_PATH` | path to the staged Mach-O binary, ios only |
| `STATE_FILE` | last-seen build state path |
| `APK_STASH_DIR` | pulled APK / staged binary directory (shared mount with the main container for ios) |
| `POLL_INTERVAL` | seconds between poll ticks |
| `SYNC_EVENT_URL` | sync server new-version ingest endpoint |
| `SYNC_EVENT_SECRET` | bearer token for the event POST |
| `RUNNER_TRIGGER_SECRET` | bearer the `/resync` listener requires; EGI sends it as `RUNNER_AGENT_SECRET` |
| `RUNNER_TRIGGER_URLS` | e.g. `http://0.0.0.0:5055` |
| `PREV_CLIENT_VERSION` | bootstrap anchor for clientVersion extraction, android only |

## clientVersion extraction

In-process C# (`Elf64` + `Arm64ClientVersionScanner`) against `libegginc.so`, anchored to `PREV_CLIENT_VERSION`. Self-advances after each extract.

## Redeploy

No app-callback needed. Runner is a stack-56 service like any other; `eggincognito.yaml`'s `docker-pull`+`portainer-update-stack` pipeline picks up a new `eggincognito-runner:latest` push automatically.
