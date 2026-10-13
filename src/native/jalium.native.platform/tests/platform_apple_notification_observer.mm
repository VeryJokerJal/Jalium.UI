// Read-only, external-process acceptance companion for HostSmoke's
// --window-notifications-observe[-custom]. Requires existing Accessibility
// permission and a live host PID; never prompts, launches or operates a UI.
#import <Foundation/Foundation.h>
#import <ApplicationServices/ApplicationServices.h>
#include <cerrno>
#include <climits>
#include <cstdlib>
#include <cstdio>
#include <signal.h>
#include <vector>

namespace {
struct Registration {
    AXUIElementRef element;
    NSString* identifier;
    NSString* role;
    std::vector<CFStringRef> notifications;
};
struct ObserverState {
    AXObserverRef observer = nullptr;
    AXUIElementRef application = nullptr;
    std::vector<Registration> registrations;
    NSUInteger sequence = 0;
};

id Read(AXUIElementRef element, CFStringRef attribute) {
    CFTypeRef value = nullptr;
    if (AXUIElementCopyAttributeValue(element, attribute, &value) != kAXErrorSuccess) return nil;
    if (CFGetTypeID(value) == AXValueGetTypeID()) {
        CFRange range{};
        if (AXValueGetType((AXValueRef)value) == kAXValueTypeCFRange &&
            AXValueGetValue((AXValueRef)value, kAXValueTypeCFRange, &range)) {
            CFRelease(value);
            return @{ @"location": @(range.location), @"length": @(range.length) };
        }
    }
    return CFBridgingRelease(value);
}

void Emit(ObserverState& state, NSMutableDictionary* entry) {
    entry[@"sequence"] = @(++state.sequence);
    entry[@"time"] = @(CFAbsoluteTimeGetCurrent());
    NSError* error = nil;
    NSData* data = [NSJSONSerialization dataWithJSONObject:entry options:0 error:&error];
    if (!data) {
        std::fprintf(stderr, "Observer JSON failure: %s\n", error.localizedDescription.UTF8String);
        return;
    }
    std::fwrite(data.bytes, 1, data.length, stdout);
    std::fputc('\n', stdout);
    std::fflush(stdout);
}

size_t Find(const ObserverState& state, AXUIElementRef element) {
    for (size_t i = 0; i < state.registrations.size(); ++i)
        if (CFEqual(state.registrations[i].element, element)) return i;
    return state.registrations.size();
}

void Notification(AXObserverRef, AXUIElementRef element, CFStringRef notification, void* context) {
    @autoreleasepool {
        auto& state = *static_cast<ObserverState*>(context);
        const auto index = Find(state, element);
        NSMutableDictionary* entry = [@{ @"kind": @"notification", @"notification": (__bridge NSString*)notification,
            @"identity": index < state.registrations.size() ? @(index + 1) : @0 } mutableCopy];
        if (index < state.registrations.size()) {
            entry[@"identifier"] = state.registrations[index].identifier;
            entry[@"role"] = state.registrations[index].role;
        }
        // Destruction callbacks deliberately retain only registration metadata;
        // querying a defunct element cannot establish its former live state.
        if (!CFEqual(notification, kAXUIElementDestroyedNotification)) {
            for (NSString* name in @[ @"AXValue", @"AXSelectedText", @"AXSelectedTextRange", @"AXFocused" ]) {
                id value = Read(element, (__bridge CFStringRef)name);
                if ([value isKindOfClass:NSString.class] || [value isKindOfClass:NSNumber.class] ||
                    [value isKindOfClass:NSDictionary.class]) entry[name] = value;
            }
            id focused = Read(state.application, kAXFocusedUIElementAttribute);
            if (focused && CFGetTypeID((__bridge CFTypeRef)focused) == AXUIElementGetTypeID()) {
                id identifier = Read((__bridge AXUIElementRef)focused, kAXIdentifierAttribute);
                if ([identifier isKindOfClass:NSString.class]) entry[@"focusedIdentifier"] = identifier;
            }
        }
        Emit(state, entry);
    }
}

void Discover(ObserverState& state, AXUIElementRef element, unsigned depth,
    std::vector<AXUIElementRef>& visited) {
    if (depth > 32 || visited.size() >= 256) return;
    for (auto previous : visited) if (CFEqual(previous, element)) return;
    visited.push_back((AXUIElementRef)CFRetain(element));
    AXUIElementSetMessagingTimeout(element, 1.0f);
    id role = Read(element, kAXRoleAttribute);
    if (![role isKindOfClass:NSString.class]) return;
    if (Find(state, element) == state.registrations.size()) {
        id identifier = Read(element, kAXIdentifierAttribute);
        if (![identifier isKindOfClass:NSString.class]) identifier = Read(element, kAXTitleAttribute);
        Registration registration{ (AXUIElementRef)CFRetain(element),
            [identifier isKindOfClass:NSString.class] ? identifier : @"", role, {} };
        state.registrations.push_back(registration);
        const size_t index = state.registrations.size() - 1;
        std::vector<CFStringRef> names = { kAXFocusedUIElementChangedNotification,
            kAXLayoutChangedNotification, kAXUIElementDestroyedNotification };
        if ([role isEqualToString:@"AXApplication"]) names.push_back(kAXWindowCreatedNotification);
        if ([role isEqualToString:@"AXWindow"]) {
            names.push_back(kAXWindowMovedNotification); names.push_back(kAXWindowResizedNotification);
            names.push_back(kAXTitleChangedNotification);
        }
        if ([role isEqualToString:@"AXTextArea"] || [role isEqualToString:@"AXTextField"] ||
            [role isEqualToString:@"AXStaticText"] || [role isEqualToString:@"AXButton"])
            names.push_back(kAXValueChangedNotification);
        if ([role isEqualToString:@"AXTextArea"] || [role isEqualToString:@"AXTextField"])
            names.push_back(kAXSelectedTextChangedNotification);
        NSMutableDictionary* results = [NSMutableDictionary new];
        for (CFStringRef name : names) {
            AXError result = AXObserverAddNotification(state.observer, element, name, &state);
            results[(__bridge NSString*)name] = @(result);
            if (result == kAXErrorSuccess) state.registrations[index].notifications.push_back(name);
        }
        Emit(state, [@{ @"kind": @"subscription", @"identity": @(index + 1),
            @"identifier": registration.identifier, @"role": registration.role, @"results": results } mutableCopy]);
    }
    // Application children also contain the entire menu bar. Observe the
    // application's windows, then each window's content; menu nodes are not
    // part of this Window acceptance and can crowd out later content nodes.
    const CFStringRef attribute = [role isEqualToString:@"AXApplication"] ? kAXWindowsAttribute : kAXChildrenAttribute;
    {
        id children = Read(element, attribute);
        if (![children isKindOfClass:NSArray.class]) return;
        for (id child in (NSArray*)children)
            if (CFGetTypeID((__bridge CFTypeRef)child) == AXUIElementGetTypeID())
                Discover(state, (__bridge AXUIElementRef)child, depth + 1, visited);
    }
}
}

int main(int argc, const char* argv[]) {
    @autoreleasepool {
        if (argc != 3) {
            std::fprintf(stderr, "Usage: %s HOST_PID SECONDS (1..600)\n", argv[0]); return 2;
        }
        char* end = nullptr;
        const long pid = std::strtol(argv[1], &end, 10);
        if (!end || *end || pid <= 0 || pid > INT_MAX) return 2;
        const long seconds = std::strtol(argv[2], &end, 10);
        if (!end || *end || seconds < 1 || seconds > 600) return 2;
        if (!AXIsProcessTrusted()) { std::fprintf(stderr, "Existing AX permission is unavailable.\n"); return 3; }
        ObserverState state;
        AXError result = AXObserverCreate((pid_t)pid, Notification, &state.observer);
        if (result != kAXErrorSuccess) { std::fprintf(stderr, "AXObserverCreate: %d\n", result); return 4; }
        state.application = AXUIElementCreateApplication((pid_t)pid);
        CFRunLoopSourceRef source = AXObserverGetRunLoopSource(state.observer);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), source, kCFRunLoopDefaultMode);
        const CFAbsoluteTime deadline = CFAbsoluteTimeGetCurrent() + seconds;
        bool alive = true;
        bool ready = false;
        while (alive && CFAbsoluteTimeGetCurrent() < deadline) {
            @autoreleasepool {
                std::vector<AXUIElementRef> visited;
                Discover(state, state.application, 0, visited);
                for (auto element : visited) CFRelease(element);
                if (!ready && state.registrations.size() > 1) {
                    Emit(state, [@{ @"kind": @"ready", @"pid": @(pid), @"identities": @(state.registrations.size()) } mutableCopy]);
                    ready = true;
                }
                CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.2, false);
                alive = kill((pid_t)pid, 0) == 0 || errno != ESRCH;
            }
        }
        // Deliver queued destruction events after the original host has exited.
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.2, false);
        Emit(state, [@{ @"kind": @"stopped", @"hostAlive": @(alive), @"ready": @(ready),
            @"identities": @(state.registrations.size()) } mutableCopy]);
        CFRunLoopRemoveSource(CFRunLoopGetCurrent(), source, kCFRunLoopDefaultMode);
        for (auto& registration : state.registrations) {
            for (CFStringRef name : registration.notifications)
                AXObserverRemoveNotification(state.observer, registration.element, name);
            CFRelease(registration.element);
        }
        CFRelease(state.application); CFRelease(state.observer);
        return ready ? 0 : 5;
    }
}
