#import <Foundation/Foundation.h>
#include "platform_apple_window_validation_app.h"

int main()
{
    @autoreleasepool {
        __block bool finished = false;
        __block bool delivered = false;
        __block bool mainThread = false;
        ScheduleWindowValidationContracts(^{
            mainThread = NSThread.isMainThread;
            dispatch_async(dispatch_get_main_queue(), ^{ delivered = true; });
            NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:1];
            while (!delivered && deadline.timeIntervalSinceNow > 0)
                [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.005]];
            finished = true;
        });
        NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:2];
        while (!finished && deadline.timeIntervalSinceNow > 0)
            [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.005]];
        if (!finished || !mainThread || !delivered) {
            std::fprintf(stderr, "FAIL: Window validation nested loop finished=%d main=%d delivered=%d\n",
                finished, mainThread, delivered);
            return 1;
        }
        std::puts("Window validation nested loop delivered its deferred main-queue completion");
        return 0;
    }
}
