# DeviceTools

On-device native tooling for the jailbroken iOS capture/extraction device, part of the app project. The frida scripts are embedded resources served through a typed accessor; the Theos tweaks build inside a pinned Docker image. The `ios/` payloads run on the phone.

## Building tweaks

`ios/tweaks/Dockerfile` pins Theos, the L1ghtmann iOS clang toolchain and the patched iPhoneOS 16.5 SDK. No Theos install on any host. `ios/tweaks/build.sh` builds the image on first use, runs `make clean package` with the tweak dir mounted, and prints the deb path.

```bash
cd EggIncognito/DeviceTools/ios/tweaks
./build.sh eggupdate                    # unarmed
./build.sh eggupdate EGGUPDATE_ARMED=1  # armed, script verifies the arming string in the dylib
./build.sh uinav
```

Output lands in `<tweak>/packages/`, gitignored. Ship with `scp` + `dpkg -i` on the phone. Bump the `ARG` pins in the Dockerfile and `docker rmi egi-theos` to move toolchain versions.

## eggupdate tweak

Headless App Store update of Egg, Inc., so each new proto extracts with zero taps. An ellekit dylib loads into the App Store, watches a trigger file the app touches over ssh, and drives the phone's own logged-in StoreServices session through the update. Reusing that session is what clears the auth wall an external downloader hits. An arming flag gates the actual install; only an armed build updates anything.

## egiuinav tweak

On-device UI navigation: drives synthetic touch/HID events and screen captures through a file-based command channel (`/tmp/egi-uinav.*`), so the app can tap through device UI without a jailbreak-unfriendly automation stack.

## frida scripts

Live-capture scripts for particle-effect research: one hooks the particle renderer and logs per-frame transforms, one profiles the main thread to locate the particle functions. Copied and run over ssh.
