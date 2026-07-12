#pragma once

#if defined(_WIN32)
#define VK_USE_PLATFORM_WIN32_KHR 1
#elif defined(__ANDROID__)
#define VK_USE_PLATFORM_ANDROID_KHR 1
#include <android/native_window.h>
#elif defined(__linux__)
#define VK_USE_PLATFORM_XLIB_KHR 1
#define VK_USE_PLATFORM_WAYLAND_KHR 1
#include <X11/Xlib.h>
// Xlib exposes `None` as a global preprocessor macro.  Leaving it defined
// corrupts strongly typed C++ enums in the Vulkan and text headers (for
// example `enum class Res { None }`) on Linux builds.
#ifdef None
#undef None
#endif
#endif

#include <vulkan/vulkan.h>
