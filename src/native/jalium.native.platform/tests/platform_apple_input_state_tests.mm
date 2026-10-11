#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#import <Carbon/Carbon.h>
#import <objc/runtime.h>
#include <IOKit/hidsystem/IOLLEvent.h>
#include <cstdio>
#include <dlfcn.h>
#include <initializer_list>
#include <thread>

extern "C" void JaliumTestSetPolledKey(unsigned, bool);
extern "C" void JaliumTestResetPolledKeys();
extern "C" unsigned JaliumTestKeyQueryCount();
extern "C" bool JaliumTestWrongKeySource();
extern "C" void JaliumTestSetLayoutASCII(int);
extern "C" void JaliumTestKeyboardLayoutChanged();
extern "C" unsigned JaliumTestOffMainLayoutQueries();
static NSEventModifierFlags flags;
static NSUInteger buttons;
static bool swappedLayout;
static bool fullWidthLayout;
static bool frenchNumberRow;
static bool nonRomanLayout;
static bool commandLayout;
static NSString* overrideText;
static CFDataRef nativeLayout;
static IMP originalCharacters;
static NSEventModifierFlags TestFlags(id, SEL) { return flags; }
static NSUInteger TestButtons(id, SEL) { return buttons; }
struct Letter { unsigned scan; int vk; };
static const Letter letters[] = {{0,'A'},{1,'S'},{2,'D'},{3,'F'},{4,'H'},{5,'G'},
    {6,'Z'},{7,'X'},{8,'C'},{9,'V'},{11,'B'},{12,'Q'},{13,'W'},{14,'E'},
    {15,'R'},{16,'Y'},{17,'T'},{31,'O'},{32,'U'},{34,'I'},{35,'P'},
    {37,'L'},{38,'J'},{40,'K'},{45,'N'},{46,'M'}};
static NSString* NativeCharacters(CFDataRef layout, unsigned scan, NSEventModifierFlags modifiers)
{
    UInt32 mask = ((modifiers & NSEventModifierFlagCommand) ? (cmdKey >> 8) : 0) |
        ((modifiers & NSEventModifierFlagShift) ? (shiftKey >> 8) : 0) |
        ((modifiers & NSEventModifierFlagOption) ? (optionKey >> 8) : 0) |
        ((modifiers & NSEventModifierFlagControl) ? (controlKey >> 8) : 0);
    UInt32 dead = 0; UniChar text[16]; UniCharCount length = 0;
    OSStatus result = UCKeyTranslate(reinterpret_cast<const UCKeyboardLayout*>(CFDataGetBytePtr(layout)),
        scan, kUCKeyActionDown, mask, LMGetKbdType(), kUCKeyTranslateNoDeadKeysMask, &dead, 16, &length, text);
    return result == noErr ? [[NSString alloc] initWithCharacters:text length:length] : nil;
}
static NSString* TestCharacters(NSEvent* event, SEL selector, NSEventModifierFlags modifiers)
{
    // Controlled US/IME/AZERTY layout data, independent of the user's active
    // input source. This is separate from actual non-English hardware input.
    if (nativeLayout) return NativeCharacters(nativeLayout, event.keyCode, modifiers);
    if (overrideText) return overrideText;
    if (nonRomanLayout && !(modifiers & NSEventModifierFlagCommand)) return event.keyCode == 12 ? @";" : @"ф";
    if (commandLayout && event.keyCode == 12) return (modifiers & NSEventModifierFlagCommand) ? @"q" : @"'";
    if (swappedLayout && event.keyCode == 0) return @"q";
    if (swappedLayout && event.keyCode == 12) return @"a";
    if (fullWidthLayout) {
        switch (event.keyCode) {
            case 24: return @"＝"; case 27: return @"－";
            case 33: return @"【"; case 30: return @"】";
            case 42: return @"、"; case 39: return @"‘";
            case 41: return @"；"; case 43: return @"，";
            case 47: return @"。"; case 44: return @"／";
            case 50: return @"·"; case 0: return @"ａ";
        }
    }
    if (frenchNumberRow) {
        switch (event.keyCode) {
            case 18: return @"&"; case 19: return @"é";
            case 20: return @"\""; case 21: return @"'";
            case 23: return @"("; case 22: return @"-";
            case 26: return @"è"; case 28: return @"_";
            case 25: return @"ç"; case 29: return @"à";
        }
    }
    for (auto key : letters) if (event.keyCode == key.scan) return [NSString stringWithFormat:@"%c", key.vk + 'a' - 'A'];
    switch (event.keyCode) {
        case 24: return @"="; case 27: return @"-";
        case 33: return @"["; case 30: return @"]";
        case 42: return @"\\"; case 39: return @"'";
        case 41: return @";"; case 43: return @",";
        case 47: return @"."; case 44: return @"/"; case 50: return @"`";
    }
    return reinterpret_cast<NSString* (*)(id, SEL, NSEventModifierFlags)>(originalCharacters)(event, selector, modifiers);
}
struct Replacement {
    Method method;
    IMP original;
    Replacement(Method m, IMP replacement) : method(m), original(method_setImplementation(m, replacement)) {}
    ~Replacement() { method_setImplementation(method, original); }
};
static int passed, total;
static void Check(bool valid, const char* name)
{
    ++total;
    if (valid) ++passed;
    else std::fprintf(stderr, "FAIL: %s\n", name);
}
static bool Down(int key) { return static_cast<uint16_t>(jalium_input_get_key_state(key)) == 0x8000; }
struct EventReceiver {
    JaliumPlatformWindow* window = nullptr;
    unsigned count = 0;
    JaliumPlatformEvent last{};
};
static void ReceiveKey(const JaliumPlatformEvent* event, void* context)
{
    if (event->type != JALIUM_EVENT_KEY_DOWN && event->type != JALIUM_EVENT_KEY_UP) return;
    auto& receiver = *static_cast<EventReceiver*>(context);
    ++receiver.count; receiver.last = *event;
}
static void CheckDeliveredKeys(EventReceiver (&receivers)[2], unsigned scan, int vk,
    NSString* text, NSEventModifierFlags modifiers = 0)
{
    for (auto& receiver : receivers) {
        NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(receiver.window));
        // Command key-up verifies its identity without invoking an application
        // key equivalent. No event is posted or sent through WindowServer.
        for (NSEventType type : {NSEventTypeKeyDown, NSEventTypeKeyUp}) {
            if (type == NSEventTypeKeyDown && (modifiers & NSEventModifierFlagCommand)) continue;
            unsigned count = receiver.count;
            NSEvent* event = [NSEvent keyEventWithType:type location:NSZeroPoint modifierFlags:modifiers
                timestamp:0 windowNumber:view.window.windowNumber context:nil characters:text ?: @""
                charactersIgnoringModifiers:text ?: @"" isARepeat:NO keyCode:scan];
            if (type == NSEventTypeKeyDown) [view keyDown:event]; else [view keyUp:event];
            Check(receiver.count == count + 1 && receiver.last.key.keyCode == vk &&
                receiver.last.key.scanCode == static_cast<int32_t>(scan) &&
                receiver.last.type == (type == NSEventTypeKeyDown ? JALIUM_EVENT_KEY_DOWN : JALIUM_EVENT_KEY_UP),
                "native/custom responder events share the polled VK identity");
            Check([event.characters isEqualToString:text ?: @""], "key normalization leaves original event text intact");
        }
    }
}

int main()
{
    @autoreleasepool {
        Dl_info loaded{};
        if (dladdr(reinterpret_cast<const void*>(jalium_input_get_key_state), &loaded))
            std::printf("LOADED_PLATFORM %s\n", loaded.dli_fname);
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        Replacement modifier(class_getClassMethod(NSEvent.class, @selector(modifierFlags)), reinterpret_cast<IMP>(TestFlags));
        Replacement mouse(class_getClassMethod(NSEvent.class, @selector(pressedMouseButtons)), reinterpret_cast<IMP>(TestButtons));
        Replacement chars(class_getInstanceMethod(NSEvent.class, @selector(charactersByApplyingModifiers:)), reinterpret_cast<IMP>(TestCharacters));
        originalCharacters = chars.original;
        EventReceiver receivers[2];
        for (unsigned i = 0; i < 2; ++i) {
            JaliumWindowParams params{};
            params.title = reinterpret_cast<const JaliumUtf16Char*>(u"Hidden key protocol fixture");
            params.x = 80; params.y = 100; params.width = 320; params.height = 180;
            params.style = i ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR;
            receivers[i].window = jalium_window_create(&params);
            Check(receivers[i].window != nullptr, "create hidden native/custom protocol view");
            if (!receivers[i].window) return 1;
            jalium_window_update_ime_context(receivers[i].window, 0, nullptr, 0, 0, 0, 0, 0, 0);
            jalium_window_set_event_callback(receivers[i].window, ReceiveKey, &receivers[i]);
        }
        struct Key { unsigned scan; int vk; };
        const Key keys[] = {{0, 'A'}, {11, 'B'}, {12, 'Q'}, {18, '1'},
            {19, '2'}, {20, '3'}, {21, '4'}, {23, '5'}, {22, '6'}, {26, '7'},
            {28, '8'}, {25, '9'}, {29, '0'}, {10, 0xe2}, {49, 0x20},
            {48, 0x09}, {36, 0x0d}, {53, 0x1b}, {51, 0x08}, {123, 0x25}, {124, 0x27},
            {125, 0x28}, {126, 0x26}, {115, 0x24}, {119, 0x23}, {116, 0x21}, {121, 0x22},
            {117, 0x2e}, {122, 0x70}, {120, 0x71}, {99, 0x72}, {118, 0x73}, {96, 0x74},
            {97, 0x75}, {98, 0x76}, {100, 0x77}, {101, 0x78}, {109, 0x79}, {103, 0x7a},
            {111, 0x7b}, {105, 0x7c}, {107, 0x7d}, {113, 0x7e}, {106, 0x7f}, {64, 0x80},
            {79, 0x81}, {80, 0x82}, {90, 0x83}, {82, 0x60}, {83, 0x61}, {84, 0x62},
            {85, 0x63}, {86, 0x64}, {87, 0x65}, {88, 0x66}, {89, 0x67}, {91, 0x68},
            {92, 0x69}, {65, 0x6e}, {67, 0x6a}, {69, 0x6b}, {75, 0x6f}, {78, 0x6d},
            {24, 0xbb}, {27, 0xbd}, {33, 0xdb}, {30, 0xdd}, {42, 0xdc}, {39, 0xde},
            {41, 0xba}, {43, 0xbc}, {47, 0xbe}, {44, 0xbf}, {50, 0xc0}};
        for (auto key : keys) {
            JaliumTestResetPolledKeys();
            Check(jalium_input_get_key_state(key.vk) == 0, "released key snapshot");
            JaliumTestSetPolledKey(key.scan, true);
            bool held = Down(key.vk);
            if (!held) {
                NSEvent* event = [NSEvent keyEventWithType:NSEventTypeKeyDown location:NSZeroPoint
                    modifierFlags:0 timestamp:0 windowNumber:0 context:nil characters:@""
                    charactersIgnoringModifiers:@"" isARepeat:NO keyCode:key.scan];
                NSString* text = [event charactersByApplyingModifiers:0];
                std::fprintf(stderr, "key scan=%u expected VK=0x%x layout=\"%s\"\n", key.scan, key.vk, text.UTF8String ?: "");
            }
            Check(held, "held key uses normalized VK and high state bit");
            JaliumTestSetPolledKey(key.scan, false);
            Check(jalium_input_get_key_state(key.vk) == 0, "released without a delivered key-up");
            Check(JaliumTestKeyQueryCount() > 0 && !JaliumTestWrongKeySource(), "combined login-session snapshot");
        }
        JaliumTestResetPolledKeys(); flags = NSEventModifierFlagShift;
        JaliumTestSetPolledKey(18, true);
        Check(Down('1') && !Down(0x21), "Shift+1 remains Digit1 rather than PageUp");
        flags = 0; swappedLayout = true;
        JaliumTestKeyboardLayoutChanged();
        JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(0, true);
        Check(Down('Q') && !Down('A'), "current layout maps physical A to Q");
        JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(12, true);
        Check(Down('A') && !Down('Q'), "current layout maps physical Q to A");
        swappedLayout = false;
        fullWidthLayout = true;
        JaliumTestKeyboardLayoutChanged();
        for (Key key : {Key{24,0xbb}, Key{27,0xbd}, Key{33,0xdb}, Key{30,0xdd}, Key{42,0xdc},
             Key{39,0xde}, Key{41,0xba}, Key{43,0xbc}, Key{47,0xbe}, Key{44,0xbf}, Key{50,0xc0}, Key{0,'A'}}) {
            JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(key.scan, true);
            Check(Down(key.vk), "full-width/IME text preserves normalized key identity");
            JaliumTestSetPolledKey(key.scan, false);
            Check(!Down(key.vk), "IME punctuation released without delivered key-up");
        }
        fullWidthLayout = false; frenchNumberRow = true;
        JaliumTestKeyboardLayoutChanged();
        for (Key key : {Key{18,'1'}, Key{19,'2'}, Key{20,'3'}, Key{21,'4'}, Key{23,'5'},
             Key{22,'6'}, Key{26,'7'}, Key{28,'8'}, Key{25,'9'}, Key{29,'0'}}) {
            JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(key.scan, true);
            Check(Down(key.vk) && !Down(0x26) && !Down(0x28), "AZERTY number-row text cannot become navigation");
            JaliumTestSetPolledKey(key.scan, false);
            Check(!Down(key.vk), "AZERTY number-row released");
        }
        frenchNumberRow = false;
        nonRomanLayout = true; JaliumTestSetLayoutASCII(0);
        JaliumTestKeyboardLayoutChanged();
        for (auto key : letters) {
            JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(key.scan, true);
            Check(Down(key.vk), "non-Roman layout uses its native Command identity");
            CheckDeliveredKeys(receivers, key.scan, key.vk, key.scan == 12 ? @";" : @"ф");
            if (key.scan == 12) Check(!Down(0xba), "Greek semicolon on Q is not an OEM key identity");
            JaliumTestSetPolledKey(key.scan, false);
            Check(!Down(key.vk), "non-Roman held key release does not require a delivered key-up");
        }
        nonRomanLayout = false; JaliumTestSetLayoutASCII(1);
        commandLayout = true; JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(12, true);
        JaliumTestKeyboardLayoutChanged();
        Check(Down(0xde) && !Down('Q'), "ASCII Dvorak layout retains ordinary letter/punctuation positions");
        flags = NSEventModifierFlagCommand;
        Check(Down('Q') && !Down(0xde), "held Command uses Dvorak-QWERTY Command identity");
        CheckDeliveredKeys(receivers, 12, 'Q', @"q", NSEventModifierFlagCommand);
        flags = 0; commandLayout = false;
        for (NSString* text : {@"ä", @"ф", @"!", @"@", @"🙂", @"", @"\u00f0"}) {
            overrideText = text;
            JaliumTestKeyboardLayoutChanged();
            JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(0, true);
            Check(Down('A') && !Down(0x21) && !Down(0x40) && !Down(0xf0), "unmapped/dead text cannot collide with other VK identities");
            JaliumTestSetPolledKey(0, false); Check(!Down('A'), "fallback key releases without a delivered key-up");
        }
        overrideText = nil;
        JaliumTestKeyboardLayoutChanged();
        for (int metadata : {-1, 2}) {
            JaliumTestSetLayoutASCII(metadata); JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(0, true);
            Check(Down('A'), "missing or mistyped layout metadata retains key translation");
        }
        JaliumTestSetLayoutASCII(1);
        for (Key key : {Key{93,0xdc}, Key{94,0xe2}, Key{95,0x6c}, Key{102,0xf0}, Key{104,0x15}, Key{114,0x2f}}) {
            overrideText = @"¥"; JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(key.scan, true);
            JaliumTestKeyboardLayoutChanged();
            Check(Down(key.vk), "JIS/Help physical identity is independent of text");
            CheckDeliveredKeys(receivers, key.scan, key.vk, @"¥");
            Check(!Down(0xa5) && !Down(0x25), "JIS Yen cannot masquerade as RightAlt or Left");
            JaliumTestSetPolledKey(key.scan, false); Check(!Down(key.vk), "JIS/Help release is a fresh snapshot");
        }
        overrideText = nil;
        // Native layout bytes are read without selecting an input source. The
        // event translation bridge uses Apple's UCKeyTranslate implementation;
        // only process-local source metadata and polled down bits are supplied.
        JaliumTestSetLayoutASCII(3);
        CFArrayRef sources = TISCreateInputSourceList(nullptr, true);
        unsigned nativeLayouts = 0;
        for (CFIndex i = 0; sources && i < CFArrayGetCount(sources); ++i) {
            TISInputSourceRef source = static_cast<TISInputSourceRef>(const_cast<void*>(CFArrayGetValueAtIndex(sources, i)));
            NSString* id = (__bridge NSString*)TISGetInputSourceProperty(source, kTISPropertyInputSourceID);
            bool roman = [id isEqualToString:@"com.apple.keylayout.German"];
            bool dvorakCommand = [id isEqualToString:@"com.apple.keylayout.DVORAK-QWERTYCMD"];
            if (!roman && !dvorakCommand && ![id isEqualToString:@"com.apple.keylayout.Russian"] &&
                ![id isEqualToString:@"com.apple.keylayout.Greek"] && ![id isEqualToString:@"com.apple.keylayout.Hebrew"] &&
                ![id isEqualToString:@"com.apple.keylayout.Arabic"] && ![id isEqualToString:@"com.apple.keylayout.Thai"]) continue;
            nativeLayout = static_cast<CFDataRef>(TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData));
            if (!nativeLayout) continue;
            ++nativeLayouts; JaliumTestSetLayoutASCII((roman || dvorakCommand) ? 1 : 0);
            JaliumTestKeyboardLayoutChanged();
            if (dvorakCommand) {
                JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(12, true);
                Check(Down(0xde) && !Down('Q'), "native Dvorak retains its ordinary punctuation identity");
                flags = NSEventModifierFlagCommand;
                Check(Down('Q') && !Down(0xde), "native Dvorak Command layer supplies Q");
                CheckDeliveredKeys(receivers, 12, 'Q', @"q", NSEventModifierFlagCommand);
                JaliumTestSetPolledKey(12, false);
                Check(!Down('Q') && !Down(0xde), "native Dvorak Command key released");
                flags = 0; JaliumTestSetLayoutASCII(3); nativeLayout = nullptr;
                continue;
            }
            for (auto key : letters) {
                int vk = roman && key.vk == 'Y' ? 'Z' : roman && key.vk == 'Z' ? 'Y' : key.vk;
                JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(key.scan, true);
                Check(Down(vk), "native installed Roman/non-Roman layout preserves letter VKs");
                bool workerDown = false;
                std::thread worker([&] { workerDown = Down(vk); }); worker.join();
                Check(workerDown, "background polling uses the complete current layout snapshot");
                CheckDeliveredKeys(receivers, key.scan, vk, NativeCharacters(nativeLayout, key.scan, 0));
                JaliumTestSetPolledKey(key.scan, false); Check(!Down(vk), "native installed layout key release");
            }
            if (roman) {
                JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(41, true);
                Check(Down(0xba) && !Down(0xf6), "native German umlaut OEM position cannot become Play");
            }
            JaliumTestSetLayoutASCII(3); nativeLayout = nullptr;
        }
        if (sources) CFRelease(sources);
        Check(nativeLayouts >= 4, "native installed layout data includes multiple scripts");
        std::printf("Native installed layout data: %u layouts\n", nativeLayouts);
        JaliumTestSetLayoutASCII(1);
        JaliumTestKeyboardLayoutChanged();
        Check(JaliumTestOffMainLayoutQueries() == 0, "background polling never calls TextInputSources");
        for (auto& receiver : receivers) jalium_window_destroy(receiver.window);
        JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(36, true); JaliumTestSetPolledKey(76, true);
        Check(Down(0x0d), "main Return and keypad Enter can both be held");
        JaliumTestSetPolledKey(36, false);
        Check(Down(0x0d), "releasing Return retains held keypad Enter");
        JaliumTestSetPolledKey(76, false);
        Check(!Down(0x0d), "both Return keys released");
        const int mouseKeys[] = {0x01, 0x02, 0x04, 0x05, 0x06};
        for (buttons = 0; buttons < 32; ++buttons)
            for (unsigned i = 0; i < 5; ++i)
                Check(Down(mouseKeys[i]) == ((buttons & (NSUInteger(1) << i)) != 0), "five-button mouse snapshot");
        buttons = 0;
        struct Modifier { int left, right, aggregate; NSUInteger lmask, rmask, combined; };
        for (auto m : {Modifier{0xa0,0xa1,0x10,NX_DEVICELSHIFTKEYMASK,NX_DEVICERSHIFTKEYMASK,NSEventModifierFlagShift},
             Modifier{0xa2,0xa3,0x11,NX_DEVICELCTLKEYMASK,NX_DEVICERCTLKEYMASK,NSEventModifierFlagControl},
             Modifier{0xa4,0xa5,0x12,NX_DEVICELALTKEYMASK,NX_DEVICERALTKEYMASK,NSEventModifierFlagOption},
             Modifier{0x5b,0x5c,0,NX_DEVICELCMDKEYMASK,NX_DEVICERCMDKEYMASK,NSEventModifierFlagCommand}}) {
            for (unsigned state = 0; state < 4; ++state) {
                flags = (state ? m.combined : 0) | ((state & 1) ? m.lmask : 0) | ((state & 2) ? m.rmask : 0);
                Check(Down(m.left) == ((state & 1) != 0) && Down(m.right) == ((state & 2) != 0) &&
                    (!m.aggregate || Down(m.aggregate) == (state != 0)), "left/right and aggregate modifiers");
            }
        }
        for (unsigned state = 0; state < 4; ++state) {
            flags = (state & 1) ? NSEventModifierFlagCapsLock : 0;
            JaliumTestSetPolledKey(57, (state & 2) != 0);
            Check(static_cast<uint16_t>(jalium_input_get_key_state(0x14)) ==
                (((state & 1) ? 1 : 0) | ((state & 2) ? 0x8000 : 0)), "Caps Lock down and toggle are independent");
        }
        flags = 0; JaliumTestResetPolledKeys(); JaliumTestSetPolledKey(0, true);
        for (int invalid : {-1000, -1, 0, 0xff, 256, 65535})
            Check(jalium_input_get_key_state(invalid) == 0, "invalid normalized key is released");
        std::printf("macOS input-state snapshot checks: %d/%d passed\n", passed, total);
        return passed == total ? 0 : 1;
    }
}
