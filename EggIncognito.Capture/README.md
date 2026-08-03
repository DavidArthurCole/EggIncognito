# EggIncognito.Capture

The capture engine (net10.0). A TLS-intercepting proxy that records real auxbrain.com traffic and turns it into mock-server endpoints. References [EggIncognito.Core](../EggIncognito.Core/README.md).

Part of [EggIncognito](../README.md). The web project hosts the Capture dashboard (`/capture`) and the `/api/capture/*` controller; this library is the engine behind them.

## How it works

EggIncognito embeds a TLS-intercepting proxy. The phone routes its Wi-Fi traffic through the proxy. The proxy decodes the Egg, Inc. requests and responses and writes them out as endpoint files the mock server can replay.

Decryption is selective. The proxy only decrypts TLS for auxbrain hosts:

- `*.auxbrain.com`
- App Engine hosts matching `*-dot-auxbrainhome.appspot.com`

Everything else tunnels through untouched and is never decrypted. This includes Apple, Google, the App Store, and certificate-pinned apps. The rest of the phone keeps working while you capture. The host filter is `AuxbrainHosts` in Core, the single source of truth.

## Engine classes

| Class | Role |
|---|---|
| `UnobtaniumCaptureProxy` | The proxy (Unobtanium.Web.Proxy). Selective-decrypt of auxbrain only. Implements `ICaptureProxy` over `CapturedFlow`, the engine seam. |
| `CaptureHub` / `FlowProcessor` / `FlowDecoder` | Receive, decode, and fan out captured flows. |
| `HarWriter` | Appends each flow to the session HAR. |
| `CaptureSession` | The thread-safe start/stop lifecycle owner the web app drives. Persists device + routes changes. |
| `DeviceStore` | Remembers devices across runs in the device store file. The hub seeds + merges live/offline; the session persists on change. |
| `LiveVersionStore` | Persists the latest live app version per platform to the live-version store file. Source is the harvested rinfo. This is the authoritative iOS `clientVersion` and build, which the static binary cannot give. |
| `DeviceRinfoStore` | The per-device sibling of `LiveVersionStore`, keyed by device id, in the per-device rinfo store file. Persistent per-device capture points each device at its own listener, so a harvested flow maps to exactly one device. |

Captured flows funnel into Core's `EndpointExtractor.ProcessFlow`, the same per-flow contract the HAR import path uses, so a live capture and a HAR replay yield the same endpoints.

The Blazor capture page subscribes to `CaptureHub` in-process over its SignalR circuit for the live flow stream. The controller's SSE `stream` endpoint (`/api/capture/stream`) is no longer used by the UI but remains for external consumers.

## Device farm (auto-capture)

The manual walkthrough below is still the path for ad-hoc capture from any phone. Separately, the project drives a fixed farm of rooted Android and jailbroken iOS devices with zero on-device taps. Capture, proxy config, and CA trust are all automated over the same control channels the probes use (`adb` / `ssh`).

Gated by `DeviceCapture:Enabled` (default off). When off, the farm path no-ops entirely and only the manual walkthrough applies.

How it differs from the manual path:

- **Persistent per-device listeners.** `DeviceCaptureManager` (in the web project) gives each declared device its own long-lived capture proxy on a dedicated loopback and LAN port. A harvested flow maps to exactly one device by listener identity. Failure of one device's proxy is isolated and never kills the others.
- **Proxy auto-pointed.** The device's system HTTP proxy is set to its listener via Core's `IDeviceProxyConfigurator` (`AdbProxyConfigurator` over `adb`, `IosProxyConfigurator`). No manual Wi-Fi proxy entry.
- **CA auto-installed and trusted.** Core's `IDeviceCaInstaller` installs the capture root CA on the device with no tap. Android (`AdbCaInstaller`) writes the cert into the system trust store on a rooted device, bind-mounting into zygote's mount namespace so freshly forked apps see it. iOS (`IosCaInstaller`) inserts a trust row into `TrustStore.sqlite3` over ssh and restarts `trustd`. Called on capture start and again whenever the proxy mints a fresh CA.
- **Rinfo harvest, not endpoints.** The farm path is rolling-rinfo-only: no HAR, no endpoint extract. It decodes each request enough to read `BasicRequestInfo` (`clientVersion` / `version` / `build` / `platform`) via Core's `RinfoHarvester` and stores the latest per device in `DeviceRinfoStore`. This records what devices report on the wire.
- **Client version from the binary.** The authoritative `clientVersion` is no longer harvested from traffic. `GameBinaryProvider` extracts it deterministically from the stored device binary (the `GameController::currentClientVersion` constant). The old restart-app endpoint that forced a fresh auxbrain call is gone; passive rinfo capture still records what devices report.

Per-device diagnostics tell you which boundary failed when no rinfo harvests: client connects 0 means the device is not routing through the proxy; auxbrain connects 0 means it reaches the proxy but not auxbrain; flows 0 with auxbrain connects means the CA is not trusted and the TLS handshake fails.

## Device-capture walkthrough

Record real auxbrain traffic from a phone.

### Prerequisites

- This repo checked out with the .NET SDK installed.
- A phone (iOS or Android) on the same Wi-Fi network as the computer.
- Optionally, your EID (`EI1234567890123456`). It scrubs your ID from saved endpoints and names the HAR.

### 1. Start the proxy

Run the app, then turn the proxy on from the Capture tab at `http://localhost:5080/capture`, with **Start capture** / **Stop capture** buttons.

```
dotnet run --project EggIncognito
```

Or auto-start at launch. This opens the Capture tab. `--eid` and `--label` are optional.

```
dotnet run --project EggIncognito -- --capture --eid EI1234567890123456 --label iphone
```

The app surfaces two things you need:

- The listening port. Default `8080`, set by `CapturePort`.
- The root CA path, set by `CaPath`. The CA is generated on first run and reused after that.

### 2. Find the computer's LAN IP

- Windows: `ipconfig`, read the IPv4 Address (e.g. `192.168.1.50`).
- macOS / Linux: `ifconfig` or `ip addr`.

### 3. Point the phone's Wi-Fi proxy at the computer

- iOS: Settings -> Wi-Fi -> (i) next to the network -> Configure Proxy -> Manual -> Server = the computer's IP, Port = `8080`.
- Android: Settings -> Wi-Fi -> long-press the network -> Modify network -> Advanced/Proxy -> Manual -> Proxy hostname = the computer's IP, Port = `8080`.

### 4. Get the CA onto the phone

Transfer the CA file to the phone. Use AirDrop on iOS, email, or serve the folder. To serve it, run `python -m http.server` inside the capture directory, then open `http://<computer-ip>:8000/eggincognito-ca.cer` on the phone.

### 5. Install and trust the CA

Installing the certificate and trusting it are separate actions. Both are required.

iOS:

1. Install the profile: Settings -> General -> VPN & Device Management -> the downloaded profile -> Install.
2. Enable full trust: Settings -> General -> About -> Certificate Trust Settings -> toggle on the EggIncognito certificate.

Without step 2, decryption fails and Egg, Inc. will not load.

Android: Settings -> Security -> Encryption & credentials -> Install a certificate -> CA certificate -> pick the file. Exact wording varies by version and manufacturer.

### 6. Play

Open Egg, Inc. and navigate around. Each captured flow shows up as a row on the Capture tab. It also prints a `capture ei/<endpoint>` line on the console. The more you do in-game, the more endpoints you get.

### 7. Stop

Click **Stop capture**, or stop the app. On stop, in one pass, the proxy:

- writes the HAR under `CapturePath`,
- saves the route map once,
- surfaces the flow counts (`new / upd / diff / same / loss / err`) and the self-repair report on the dashboard.

If no auxbrain traffic was seen, no HAR is written.

## What you get per flow

For each Egg, Inc. flow, in one pass, the tool:

- appends a HAR entry,
- decodes the flow and writes a redacted endpoint to the mock server's endpoint store,
- self-repairs the route map by filling in newly-seen endpoints and types.

Without `CaptureOverwrite`, changed endpoints are staged for review rather than overwritten, so you can diff them before applying. The capture directory is gitignored. It can hold player data and the CA.

## Re-running a capture

The HAR is the durable artifact. Replay it to reproduce the same endpoints without the phone:

1. Open the **Import** tab (`/import`).
2. Choose the session HAR.
3. Import.

Tick "overwrite existing" to apply changes instead of staging them. The live-capture and HAR-import paths share the same extraction code, so a replay yields the same endpoints. It is idempotent. Import is a Local-mode feature. It is disabled on the public hosted deploy.

## Configuration

| Setting | Default | Effect |
|---|---|---|
| `CapturePort` | `8080` | Proxy listen port. |
| `EGG_INC_EID` | (unset) | Your EID. Scrubbed from saved endpoints and added to the HAR name when it matches `^EI\d{16,}$`. |
| `CaptureLabel` | (none) | Label added to the HAR filename, e.g. `iphone`. |
| `CaptureOverwrite` | off | Overwrite existing endpoints instead of staging diffs. |
| `CapturePath` | capture directory | HAR output directory. |
| `CaPath` | CA file in capture directory | Persisted root CA file. |

## Troubleshooting

- **Egg, Inc. won't load.** The CA is not trusted. On iOS, check Certificate Trust Settings (step 5, item 2). Installing the profile alone is not enough.
- **`No auxbrain flows captured`.** The phone is not routing through the proxy. Recheck the IP and port, confirm both devices share the Wi-Fi network, and confirm the proxy is reachable.
- **Other apps break.** Only auxbrain traffic is decrypted, so this should not happen. If it does, remove the proxy setting on the phone. Nothing else was inspected.
- **When finished, remove the Wi-Fi proxy setting on the phone.** Otherwise it keeps routing through a proxy that is no longer running.
