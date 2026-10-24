// Tablet pointer packets share the existing pointer ABI. The optional callback
// also carries the driver's original mouse mapping for controlled promotion.
struct AppleTabletContact {
    JaliumPlatformEvent point{};
    bool contact = false;
};
struct AppleTabletState {
    JaliumAppleTabletCallback callback = nullptr;
    void* userData = nullptr;
    JaliumPlatformWindow* owner = nullptr;
    __weak NSWindow* window;
    std::unordered_map<NSUInteger, AppleTabletContact> contacts;
    std::unordered_set<NSUInteger> blockedContacts;
};
namespace {
std::atomic<uint64_t> gAppleTabletPointerId{2};
std::unordered_map<NSUInteger, NSPointingDeviceType> gAppleTabletTools;
std::vector<std::weak_ptr<AppleTabletState>> gAppleTabletStates;
NSUInteger gAppleTabletCursorDevice = 0;
bool gAppleTabletCursorKnown = false;
std::unordered_set<NSUInteger> gAppleTabletBlockedContacts;
enum class AppleTabletDisposition { Unhandled, Handled, Retired };

void QueueAppleTabletRetirement(const std::shared_ptr<AppleTabletState>& state,
    const AppleTabletContact& contact, bool proximityExit = false)
{
    if (!state->callback) return;
    auto callback = state->callback;
    void* userData = state->userData;
    JaliumPlatformWindow* owner = state->owner;
    NSWindow* window = state->window;
    JaliumAppleTabletEvent event{};
    event.size = sizeof(event); event.pointer = contact.point;
    event.pointer.type = proximityExit && !contact.contact ? JALIUM_EVENT_POINTER_MOVE : JALIUM_EVENT_POINTER_CANCEL;
    event.pointer.pointer.flags &= JALIUM_POINTER_FLAG_ERASER | JALIUM_POINTER_FLAG_INVERTED;
    event.pointer.pointer.buttons = 0; event.pointer.pointer.pressure = 0;
    // Copy the registration: an asynchronous block must not capture the
    // caller's shared_ptr reference after its stack has unwound.
    auto registration = state;
    // Deliver on a later run-loop turn. A host may itself pump AppKit inside
    // the serial main dispatch queue, which would starve another queue block.
    CFRunLoopPerformBlock(CFRunLoopGetMain(), kCFRunLoopCommonModes, ^{
        if (!window || !IsLiveWindow(owner, window) || owner->destroying ||
            owner->tablets != registration || registration->callback != callback ||
            registration->userData != userData) return;
        callback(&event, userData);
    });
    CFRunLoopWakeUp(CFRunLoopGetMain());
}

void RetireAppleTabletDevice(const std::shared_ptr<AppleTabletState>& state,
    NSUInteger device, bool proximityExit = false)
{
    auto found = state->contacts.find(device);
    state->blockedContacts.insert(device);
    if (found == state->contacts.end()) return;
    AppleTabletContact contact = found->second;
    if (contact.contact) gAppleTabletBlockedContacts.insert(device);
    state->contacts.erase(found); // Detach before any application callback.
    QueueAppleTabletRetirement(state, contact, proximityExit);
}

void InvalidateAppleTablets(JaliumPlatformWindow* owner)
{
    if (!owner) return;
    ++owner->tabletDispatchGeneration;
    auto state = owner->tablets;
    if (!state) return;
    auto contacts = std::move(state->contacts);
    state->contacts.clear();
    for (const auto& [device, contact] : contacts) {
        state->blockedContacts.insert(device);
        if (contact.contact) gAppleTabletBlockedContacts.insert(device);
        QueueAppleTabletRetirement(state, contact);
    }
}

struct AppleTabletDispatchContext {
    __strong JaliumAppleView* view;
    JaliumPlatformWindow* owner;
    __strong NSWindow* window;
    std::shared_ptr<AppleTabletState> state;
    uint64_t dispatch = 0, selection = 0, geometry = 0;
    bool valid = false;
    explicit AppleTabletDispatchContext(JaliumAppleView* client)
        : view(client), owner(view.jaliumOwner), window(nil)
    {
        if (!owner || !IsLiveWindow(owner) || owner->destroying) return;
        dispatch = ++owner->tabletDispatchGeneration;
        window = view.window;
        if (!window || !IsLiveWindow(owner, window) || view.jaliumOwner != owner ||
            owner->view != view || owner->destroying || owner->tabletDispatchGeneration != dispatch) return;
        state = owner->tablets; selection = owner->selectionGeneration;
        geometry = view.geometryRevision; valid = true;
    }
    bool Current() const
    {
        return valid && IsLiveWindow(owner, window) && view.jaliumOwner == owner &&
            owner->view == view && !owner->destroying && owner->tablets == state &&
            owner->tabletDispatchGeneration == dispatch && owner->selectionGeneration == selection &&
            view.geometryRevision == geometry;
    }
    bool Ready() const
    {
        if (!Current()) return false;
        bool ready = [view canDispatchPointerForWindow:window owner:owner];
        if (!ready || !Current()) return false;
        NSWindow* modal = NSApp.modalWindow;
        bool allowed = !modal;
        int depth = 0;
        for (NSWindow* ancestor = window; modal && ancestor && !allowed && depth++ < 64;) {
            if (ancestor == modal) { allowed = true; break; }
            NSWindow* next = ancestor.parentWindow;
            if (next == ancestor) break;
            ancestor = next;
        }
        return allowed && Current() && !owner->fullScreenTransition &&
            !(owner->style & JALIUM_WINDOW_STYLE_INPUT_TRANSPARENT);
    }
};

AppleTabletDisposition SendAppleTablet(const AppleTabletDispatchContext& context,
    NSEvent* input, int32_t mouseType = 0, int32_t mouseButton = 0)
{
    if (!context.Current()) return AppleTabletDisposition::Retired;
    auto state = context.state;
    if (!state || !state->callback) return AppleTabletDisposition::Unhandled;
    NSEventType nativeType = input.type;
    bool nativePoint = nativeType == NSEventTypeTabletPoint;
    bool nativeProximity = nativeType == NSEventTypeTabletProximity;
    if (nativePoint || nativeProximity) mouseType = 0;
    NSEventSubtype subtype = nativePoint || nativeProximity ? NSEventSubtypeMouseEvent : input.subtype;
    bool proximity = nativeProximity || subtype == NSEventSubtypeTabletProximity;
    bool pointEvent = nativePoint || subtype == NSEventSubtypeTabletPoint;
    if (!context.Current()) return AppleTabletDisposition::Retired;
    if (!pointEvent && !proximity) {
        if (mouseType && context.Ready() && context.Current()) gAppleTabletCursorKnown = false;
        return context.Current() ? AppleTabletDisposition::Unhandled : AppleTabletDisposition::Retired;
    }
    NSUInteger device = input.deviceID;
    if (!context.Current()) return AppleTabletDisposition::Retired;
    if (!context.Ready()) {
        if (context.Current()) RetireAppleTabletDevice(state, device);
        return AppleTabletDisposition::Retired;
    }
    if (proximity) {
        bool entering = input.enteringProximity;
        NSPointingDeviceType tool = input.pointingDeviceType;
        if (!context.Current()) return AppleTabletDisposition::Retired;
        if (entering) {
            RetireAppleTabletDevice(state, device);
            state->blockedContacts.erase(device);
            gAppleTabletBlockedContacts.erase(device);
            gAppleTabletTools[device] = tool;
        } else {
            gAppleTabletTools.erase(device);
            // Proximity belongs to the physical transducer, even if AppKit
            // delivers its exit to a different currently focused window.
            auto states = gAppleTabletStates;
            for (const auto& weak : states) if (auto active = weak.lock())
                RetireAppleTabletDevice(active, device, true);
        }
        return tool == NSPointingDeviceTypeCursor || mouseType ? AppleTabletDisposition::Unhandled : AppleTabletDisposition::Handled;
    }
    auto toolIt = gAppleTabletTools.find(device);
    NSPointingDeviceType tool = toolIt == gAppleTabletTools.end() ? NSPointingDeviceTypeUnknown : toolIt->second;
    if (tool == NSPointingDeviceTypeCursor) {
        if (mouseType) gAppleTabletCursorKnown = false;
        return AppleTabletDisposition::Unhandled;
    }
    NSPoint location = input.locationInWindow;
    float pressure = input.pressure, rotation = input.rotation;
    NSPoint tilt = input.tilt;
    NSEventButtonMask buttons = input.buttonMask;
    NSEventModifierFlags modifiers = input.modifierFlags;
    double timestamp = input.timestamp;
    NSInteger nativeButton = 0, clicks = 0;
    NSUInteger mouseButtons = 0;
    BOOL active = NO;
    if (mouseType) {
        nativeButton = input.buttonNumber; clicks = input.clickCount;
        mouseButtons = NSEvent.pressedMouseButtons; active = NSApp.active;
    }
    NSPoint point = [context.view convertPoint:location fromView:nil];
    double scale = context.window.backingScaleFactor;
    if (!context.Current()) return AppleTabletDisposition::Retired;
    if (!context.Ready()) {
        if (context.Current()) RetireAppleTabletDevice(state, device);
        return AppleTabletDisposition::Retired;
    }
    if (!std::isfinite(pressure) || !std::isfinite(rotation) || !std::isfinite(tilt.x) || !std::isfinite(tilt.y) ||
        !std::isfinite(point.x) || !std::isfinite(point.y) || !std::isfinite(scale) || scale <= 0 ||
        !std::isfinite(static_cast<float>(point.x * scale)) || !std::isfinite(static_cast<float>(point.y * scale)) ||
        !std::isfinite(timestamp) || timestamp < 0 || timestamp > 1e12) {
        RetireAppleTabletDevice(state, device);
        return AppleTabletDisposition::Retired;
    }
    bool contact = (buttons & NSEventButtonMaskPenTip) != 0;
    bool freshDown = mouseType == JALIUM_EVENT_MOUSE_DOWN;
    if (freshDown || !contact) {
        state->blockedContacts.erase(device);
        gAppleTabletBlockedContacts.erase(device);
    }
    if (contact && (state->blockedContacts.count(device) || gAppleTabletBlockedContacts.count(device)))
        return AppleTabletDisposition::Retired;
    auto found = state->contacts.find(device);
    if (found == state->contacts.end()) {
        uint64_t id = gAppleTabletPointerId.fetch_add(1);
        if (id > UINT32_MAX) return AppleTabletDisposition::Retired;
        AppleTabletContact value{};
        value.point.pointer.pointerId = static_cast<uint32_t>(id);
        found = state->contacts.emplace(device, value).first;
    }
    auto& session = found->second;
    JaliumAppleTabletEvent event{};
    event.size = sizeof(event);
    event.pointer.window = context.owner;
    event.pointer.type = contact && !session.contact ? JALIUM_EVENT_POINTER_DOWN :
        !contact && session.contact ? JALIUM_EVENT_POINTER_UP : JALIUM_EVENT_POINTER_MOVE;
    auto& pointer = event.pointer.pointer;
    pointer.pointerId = session.point.pointer.pointerId;
    pointer.x = point.x * scale; pointer.y = point.y * scale;
    pointer.pressure = contact ? std::clamp(pressure, 0.0f, 1.0f) : 0;
    pointer.tiltX = std::clamp(tilt.x, -1.0, 1.0) * 90;
    pointer.tiltY = std::clamp(tilt.y, -1.0, 1.0) * 90;
    pointer.twist = std::fmod(std::fmod(rotation, 360.0f) + 360.0f, 360.0f);
    pointer.pointerType = JALIUM_POINTER_PEN;
    pointer.modifiers = ModifiersFromFlags(modifiers);
    pointer.flags = JALIUM_POINTER_FLAG_IN_RANGE | (contact ? JALIUM_POINTER_FLAG_IN_CONTACT : 0);
    pointer.toolType = tool == NSPointingDeviceTypeEraser ? JALIUM_POINTER_TOOL_ERASER :
        tool == NSPointingDeviceTypePen ? JALIUM_POINTER_TOOL_PEN : JALIUM_POINTER_TOOL_UNKNOWN;
    if (tool == NSPointingDeviceTypeEraser) pointer.flags |= JALIUM_POINTER_FLAG_ERASER | JALIUM_POINTER_FLAG_INVERTED;
    if (buttons & NSEventButtonMaskPenTip) pointer.buttons |= JALIUM_POINTER_BUTTON_PRIMARY;
    if (buttons & NSEventButtonMaskPenLowerSide) pointer.buttons |= JALIUM_POINTER_BUTTON_SECONDARY | JALIUM_POINTER_BUTTON_BARREL;
    if (buttons & NSEventButtonMaskPenUpperSide) pointer.buttons |= JALIUM_POINTER_BUTTON_TERTIARY | JALIUM_POINTER_BUTTON_BARREL;
    if (pointer.buttons & JALIUM_POINTER_BUTTON_BARREL) pointer.flags |= JALIUM_POINTER_FLAG_BARREL;
    if (contact && tool == NSPointingDeviceTypeEraser) pointer.buttons |= JALIUM_POINTER_BUTTON_ERASER;
    pointer.timestampMillis = static_cast<int64_t>(timestamp * 1000.0);
    if (mouseType) {
        if (mouseButton < 0) mouseButton = nativeButton >= 2 && nativeButton <= 4 ? static_cast<int32_t>(nativeButton) : -1;
        if (mouseButton >= 0 && mouseButton <= 4) {
            NSInteger stateButton = nativeType == NSEventTypeLeftMouseDown || nativeType == NSEventTypeLeftMouseUp ||
                nativeType == NSEventTypeLeftMouseDragged ? 0 :
                nativeType == NSEventTypeRightMouseDown || nativeType == NSEventTypeRightMouseUp ||
                nativeType == NSEventTypeRightMouseDragged ? 1 : nativeButton;
            if (stateButton >= 0 && stateButton <= 4) {
                NSUInteger bit = NSUInteger(1) << stateButton;
                if (mouseType == JALIUM_EVENT_MOUSE_DOWN || nativeType == NSEventTypeLeftMouseDragged ||
                    nativeType == NSEventTypeRightMouseDragged || nativeType == NSEventTypeOtherMouseDragged) mouseButtons |= bit;
                else if (mouseType == JALIUM_EVENT_MOUSE_UP) mouseButtons &= ~bit;
            }
            event.mouseType = mouseType; event.mouseButton = mouseButton;
            event.mouseClickCount = static_cast<int32_t>(std::clamp<NSInteger>(clicks, 0, INT32_MAX));
            event.mouseButtonStates = 0x80000000u | (static_cast<uint32_t>(mouseButtons) & 0x1f);
            [context.view noteTabletCursorButton:nativeButton type:nativeType active:active];
        }
    }
    if (mouseType) { gAppleTabletCursorDevice = device; gAppleTabletCursorKnown = true; }
    if (gAppleTabletCursorKnown && gAppleTabletCursorDevice == device) pointer.flags |= JALIUM_POINTER_FLAG_PRIMARY;
    session.point = event.pointer; session.contact = contact;
    auto callback = state->callback; void* data = state->userData;
    if (!context.Current()) return AppleTabletDisposition::Retired;
    int32_t handled = callback(&event, data);
    if (!context.Current() || state->callback != callback || state->userData != data)
        return AppleTabletDisposition::Retired;
    if (handled < 0) { RetireAppleTabletDevice(state, device); return AppleTabletDisposition::Retired; }
    return handled ? AppleTabletDisposition::Handled : AppleTabletDisposition::Unhandled;
}
} // namespace
