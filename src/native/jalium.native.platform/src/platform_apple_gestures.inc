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

bool SendAppleGesture(JaliumAppleView* client, NSEvent* input, int kind, int boundary)
{
    JaliumAppleView* __attribute__((objc_precise_lifetime)) view = client;
    NSWindow* __attribute__((objc_precise_lifetime)) window = view.window;
    JaliumPlatformWindow* owner = view.jaliumOwner;
    if (!owner || !IsLiveWindow(owner, window) || !owner->gestures) return false;
    auto state = owner->gestures;
    if (!state->callback) return false;
    auto live = [&] { return IsLiveWindow(owner, window) && owner->gestures == state &&
        view.jaliumOwner == owner && owner->view == view && view.window == window; };
    if (!live() || owner->destroying) return false;
    auto callback = state->callback;
    void* userData = state->userData;
    uint64_t selection = owner->selectionGeneration;
    uint64_t operation = state->revision;
    bool initiallyClaimed = state->claimed || state->kinds != 0;
    if (!live() || state->callback != callback) return false;
    NSWindow* modal = NSApp.modalWindow;
    bool modalAllows = modal == nil;
    int ancestors = 0;
    for (NSWindow* ancestor = window; modal && ancestor && !modalAllows && ancestors++ < 64;) {
        if (ancestor == modal) { modalAllows = true; break; }
        NSWindow* next = ancestor.parentWindow;
        if (next == ancestor) break;
        ancestor = next;
    }
    if (!live() || operation != state->revision || selection != owner->selectionGeneration) return initiallyClaimed;
    if (!modalAllows) { InvalidateAppleGestures(owner); return initiallyClaimed; }
    if (!owner->enabled || !owner->visible || owner->miniaturizing || owner->fullScreenTransition ||
        (owner->style & JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT) ||
        ![view canDispatchPointerForWindow:window owner:owner]) {
        if (live()) InvalidateAppleGestures(owner);
        return false;
    }
    if (!live() || selection != owner->selectionGeneration || state->callback != callback) return false;
    uint64_t geometry = view.geometryRevision;
    NSPoint point = [view convertPoint:input.locationInWindow fromView:nil];
    double scale = kind == JALIUM_APPLE_GESTURE_MAGNIFY ? 1.0 + input.magnification : 1.0;
    // AppKit reports counterclockwise degrees; framework screen-space rotation
    // is clockwise in a flipped client view.
    double rotation = kind == JALIUM_APPLE_GESTURE_ROTATE ? -input.rotation : 0.0;
    double timestamp = input.timestamp;
    NSEventPhase phase = input.phase;
    double dpi = window.backingScaleFactor;
    if (!live() || callback != state->callback || operation != state->revision ||
        selection != owner->selectionGeneration) return initiallyClaimed;
    if (geometry != view.geometryRevision || !std::isfinite(point.x) || !std::isfinite(point.y) ||
        !std::isfinite(dpi) || dpi <= 0 || !std::isfinite(scale) || scale <= 0 ||
        !std::isfinite(rotation) || !std::isfinite(timestamp) || timestamp < 0 || timestamp > 1e12) {
        InvalidateAppleGestures(owner);
        return initiallyClaimed;
    }
    if (state->sequence && state->selection != selection) {
        bool claimed = state->claimed;
        InvalidateAppleGestures(owner);
        if (boundary != 1 && !(phase & NSEventPhaseBegan)) return claimed;
    }
    if (boundary == 1) {
        InvalidateAppleGestures(owner);
        ClearAppleGesture(state);
        state->generic = true;
        state->selection = selection;
        return true;
    }
    if (boundary == 2) {
        if (!state->sequence || state->rejected) { bool suppress = state->suppress; ClearAppleGesture(state); return suppress; }
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
            return suppress;
        }
        ClearAppleGesture(state);
    }
    if (began && (state->kinds & bit)) {
        InvalidateAppleGestures(owner);
        ClearAppleGesture(state);
    }
    if (!state->sequence) {
        if (!kind || cancelled || ended || (!began && phase != NSEventPhaseNone)) return false;
        state->sequence = gAppleGestureSequence.fetch_add(1);
        state->selection = selection;
        state->rejected = false;
        began = true;
    } else if (kind && !(state->kinds & bit) && !cancelled && !ended) {
        began = true;
    }
    if (kind && began) state->kinds |= bit;
    if (kind && !(state->kinds & bit)) return state->claimed;
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
    if (!live() || state->callback != callback || state->userData != userData) return claimed || handled;
    if (state->sequence != event.sequence) {
        // A phase-less packet is a complete operation even if its callback
        // invalidated it. A newer sequence/generic begin remains untouched.
        if (single && !state->sequence && !state->generic) ClearAppleGesture(state);
        return claimed || handled;
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
        return claimed || handled;
    }
    state->claimed |= handled;
    if (!handled && !claimed) {
        state->kinds = 0; state->rejected = true;
        return false;
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
    return true;
}
} // namespace
