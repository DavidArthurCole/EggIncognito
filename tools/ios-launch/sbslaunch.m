
//


//

//
//
#import <Foundation/Foundation.h>
#import <dlfcn.h>

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
           
           
           
           
        }

       
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
