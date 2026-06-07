# Capturing Egg, Inc. Traffic

Part of [EggIncognito](README.md) - see the main README for the project overview.

This is a step-by-step, beginner-friendly guide for capturing real Egg, Inc. (auxbrain.com) network traffic from a phone and turning it into mock-server endpoints. You do not need any prior experience with proxies or MITM (man-in-the-middle) work. Just follow the steps in order.

---

## What this does (in plain terms)

EggIncognito includes a built-in capture proxy written in C#. Your phone sends its traffic through this proxy running on your computer. The proxy looks at the Egg, Inc. requests and responses, decodes them, and saves them as endpoint files the mock server can replay later.

The most important safety property: **the proxy only decrypts traffic to Egg, Inc. servers.** Everything else passes through untouched.

---

## The one safety thing to understand: selective decryption

The proxy decrypts TLS (the encryption on HTTPS) **only** for auxbrain hosts:

- `*.auxbrain.com`
- Google App Engine hosts matching `*-dot-auxbrainhome.appspot.com`

**All other HTTPS traffic** - Apple, Google, the App Store, banking apps, anything that is certificate-pinned - **passes through untouched and is never decrypted.** That means the rest of your phone's apps keep working normally while you capture. Only Egg, Inc. is inspected.

---

## Before you start

You will need:

- This repository checked out, with the .NET SDK installed (so `dotnet run` works).
- A phone (iOS or Android).
- The phone and the computer on the **same Wi-Fi network**.
- Your Egg, Inc. EID (looks like `EI1234567890123456`). This is optional but recommended.

---

## Step-by-step walkthrough

### 1. Start the proxy on your computer

Capture is part of the main app. Start the app, then turn the proxy on - either from the GUI or
with a launch flag.

From the GUI: run `dotnet run --project EggIncognito`, open the Capture tab at
`http://localhost:5080/capture/`, and click **Start capture**. Click **Stop capture** to turn it off.

To auto-start the proxy at launch:

```
dotnet run --project EggIncognito -- --capture
```

On start, the app prints / surfaces two things you need:

- The **listening port** (default `8080`, configurable via the `CapturePort` setting).
- The **root CA path** (default `captures/eggincognito-ca.cer`, configurable via `CaPath`). A
  self-signed root CA is exported there on first run.

Leave the proxy running. The Capture tab shows a live row for each captured Egg, Inc. flow.

Capture behavior is configured via app settings / environment variables (defaults shown):

| Setting | Default | What it does |
|---|---|---|
| `CapturePort` | `8080` | The port the proxy listens on. |
| `EGG_INC_EID` | (env var) | Your Egg, Inc. EID. Used to scrub your EID out of saved endpoints and to name the HAR file. To be used in the filename, it must match `^EI\d{16,}$`. |
| `CaptureLabel` | (none) | A label added to the HAR filename (for example `iphone`). |
| `CaptureOverwrite` | (off) | Overwrite existing endpoints instead of staging diffs for review. |
| `CapturePath` | `captures/` | Directory the HAR is written to. |
| `CaPath` | `captures/eggincognito-ca.cer` | The persisted root CA file. |

### 2. Find your computer's LAN IP address

Your phone needs to know where your computer is on the network.

- **Windows:** run `ipconfig` and look for the **IPv4 Address** (for example `192.168.1.50`).
- **macOS / Linux:** run `ifconfig` or `ip addr`.

Write this IP down. The phone and computer must be on the **same Wi-Fi network**.

### 3. Point the phone's Wi-Fi proxy at your computer

- **iOS:** Settings -> Wi-Fi -> tap the **(i)** next to your network -> Configure Proxy -> **Manual** -> Server = your computer's IP, Port = `8080`.
- **Android:** Settings -> Wi-Fi -> long-press your network -> Modify network -> Advanced/Proxy -> **Manual** -> Proxy hostname = your computer's IP, Port = `8080`.

### 4. Get the CA certificate onto the phone

The phone needs the CA file (`captures/eggincognito-ca.cer`) so it can trust the proxy. Easy ways to transfer it:

- **AirDrop** (iOS).
- **Email it to yourself** and open it on the phone.
- **Serve the folder over HTTP:** run `python -m http.server` inside the `captures` directory, then open `http://<computer-ip>:8000/eggincognito-ca.cer` in the phone's browser.

### 5. Install AND trust the CA on the phone

**This is the step people get wrong. Read it carefully.** Installing the certificate is not enough on its own - you also have to **trust** it.

**iOS is TWO separate steps:**

1. **Install the profile:** Settings -> General -> VPN & Device Management -> tap the downloaded profile -> **Install**.
2. **Then separately enable full trust:** Settings -> General -> About -> Certificate Trust Settings -> toggle **ON** the EggIncognito certificate.

If you skip step (b), decryption fails and Egg, Inc. will not load.

**Android:** Settings -> Security -> Encryption & credentials -> Install a certificate -> **CA certificate** -> pick the file. (The exact wording varies by Android version and manufacturer.)

### 6. Open Egg, Inc. and play

Open Egg, Inc. on the phone and play / navigate around. Watch your computer's console. Lines like this appear as flows are captured:

```
  capture ei/<endpoint>
```

The more you do in the game, the more endpoints get captured.

### 7. Finish by stopping capture

When you are done, click **Stop capture** on the Capture tab (or stop the app). On stop, in one pass:

- The HAR is written to `captures/session[_label][_EID].har` (under `CapturePath`).
- The `routes.yaml` editor saves once.
- The captured flow counts (`new / upd / diff / same / loss / err`) and the self-repair report are
  available from the dashboard.

If no Egg, Inc. traffic was seen, no HAR is written (see Troubleshooting).

---

## What happens to each captured flow

For every Egg, Inc. flow, in a single pass, the tool:

1. Appends an entry to the HAR.
2. Decodes the flow, redacts PII, and writes an endpoint to `EggIncognito/Endpoints/default/<namespace>/<endpoint>.json`.
3. "Self-repairs" `routes.yaml` - newly-seen endpoint and type information is filled in automatically.

A live `  capture ei/<endpoint>` log line prints per captured flow.

---

## What you get

- **Endpoints** under `EggIncognito/Endpoints/default/`, grouped by namespace.
- **A HAR file** in `captures/`.
- **`routes.yaml`** updated with any newly-seen endpoints and types.

Without `--overwrite`, changed endpoints are **staged for review** rather than overwritten, so you can inspect diffs before applying them.

The `captures/` directory is gitignored, because it can contain player data and the CA certificate.

---

## Re-running a capture

The **HAR file is the durable artifact.** Once you have it, you can reproduce the exact same endpoints without the phone, by replaying the HAR through the Seeder:

```
dotnet run --project EggIncognito.Seeder -- --from-har captures/session_iphone_EI....har
```

Add `--overwrite` to apply changes instead of staging them.

The in-process capture path and the file replay path are kept in sync, so this produces the same endpoints as the live capture. It is safe and idempotent to re-run.

---

## Troubleshooting

- **Egg, Inc. won't load / connection errors on the phone.** The CA is not trusted. On iOS, check **Certificate Trust Settings** (step 5b) - installing the profile alone is not enough.
- **Nothing captured (`No auxbrain flows captured`).** The phone's proxy is not actually routing through your computer. Re-check the IP and port, confirm both devices are on the same Wi-Fi network, and confirm the proxy is reachable.
- **Other apps break.** This should not happen, because only Egg, Inc. traffic is decrypted - everything else is passthrough. If it does, remove the proxy setting on the phone; nothing else was being inspected.
- **When you are done, REMOVE the Wi-Fi proxy setting on the phone.** Otherwise the phone keeps trying to route traffic through your computer after the proxy has stopped.

---

## How it works under the hood

For the wire format, architecture, and how flows become endpoints, see [TECHNICAL.md](TECHNICAL.md).
