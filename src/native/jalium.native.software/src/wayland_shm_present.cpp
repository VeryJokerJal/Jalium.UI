#include "wayland_shm_present.h"
#include "jalium_platform.h"

#include <wayland-client.h>

#include <algorithm>
#include <cerrno>
#include <climits>
#include <cstdlib>
#include <cstring>
#include <fcntl.h>
#include <memory>
#include <string>
#include <sys/mman.h>
#include <unistd.h>
#include <vector>

namespace jalium {
namespace {

constexpr int32_t kDamageTileSize = 64;

int CreateAnonymousFile(size_t size)
{
    if (size == 0 || size > static_cast<size_t>(INT32_MAX))
        return -1;

    const char* runtimeDirectory = std::getenv("XDG_RUNTIME_DIR");
    if (!runtimeDirectory || runtimeDirectory[0] == '\0')
        runtimeDirectory = "/tmp";

    std::string path(runtimeDirectory);
    if (!path.empty() && path.back() != '/')
        path.push_back('/');
    path.append("jalium-shm-XXXXXX");

    std::vector<char> mutablePath(path.begin(), path.end());
    mutablePath.push_back('\0');
    const int fd = mkstemp(mutablePath.data());
    if (fd < 0)
        return -1;

    // The compositor receives its own descriptor with wl_shm.create_pool, so
    // the filesystem name is never needed after mkstemp succeeds.
    unlink(mutablePath.data());
    (void)fcntl(fd, F_SETFD, FD_CLOEXEC);

    if (ftruncate(fd, static_cast<off_t>(size)) != 0)
    {
        close(fd);
        return -1;
    }
    return fd;
}

int ReadBufferCount()
{
    const char* value = std::getenv("JALIUM_WAYLAND_SHM_BUFFERS");
    if (value && std::strcmp(value, "2") == 0)
        return 2;
    return 3;
}

uint32_t ReadBufferFormat()
{
    const char* value = std::getenv("JALIUM_WAYLAND_SHM_FORMAT");
    return value && (std::strcmp(value, "xrgb") == 0 ||
                     std::strcmp(value, "XRGB8888") == 0)
        ? WL_SHM_FORMAT_XRGB8888
        : WL_SHM_FORMAT_ARGB8888;
}

} // namespace

struct WaylandShmPresenter::Impl
{
    struct Buffer
    {
        wl_buffer* proxy = nullptr;
        uint8_t* mapping = nullptr;
        size_t mappingSize = 0;
        int32_t width = 0;
        int32_t height = 0;
        int32_t stride = 0;
        bool busy = false;
        bool retired = false;
        std::vector<uint64_t> tileVersions;
    };

    wl_display* display = nullptr;
    wl_surface* surface = nullptr;
    wl_shm* shm = nullptr;
    uint32_t format = WL_SHM_FORMAT_ARGB8888;
    int bufferCount = 3;
    std::vector<std::unique_ptr<Buffer>> buffers;
    int32_t damageWidth = 0;
    int32_t damageHeight = 0;
    int32_t tileColumns = 0;
    int32_t tileRows = 0;
    uint64_t damageVersion = 1;
    std::vector<uint64_t> tileVersions;

    bool EnsureDamageGrid(int32_t width, int32_t height)
    {
        if (damageWidth == width && damageHeight == height &&
            !tileVersions.empty()) {
            return false;
        }

        damageWidth = width;
        damageHeight = height;
        tileColumns = (width + kDamageTileSize - 1) / kDamageTileSize;
        tileRows = (height + kDamageTileSize - 1) / kDamageTileSize;
        damageVersion = 1;
        tileVersions.assign(
            static_cast<size_t>(tileColumns) * tileRows, damageVersion);
        for (const auto& buffer : buffers)
            buffer->tileVersions.assign(tileVersions.size(), 0);
        return true;
    }

    void MarkDamage(int32_t left, int32_t top, int32_t right, int32_t bottom)
    {
        ++damageVersion;
        if (damageVersion == 0)
        {
            damageVersion = 1;
            std::fill(tileVersions.begin(), tileVersions.end(), 1);
            for (const auto& buffer : buffers)
                std::fill(buffer->tileVersions.begin(),
                          buffer->tileVersions.end(), 0);
        }

        const int32_t firstColumn = left / kDamageTileSize;
        const int32_t lastColumn = (right - 1) / kDamageTileSize;
        const int32_t firstRow = top / kDamageTileSize;
        const int32_t lastRow = (bottom - 1) / kDamageTileSize;
        for (int32_t tileY = firstRow; tileY <= lastRow; ++tileY)
        {
            for (int32_t tileX = firstColumn; tileX <= lastColumn; ++tileX)
            {
                tileVersions[static_cast<size_t>(tileY) * tileColumns + tileX] =
                    damageVersion;
            }
        }
    }

    static void BufferRelease(void* data, wl_buffer*)
    {
        static_cast<Buffer*>(data)->busy = false;
    }

    void DestroyBuffer(Buffer& buffer)
    {
        if (buffer.proxy)
        {
            wl_buffer_destroy(buffer.proxy);
            buffer.proxy = nullptr;
        }
        if (buffer.mapping)
        {
            munmap(buffer.mapping, buffer.mappingSize);
            buffer.mapping = nullptr;
        }
    }

    void CleanupRetiredBuffers()
    {
        for (auto iterator = buffers.begin(); iterator != buffers.end();)
        {
            Buffer& buffer = **iterator;
            if (buffer.retired && !buffer.busy)
            {
                DestroyBuffer(buffer);
                iterator = buffers.erase(iterator);
            }
            else
            {
                ++iterator;
            }
        }
    }

    Buffer* CreateBuffer(int32_t width, int32_t height)
    {
        if (width <= 0 || height <= 0 || width > INT32_MAX / 4)
            return nullptr;

        const int32_t stride = width * 4;
        if (static_cast<size_t>(height) >
            static_cast<size_t>(INT32_MAX) / static_cast<size_t>(stride))
            return nullptr;
        const size_t byteCount = static_cast<size_t>(stride) * height;

        const int fd = CreateAnonymousFile(byteCount);
        if (fd < 0)
            return nullptr;

        void* mapping = mmap(nullptr, byteCount, PROT_READ | PROT_WRITE,
                             MAP_SHARED, fd, 0);
        if (mapping == MAP_FAILED)
        {
            close(fd);
            return nullptr;
        }

        wl_shm_pool* pool = wl_shm_create_pool(shm, fd, static_cast<int32_t>(byteCount));
        close(fd);
        if (!pool)
        {
            munmap(mapping, byteCount);
            return nullptr;
        }

        auto buffer = std::make_unique<Buffer>();
        buffer->mapping = static_cast<uint8_t*>(mapping);
        buffer->mappingSize = byteCount;
        buffer->width = width;
        buffer->height = height;
        buffer->stride = stride;
        buffer->tileVersions.assign(tileVersions.size(), 0);
        buffer->proxy = wl_shm_pool_create_buffer(
            pool, 0, width, height, stride, format);
        wl_shm_pool_destroy(pool);
        if (!buffer->proxy)
        {
            munmap(mapping, byteCount);
            return nullptr;
        }

        static constexpr wl_buffer_listener bufferListener = { &BufferRelease };
        wl_buffer_add_listener(buffer->proxy, &bufferListener, buffer.get());
        Buffer* result = buffer.get();
        buffers.push_back(std::move(buffer));
        return result;
    }

    Buffer* AcquireBuffer(int32_t width, int32_t height)
    {
        for (const auto& buffer : buffers)
        {
            if (!buffer->retired &&
                (buffer->width != width || buffer->height != height))
                buffer->retired = true;
        }
        CleanupRetiredBuffers();

        int activeCount = 0;
        Buffer* available = nullptr;
        for (const auto& buffer : buffers)
        {
            if (buffer->retired)
                continue;
            ++activeCount;
            if (!buffer->busy && !available)
                available = buffer.get();
        }

        while (activeCount < bufferCount)
        {
            Buffer* created = CreateBuffer(width, height);
            if (!created)
                break;
            ++activeCount;
            if (!available)
                available = created;
        }
        return available;
    }
};

WaylandShmPresenter::WaylandShmPresenter(std::unique_ptr<Impl> impl)
    : impl_(std::move(impl))
{
}

std::unique_ptr<WaylandShmPresenter> WaylandShmPresenter::Create(
    wl_display* display, wl_surface* surface, wl_shm* shm)
{
    if (!display || !surface || !shm)
        return nullptr;

    auto impl = std::make_unique<Impl>();
    impl->display = display;
    impl->surface = surface;
    impl->shm = shm;
    impl->format = ReadBufferFormat();
    impl->bufferCount = ReadBufferCount();
    return std::unique_ptr<WaylandShmPresenter>(
        new WaylandShmPresenter(std::move(impl)));
}

WaylandShmPresenter::~WaylandShmPresenter()
{
    if (!impl_)
        return;

    // Do not attach a null buffer here: for an xdg_toplevel that would unmap
    // the window and force a new configure handshake during render-target
    // recreation. Destroying a wl_buffer proxy while the compositor retains
    // the committed storage is explicitly permitted by the protocol.
    for (const auto& buffer : impl_->buffers)
        impl_->DestroyBuffer(*buffer);
    impl_->buffers.clear();
    // wl_shm is borrowed from the platform surface descriptor.
    impl_->shm = nullptr;
}

bool WaylandShmPresenter::Present(const uint8_t* bgraPixels,
                                  int32_t width,
                                  int32_t height,
                                  int32_t sourceStride,
                                  int32_t left,
                                  int32_t top,
                                  int32_t right,
                                  int32_t bottom)
{
    if (!impl_ || !bgraPixels || width <= 0 || height <= 0 ||
        sourceStride < width * 4)
        return false;

    // xdg-shell is strict: committing the first buffer before the platform has
    // received and acknowledged xdg_surface.configure disconnects the client.
    // Treat the not-yet-configured/hidden state as a transient dropped present.
    if (!jalium_wayland_surface_is_ready(
            reinterpret_cast<intptr_t>(impl_->surface)))
        return false;

    const bool resized = impl_->EnsureDamageGrid(width, height);
    if (resized)
    {
        left = 0;
        top = 0;
        right = width;
        bottom = height;
    }
    else
    {
        left = std::clamp(left, 0, width);
        top = std::clamp(top, 0, height);
        right = std::clamp(right, left, width);
        bottom = std::clamp(bottom, top, height);
    }
    if (right <= left || bottom <= top)
        return true;
    impl_->MarkDamage(left, top, right, bottom);

    Impl::Buffer* buffer = impl_->AcquireBuffer(width, height);
    if (!buffer)
        return false;

    // A rotating buffer can be several frames behind. Compare every tile's
    // version and copy only the union of damage accumulated since this buffer
    // was last presented. Newly allocated buffers start at version zero and
    // therefore receive one complete initialization.
    for (int32_t tileY = 0; tileY < impl_->tileRows; ++tileY)
    {
        const int32_t copyTop = tileY * kDamageTileSize;
        const int32_t copyBottom = std::min(copyTop + kDamageTileSize, height);
        for (int32_t tileX = 0; tileX < impl_->tileColumns; ++tileX)
        {
            const size_t tileIndex =
                static_cast<size_t>(tileY) * impl_->tileColumns + tileX;
            if (buffer->tileVersions[tileIndex] ==
                impl_->tileVersions[tileIndex]) {
                continue;
            }

            const int32_t copyLeft = tileX * kDamageTileSize;
            const int32_t copyRight = std::min(copyLeft + kDamageTileSize, width);
            const size_t copyBytes = static_cast<size_t>(copyRight - copyLeft) * 4;
            for (int32_t y = copyTop; y < copyBottom; ++y)
            {
                const uint8_t* source = bgraPixels +
                    static_cast<size_t>(y) * sourceStride +
                    static_cast<size_t>(copyLeft) * 4;
                uint8_t* destination = buffer->mapping +
                    static_cast<size_t>(y) * buffer->stride +
                    static_cast<size_t>(copyLeft) * 4;
                std::memcpy(destination, source, copyBytes);
                if (impl_->format == WL_SHM_FORMAT_XRGB8888)
                {
                    for (int32_t x = copyLeft; x < copyRight; ++x)
                    {
                        buffer->mapping[static_cast<size_t>(y) * buffer->stride +
                            static_cast<size_t>(x) * 4 + 3] = 0xff;
                    }
                }
            }
            buffer->tileVersions[tileIndex] = impl_->tileVersions[tileIndex];
        }
    }

    buffer->busy = true;
    wl_surface_attach(impl_->surface, buffer->proxy, 0, 0);
    const uint32_t surfaceVersion = wl_proxy_get_version(
        reinterpret_cast<wl_proxy*>(impl_->surface));
    if (surfaceVersion >= WL_SURFACE_DAMAGE_BUFFER_SINCE_VERSION)
        wl_surface_damage_buffer(
            impl_->surface, left, top, right - left, bottom - top);
    else
        wl_surface_damage(
            impl_->surface, left, top, right - left, bottom - top);
    wl_surface_commit(impl_->surface);

    const int flushResult = wl_display_flush(impl_->display);
    return flushResult >= 0 || errno == EAGAIN;
}

} // namespace jalium
