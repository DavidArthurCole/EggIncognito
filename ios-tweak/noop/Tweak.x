// Phase-B no-op validation tweak. Loads into SpringBoard at launch, writes one line to a log file,
// does nothing else. Purpose: prove a launch-time ellekit tweak is STABLE on this exact build
// (iOS 16.7.x, A11, rootful palera1n) before adding any StoreServices logic in eggupdate.dylib.
//
// If installing + respringing with this loaded safe-modes the phone, SpringBoard is an unsafe host
// and the eggupdate plan must move to a standalone helper. See docs/device-auto-update-MISSION.md.

#import <Foundation/Foundation.h>

%ctor {
    @autoreleasepool {
        NSString *line = [NSString stringWithFormat:@"%@ eggnoop loaded in pid %d (%@)\n",
                          [NSDate date], getpid(),
                          [[NSProcessInfo processInfo] processName]];
        // SpringBoard runs as user `mobile`, which cannot write /var/root. Log under /var/mobile (mobile-owned).
        NSString *path = @"/var/mobile/eggnoop.log";
        FILE *f = fopen([path fileSystemRepresentation], "a");
        if (f) {
            fwrite([line UTF8String], 1, strlen([line UTF8String]), f);
            fclose(f);
        }
    }
}
