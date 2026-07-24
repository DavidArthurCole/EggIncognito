# EggIncognito.DeviceTools

On-device native tooling for the jailbroken iOS capture/extraction device. A .NET project so it is a
first-class part of the solution: the frida scripts are embedded as resources and served to the app via
`DeviceScripts` (no fragile runtime path lookup); the Theos tweak sources ship as project content, built
on the device host. The C# in this project compiles; the `ios/` payloads run on the phone.

```
EggIncognito.DeviceTools/
  DeviceScripts.cs      typed accessor: DeviceScripts.ParticleCapture / ParticleDiscover (embedded)
  ios/tweaks/eggupdate/ ellekit tweak: headless App Store update of Egg Inc
  ios/frida/            live frida scripts (embedded), scp'd + run over ssh
```

## ios/tweaks/eggupdate

Headless Egg Inc App Store update, so each new proto extracts with zero taps. ellekit loads the dylib
into the App Store at launch (filter `com.apple.AppStore`), NOT frida injection (that panicked the phone
once; see memory `ios-frida-spike-danger`).

- Watches `/var/mobile/eggupdate.trigger` via a kqueue dispatch source. The app fires the update by
  `touch`-ing that file over ssh (`IosStoreChecker`). No notifyutil/nc/socat needed.
- On trigger: phase 1 `getUpdatesWithCompletionBlock:` on `[ASDUpdatesService defaultService]`, wait for
  callback, find adam-id `993492744`, phase 2 install. Reuses the phone's logged-in StoreServices session,
  so it never re-auths and dodges the GSA/Anisette wall that kills ipatool.
- Result logged to `/var/mobile/eggupdate.log`. Authoritative version check is `ideviceinstaller -l` over
  usbmux. `EGGUPDATE_ARMED` (default 0) gates the install: unarmed dumps the update object's methods.

Build (on the device host, Theos toolchain):

```bash
make package THEOS=$HOME/theos                    # unarmed
make package EGGUPDATE_ARMED=1 THEOS=$HOME/theos  # armed, after the phase-2 selector is confirmed
```

Wire the app on once the armed tweak reliably climbs the version:

```
DeviceUpdate:Enabled=true
DeviceUpdate:Ios:Enabled=true
DeviceUpdate:Ios:SshHost=<phone-ip>
DeviceUpdate:Ios:SshKeyPath=<key>
# optional: SshPort (2222), TriggerPath (/var/mobile/eggupdate.trigger), PollSeconds (15), PollAttempts (24)
```

## ios/frida

- `particle-capture.js`: hooks `ParticleBatchedMesh::addParticle`, logs per-frame transforms as NDJSON,
  self-detaches after 5s. scp'd + run over ssh by `IosParticleCapturer` (admin `POST /api/decomp/particle-capture`).
- `particle-discover.js`: Stalker script that follows the main thread and reports the hottest module-relative
  offsets. Run by hand to locate the particle functions before capture.
