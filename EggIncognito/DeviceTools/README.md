# DeviceTools

On-device native tooling for the jailbroken iOS capture/extraction device, part of the app project. The frida scripts are embedded resources served through a typed accessor; the Theos tweak sources build on the device host. The `ios/` payloads run on the phone.

## eggupdate tweak

Headless App Store update of Egg, Inc., so each new proto extracts with zero taps. An ellekit dylib loads into the App Store (not frida injection, which proved unsafe on this device), watches a trigger file the app touches over ssh, and drives the phone's own logged-in StoreServices session through the update. That reuse dodges the auth wall that kills external downloaders. An arming flag gates the actual install.

Build on the device host with the Theos toolchain:

```bash
make package THEOS=$HOME/theos                    # unarmed
make package EGGUPDATE_ARMED=1 THEOS=$HOME/theos  # armed
```

## egiuinav tweak

On-device UI navigation: drives synthetic touch/HID events and screen captures through a file-based command channel (`/tmp/egi-uinav.*`), so the app can tap through device UI without a jailbreak-unfriendly automation stack. Same Theos build path as `eggupdate`.

## frida scripts

Live-capture scripts for particle-effect research: one hooks the particle renderer and logs per-frame transforms, one profiles the main thread to locate the particle functions. Copied and run over ssh.
