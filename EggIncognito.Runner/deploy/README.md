# EggIncognito.Runner deploy

Host-side device runner. Polls a device over adb, extracts the cleaned proto, posts a `NewVersionEvent` to the sync server, and serves an authed `POST /resync` for the EGI admin re-sync button.

## Build

```bash
dotnet publish EggIncognito.Runner -c Release -o /opt/eggincognito-runner
```

Copy the vendored toolchain beside the binary as `proto-extract/`, then run its setup once:

```bash
cp -r tools/proto-extract /opt/eggincognito-runner/proto-extract
cd /opt/eggincognito-runner/proto-extract && bash setup.sh
```

`setup.sh` strips CRLF, chmods the binaries, builds the venv, installs deps. Needs java on PATH (dex2jar). The runner shells `proto-extract/.venv/bin/python3` for the decompile half, then runs the in-process C# `ProtoCleanup`.

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
| `RUNNER_TRIGGER_SECRET` | bearer the `/resync` listener requires; EGI sends it as `RUNNER_AGENT_SECRET` |

The unit sets `RUNNER_TRIGGER_URLS=http://127.0.0.1:5055`. The serve listener turns on whenever `RUNNER_TRIGGER_SECRET` is set.
