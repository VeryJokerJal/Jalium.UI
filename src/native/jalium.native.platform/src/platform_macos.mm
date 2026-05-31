#import "jalium_platform.h"
#import <Cocoa/Cocoa.h>
#import <QuartzCore/QuartzCore.h>
#import <CoreGraphics/CoreGraphics.h>
#import <objc/runtime.h>

#include <atomic>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <string>

static CFRunLoopRef g_mainRunLoop = nullptr;
static std::atomic<bool> g_quitRequested{false};
static std::atomic<int32_t> g_exitCode{0};

static std::mutex g_windowMapMutex;
static std::unordered_map<NSWindow*, JaliumPlatformWindow*> g_windowMap;

struct JaliumPlatformWindow {
    NSWindow* nsWindow = nil;
    JaliumEventCallback callback = nullptr;
    void* userData = nullptr;
    uint32_t style = 0;
    int32_t width = 0;
    int32_t height = 0;
    float dpiScale = 1.0f;
    bool destroyed = false;
};

static int32_t GetModifiers(NSEvent* event)
{
    int32_t mods = JALIUM_MOD_NONE;
    NSEventModifierFlags flags = [event modifierFlags];
    if (flags & NSEventModifierFlagShift) mods |= JALIUM_MOD_SHIFT;
    if (flags & NSEventModifierFlagControl) mods |= JALIUM_MOD_CTRL;
    if (flags & NSEventModifierFlagOption) mods |= JALIUM_MOD_ALT;
    if (flags & NSEventModifierFlagCommand) mods |= JALIUM_MOD_META;
    if (flags & NSEventModifierFlagCapsLock) mods |= JALIUM_MOD_CAPS;
    return mods;
}

static void DispatchEvent(JaliumPlatformWindow* win, const JaliumPlatformEvent& evt)
{
    if (win && win->callback && !win->destroyed)
    {
        win->callback(&evt, win->userData);
    }
}

@interface JaliumMacOSWindow : NSWindow
@property(nonatomic, assign) void* platformWindowPtr;
@end

@implementation JaliumMacOSWindow

- (BOOL)canBecomeKeyWindow
{
    return YES;
}

- (BOOL)canBecomeMainWindow
{
    return YES;
}

- (BOOL)mouseDownCanMoveWindow
{
    return YES;
}

- (void)sendEvent:(NSEvent*)event
{
    [super sendEvent:event];

    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return;

    JaliumPlatformEvent evt{};
    evt.window = win;

    switch (event.type)
    {
        case NSEventTypeLeftMouseDown:
        case NSEventTypeRightMouseDown:
        case NSEventTypeOtherMouseDown:
        {
            evt.type = JALIUM_EVENT_MOUSE_DOWN;
            evt.mouse.x = (float)[event locationInWindow].x;
            evt.mouse.y = (float)(win->height - [event locationInWindow].y);
            evt.mouse.button = (event.type == NSEventTypeRightMouseDown) ? JALIUM_MOUSE_BUTTON_RIGHT : JALIUM_MOUSE_BUTTON_LEFT;
            evt.mouse.modifiers = GetModifiers(event);
            evt.mouse.clickCount = (int32_t)[event clickCount];
            DispatchEvent(win, evt);
            break;
        }
        case NSEventTypeLeftMouseUp:
        case NSEventTypeRightMouseUp:
        case NSEventTypeOtherMouseUp:
        {
            evt.type = JALIUM_EVENT_MOUSE_UP;
            evt.mouse.x = (float)[event locationInWindow].x;
            evt.mouse.y = (float)(win->height - [event locationInWindow].y);
            evt.mouse.button = (event.type == NSEventTypeRightMouseUp) ? JALIUM_MOUSE_BUTTON_RIGHT : JALIUM_MOUSE_BUTTON_LEFT;
            evt.mouse.modifiers = GetModifiers(event);
            evt.mouse.clickCount = (int32_t)[event clickCount];
            DispatchEvent(win, evt);
            break;
        }
        case NSEventTypeMouseMoved:
        case NSEventTypeLeftMouseDragged:
        case NSEventTypeRightMouseDragged:
        case NSEventTypeOtherMouseDragged:
        {
            evt.type = JALIUM_EVENT_MOUSE_MOVE;
            evt.mouse.x = (float)[event locationInWindow].x;
            evt.mouse.y = (float)(win->height - [event locationInWindow].y);
            evt.mouse.modifiers = GetModifiers(event);
            DispatchEvent(win, evt);
            break;
        }
        case NSEventTypeScrollWheel:
        {
            evt.type = JALIUM_EVENT_MOUSE_WHEEL;
            evt.wheel.x = (float)[event scrollingDeltaX];
            evt.wheel.y = (float)[event scrollingDeltaY];
            evt.wheel.deltaX = (float)[event scrollingDeltaX];
            evt.wheel.deltaY = (float)[event scrollingDeltaY];
            evt.wheel.modifiers = GetModifiers(event);
            DispatchEvent(win, evt);
            break;
        }
        case NSEventTypeKeyDown:
        case NSEventTypeKeyUp:
        {
            evt.type = (event.type == NSEventTypeKeyDown) ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP;
            evt.key.keyCode = (int32_t)[event keyCode];
            evt.key.scanCode = (int32_t)[event keyCode];
            evt.key.modifiers = GetModifiers(event);
            evt.key.isRepeat = [event isARepeat] ? 1 : 0;
            DispatchEvent(win, evt);

            if (event.type == NSEventTypeKeyDown)
            {
                NSString* chars = [event charactersIgnoringModifiers];
                if ([chars length] > 0)
                {
                    unichar code = [chars characterAtIndex:0];
                    JaliumPlatformEvent charEvt{};
                    charEvt.type = JALIUM_EVENT_CHAR_INPUT;
                    charEvt.window = win;
                    charEvt.character.codepoint = static_cast<uint32_t>(code);
                    DispatchEvent(win, charEvt);
                }
            }
            break;
        }
        default:
            break;
    }
}

@end

@interface JaliumWindowDelegate : NSObject <NSWindowDelegate>
@property(nonatomic, assign) void* platformWindowPtr;
@end

@implementation JaliumWindowDelegate

- (BOOL)windowShouldClose:(id)sender
{
    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return YES;

    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_CLOSE_REQUESTED;
    evt.window = win;
    DispatchEvent(win, evt);
    return NO;
}

- (void)windowDidResize:(NSNotification*)notification
{
    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return;

    NSRect contentRect = [win->nsWindow contentLayoutRect];
    win->width = (int32_t)contentRect.size.width;
    win->height = (int32_t)contentRect.size.height;

    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_RESIZE;
    evt.window = win;
    evt.resize.width = win->width;
    evt.resize.height = win->height;
    DispatchEvent(win, evt);
}

- (void)windowDidMove:(NSNotification*)notification
{
    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return;

    NSRect frame = [win->nsWindow frame];
    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_MOVE;
    evt.window = win;
    evt.move.x = (int32_t)frame.origin.x;
    evt.move.y = (int32_t)frame.origin.y;
    DispatchEvent(win, evt);
}

- (void)windowDidBecomeKey:(NSNotification*)notification
{
    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return;

    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_ACTIVATE;
    evt.window = win;
    DispatchEvent(win, evt);
}

- (void)windowDidResignKey:(NSNotification*)notification
{
    JaliumPlatformWindow* win = (JaliumPlatformWindow*)self.platformWindowPtr;
    if (!win || !win->callback || win->destroyed)
        return;

    JaliumPlatformEvent evt{};
    evt.type = JALIUM_EVENT_DEACTIVATE;
    evt.window = win;
    DispatchEvent(win, evt);
}

@end

static void ProcessPendingEvents(int32_t& count)
{
    @autoreleasepool {
        NSEvent* event = nil;
        NSDate* limit = [NSDate dateWithTimeIntervalSinceNow:0.001];
        while ((event = [NSApp nextEventMatchingMask:NSEventMaskAny
                                        untilDate:limit
                                           inMode:NSDefaultRunLoopMode
                                          dequeue:YES]))
        {
            [NSApp sendEvent:event];
            [NSApp updateWindows];
            count++;
        }
    }
}

static NSString* StringFromWChar(const wchar_t* text)
{
    if (!text) return nil;
    size_t length = 0;
    while (text[length] != 0) length++;

    if (length == 0)
        return @"";

    std::vector<UniChar> chars;
    chars.reserve(length);
    for (size_t i = 0; i < length; ++i)
    {
        wchar_t wc = text[i];
        chars.push_back(static_cast<UniChar>(wc));
    }

    return [NSString stringWithCharacters:chars.data() length:chars.size()];
}

static wchar_t* WCharFromString(NSString* string)
{
    if (!string) return nullptr;
    NSUInteger len = [string length];
    if (len == 0)
    {
        wchar_t* result = static_cast<wchar_t*>(malloc(sizeof(wchar_t)));
        if (result) result[0] = 0;
        return result;
    }

    wchar_t* result = static_cast<wchar_t*>(malloc((len + 1) * sizeof(wchar_t)));
    if (!result) return nullptr;

    for (NSUInteger i = 0; i < len; ++i)
    {
        result[i] = static_cast<wchar_t>([string characterAtIndex:i]);
    }
    result[len] = 0;
    return result;
}

JaliumResult jalium_platform_init_impl()
{
    @autoreleasepool {
        if (!g_mainRunLoop)
        {
            NSApplication* app = [NSApplication sharedApplication];
            [app setActivationPolicy:NSApplicationActivationPolicyRegular];
            [app finishLaunching];
            g_mainRunLoop = CFRunLoopGetCurrent();
            CFRetain(g_mainRunLoop);
        }
    }
    return JALIUM_OK;
}

void jalium_platform_shutdown_impl()
{
    @autoreleasepool {
        if (g_mainRunLoop)
        {
            CFRelease(g_mainRunLoop);
            g_mainRunLoop = nullptr;
        }
    }
}

JaliumPlatform jalium_platform_get_current_impl()
{
    return JALIUM_PLATFORM_MACOS;
}

JaliumPlatformWindow* jalium_window_create(const JaliumWindowParams* params)
{
    if (!params) return nullptr;

    @autoreleasepool {
        NSRect frame = NSMakeRect(0, 0, params->width > 0 ? params->width : 800, params->height > 0 ? params->height : 600);
        NSUInteger styleMask = NSWindowStyleMaskTitled | NSWindowStyleMaskResizable | NSWindowStyleMaskClosable;
        if (params->style & JALIUM_WINDOW_STYLE_BORDERLESS)
        {
            styleMask = NSWindowStyleMaskBorderless;
        }
        else if (!(params->style & JALIUM_WINDOW_STYLE_TITLEBAR))
        {
            styleMask &= ~NSWindowStyleMaskTitled;
        }
        if (!(params->style & JALIUM_WINDOW_STYLE_RESIZABLE))
            styleMask &= ~NSWindowStyleMaskResizable;

        JaliumMacOSWindow* window = [[JaliumMacOSWindow alloc] initWithContentRect:frame
                                                                          styleMask:styleMask
                                                                            backing:NSBackingStoreBuffered
                                                                              defer:NO];
        if (!window)
            return nullptr;

        JaliumPlatformWindow* win = new JaliumPlatformWindow();
        win->nsWindow = window;
        win->style = params->style;
        win->width = (int32_t)frame.size.width;
        win->height = (int32_t)frame.size.height;
        win->dpiScale = (float)[window backingScaleFactor];
        window.platformWindowPtr = win;

        JaliumWindowDelegate* delegate = [[JaliumWindowDelegate alloc] init];
        delegate.platformWindowPtr = win;
        [window setDelegate:delegate];
        objc_setAssociatedObject(window, "JaliumWindowDelegate", delegate, OBJC_ASSOCIATION_RETAIN_NONATOMIC);

        if (params->title)
        {
            NSString* title = StringFromWChar(params->title);
            if (title)
            {
                [window setTitle:title];
            }
        }

        if (params->style & JALIUM_WINDOW_STYLE_BORDERLESS)
        {
            [window setMovableByWindowBackground:YES];
        }

        if (params->x != JALIUM_DEFAULT_POS || params->y != JALIUM_DEFAULT_POS)
        {
            NSPoint origin = NSMakePoint(params->x != JALIUM_DEFAULT_POS ? params->x : frame.origin.x,
                                         params->y != JALIUM_DEFAULT_POS ? params->y : frame.origin.y);
            [window setFrameOrigin:origin];
        }
        else
        {
            [window center];
        }

        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        g_windowMap[window] = win;
        return win;
    }
}

void jalium_window_destroy(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return;

    @autoreleasepool {
        window->destroyed = true;
        [[window->nsWindow contentView] removeFromSuperview];
        [window->nsWindow close];
        JaliumPlatformEvent evt{};
        evt.type = JALIUM_EVENT_DESTROYED;
        evt.window = window;
        DispatchEvent(window, evt);

        std::lock_guard<std::mutex> lock(g_windowMapMutex);
        g_windowMap.erase(window->nsWindow);
        window->nsWindow = nil;
    }
    delete window;
}

void jalium_window_show(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        [window->nsWindow makeKeyAndOrderFront:nil];
        [NSApp activateIgnoringOtherApps:YES];
    }
}

void jalium_window_hide(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        [window->nsWindow orderOut:nil];
    }
}

void jalium_window_set_title(JaliumPlatformWindow* window, const wchar_t* title)
{
    if (!window || !window->nsWindow || !title) return;
    @autoreleasepool {
        NSString* string = StringFromWChar(title);
        if (string)
            [window->nsWindow setTitle:string];
    }
}

void jalium_window_resize(JaliumPlatformWindow* window, int32_t width, int32_t height)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        NSRect frame = [window->nsWindow frame];
        NSRect contentRect = [window->nsWindow contentRectForFrameRect:frame];
        contentRect.size.width = width;
        contentRect.size.height = height;
        NSRect newFrame = [window->nsWindow frameRectForContentRect:contentRect];
        [window->nsWindow setFrame:newFrame display:YES animate:NO];
        window->width = width;
        window->height = height;
    }
}

void jalium_window_move(JaliumPlatformWindow* window, int32_t x, int32_t y)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        NSRect frame = [window->nsWindow frame];
        frame.origin.x = x;
        frame.origin.y = y;
        [window->nsWindow setFrameOrigin:frame.origin];
    }
}

void jalium_window_set_state(JaliumPlatformWindow* window, JaliumWindowState state)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        switch (state)
        {
            case JALIUM_WINDOW_STATE_MINIMIZED:
                [window->nsWindow miniaturize:nil];
                break;
            case JALIUM_WINDOW_STATE_MAXIMIZED:
                [window->nsWindow zoom:nil];
                break;
            case JALIUM_WINDOW_STATE_FULLSCREEN:
                if (([window->nsWindow styleMask] & NSWindowStyleMaskFullScreen) == 0)
                    [window->nsWindow toggleFullScreen:nil];
                break;
            default:
                if ([window->nsWindow isMiniaturized])
                    [window->nsWindow deminiaturize:nil];
                break;
        }
    }
}

JaliumWindowState jalium_window_get_state(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return JALIUM_WINDOW_STATE_NORMAL;
    @autoreleasepool {
        if ([window->nsWindow isMiniaturized])
            return JALIUM_WINDOW_STATE_MINIMIZED;
        if ([window->nsWindow isZoomed])
            return JALIUM_WINDOW_STATE_MAXIMIZED;
        return JALIUM_WINDOW_STATE_NORMAL;
    }
}

intptr_t jalium_window_get_native_handle(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return 0;
    return reinterpret_cast<intptr_t>(window->nsWindow);
}

JaliumSurfaceDescriptor jalium_window_get_surface(JaliumPlatformWindow* window)
{
    JaliumSurfaceDescriptor desc{};
    if (!window || !window->nsWindow)
        return desc;

    desc.platform = JALIUM_PLATFORM_MACOS;
    desc.handle0 = reinterpret_cast<intptr_t>(window->nsWindow);
    return desc;
}

void jalium_window_set_event_callback(JaliumPlatformWindow* window,
                                      JaliumEventCallback callback, void* userData)
{
    if (!window) return;
    window->callback = callback;
    window->userData = userData;
}

void jalium_window_invalidate(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return;
    @autoreleasepool {
        [[window->nsWindow contentView] setNeedsDisplay:YES];
    }
}

void jalium_window_set_cursor(JaliumPlatformWindow* window, JaliumCursorShape cursor)
{
    if (!window) return;
    @autoreleasepool {
        switch (cursor)
        {
            case JALIUM_CURSOR_ARROW:
                [[NSCursor arrowCursor] set];
                break;
            case JALIUM_CURSOR_HAND:
                [[NSCursor pointingHandCursor] set];
                break;
            case JALIUM_CURSOR_IBEAM:
                [[NSCursor IBeamCursor] set];
                break;
            case JALIUM_CURSOR_CROSSHAIR:
                [[NSCursor crosshairCursor] set];
                break;
            case JALIUM_CURSOR_RESIZE_NS:
            case JALIUM_CURSOR_RESIZE_NWSE:
            case JALIUM_CURSOR_RESIZE_NESW:
            case JALIUM_CURSOR_RESIZE_EW:
            case JALIUM_CURSOR_RESIZE_ALL:
                [[NSCursor resizeLeftRightCursor] set];
                break;
            case JALIUM_CURSOR_WAIT:
                [[NSCursor closedHandCursor] set];
                break;
            case JALIUM_CURSOR_HIDDEN:
                [NSCursor hide];
                break;
            default:
                [[NSCursor arrowCursor] set];
                break;
        }
    }
}

void jalium_window_get_client_size(JaliumPlatformWindow* window, int32_t* width, int32_t* height)
{
    if (!window || !window->nsWindow) { if (width) *width = 0; if (height) *height = 0; return; }
    @autoreleasepool {
        NSRect contentRect = [window->nsWindow contentLayoutRect];
        if (width) *width = (int32_t)contentRect.size.width;
        if (height) *height = (int32_t)contentRect.size.height;
    }
}

void jalium_window_get_position(JaliumPlatformWindow* window, int32_t* x, int32_t* y)
{
    if (!window || !window->nsWindow) { if (x) *x = 0; if (y) *y = 0; return; }
    @autoreleasepool {
        NSRect frame = [window->nsWindow frame];
        if (x) *x = (int32_t)frame.origin.x;
        if (y) *y = (int32_t)frame.origin.y;
    }
}

float jalium_window_get_dpi_scale(JaliumPlatformWindow* window)
{
    if (!window || !window->nsWindow) return 1.0f;
    @autoreleasepool {
        return (float)[window->nsWindow backingScaleFactor];
    }
}

int32_t jalium_window_get_monitor_refresh_rate(JaliumPlatformWindow* window)
{
    return 60;
}

int32_t jalium_platform_run_message_loop(void)
{
    g_quitRequested = false;
    while (!g_quitRequested.load(std::memory_order_acquire))
    {
        int32_t count = 0;
        ProcessPendingEvents(count);
        if (g_quitRequested.load(std::memory_order_acquire))
            break;

        // Sleep briefly to avoid burning CPU.
        [NSThread sleepForTimeInterval:0.01];
    }
    return g_exitCode.load(std::memory_order_acquire);
}

int32_t jalium_platform_poll_events(void)
{
    int32_t count = 0;
    ProcessPendingEvents(count);
    return count;
}

void jalium_platform_quit(int32_t exitCode)
{
    g_exitCode = exitCode;
    g_quitRequested = true;
    if (g_mainRunLoop)
    {
        CFRunLoopWakeUp(g_mainRunLoop);
    }
}

struct JaliumDispatcher {
    CFRunLoopSourceRef source = nullptr;
    CFRunLoopRef runLoop = nullptr;
    JaliumDispatcherCallback callback = nullptr;
    void* userData = nullptr;
};

static void DispatcherSourcePerform(void* info)
{
    JaliumDispatcher* dispatcher = reinterpret_cast<JaliumDispatcher*>(info);
    if (dispatcher && dispatcher->callback)
        dispatcher->callback(dispatcher->userData);
}

JaliumResult jalium_dispatcher_create(JaliumDispatcher** outDispatcher)
{
    if (!outDispatcher) return JALIUM_ERROR_INVALID_ARGUMENT;

    JaliumDispatcher* dispatcher = new JaliumDispatcher();
    CFRunLoopSourceContext context{};
    context.version = 0;
    context.info = dispatcher;
    context.perform = DispatcherSourcePerform;

    dispatcher->source = CFRunLoopSourceCreate(nullptr, 0, &context);
    if (!dispatcher->source)
    {
        delete dispatcher;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    dispatcher->runLoop = CFRunLoopGetCurrent();
    CFRetain(dispatcher->runLoop);
    CFRunLoopAddSource(dispatcher->runLoop, dispatcher->source, kCFRunLoopDefaultMode);

    *outDispatcher = dispatcher;
    return JALIUM_OK;
}

void jalium_dispatcher_destroy(JaliumDispatcher* dispatcher)
{
    if (!dispatcher) return;
    if (dispatcher->source && dispatcher->runLoop)
    {
        CFRunLoopRemoveSource(dispatcher->runLoop, dispatcher->source, kCFRunLoopDefaultMode);
        CFRelease(dispatcher->source);
        CFRelease(dispatcher->runLoop);
    }
    delete dispatcher;
}

void jalium_dispatcher_wake(JaliumDispatcher* dispatcher)
{
    if (!dispatcher || !dispatcher->source || !dispatcher->runLoop) return;
    CFRunLoopSourceSignal(dispatcher->source);
    CFRunLoopWakeUp(dispatcher->runLoop);
}

void jalium_dispatcher_set_callback(JaliumDispatcher* dispatcher,
                                    JaliumDispatcherCallback callback, void* userData)
{
    if (!dispatcher) return;
    dispatcher->callback = callback;
    dispatcher->userData = userData;
}

struct JaliumTimer {
    dispatch_source_t source = nullptr;
    JaliumTimerCallback callback = nullptr;
    void* userData = nullptr;
    dispatch_semaphore_t semaphore = nullptr;
    bool repeating = false;
};

JaliumResult jalium_timer_create(JaliumTimer** outTimer)
{
    if (!outTimer) return JALIUM_ERROR_INVALID_ARGUMENT;

    JaliumTimer* timer = new JaliumTimer();
    timer->source = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0));
    if (!timer->source)
    {
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    timer->semaphore = dispatch_semaphore_create(0);
    if (!timer->semaphore)
    {
        dispatch_release(timer->source);
        delete timer;
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }

    dispatch_source_set_event_handler(timer->source, ^{
        if (timer->callback)
            timer->callback(timer->userData);
        dispatch_semaphore_signal(timer->semaphore);
        if (!timer->repeating)
            dispatch_suspend(timer->source);
    });

    dispatch_resume(timer->source);
    *outTimer = timer;
    return JALIUM_OK;
}

void jalium_timer_destroy(JaliumTimer* timer)
{
    if (!timer) return;
    if (timer->source)
    {
        dispatch_source_cancel(timer->source);
        dispatch_release(timer->source);
        timer->source = nullptr;
    }
    if (timer->semaphore)
    {
        dispatch_release(timer->semaphore);
        timer->semaphore = nullptr;
    }
    delete timer;
}

void jalium_timer_arm(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || !timer->source) return;
    timer->repeating = false;
    dispatch_time_t start = dispatch_time(DISPATCH_TIME_NOW, intervalMicroseconds * 1000);
    dispatch_source_set_timer(timer->source, start, DISPATCH_TIME_FOREVER, 0);
}

void jalium_timer_arm_repeating(JaliumTimer* timer, int64_t intervalMicroseconds)
{
    if (!timer || !timer->source) return;
    timer->repeating = true;
    dispatch_time_t start = dispatch_time(DISPATCH_TIME_NOW, intervalMicroseconds * 1000);
    uint64_t interval = intervalMicroseconds * 1000;
    dispatch_source_set_timer(timer->source, start, interval, 0);
}

void jalium_timer_disarm(JaliumTimer* timer)
{
    if (!timer || !timer->source) return;
    dispatch_source_set_timer(timer->source, DISPATCH_TIME_FOREVER, DISPATCH_TIME_FOREVER, 0);
    timer->repeating = false;
}

void jalium_timer_set_callback(JaliumTimer* timer, JaliumTimerCallback callback, void* userData)
{
    if (!timer) return;
    timer->callback = callback;
    timer->userData = userData;
}

int32_t jalium_timer_wait(JaliumTimer* timer, uint32_t timeoutMs)
{
    if (!timer || !timer->semaphore) return 0;
    dispatch_time_t waitTime = (timeoutMs == 0)
        ? DISPATCH_TIME_FOREVER
        : dispatch_time(DISPATCH_TIME_NOW, (uint64_t)timeoutMs * 1000000ULL);
    long result = dispatch_semaphore_wait(timer->semaphore, waitTime);
    return (result == 0) ? 1 : 0;
}

float jalium_platform_get_system_dpi_scale(void)
{
    @autoreleasepool {
        NSScreen* screen = [NSScreen mainScreen];
        if (!screen) return 1.0f;
        return (float)[screen backingScaleFactor];
    }
}

int16_t jalium_input_get_key_state(int32_t jaliumVirtualKey)
{
    // macOS virtual key state mapping is not implemented.
    return 0;
}

void jalium_input_get_cursor_pos(float* x, float* y)
{
    CGEventRef event = CGEventCreate(nullptr);
    CGPoint loc = CGEventGetLocation(event);
    if (x) *x = (float)loc.x;
    if (y) *y = (float)loc.y;
    if (event) CFRelease(event);
}

JaliumResult jalium_clipboard_get_text(wchar_t** outText)
{
    if (!outText) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outText = nullptr;

    @autoreleasepool {
        NSPasteboard* pb = [NSPasteboard generalPasteboard];
        NSString* str = [pb stringForType:NSPasteboardTypeString];
        if (!str) return JALIUM_OK;
        *outText = WCharFromString(str);
        return JALIUM_OK;
    }
}

JaliumResult jalium_clipboard_set_text(const wchar_t* text)
{
    if (!text) return JALIUM_ERROR_INVALID_ARGUMENT;

    @autoreleasepool {
        NSString* str = StringFromWChar(text);
        if (!str) return JALIUM_ERROR_INVALID_ARGUMENT;
        NSPasteboard* pb = [NSPasteboard generalPasteboard];
        [pb clearContents];
        [pb setString:str forType:NSPasteboardTypeString];
        return JALIUM_OK;
    }
}
