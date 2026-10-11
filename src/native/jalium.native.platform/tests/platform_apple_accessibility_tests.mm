#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <functional>
#include <string>
#include <thread>

static void Check(bool condition, const char* message)
{
    if (!condition) { std::fprintf(stderr, "FAIL: %s\n", message); std::abort(); }
}
struct Fixture {
    JaliumPlatformWindow* window;
    int calls = 0;
    int presses = 0;
    bool closeOnPress = false;
    uint32_t windowFlags = 0;
    int32_t childRole = 0;
    uint32_t childFlags = JALIUM_AX_ENABLED | JALIUM_AX_PRESSABLE;
    NSString* stylePayload = nil;
    bool oversizedStyles = false;
    int childCount = 1;
    int childEnumerations = 0;
    int snapshotReleases = 0;
    std::function<void(JaliumAccessibilityRequest*)> duringQuery;
};
static int32_t Query(JaliumAccessibilityRequest* request, void* context)
{
    auto& fixture = *static_cast<Fixture*>(context);
    fixture.calls++;
    Check(NSThread.isMainThread, "managed callback must stay on AppKit main thread");
    if (request->nodeId < 1 || request->nodeId > (uint64_t)(fixture.childCount + 1)) return 0;
    if (fixture.duringQuery) fixture.duringQuery(request);
    switch (request->operation) {
        case JALIUM_AX_INFO:
            request->role = request->nodeId == 1 ? 32 : fixture.childRole;
            request->parentId = request->nodeId == 1 ? 0 : 1;
            request->childCount = request->nodeId == 1 ? fixture.childCount : 0;
            request->flags = request->nodeId == 1 ? JALIUM_AX_ENABLED : fixture.childFlags;
            if (request->nodeId == 1) request->flags |= fixture.windowFlags;
            request->x = 20; request->y = 30; request->width = 100; request->height = 40;
            request->textCount = 4;
            return 1;
        case JALIUM_AX_BEGIN_CHILDREN:
            fixture.childEnumerations++;
            request->resultId = 1;
            request->childCount = request->nodeId == 1 ? fixture.childCount : 0;
            return 1;
        case JALIUM_AX_READ_CHILDREN:
            if (request->textCapacity < request->childCount) return 0;
            for (int i = 0; i < request->childCount; ++i) {
                const uint64_t id = (uint64_t)i + 2;
                std::memcpy(request->text + i * 4, &id, sizeof(id));
            }
            return 1;
        case JALIUM_AX_RELEASE_CHILDREN: fixture.snapshotReleases++; return 1;
        case JALIUM_AX_CHILD:
            if (request->nodeId != 1 || request->index < 0 || request->index >= fixture.childCount) return 0;
            request->resultId = (uint64_t)request->index + 2; return 1;
        case JALIUM_AX_STRING: {
            constexpr char16_t label[] = u"中文🙂";
            request->textCount = 4;
            if (!request->text) return 1;
            if (request->textCapacity < 4) return 0;
            std::memcpy(request->text, label, 4 * sizeof(uint16_t));
            return 1;
        }
        case JALIUM_AX_ACTION:
            if (request->index != JALIUM_AX_PRESS) return 0;
            fixture.presses++;
            if (fixture.closeOnPress) { jalium_window_destroy(fixture.window); fixture.window = nullptr; }
            return 1;
        case JALIUM_AX_HIT_TEST: request->resultId = 2; return 1;
        case JALIUM_AX_WINDOW_BUTTON:
            if (request->nodeId != 1 || (uint32_t)request->index > JALIUM_AX_ZOOM_BUTTON) return 0;
            request->resultId = 2; return 1;
        case JALIUM_AX_ATTACHED: return 1;
        case JALIUM_AX_TEXT_STYLES:
            if (fixture.oversizedStyles) { request->textCount = 64 * 1024 * 1024 + 1; return 1; }
            if (!fixture.stylePayload || request->textStart != 0 || request->textLength != 4) return 0;
            request->textCount = (int32_t)fixture.stylePayload.length;
            if (!request->text) return 1;
            if (request->textCount > request->textCapacity) return 0;
            [fixture.stylePayload getCharacters:request->text range:NSMakeRange(0, request->textCount)];
            return 1;
        case JALIUM_AX_TEXT_STYLE_RANGE:
            if (request->textStart < 0 || request->textStart > 4) return 0;
            request->textLength = request->textStart == 4 ? 0 : 4; request->textStart = 0;
            return 1;
        default: return 0;
    }
}
static int RunReadLifecycleCases(int onlyCase)
{
    const char* paths[] = { "view hit test", "root hit test", "child hit test", "Window button",
        "view children", "label sizing", "label copying", "style sizing", "style copying", "partial children" };
    const char* mutations[] = { "close", "hide", "detach", "replace", "disable" };
    int completed = 0;
    for (int titlebar = 0; titlebar < 2; ++titlebar) {
        for (int path = 0; path < 10; ++path) {
            for (int mutation = 0; mutation < 5; ++mutation) {
                const int caseId = (titlebar * 10 + path) * 5 + mutation;
                if (onlyCase >= 0 && caseId != onlyCase) continue;
                @autoreleasepool {
                    std::printf("AX read lifecycle %d: %s / %s / %s\n", caseId,
                        titlebar ? "native" : "custom", paths[path], mutations[mutation]);
                    std::fflush(stdout);
                    JaliumWindowParams params{};
                    params.title = reinterpret_cast<const uint16_t*>(u"AX read lifecycle");
                    params.x = params.y = JALIUM_DEFAULT_POS; params.width = 640; params.height = 480;
                    const uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
                        JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
                    params.style = titlebar ? regular : (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
                    Fixture fixture{jalium_window_create(&params)};
                    Check(fixture.window != nullptr, "create AX lifecycle window");
                    fixture.childRole = 4;
                    fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_TEXT | JALIUM_AX_STYLED_TEXT;
                    fixture.childCount = path == 9 ? 2 : 1;
                    fixture.stylePayload = @"{\"version\":1,\"text16\":\"LU6HZT3YQt4=\",\"runs\":[{\"start\":0,\"length\":4,\"family\":\"Arial\",\"size\":21,\"weight\":400,\"italic\":0,\"underline\":0,\"strike\":0,\"alignment\":0,\"direction\":0,\"language\":\"zh-CN\"}]}";
                    NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(fixture.window));
                    NSWindow* nativeWindow = view.window;
                    jalium_apple_window_set_accessibility(fixture.window, Query, &fixture);
                    jalium_apple_window_show(fixture.window, 0);
                    NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
                    NSAccessibilityElement* child = root.accessibilityChildren.firstObject;
                    Check(root && child, "attach actual content before reentrant read");
                    Check([child.accessibilityLabel isEqual:@"中文🙂"], "readable text exists before reentrant read");
                    Check([[[child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] string] isEqual:@"中文🙂"],
                        "valid styled snapshot exists before reentrant read");
                    const NSPoint point = [nativeWindow convertPointToScreen:[view convertPoint:NSMakePoint(25, 35) toView:nil]];
                    Check([view accessibilityHitTest:point] == child, "hit test resolves the original child before mutation");
                    Fixture replacement{fixture.window};
                    replacement.childRole = 0;
                    bool fired = false;
                    fixture.duringQuery = [&](JaliumAccessibilityRequest* request) {
                        const int32_t operation = path <= 2 ? JALIUM_AX_HIT_TEST : path == 3 ? JALIUM_AX_WINDOW_BUTTON
                            : path == 4 ? JALIUM_AX_INFO : path <= 6 ? JALIUM_AX_STRING
                            : path <= 8 ? JALIUM_AX_TEXT_STYLES : JALIUM_AX_READ_CHILDREN;
                        if (fired || request->operation != operation ||
                            ((path == 6 || path == 8) && !request->text) || (path == 9 && !request->text)) return;
                        fired = true;
                        if (mutation == 0) { jalium_window_destroy(fixture.window); fixture.window = nullptr; }
                        else if (mutation == 1) jalium_window_hide(fixture.window);
                        else if (mutation == 4) {
                            fixture.childFlags &= ~JALIUM_AX_ENABLED;
                            jalium_window_set_enabled(fixture.window, 0);
                        } else jalium_apple_window_set_accessibility(fixture.window, mutation == 2 ? nullptr : Query,
                            mutation == 2 ? nullptr : &replacement);
                    };
                    id result = nil;
                    if (path == 0) result = [view accessibilityHitTest:point];
                    else if (path == 1) result = [root accessibilityHitTest:point];
                    else if (path == 2) result = [child accessibilityHitTest:point];
                    else if (path == 3) result = nativeWindow.accessibilityDefaultButton;
                    else if (path == 4 || path == 9) result = path == 4 ? view.accessibilityChildren : root.accessibilityChildren;
                    else if (path <= 6) result = child.accessibilityLabel;
                    else result = [child accessibilityAttributedStringForRange:NSMakeRange(0, 4)];
                    Check(fired, "reentrant mutation executes in the requested provider phase");
                    if (mutation == 4) {
                        const bool readable = path <= 3 ? result == child : path == 4 || path == 9
                            ? [(NSArray*)result count] == fixture.childCount : path <= 6 ? [result isEqual:@"中文🙂"]
                            : [[(NSAttributedString*)result string] isEqual:@"中文🙂"];
                        Check(readable && child.accessibilityElement && !child.accessibilityEnabled
                            && child.accessibilityParent == root, "disabling input preserves the same readable AX tree");
                    } else Check(path == 4 || path == 9 ? [(NSArray*)result count] == 0 : result == nil,
                        "read discards results from a closed, hidden, detached or replaced tree");
                    const int before = fixture.calls;
                    if (mutation != 4) {
                        Check(!child.accessibilityElement && child.accessibilityParent == nil && child.accessibilityLabel == nil,
                            "retained old child cannot read the retired tree");
                        Check(fixture.calls == before, "old child never invokes the retired provider again");
                    }
                    if (mutation == 0) Check([view accessibilityHitTest:point] == nil,
                        "closed retained view has no AppKit fallback hit");
                    if (mutation == 3) {
                        NSAccessibilityElement* newRoot = view.accessibilityChildren.firstObject;
                        NSAccessibilityElement* newChild = newRoot.accessibilityChildren.firstObject;
                        Check(newChild && newChild != child && [view accessibilityHitTest:point] == newChild,
                            "replacement tree has fresh identity and remains queryable");
                        Check(replacement.calls > 0 && fixture.calls == before, "queries use only the replacement provider");
                    }
                    if (fixture.window) jalium_window_destroy(fixture.window);
                    completed++;
                }
            }
        }
    }
    std::printf("macOS AX read lifecycle: %d/%d passed\n", completed, onlyCase >= 0 ? 1 : 100);
    return completed == (onlyCase >= 0 ? 1 : 100) ? 0 : 1;
}

static int RunNotificationLifecycleCases(int onlyCase)
{
    const char* mutations[] = { "close", "hide", "detach", "replace", "disable" };
    int completed = 0;
    for (int titlebar = 0; titlebar < 2; ++titlebar) {
        for (int phase = 0; phase < 2; ++phase) {
            for (int mutation = 0; mutation < 5; ++mutation) {
                const int caseId = (titlebar * 2 + phase) * 5 + mutation;
                if (onlyCase >= 0 && caseId != onlyCase) continue;
                @autoreleasepool {
                    std::printf("AX notification lifecycle %d: %s / query %d / %s\n", caseId,
                        titlebar ? "native" : "custom", phase + 1, mutations[mutation]);
                    std::fflush(stdout);
                    JaliumWindowParams params{};
                    params.title = reinterpret_cast<const uint16_t*>(u"AX notification lifecycle");
                    params.x = params.y = JALIUM_DEFAULT_POS; params.width = 640; params.height = 480;
                    const uint32_t regular = JALIUM_WINDOW_STYLE_TITLEBAR | JALIUM_WINDOW_STYLE_CLOSABLE |
                        JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_MINIMIZABLE | JALIUM_WINDOW_STYLE_MAXIMIZABLE;
                    params.style = titlebar ? regular : (regular & ~JALIUM_WINDOW_STYLE_TITLEBAR) | JALIUM_WINDOW_STYLE_BORDERLESS;
                    Fixture fixture{jalium_window_create(&params)};
                    Check(fixture.window != nullptr, "create notification lifecycle window");
                    fixture.childCount = 2;
                    NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(fixture.window));
                    jalium_apple_window_set_accessibility(fixture.window, Query, &fixture);
                    jalium_apple_window_show(fixture.window, 0);
                    NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
                    NSArray* children = root.accessibilityChildren;
                    NSAccessibilityElement* child = children.firstObject;
                    Check(root && children.count == 2 && [child.accessibilityLabel isEqual:@"中文🙂"],
                        "cache actual peers before notifying layout");
                    Fixture replacement{fixture.window};
                    int attachedQueries = 0;
                    bool fired = false;
                    fixture.duringQuery = [&](JaliumAccessibilityRequest* request) {
                        if (fired || request->operation != JALIUM_AX_ATTACHED || ++attachedQueries != phase + 1) return;
                        fired = true;
                        if (mutation == 0) { jalium_window_destroy(fixture.window); fixture.window = nullptr; }
                        else if (mutation == 1) jalium_window_hide(fixture.window);
                        else if (mutation == 4) {
                            fixture.childFlags &= ~JALIUM_AX_ENABLED;
                            jalium_window_set_enabled(fixture.window, 0);
                        } else jalium_apple_window_set_accessibility(fixture.window, mutation == 2 ? nullptr : Query,
                            mutation == 2 ? nullptr : &replacement);
                    };
                    jalium_apple_window_notify_accessibility(fixture.window, 1, JALIUM_AX_NOTIFY_LAYOUT);
                    Check(fired, "layout notification executes reentrant provider mutation");
                    const int before = fixture.calls;
                    if (mutation == 4) {
                        Check(view.accessibilityChildren.firstObject == root && root.accessibilityChildren.firstObject == child
                            && child.accessibilityElement && !child.accessibilityEnabled && [child.accessibilityLabel isEqual:@"中文🙂"],
                            "disabled layout retains the readable peer identities");
                    } else {
                        Check(!child.accessibilityElement && child.accessibilityParent == nil && child.accessibilityLabel == nil,
                            "notification cannot publish a retired tree");
                        Check(fixture.calls == before, "retired peers do not call the original provider");
                    }
                    if (mutation == 1) {
                        fixture.duringQuery = {};
                        jalium_apple_window_show(fixture.window, 0);
                        Check(view.accessibilityChildren.firstObject == root && [root.accessibilityChildren isEqualToArray:children]
                            && [child.accessibilityLabel isEqual:@"中文🙂"],
                            "hiding during notification preserves all peers when shown again");
                    } else if (mutation == 3) {
                        NSAccessibilityElement* newRoot = view.accessibilityChildren.firstObject;
                        NSAccessibilityElement* newChild = newRoot.accessibilityChildren.firstObject;
                        Check(newRoot && newRoot != root && newChild && newChild != child
                            && [newChild.accessibilityLabel isEqual:@"中文🙂"] && replacement.calls > 0 && fixture.calls == before,
                            "only replacement peers and provider remain accessible");
                    }
                    if (fixture.window) jalium_window_destroy(fixture.window);
                    completed++;
                }
            }
        }
        const int caseId = 20 + titlebar;
        if (onlyCase >= 0 && caseId != onlyCase) continue;
        @autoreleasepool {
            std::printf("AX notification lifecycle %d: %s / detached node\n", caseId, titlebar ? "native" : "custom");
            std::fflush(stdout);
            JaliumWindowParams params{};
            params.title = reinterpret_cast<const uint16_t*>(u"AX detached peer notification");
            params.x = params.y = JALIUM_DEFAULT_POS; params.width = 640; params.height = 480;
            params.style = JALIUM_WINDOW_STYLE_CLOSABLE | (titlebar ? JALIUM_WINDOW_STYLE_TITLEBAR : JALIUM_WINDOW_STYLE_BORDERLESS);
            Fixture fixture{jalium_window_create(&params)};
            Check(fixture.window != nullptr, "create detached peer fixture");
            NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(fixture.window));
            jalium_apple_window_set_accessibility(fixture.window, Query, &fixture);
            jalium_apple_window_show(fixture.window, 0);
            NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
            NSAccessibilityElement* oldChild = root.accessibilityChildren.firstObject;
            Check(root && oldChild, "cache the peer before removing its provider node");
            fixture.childCount = 0;
            jalium_apple_window_notify_accessibility(fixture.window, 1, JALIUM_AX_NOTIFY_LAYOUT);
            const int before = fixture.calls;
            Check(!oldChild.accessibilityElement && oldChild.accessibilityParent == nil && oldChild.accessibilityWindow == nil
                && oldChild.accessibilityLabel == nil && NSEqualRects(oldChild.accessibilityFrame, NSZeroRect),
                "destroyed peer no longer exposes its former Window or content");
            Check(fixture.calls == before, "destroyed peer is disconnected from the provider");
            Check(view.accessibilityChildren.firstObject == root && root.accessibilityChildren.count == 0,
                "removing a child preserves the attached root");
            fixture.childCount = 1;
            jalium_apple_window_notify_accessibility(fixture.window, 1, JALIUM_AX_NOTIFY_LAYOUT);
            NSAccessibilityElement* newChild = root.accessibilityChildren.firstObject;
            const int after = fixture.calls;
            Check(newChild && newChild != oldChild && !oldChild.accessibilityElement && oldChild.accessibilityLabel == nil,
                "a subsequently attached provider node cannot resurrect a destroyed peer");
            Check(fixture.calls == after && [newChild.accessibilityLabel isEqual:@"中文🙂"],
                "newly attached peer alone reaches the provider");
            const int beforeRetiredSelectors = fixture.calls;
            Check([oldChild isAccessibilitySelectorAllowed:@selector(accessibilityNotifiesWhenDestroyed)]
                && ![oldChild isAccessibilitySelectorAllowed:@selector(accessibilityPerformPress)]
                && fixture.calls == beforeRetiredSelectors,
                "retired peer retains lifetime metadata and rejects actions without querying its provider");
            jalium_window_destroy(fixture.window);
            completed++;
        }
    }
    std::printf("macOS AX notification lifecycle: %d/%d passed\n", completed, onlyCase >= 0 ? 1 : 22);
    return completed == (onlyCase >= 0 ? 1 : 22) ? 0 : 1;
}

int main(int argc, const char** argv)
{
    @autoreleasepool {
        static_assert(sizeof(JaliumAccessibilityRequest) == 136, "managed/native accessibility ABI");
        static_assert(JALIUM_AX_TEXT_NAVIGATION == 12 && JALIUM_AX_TEXT_STYLES == 13 && JALIUM_AX_TEXT_STYLE_RANGE == 14,
            "format operations append without renumbering the existing ABI");
        Check(jalium_platform_init() == JALIUM_OK, "initialize platform");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        if (argc > 1) {
            const std::string argument = argv[1];
            const bool notification = argument == "--notification-lifecycle-only" || argument.rfind("--notification-lifecycle-case=", 0) == 0;
            Check(notification || argument == "--read-lifecycle-only" || argument.rfind("--read-lifecycle-case=", 0) == 0,
                "known accessibility regression mode");
            const int selected = argument == "--read-lifecycle-only" || argument == "--notification-lifecycle-only"
                ? -1 : std::stoi(argument.substr(notification ? 30 : 22));
            const int result = notification ? RunNotificationLifecycleCases(selected) : RunReadLifecycleCases(selected);
            jalium_platform_shutdown();
            return result;
        }
        JaliumWindowParams params{};
        params.title = reinterpret_cast<const uint16_t*>(u"Accessibility protocol tests");
        params.x = params.y = JALIUM_DEFAULT_POS; params.width = 640; params.height = 480;
        Fixture fixture{jalium_window_create(&params)};
        Check(fixture.window != nullptr, "create native fixture");
        NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(fixture.window));
        NSWindow* nativeWindow = view.window;
        Check(nativeWindow.accessibilityEnabled && !nativeWindow.accessibilityModal,
            "native Window enabled/modality state works before attaching managed tree");
        jalium_window_set_enabled(fixture.window, 0);
        Check(!nativeWindow.accessibilityEnabled, "native disabled Window is not AX enabled without a managed tree");
        jalium_window_set_enabled(fixture.window, 1);
        Check(nativeWindow.accessibilityEnabled, "native enabled state restores without a managed tree");
        jalium_apple_window_set_accessibility(fixture.window, Query, &fixture);
        Check(view.accessibilityChildren.count == 0, "hidden window exposes no content");
        jalium_apple_window_show(fixture.window, 0);
        NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
        NSAccessibilityElement* child = root.accessibilityChildren.firstObject;
        Check(root != nil && child != nil, "custom content appears under AppKit view");
        Check([root.accessibilityRole isEqual:NSAccessibilityGroupRole], "managed Window peer is a content group");
        Check([child.accessibilityLabel isEqual:@"中文🙂"], "UTF-16 name and surrogate pair survive ABI");
        Check(child.accessibilityParent == root && root.accessibilityParent == view.window, "accessible ancestry reaches native Window");
        Check(root.accessibilityChildren.firstObject == child, "repeated queries retain native AX identity");
        fixture.childCount = 4096;
        const int enumerationsBefore = fixture.childEnumerations;
        const int releasesBefore = fixture.snapshotReleases;
        NSArray* bulkChildren = root.accessibilityChildren;
        Check(bulkChildren.count == 4096 && bulkChildren.firstObject == child,
            "large child snapshots preserve IDs and order");
        Check(fixture.childEnumerations == enumerationsBefore + 1 && fixture.snapshotReleases == releasesBefore + 1,
            "one native children request enumerates once and releases its snapshot");
        fixture.childCount = 1;

        Check([root respondsToSelector:@selector(accessibilityNotifiesWhenDestroyed)]
            && [child respondsToSelector:@selector(accessibilityNotifiesWhenDestroyed)]
            && [root accessibilityNotifiesWhenDestroyed] && [child accessibilityNotifiesWhenDestroyed],
            "custom peers advertise destruction notification support to external AX clients");
        for (NSAccessibilityElement* element in @[root, child]) {
            for (NSString* name in @[@"isAccessibilitySelected", @"isAccessibilityExpanded", @"isAccessibilityDisclosed",
                @"accessibilitySelectedChildren", @"accessibilityRows", @"accessibilitySelectedRows", @"accessibilityDisclosedRows",
                @"accessibilityDisclosureLevel", @"accessibilityMinValue", @"accessibilityMaxValue", @"accessibilityValue"])
                Check(![element isAccessibilitySelectorAllowed:NSSelectorFromString(name)],
                    "ordinary content must not advertise selection, disclosure, range or value properties");
        }
        fixture.childRole = 24;
        fixture.childFlags |= JALIUM_AX_SELECTABLE;
        Check([child isAccessibilitySelectorAllowed:@selector(isAccessibilitySelected)]
            && [child isAccessibilitySelectorAllowed:@selector(setAccessibilitySelected:)]
            && ![child isAccessibilitySelectorAllowed:@selector(isAccessibilityDisclosed)]
            && [child isAccessibilitySelectorAllowed:@selector(accessibilityDisclosureLevel)],
            "leaf row remains selectable without advertising disclosure");
        fixture.childFlags |= JALIUM_AX_EXPANDABLE;
        Check([child isAccessibilitySelectorAllowed:@selector(isAccessibilityExpanded)]
            && [child isAccessibilitySelectorAllowed:@selector(setAccessibilityDisclosed:)],
            "collapsed branch advertises supported disclosure operations");
        fixture.childFlags &= ~JALIUM_AX_ENABLED;
        Check([child isAccessibilitySelectorAllowed:@selector(isAccessibilitySelected)]
            && [child isAccessibilitySelectorAllowed:@selector(isAccessibilityExpanded)]
            && ![child isAccessibilitySelectorAllowed:@selector(setAccessibilitySelected:)]
            && ![child isAccessibilitySelectorAllowed:@selector(setAccessibilityDisclosed:)],
            "disabled node preserves readable capabilities and rejects their setters");
        fixture.childRole = 0; fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_PRESSABLE;
        Check(nativeWindow.accessibilityDefaultButton == child && nativeWindow.accessibilityCancelButton == child,
            "Window action references reuse the actual child AX object");
        Check(nativeWindow.accessibilityCloseButton == child && nativeWindow.accessibilityMinimizeButton == child
            && nativeWindow.accessibilityZoomButton == child,
            "custom caption references reuse the actual child AX object");
        struct CaptionRole { uint32_t flag; NSAccessibilitySubrole subrole; };
        for (const CaptionRole& caption : { CaptionRole{JALIUM_AX_CLOSE, NSAccessibilityCloseButtonSubrole},
            CaptionRole{JALIUM_AX_MINIMIZE, NSAccessibilityMinimizeButtonSubrole},
            CaptionRole{JALIUM_AX_ZOOM, NSAccessibilityZoomButtonSubrole} }) {
            fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_PRESSABLE | caption.flag;
            Check([child.accessibilitySubrole isEqual:caption.subrole], "caption flag supplies its AppKit subrole");
            fixture.childFlags &= ~JALIUM_AX_ENABLED;
            Check([child.accessibilitySubrole isEqual:caption.subrole] && !child.accessibilityEnabled,
                "disabled caption preserves its readable subrole");
        }
        fixture.childRole = 24;
        Check(child.accessibilitySubrole == nil, "caption flags cannot change a non-button role");
        fixture.childRole = 0; fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_PRESSABLE;
        fixture.windowFlags = JALIUM_AX_DIALOG | JALIUM_AX_MODAL;
        Check(nativeWindow.accessibilityModal && [nativeWindow.accessibilitySubrole isEqual:NSAccessibilityDialogSubrole],
            "managed modal dialog flags supplement real NSWindow");
        Check([nativeWindow.accessibilityRoleDescription isEqual:
            NSAccessibilityRoleDescription(NSAccessibilityWindowRole, NSAccessibilityDialogSubrole)],
            "native dialog role description uses AppKit localization");
        fixture.windowFlags = JALIUM_AX_DIALOG;
        Check(!nativeWindow.accessibilityModal && [nativeWindow.accessibilitySubrole isEqual:NSAccessibilityDialogSubrole],
            "modeless dialog semantics do not imply modality");
        fixture.windowFlags = 0;
        Check(!nativeWindow.accessibilityModal && [nativeWindow.accessibilitySubrole isEqual:NSAccessibilityStandardWindowSubrole],
            "removing dialog flags restores native Window subrole");
        jalium_window_set_enabled(fixture.window, 0);
        Check(!nativeWindow.accessibilityEnabled && !nativeWindow.accessibilityModal
            && [nativeWindow.accessibilitySubrole isEqual:NSAccessibilityStandardWindowSubrole],
            "disabled ordinary Window retains its standard-window subrole");
        Check([nativeWindow.accessibilityRoleDescription isEqual:
            NSAccessibilityRoleDescription(NSAccessibilityWindowRole, NSAccessibilityStandardWindowSubrole)],
            "disabled ordinary Window keeps its standard-window role description");
        fixture.windowFlags = JALIUM_AX_DIALOG | JALIUM_AX_MODAL;
        Check(nativeWindow.accessibilityModal && [nativeWindow.accessibilitySubrole isEqual:NSAccessibilityDialogSubrole],
            "disabled outer modal remains a modal dialog when an inner dialog owns input");
        fixture.windowFlags = 0;
        jalium_window_set_enabled(fixture.window, 1);
        fixture.childRole = 20;
        fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_HAS_VALUE;
        Check([child respondsToSelector:@selector(accessibilityValue)]
            && ![child respondsToSelector:@selector(setAccessibilityValue:)]
            && ![child respondsToSelector:@selector(setAccessibilityFocused:)],
            "static text advertises a readable value without value or focus setters");
        fixture.childRole = 4;
        fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_TEXT | JALIUM_AX_FOCUSABLE;
        Check(![child respondsToSelector:@selector(setAccessibilityValue:)]
            && [child respondsToSelector:@selector(setAccessibilitySelectedTextRange:)]
            && [child respondsToSelector:@selector(setAccessibilityFocused:)],
            "read-only editor remains focusable and selectable without a value setter");
        fixture.childFlags |= JALIUM_AX_WRITABLE;
        Check([child respondsToSelector:@selector(setAccessibilityValue:)],
            "same AX object advertises value writes after the editor becomes writable");
        fixture.childFlags &= ~JALIUM_AX_ENABLED;
        Check(![child respondsToSelector:@selector(setAccessibilityValue:)]
            && ![child respondsToSelector:@selector(setAccessibilitySelectedTextRange:)]
            && ![child respondsToSelector:@selector(setAccessibilityFocused:)]
            && [child respondsToSelector:@selector(accessibilityValue)],
            "disabled editor retains readable state without advertising mutations");
        fixture.childFlags = JALIUM_AX_TEXT | JALIUM_AX_STYLED_TEXT;
        auto payload = [](NSArray* runs, NSString* text16) {
            NSData* data = [NSJSONSerialization dataWithJSONObject:@{@"version":@1, @"text16":text16, @"runs":runs} options:0 error:nil];
            return [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        };
        const unichar characters[] = {0x4e2d, 0x6587, 0xd83d, 0xde42};
        NSString* text16 = [[NSData dataWithBytes:characters length:sizeof(characters)] base64EncodedStringWithOptions:0];
        NSDictionary* style = @{@"start":@0, @"length":@4, @"family":@"Arial", @"size":@21, @"weight":@700, @"italic":@2,
            @"foreground":@0xffa01234, @"underline":@1, @"underlineColor":@0xff123456, @"strike":@1,
            @"alignment":@0, @"direction":@0, @"language":@"zh-CN"};
        fixture.stylePayload = payload(@[style], text16);
        NSAttributedString* attributed = [child accessibilityAttributedStringForRange:NSMakeRange(0, 4)];
        NSDictionary* attributes = [attributed attributesAtIndex:0 effectiveRange:nil];
        Check([attributed.string isEqual:@"中文🙂"] && [attributes[NSAccessibilityFontTextAttribute][NSAccessibilityFontSizeKey] isEqual:@21]
            && attributes[NSAccessibilityForegroundColorTextAttribute] && [attributes[NSAccessibilityUnderlineTextAttribute] isEqual:@1],
            "disabled styled text preserves font, color, decoration and Unicode");
        Check([[attributes[NSAccessibilityFontTextAttribute][NSAccessibilityFontNameKey] lowercaseString] containsString:@"bolditalic"],
            "AX font dictionary identifies the selected bold italic face");
        NSData* rtf = [child accessibilityRTFForRange:NSMakeRange(0, 4)];
        NSAttributedString* decoded = [[NSAttributedString alloc] initWithRTF:rtf documentAttributes:nil];
        Check([decoded.string isEqual:@"中文🙂"] && [[[decoded attributesAtIndex:0 effectiveRange:nil] objectForKey:NSFontAttributeName] pointSize] == 21,
            "RTF is decoded by AppKit with the same text and point size");
        Check(NSEqualRanges([child accessibilityStyleRangeForIndex:2], NSMakeRange(0, 4))
            && NSEqualRanges([child accessibilityStyleRangeForIndex:4], NSMakeRange(0, 0)), "style extents include UTF-16 characters and reject EOF as a character");
        for (NSDictionary* changes in @[@{@"start":@1}, @{@"length":@0}, @{@"length":@5}, @{@"size":@(-1)}, @{@"weight":@1001},
            @{@"italic":@3}, @{@"foreground":@4294967296ULL}, @{@"family":@[]}, @{@"language":@1}, @{@"direction":@2}]) {
            NSMutableDictionary* invalid = [style mutableCopy]; [invalid addEntriesFromDictionary:changes];
            fixture.stylePayload = payload(@[invalid], text16);
            Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil
                && [child accessibilityRTFForRange:NSMakeRange(0, 4)] == nil, "malformed style snapshots cannot escape the ABI boundary");
        }
        for (NSString* invalid in @[@"bad-json", @"[]", @"{\"version\":1}"] ) {
            fixture.stylePayload = invalid;
            Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil, "invalid style envelope is rejected");
        }
        fixture.stylePayload = payload(@[style], @"not-base64");
        Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil, "invalid UTF-16 base64 is rejected");
        fixture.stylePayload = payload(@[], text16);
        Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil, "missing styles cannot produce a partially formatted document");
        fixture.stylePayload = payload(@[style], text16); fixture.oversizedStyles = true;
        Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil, "oversized style payload is rejected before allocation");
        fixture.oversizedStyles = false; fixture.childFlags |= JALIUM_AX_PASSWORD;
        Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil && [child accessibilityRTFForRange:NSMakeRange(0, 4)] == nil
            && ![child respondsToSelector:@selector(accessibilityRTFForRange:)], "private text cannot expose style or RTF data even with an inconsistent capability flag");
        fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_TEXT;
        Check(![child respondsToSelector:@selector(accessibilityRTFForRange:)]
            && [[child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] attributesAtIndex:0 effectiveRange:nil].count == 0,
            "legacy plain-text providers retain compatibility without advertising format export");
        fixture.childRole = 0; fixture.childFlags = JALIUM_AX_ENABLED | JALIUM_AX_PRESSABLE;
        NSRect expected = [view.window convertRectToScreen:[view convertRect:NSMakeRect(20, 30, 100, 40) toView:nil]];
        Check(NSEqualRects(child.accessibilityFrame, expected), "content points map to global AppKit screen coordinates");
        Check([child accessibilityPerformPress] && fixture.presses == 1, "press callback reaches provider");
        int before = fixture.calls;
        const NSPoint hitPoint = NSMakePoint(NSMidX(expected), NSMidY(expected));
        std::thread foreign([child, nativeWindow, view, root, hitPoint, platformWindow = fixture.window] { @autoreleasepool {
            Check(!child.accessibilityEnabled, "foreign AX thread safely rejects query");
            Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil && [child accessibilityRTFForRange:NSMakeRange(0, 4)] == nil,
                "foreign format queries never invoke a managed style source");
            Check(!nativeWindow.accessibilityEnabled, "foreign AX thread rejects native enabled-state query");
            Check(view.accessibilityChildren.count == 0 && [view accessibilityHitTest:hitPoint] == nil
                && [root accessibilityHitTest:hitPoint] == nil && [child accessibilityHitTest:hitPoint] == nil,
                "foreign content tree and hit queries never enter AppKit or the managed provider");
            (void)nativeWindow.accessibilityModal;
            (void)nativeWindow.accessibilitySubrole;
            Check(nativeWindow.accessibilityDefaultButton == nil && nativeWindow.accessibilityCancelButton == nil,
                "foreign Window button query does not enter managed callback");
            Check(nativeWindow.accessibilityCloseButton == nil && nativeWindow.accessibilityMinimizeButton == nil
                && nativeWindow.accessibilityZoomButton == nil, "foreign caption queries never enter managed callback");
            for (int notification : { JALIUM_AX_NOTIFY_FOCUS, JALIUM_AX_NOTIFY_VALUE, JALIUM_AX_NOTIFY_SELECTION,
                    JALIUM_AX_NOTIFY_LAYOUT, JALIUM_AX_NOTIFY_TITLE })
                jalium_apple_window_notify_accessibility(platformWindow, 1, notification);
            jalium_apple_window_set_accessibility(platformWindow, nullptr, nullptr);
        }});
        foreign.join();
        Check(fixture.calls == before, "foreign thread never enters managed provider");
        Check(view.accessibilityChildren.firstObject == root && root.accessibilityChildren.firstObject == child,
            "foreign notification and detach requests cannot alter the main-thread tree");
        fixture.closeOnPress = true;
        Check([child accessibilityPerformPress] && fixture.window == nullptr, "provider can destroy its window during an action");
        before = fixture.calls;
        Check(!child.accessibilityElement && ![child accessibilityPerformPress] && child.accessibilityParent == nil,
            "stale AX objects reject queries after destruction");
        Check(fixture.calls == before, "destroyed window never re-enters released managed callback");
        Check([child accessibilityAttributedStringForRange:NSMakeRange(0, 4)] == nil && [child accessibilityRTFForRange:NSMakeRange(0, 4)] == nil,
            "destroyed styled text cannot return cached private data");
        Check(view.accessibilityChildren.count == 0, "detached view has no stale content");
        Check(!nativeWindow.accessibilityEnabled, "destroyed native Window is no longer AX enabled");
        Check(nativeWindow.accessibilityDefaultButton == nil && nativeWindow.accessibilityCancelButton == nil,
            "destroyed native Window does not retain managed button references");
        Check(nativeWindow.accessibilityCloseButton == nil && nativeWindow.accessibilityMinimizeButton == nil
            && nativeWindow.accessibilityZoomButton == nil, "destroyed Window does not retain caption references");
        Check(RunReadLifecycleCases(-1) == 0, "complete public read lifecycle regressions");
        Check(RunNotificationLifecycleCases(-1) == 0, "complete public notification lifecycle regressions");
        jalium_platform_shutdown();
        std::puts("macOS accessibility native protocol tests passed");
    }
    return 0;
}
