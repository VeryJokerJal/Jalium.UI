#pragma once

#if defined(_WIN32)
#define VK_USE_PLATFORM_WIN32_KHR 1
#elif defined(__linux__)
#define VK_USE_PLATFORM_XLIB_KHR 1
#include <X11/Xlib.h>
#endif

#include <vulkan/vulkan.h>
