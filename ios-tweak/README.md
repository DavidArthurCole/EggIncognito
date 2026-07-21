# iOS auto-update tweaks (eggnoop + eggupdate)

Headless Egg Inc App Store update for the frame-tethered jailbroken iPhone, so each new proto can be
extracted with zero taps. This is mission step 2 for iOS.

Two tweaks:

| Package | Purpose |
|---|---|
| `me.eggincognito.eggnoop` | Phase B: prove a launch-time tweak loads stably in SpringBoard. Logs one line on load, nothing else. |
| `me.eggincognito.eggupdate` | Phase C: file-watch trigger -> two-phase StoreServices update of Egg Inc. |

## How it works

- ellekit loads the dylib into SpringBoard at launch (filter = `com.apple.springboard`). NOT frida injection (that panicked the phone once; see memory `ios-frida-spike-danger`).
- `eggupdate` watches `/var/root/eggupdate.trigger` via a kqueue dispatch source. frame fires the update by `touch`-ing that file over ssh. No notifyutil/nc/socat needed (none installed).
- On trigger: phase 1 `getUpdatesWithCompletionBlock:` on `[ASDUpdatesService defaultService]`, WAIT for callback, find adam-id `993492744`, phase 2 install. The phone's existing logged-in StoreServices session is used, so it never re-auths -> dodges the GSA/Anisette wall that kills ipatool.
- Result logged to `/var/root/eggupdate.log`; frame's authoritative check is `ideviceinstaller -l` over usbmux.

## Build (on frame, no device needed)

Theos is set up at `~/theos` on frame (toolchain: kabiroberai swift-5.8 ubuntu22.04; SDK: iPhoneOS16.5).

```bash
export THEOS=$HOME/theos
cd ~/ios-tweak/noop     && make package THEOS=$HOME/theos   # -> packages/*.deb
cd ~/ios-tweak/eggupdate && make package THEOS=$HOME/theos   # unarmed (EGGUPDATE_ARMED=0)
# after the phase-2 selector is confirmed on device (see step 5):
cd ~/ios-tweak/eggupdate && make package EGGUPDATE_ARMED=1 THEOS=$HOME/theos
```

Both already build clean. `.deb`s are in each dir's `packages/`.

## Connection (all from frame)

```bash
PHONE="ssh -p 2222 -i ~/.ssh/id_phone_ed25519 -o StrictHostKeyChecking=no root@192.168.1.132"
UDID=3489c6b0dd912b500cafa5741428b25cca88d406
# SAFE version read (usbmux, jailbreak-independent) - use after EVERY device step:
ideviceinstaller -u $UDID -l 2>&1 | grep -i auxbrain
```

Phone IP is DHCP (`192.168.1.132` as of 2026-06-18); if unreachable, owner supplies new IP.

## SUPERVISED device steps (owner present - one at a time, verify between)

Hard safety rules (memory `ios-frida-spike-danger`):
after EVERY step run the safe version read + an ssh `echo`. If either fails -> phone likely panicked ->
STOP, do not pile on recovery, document, wait. One destabilizing op at a time.

### 1. Install ldid on the phone (safe, just a binary)
```bash
$PHONE 'apt-get update && apt-get install -y ldid'   # procursus repo, candidate 2.1.5
```
Not strictly required (Theos signs the deb on frame), but useful for on-device fiddling.

### 2. Phase B: validate the no-op tweak loads + resprings safely  ← DO THIS FIRST
```bash
scp ~/ios-tweak/noop/packages/me.eggincognito.eggnoop_*.deb root@PHONE:/tmp/   # via the phone ssh, adapt host
$PHONE 'dpkg -i /tmp/me.eggincognito.eggnoop_*.deb'
$PHONE 'sbreload || killall -9 SpringBoard'   # respring = normal tweak behavior, NOT frida injection
# wait ~20s for SpringBoard to come back, then:
ideviceinstaller -u $UDID -l >/dev/null && $PHONE 'echo ALIVE; cat /var/root/eggnoop.log'
```
- Log line present AND phone responsive  -> SpringBoard host is safe. Proceed.
- Phone safe-mode / unresponsive  -> SpringBoard host is OUT. STOP. Reassess (standalone helper daemon
  instead of a SpringBoard tweak). Do not proceed to eggupdate.

### 3. Set up the downgrade test loop (the 1.35.8 IPA gives infinite "needs update" respawns)
IPA is on frame at `/home/david/com.auxbrain.egginc-1.35.8.ipa` (decrypted, unsigned -> needs a sig-bypass
installer). The phone currently has NO TrollStore/AppSync. Options to install the downgrade:
- install AppSync Unified (Sileo/apt) + `ideviceinstaller -u $UDID -i <ipa>`, or
- TrollStore CLI.
Decide with owner; AppSync+ideviceinstaller is the lighter path. Verify the read shows 1.35.8 after.
Then nudge availability: `$PHONE 'uiopen itms-apps://itunes.apple.com/app/id993492744'` (safe, proven exit 0).

### 4. Phase C dry run (eggupdate UNARMED - safe, no install)
```bash
scp ~/ios-tweak/eggupdate/packages/me.eggincognito.eggupdate_*.deb root@PHONE:/tmp/
$PHONE 'dpkg -i /tmp/me.eggincognito.eggupdate_*.deb && (sbreload || killall -9 SpringBoard)'
# wait, verify alive, then fire the trigger:
$PHONE 'touch /var/root/eggupdate.trigger'
sleep 5; $PHONE 'cat /var/root/eggupdate.log'
```
Unarmed: it runs phase 1, finds the egginc update object, and DUMPS ITS METHODS to the log, then stops
before any install. Read the dumped methods to pick the exact phase-2 install selector.

### 5. Confirm the phase-2 selector, arm, retest
- From the `eggUpdate methods` dump in `/var/root/eggupdate.log`, identify the install selector
  (`updateAllWithOrder:completionBlock:` scoped to egginc, or `SSPurchase`+`SSPurchaseRequest`).
- Wire it into the eggupdate tweak's `installUpdate()` (replace the TODO block).
- `make package EGGUPDATE_ARMED=1`, reinstall, downgrade to 1.35.8 (step 3), fire trigger, then verify
  `ideviceinstaller -l` climbs to 1.36. Iterate via the downgrade loop until reliable.

### 6. Wire frame on
Once the armed tweak reliably climbs the version, enable the app updater on frame:
```
DeviceUpdate:Enabled=true
DeviceUpdate:Ios:Enabled=true
DeviceUpdate:Ios:SshHost=192.168.1.132
DeviceUpdate:Ios:SshKeyPath=/home/david/.ssh/id_phone_ed25519
# optional: SshPort (2222), TriggerPath (/var/root/eggupdate.trigger), PollSeconds (15), PollAttempts (24)
```
`IosDeviceUpdater` ssh-touches the trigger, then polls usbmux until the version climbs. Stays OFF until
proven (mutating action; frame opts in).

## Status (2026-06-18, owner AFK)

Done while AFK (no device risk taken):
- Theos + toolchain + SDK on frame; both tweaks compile/sign/package clean.
- `IosDeviceUpdater` rewritten to the touch-trigger + usbmux-poll flow, frida path dropped.

Blocked on supervised device work (steps 2-5 above) because they respring SpringBoard / install over the
App Store version while the owner is AFK. Left ready to run.
