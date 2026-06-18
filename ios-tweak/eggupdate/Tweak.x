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
#import <dlfcn.h>

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

// Read-only ivar dump: log every ivar name + its current object value's class. Used to find the
// connection/proxy ivar inside the local ASDUpdatesService so we can see (and potentially swap) the
// entitled NSXPCConnection the broker owns.
static void dumpAllIvars(id obj, NSString *label) {
    if (!obj) { egglog(@"  %@ ivars = nil", label); return; }
    Class c = object_getClass(obj);
    egglog(@"  %@ ivars (class %s):", label, class_getName(c));
    unsigned int n = 0;
    Ivar *ivars = class_copyIvarList(c, &n);
    for (unsigned int i = 0; i < n; i++) {
        const char *name = ivar_getName(ivars[i]);
        const char *type = ivar_getTypeEncoding(ivars[i]);
        // Only deref object-typed ivars (@...) to read their class; log scalars as type only.
        if (type && type[0] == '@') {
            @try {
                id v = object_getIvar(obj, ivars[i]);
                egglog(@"    %s : %@", name, v ? NSStringFromClass([v class]) : @"nil");
            } @catch (NSException *e) { egglog(@"    %s : <threw>", name); }
        } else {
            egglog(@"    %s : (scalar %s)", name, type ? type : "?");
        }
    }
    free(ivars);
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

// Broker probe (read-only). The bare [ASDUpdatesService defaultService] singleton's XPC connection to
// appstored is not the entitled one (hasEntitlement=0 even from AppStore.app with a real pending update,
// confirmed by the 2026-06-18 downgrade experiment). The App Store UI vends its ASDUpdatesService through
// ASDServiceBroker, which owns the entitled NSXPCConnection. Enumerate the broker + try common vend
// selectors; for any service it hands back, re-check hasEntitlement and (if entitled) run the real flow.
// Pure introspection + read-only update fetch; no install (still gated behind EGGUPDATE_ARMED downstream).
__attribute__((unused)) static void probeServiceBroker(void) {
    Class Broker = NSClassFromString(@"ASDServiceBroker");
    if (!Broker) { egglog(@"ASDServiceBroker not found in this process"); return; }
    egglog(@"ASDServiceBroker found; enumerating");

    // The broker is usually a singleton. Try the common accessors, then its instance methods.
    id broker = nil;
    const char *accessors[] = {"sharedInstance", "defaultBroker", "sharedBroker", "broker"};
    for (unsigned int ai = 0; ai < sizeof(accessors)/sizeof(accessors[0]); ai++) {
        const char *acc = accessors[ai];
        SEL s = sel_registerName(acc);
        if ([Broker respondsToSelector:s]) {
            broker = ((id(*)(id, SEL))objc_msgSend)(Broker, s);
            egglog(@"  +[ASDServiceBroker %s] -> %@", acc, broker);
            if (broker) break;
        }
    }
    if (!broker) {
        @try { broker = ((id(*)(id, SEL))objc_msgSend)((id)Broker, sel_registerName("alloc"));
               broker = ((id(*)(id, SEL))objc_msgSend)(broker, sel_registerName("init"));
               egglog(@"  alloc/init broker -> %@", broker); }
        @catch (NSException *e) { egglog(@"  alloc/init broker threw %@", e.reason); }
    }
    if (!broker) { egglog(@"  could not obtain an ASDServiceBroker instance"); return; }
    dumpAllMethods(broker, @"ASDServiceBroker");

    // Confirmed on-device 2026-06-18: the broker vends the updates service via
    //   -getUpdatesServiceWithError:           (synchronous, returns the service or nil + NSError)
    //   -getUpdatesServiceWithCompletionHandler: (async, hands back service in a block)
    // The service it returns is a proxy over the broker's ENTITLED XPC connection to appstored, unlike the
    // bare [ASDUpdatesService defaultService] singleton. Vend it, re-check hasEntitlement, and if entitled
    // run reload+get through THIS service (read-only; install still gated by EGGUPDATE_ARMED downstream).
    SEL syncSel = sel_registerName("getUpdatesServiceWithError:");
    id brokerSvc = nil;
    if ([broker respondsToSelector:syncSel]) {
        NSError *err = nil;
        @try {
            brokerSvc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, syncSel, &err);
            egglog(@"  getUpdatesServiceWithError: -> %@ (err=%@)", brokerSvc, err);
        } @catch (NSException *e) { egglog(@"  getUpdatesServiceWithError: threw %@", e.reason); }
    }
    if (!brokerSvc) { egglog(@"  broker did not vend an updates service synchronously"); return; }

    // The distant object rejected getUpdates/reload/hasEntitlement (unrecognized selector) on the prior run:
    // the XPC remote protocol uses DIFFERENT method names than the local ASDUpdatesService wrapper class.
    // So: (a) dump the broker-vended distant object's underlying connection to confirm it is the entitled one,
    // and (b) dump the LOCAL ASDUpdatesService instance's ivars to find its connection/proxy ivar, so we can
    // graft the entitled connection into the wrapper (the wrapper's getUpdatesWithCompletionBlock: would then
    // route over the entitled connection). All read-only logging here.
    dumpAllIvars(brokerSvc, @"broker-vended distant object");
    @try {
        // _NSXPCDistantObject privately exposes its connection via -_connection (or the ivar _connection).
        id conn = nil;
        if ([brokerSvc respondsToSelector:sel_registerName("_connection")])
            conn = ((id(*)(id, SEL))objc_msgSend)(brokerSvc, sel_registerName("_connection"));
        egglog(@"  distant-object _connection = %@ (class %@)", conn, conn ? NSStringFromClass([conn class]) : @"nil");
        if (conn) dumpAllIvars(conn, @"entitled NSXPCConnection");
    } @catch (NSException *e) { egglog(@"  _connection probe threw %@", e.reason); }

    // Dump the LOCAL singleton's ivars to locate its (unentitled) connection ivar for a possible graft.
    Class ASDU = NSClassFromString(@"ASDUpdatesService");
    id localSvc = ASDU ? ((id(*)(id, SEL))objc_msgSend)(ASDU, sel_registerName("defaultService")) : nil;
    dumpAllIvars(localSvc, @"local ASDUpdatesService singleton");

    // DECISIVE: the wrapper caches the entitlement in a BOOL ivar `_hasUpdatesEntitlement` and `hasEntitlement`
    // just returns it. If the wrapper REFUSES to call appstored when that flag is 0 (client-side gate), forcing
    // it to 1 then running reload+get will make the real XPC call go out and appstored will answer. If appstored
    // ITSELF enforces (server-side), the call goes out but comes back empty/erroring -> flag flip won't help.
    // This in-memory flip lives only in this short-lived process; nothing persists.
    if (localSvc) {
        Ivar entIvar = class_getInstanceVariable(ASDU, "_hasUpdatesEntitlement");
        if (entIvar) {
            ptrdiff_t off = ivar_getOffset(entIvar);
            BOOL *flag = (BOOL *)((char *)(__bridge void *)localSvc + off);
            egglog(@"  _hasUpdatesEntitlement before flip = %d", *flag);
            *flag = YES;
            BOOL after = ((BOOL(*)(id, SEL))objc_msgSend)(localSvc, sel_registerName("hasEntitlement"));
            egglog(@"  _hasUpdatesEntitlement after flip, hasEntitlement() = %d", after);

            egglog(@"  [flipped-local] phase 0: reloadFromServerWithCompletionBlock: ...");
            void (^getAfter)(void) = ^{
                void (^cb)(id) = ^(id updates) {
                    @autoreleasepool {
                        NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                        egglog(@"  [flipped-local] getUpdates callback: %lu updates", (unsigned long)list.count);
                        for (id u in list) {
                            long long adam = 0;
                            if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                                adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                            egglog(@"  [flipped-local] update adamID=%lld", adam);
                        }
                    }
                };
                ((void(*)(id, SEL, id))objc_msgSend)(localSvc, sel_registerName("getUpdatesWithCompletionBlock:"), cb);
            };
            void (^reloadCb)(id) = ^(id arg) {
                @autoreleasepool {
                    egglog(@"  [flipped-local] reload callback arg=%@ (class %@)",
                           arg, arg ? NSStringFromClass([arg class]) : @"nil");
                    getAfter();
                }
            };
            ((void(*)(id, SEL, id))objc_msgSend)(localSvc, sel_registerName("reloadFromServerWithCompletionBlock:"), reloadCb);
        } else {
            egglog(@"  _hasUpdatesEntitlement ivar not found");
        }
    }

    // Run reload->get through the broker-vended (entitled) service. Call the selectors directly (no
    // respondsToSelector: gate - it lies for distant objects). If this returns the pending update, the
    // lever works: the entitled connection was the missing piece.
    SEL reloadSel = sel_registerName("reloadFromServerWithCompletionBlock:");
    SEL getSel    = sel_registerName("getUpdatesWithCompletionBlock:");
    void (^readVia)(void) = ^{
        egglog(@"  [broker-svc] phase 1: getUpdatesWithCompletionBlock: ...");
        void (^cb)(id) = ^(id updates) {
            @autoreleasepool {
                NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                egglog(@"  [broker-svc] getUpdates callback: %lu updates", (unsigned long)list.count);
                for (id u in list) {
                    long long adam = 0;
                    if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                    else if ([u respondsToSelector:sel_registerName("adamID")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
                    egglog(@"  [broker-svc] update adamID=%lld", adam);
                }
            }
        };
        @try { ((void(*)(id, SEL, id))objc_msgSend)(brokerSvc, getSel, cb); }
        @catch (NSException *e) { egglog(@"  [broker-svc] getUpdates threw %@", e.reason); }
    };
    egglog(@"  [broker-svc] phase 0: reloadFromServerWithCompletionBlock: ...");
    void (^reloadDone)(id) = ^(id arg) {
        @autoreleasepool {
            egglog(@"  [broker-svc] reload callback arg=%@ (class %@)",
                   arg, arg ? NSStringFromClass([arg class]) : @"nil");
            readVia();
        }
    };
    @try { ((void(*)(id, SEL, id))objc_msgSend)(brokerSvc, reloadSel, reloadDone); }
    @catch (NSException *e) { egglog(@"  [broker-svc] reload threw %@; reading directly", e.reason); readVia(); }
}

// In the UI-primed process hasEntitlement is genuinely 1, the UI shows the update, yet legacy
// getUpdatesWithCompletionBlock: returns 0. The tell is -shouldUseModernUpdatesWithCompletionBlock:: on
// modern iOS the pending list lives in a DIFFERENT source than the legacy getUpdates cache. Probe the
// alternate readers to find which one returns the real list (then that becomes the production read path).
static void logUpdateList(NSString *tag, id updates) {
    NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
    egglog(@"  [%@] -> %lu items (raw class %@)", tag, (unsigned long)list.count,
           updates ? NSStringFromClass([updates class]) : @"nil");
    for (id u in list) {
        long long adam = 0;
        if ([u respondsToSelector:sel_registerName("itemIdentifier")])
            adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
        else if ([u respondsToSelector:sel_registerName("adamID")])
            adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
        egglog(@"  [%@]   item adamID=%lld class=%@", tag, adam, NSStringFromClass([u class]));
    }
}

// Synchronous block-reader: fire an async ...WithCompletionBlock: selector and BLOCK (semaphore) until its
// completion fires on the singleton's callout queue, so we actually capture + log the reply. The prior
// runloop-spin approach missed these because the completions run on _calloutQueue, not our thread. We call
// this from a background thread (never the main/callout queue) so waiting can't deadlock the completion.
// 12s timeout per call; logs TIMEOUT if the reply never lands.
static void readerSync(id svc, const char *selName) {
    SEL s = sel_registerName(selName);
    if (![svc respondsToSelector:s]) { egglog(@"  %s: not responded", selName); return; }
    NSString *tag = [NSString stringWithUTF8String:selName];
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    void (^cb)(id) = ^(id updates) { @autoreleasepool { logUpdateList(tag, updates); dispatch_semaphore_signal(sem); } };
    @try {
        ((void(*)(id, SEL, id))objc_msgSend)(svc, s, cb);
        if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(12 * NSEC_PER_SEC))) != 0)
            egglog(@"  %s: TIMEOUT (no reply in 12s)", selName);
    } @catch (NSException *e) { egglog(@"  %s threw %@", selName, e.reason); }
}

// Run the modern-updates probe on a BACKGROUND queue so the synchronous semaphore waits don't block the
// callout queue the completions use. Returns immediately; results land in the log as replies arrive.
__attribute__((unused)) static void probeModernUpdates(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class ASDU = NSClassFromString(@"ASDUpdatesService");
            if (!ASDU) { egglog(@"probeModern: no ASDUpdatesService"); return; }
            id svc = ((id(*)(id, SEL))objc_msgSend)(ASDU, sel_registerName("defaultService"));
            if (!svc) { egglog(@"probeModern: nil defaultService"); return; }
            egglog(@"probeModern: hasEntitlement=%d (this process)",
                   ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("hasEntitlement")));

            // shouldUseModernUpdates? (sync-captured)
            SEL modSel = sel_registerName("shouldUseModernUpdatesWithCompletionBlock:");
            if ([svc respondsToSelector:modSel]) {
                dispatch_semaphore_t sm = dispatch_semaphore_create(0);
                void (^cb)(id) = ^(id v) { egglog(@"  shouldUseModernUpdates -> %@", v); dispatch_semaphore_signal(sm); };
                @try { ((void(*)(id, SEL, id))objc_msgSend)(svc, modSel, cb);
                       dispatch_semaphore_wait(sm, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(8*NSEC_PER_SEC))); }
                @catch (NSException *e) { egglog(@"  shouldUseModernUpdates threw %@", e.reason); }
            }

            // Refresh (background variant the UI likely uses) then read every list source, each sync-captured.
            SEL bgReload = sel_registerName("reloadFromServerInBackgroundWithCompletionBlock:");
            if ([svc respondsToSelector:bgReload]) {
                egglog(@"  reloadFromServerInBackground ...");
                dispatch_semaphore_t sr = dispatch_semaphore_create(0);
                void (^rcb)(id) = ^(id arg) {
                    egglog(@"  bgReload cb arg=%@ (class %@)", arg, arg ? NSStringFromClass([arg class]) : @"nil");
                    dispatch_semaphore_signal(sr);
                };
                @try { ((void(*)(id, SEL, id))objc_msgSend)(svc, bgReload, rcb);
                       if (dispatch_semaphore_wait(sr, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(12*NSEC_PER_SEC))) != 0)
                           egglog(@"  bgReload: TIMEOUT"); }
                @catch (NSException *e) { egglog(@"  bgReload threw %@", e.reason); }
            }
            readerSync(svc, "getUpdatesWithCompletionBlock:");
            readerSync(svc, "getUpdatesIncludingMetricsWithCompletionBlock:");
            readerSync(svc, "getManagedUpdatesWithCompletionBlock:");
            egglog(@"probeModern: done");
        }
    });
}

// Dump the remote XPC protocol the broker's updates-service distant object accepts. The distant object
// holds an _remoteInterface (NSXPCInterface) whose -protocol lists the EXACT selectors appstored vends -
// the real method names (we were guessing the local-wrapper names, which the proxy rejects). Read-only.
static void dumpProtocolMethods(Protocol *p, NSString *label) {
    if (!p) { egglog(@"  %@ protocol = nil", label); return; }
    egglog(@"  %@ protocol = %s", label, protocol_getName(p));
    for (int req = 0; req < 2; req++) {
        for (int inst = 0; inst < 2; inst++) {
            unsigned int n = 0;
            struct objc_method_description *md =
                protocol_copyMethodDescriptionList(p, req == 0, inst == 1, &n);
            for (unsigned int i = 0; i < n; i++)
                egglog(@"    %@%@ %s", req == 0 ? @"@req " : @"@opt ", inst == 1 ? @"-" : @"+",
                       sel_getName(md[i].name));
            if (md) free(md);
        }
    }
}

static void probeRemoteProtocol(void) {
    Class Broker = NSClassFromString(@"ASDServiceBroker");
    if (!Broker) { egglog(@"remoteProto: no ASDServiceBroker"); return; }
    id broker = nil;
    if ([Broker respondsToSelector:sel_registerName("defaultBroker")])
        broker = ((id(*)(id, SEL))objc_msgSend)(Broker, sel_registerName("defaultBroker"));
    if (!broker) { egglog(@"remoteProto: no broker instance"); return; }
    id svc = nil;
    SEL syncSel = sel_registerName("getUpdatesServiceWithError:");
    if ([broker respondsToSelector:syncSel]) {
        NSError *err = nil;
        @try { svc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, syncSel, &err); }
        @catch (NSException *e) { egglog(@"  getUpdatesService threw %@", e.reason); }
    }
    if (!svc) { egglog(@"remoteProto: no distant object"); return; }

    // distant object -> _remoteInterface (NSXPCInterface) -> -protocol -> the real selector set.
    Ivar ri = class_getInstanceVariable(object_getClass(svc), "_remoteInterface");
    id iface = ri ? object_getIvar(svc, ri) : nil;
    egglog(@"  _remoteInterface = %@ (class %@)", iface, iface ? NSStringFromClass([iface class]) : @"nil");
    if (iface && [iface respondsToSelector:sel_registerName("protocol")]) {
        Protocol *p = ((Protocol*(*)(id, SEL))objc_msgSend)(iface, sel_registerName("protocol"));
        dumpProtocolMethods(p, @"updates-service remote");
    }
    // Also dump the broker's OWN remote protocol (it is itself a client of appstored's broker endpoint).
    dumpAllMethods(svc, @"distant-object proxy");
}

// THE LEVER: call the REAL ASDUpdatesServiceProtocol selectors (...WithReplyHandler:, confirmed from the
// remote interface dump) directly on the broker's ENTITLED distant object, bypassing the unentitled local
// ASDUpdatesService singleton. If appstored answers over this entitled connection with the egginc pending
// update, the wall is beaten without the local wrapper. Read-only (no updateAll here); install stays gated.
static void probeEntitledRemote(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class Broker = NSClassFromString(@"ASDServiceBroker");
            if (!Broker) { egglog(@"entitledRemote: no broker class"); return; }
            id broker = ((id(*)(id, SEL))objc_msgSend)(Broker, sel_registerName("defaultBroker"));
            if (!broker) { egglog(@"entitledRemote: no broker"); return; }
            NSError *verr = nil;
            id svc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, sel_registerName("getUpdatesServiceWithError:"), &verr);
            if (!svc) { egglog(@"entitledRemote: vend failed err=%@", verr); return; }

            // Wrap with an error handler so XPC-level failures (entitlement reject, interrupt) surface.
            id proxy = svc;
            SEL ehSel = sel_registerName("remoteObjectProxyWithErrorHandler:");
            if ([svc respondsToSelector:ehSel]) {
                void (^eh)(NSError *) = ^(NSError *e) { egglog(@"  [entitled] XPC error: %@", e); };
                @try { proxy = ((id(*)(id, SEL, id))objc_msgSend)(svc, ehSel, eh); }
                @catch (NSException *ex) { egglog(@"  proxyWithErrorHandler threw %@", ex.reason); proxy = svc; }
            }

            // 1) reloadFromServerWithReplyHandler:  (refresh the entitled connection's pending list)
            dispatch_semaphore_t s1 = dispatch_semaphore_create(0);
            void (^reloadReply)(id, id) = ^(id a, id b) {
                egglog(@"  [entitled] reload reply a=%@ b=%@", a, b); dispatch_semaphore_signal(s1);
            };
            egglog(@"  [entitled] reloadFromServerWithReplyHandler: ...");
            @try { ((void(*)(id, SEL, id))objc_msgSend)(proxy, sel_registerName("reloadFromServerWithReplyHandler:"), reloadReply);
                   if (dispatch_semaphore_wait(s1, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] reload TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] reload threw %@", ex.reason); }

            // 2) getUpdatesWithReplyHandler:  (read the pending list over the entitled connection)
            dispatch_semaphore_t s2 = dispatch_semaphore_create(0);
            void (^getReply)(id) = ^(id updates) {
                @autoreleasepool { logUpdateList(@"entitled getUpdates", updates); dispatch_semaphore_signal(s2); }
            };
            egglog(@"  [entitled] getUpdatesWithReplyHandler: ...");
            @try { ((void(*)(id, SEL, id))objc_msgSend)(proxy, sel_registerName("getUpdatesWithReplyHandler:"), getReply);
                   if (dispatch_semaphore_wait(s2, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] getUpdates TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] getUpdates threw %@", ex.reason); }

            // 3) getUpdateMetadataForBundleID:withReplyHandler:  (ask appstored directly about egginc)
            dispatch_semaphore_t s3 = dispatch_semaphore_create(0);
            void (^metaReply)(id) = ^(id meta) {
                egglog(@"  [entitled] egginc metadata = %@ (class %@)", meta, meta ? NSStringFromClass([meta class]) : @"nil");
                dispatch_semaphore_signal(s3);
            };
            egglog(@"  [entitled] getUpdateMetadataForBundleID:com.auxbrain.egginc ...");
            @try { ((void(*)(id, SEL, id, id))objc_msgSend)(proxy, sel_registerName("getUpdateMetadataForBundleID:withReplyHandler:"),
                                                            @"com.auxbrain.egginc", metaReply);
                   if (dispatch_semaphore_wait(s3, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] metadata TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] metadata threw %@", ex.reason); }

            egglog(@"entitledRemote: done");
        }
    });
}

// SSPurchase path probe. appstored's ASDUpdatesService DB never tracks the grafted egginc (0/null in both
// receipt variants), so the update-list pipeline is a dead end. The UI installs via the StoreKit purchase/
// download flow against the catalog adam-id (993492744) instead. This introspects the relevant private
// StoreKit classes so we can construct the buy on this exact build. Pure dump (no purchase fired here);
// the actual SSPurchaseRequest start stays gated behind EGGUPDATE_ARMED.
static void dumpClassFull(const char *clsName) {
    Class c = NSClassFromString([NSString stringWithUTF8String:clsName]);
    if (!c) { egglog(@"  class %s NOT present", clsName); return; }
    egglog(@"  class %s instance methods:", clsName);
    unsigned int n = 0;
    Method *m = class_copyMethodList(c, &n);
    for (unsigned int i = 0; i < n; i++) egglog(@"    -[%s %s]", clsName, sel_getName(method_getName(m[i])));
    free(m);
    unsigned int cn = 0;
    Method *cm = class_copyMethodList(object_getClass(c), &cn);
    for (unsigned int i = 0; i < cn; i++) egglog(@"    +[%s %s]", clsName, sel_getName(method_getName(cm[i])));
    free(cm);
}

static void probeSSPurchase(void) {
    egglog(@"probeSS: enumerating StoreKit purchase classes");
    const char *classes[] = {
        "SSPurchase", "SSPurchaseRequest", "SSPurchaseResponse", "SSPurchaseManager",
        "ISStoreClient", "SSDownloadManager", "ASDStoreClient", "SSVPurchase",
    };
    for (unsigned int i = 0; i < sizeof(classes)/sizeof(classes[0]); i++)
        dumpClassFull(classes[i]);
}

// Lock the screen (power-save for a never-auto-lock farm phone). SBSLockDevice() is a SpringBoardServices C
// function; on iOS 16 the framework is in the dyld shared cache (no standalone file), but the symbol resolves
// at runtime in this App Store process. dlsym so a missing/renamed symbol is a logged no-op, never a crash.
// Called right after the purchase is enqueued; storedownloadd installs in the background regardless of lock.
static void lockScreenViaSpringBoardServices(void) {
    void *h = dlopen("/System/Library/PrivateFrameworks/SpringBoardServices.framework/SpringBoardServices", RTLD_LAZY);
    if (!h) { egglog(@"  [lock] dlopen SpringBoardServices failed (%s)", dlerror()); return; }
    void (*lockFn)(void) = (void(*)(void))dlsym(h, "SBSLockDevice");
    if (lockFn) { egglog(@"  [lock] SBSLockDevice()"); lockFn(); }
    else        { egglog(@"  [lock] SBSLockDevice symbol not found (%s)", dlerror()); }
}

// THE INSTALL PATH (confirmed API from the SSPurchase dump): build an SSPurchase for the catalog adam-id as
// a standard redownload (STDRDL = free re-acquire of an owned app = the update), wrap in an SSPurchaseRequest,
// and start it. storedownloadd downloads + installs the latest store version (1.36). This is what the App
// Store UI's Update button does. Gated behind EGGUPDATE_ARMED because it actually mutates the device.
static void firePurchaseUpdate(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class SSPurchase = NSClassFromString(@"SSPurchase");
            Class SSPurchaseRequest = NSClassFromString(@"SSPurchaseRequest");
            if (!SSPurchase || !SSPurchaseRequest) { egglog(@"  purchase classes missing"); return; }

            // buyParameters: salableAdamId + standard-redownload pricing. productType C = iOS app.
            NSString *bp = [NSString stringWithFormat:
                @"productType=C&salableAdamId=%lld&pricingParameters=STDRDL&pg=default&price=0&hasBeenAuthedForBuy=true",
                kEggIncAdamId];
            id purchase = ((id(*)(id, SEL, id))objc_msgSend)(SSPurchase, sel_registerName("purchaseWithBuyParameters:"), bp);
            if (!purchase) { egglog(@"  purchaseWithBuyParameters: nil"); return; }
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setCreatesDownloads:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setCreatesInstallJobs:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setUsesLocalRedownloadParametersIfPossible:"), YES);
            egglog(@"  built SSPurchase buyParameters=%@", bp);

            id req = ((id(*)(id, SEL))objc_msgSend)(((id(*)(id, SEL))objc_msgSend)(SSPurchaseRequest, sel_registerName("alloc")), sel_registerName("init"));
            req = ((id(*)(id, SEL, id))objc_msgSend)(req, sel_registerName("initWithPurchases:"), @[purchase]);
            if (!req) { egglog(@"  SSPurchaseRequest nil"); return; }
            ((void(*)(id, SEL, BOOL))objc_msgSend)(req, sel_registerName("setCreatesDownloads:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(req, sel_registerName("setCreatesJobs:"), YES);

            dispatch_semaphore_t sem = dispatch_semaphore_create(0);
            void (^done)(id) = ^(id response) {
                @autoreleasepool {
                    id e = response ? ((id(*)(id, SEL))objc_msgSend)(response, sel_registerName("error")) : nil;
                    egglog(@"  [PURCHASE] completion response=%@ error=%@", response, e);
                    dispatch_semaphore_signal(sem);
                }
            };
            egglog(@"  [PURCHASE] startWithCompletionBlock: (adam %lld STDRDL) ...", kEggIncAdamId);
            @try { ((void(*)(id, SEL, id))objc_msgSend)(req, sel_registerName("startWithCompletionBlock:"), done);
                   if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(25*NSEC_PER_SEC))) != 0)
                       egglog(@"  [PURCHASE] start TIMEOUT (download may still proceed async in storedownloadd)"); }
            @catch (NSException *ex) { egglog(@"  [PURCHASE] start threw %@", ex.reason); }
            egglog(@"  [PURCHASE] done");

            // Power-save: lock the screen now that the purchase is enqueued. storedownloadd installs in the
            // background regardless of lock state, so locking here does not interrupt the update. This is the
            // only place we can lock - the phone has no shell lock tool, but SBSLockDevice() is callable in this
            // App Store process (SpringBoardServices lives in the shared cache). dlopen+dlsym so a missing
            // symbol is a no-op, never a crash. The server-side checker separately killalls the App Store app.
            lockScreenViaSpringBoardServices();
        }
    });
}

static void onTrigger(void) {
    @autoreleasepool {
        egglog(@"trigger fired");
        probeSSPurchase();
        probeRemoteProtocol();
        probeEntitledRemote();
#if EGGUPDATE_ARMED
        egglog(@"EGGUPDATE_ARMED=1: firing SSPurchase update");
        firePurchaseUpdate();
#else
        egglog(@"EGGUPDATE_ARMED=0: SSPurchase install withheld");
        (void)firePurchaseUpdate;
#endif
        fetchUpdatesThen(^(id eggUpdate, NSArray *all) {
            installUpdate(eggUpdate);
        });
        // probeModernUpdates runs sync-captured readers on a background queue (~up to 30s total). Keep this
        // thread (the kqueue trigger thread) parked so the process stays alive for the XPC replies to land.
        // When triggered into a live owner-launched UI process this is safe (not SIGKILLed).
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 52.0, false);
        egglog(@"onTrigger runloop spin done");
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
