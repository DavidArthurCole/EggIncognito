# EggIncognito.Runner deploy

Host-side device runner. Polls a device over adb, extracts the cleaned proto, posts a `NewVersionEvent` to the sync server, serves an authed `POST /resync` for the EGI admin re-sync button.

## Build

```bash
dotnet publish EggIncognito.Runner -c Release -o /opt/eggincognito-runner
```

Proto extraction and clientVersion scanning run in-process in C# (`AndroidProtoExtractor`, `Elf64`, `Arm64ClientVersionScanner`). No external toolchain, no python, no java.

## systemd instances

Templated unit, instance name = platform.

```bash
sudo cp deploy/eggincognito-runner@.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now eggincognito-runner@android
```

| Instance | State |
|---|---|
| `eggincognito-runner@android` | proven, enable it |
| `eggincognito-runner@ios` | deferred until the ios extractor exists; do not enable |

## Secrets

`/etc/eggincognito-runner/secret.env`, mode 0600, owned by `eggfarm`.

| Key | Purpose |
|---|---|
| `SYNC_EVENT_URL` | sync server new-version ingest endpoint |
| `SYNC_EVENT_SECRET` | bearer token for the event POST |
| `RUNNER_TRIGGER_SECRET` | bearer the `/resync` + `/extract` listener requires; EGI sends it as `RUNNER_AGENT_SECRET` |
| `ADB_TARGET` | device serial (USB) or host:port (network adb) for the poll |
| `PREV_CLIENT_VERSION` | bootstrap anchor for clientVersion extraction (the last known value, e.g. 71). Self-advances into `apks/clientversion-<platform>.txt` after each extract; only needed for the first run |

The unit binds `RUNNER_TRIGGER_URLS=http://0.0.0.0:5055` so a container on the host can reach it. The serve listener turns on whenever `RUNNER_TRIGGER_SECRET` is set.

## clientVersion extraction

The runner extracts the API `clientVersion` (e.g. 72) in-process using pure C# (`Elf64` + `Arm64ClientVersionScanner`). It disassembles `libegginc.so` and picks the compiled-in constant anchored to `PREV_CLIENT_VERSION` (increments by 0-1 per build). Seed `PREV_CLIENT_VERSION` once; the runner advances it automatically. Null when no anchor is set.
