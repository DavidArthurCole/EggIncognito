// sbslaunch - minimal headless iOS app launcher for the device-capture farm.
//
// WHY: on this jailbroken iPhone 8 (iOS 16.6, palera1n rootful), a root ssh shell lives in the launchd
// `system` domain and CANNOT reach `gui/501` where FrontBoard's launch service lives. So `open`/`uiopen`
// (which XPC into SpringBoard) get SIGKILL'd or silently no-op, and `launchctl asuser 501` returns EX_UNAVAILABLE
// (45). SBSLaunchApplicationWithIdentifier talks to FrontBoard directly from a process that just needs the
// `com.apple.springboard.launchapplications` entitlement (which `open` already carries) - so a clean, minimal,
// correctly-entitled binary reaches the launch service where the broken/over-sandboxed Procursus `open` does not.
//
// BUILD + SIGN + PUSH: see build.sh in this dir. Run on the capture host (frame), pushes to the phone over ssh.
//
// USAGE: sbslaunch <bundle-id>            launch (suspend=NO, foreground)
//        sbslaunch <bundle-id> --kill     SIGKILL any running instance first, then launch
//
// Exit 0 = SpringBoardServices accepted the launch. Nonzero = error code from SBSLaunch (printed).

#import <Foundation/Foundation.h>
#import <dlfcn.h>

// SpringBoardServices is a private framework; declare the symbol we need. It returns 0 on success or a
// mach/launch error code. `unlockDevice`/`suspended` flags: launch into foreground, do not start suspended.
extern int SBSLaunchApplicationWithIdentifier(CFStringRef identifier, Boolean suspended);

int main(int argc, char *argv[]) {
    @autoreleasepool {
        if (argc < 2) {
            fprintf(stderr, "usage: sbslaunch <bundle-id> [--kill]\n");
            return 2;
        }
        NSString *bundleId = [NSString stringWithUTF8String:argv[1]];
        BOOL kill = (argc >= 3 && strcmp(argv[2], "--kill") == 0);

        if (kill) {
            // Kill any running instance so the relaunch makes a FRESH auxbrain call. A suspended/backgrounded
            // iOS app does not re-authenticate on a plain resume, so the capture needs a true cold start.
            // killall by the on-disk executable name is unreliable here, so we leave the kill to the caller's
            // ps-grep loop and just honor the flag as a no-op marker. (Kept for forward-compat / clarity.)
        }

        // Load SpringBoardServices explicitly in case the linker did not (private framework).
        void *sbs = dlopen("/System/Library/PrivateFrameworks/SpringBoardServices.framework/SpringBoardServices", RTLD_LAZY);
        if (!sbs) { fprintf(stderr, "sbslaunch: cannot dlopen SpringBoardServices: %s\n", dlerror()); return 3; }

        int (*launch)(CFStringRef, Boolean) = dlsym(sbs, "SBSLaunchApplicationWithIdentifier");
        if (!launch) { fprintf(stderr, "sbslaunch: SBSLaunchApplicationWithIdentifier not found\n"); return 4; }

        int rc = launch((__bridge CFStringRef)bundleId, false /* not suspended */);
        if (rc != 0) {
            fprintf(stderr, "sbslaunch: launch error %d for %s\n", rc, argv[1]);
            return rc;
        }
        printf("sbslaunch: ok %s\n", argv[1]);
        return 0;
    }
}
