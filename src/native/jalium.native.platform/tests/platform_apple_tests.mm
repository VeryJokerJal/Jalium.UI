#include "jalium_platform.h"

#import <AppKit/AppKit.h>
#include <cmath>
#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <vector>
#include <thread>
#include <chrono>

@interface JaliumTestScrollEvent : NSEvent
@property CGFloat testDeltaX;
@property CGFloat testDeltaY;
@property BOOL precise;
@property BOOL inverted;
@property NSEventPhase testPhase;
@property NSEventPhase testMomentumPhase;
@end

@implementation JaliumTestScrollEvent
- (NSEventType)type { return NSEventTypeScrollWheel; }
- (NSPoint)locationInWindow { return NSMakePoint(10, 20); }
- (NSEventModifierFlags)modifierFlags { return 0; }
- (CGFloat)scrollingDeltaX { return self.testDeltaX; }
- (CGFloat)scrollingDeltaY { return self.testDeltaY; }
- (BOOL)hasPreciseScrollingDeltas { return self.precise; }
- (BOOL)isDirectionInvertedFromDevice { return self.inverted; }
- (NSEventPhase)phase { return self.testPhase; }
- (NSEventPhase)momentumPhase { return self.testMomentumPhase; }
@end

static void Check(bool condition, const char* message)
{
    if (!condition) {
        std::fprintf(stderr, "FAIL: %s\n", message);
        std::abort();
    }
}

static void CaptureEvent(const JaliumPlatformEvent* event, void* data)
{
    if (event->type == JALIUM_EVENT_MOUSE_WHEEL) {
        Check((event->wheel.buttonStates & 0x80000000) != 0, "wheel publishes an available button snapshot");
        Check((event->wheel.buttonStates & 0x1f) == (NSEvent.pressedMouseButtons & 0x1f),
            "wheel button snapshot agrees with the current AppKit system state");
        Check((event->wheel.buttonStates & 0x7fffffe0) == 0, "wheel masks unsupported button bits");
    }
    static_cast<std::vector<JaliumPlatformEvent>*>(data)->push_back(*event);
}

static NSUInteger Snap(NSString* text, NSUInteger index, bool forward)
{
    if (index == 0 || index >= text.length) return MIN(index, text.length);
    NSRange cluster = [text rangeOfComposedCharacterSequenceAtIndex:index];
    return index == cluster.location ? index : forward ? NSMaxRange(cluster) : cluster.location;
}

static void TestSystemWordNavigation()
{
    int32_t start = -1, length = -1;
    Check(jalium_platform_text_word_boundary(nullptr, 0, 0, 2) == 0, "empty word navigation");
    Check(jalium_platform_text_word_range(nullptr, 0, 0, &start, &length) == JALIUM_OK &&
        start == 0 && length == 0, "empty word selection");
    Check(jalium_platform_text_word_boundary(nullptr, 1, 0, 2) == -1, "null word text rejected");
    Check(jalium_platform_text_word_boundary(reinterpret_cast<const JaliumUtf16Char*>(u"abc"), 3, 4, 2) == -1 &&
        jalium_platform_text_word_boundary(reinterpret_cast<const JaliumUtf16Char*>(u"abc"), 3, -1, 2) == -1 &&
        jalium_platform_text_word_boundary(reinterpret_cast<const JaliumUtf16Char*>(u"abc"), 3, 0, 4) == -1,
        "invalid word offset and direction rejected");
    Check(jalium_platform_text_word_range(reinterpret_cast<const JaliumUtf16Char*>(u"abc"), 3, 0, nullptr, &length) ==
        JALIUM_ERROR_INVALID_ARGUMENT, "missing word output rejected");
    const unichar embedded[] = {'a', 0, 'b', ' ', 0xd83d, 0xde00};
    NSArray<NSString*>* samples = @[@"hello world", @"中文输入窗口行为验证", @"日本語の編集動作",
        @"ภาษาไทยยินดีต้อนรับ", @"don't stop foo_bar 12.34", @"שלום עולם مرحبا بالعالم",
        @"left אבג right مرحبا end", @"e\u0301cole 👨‍👩‍👧‍👦 🇨🇳 👍🏽 fin", @"first\r\nsecond\u2028third",
        @"   ...!!!  ", [[NSString alloc] initWithCharacters:embedded length:6]];
    size_t checked = 0;
    for (NSString* text in samples) {
        std::vector<JaliumUtf16Char> characters(text.length);
        [text getCharacters:reinterpret_cast<unichar*>(characters.data()) range:NSMakeRange(0, text.length)];
        NSTextView* reference = [[NSTextView alloc] initWithFrame:NSMakeRect(0, 0, 1000000, 400)];
        reference.string = text;
        NSRange selected = NSMakeRange(0, Snap(text, text.length / 2, true));
        for (int direction = 0; direction < 4; ++direction) {
            [reference setSelectedRange:selected];
            switch (direction) {
                case 0: [reference moveWordForward:nil]; break;
                case 1: [reference moveWordBackward:nil]; break;
                case 2: [reference moveWordRight:nil]; break;
                case 3: [reference moveWordLeft:nil]; break;
            }
            Check(jalium_platform_text_word_selection_boundary(characters.data(), characters.size(), selected.location,
                selected.length, direction) == Snap(text, reference.selectedRange.location,
                    reference.selectedRange.location >= selected.location), "word movement chooses the native selection edge");
            ++checked;
        }
        for (NSUInteger index = 0; index <= text.length; ++index) {
            if (Snap(text, index, false) != index) continue;
            for (int direction = 0; direction < 4; ++direction) {
                [reference setSelectedRange:NSMakeRange(index, 0)];
                switch (direction) {
                    case 0: [reference moveWordForward:nil]; break;
                    case 1: [reference moveWordBackward:nil]; break;
                    case 2: [reference moveWordRight:nil]; break;
                    case 3: [reference moveWordLeft:nil]; break;
                }
                int actual = jalium_platform_text_word_boundary(characters.data(), characters.size(), index, direction);
                int expected = static_cast<int>(Snap(text, reference.selectedRange.location,
                    reference.selectedRange.location >= index));
                if (actual != expected) std::fprintf(stderr, "word mismatch: %s index=%lu direction=%d actual=%d expected=%d\n",
                    text.UTF8String, index, direction, actual, expected);
                Check(actual == expected, "word navigation agrees with NSTextView");
                Check(Snap(text, actual, false) == actual, "word caret never splits a composed cluster");
                ++checked;
            }
            NSRange word = [reference selectionRangeForProposedRange:NSMakeRange(MIN(index, text.length - 1), 0)
                granularity:NSSelectByWord];
            NSUInteger first = Snap(text, word.location, false), end = Snap(text, NSMaxRange(word), true);
            Check(jalium_platform_text_word_range(characters.data(), characters.size(), index, &start, &length) == JALIUM_OK &&
                start == first && length == end - first, "double-click selection agrees with AppKit mouse granularity");
            ++checked;
        }
    }
    std::thread worker([] {
        for (int i = 0; i < 100; ++i) {
            Check(jalium_platform_text_word_boundary(reinterpret_cast<const JaliumUtf16Char*>(u"中文输入"), 4, 0, 0) == 2,
                "thread-owned word context supports non-view logical queries");
            Check(jalium_platform_text_word_boundary(reinterpret_cast<const JaliumUtf16Char*>(u"different word"), 14, 0, 0) == 9,
                "thread cache retires a different document");
        }
    });
    worker.join();
    std::printf("AppKit word navigation/selection: %zu reference checks plus 200 worker queries passed\n", checked);
}

int main()
{
    @autoreleasepool {
        [NSApplication.sharedApplication setActivationPolicy:NSApplicationActivationPolicyProhibited];
        TestSystemWordNavigation();
        static_assert(sizeof(JaliumPlatformEvent) == 72, "Wheel metadata must fit the existing event union");
        static_assert(offsetof(JaliumPlatformEvent, wheel.buttonStates) == 48,
            "Wheel button metadata must remain inside the existing 56-byte union");
        static_assert(NSEventPhaseBegan == 1 && NSEventPhaseStationary == 2 && NSEventPhaseChanged == 4 &&
            NSEventPhaseEnded == 8 && NSEventPhaseCancelled == 16 && NSEventPhaseMayBegin == 32,
            "Managed wheel phase flags must match AppKit");
        Check(jalium_platform_init() == JALIUM_OK, "initialize AppKit platform");
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Scroll tests");
        params.x = params.y = JALIUM_DEFAULT_POS;
        params.width = 400;
        params.height = 300;
        auto* window = jalium_window_create(&params);
        Check(window != nullptr, "create hidden test window");
        NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
        std::vector<JaliumPlatformEvent> events;
        jalium_window_set_event_callback(window, CaptureEvent, &events);

        auto* scroll = [JaliumTestScrollEvent new];
        scroll.precise = YES;
        scroll.testDeltaX = -0.125;
        scroll.testDeltaY = 0.25;
        scroll.testPhase = NSEventPhaseBegan;
        [view scrollWheel:scroll];
        Check(events.size() == 1, "precise wheel produces one event");
        auto event = events.back();
        Check(event.type == JALIUM_EVENT_MOUSE_WHEEL, "wheel event type");
        Check(event.wheel.isPrecise != 0, "precise metadata survives native dispatch");
        Check(event.wheel.phase == 1 && event.wheel.momentumPhase == 0, "gesture began survives native dispatch");
        Check(std::abs(event.wheel.deltaX * 48 - 0.125) < 0.000001, "fractional horizontal points and sign");
        Check(std::abs(event.wheel.deltaY * 48 - 0.25) < 0.000001, "fractional vertical points");
        Check(std::abs(event.wheel.x - 10 * view.window.backingScaleFactor) < 0.001, "Retina pointer coordinate conversion");

        scroll.inverted = YES;
        [view scrollWheel:scroll];
        Check(events.back().wheel.deltaY == event.wheel.deltaY, "natural scrolling is not inverted twice");

        scroll.testDeltaX = scroll.testDeltaY = 0;
        scroll.testPhase = NSEventPhaseEnded;
        [view scrollWheel:scroll];
        Check(events.back().wheel.phase == 8 && events.back().wheel.deltaY == 0,
            "zero-delta release is forwarded to finish elastic scrolling");
        scroll.testPhase = NSEventPhaseNone;
        scroll.testMomentumPhase = NSEventPhaseBegan;
        scroll.testDeltaY = -4;
        [view scrollWheel:scroll];
        Check(events.back().wheel.phase == 0 && events.back().wheel.momentumPhase == 1,
            "momentum phase is separate from the direct gesture");
        scroll.testMomentumPhase = NSEventPhaseEnded;
        scroll.testDeltaY = 0;
        [view scrollWheel:scroll];
        Check(events.back().wheel.momentumPhase == 8, "momentum completion survives native dispatch");

        scroll.precise = NO;
        scroll.testMomentumPhase = NSEventPhaseNone;
        scroll.testDeltaX = -3;
        scroll.testDeltaY = -3;
        [view scrollWheel:scroll];
        Check(events.back().wheel.deltaX == 1 && events.back().wheel.deltaY == -1,
            "conventional wheel lines normalize to notches");
        Check(events.back().wheel.isPrecise == 0, "conventional wheel keeps framework smoothing");

        Check((jalium_platform_prefers_overlay_scrollbars() != 0) ==
            (NSScroller.preferredScrollerStyle == NSScrollerStyleOverlay), "use AppKit's current scroller preference");
        [NSNotificationCenter.defaultCenter postNotificationName:NSPreferredScrollerStyleDidChangeNotification object:nil];
        Check(events.back().type == JALIUM_EVENT_SCROLLBAR_SETTINGS_CHANGED, "publish scroller preference changes");
        Check((jalium_platform_prefers_reduced_motion() != 0) ==
            (NSWorkspace.sharedWorkspace.accessibilityDisplayShouldReduceMotion != NO), "read reduced motion preference");
        [NSWorkspace.sharedWorkspace.notificationCenter
            postNotificationName:NSWorkspaceAccessibilityDisplayOptionsDidChangeNotification object:nil];
        Check(events.back().type == JALIUM_EVENT_SCROLLBAR_SETTINGS_CHANGED, "publish accessibility preference changes");

        struct EditingAction { SEL selector; int32_t key; uint32_t modifiers; };
        const EditingAction editingActions[] = {
            {@selector(copy:), 'C', JALIUM_MOD_META}, {@selector(cut:), 'X', JALIUM_MOD_META},
            {@selector(paste:), 'V', JALIUM_MOD_META}, {@selector(selectAll:), 'A', JALIUM_MOD_META},
            {@selector(undo:), 'Z', JALIUM_MOD_META}, {@selector(redo:), 'Z', JALIUM_MOD_META | JALIUM_MOD_SHIFT}
        };
        for (const auto& action : editingActions) {
            auto before = events.size();
            Check([NSApp sendAction:action.selector to:view from:nil], "AppKit accepts standard responder action");
            Check(events.size() == before + 2, "responder action produces one press and release");
            for (size_t phase = 0; phase < 2; ++phase) {
                const auto& key = events[before + phase];
                Check(key.type == (phase == 0 ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP) &&
                    key.key.keyCode == action.key && key.key.modifiers == action.modifiers,
                    "responder editing action carries Command rather than physical Control");
            }
            jalium_window_set_enabled(window, 0);
            before = events.size();
            [NSApp sendAction:action.selector to:view from:nil];
            Check(events.size() == before, "disabled view refuses responder editing action");
            jalium_window_set_enabled(window, 1);
        }

        jalium_window_destroy(window);
        auto count = events.size();
        [view scrollWheel:scroll];
        Check(events.size() == count, "detached views ignore late wheel events");
        [NSApp sendAction:@selector(selectAll:) to:view from:nil];
        Check(events.size() == count, "detached views ignore late editing actions");
        jalium_platform_shutdown();
        std::puts("macOS scrollbar and responder editing action platform tests passed");
    }
    return 0;
}
