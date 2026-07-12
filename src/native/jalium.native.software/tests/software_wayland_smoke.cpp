#include "jalium_api.h"
#include "jalium_platform.h"

#include <chrono>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <thread>

using namespace std::chrono_literals;

extern "C" void jalium_software_init();

namespace {

bool PumpUntilPresentSucceeds(JaliumRenderTarget* renderTarget,
                              float red,
                              float green,
                              float blue)
{
    for (int attempt = 0; attempt < 250; ++attempt)
    {
        (void)jalium_platform_poll_events();
        if (jalium_render_target_begin_draw(renderTarget) == JALIUM_OK)
        {
            jalium_render_target_clear(renderTarget, red, green, blue, 1.0f);
            const JaliumResult result = jalium_render_target_end_draw(renderTarget);
            if (result == JALIUM_OK)
                return true;
            if (result != JALIUM_ERROR_PRESENT_FAILED)
                return false;
        }
        std::this_thread::sleep_for(2ms);
    }
    return false;
}

} // namespace

int main(int argc, char** argv)
{
    const bool x11 = argc > 1 && std::strcmp(argv[1], "--x11-smoke") == 0;
    setenv("JALIUM_WINDOW_SYSTEM", x11 ? "x11" : "wayland", 1);
    setenv("JALIUM_WAYLAND_SHM_BUFFERS", "3", 1);
    jalium_software_init();

    if (jalium_platform_init() != JALIUM_OK ||
        jalium_platform_get_current() !=
            (x11 ? JALIUM_PLATFORM_LINUX_X11 : JALIUM_PLATFORM_LINUX_WAYLAND))
    {
        std::cerr << "FAILED: initialize forced Linux window system\n";
        return 1;
    }

    const char16_t title[] = u"Jalium wl_shm smoke";
    JaliumWindowParams parameters{};
    parameters.title = reinterpret_cast<const JaliumUtf16Char*>(title);
    parameters.x = JALIUM_DEFAULT_POS;
    parameters.y = JALIUM_DEFAULT_POS;
    parameters.width = 320;
    parameters.height = 200;
    parameters.style = JALIUM_WINDOW_STYLE_DEFAULT;

    JaliumPlatformWindow* window = jalium_window_create(&parameters);
    if (!window)
    {
        std::cerr << "FAILED: create Wayland window\n";
        jalium_platform_shutdown();
        return 1;
    }
    jalium_window_show(window);

    const JaliumSurfaceDescriptor surface = jalium_window_get_surface(window);
    if (surface.platform !=
        (x11 ? JALIUM_PLATFORM_LINUX_X11 : JALIUM_PLATFORM_LINUX_WAYLAND))
    {
        std::cerr << "FAILED: window returned the wrong surface type\n";
        return 1;
    }
    JaliumContext* context = jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    JaliumRenderTarget* renderTarget = context
        ? jalium_render_target_create_for_surface(
              context, &surface, parameters.width, parameters.height)
        : nullptr;
    if (!context || !renderTarget)
    {
        std::cerr << "FAILED: create Wayland software render target\n";
        if (renderTarget) jalium_render_target_destroy(renderTarget);
        if (context) jalium_context_destroy(context);
        jalium_window_destroy(window);
        jalium_platform_shutdown();
        return 1;
    }

    if (!x11)
    {
        if (jalium_wayland_surface_is_ready(surface.handle1) != 0 ||
            jalium_render_target_begin_draw(renderTarget) != JALIUM_OK)
        {
            std::cerr << "FAILED: pre-configure Wayland software gate setup\n";
            return 1;
        }
        jalium_render_target_clear(renderTarget, 0.05f, 0.1f, 0.2f, 1.0f);
        if (jalium_render_target_end_draw(renderTarget) != JALIUM_ERROR_PRESENT_FAILED)
        {
            std::cerr << "FAILED: wl_shm buffer committed before configure ack\n";
            return 1;
        }
    }

    for (int attempt = 0; attempt < 100 &&
         (!x11 && jalium_wayland_surface_is_ready(surface.handle1) == 0); ++attempt)
    {
        (void)jalium_platform_poll_events();
        std::this_thread::sleep_for(2ms);
    }
    if (!x11 && jalium_wayland_surface_is_ready(surface.handle1) == 0)
    {
        std::cerr << "FAILED: Wayland surface never completed configure handshake\n";
        return 1;
    }
    if (x11)
    {
        // Preserve the original X11 smoke timing: let MapNotify reach the
        // platform before the synchronous XPutImage presentation checks.
        for (int attempt = 0; attempt < 100; ++attempt)
        {
            (void)jalium_platform_poll_events();
            std::this_thread::sleep_for(2ms);
        }
    }

    // Saturate the Wayland three-buffer pool without dispatching release
    // events. X11 is synchronous and only needs a normal multi-frame present.
    const int initialFrames = x11 ? 2 : 3;
    for (int frame = 0; frame < initialFrames; ++frame)
    {
        if (jalium_render_target_begin_draw(renderTarget) != JALIUM_OK)
        {
            std::cerr << "FAILED: begin initial software frame\n";
            return 1;
        }
        jalium_render_target_clear(
            renderTarget, 0.15f * frame, 0.25f, 0.65f, 1.0f);
        if (jalium_render_target_end_draw(renderTarget) != JALIUM_OK)
        {
            std::cerr << "FAILED: commit initial software frame\n";
            return 1;
        }
    }

    if (!PumpUntilPresentSucceeds(renderTarget, 0.8f, 0.25f, 0.1f))
    {
        std::cerr << "FAILED: compositor did not accept another software frame\n";
        return 1;
    }

    jalium_window_resize(window, 480, 300);
    if (jalium_render_target_resize(renderTarget, 480, 300) != JALIUM_OK ||
        !PumpUntilPresentSucceeds(renderTarget, 0.1f, 0.65f, 0.25f))
    {
        std::cerr << "FAILED: resized wl_shm buffer pool did not present\n";
        return 1;
    }

    if (!x11)
    {
        // Exercise the XRGB conversion path on a fresh target while retaining
        // the same platform surface. Alpha bytes are forced opaque.
        jalium_render_target_destroy(renderTarget);
        setenv("JALIUM_WAYLAND_SHM_FORMAT", "xrgb", 1);
        renderTarget = jalium_render_target_create_for_surface(
            context, &surface, 480, 300);
        if (!renderTarget ||
            !PumpUntilPresentSucceeds(renderTarget, 0.1f, 0.2f, 0.75f))
        {
            std::cerr << "FAILED: XRGB8888 wl_shm presentation\n";
            return 1;
        }

        jalium_window_hide(window);
        if (jalium_wayland_surface_is_ready(surface.handle1) != 0 ||
            jalium_render_target_begin_draw(renderTarget) != JALIUM_OK)
        {
            std::cerr << "FAILED: hidden wl_shm gate setup\n";
            return 1;
        }
        jalium_render_target_clear(renderTarget, 0.2f, 0.05f, 0.1f, 1.0f);
        if (jalium_render_target_end_draw(renderTarget) != JALIUM_ERROR_PRESENT_FAILED)
        {
            std::cerr << "FAILED: hidden wl_shm surface accepted a buffer\n";
            return 1;
        }

        jalium_window_show(window);
        if (jalium_wayland_surface_is_ready(surface.handle1) != 0 ||
            !PumpUntilPresentSucceeds(renderTarget, 0.25f, 0.1f, 0.55f))
        {
            std::cerr << "FAILED: re-shown wl_shm surface did not wait for and recover after configure\n";
            return 1;
        }
    }

    jalium_render_target_destroy(renderTarget);
    jalium_context_destroy(context);
    jalium_window_destroy(window);
    jalium_platform_shutdown();
    std::cout << (x11
        ? "X11 software presentation smoke test passed.\n"
        : "Wayland software wl_shm smoke test passed.\n");
    return 0;
}
