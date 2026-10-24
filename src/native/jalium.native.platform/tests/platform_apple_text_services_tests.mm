// Nonvisual service contracts: only this process owns the controlled Window
// properties. These checks do not establish physical desktop/IME acceptance.
#import <AppKit/AppKit.h>
#import <objc/runtime.h>
#include "jalium_platform.h"
#include <cstdio>
#include <cstring>
#include <functional>
#include <stdexcept>
#include <thread>
#include <vector>

static void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static BOOL MockVisible(id, SEL) { return YES; }
static BOOL MockKey(id, SEL) { return YES; }
static void MockOrder(id, SEL, id) {}
struct WindowProperties {
    Class cls;
    SEL selectors[3] = {@selector(isVisible), @selector(isKeyWindow), @selector(orderFront:)};
    IMP previous[3]{};
    WindowProperties(NSWindow* window) : cls(object_getClass(window)) {
        IMP replacements[] = {(IMP)MockVisible, (IMP)MockKey, (IMP)MockOrder};
        for (int i = 0; i < 3; ++i) {
            Method m = class_getInstanceMethod(cls, selectors[i]);
            previous[i] = method_getImplementation(m);
            class_addMethod(cls, selectors[i], previous[i], method_getTypeEncoding(m));
            method_setImplementation(class_getInstanceMethod(cls, selectors[i]), replacements[i]);
        }
    }
    ~WindowProperties() {
        for (int i = 0; i < 3; ++i) method_setImplementation(class_getInstanceMethod(cls, selectors[i]), previous[i]);
    }
};
struct Fixture {
    JaliumPlatformWindow* window = nullptr;
    NSView* view;
    NSWindow* native;
    NSString* text = @"A🙂 e\u0301 Z";
    NSRange selection = NSMakeRange(1, 2);
    uint32_t flags = JALIUM_AX_ENABLED | JALIUM_AX_TEXT | JALIUM_AX_EDITABLE_TEXT |
        JALIUM_AX_FOCUSABLE | JALIUM_AX_FOCUSED;
    uint64_t focus = 2;
    int reads = 0, writes = 0;
    bool rejectWrite = false;
    std::function<void(int32_t)> onQuery;
    Fixture(bool custom) {
        JaliumWindowParams p{}; p.title = reinterpret_cast<const uint16_t*>(u"Private service contract");
        p.x = p.y = 100; p.width = 320; p.height = 200;
        p.style = JALIUM_WINDOW_STYLE_CLOSABLE | (custom ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR);
        window = jalium_window_create(&p); Check(window != nullptr, "create service fixture");
        view = (__bridge NSView*)(void*)jalium_window_get_native_handle(window); native = view.window;
    }
    void Show() {
        jalium_apple_window_show(window, 0);
        Check([native makeFirstResponder:view], "set service responder");
        jalium_apple_window_set_accessibility(window, Query, this);
    }
    void Destroy() { if (window) { jalium_window_destroy(window); window = nullptr; } }
    ~Fixture() { Destroy(); }
    id Request(NSString* send = NSPasteboardTypeString, NSString* receive = NSPasteboardTypeString) {
        return [view validRequestorForSendType:send returnType:receive];
    }
    static int32_t Query(JaliumAccessibilityRequest* r, void* data) {
        auto& f = *static_cast<Fixture*>(data);
        if (f.onQuery) f.onQuery(r->operation);
        switch (r->operation) {
            case JALIUM_AX_INFO:
                r->role = r->nodeId == 1 ? 32 : 4; r->parentId = r->nodeId == 1 ? 0 : 1;
                r->childCount = r->nodeId == 1 ? 1 : 0; r->textCount = (int32_t)f.text.length;
                r->flags = r->nodeId == 1 ? JALIUM_AX_ENABLED : f.flags;
                r->width = 100; r->height = 30; return 1;
            case JALIUM_AX_CHILD: r->resultId = 2; return 1;
            case JALIUM_AX_FOCUS: r->resultId = f.focus; return 1;
            case JALIUM_AX_ATTACHED: return 1;
            case JALIUM_AX_TEXT_SELECTION:
                r->textStart = (int32_t)f.selection.location; r->textLength = (int32_t)f.selection.length; return 1;
            case JALIUM_AX_STRING:
                ++f.reads; r->textCount = (int32_t)f.text.length;
                if (!r->text) return 1;
                if (r->textCapacity < r->textCount) return 0;
                [f.text getCharacters:r->text range:NSMakeRange(0, f.text.length)]; return 1;
            case JALIUM_AX_TEXT_NAVIGATION:
                if (r->index != JALIUM_AX_REPLACE_SELECTION) return 0;
                ++f.writes; if (f.rejectWrite || !(f.flags & JALIUM_AX_EDITABLE_TEXT)) return 0;
                f.text = [f.text stringByReplacingCharactersInRange:f.selection withString:
                    [[NSString alloc] initWithCharacters:r->text length:r->textCount]];
                f.selection = NSMakeRange(f.selection.location + r->textCount, 0); return 1;
            default: return 0;
        }
    }
};
struct PrivateServicePasteboard {
    NSPasteboard* board = NSPasteboard.pasteboardWithUniqueName;
    ~PrivateServicePasteboard() { [board releaseGlobally]; }
};
@interface ReentrantServicePasteboard : NSObject
@property(nonatomic, copy) NSString* value;
@property(nonatomic) std::function<void()> callback;
@end
@implementation ReentrantServicePasteboard
- (NSString*)stringForType:(NSPasteboardType)type { (void)type; if (_callback) _callback(); return _value; }
@end
@interface ReentrantExportPasteboard : NSObject
@property(nonatomic, copy) NSString* stored;
@property(nonatomic) std::function<void()> callback;
@end
@implementation ReentrantExportPasteboard
- (NSInteger)declareTypes:(NSArray*)types owner:(id)owner {
    (void)types; (void)owner; if (_callback) _callback(); return 1;
}
- (BOOL)setString:(NSString*)text forType:(NSString*)type { (void)type; self.stored = text; return YES; }
@end

int main() {
    @autoreleasepool {
        Check(jalium_platform_init() == 0, "initialize service platform");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];
        int passed = 0, total = 0;
        for (bool custom : {false, true}) {
            auto run = [&](const char* name, const std::function<void(Fixture&)>& test) {
                ++total;
                @autoreleasepool {
                    try {
                        Fixture f(custom); WindowProperties properties(f.native); f.Show(); test(f);
                        ++passed; std::printf("PASS %s: %s\n", custom ? "Custom" : "Native", name);
                    } catch (const std::exception& e) {
                        std::fprintf(stderr, "FAIL %s: %s: %s\n", custom ? "Custom" : "Native", name, e.what());
                    }
                }
            };
            run("plain UTF-16 export uses the private board", [](Fixture& f) {
                id request = f.Request(); Check(request != nil, "missing service requestor");
                auto* board = NSPasteboard.pasteboardWithUniqueName;
                Check([request writeSelectionToPasteboard:board types:@[NSPasteboardTypeString]], "export rejected");
                Check([[board stringForType:NSPasteboardTypeString] isEqualToString:@"🙂"], "selection split or wrong text");
                [board releaseGlobally];
            });
            run("AppKit legacy export preserves the requested text type", [](Fixture& f) {
                id request = f.Request(); PrivateServicePasteboard pasteboard;
                Check([request writeSelectionToPasteboard:pasteboard.board types:@[@"NSStringPboardType"]],
                    "legacy AppKit export rejected");
                Check([[pasteboard.board stringForType:@"NSStringPboardType"] isEqualToString:@"🙂"] &&
                    [[pasteboard.board stringForType:NSPasteboardTypeString] isEqualToString:@"🙂"],
                    "legacy export cannot be read through both AppKit aliases");
            });
            run("legacy and modern service directions interoperate", [](Fixture& f) {
                for (NSString* send in @[NSPasteboardTypeString, @"NSStringPboardType"])
                    for (NSString* receive in @[NSPasteboardTypeString, @"NSStringPboardType"])
                        Check(f.Request(send, receive) != nil, "plain-text alias combination rejected");
            });
            run("legacy service return replaces the Unicode selection", [](Fixture& f) {
                id request = f.Request(@"NSStringPboardType", @"NSStringPboardType");
                PrivateServicePasteboard pasteboard;
                [pasteboard.board setString:@"窗口驗證" forType:@"NSStringPboardType"];
                Check([request readSelectionFromPasteboard:pasteboard.board] && f.writes == 1 &&
                    [f.text isEqualToString:@"A窗口驗證 e\u0301 Z"], "legacy return missed managed selection");
            });
            run("legacy read-only text supports export without mutation", [](Fixture& f) {
                f.flags &= ~JALIUM_AX_EDITABLE_TEXT;
                id request = f.Request(@"NSStringPboardType", nil);
                Check(request != nil && f.Request(nil, @"NSStringPboardType") == nil &&
                    f.Request(@"NSStringPboardType", @"NSStringPboardType") == nil, "wrong legacy read-only capability");
                PrivateServicePasteboard pasteboard;
                Check([request writeSelectionToPasteboard:pasteboard.board types:@[@"NSStringPboardType"]],
                    "legacy read-only export rejected");
                [pasteboard.board setString:@"bad" forType:@"NSStringPboardType"];
                Check(![request readSelectionFromPasteboard:pasteboard.board] && f.writes == 0,
                    "legacy read-only service changed text");
            });
            run("legacy services cannot expose password text", [](Fixture& f) {
                f.flags |= JALIUM_AX_PASSWORD;
                Check(f.Request(@"NSStringPboardType", nil) == nil && f.Request(nil, @"NSStringPboardType") == nil &&
                    f.Request(@"NSStringPboardType", @"NSStringPboardType") == nil && f.reads == 0 && f.writes == 0,
                    "legacy alias bypassed password privacy");
            });
            run("legacy insertion service accepts a collapsed selection", [](Fixture& f) {
                f.selection = NSMakeRange(0, 0);
                Check(f.Request(@"NSStringPboardType", nil) == nil, "legacy send advertised an empty selection");
                id request = f.Request(nil, @"NSStringPboardType"); PrivateServicePasteboard pasteboard;
                [pasteboard.board setString:@"前" forType:@"NSStringPboardType"];
                Check([request readSelectionFromPasteboard:pasteboard.board] &&
                    [f.text isEqualToString:@"前A🙂 e\u0301 Z"], "legacy insertion rejected");
            });
            run("legacy text aliases do not advertise rich transfer", [](Fixture& f) {
                Check(f.Request(NSPasteboardTypeRTF, @"NSStringPboardType") == nil &&
                    f.Request(@"NSStringPboardType", NSPasteboardTypeRTF) == nil &&
                    f.Request(@"public.plain-text", @"NSStringPboardType") == nil,
                    "legacy handling advertised an unsupported transfer");
            });
            run("service return replaces the selection", [](Fixture& f) {
                id request = f.Request(); auto* board = NSPasteboard.pasteboardWithUniqueName;
                [board setString:@"窗口验证" forType:NSPasteboardTypeString];
                Check([request readSelectionFromPasteboard:board], "return rejected");
                Check([f.text isEqualToString:@"A窗口验证 e\u0301 Z"] && f.writes == 1, "return missed managed selection");
                [board releaseGlobally];
            });
            run("empty returned text deletes selection", [](Fixture& f) {
                id request = f.Request(); auto* board = NSPasteboard.pasteboardWithUniqueName;
                [board setString:@"" forType:NSPasteboardTypeString];
                Check([request readSelectionFromPasteboard:board] && [f.text isEqualToString:@"A e\u0301 Z"], "empty return did not delete");
                [board releaseGlobally];
            });
            run("read-only allows send but rejects return", [](Fixture& f) {
                f.flags &= ~JALIUM_AX_EDITABLE_TEXT;
                Check(f.Request(NSPasteboardTypeString, nil) != nil && f.Request() == nil, "wrong read-only capabilities");
                id request = f.Request(NSPasteboardTypeString, nil); auto* board = NSPasteboard.pasteboardWithUniqueName;
                [board setString:@"bad" forType:NSPasteboardTypeString];
                Check(![request readSelectionFromPasteboard:board] && f.writes == 0, "read-only accepted mutation");
                [board releaseGlobally];
            });
            run("password does not export or accept services", [](Fixture& f) {
                f.flags |= JALIUM_AX_PASSWORD;
                Check(f.Request() == nil && f.Request(NSPasteboardTypeString, nil) == nil && f.reads == 0, "private editor exposed");
            });
            run("no selection still accepts an insertion service", [](Fixture& f) {
                f.selection = NSMakeRange(0, 0);
                Check(f.Request() == nil && f.Request(nil, NSPasteboardTypeString) != nil, "wrong empty selection capability");
            });
            run("unsupported types and empty request are declined", [](Fixture& f) {
                Check(f.Request(NSPasteboardTypeRTF, NSPasteboardTypeString) == nil &&
                    f.Request(NSPasteboardTypeString, NSPasteboardTypeRTF) == nil && f.Request(nil, nil) == nil,
                    "unsupported service type advertised");
            });
            run("bad provider selection is declined", [](Fixture& f) {
                f.selection = NSMakeRange(99, 1); Check(f.Request() == nil, "invalid provider range advertised");
            });
            run("retained requestor cannot redirect to a new focused peer", [](Fixture& f) {
                id request = f.Request(); f.focus = 3; auto* board = NSPasteboard.pasteboardWithUniqueName;
                [board setString:@"bad" forType:NSPasteboardTypeString];
                Check(![request readSelectionFromPasteboard:board] && f.writes == 0, "old service redirected");
                [board releaseGlobally];
            });
            run("retained requestor respects a later read-only change", [](Fixture& f) {
                id request = f.Request(); f.flags &= ~JALIUM_AX_EDITABLE_TEXT;
                auto* board = NSPasteboard.pasteboardWithUniqueName; [board setString:@"bad" forType:NSPasteboardTypeString];
                Check(![request readSelectionFromPasteboard:board] && f.writes == 0, "late read-only ignored");
                [board releaseGlobally];
            });
            run("pasteboard callback cannot change the recipient", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                board.callback = [&] { f.focus = 3; };
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0, "reentrant board redirected");
            });
            run("provider mutation rejection propagates", [](Fixture& f) {
                id request = f.Request(); f.rejectWrite = true; auto* board = NSPasteboard.pasteboardWithUniqueName;
                [board setString:@"rejected" forType:NSPasteboardTypeString];
                Check(![request readSelectionFromPasteboard:board] && f.writes == 1, "provider rejection hidden");
                [board releaseGlobally];
            });
            run("pasteboard callback cannot replace a different selection", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                NSString* original = f.text;
                board.callback = [&] { f.selection = NSMakeRange(4, 2); };
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0 &&
                    [f.text isEqualToString:original] && NSEqualRanges(f.selection, NSMakeRange(4, 2)),
                    "reentrant board replaced the new selection");
            });
            run("pasteboard callback cannot redirect to a new insertion point", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                NSString* original = f.text;
                board.callback = [&] { f.selection = NSMakeRange(3, 0); };
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0 &&
                    [f.text isEqualToString:original] && NSEqualRanges(f.selection, NSMakeRange(3, 0)),
                    "reentrant board inserted at the new caret");
            });
            run("hidden owner invalidates retained requestor", [](Fixture& f) {
                id request = f.Request(); jalium_window_hide(f.window);
                auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0, "hidden owner served text");
            });
            run("disabled owner invalidates retained requestor", [](Fixture& f) {
                id request = f.Request(); jalium_window_set_enabled(f.window, 0);
                Check(f.Request() == nil, "disabled owner served text"); (void)request;
            });
            run("detached tree invalidates retained requestor", [](Fixture& f) {
                id request = f.Request(); jalium_apple_window_set_accessibility(f.window, nullptr, nullptr);
                auto* board = NSPasteboard.pasteboardWithUniqueName; [board setString:@"bad" forType:NSPasteboardTypeString];
                Check(![request readSelectionFromPasteboard:board], "detached service still writable"); [board releaseGlobally];
            });
            run("foreign-thread calls do not reach the provider", [](Fixture& f) {
                id request = f.Request(); bool rejected = false; int reads = f.reads;
                auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                std::thread worker([&] { @autoreleasepool { rejected = ![request readSelectionFromPasteboard:(NSPasteboard*)board]; } });
                worker.join(); Check(rejected && f.reads == reads, "foreign-thread provider access");
            });
            run("closed owner invalidates retained requestor", [](Fixture& f) {
                id request = f.Request(); f.Destroy();
                auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0,
                    "closed owner accepted service text");
            });
            run("string query cannot close the export owner", [](Fixture& f) {
                id request = f.Request(); bool closed = false;
                f.onQuery = [&](int32_t op) { if (op == JALIUM_AX_STRING && !closed) { closed = true; f.Destroy(); } };
                auto* board = NSPasteboard.pasteboardWithUniqueName;
                Check(![request writeSelectionToPasteboard:board types:@[NSPasteboardTypeString]], "closed string query exported");
                Check(closed, "close hook did not execute"); [board releaseGlobally];
            });
            run("pasteboard read cannot close the recipient", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantServicePasteboard new]; board.value = @"bad";
                board.callback = [&] { f.Destroy(); };
                Check(![request readSelectionFromPasteboard:(NSPasteboard*)board] && f.writes == 0,
                    "retired recipient accepted service text");
            });
            run("export callback cannot change the focused peer", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantExportPasteboard new];
                board.callback = [&] { f.focus = 3; };
                Check(![request writeSelectionToPasteboard:(NSPasteboard*)board types:@[NSPasteboardTypeString]] &&
                    board.stored == nil, "export followed changed focus");
            });
            run("export callback cannot replace the selected source", [](Fixture& f) {
                id request = f.Request(); auto* board = [ReentrantExportPasteboard new];
                board.callback = [&] { f.text = @"A別 e\u0301 Z"; };
                Check(![request writeSelectionToPasteboard:(NSPasteboard*)board types:@[NSPasteboardTypeString]] &&
                    board.stored == nil, "export used a changed selection snapshot");
            });
        }
        jalium_platform_shutdown();
        std::printf("Private text service contracts: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
