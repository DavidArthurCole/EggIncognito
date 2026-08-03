# EggIncognito.DeviceTools

On-device native tooling for the jailbroken iOS capture/extraction device. The frida scripts are embedded resources served to the app through a typed accessor; the Theos tweak sources ship as project content and build on the device host. The C# compiles in the solution; the `ios/` payloads run on the phone.

## eggupdate tweak

Headless App Store update of Egg, Inc., so each new proto extracts with zero taps. An ellekit dylib loads into the App Store (not frida injection, which proved unsafe on this device), watches a trigger file the app touches over ssh, and drives the phone's own logged-in StoreServices session through the update. That reuse dodges the auth wall that kills external downloaders. An arming flag gates the actual install.

Build on the device host with the Theos toolchain:

```bash
make package THEOS=$HOME/theos                    # unarmed
make package EGGUPDATE_ARMED=1 THEOS=$HOME/theos  # armed
```

## frida scripts

Live-capture scripts for particle-effect research: one hooks the particle renderer and logs per-frame transforms, one profiles the main thread to locate the particle functions. Copied and run over ssh.
