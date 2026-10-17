// AppKit recognizes trackpad gestures. Keep their incremental factors and
// sequence boundaries; indirect trackpad input is never fabricated as touch.
struct AppleGestureState {
    JaliumAppleGestureCallback callback = nullptr;
    void* userData = nullptr;
    uint64_t sequence = 0, revision = 0, selection = 0;
    uint32_t kinds = 0;
    bool generic = false, claimed = false, rejected = false, suppress = false;
};

namespace {
std::atomic<uint64_t> gAppleGestureSequence{1};

void ClearAppleGesture(const std::shared_ptr<AppleGestureState>& state)
{
    ++state->revision;
    state->sequence = 0;
    state->kinds = 0;
    state->generic = state->claimed = state->rejected = state->suppress = false;
}

void InvalidateAppleGestures(JaliumPlatformWindow* owner)
{
    if (!owner || !owner->gestures) return;
    auto state = owner->gestures;
    uint64_t sequence = state->sequence;
    bool claimed = state->claimed || state->kinds != 0 || state->suppress;
    bool generic = state->generic, rejected = state->rejected;
    ClearAppleGesture(state);
    state->generic = generic; state->suppress = claimed; state->rejected = rejected || claimed;
    if (!sequence || !claimed || !state->callback) return;
    auto callback = state->callback;
    void* userData = state->userData;
    NSWindow* window = owner->window;
    // State-changing C entry points do not acquire a new reentrant callback.
    // Deliver cancellation after their stack unwinds and recheck registration.
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!IsLiveWindow(owner, window) || owner->gestures != state ||
            state->callback != callback || state->userData != userData) return;
        JaliumAppleGestureEvent event{};
        event.size = sizeof(event); event.sequence = sequence;
        event.phase = JALIUM_APPLE_GESTURE_CANCEL; event.scale = 1;
        callback(&event, userData);
    });
}

enum class AppleGestureDisposition { Unhandled, Handled, Retired };

// The scope includes AppKit's super implementation, which can call another
// responder and synchronously close the original window.
struct AppleGestureDispatchContext {
    __strong JaliumAppleView* view;
    JaliumPlatformWindow* owner;
    __strong NSWindow* window;
    std::shared_ptr<AppleGestureState> state;
    uint64_t selection = 0, geometry = 0, dispatch = 0;
    bool valid = false;
    explicit AppleGestureDispatchContext(JaliumAppleView* client)
        : view(client), owner(view.jaliumOwner), window(nil)
    {
        if (!owner || !IsLiveWindow(owner) || owner->destroying) return;
        // Capture input entry before any Cocoa query can synchronously deliver
        // a newer gesture on this same window.
        dispatch = ++owner->gestureDispatchGeneration;
        window = view.window;
        if (window && IsLiveWindow(owner, window) && view.jaliumOwner == owner &&
            owner->view == view && !owner->destroying &&
            owner->gestureDispatchGeneration == dispatch) {
            state = owner->gestures; selection = owner->selectionGeneration;
            geometry = view.geometryRevision; valid = true;
        }
    }
    bool SameOwner() const
    {
        return valid && IsLiveWindow(owner, window) && view.jaliumOwner == owner &&
            owner->view == view && owner->gestures == state && !owner->destroying;
    }
    bool IsCurrent() const
    {
        return SameOwner() && owner->selectionGeneration == selection &&
            owner->gestureDispatchGeneration == dispatch && view.geometryRevision == geometry;
    }
    bool CanForward() const
    {
        if (!IsCurrent()) return false;
        bool ready = [view canDispatchPointerForWindow:window owner:owner];
        // Cocoa readiness getters can reenter. Do not read the owner again
        // until its original identity has been checked after those queries.
        if (!ready || !IsCurrent()) return false;
        NSWindow* modal = NSApp.modalWindow;
        bool allowed = modal == nil;
        int depth = 0;
        for (NSWindow* ancestor = window; modal && ancestor && !allowed && depth++ < 64;) {
            if (ancestor == modal) { allowed = true; break; }
            NSWindow* next = ancestor.parentWindow;
            if (next == ancestor) break;
            ancestor = next;
        }
        return allowed && IsCurrent() && !owner->fullScreenTransition &&
            !(owner->style & JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT);
    }
};

AppleGestureDisposition SendAppleGesture(const AppleGestureDispatchContext& context, NSEvent* input, int kind, int boundary)
{
    if (!context.IsCurrent()) return AppleGestureDisposition::Retired;
    JaliumAppleView* view = context.view;
    NSWindow* window = context.window;
    JaliumPlatformWindow* owner = context.owner;
    if (!owner->gestures) return AppleGestureDisposition::Unhandled;
    auto state = owner->gestures;
    if (!state->callback) return AppleGestureDisposition::Unhandled;
    auto live = [&] { return context.SameOwner() && owner->gestures == state &&
        view.window == window && context.SameOwner(); };
    if (!live() || owner->destroying) return AppleGestureDisposition::Retired;
    auto callback = state->callback;
    void* userData = state->userData;
    uint64_t selection = owner->selectionGeneration;
    uint64_t operation = state->revision;
    if (!live() || state->callback != callback) return AppleGestureDisposition::Retired;
    NSWindow* modal = NSApp.modalWindow;
    bool modalAllows = modal == nil;
    int ancestors = 0;
    for (NSWindow* ancestor = window; modal && ancestor && !modalAllows && ancestors++ < 64;) {
        if (ancestor == modal) { modalAllows = true; break; }
        NSWindow* next = ancestor.parentWindow;
        if (next == ancestor) break;
        ancestor = next;
    }
    if (!live() || !context.IsCurrent() || operation != state->revision || selection != owner->selectionGeneration) return AppleGestureDisposition::Retired;
    if (!modalAllows) { InvalidateAppleGestures(owner); return AppleGestureDisposition::Retired; }
    if (!owner->enabled || !owner->visible || owner->miniaturizing || owner->fullScreenTransition ||
        (owner->style & JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT)) {
        InvalidateAppleGestures(owner);
        return AppleGestureDisposition::Retired;
    }
    bool ready = [view canDispatchPointerForWindow:window owner:owner];
    if (!live() || !context.IsCurrent() || operation != state->revision ||
        selection != owner->selectionGeneration || state->callback != callback)
        return AppleGestureDisposition::Retired;
    if (!ready) {
        InvalidateAppleGestures(owner);
        return AppleGestureDisposition::Retired;
    }
    uint64_t geometry = view.geometryRevision;
    NSPoint point = [view convertPoint:input.locationInWindow fromView:nil];
    double scale = kind == JALIUM_APPLE_GESTURE_MAGNIFY ? 1.0 + input.magnification : 1.0;
    // AppKit reports counterclockwise degrees; framework screen-space rotation
    // is clockwise in a flipped client view.
    double rotation = kind == JALIUM_APPLE_GESTURE_ROTATE ? -input.rotation : 0.0;
    double timestamp = input.timestamp;
    NSEventPhase phase = input.phase;
    double dpi = window.backingScaleFactor;
    if (!live() || !context.IsCurrent() || callback != state->callback || operation != state->revision ||
        selection != owner->selectionGeneration) return AppleGestureDisposition::Retired;
    if (geometry != view.geometryRevision || !std::isfinite(point.x) || !std::isfinite(point.y) ||
        !std::isfinite(dpi) || dpi <= 0 || !std::isfinite(scale) || scale <= 0 ||
        !std::isfinite(rotation) || !std::isfinite(timestamp) || timestamp < 0 || timestamp > 1e12) {
        InvalidateAppleGestures(owner);
        return AppleGestureDisposition::Retired;
    }
    if (state->sequence && state->selection != selection) {
        InvalidateAppleGestures(owner);
        if (boundary != 1 && !(phase & NSEventPhaseBegan)) return AppleGestureDisposition::Retired;
    }
    if (boundary == 1) {
        InvalidateAppleGestures(owner);
        ClearAppleGesture(state);
        state->generic = true;
        state->selection = selection;
        return AppleGestureDisposition::Handled;
    }
    if (boundary == 2) {
        if (!state->sequence || state->rejected) { bool suppress = state->suppress; ClearAppleGesture(state); return suppress ? AppleGestureDisposition::Handled : AppleGestureDisposition::Unhandled; }
        kind = 0; phase = NSEventPhaseEnded;
    }
    const uint32_t bit = kind > 0 ? (1u << (kind - 1)) : 0;
    bool began = (phase & NSEventPhaseBegan) != 0;
    bool ended = (phase & NSEventPhaseEnded) != 0;
    bool cancelled = (phase & NSEventPhaseCancelled) != 0;
    bool single = phase == NSEventPhaseNone && !state->generic;
    if (state->rejected) {
        bool suppress = state->suppress;
        if (state->generic || !began) {
            if (cancelled || (ended && !state->generic)) ClearAppleGesture(state);
            return suppress ? AppleGestureDisposition::Handled : AppleGestureDisposition::Unhandled;
        }
        ClearAppleGesture(state);
    }
    if (began && (state->kinds & bit)) {
        InvalidateAppleGestures(owner);
        ClearAppleGesture(state);
    }
    if (!state->sequence) {
        if (!kind || cancelled || ended || (!began && phase != NSEventPhaseNone)) return AppleGestureDisposition::Unhandled;
        state->sequence = gAppleGestureSequence.fetch_add(1);
        state->selection = selection;
        state->rejected = false;
        began = true;
    } else if (kind && !(state->kinds & bit) && !cancelled && !ended) {
        began = true;
    }
    if (kind && began) state->kinds |= bit;
    if (kind && !(state->kinds & bit)) return state->claimed ? AppleGestureDisposition::Handled : AppleGestureDisposition::Unhandled;
    JaliumAppleGestureEvent event{};
    event.size = sizeof(event); event.kind = kind; event.sequence = state->sequence;
    event.phase = cancelled ? JALIUM_APPLE_GESTURE_CANCEL : ended ? JALIUM_APPLE_GESTURE_END :
        began ? JALIUM_APPLE_GESTURE_BEGIN : JALIUM_APPLE_GESTURE_UPDATE;
    event.flags = state->generic && boundary != 2 ? 1u : 0u;
    event.x = point.x * dpi; event.y = point.y * dpi;
    event.scale = scale; event.rotation = rotation; event.timestamp = timestamp;
    uint64_t revision = ++state->revision;
    bool claimed = state->claimed;
    bool handled = callback(&event, userData) != 0;
    if (!live() || state->callback != callback || state->userData != userData) return AppleGestureDisposition::Retired;
    if (state->sequence != event.sequence) {
        // A phase-less packet is a complete operation even if its callback
        // invalidated it. A newer sequence/generic begin remains untouched.
        if (single && !state->sequence && !state->generic) ClearAppleGesture(state);
        return AppleGestureDisposition::Retired;
    }
    if (state->revision != revision) {
        // Reentrant updates of this same bounded operation must not suppress
        // its terminal event. Newer sequence identities were excluded above.
        if (single && !state->generic) {
            event.phase = JALIUM_APPLE_GESTURE_END; event.kind = 0;
            event.scale = 1; event.rotation = 0;
            ClearAppleGesture(state);
            callback(&event, userData);
        }
        return AppleGestureDisposition::Retired;
    }
    state->claimed |= handled;
    if (!handled && !claimed) {
        state->kinds = 0; state->rejected = true;
        return AppleGestureDisposition::Unhandled;
    }
    if (kind && ended) state->kinds &= ~bit;
    if (cancelled || boundary == 2 || (ended && !state->generic && !state->kinds)) ClearAppleGesture(state);
    else if (single) {
        // Legacy phase-less events without a generic sequence are a complete
        // operation, not an indefinitely retained manipulation.
        event.phase = JALIUM_APPLE_GESTURE_END; event.kind = 0;
        event.scale = 1; event.rotation = 0;
        ClearAppleGesture(state);
        callback(&event, userData);
    }
    return AppleGestureDisposition::Handled;
}
} // namespace
