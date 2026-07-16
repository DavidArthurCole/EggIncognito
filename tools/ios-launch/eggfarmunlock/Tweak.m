
//

//

//
#import <Foundation/Foundation.h>
#import <objc/runtime.h>
#import <dlfcn.h>
#import <notify.h>
typedef uint32_t IOPMAssertionID;
extern int IOPMAssertionCreateWithName(CFStringRef type, uint32_t level, CFStringRef name, IOPMAssertionID *id);
extern int IOPMAssertionRelease(IOPMAssertionID id);
#define kIOPMAssertionLevelOn 255
static void (*SBSUndimScreen)(void);
static void (*SBSDimScreen)(void);

static IOPMAssertionID gAssertion = 0;

@interface SBLockScreenManager : NSObject
+ (instancetype)sharedInstance;
- (BOOL)isUILocked;
- (BOOL)unlockUIFromSource:(int)source withOptions:(id)options;
- (BOOL)lockUIFromSource:(int)source withOptions:(id)options;
@end

static void holdDisplayAssertion(void) {
    if (gAssertion != 0) return;
    IOPMAssertionCreateWithName(CFSTR("PreventUserIdleDisplaySleep"), kIOPMAssertionLevelOn,
                                CFSTR("egg-farm-keepawake"), &gAssertion);
}

static void releaseDisplayAssertion(void) {
    if (gAssertion == 0) return;
    IOPMAssertionRelease(gAssertion);
    gAssertion = 0;
}

static void onUnlock(CFNotificationCenterRef c, void *o, CFStringRef n, const void *obj, CFDictionaryRef i) {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (SBSUndimScreen) SBSUndimScreen();
        holdDisplayAssertion();
        SBLockScreenManager *m = [objc_getClass("SBLockScreenManager") sharedInstance];
        if (m && [m isUILocked]) {
            [m unlockUIFromSource:0xbeef withOptions:@{
                @"SBUIUnlockOptionsNoPasscodeAnimationKey": @YES,
                @"SBUIUnlockOptionsBypassPasscodeKey": @YES,
            }];
        }
    });
}

static void onRelock(CFNotificationCenterRef c, void *o, CFStringRef n, const void *obj, CFDictionaryRef i) {
    dispatch_async(dispatch_get_main_queue(), ^{
        SBLockScreenManager *m = [objc_getClass("SBLockScreenManager") sharedInstance];
        if (m && ![m isUILocked]) [m lockUIFromSource:0xbeef withOptions:nil];
        releaseDisplayAssertion();
        if (SBSDimScreen) SBSDimScreen();
    });
}

__attribute__((constructor))
static void init(void) {
    void *sbs = dlopen("/System/Library/PrivateFrameworks/SpringBoardServices.framework/SpringBoardServices", RTLD_LAZY);
    if (sbs) {
        SBSUndimScreen = dlsym(sbs, "SBSUndimScreen");
        SBSDimScreen = dlsym(sbs, "SBSDimScreen");
    }
    CFNotificationCenterRef dc = CFNotificationCenterGetDarwinNotifyCenter();
    CFNotificationCenterAddObserver(dc, NULL, onUnlock, CFSTR("me.egg.farm.unlock"), NULL,
                                    CFNotificationSuspensionBehaviorDeliverImmediately);
    CFNotificationCenterAddObserver(dc, NULL, onRelock, CFSTR("me.egg.farm.relock"), NULL,
                                    CFNotificationSuspensionBehaviorDeliverImmediately);
}
