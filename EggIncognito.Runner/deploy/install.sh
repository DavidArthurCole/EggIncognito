#!/usr/bin/env bash
# Turnkey install for the EggIncognito device runner on a Linux host.
# Idempotent: re-run any time to update the binary or config.
#
# Prereqs:
#   - The published runner binary at ./EggIncognito.Runner (self-contained linux-x64), next to this script,
#     OR pass its path as $RUNNER_BIN. Build it with:
#       dotnet publish EggIncognito.Runner/EggIncognito.Runner.csproj -c Release -r linux-x64 \
#         --self-contained true -p:PublishSingleFile=true -o <outdir>
#   - adb on PATH with the device authorized (poll path).
#   - Run as the user that owns the adb key + USB device (NOT root; sudo is used per-step).
#
# Env overrides:
#   RUN_USER          service user (default: the invoking $USER)
#   PLATFORM          android|ios (default: android)
#   ADB_TARGET        device serial or host:port (REQUIRED for the poll path; e.g. RF8X20GLYDY)
#   SYNC_EVENT_URL    ingest URL (default: auto-detected from the eggincognito container's published port)
#   SYNC_EVENT_SECRET the EGI_SYNC_EVENT_SECRET value (REQUIRED; matches the eggincognito stack)
#   TRIGGER_SECRET    bearer for the extract/resync listener (default: generated; printed at the end)
#   TRIGGER_PORT      listener port (default: 5055)
#   RUNNER_BIN        path to the published binary (default: ./EggIncognito.Runner)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"

RUN_USER="${RUN_USER:-$USER}"
PLATFORM="${PLATFORM:-android}"
TRIGGER_PORT="${TRIGGER_PORT:-5055}"
RUNNER_BIN="${RUNNER_BIN:-$here/EggIncognito.Runner}"
DEST=/opt/eggincognito-runner
SECRET_DIR=/etc/eggincognito-runner
UNIT=/etc/systemd/system/eggincognito-runner@.service

fail() { echo "ERROR: $*" >&2; exit 1; }

[ -f "$RUNNER_BIN" ] || fail "runner binary not found at $RUNNER_BIN (build it or set RUNNER_BIN)"
[ -n "${ADB_TARGET:-}" ] || echo "WARN: ADB_TARGET unset; the device poll will fail until it is set in $SECRET_DIR/secret.env"
[ -n "${SYNC_EVENT_SECRET:-}" ] || fail "SYNC_EVENT_SECRET required (the EGI_SYNC_EVENT_SECRET from the eggincognito stack)"

# Auto-detect the ingest URL from the running eggincognito container's published port, unless overridden.
if [ -z "${SYNC_EVENT_URL:-}" ]; then
  port="$(docker ps --filter name=eggincognito --format '{{.Ports}}' 2>/dev/null \
    | grep -oE '0\.0\.0\.0:[0-9]+->8080' | head -1 | grep -oE ':[0-9]+' | tr -d ':' || true)"
  [ -n "$port" ] || fail "could not auto-detect eggincognito's host port; set SYNC_EVENT_URL=http://localhost:<port>/events/new-version"
  SYNC_EVENT_URL="http://localhost:${port}/events/new-version"
  echo "detected ingest: $SYNC_EVENT_URL"
fi

TRIGGER_SECRET="${TRIGGER_SECRET:-$(openssl rand -hex 32)}"

echo "installing runner -> $DEST (user $RUN_USER, platform $PLATFORM, listener :$TRIGGER_PORT)"

sudo mkdir -p "$DEST" "$SECRET_DIR"
sudo install -m 0755 "$RUNNER_BIN" "$DEST/EggIncognito.Runner"

# secret.env, mode 0600, only written if absent so a re-run does not clobber a hand-edited secret.
if [ ! -f "$SECRET_DIR/secret.env" ]; then
  sudo tee "$SECRET_DIR/secret.env" >/dev/null <<EOF
RUNNER_TRIGGER_SECRET=$TRIGGER_SECRET
SYNC_EVENT_URL=$SYNC_EVENT_URL
SYNC_EVENT_SECRET=$SYNC_EVENT_SECRET
ADB_TARGET=${ADB_TARGET:-}
EOF
  sudo chmod 600 "$SECRET_DIR/secret.env"
  echo "wrote $SECRET_DIR/secret.env"
else
  echo "$SECRET_DIR/secret.env exists; left as-is (delete it to regenerate)"
fi

sudo tee "$UNIT" >/dev/null <<EOF
[Unit]
Description=EggIncognito device runner (%i)
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$RUN_USER
Group=$RUN_USER
WorkingDirectory=$DEST
ExecStart=$DEST/EggIncognito.Runner
Environment=PATH=/usr/local/bin:/usr/bin:/bin:/opt/platform-tools
Environment=PLATFORM=%i
Environment=PACKAGE=com.auxbrain.egginc
Environment=STATE_FILE=$DEST/state-%i.json
Environment=APK_STASH_DIR=$DEST/apks
Environment=POLL_INTERVAL=300
Environment=RUNNER_TRIGGER_URLS=http://0.0.0.0:$TRIGGER_PORT
EnvironmentFile=$SECRET_DIR/secret.env
Restart=on-failure
RestartSec=15
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=false
ReadWritePaths=$DEST /tmp
PrivateTmp=false

[Install]
WantedBy=multi-user.target
EOF

sudo chown -R "$RUN_USER":"$RUN_USER" "$DEST"
sudo systemctl daemon-reload
sudo systemctl enable --now "eggincognito-runner@$PLATFORM"
sleep 2
sudo systemctl restart "eggincognito-runner@$PLATFORM"

echo
echo "done. listener: http://0.0.0.0:$TRIGGER_PORT"
echo "set this on the eggincognito stack so the Extract button reaches the runner:"
echo "  RUNNER_AGENT_URL=http://<this-host>:$TRIGGER_PORT"
echo "  RUNNER_AGENT_SECRET=$(sudo grep '^RUNNER_TRIGGER_SECRET=' "$SECRET_DIR/secret.env" | cut -d= -f2-)"
echo
echo "logs: journalctl -u eggincognito-runner@$PLATFORM -f"
