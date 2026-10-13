#import <Foundation/Foundation.h>
#include "platform_apple_window_validation_app.h"

static bool PumpUntil(BOOL (^done)(void), double seconds)
{
    NSDate* deadline = [NSDate dateWithTimeIntervalSinceNow:seconds];
    while (!done() && deadline.timeIntervalSinceNow > 0)
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.005]];
    return done();
}

static bool NestedMainQueueCompletes()
{
    __block bool delivered = false;
    dispatch_async(dispatch_get_main_queue(), ^{ delivered = true; });
    return PumpUntil(^BOOL { return delivered; }, 1);
}

int main()
{
    @autoreleasepool {
        int passed = 0;
        __block int calls = 0;
        __block bool nested = false, mainThread = false;
        ScheduleWindowValidationContracts(^{
            ++calls;
            mainThread = NSThread.isMainThread;
            nested = NestedMainQueueCompletes();
        });
        bool finished = PumpUntil(^BOOL { return calls != 0; }, 2);
        if (finished && calls == 1 && mainThread && nested) {
            ++passed;
            std::puts("PASS default timer delivers deferred main-queue completion");
        } else std::fprintf(stderr, "FAIL default finished=%d calls=%d main=%d nested=%d\n", finished, calls, mainThread, nested);

        __block bool ready = false;
        __block int delayedCalls = 0, delayedTimeouts = 0;
        __block bool delayedNested = false, ranWhenReady = false;
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 40 * NSEC_PER_MSEC), dispatch_get_main_queue(), ^{ ready = true; });
        ScheduleWindowValidationContracts(^{
            ++delayedCalls;
            ranWhenReady = ready;
            delayedNested = NSThread.isMainThread && NestedMainQueueCompletes();
            // Nested pumping must not reenter the repeating startup timer.
            NSDate* end = [NSDate dateWithTimeIntervalSinceNow:0.04];
            [NSRunLoop.mainRunLoop runUntilDate:end];
        }, ^BOOL { return ready; }, ^{ ++delayedTimeouts; }, 1);
        finished = PumpUntil(^BOOL { return delayedCalls != 0 || delayedTimeouts != 0; }, 2);
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.03]];
        if (finished && delayedCalls == 1 && delayedTimeouts == 0 && ranWhenReady && delayedNested) {
            ++passed;
            std::puts("PASS delayed readiness runs once and retains nested main-queue delivery");
        } else std::fprintf(stderr, "FAIL delayed finished=%d calls=%d timeout=%d ready=%d nested=%d\n", finished, delayedCalls, delayedTimeouts, ranWhenReady, delayedNested);

        __block int unavailableCalls = 0, unavailableTimeouts = 0;
        ScheduleWindowValidationContracts(^{ ++unavailableCalls; }, ^BOOL { return NO; }, ^{ ++unavailableTimeouts; }, 0.03);
        finished = PumpUntil(^BOOL { return unavailableTimeouts != 0; }, 1);
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.04]];
        if (finished && unavailableCalls == 0 && unavailableTimeouts == 1) {
            ++passed;
            std::puts("PASS unavailable readiness expires once without running contracts");
        } else std::fprintf(stderr, "FAIL unavailable finished=%d calls=%d timeout=%d\n", finished, unavailableCalls, unavailableTimeouts);

        __block bool lateReady = false;
        __block int lateCalls = 0, lateTimeouts = 0;
        ScheduleWindowValidationContracts(^{ ++lateCalls; }, ^BOOL { return lateReady; }, ^{ ++lateTimeouts; }, 0.02);
        usleep(45000); // Starve the initial timer beyond its bounded startup budget.
        lateReady = true;
        finished = PumpUntil(^BOOL { return lateTimeouts != 0 || lateCalls != 0; }, 1);
        [NSRunLoop.mainRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.03]];
        if (finished && lateCalls == 0 && lateTimeouts == 1) {
            ++passed;
            std::puts("PASS late readiness cannot start contracts after the deadline");
        } else std::fprintf(stderr, "FAIL late finished=%d calls=%d timeout=%d\n", finished, lateCalls, lateTimeouts);

        std::printf("Window validation readiness and nested-loop checks: %d/4 passed\n", passed);
        return passed == 4 ? 0 : 1;
    }
}
