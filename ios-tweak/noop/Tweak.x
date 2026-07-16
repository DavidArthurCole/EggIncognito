
//

#import <Foundation/Foundation.h>

%ctor {
    @autoreleasepool {
        NSString *line = [NSString stringWithFormat:@"%@ eggnoop loaded in pid %d (%@)\n",
                          [NSDate date], getpid(),
                          [[NSProcessInfo processInfo] processName]];
       
        NSString *path = @"/var/mobile/eggnoop.log";
        FILE *f = fopen([path fileSystemRepresentation], "a");
        if (f) {
            fwrite([line UTF8String], 1, strlen([line UTF8String]), f);
            fclose(f);
        }
    }
}
