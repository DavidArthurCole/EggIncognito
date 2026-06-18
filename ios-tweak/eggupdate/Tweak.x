// eggupdate.dylib (Phase C). Launch-time ellekit tweak loaded into SpringBoard (always alive, has
// StoreServices loaded). Drives the phone's EXISTING, trusted StoreServices session to update Egg Inc
// to the latest App Store version with no taps. This dodges the GSA/Anisette re-auth wall that kills
// ipatool, because it never re-authenticates: it uses the session already signed in on the device.
//
// SAFETY: this is a launch-time TWEAK, not frida injection into a running daemon. The injection path
// kernel-panicked the phone once (see docs memory ios-frida-spike-danger). Loading a dylib at SpringBoard
// launch is the supported, stable mechanism. Validate with the no-op tweak (../noop) FIRST.
//
// TRIGGER: frame fires the update over ssh WITHOUT injecting anything, by touching a watched file:
//     ssh phone 'touch /var/root/eggupdate.trigger'
// We kqueue-watch that path via a GCD dispatch source. (notifyutil is not installed on the phone and
// nc/socat are absent, so a file-watch is the zero-extra-dependency trigger. A Darwin-notification
// alternative using notify_register_dispatch on "me.eggincognito.update" is left commented below.)
//
// TWO-PHASE update flow (the prior frida no-op called phase 2 with an empty list; this WAITS for phase 1):
//   Phase 1: [[ASDUpdatesService defaultService] getUpdatesWithCompletionBlock:]  -> populate pending list (ASYNC)
//   Phase 2: in the callback, find the egginc update (adam-id 993492744), then install it.
//
// The exact phase-2 install selector (updateAllWithOrder: vs SSPurchase+SSPurchaseRequest) MUST be
// confirmed on-device by enumerating the update object's methods (read-only ObjC introspection from
// INSIDE this tweak's own process at trigger time, NOT by injecting a running daemon). The dumpAllMethods()
// helper below logs them so a supervised run can pick the call. Until confirmed, install is gated behind
// EGGUPDATE_ARMED (off by default) so a trigger only does the safe phase-1 + introspection, never a blind install.

#import <Foundation/Foundation.h>
#import <dispatch/dispatch.h>
#import <sys/event.h>
#import <fcntl.h>
#import <objc/runtime.h>
#import <objc/message.h>

// SpringBoard runs as user `mobile`, which cannot write /var/root. Both paths live under /var/mobile
// (mobile-owned) so the tweak can create + write them and frame can `touch` the trigger over ssh.
static NSString *const kTriggerPath = @"/var/mobile/eggupdate.trigger";
static NSString *const kLogPath     = @"/var/mobile/eggupdate.log";
static const long long kEggIncAdamId = 993492744; // Egg Inc, US storefront, bundle com.auxbrain.egginc

// Set to 1 only AFTER the phase-2 install selector is confirmed on-device. While 0, a trigger logs the
// pending-update list + the egginc update object's methods and STOPS short of any install (safe to fire).
#ifndef EGGUPDATE_ARMED
#define EGGUPDATE_ARMED 0
#endif

static void egglog(NSString *fmt, ...) {
    va_list ap; va_start(ap, fmt);
    NSString *msg = [[NSString alloc] initWithFormat:fmt arguments:ap];
    va_end(ap);
    NSString *line = [NSString stringWithFormat:@"%@ %@\n", [NSDate date], msg];
    FILE *f = fopen([kLogPath fileSystemRepresentation], "a");
    if (f) { fwrite([line UTF8String], 1, strlen([line UTF8String]), f); fclose(f); }
}

// Read-only introspection: dump every method of an object's class so a supervised run can pick the
// correct phase-2 install selector. Safe (no calls, just name logging).
static void dumpAllMethods(id obj, NSString *label) {
    if (!obj) { egglog(@"  %@ = nil", label); return; }
    Class c = object_getClass(obj);
    egglog(@"  %@ class = %s", label, class_getName(c));
    unsigned int n = 0;
    Method *methods = class_copyMethodList(c, &n);
    for (unsigned int i = 0; i < n; i++) {
        egglog(@"    -[%s %s]", class_getName(c), sel_getName(method_getName(methods[i])));
    }
    free(methods);
}

// Phase 1: ask StoreServices for the pending update list, WAIT for the async callback, then hand the
// egginc update object (if any) to the completion. Uses ASDUpdatesService when present; this class was
// confirmed loaded in SpringBoard in a prior session.
static void fetchUpdatesThen(void (^then)(id eggUpdate, NSArray *allUpdates)) {
    Class ASDUpdatesService = NSClassFromString(@"ASDUpdatesService");
    if (!ASDUpdatesService) { egglog(@"ASDUpdatesService not found in this process"); then(nil, nil); return; }

    id svc = ((id(*)(id, SEL))objc_msgSend)(ASDUpdatesService, sel_registerName("defaultService"));
    if (!svc) { egglog(@"ASDUpdatesService defaultService = nil"); then(nil, nil); return; }

    // Always dump the service's methods so a supervised run can see the available refresh/install selectors.
    dumpAllMethods(svc, @"ASDUpdatesService");

    // Does SpringBoard's StoreServices instance hold the update entitlement? The App Store app does; if
    // SpringBoard does not, reloadFromServer no-ops (instant empty callback) and we must host elsewhere.
    if ([svc respondsToSelector:sel_registerName("hasEntitlement")]) {
        BOOL ent = ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("hasEntitlement"));
        egglog(@"ASDUpdatesService hasEntitlement = %d", ent);
    }
    if ([svc respondsToSelector:sel_registerName("autoUpdateEnabled")]) {
        BOOL au = ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("autoUpdateEnabled"));
        egglog(@"ASDUpdatesService autoUpdateEnabled = %d", au);
    }

    SEL getSel = sel_registerName("getUpdatesWithCompletionBlock:");
    if (![svc respondsToSelector:getSel]) {
        egglog(@"svc has no getUpdatesWithCompletionBlock:");
        then(nil, nil);
        return;
    }

    // getUpdatesWithCompletionBlock: reads the CACHED pending list. After a receipt/version change the cache
    // is stale (App Store UI shows the update but ASDUpdatesService does not), so first ask Apple to refresh
    // it, THEN read. Try the known refresh selectors; fall back to a bare getUpdates if none respond.
    void (^readUpdates)(void) = ^{
        egglog(@"phase 1: getUpdatesWithCompletionBlock: ...");
        void (^completion)(id) = ^(id updates) {
            @autoreleasepool {
                NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                egglog(@"phase 1 callback: %lu updates", (unsigned long)list.count);
                id egg = nil;
                for (id u in list) {
                    long long adam = 0;
                    if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                    else if ([u respondsToSelector:sel_registerName("adamID")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
                    egglog(@"  update adamID=%lld", adam);
                    if (adam == kEggIncAdamId) egg = u;
                }
                then(egg, list);
            }
        };
        ((void(*)(id, SEL, id))objc_msgSend)(svc, getSel, completion);
    };

    // refresh-then-read. The refresh selector on this build (16.7.x) is reloadFromServerWithCompletionBlock:
    // (confirmed by the method dump). It fetches the pending list from Apple; getUpdates then reads the now-
    // fresh cache. Without this, getUpdates returns the stale cache (0 updates) even when the UI shows one.
    SEL reloadSel = sel_registerName("reloadFromServerWithCompletionBlock:");
    if ([svc respondsToSelector:reloadSel]) {
        egglog(@"phase 0: reloadFromServerWithCompletionBlock: ...");
        void (^reloadDone)(id arg) = ^(id arg) {
            @autoreleasepool {
                egglog(@"reloadFromServer callback returned arg=%@ (class %@); reading updates",
                       arg, arg ? NSStringFromClass([arg class]) : @"nil");
                readUpdates();
            }
        };
        ((void(*)(id, SEL, id))objc_msgSend)(svc, reloadSel, reloadDone);
    } else {
        egglog(@"no reloadFromServerWithCompletionBlock:; reading cached updates directly");
        readUpdates();
    }
}

// Phase 2: install the egginc update. The exact selector is build-specific; this logs the object's
// methods and, only when EGGUPDATE_ARMED, attempts the install. The two candidate paths from research:
//   (a) [[ASDUpdatesService defaultService] updateAllWithOrder:completionBlock:] scoped to egginc, or
//   (b) construct SSPurchase + SSPurchaseRequest from the update's buyParameters and [req start].
static void installUpdate(id eggUpdate) {
    if (!eggUpdate) { egglog(@"no egginc update in pending list (downgrade + uiopen nudge may be needed)"); return; }
    egglog(@"egginc update object found; methods follow (pick phase-2 selector from these):");
    dumpAllMethods(eggUpdate, @"eggUpdate");

#if EGGUPDATE_ARMED
    // TODO(supervised): replace with the confirmed selector once dumpAllMethods identifies it.
    // Example shape for candidate (a):
    //   Class ASD = NSClassFromString(@"ASDUpdatesService");
    //   id svc = ((id(*)(id,SEL))objc_msgSend)(ASD, sel_registerName("defaultService"));
    //   SEL inst = sel_registerName("updateAllWithOrder:completionBlock:"); // verify exact selector
    //   ... build order from eggUpdate, then invoke, log completion ...
    egglog(@"EGGUPDATE_ARMED set but no confirmed install selector wired yet; aborting install");
#else
    egglog(@"EGGUPDATE_ARMED=0; phase-2 install withheld (safe). Re-build with -DEGGUPDATE_ARMED=1 after confirming selector.");
#endif
}

static void onTrigger(void) {
    @autoreleasepool {
        egglog(@"trigger fired");
        fetchUpdatesThen(^(id eggUpdate, NSArray *all) {
            installUpdate(eggUpdate);
        });
    }
}

// Watch kTriggerPath for writes via a kqueue dispatch source. The file is created if missing so the
// watch attaches. frame fires by `touch`-ing it.
static dispatch_source_t gSource;
static void installTriggerWatch(void) {
    int fd = open([kTriggerPath fileSystemRepresentation], O_CREAT | O_RDONLY, 0644);
    if (fd < 0) { egglog(@"cannot open trigger path %@ (errno %d)", kTriggerPath, errno); return; }
    gSource = dispatch_source_create(DISPATCH_SOURCE_TYPE_VNODE, fd,
                                     DISPATCH_VNODE_WRITE | DISPATCH_VNODE_ATTRIB | DISPATCH_VNODE_EXTEND,
                                     dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0));
    dispatch_source_set_event_handler(gSource, ^{ onTrigger(); });
    dispatch_source_set_cancel_handler(gSource, ^{ close(fd); });
    dispatch_resume(gSource);
    egglog(@"trigger watch armed on %@", kTriggerPath);

    // Darwin-notification alternative (if notifyutil/notify gets installed later):
    //   int token;
    //   notify_register_dispatch("me.eggincognito.update", &token,
    //       dispatch_get_main_queue(), ^(int t){ onTrigger(); });
}

// Confirm an install landed: observe SBInstalledApplicationsDidChangeNotification and re-log. frame's
// authoritative check is still `ideviceinstaller -l` over usbmux, but this gives an on-device signal.
static void installInstallObserver(void) {
    [[NSNotificationCenter defaultCenter] addObserverForName:@"SBInstalledApplicationsDidChangeNotification"
                                                      object:nil queue:nil
                                                  usingBlock:^(NSNotification *note) {
        egglog(@"SBInstalledApplicationsDidChange");
    }];
}

// Host = the App Store app (com.apple.AppStore), the only process holding the appstored.update-apps
// entitlement (SpringBoard + the appstored daemon return hasEntitlement=0). The app is SIGKILLed within
// ~1s when launched headless, so we do NOT rely on a long-lived file-watch here: instead, when frame wants
// an update it drops a one-shot arm file then launches the app (uiopen itms-apps://...id993492744). On load
// we see the arm file, run the flow immediately (reload->get->install; the install is async XPC to
// storedownloadd which finishes after we die), and delete the arm file so a normal App Store open is a
// no-op. The kqueue watch is kept too, in case the app is already alive when frame touches the trigger.
static NSString *const kArmPath = @"/var/mobile/eggupdate.armed";

static void runIfArmed(void) {
    @autoreleasepool {
        if (![[NSFileManager defaultManager] fileExistsAtPath:kArmPath]) {
            egglog(@"launched but not armed (%@ absent); no-op", kArmPath);
            return;
        }
        [[NSFileManager defaultManager] removeItemAtPath:kArmPath error:nil]; // one-shot
        egglog(@"armed: running update flow on App Store launch");
        onTrigger();
    }
}

%ctor {
    @autoreleasepool {
        egglog(@"eggupdate loaded in %@ pid %d (ARMED=%d)",
               [[NSProcessInfo processInfo] processName], getpid(), EGGUPDATE_ARMED);
        installTriggerWatch();
        installInstallObserver();
        // Give StoreServices a moment to init in the app, then run if frame armed this launch. The app may
        // be SIGKILLed shortly after; the async store calls survive in storedownloadd regardless.
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.0 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{ runIfArmed(); });
    }
}
