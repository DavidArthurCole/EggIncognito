// wakeunlock - headless wake + (passcode-free) unlock for the capture iPhone.
//
// WHY: the farm phone sits LOCKED with the screen OFF to save power (no passcode, Auto-Lock never). An app
// launched via `uiopen` while the device is locked stays SUSPENDED and never makes its network call, so no
// auxbrain flow is captured. We must wake the display and dismiss the (passcode-free) lock screen over ssh
// before launching the app.
//
// Approach (private SpringBoardServices, all available on iOS 16):
//   1. SBSUndimScreen()           - power the display on (undim/wake).
//   2. SBSUnlockDevice()          - dismiss the lock screen. With no passcode this fully unlocks; if a
//                                   passcode were set it would only reach the passcode prompt.
// Both are weakly resolved via dlsym so a missing symbol degrades to a logged no-op instead of a crash.
//
// USAGE: wakeunlock          (wake + unlock)
//        wakeunlock wake     (wake only)
//
// Build + sign + push: build-wakeunlock.sh (theos toolchain on frame), entitlements in wakeunlock.entitlements.

#import <Foundation/Foundation.h>
#import <dlfcn.h>

int main(int argc, char *argv[]) {
    @autoreleasepool {
        BOOL wakeOnly = (argc >= 2 && strcmp(argv[1], "wake") == 0);

        void *sbs = dlopen("/System/Library/PrivateFrameworks/SpringBoardServices.framework/SpringBoardServices", RTLD_LAZY);
        if (!sbs) { fprintf(stderr, "wakeunlock: dlopen SpringBoardServices failed: %s\n", dlerror()); return 2; }

        void (*undim)(void) = dlsym(sbs, "SBSUndimScreen");
        if (undim) { undim(); printf("wakeunlock: SBSUndimScreen ok\n"); }
        else fprintf(stderr, "wakeunlock: SBSUndimScreen missing\n");

        if (wakeOnly) return 0;

        // Give the display a moment to power up before dismissing the lock screen.
        usleep(400 * 1000);

        int (*unlock)(void) = dlsym(sbs, "SBSUnlockDevice");
        if (unlock) { int rc = unlock(); printf("wakeunlock: SBSUnlockDevice rc=%d\n", rc); }
        else fprintf(stderr, "wakeunlock: SBSUnlockDevice missing\n");

        return 0;
    }
}
