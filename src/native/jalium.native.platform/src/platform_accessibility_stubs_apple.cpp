#include "jalium_platform.h"

// UIKit accessibility uses its own platform adapter; preserve the shared
// Apple entry points for static linking without attaching an AppKit tree.
// Non-Apple headers omit their declarations, so state C linkage explicitly.
extern "C" {
JALIUM_PLATFORM_API void jalium_apple_window_set_accessibility(JaliumPlatformWindow*, JaliumAccessibilityCallback, void*) {}
JALIUM_PLATFORM_API void jalium_apple_window_notify_accessibility(JaliumPlatformWindow*, uint64_t, int32_t) {}
}
