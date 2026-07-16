
//


//

//
//
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

       
        usleep(400 * 1000);

        int (*unlock)(void) = dlsym(sbs, "SBSUnlockDevice");
        if (unlock) { int rc = unlock(); printf("wakeunlock: SBSUnlockDevice rc=%d\n", rc); }
        else fprintf(stderr, "wakeunlock: SBSUnlockDevice missing\n");

        return 0;
    }
}
