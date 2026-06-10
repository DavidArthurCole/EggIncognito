# Capturing Egg, Inc. Traffic

Part of [EggIncognito](README.md). This covers recording real auxbrain.com traffic from a phone and
turning it into mock-server endpoints.

## How it works

EggIncognito embeds a TLS-intercepting proxy. The phone routes its traffic through the proxy. The
proxy decodes the Egg, Inc. requests and responses. It writes them out as endpoint files the mock
server can replay.

Decryption is selective. The proxy only decrypts TLS for auxbrain hosts:

- `*.auxbrain.com`
- App Engine hosts matching `*-dot-auxbrainhome.appspot.com`

Everything else tunnels through untouched and is never decrypted. This includes Apple, Google, the
App Store, and certificate-pinned apps. The rest of the phone keeps working while you capture.

## Prerequisites

- This repo checked out with the .NET SDK installed.
- A phone (iOS or Android) on the same Wi-Fi network as the computer.
- Optionally, your EID (`EI1234567890123456`). It scrubs your ID from saved endpoints and names the
  HAR.

## 1. Start the proxy

Run the app, then turn the proxy on from the Capture tab. The tab is at
`http://localhost:5080/capture/`, with **Start capture** / **Stop capture** buttons.

```
dotnet run --project EggIncognito
```

Or auto-start it at launch. This opens the Capture tab. `--eid` and `--label` are optional.

```
dotnet run --project EggIncognito -- --capture --eid EI1234567890123456 --label iphone
```

The app surfaces two things you need:

- The listening port. Default `8080`, set by `CapturePort`.
- The root CA path. Default `captures/eggincognito-ca.cer`, set by `CaPath`. The CA is generated on
  first run and reused after that.

Capture behavior is configured through app settings / environment variables:

| Setting | Default | Effect |
|---|---|---|
| `CapturePort` | `8080` | Proxy listen port. |
| `EGG_INC_EID` | (unset) | Your EID. Scrubbed from saved endpoints and added to the HAR name when it matches `^EI\d{16,}$`. |
| `CaptureLabel` | (none) | Label added to the HAR filename, e.g. `iphone`. |
| `CaptureOverwrite` | off | Overwrite existing endpoints instead of staging diffs. |
| `CapturePath` | `captures/` | HAR output directory. |
| `CaPath` | `captures/eggincognito-ca.cer` | Persisted root CA file. |

## 2. Find the computer's LAN IP

- Windows: `ipconfig`, read the IPv4 Address (e.g. `192.168.1.50`).
- macOS / Linux: `ifconfig` or `ip addr`.

## 3. Point the phone's Wi-Fi proxy at the computer

- iOS: Settings -> Wi-Fi -> (i) next to the network -> Configure Proxy -> Manual -> Server = the
  computer's IP, Port = `8080`.
- Android: Settings -> Wi-Fi -> long-press the network -> Modify network -> Advanced/Proxy -> Manual
  -> Proxy hostname = the computer's IP, Port = `8080`.

## 4. Get the CA onto the phone

Transfer `captures/eggincognito-ca.cer` to the phone. Use AirDrop on iOS, email, or serve the
folder. To serve it, run `python -m http.server` inside `captures/`, then open
`http://<computer-ip>:8000/eggincognito-ca.cer` on the phone.

## 5. Install and trust the CA

Installing the certificate and trusting it are separate actions. Both are required.

iOS:

1. Install the profile: Settings -> General -> VPN & Device Management -> the downloaded profile ->
   Install.
2. Enable full trust: Settings -> General -> About -> Certificate Trust Settings -> toggle on the
   EggIncognito certificate.

Without step 2, decryption fails and Egg, Inc. will not load.

Android: Settings -> Security -> Encryption & credentials -> Install a certificate -> CA certificate
-> pick the file. Exact wording varies by version and manufacturer.

## 6. Play

Open Egg, Inc. and navigate around. Each captured flow shows up as a row on the Capture tab. It also
prints a `capture ei/<endpoint>` line on the console. The more you do in-game, the more endpoints you
get.

## 7. Stop

Click **Stop capture**, or stop the app. On stop, in one pass, the proxy:

- writes the HAR to `captures/session[_label][_EID].har` (under `CapturePath`),
- saves `routes.yaml` once,
- surfaces the flow counts (`new / upd / diff / same / loss / err`) and the self-repair report on the
  dashboard.

If no auxbrain traffic was seen, no HAR is written. See [Troubleshooting](#troubleshooting).

## What you get per flow

For each Egg, Inc. flow, in one pass, the tool:

- appends a HAR entry,
- decodes the flow and writes a redacted endpoint to
  `EggIncognito/Endpoints/default/<namespace>/<endpoint>.json`,
- self-repairs `routes.yaml` by filling in newly-seen endpoints and types.

Without `CaptureOverwrite`, changed endpoints are staged for review rather than overwritten, so you
can diff them before applying. The `captures/` directory is gitignored. It can hold player data and
the CA.

## Re-running a capture

The HAR is the durable artifact. Replay it to reproduce the same endpoints without the phone:

1. Open the **Import** tab (`/import/`).
2. Choose the HAR, e.g. `captures/session_iphone_EI....har`.
3. Import.

Tick "overwrite existing" to apply changes instead of staging them. The live-capture and HAR-import
paths share the same extraction code, so a replay yields the same endpoints. It is idempotent. Import
is a Local-mode feature. It is disabled on the public hosted deploy.

## Troubleshooting

- **Egg, Inc. won't load.** The CA is not trusted. On iOS, check Certificate Trust Settings (step 5,
  item 2). Installing the profile alone is not enough.
- **`No auxbrain flows captured`.** The phone is not routing through the proxy. Recheck the IP and
  port, confirm both devices share the Wi-Fi network, and confirm the proxy is reachable.
- **Other apps break.** Only auxbrain traffic is decrypted, so this should not happen. If it does,
  remove the proxy setting on the phone. Nothing else was inspected.
- **When finished, remove the Wi-Fi proxy setting on the phone.** Otherwise it keeps routing through
  a proxy that is no longer running.

## Internals

For the wire format, architecture, and how flows become endpoints, see [TECHNICAL.md](TECHNICAL.md).
