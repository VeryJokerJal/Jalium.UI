#pragma once

#include <cstdint>
#include <memory>

struct wl_display;
struct wl_surface;
struct wl_shm;

namespace jalium {

/// Presents the software backend's premultiplied BGRA8 framebuffer through
/// Wayland wl_shm. The wl_display and wl_surface remain owned by the platform
/// window; this object owns only the wl_shm proxy and its buffer pool.
class WaylandShmPresenter final {
public:
    static std::unique_ptr<WaylandShmPresenter> Create(
        wl_display* display, wl_surface* surface, wl_shm* shm);

    ~WaylandShmPresenter();

    WaylandShmPresenter(const WaylandShmPresenter&) = delete;
    WaylandShmPresenter& operator=(const WaylandShmPresenter&) = delete;

    /// Copies one BGRA8 frame and commits it. Returns false when every buffer
    /// is still owned by the compositor or when the Wayland connection fails.
    bool Present(const uint8_t* bgraPixels,
                 int32_t width,
                 int32_t height,
                 int32_t sourceStride);

private:
    struct Impl;

    explicit WaylandShmPresenter(std::unique_ptr<Impl> impl);

    std::unique_ptr<Impl> impl_;
};

} // namespace jalium
