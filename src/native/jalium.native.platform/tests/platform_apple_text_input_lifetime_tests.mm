#import <AppKit/AppKit.h>
#include "jalium_platform.h"
#include <dlfcn.h>
#include <cstdio>
#include <functional>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

static int passed, failed;
static void Require(bool value, const char* message)
{ if (!value) throw std::runtime_error(message); }

@interface TextOwnershipResponder : NSView
@end
@implementation TextOwnershipResponder
- (BOOL)acceptsFirstResponder { return YES; }
@end

struct Fixture {
    JaliumPlatformWindow* window = nullptr;
    NSView* view;
    id<NSTextInputClient> client;
    NSWindow* native;
    int target = 0, requests = 0;
    bool document;
    struct Character { int target; uint32_t codepoint; };
    struct Composition { int target; JaliumEventType type; std::string text; };
    std::vector<Character> characters;
    std::vector<Composition> compositions;
    std::function<void(const JaliumPlatformEvent*)> callback;
    std::function<void(const JaliumPlatformEvent*)> refresh;

    explicit Fixture(uint32_t style, bool hasDocument) : document(hasDocument)
    {
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Jalium native text ownership");
        params.x = 80; params.y = 80; params.width = 420; params.height = 260; params.style = style;
        window = jalium_window_create(&params); Require(window != nullptr, "create fixture");
        view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
        client = (id<NSTextInputClient>)view; native = view.window;
        Require(view && native, "real AppKit text-input client");
        jalium_window_set_event_callback(window, Event, this);
        jalium_apple_window_show(window, 0);
        Publish();
    }
    ~Fixture() { if (window) { jalium_window_set_event_callback(window, nullptr, nullptr); jalium_window_destroy(window); } }
    void Publish()
    {
        Require(jalium_window_update_ime_context(window, 1, document ? "seed" : nullptr, 0, 0,
            20 + static_cast<int>(characters.size()), 20, 2, 28) == 0, "publish native input context");
    }
    void Handover(bool roundTrip = false)
    {
        jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
        ++target; Publish();
        if (roundTrip) {
            jalium_window_update_ime_context(window, 0, nullptr, 0, 0, 0, 0, 0, 0);
            --target; Publish();
        }
    }
    void Insert() { [client insertText:@"ab🙂中" replacementRange:NSMakeRange(NSNotFound, 0)]; }
    void Mark(NSString* text, NSRange replacement = NSMakeRange(NSNotFound, 0))
    { [client setMarkedText:text selectedRange:NSMakeRange(text.length, 0) replacementRange:replacement]; }
    int Count(JaliumEventType type, int destination, const char* text = nullptr) const
    {
        int count = 0;
        for (const auto& item : compositions)
            if (item.type == type && item.target == destination && (!text || item.text == text)) ++count;
        return count;
    }
    static void Event(const JaliumPlatformEvent* event, void* data)
    {
        auto& f = *static_cast<Fixture*>(data);
        if (event->type == JALIUM_EVENT_CHAR_INPUT) f.characters.push_back({ f.target, event->character.codepoint });
        if (event->type == JALIUM_EVENT_IME_TEXT_REQUEST) ++f.requests;
        if (event->type == JALIUM_EVENT_COMPOSITION_START || event->type == JALIUM_EVENT_COMPOSITION_UPDATE ||
            event->type == JALIUM_EVENT_COMPOSITION_END)
            f.compositions.push_back({ f.target, event->type, event->composition.utf8Text ? event->composition.utf8Text : "" });
        if (f.callback) { auto cb = std::move(f.callback); f.callback = {}; cb(event); }
        if (f.refresh) f.refresh(event);
    }
};
static void Run(const char* name, uint32_t style, const std::function<void()>& run)
{
    @autoreleasepool {
        try { run(); ++passed; std::printf("PASS %s: %s\n", style ? "Native" : "Custom", name); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s: %s\n", style ? "Native" : "Custom", name, error.what()); }
    }
}
int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication]; [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory]; [NSApp finishLaunching];
        jalium_platform_init();
        Dl_info library{}; dladdr(reinterpret_cast<void*>(&jalium_window_create), &library);
        std::printf("LOADED_PLATFORM %s\n", library.dli_fname ?: "<unknown>");
        for (uint32_t style : { uint32_t(0), uint32_t(JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_RESIZABLE) }) {
            for (bool roundTrip : { false, true })
                Run(roundTrip ? "private commit retires focus round trip" : "private commit stops at context handover", style, [&] {
                    Fixture f(style, false);
                    f.callback = [&](const JaliumPlatformEvent* e) { if (e->type == JALIUM_EVENT_CHAR_INPUT) f.Handover(roundTrip); };
                    f.Insert();
                    Require(f.characters.size() == 1 && f.characters.front().codepoint == 'a',
                        "remaining private characters reached another input context");
                });
            Run("same private editor refresh preserves Unicode batch", style, [&] {
                Fixture f(style, false);
                f.refresh = [&](const JaliumPlatformEvent* e) { if (e->type == JALIUM_EVENT_CHAR_INPUT) f.Publish(); };
                f.Insert();
                Require(f.characters.size() == 4 && f.characters[2].codepoint == 0x1f642 && f.characters[3].codepoint == 0x4e2d,
                    "same-context caret refresh truncated Unicode input");
            });
            Run("legacy range fallback stops at handover", style, [&] {
                Fixture f(style, true);
                f.refresh = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_CHAR_INPUT && f.characters.size() == 1) f.Handover();
                };
                f.Insert();
                Require(f.requests == 1 && f.characters.size() == 1, "legacy fallback continued after context handover");
            });
            Run("same legacy editor refresh preserves Unicode batch", style, [&] {
                Fixture f(style, true);
                f.refresh = [&](const JaliumPlatformEvent* e) { if (e->type == JALIUM_EVENT_CHAR_INPUT) f.Publish(); };
                f.Insert();
                Require(f.requests == 1 && f.characters.size() == 4 && f.characters[2].codepoint == 0x1f642,
                    "legacy compatibility no longer commits the complete batch");
            });
            Run("unhandled retired range request cannot fall through", style, [&] {
                Fixture f(style, true);
                f.callback = [&](const JaliumPlatformEvent* e) { if (e->type == JALIUM_EVENT_IME_TEXT_REQUEST) f.Handover(); };
                f.Insert();
                Require(f.requests == 1 && f.characters.empty(), "retired range acknowledgement inserted into the new target");
            });
            Run("explicit range rejection preserves no-fallback contract", style, [&] {
                Fixture f(style, true);
                f.callback = [&](const JaliumPlatformEvent* e) { if (e->type == JALIUM_EVENT_IME_TEXT_REQUEST) *e->imeTextRequest.applied = -1; };
                f.Insert();
                Require(f.requests == 1 && f.characters.empty(), "rejected range request fell through");
            });
            for (bool change : { false, true })
                Run(change ? "range geometry discards retired response" : "same-editor geometry refresh remains valid", style, [&] {
                    Fixture f(style, true);
                    f.callback = [&](const JaliumPlatformEvent* e) {
                        if (e->type != JALIUM_EVENT_IME_GEOMETRY_REQUEST) return;
                        if (change) f.Handover(); else f.Publish();
                        auto* result = e->imeGeometryRequest.result;
                        *result = { 1, 0, 1, 0, 10, 20, 30, 40 };
                    };
                    NSRange actual{};
                    NSRect rectangle = [f.client firstRectForCharacterRange:NSMakeRange(0, 1) actualRange:&actual];
                    Require(change ? NSEqualRects(rectangle, NSZeroRect) && actual.location == NSNotFound :
                        !NSEqualRects(rectangle, NSZeroRect) && NSEqualRanges(actual, NSMakeRange(0, 1)),
                        "geometry response did not match its input-context lifetime");
                });
            Run("point geometry discards retired response", style, [&] {
                Fixture f(style, true);
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type != JALIUM_EVENT_IME_GEOMETRY_REQUEST) return;
                    f.Handover(); *e->imeGeometryRequest.result = { 1, 0, 0, 1, 0, 0, 0, 0 };
                };
                NSPoint point = [f.native convertPointToScreen:[f.view convertPoint:NSMakePoint(25, 25) toView:nil]];
                Require([f.client characterIndexForPoint:point] == NSNotFound, "point query returned a retired target index");
            });
            Run("composition start cannot update a replacement target", style, [&] {
                Fixture f(style, false);
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_COMPOSITION_START) { f.Handover(); f.Mark(@"fresh"); }
                };
                f.Mark(@"old");
                Require(f.Count(JALIUM_EVENT_COMPOSITION_UPDATE, 1, "old") == 0 &&
                    f.Count(JALIUM_EVENT_COMPOSITION_UPDATE, 1, "fresh") == 1 && f.client.markedRange.length == 5,
                    "old composition start sent an update into the replacement target");
            });
            Run("empty composition cannot clear replacement preedit", style, [&] {
                Fixture f(style, false); f.Mark(@"old"); f.compositions.clear();
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_COMPOSITION_UPDATE) { f.Handover(); f.Mark(@"fresh"); }
                };
                f.Mark(@"");
                Require(f.client.hasMarkedText && f.client.markedRange.length == 5 &&
                    f.Count(JALIUM_EVENT_COMPOSITION_END, 1) == 0, "old empty update cleared a new target's preedit");
            });
            Run("empty composition respects nested same-editor preedit", style, [&] {
                Fixture f(style, false); f.Mark(@"old"); f.compositions.clear();
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_COMPOSITION_UPDATE) f.Mark(@"fresh");
                };
                f.Mark(@"");
                Require(f.client.hasMarkedText && f.client.markedRange.length == 5 &&
                    f.Count(JALIUM_EVENT_COMPOSITION_END, 0) == 0, "old empty update cleared a newer same-editor preedit");
            });
            Run("composition reanchor stops after commit handover", style, [&] {
                Fixture f(style, true); f.Mark(@"old"); f.compositions.clear();
                f.refresh = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_COMPOSITION_END && f.target == 0) { f.Handover(); f.Mark(@"fresh"); }
                };
                f.Mark(@"later", NSMakeRange(1, 0));
                Require(f.client.markedRange.length == 5 && f.Count(JALIUM_EVENT_COMPOSITION_UPDATE, 1, "later") == 0 &&
                    f.Count(JALIUM_EVENT_COMPOSITION_UPDATE, 1, "fresh") == 1,
                    "reanchor overwrote the replacement target's preedit");
            });
            Run("native responder round trip retires private batch", style, [&] {
                Fixture f(style, false);
                auto* embedded = [[TextOwnershipResponder alloc] initWithFrame:NSMakeRect(0, 0, 40, 30)];
                [f.view addSubview:embedded];
                Require([f.native makeFirstResponder:f.view], "select initial native input view");
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type != JALIUM_EVENT_CHAR_INPUT) return;
                    Require([f.native makeFirstResponder:embedded] && [f.native makeFirstResponder:f.view],
                        "complete native responder round trip");
                };
                f.Insert(); Require(f.characters.size() == 1, "old batch survived a native responder handover");
            });
            Run("destroyed input stops remaining private characters", style, [&] {
                Fixture f(style, false);
                f.callback = [&](const JaliumPlatformEvent* e) {
                    if (e->type == JALIUM_EVENT_CHAR_INPUT) { auto* previous = f.window; f.window = nullptr; jalium_window_destroy(previous); }
                };
                f.Insert(); Require(f.characters.size() == 1, "destroyed input dispatched remaining characters");
            });
        }
        std::printf("macOS text input lifetime scenarios: %d/%d passed\n", passed, passed + failed);
        std::printf("OBSERVATION active=%d foreground=%d process=%d\n", NSApp.active,
            NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier, getpid());
        jalium_platform_shutdown(); return failed ? 1 : 0;
    }
}
