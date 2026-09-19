#include "software_backend.h"
#include "jalium_scanline_rasterizer.h"   // PixelRect / RasterizePathToRects
#include "jalium_impeller_stroke.h"       // ExpandStrokePath (collect-contours mode)
#include "jalium_triangulate.h"           // FlattenPathToContours
#include <algorithm>
#include <array>
#include <cstring>
#include <cmath>
#include <cstdlib>
#include <atomic>
#include <condition_variable>
#include <chrono>
#include <functional>
#include <iterator>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <system_error>
#include <thread>
#include <unordered_map>
#include <string_view>

#if defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
#include <intrin.h>
#include <immintrin.h>
#elif defined(__AVX2__) || defined(__SSE2__)
#include <immintrin.h>
#elif defined(__aarch64__) || defined(_M_ARM64)
#include <arm_neon.h>
#endif

#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
#include "wayland_shm_present.h"
#include <wayland-client.h>
#endif

#ifdef _WIN32
#include <Windows.h>
#include <wincodec.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#endif

#ifdef __APPLE__
#import <TargetConditionals.h>
#import <CoreGraphics/CoreGraphics.h>
#import <CoreText/CoreText.h>
#import <QuartzCore/QuartzCore.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#else
#import <UIKit/UIKit.h>
#endif
#endif

#ifdef __ANDROID__
#include <android/native_window.h>
#include <android/hardware_buffer.h>
#include <android/log.h>
#define LOGI_SW(...) __android_log_print(ANDROID_LOG_INFO, "JaliumSoftware", __VA_ARGS__)
#define LOGE_SW(...) __android_log_print(ANDROID_LOG_ERROR, "JaliumSoftware", __VA_ARGS__)
#endif

#if defined(__linux__) || defined(__ANDROID__)
// stb_image for cross-platform image decoding (Software backend non-Windows)
#define STB_IMAGE_STATIC
#ifndef STB_IMAGE_IMPLEMENTATION
#define STB_IMAGE_IMPLEMENTATION
#endif
#define STBI_NO_STDIO
#define STBI_FAILURE_USERMSG
#include <stb_image.h>
#endif

#if defined(JALIUM_SOFTWARE_X11_PRESENT)
#include <X11/Xlib.h>
#include <X11/Xutil.h>
#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
#include <X11/extensions/XShm.h>
#include <sys/ipc.h>
#include <sys/shm.h>
#endif
#endif

namespace jalium {

static uint64_t SoftwareNowNs()
{
    return static_cast<uint64_t>(
        std::chrono::duration_cast<std::chrono::nanoseconds>(
            std::chrono::steady_clock::now().time_since_epoch()).count());
}

static void MaybeFailFramebufferAllocationForTesting()
{
#ifdef JALIUM_SOFTWARE_TESTING
    constexpr const char* kFailureVariable =
        "JALIUM_SOFTWARE_TEST_FAIL_FRAMEBUFFER_ALLOCATION";
    const char* value = std::getenv(kFailureVariable);
    if (value && value[0] == '1' && value[1] == '\0') {
#ifdef _WIN32
        (void)_putenv_s(kFailureVariable, "");
#else
        (void)unsetenv(kFailureVariable);
#endif
        throw std::bad_alloc();
    }
#endif
}

static size_t ComputeFramebufferGrowthCapacity(
    size_t currentCapacity,
    size_t requiredBytes)
{
    constexpr size_t kCapacityAlignment = 64u * 1024u;
    constexpr size_t kMaximumGrowthSlack = 16u * 1024u * 1024u;
    const size_t maximum = std::numeric_limits<size_t>::max();

    size_t grownCapacity = currentCapacity;
    if (grownCapacity < requiredBytes) {
        const size_t halfCapacity = currentCapacity / 2u;
        grownCapacity = currentCapacity > maximum - halfCapacity
            ? maximum
            : currentCapacity + halfCapacity;
        grownCapacity = std::max(grownCapacity, requiredBytes);
    }

    const size_t growthLimit = requiredBytes > maximum - kMaximumGrowthSlack
        ? maximum
        : requiredBytes + kMaximumGrowthSlack;
    grownCapacity = std::min(grownCapacity, growthLimit);

    if (grownCapacity <= maximum - (kCapacityAlignment - 1u)) {
        const size_t alignedCapacity =
            (grownCapacity + kCapacityAlignment - 1u) &
            ~(kCapacityAlignment - 1u);
        grownCapacity = std::min(alignedCapacity, growthLimit);
    }
    return std::max(grownCapacity, requiredBytes);
}

static constexpr size_t kRetainedCaptureBufferCacheBudget =
    32u * 1024u * 1024u;
static constexpr size_t kRetainedCaptureBufferCacheDepth = 4u;

static constexpr size_t kSoftwareResourceCacheBudget =
    48u * 1024u * 1024u;
static std::atomic<size_t> gSoftwareResourceCacheBytes{0};

static bool TryReserveSoftwareResourceCache(size_t bytes)
{
    size_t current = gSoftwareResourceCacheBytes.load(std::memory_order_relaxed);
    for (;;) {
        if (bytes > kSoftwareResourceCacheBudget ||
            current > kSoftwareResourceCacheBudget - bytes) return false;
        if (gSoftwareResourceCacheBytes.compare_exchange_weak(
                current, current + bytes,
                std::memory_order_relaxed,
                std::memory_order_relaxed)) return true;
    }
}

static void ReleaseSoftwareResourceCache(size_t bytes)
{
    if (bytes > 0)
        gSoftwareResourceCacheBytes.fetch_sub(bytes, std::memory_order_relaxed);
}

static size_t SoftwareResourceCacheBytes()
{
    return gSoftwareResourceCacheBytes.load(std::memory_order_relaxed);
}

static std::atomic<int> gSoftwareSimdMode{2}; // 0=scalar, 1=SSE2/NEON, 2=widest

static std::string ReadSoftwareEnvironmentVariable(const char* name)
{
#if defined(_MSC_VER)
    char* value = nullptr;
    size_t length = 0;
    if (_dupenv_s(&value, &length, name) != 0 || !value) {
        if (value) std::free(value);
        return {};
    }
    std::string result(value);
    std::free(value);
    return result;
#else
    const char* value = std::getenv(name);
    return value ? std::string(value) : std::string{};
#endif
}

static void RefreshSoftwareSimdMode()
{
    const std::string configured =
        ReadSoftwareEnvironmentVariable("JALIUM_SOFTWARE_SIMD");
    int mode = 2;
    if (configured == "0" ||
        configured == "scalar" ||
        configured == "SCALAR") {
        mode = 0;
    } else if (configured == "sse2" ||
               configured == "SSE2" ||
               configured == "neon" ||
               configured == "NEON") {
        mode = 1;
    }
    gSoftwareSimdMode.store(mode, std::memory_order_relaxed);
}

static bool SoftwareSimdEnabled()
{
    return gSoftwareSimdMode.load(std::memory_order_relaxed) != 0;
}

#if defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
static bool SoftwareCpuHasAvx2()
{
    if (gSoftwareSimdMode.load(std::memory_order_relaxed) != 2) return false;
    static const bool available = [] {
        int registers[4] = {};
        __cpuid(registers, 1);
        constexpr int kOsXsave = 1 << 27;
        constexpr int kAvx = 1 << 28;
        if ((registers[2] & (kOsXsave | kAvx)) != (kOsXsave | kAvx)) return false;
        const unsigned __int64 xcr0 = _xgetbv(0);
        if ((xcr0 & 0x6u) != 0x6u) return false;
        __cpuidex(registers, 7, 0);
        return (registers[1] & (1 << 5)) != 0;
    }();
    return available;
}
#endif

// A plain BGRA clear is memory-bandwidth bound and remains cheaper on the
// submitting thread at ordinary window sizes. Keeping this just above a
// 1920x1080 surface also prevents an otherwise empty 800x600 logical window
// from materializing worker stacks at common high-DPI scale factors.
constexpr uint64_t kParallelClearPixelThreshold = 2u * 1024u * 1024u;

// ============================================================================
// Persistent row worker pool
// ============================================================================

class SoftwareWorkerPool {
public:
    SoftwareWorkerPool()
    {
        const unsigned hardwareThreads = std::max(1u, std::thread::hardware_concurrency());
#ifdef __ANDROID__
        const unsigned workerLimit = 4;
#else
        const unsigned workerLimit = 8;
#endif
        maxWorkerCount_ = std::min(
            hardwareThreads > 1 ? hardwareThreads - 1 : 0u,
            workerLimit);
        const std::string threadOverride =
            ReadSoftwareEnvironmentVariable("JALIUM_SOFTWARE_THREADS");
        if (!threadOverride.empty()) {
            const char* overrideValue = threadOverride.c_str();
            char* end = nullptr;
            const long parsed = std::strtol(overrideValue, &end, 10);
            if (end != overrideValue && parsed >= 0 && parsed <= 16) {
                maxWorkerCount_ = static_cast<size_t>(parsed);
            }
        }
    }

    ~SoftwareWorkerPool()
    {
        // Backend destruction can race a final render-target release. Let an
        // in-flight dispatch finish before waking the pool for shutdown.
        std::unique_lock<std::mutex> dispatchLock(dispatchMutex_);
        {
            std::lock_guard<std::mutex> lock(stateMutex_);
            stopping_ = true;
            ++generation_;
        }
        workAvailable_.notify_all();
        for (auto& worker : workers_) {
            if (worker.joinable()) worker.join();
        }
        workerCount_.store(0, std::memory_order_relaxed);
    }

    void ParallelFor(
        int32_t begin, int32_t end, int32_t grain,
        const std::function<void(int32_t, int32_t)>& function)
    {
        if (end <= begin) return;
        grain = std::max(grain, 1);
        const int64_t itemCount = static_cast<int64_t>(end) - begin;
        if (maxWorkerCount_ == 0 || itemCount <= grain) {
            function(begin, end);
            return;
        }

        const size_t chunkCount = static_cast<size_t>(
            (itemCount + grain - 1) / grain);
        const size_t desiredWorkerCount = std::min(
            maxWorkerCount_, chunkCount > 1 ? chunkCount - 1 : 0u);
        if (desiredWorkerCount == 0) {
            function(begin, end);
            return;
        }

        // A backend owns one pool shared by all its render targets. Serialize
        // dispatch, while individual jobs still fan out over all workers.
        std::unique_lock<std::mutex> dispatchLock(dispatchMutex_);
        EnsureWorkerCount(desiredWorkerCount);
        const size_t activeWorkerCount = std::min(
            desiredWorkerCount, workers_.size());
        if (activeWorkerCount == 0) {
            function(begin, end);
            return;
        }

        const auto started = std::chrono::steady_clock::now();
        {
            std::lock_guard<std::mutex> lock(stateMutex_);
            function_ = function;
            next_.store(begin, std::memory_order_relaxed);
            end_ = end;
            grain_ = grain;
            completedWorkers_ = 0;
            activeWorkerCount_ = activeWorkerCount;
            ++generation_;
        }
        workAvailable_.notify_all();

        // The submitting/render thread participates instead of sleeping.
        Consume(function, end, grain);

        std::unique_lock<std::mutex> stateLock(stateMutex_);
        workComplete_.wait(stateLock, [this] {
            return completedWorkers_ == activeWorkerCount_;
        });
        function_ = {};
        activeWorkerCount_ = 0;
        const auto finished = std::chrono::steady_clock::now();
        totalParallelNs_.fetch_add(
            static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
                finished - started).count()),
            std::memory_order_relaxed);
    }

    bool CanParallelize() const { return maxWorkerCount_ > 0; }
    size_t WorkerCount() const
    {
        return workerCount_.load(std::memory_order_relaxed);
    }
    uint64_t TotalParallelNs() const
    {
        return totalParallelNs_.load(std::memory_order_relaxed);
    }

private:
    void EnsureWorkerCount(size_t desiredWorkerCount)
    {
        while (workers_.size() < desiredWorkerCount) {
            const size_t workerIndex = workers_.size();
            uint64_t initialGeneration = 0;
            {
                std::lock_guard<std::mutex> lock(stateMutex_);
                initialGeneration = generation_;
            }

            try {
                workers_.emplace_back(
                    [this, workerIndex, initialGeneration] {
                        WorkerLoop(workerIndex, initialGeneration);
                    });
                workerCount_.store(workers_.size(), std::memory_order_relaxed);
            } catch (const std::system_error&) {
                return;
            } catch (const std::bad_alloc&) {
                return;
            }
        }
    }

    void Consume(
        const std::function<void(int32_t, int32_t)>& function,
        int32_t end, int32_t grain)
    {
        for (;;) {
            const int32_t chunkBegin = next_.fetch_add(grain, std::memory_order_relaxed);
            if (chunkBegin >= end) return;
            function(chunkBegin, std::min(chunkBegin + grain, end));
        }
    }

    void WorkerLoop(size_t workerIndex, uint64_t observedGeneration)
    {
        for (;;) {
            std::function<void(int32_t, int32_t)> function;
            int32_t end = 0;
            int32_t grain = 1;
            {
                std::unique_lock<std::mutex> lock(stateMutex_);
                workAvailable_.wait(lock, [this, observedGeneration] {
                    return stopping_ || generation_ != observedGeneration;
                });
                if (stopping_) return;
                observedGeneration = generation_;
                if (workerIndex >= activeWorkerCount_) continue;
                function = function_;
                end = end_;
                grain = grain_;
            }

            Consume(function, end, grain);

            {
                std::lock_guard<std::mutex> lock(stateMutex_);
                ++completedWorkers_;
                if (completedWorkers_ == activeWorkerCount_) {
                    workComplete_.notify_one();
                }
            }
        }
    }

    std::vector<std::thread> workers_;
    size_t maxWorkerCount_ = 0;
    std::atomic<size_t> workerCount_{0};
    std::mutex dispatchMutex_;
    std::mutex stateMutex_;
    std::condition_variable workAvailable_;
    std::condition_variable workComplete_;
    std::function<void(int32_t, int32_t)> function_;
    std::atomic<int32_t> next_{0};
    int32_t end_ = 0;
    int32_t grain_ = 1;
    size_t completedWorkers_ = 0;
    size_t activeWorkerCount_ = 0;
    uint64_t generation_ = 0;
    bool stopping_ = false;
    std::atomic<uint64_t> totalParallelNs_{0};
};

class SoftwareRetainedLayer {
public:
    std::unique_ptr<SoftwareBitmap> bitmap;

    size_t ByteSize() const
    {
        return bitmap ? bitmap->pixels_.size() : 0u;
    }
};

struct SoftwareCoverageSpan {
    int32_t y = 0;
    int32_t x = 0;
    int32_t width = 0;
    uint8_t hits = 0;
};

class SoftwareEllipseMaskCache {
public:
    bool Matches(
        int32_t width, int32_t height,
        float centerX, float centerY,
        float radiusX, float radiusY,
        float innerRadiusX, float innerRadiusY,
        bool stroke) const
    {
        constexpr float kEpsilon = 1e-6f;
        return valid_ && width_ == width && height_ == height && stroke_ == stroke &&
            std::abs(centerX_ - centerX) < kEpsilon &&
            std::abs(centerY_ - centerY) < kEpsilon &&
            std::abs(radiusX_ - radiusX) < kEpsilon &&
            std::abs(radiusY_ - radiusY) < kEpsilon &&
            std::abs(innerRadiusX_ - innerRadiusX) < kEpsilon &&
            std::abs(innerRadiusY_ - innerRadiusY) < kEpsilon;
    }

    void Build(
        int32_t width, int32_t height,
        float centerX, float centerY,
        float radiusX, float radiusY,
        float innerRadiusX, float innerRadiusY,
        bool stroke)
    {
        valid_ = false;
        spans_.clear();
        if (width <= 0 || height <= 0 || radiusX <= 0.0f || radiusY <= 0.0f) return;

        width_ = width;
        height_ = height;
        centerX_ = centerX;
        centerY_ = centerY;
        radiusX_ = radiusX;
        radiusY_ = radiusY;
        innerRadiusX_ = innerRadiusX;
        innerRadiusY_ = innerRadiusY;
        stroke_ = stroke;
        spans_.reserve(static_cast<size_t>(height) * 8u);

        constexpr int kSub = 4;
        constexpr float kStep = 1.0f / static_cast<float>(kSub);
        const bool hasInner = stroke && innerRadiusX > 0.0f && innerRadiusY > 0.0f;

        for (int32_t py = 0; py < height; ++py) {
            int32_t runStart = 0;
            int runHits = -1;
            auto flushRun = [&](int32_t runEnd) {
                if (runHits <= 0 || runEnd <= runStart) return;
                spans_.push_back({
                    py, runStart, runEnd - runStart,
                    static_cast<uint8_t>(runHits)
                });
            };

            for (int32_t px = 0; px < width; ++px) {
                int hits = 0;
                for (int sy = 0; sy < kSub; ++sy) {
                    const float localY =
                        static_cast<float>(py) + (sy + 0.5f) * kStep - centerY;
                    const float outerY = localY / radiusY;
                    const float innerY = hasInner ? localY / innerRadiusY : 0.0f;
                    for (int sx = 0; sx < kSub; ++sx) {
                        const float localX =
                            static_cast<float>(px) + (sx + 0.5f) * kStep - centerX;
                        const float outerX = localX / radiusX;
                        const bool insideOuter = outerX * outerX + outerY * outerY <= 1.0f;
                        const float innerX = hasInner ? localX / innerRadiusX : 0.0f;
                        const bool insideInner = hasInner &&
                            innerX * innerX + innerY * innerY <= 1.0f;
                        if (insideOuter && !insideInner) ++hits;
                    }
                }

                if (hits != runHits) {
                    flushRun(px);
                    runStart = px;
                    runHits = hits;
                }
            }
            flushRun(width);
        }
        valid_ = true;
    }

    const std::vector<SoftwareCoverageSpan>& Spans() const { return spans_; }
    size_t ByteSize() const { return spans_.capacity() * sizeof(SoftwareCoverageSpan); }

private:
    std::vector<SoftwareCoverageSpan> spans_;
    int32_t width_ = 0;
    int32_t height_ = 0;
    float centerX_ = 0.0f;
    float centerY_ = 0.0f;
    float radiusX_ = 0.0f;
    float radiusY_ = 0.0f;
    float innerRadiusX_ = 0.0f;
    float innerRadiusY_ = 0.0f;
    bool stroke_ = false;
    bool valid_ = false;
};

class SoftwareRoundedRectMaskCache {
public:
    struct Mask {
        int32_t width = 0;
        int32_t height = 0;
        float originX = 0.0f;
        float originY = 0.0f;
        float shapeWidth = 0.0f;
        float shapeHeight = 0.0f;
        float topLeft = 0.0f;
        float topRight = 0.0f;
        float bottomRight = 0.0f;
        float bottomLeft = 0.0f;
        float innerOffset = 0.0f;
        float innerWidth = 0.0f;
        float innerHeight = 0.0f;
        float innerTopLeft = 0.0f;
        float innerTopRight = 0.0f;
        float innerBottomRight = 0.0f;
        float innerBottomLeft = 0.0f;
        bool stroke = false;
        std::vector<SoftwareCoverageSpan> spans;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return spans.capacity() * sizeof(SoftwareCoverageSpan) + sizeof(Mask);
        }
    };

    const Mask* GetOrBuild(
        int32_t width, int32_t height,
        float originX, float originY,
        float shapeWidth, float shapeHeight,
        float topLeft, float topRight, float bottomRight, float bottomLeft,
        float innerOffset, float innerWidth, float innerHeight,
        float innerTopLeft, float innerTopRight,
        float innerBottomRight, float innerBottomLeft,
        bool stroke)
    {
        for (auto& mask : masks_) {
            if (Matches(mask, width, height, originX, originY,
                    shapeWidth, shapeHeight,
                    topLeft, topRight, bottomRight, bottomLeft,
                    innerOffset, innerWidth, innerHeight,
                    innerTopLeft, innerTopRight,
                    innerBottomRight, innerBottomLeft, stroke)) {
                mask.lastUse = ++clock_;
                return &mask;
            }
        }

        Mask mask;
        mask.width = width;
        mask.height = height;
        mask.originX = originX;
        mask.originY = originY;
        mask.shapeWidth = shapeWidth;
        mask.shapeHeight = shapeHeight;
        mask.topLeft = topLeft;
        mask.topRight = topRight;
        mask.bottomRight = bottomRight;
        mask.bottomLeft = bottomLeft;
        mask.innerOffset = innerOffset;
        mask.innerWidth = innerWidth;
        mask.innerHeight = innerHeight;
        mask.innerTopLeft = innerTopLeft;
        mask.innerTopRight = innerTopRight;
        mask.innerBottomRight = innerBottomRight;
        mask.innerBottomLeft = innerBottomLeft;
        mask.stroke = stroke;
        mask.lastUse = ++clock_;
        Build(mask);

        constexpr size_t kBudget = 8u * 1024u * 1024u;
        constexpr size_t kMaxMasks = 128;
        const size_t bytes = mask.ByteSize();
        if (bytes > kBudget) return nullptr;
        while (!masks_.empty() &&
               (masks_.size() >= kMaxMasks || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                masks_.begin(), masks_.end(),
                [](const Mask& left, const Mask& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            masks_.erase(oldest);
        }
        bytes_ += bytes;
        masks_.push_back(std::move(mask));
        return &masks_.back();
    }

    void Clear()
    {
        masks_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return masks_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    static bool NearlyEqual(float left, float right)
    {
        return std::abs(left - right) < 1e-6f;
    }

    static bool Matches(
        const Mask& mask,
        int32_t width, int32_t height,
        float originX, float originY,
        float shapeWidth, float shapeHeight,
        float topLeft, float topRight, float bottomRight, float bottomLeft,
        float innerOffset, float innerWidth, float innerHeight,
        float innerTopLeft, float innerTopRight,
        float innerBottomRight, float innerBottomLeft,
        bool stroke)
    {
        return mask.width == width && mask.height == height && mask.stroke == stroke &&
            NearlyEqual(mask.originX, originX) && NearlyEqual(mask.originY, originY) &&
            NearlyEqual(mask.shapeWidth, shapeWidth) &&
            NearlyEqual(mask.shapeHeight, shapeHeight) &&
            NearlyEqual(mask.topLeft, topLeft) && NearlyEqual(mask.topRight, topRight) &&
            NearlyEqual(mask.bottomRight, bottomRight) &&
            NearlyEqual(mask.bottomLeft, bottomLeft) &&
            NearlyEqual(mask.innerOffset, innerOffset) &&
            NearlyEqual(mask.innerWidth, innerWidth) &&
            NearlyEqual(mask.innerHeight, innerHeight) &&
            NearlyEqual(mask.innerTopLeft, innerTopLeft) &&
            NearlyEqual(mask.innerTopRight, innerTopRight) &&
            NearlyEqual(mask.innerBottomRight, innerBottomRight) &&
            NearlyEqual(mask.innerBottomLeft, innerBottomLeft);
    }

    static bool InsideRoundedRect(
        float x, float y, float width, float height,
        float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        if (x < 0.0f || x > width || y < 0.0f || y > height) return false;
        if (topLeft > 0.0f && x < topLeft && y < topLeft) {
            const float dx = (x - topLeft) / topLeft;
            const float dy = (y - topLeft) / topLeft;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (topRight > 0.0f && x > width - topRight && y < topRight) {
            const float dx = (x - (width - topRight)) / topRight;
            const float dy = (y - topRight) / topRight;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (bottomRight > 0.0f && x > width - bottomRight && y > height - bottomRight) {
            const float dx = (x - (width - bottomRight)) / bottomRight;
            const float dy = (y - (height - bottomRight)) / bottomRight;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (bottomLeft > 0.0f && x < bottomLeft && y > height - bottomLeft) {
            const float dx = (x - bottomLeft) / bottomLeft;
            const float dy = (y - (height - bottomLeft)) / bottomLeft;
            return dx * dx + dy * dy <= 1.0f;
        }
        return true;
    }

    static void Build(Mask& mask)
    {
        constexpr int kSub = 4;
        constexpr float kStep = 1.0f / static_cast<float>(kSub);
        mask.spans.reserve(static_cast<size_t>(mask.height) * 8u);
        for (int32_t py = 0; py < mask.height; ++py) {
            int32_t runStart = 0;
            int runHits = -1;
            auto flushRun = [&](int32_t runEnd) {
                if (runHits <= 0 || runEnd <= runStart) return;
                mask.spans.push_back({
                    py, runStart, runEnd - runStart,
                    static_cast<uint8_t>(runHits)
                });
            };
            for (int32_t px = 0; px < mask.width; ++px) {
                int hits = 0;
                for (int sy = 0; sy < kSub; ++sy) {
                    const float y = static_cast<float>(py) +
                        (sy + 0.5f) * kStep - mask.originY;
                    for (int sx = 0; sx < kSub; ++sx) {
                        const float x = static_cast<float>(px) +
                            (sx + 0.5f) * kStep - mask.originX;
                        if (!InsideRoundedRect(
                                x, y, mask.shapeWidth, mask.shapeHeight,
                                mask.topLeft, mask.topRight,
                                mask.bottomRight, mask.bottomLeft)) continue;
                        const bool insideInner = mask.stroke &&
                            mask.innerWidth > 0.0f && mask.innerHeight > 0.0f &&
                            InsideRoundedRect(
                                x - mask.innerOffset, y - mask.innerOffset,
                                mask.innerWidth, mask.innerHeight,
                                mask.innerTopLeft, mask.innerTopRight,
                                mask.innerBottomRight, mask.innerBottomLeft);
                        if (!insideInner) ++hits;
                    }
                }
                if (hits != runHits) {
                    flushRun(px);
                    runStart = px;
                    runHits = hits;
                }
            }
            flushRun(mask.width);
        }
    }

    std::vector<Mask> masks_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwareEllipticalRoundedRectMaskCache {
public:
    struct Mask {
        int32_t width = 0;
        int32_t height = 0;
        float originX = 0.0f;
        float originY = 0.0f;
        float shapeWidth = 0.0f;
        float shapeHeight = 0.0f;
        float radiusX = 0.0f;
        float radiusY = 0.0f;
        std::vector<SoftwareCoverageSpan> spans;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return sizeof(Mask) + spans.capacity() * sizeof(SoftwareCoverageSpan);
        }
    };

    const Mask* GetOrBuild(
        int32_t width, int32_t height,
        float originX, float originY,
        float shapeWidth, float shapeHeight,
        float radiusX, float radiusY)
    {
        for (auto& mask : masks_) {
            if (mask.width == width && mask.height == height &&
                NearlyEqual(mask.originX, originX) &&
                NearlyEqual(mask.originY, originY) &&
                NearlyEqual(mask.shapeWidth, shapeWidth) &&
                NearlyEqual(mask.shapeHeight, shapeHeight) &&
                NearlyEqual(mask.radiusX, radiusX) &&
                NearlyEqual(mask.radiusY, radiusY)) {
                mask.lastUse = ++clock_;
                return &mask;
            }
        }

        Mask mask;
        mask.width = width;
        mask.height = height;
        mask.originX = originX;
        mask.originY = originY;
        mask.shapeWidth = shapeWidth;
        mask.shapeHeight = shapeHeight;
        mask.radiusX = radiusX;
        mask.radiusY = radiusY;
        mask.lastUse = ++clock_;
        Build(mask);

        constexpr size_t kBudget = 8u * 1024u * 1024u;
        constexpr size_t kMaxMasks = 128;
        const size_t bytes = mask.ByteSize();
        if (bytes > kBudget) return nullptr;
        while (!masks_.empty() &&
               (masks_.size() >= kMaxMasks || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                masks_.begin(), masks_.end(),
                [](const Mask& left, const Mask& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            masks_.erase(oldest);
        }
        bytes_ += bytes;
        masks_.push_back(std::move(mask));
        return &masks_.back();
    }

    void Clear()
    {
        masks_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return masks_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    static bool NearlyEqual(float left, float right)
    {
        return std::abs(left - right) < 1e-6f;
    }

    static bool Contains(const Mask& mask, float x, float y)
    {
        if (x < 0.0f || x > mask.shapeWidth ||
            y < 0.0f || y > mask.shapeHeight) return false;
        if (mask.radiusX <= 0.0f || mask.radiusY <= 0.0f) return true;
        if (x < mask.radiusX && y < mask.radiusY) {
            const float dx = (x - mask.radiusX) / mask.radiusX;
            const float dy = (y - mask.radiusY) / mask.radiusY;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (x > mask.shapeWidth - mask.radiusX && y < mask.radiusY) {
            const float dx =
                (x - (mask.shapeWidth - mask.radiusX)) / mask.radiusX;
            const float dy = (y - mask.radiusY) / mask.radiusY;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (x < mask.radiusX && y > mask.shapeHeight - mask.radiusY) {
            const float dx = (x - mask.radiusX) / mask.radiusX;
            const float dy =
                (y - (mask.shapeHeight - mask.radiusY)) / mask.radiusY;
            return dx * dx + dy * dy <= 1.0f;
        }
        if (x > mask.shapeWidth - mask.radiusX &&
            y > mask.shapeHeight - mask.radiusY) {
            const float dx =
                (x - (mask.shapeWidth - mask.radiusX)) / mask.radiusX;
            const float dy =
                (y - (mask.shapeHeight - mask.radiusY)) / mask.radiusY;
            return dx * dx + dy * dy <= 1.0f;
        }
        return true;
    }

    static void Build(Mask& mask)
    {
        constexpr int kSub = 4;
        constexpr float kStep = 1.0f / static_cast<float>(kSub);
        mask.spans.reserve(static_cast<size_t>(mask.height) * 8u);
        for (int32_t py = 0; py < mask.height; ++py) {
            int32_t runStart = 0;
            int runHits = -1;
            auto flushRun = [&](int32_t runEnd) {
                if (runHits > 0 && runEnd > runStart) {
                    mask.spans.push_back({
                        py, runStart, runEnd - runStart,
                        static_cast<uint8_t>(runHits) });
                }
            };
            for (int32_t px = 0; px < mask.width; ++px) {
                int hits = 0;
                for (int sy = 0; sy < kSub; ++sy) {
                    const float localY = static_cast<float>(py) +
                        (sy + 0.5f) * kStep - mask.originY;
                    for (int sx = 0; sx < kSub; ++sx) {
                        const float localX = static_cast<float>(px) +
                            (sx + 0.5f) * kStep - mask.originX;
                        if (Contains(mask, localX, localY)) ++hits;
                    }
                }
                if (hits != runHits) {
                    flushRun(px);
                    runStart = px;
                    runHits = hits;
                }
            }
            flushRun(mask.width);
        }
    }

    std::vector<Mask> masks_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwarePathRasterCache {
public:
    struct Entry {
        float startX = 0.0f;
        float startY = 0.0f;
        float transform[6] = {};
        int32_t fillRule = 0;
        bool stroke = false;
        float strokeWidth = 0.0f;
        bool closed = false;
        int32_t lineJoin = 0;
        float miterLimit = 0.0f;
        int32_t lineCap = 0;
        float dashOffset = 0.0f;
        std::vector<float> commands;
        std::vector<float> dashPattern;
        std::vector<PixelRect> rects;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return sizeof(Entry) + commands.capacity() * sizeof(float) +
                dashPattern.capacity() * sizeof(float) +
                rects.capacity() * sizeof(PixelRect);
        }
    };

    const Entry* Find(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const SoftwareTransform& transform, int32_t fillRule)
    {
        for (auto& entry : entries_) {
            if (entry.stroke || entry.startX != startX || entry.startY != startY ||
                entry.fillRule != fillRule ||
                entry.commands.size() != commandLength ||
                std::memcmp(entry.transform, transform.m, sizeof(entry.transform)) != 0) {
                continue;
            }
            if (commandLength > 0 &&
                std::memcmp(entry.commands.data(), commands,
                    static_cast<size_t>(commandLength) * sizeof(float)) != 0) {
                continue;
            }
            entry.lastUse = ++clock_;
            return &entry;
        }
        return nullptr;
    }

    const Entry* Store(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const SoftwareTransform& transform, int32_t fillRule,
        std::vector<PixelRect> rects)
    {
        Entry entry;
        entry.startX = startX;
        entry.startY = startY;
        std::memcpy(entry.transform, transform.m, sizeof(entry.transform));
        entry.fillRule = fillRule;
        entry.stroke = false;
        if (commandLength > 0) {
            entry.commands.assign(commands, commands + commandLength);
        }
        entry.rects = std::move(rects);
        entry.lastUse = ++clock_;

        constexpr size_t kBudget = 16u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 256;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return nullptr;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
        return &entries_.back();
    }

    const Entry* FindStroke(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const SoftwareTransform& transform,
        float strokeWidth, bool closed, int32_t lineJoin,
        float miterLimit, int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset)
    {
        for (auto& entry : entries_) {
            if (!entry.stroke || entry.startX != startX || entry.startY != startY ||
                entry.strokeWidth != strokeWidth || entry.closed != closed ||
                entry.lineJoin != lineJoin || entry.miterLimit != miterLimit ||
                entry.lineCap != lineCap || entry.dashOffset != dashOffset ||
                entry.commands.size() != commandLength ||
                entry.dashPattern.size() != dashCount ||
                std::memcmp(entry.transform, transform.m, sizeof(entry.transform)) != 0) {
                continue;
            }
            if (commandLength > 0 &&
                std::memcmp(entry.commands.data(), commands,
                    static_cast<size_t>(commandLength) * sizeof(float)) != 0) continue;
            if (dashCount > 0 &&
                std::memcmp(entry.dashPattern.data(), dashPattern,
                    static_cast<size_t>(dashCount) * sizeof(float)) != 0) continue;
            entry.lastUse = ++clock_;
            return &entry;
        }
        return nullptr;
    }

    const Entry* StoreStroke(
        float startX, float startY,
        const float* commands, uint32_t commandLength,
        const SoftwareTransform& transform,
        float strokeWidth, bool closed, int32_t lineJoin,
        float miterLimit, int32_t lineCap,
        const float* dashPattern, uint32_t dashCount, float dashOffset,
        std::vector<PixelRect> rects)
    {
        Entry entry;
        entry.startX = startX;
        entry.startY = startY;
        std::memcpy(entry.transform, transform.m, sizeof(entry.transform));
        entry.stroke = true;
        entry.strokeWidth = strokeWidth;
        entry.closed = closed;
        entry.lineJoin = lineJoin;
        entry.miterLimit = miterLimit;
        entry.lineCap = lineCap;
        entry.dashOffset = dashOffset;
        if (commandLength > 0)
            entry.commands.assign(commands, commands + commandLength);
        if (dashCount > 0)
            entry.dashPattern.assign(dashPattern, dashPattern + dashCount);
        entry.rects = std::move(rects);
        entry.lastUse = ++clock_;
        return Insert(std::move(entry));
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    const Entry* Insert(Entry entry)
    {
        constexpr size_t kBudget = 16u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 256;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return nullptr;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
        return &entries_.back();
    }

    std::vector<Entry> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwareLineRasterCache {
public:
    struct Span {
        int32_t y = 0;
        int32_t x = 0;
        int32_t width = 0;
        float coverage = 0.0f;
    };

    struct Entry {
        float x1 = 0.0f;
        float y1 = 0.0f;
        float x2 = 0.0f;
        float y2 = 0.0f;
        float halfWidth = 0.0f;
        int32_t targetWidth = 0;
        int32_t targetHeight = 0;
        std::vector<Span> spans;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return sizeof(Entry) + spans.capacity() * sizeof(Span);
        }
    };

    const Entry* Find(
        float x1, float y1, float x2, float y2, float halfWidth,
        int32_t targetWidth, int32_t targetHeight)
    {
        for (auto& entry : entries_) {
            if (entry.x1 != x1 || entry.y1 != y1 ||
                entry.x2 != x2 || entry.y2 != y2 ||
                entry.halfWidth != halfWidth ||
                entry.targetWidth != targetWidth ||
                entry.targetHeight != targetHeight) continue;
            entry.lastUse = ++clock_;
            return &entry;
        }
        return nullptr;
    }

    const Entry* Store(Entry entry)
    {
        constexpr size_t kBudget = 8u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 512;
        entry.lastUse = ++clock_;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return nullptr;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
        return &entries_.back();
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    std::vector<Entry> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwareTextMaskCache {
public:
    struct CoverageRun {
        int32_t y = 0;
        int32_t x = 0;
        int32_t width = 0;
    };

    struct Key {
        std::wstring text;
        std::wstring fontFamily;
        int32_t pixelWidth = 0;
        int32_t pixelHeight = 0;
        int32_t fontHeight = 0;
        int32_t fontWeight = 0;
        int32_t fontStyle = 0;
        int32_t alignment = 0;

        bool operator==(const Key& other) const
        {
            return pixelWidth == other.pixelWidth && pixelHeight == other.pixelHeight &&
                fontHeight == other.fontHeight && fontWeight == other.fontWeight &&
                fontStyle == other.fontStyle && alignment == other.alignment &&
                text == other.text && fontFamily == other.fontFamily;
        }
    };

    struct KeyView {
        std::wstring_view text;
        std::wstring_view fontFamily;
        int32_t pixelWidth = 0;
        int32_t pixelHeight = 0;
        int32_t fontHeight = 0;
        int32_t fontWeight = 0;
        int32_t fontStyle = 0;
        int32_t alignment = 0;
    };

    struct KeyHash {
        using is_transparent = void;

        size_t operator()(const Key& key) const
        {
            return Hash(
                key.text, key.fontFamily,
                key.pixelWidth, key.pixelHeight, key.fontHeight,
                key.fontWeight, key.fontStyle, key.alignment);
        }

        size_t operator()(const KeyView& key) const
        {
            return Hash(
                key.text, key.fontFamily,
                key.pixelWidth, key.pixelHeight, key.fontHeight,
                key.fontWeight, key.fontStyle, key.alignment);
        }

    private:
        static size_t Hash(
            std::wstring_view text,
            std::wstring_view fontFamily,
            int32_t pixelWidth,
            int32_t pixelHeight,
            int32_t fontHeight,
            int32_t fontWeight,
            int32_t fontStyle,
            int32_t alignment)
        {
            size_t hash = std::hash<std::wstring_view>{}(text);
            auto combine = [&](size_t value) {
                hash ^= value + static_cast<size_t>(0x9e3779b9u) +
                    (hash << 6) + (hash >> 2);
            };
            combine(std::hash<std::wstring_view>{}(fontFamily));
            combine(std::hash<int32_t>{}(pixelWidth));
            combine(std::hash<int32_t>{}(pixelHeight));
            combine(std::hash<int32_t>{}(fontHeight));
            combine(std::hash<int32_t>{}(fontWeight));
            combine(std::hash<int32_t>{}(fontStyle));
            combine(std::hash<int32_t>{}(alignment));
            return hash;
        }
    };

    struct KeyEqual {
        using is_transparent = void;

        bool operator()(const Key& left, const Key& right) const
        {
            return left == right;
        }

        bool operator()(const Key& left, const KeyView& right) const
        {
            return Equals(left, right);
        }

        bool operator()(const KeyView& left, const Key& right) const
        {
            return Equals(right, left);
        }

    private:
        static bool Equals(const Key& left, const KeyView& right)
        {
            return left.pixelWidth == right.pixelWidth &&
                left.pixelHeight == right.pixelHeight &&
                left.fontHeight == right.fontHeight &&
                left.fontWeight == right.fontWeight &&
                left.fontStyle == right.fontStyle &&
                left.alignment == right.alignment &&
                std::wstring_view(left.text) == right.text &&
                std::wstring_view(left.fontFamily) == right.fontFamily;
        }
    };

    struct Entry {
        int32_t x = 0;
        int32_t y = 0;
        int32_t width = 0;
        int32_t height = 0;
        std::vector<uint16_t> channelSums;
        std::vector<CoverageRun> coverageRuns;
        uint64_t cacheId = 0;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return channelSums.capacity() * sizeof(uint16_t) +
                coverageRuns.capacity() * sizeof(CoverageRun) + sizeof(Entry);
        }
    };

    Entry* Find(const KeyView& key)
    {
#if defined(__cpp_lib_generic_unordered_lookup) && \
    __cpp_lib_generic_unordered_lookup >= 201811L
        auto iterator = entries_.find(key);
#else
        // Heterogeneous unordered lookup was standardized in C++20, but the
        // Ubuntu 20.04 baseline ships libstdc++ 9 without that overload. Keep
        // the lookup allocation-free on that baseline instead of materializing
        // two temporary std::wstring instances for every text draw.
        const KeyEqual equals;
        auto iterator = std::find_if(
            entries_.begin(), entries_.end(),
            [&](const auto& candidate) { return equals(candidate.first, key); });
#endif
        if (iterator == entries_.end()) return nullptr;
        iterator->second.lastUse = ++clock_;
        return &iterator->second;
    }

    void Insert(Key key, Entry entry)
    {
        constexpr size_t kBudget = 32u * 1024u * 1024u;
        entry.cacheId = ++nextCacheId_;
        entry.lastUse = ++clock_;
        const size_t entryBytes = entry.ByteSize();
        auto existing = entries_.find(key);
        if (existing != entries_.end()) {
            bytes_ -= existing->second.ByteSize();
            entries_.erase(existing);
        }
        while (!entries_.empty() && bytes_ + entryBytes > kBudget) {
            auto oldest = entries_.begin();
            for (auto iterator = std::next(entries_.begin());
                 iterator != entries_.end(); ++iterator) {
                if (iterator->second.lastUse < oldest->second.lastUse) oldest = iterator;
            }
            bytes_ -= oldest->second.ByteSize();
            entries_.erase(oldest);
        }
        if (entryBytes > kBudget) return;
        bytes_ += entryBytes;
        entries_.emplace(std::move(key), std::move(entry));
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    std::unordered_map<Key, Entry, KeyHash, KeyEqual> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
    uint64_t nextCacheId_ = 0;
};

class SoftwareTextCompositeCache {
public:
    struct Entry {
        uint64_t maskId = 0;
        uint8_t red = 0;
        uint8_t green = 0;
        uint8_t blue = 0;
        uint8_t alpha = 0;
        float phaseX = 0.0f;
        float phaseY = 0.0f;
        int32_t x = 0;
        int32_t y = 0;
        int32_t width = 0;
        int32_t height = 0;
        bool hasClip = false;
        SoftwareClipRect clip{};
        std::vector<SoftwareClipRect> roundedClips;
        std::vector<uint8_t> contextPixels;
        std::vector<uint8_t> outputPixels;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return sizeof(Entry) +
                roundedClips.capacity() * sizeof(SoftwareClipRect) +
                contextPixels.capacity() + outputPixels.capacity();
        }
    };

    bool TryApply(
        uint64_t maskId,
        uint8_t red, uint8_t green, uint8_t blue, uint8_t alpha,
        float phaseX, float phaseY,
        int32_t x, int32_t y, int32_t width, int32_t height,
        const SoftwareClipRect* clip,
        const std::vector<SoftwareClipRect>& roundedClips,
        SoftwareFramebuffer& framebuffer)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > framebuffer.width || y + height > framebuffer.height)
            return false;
        const size_t rowBytes = static_cast<size_t>(width) * 4u;
        const size_t bytes = rowBytes * static_cast<size_t>(height);
        for (auto& entry : entries_) {
            if (entry.maskId != maskId || entry.red != red ||
                entry.green != green || entry.blue != blue || entry.alpha != alpha ||
                entry.phaseX != phaseX || entry.phaseY != phaseY ||
                entry.x != x || entry.y != y || entry.width != width ||
                entry.height != height || entry.hasClip != (clip != nullptr) ||
                (clip && !ClipEquals(entry.clip, *clip)) ||
                !RoundedClipsEqual(entry.roundedClips, roundedClips) ||
                entry.contextPixels.size() != bytes ||
                entry.outputPixels.size() != bytes) continue;

            bool matches = true;
            for (int32_t row = 0; row < height; ++row) {
                const uint8_t* current = framebuffer.pixels.data() +
                    (static_cast<size_t>(y + row) * framebuffer.width + x) * 4u;
                const uint8_t* expected = entry.contextPixels.data() +
                    static_cast<size_t>(row) * rowBytes;
                if (std::memcmp(current, expected, rowBytes) != 0) {
                    matches = false;
                    break;
                }
            }
            if (!matches) continue;

            for (int32_t row = 0; row < height; ++row) {
                uint8_t* destination = framebuffer.pixels.data() +
                    (static_cast<size_t>(y + row) * framebuffer.width + x) * 4u;
                const uint8_t* source = entry.outputPixels.data() +
                    static_cast<size_t>(row) * rowBytes;
                std::memcpy(destination, source, rowBytes);
            }
            entry.lastUse = ++clock_;
            return true;
        }
        return false;
    }

    void Store(Entry entry)
    {
        constexpr size_t kBudget = 16u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 1024;
        entry.lastUse = ++clock_;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    static bool ClipEquals(
        const SoftwareClipRect& left, const SoftwareClipRect& right)
    {
        return left.x == right.x && left.y == right.y &&
            left.w == right.w && left.h == right.h &&
            left.radiusTL == right.radiusTL && left.radiusTR == right.radiusTR &&
            left.radiusBR == right.radiusBR && left.radiusBL == right.radiusBL &&
            left.rx == right.rx && left.ry == right.ry &&
            left.ownsRounded == right.ownsRounded;
    }

    static bool RoundedClipsEqual(
        const std::vector<SoftwareClipRect>& left,
        const std::vector<SoftwareClipRect>& right)
    {
        if (left.size() != right.size()) return false;
        for (size_t index = 0; index < left.size(); ++index) {
            if (!ClipEquals(left[index], right[index])) return false;
        }
        return true;
    }

    std::vector<Entry> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwareBackdropCache {
public:
    struct Entry {
        JaliumBackdropMaterialDesc descriptor{};
        float transform[6] = {};
        float ambientOpacity = 1.0f;
        bool hasClip = false;
        SoftwareClipRect clip{};
        int32_t panelX = 0;
        int32_t panelY = 0;
        int32_t panelWidth = 0;
        int32_t panelHeight = 0;
        std::vector<uint8_t> sourcePixels;
        std::vector<uint8_t> outputPixels;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return sourcePixels.capacity() + outputPixels.capacity() + sizeof(Entry);
        }
    };

    Entry* Find(
        const JaliumBackdropMaterialDesc& descriptor,
        const SoftwareTransform& transform,
        float ambientOpacity,
        const SoftwareClipRect* clip,
        const std::vector<uint8_t>& sourcePixels)
    {
        for (auto& entry : entries_) {
            if (std::memcmp(&entry.descriptor, &descriptor, sizeof(descriptor)) != 0 ||
                std::memcmp(entry.transform, transform.m, sizeof(entry.transform)) != 0 ||
                entry.ambientOpacity != ambientOpacity ||
                entry.hasClip != (clip != nullptr) ||
                (clip && !ClipEquals(entry.clip, *clip)) ||
                entry.sourcePixels.size() != sourcePixels.size()) {
                continue;
            }
            if (!sourcePixels.empty() &&
                std::memcmp(entry.sourcePixels.data(), sourcePixels.data(), sourcePixels.size()) != 0) {
                continue;
            }
            entry.lastUse = ++clock_;
            return &entry;
        }
        return nullptr;
    }

    void Store(Entry entry)
    {
        constexpr size_t kBudget = 24u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 16;
        entry.lastUse = ++clock_;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }

private:
    static bool ClipEquals(const SoftwareClipRect& left, const SoftwareClipRect& right)
    {
        return left.x == right.x && left.y == right.y &&
            left.w == right.w && left.h == right.h &&
            left.radiusTL == right.radiusTL && left.radiusTR == right.radiusTR &&
            left.radiusBR == right.radiusBR && left.radiusBL == right.radiusBL &&
            left.ownsRounded == right.ownsRounded;
    }

    std::vector<Entry> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
};

class SoftwareEffectResultCache {
public:
    struct Entry {
        std::vector<uint8_t> key;
        std::vector<uint8_t> sourcePixels;
        std::vector<uint8_t> contextPixels;
        std::vector<uint8_t> outputPixels;
        std::weak_ptr<const SoftwareGradientRaster> gradientRaster;
        std::weak_ptr<const SoftwareScaledBitmap> scaledBitmap;
        int32_t panelX = 0;
        int32_t panelY = 0;
        int32_t panelWidth = 0;
        int32_t panelHeight = 0;
        uint64_t lastUse = 0;

        size_t ByteSize() const
        {
            return key.capacity() + sourcePixels.capacity() + contextPixels.capacity() +
                outputPixels.capacity() + sizeof(Entry);
        }
    };

    Entry* Find(
        const std::vector<uint8_t>& key,
        const std::vector<uint8_t>& sourcePixels)
    {
        for (auto& entry : entries_) {
            if (entry.key != key || entry.sourcePixels.size() != sourcePixels.size()) continue;
            if (!sourcePixels.empty() &&
                std::memcmp(entry.sourcePixels.data(), sourcePixels.data(), sourcePixels.size()) != 0) {
                continue;
            }
            entry.lastUse = ++clock_;
            ++hits_;
            return &entry;
        }
        ++misses_;
        return nullptr;
    }

    Entry* FindComposited(
        const std::vector<uint8_t>& key,
        const std::vector<uint8_t>& sourcePixels,
        const SoftwareFramebuffer& context,
        int32_t panelX, int32_t panelY,
        int32_t panelWidth, int32_t panelHeight)
    {
        if (panelX < 0 || panelY < 0 || panelWidth <= 0 || panelHeight <= 0 ||
            panelX + panelWidth > context.width ||
            panelY + panelHeight > context.height) {
            ++misses_;
            return nullptr;
        }
        const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
        const size_t expectedBytes = rowBytes * static_cast<size_t>(panelHeight);
        for (auto& entry : entries_) {
            if (entry.key != key || entry.panelX != panelX || entry.panelY != panelY ||
                entry.panelWidth != panelWidth || entry.panelHeight != panelHeight ||
                entry.sourcePixels.size() != sourcePixels.size() ||
                entry.contextPixels.size() != expectedBytes ||
                entry.outputPixels.size() != expectedBytes) continue;
            if (!sourcePixels.empty() &&
                std::memcmp(entry.sourcePixels.data(), sourcePixels.data(),
                    sourcePixels.size()) != 0) continue;

            bool contextMatches = true;
            for (int32_t row = 0; row < panelHeight; ++row) {
                const uint8_t* current = context.pixels.data() +
                    (static_cast<size_t>(panelY + row) * context.width + panelX) * 4u;
                const uint8_t* cached = entry.contextPixels.data() +
                    static_cast<size_t>(row) * rowBytes;
                if (std::memcmp(current, cached, rowBytes) != 0) {
                    contextMatches = false;
                    break;
                }
            }
            if (!contextMatches) continue;
            entry.lastUse = ++clock_;
            ++hits_;
            return &entry;
        }
        ++misses_;
        return nullptr;
    }

    Entry* FindGradientComposited(
        const std::vector<uint8_t>& key,
        const std::shared_ptr<const SoftwareGradientRaster>& raster,
        const SoftwareFramebuffer& context,
        int32_t panelX, int32_t panelY,
        int32_t panelWidth, int32_t panelHeight)
    {
        if (!raster || panelX < 0 || panelY < 0 ||
            panelWidth <= 0 || panelHeight <= 0 ||
            panelX + panelWidth > context.width ||
            panelY + panelHeight > context.height) {
            ++misses_;
            return nullptr;
        }
        const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
        const size_t expectedBytes = rowBytes * static_cast<size_t>(panelHeight);
        for (auto& entry : entries_) {
            auto cachedRaster = entry.gradientRaster.lock();
            if (!cachedRaster || cachedRaster.get() != raster.get() ||
                entry.key != key || entry.panelX != panelX || entry.panelY != panelY ||
                entry.panelWidth != panelWidth || entry.panelHeight != panelHeight ||
                entry.contextPixels.size() != expectedBytes ||
                entry.outputPixels.size() != expectedBytes) continue;

            bool contextMatches = true;
            for (int32_t row = 0; row < panelHeight; ++row) {
                const uint8_t* current = context.pixels.data() +
                    (static_cast<size_t>(panelY + row) * context.width + panelX) * 4u;
                const uint8_t* cached = entry.contextPixels.data() +
                    static_cast<size_t>(row) * rowBytes;
                if (std::memcmp(current, cached, rowBytes) != 0) {
                    contextMatches = false;
                    break;
                }
            }
            if (!contextMatches) continue;
            entry.lastUse = ++clock_;
            ++hits_;
            return &entry;
        }
        ++misses_;
        return nullptr;
    }

    Entry* FindBitmapComposited(
        const std::vector<uint8_t>& key,
        const std::shared_ptr<const SoftwareScaledBitmap>& bitmap,
        const SoftwareFramebuffer& context,
        int32_t panelX, int32_t panelY,
        int32_t panelWidth, int32_t panelHeight)
    {
        if (!bitmap || panelX < 0 || panelY < 0 ||
            panelWidth <= 0 || panelHeight <= 0 ||
            panelX + panelWidth > context.width ||
            panelY + panelHeight > context.height) {
            ++misses_;
            return nullptr;
        }
        const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
        const size_t expectedBytes = rowBytes * static_cast<size_t>(panelHeight);
        for (auto& entry : entries_) {
            auto cachedBitmap = entry.scaledBitmap.lock();
            if (!cachedBitmap || cachedBitmap.get() != bitmap.get() ||
                entry.key != key || entry.panelX != panelX || entry.panelY != panelY ||
                entry.panelWidth != panelWidth || entry.panelHeight != panelHeight ||
                entry.contextPixels.size() != expectedBytes ||
                entry.outputPixels.size() != expectedBytes) continue;

            bool contextMatches = true;
            for (int32_t row = 0; row < panelHeight; ++row) {
                const uint8_t* current = context.pixels.data() +
                    (static_cast<size_t>(panelY + row) * context.width + panelX) * 4u;
                const uint8_t* cached = entry.contextPixels.data() +
                    static_cast<size_t>(row) * rowBytes;
                if (std::memcmp(current, cached, rowBytes) != 0) {
                    contextMatches = false;
                    break;
                }
            }
            if (!contextMatches) continue;
            entry.lastUse = ++clock_;
            ++hits_;
            return &entry;
        }
        ++misses_;
        return nullptr;
    }

    void Store(Entry entry)
    {
        constexpr size_t kBudget = 32u * 1024u * 1024u;
        constexpr size_t kMaxEntries = 32;
        entry.lastUse = ++clock_;
        const size_t bytes = entry.ByteSize();
        if (bytes > kBudget) return;
        while (!entries_.empty() &&
               (entries_.size() >= kMaxEntries || bytes_ + bytes > kBudget)) {
            auto oldest = std::min_element(
                entries_.begin(), entries_.end(),
                [](const Entry& left, const Entry& right) {
                    return left.lastUse < right.lastUse;
                });
            bytes_ -= oldest->ByteSize();
            entries_.erase(oldest);
        }
        bytes_ += bytes;
        entries_.push_back(std::move(entry));
    }

    void Clear()
    {
        entries_.clear();
        bytes_ = 0;
        hits_ = 0;
        misses_ = 0;
    }

    size_t EntryCount() const { return entries_.size(); }
    size_t ByteSize() const { return bytes_; }
    uint64_t Hits() const { return hits_; }
    uint64_t Misses() const { return misses_; }

private:
    std::vector<Entry> entries_;
    size_t bytes_ = 0;
    uint64_t clock_ = 0;
    uint64_t hits_ = 0;
    uint64_t misses_ = 0;
};

// ============================================================================
// Utility
// ============================================================================

static inline uint8_t FloatToU8(float v) {
    return (uint8_t)(std::clamp(v, 0.0f, 1.0f) * 255.0f + 0.5f);
}

static uint8_t TextMaskAlpha(uint16_t channelSum, uint8_t paintAlpha)
{
    // GDI masks hold B+G+R in [0,765]. Precompute the exact historical float
    // expression for every paint alpha so cached-text composition performs one
    // indexed load rather than two divisions and a clamp per ink pixel.
    static const std::array<std::array<uint8_t, 766>, 256> table = [] {
        std::array<std::array<uint8_t, 766>, 256> result{};
        for (size_t alpha = 0; alpha < result.size(); ++alpha) {
            for (size_t sum = 0; sum < result[alpha].size(); ++sum) {
                const float luminance = static_cast<float>(sum) / 3.0f;
                result[alpha][sum] = static_cast<uint8_t>(std::clamp(
                    (luminance / 255.0f) * static_cast<float>(alpha) + 0.5f,
                    0.0f, 255.0f));
            }
        }
        return result;
    }();
    return table[paintAlpha][std::min<uint16_t>(channelSum, 765u)];
}

static inline float Lerp(float a, float b, float t) {
    return a + (b - a) * t;
}

// sRGB ↔ linear conversion for perceptually correct blending and gradients.
static inline float SrgbToLinear(float s) {
    return (s <= 0.04045f) ? s / 12.92f : std::pow((s + 0.055f) / 1.055f, 2.4f);
}

static inline float LinearToSrgb(float l) {
    return (l <= 0.0031308f) ? l * 12.92f : 1.055f * std::pow(l, 1.0f / 2.4f) - 0.055f;
}

static void InterpolateGradientStops(const std::vector<JaliumGradientStop>& stops, float t,
                                      float& r, float& g, float& b, float& a)
{
    if (stops.empty()) { r = g = b = a = 0; return; }
    t = std::clamp(t, 0.0f, 1.0f);

    if (t <= stops.front().position) {
        r = LinearToSrgb(stops.front().r);
        g = LinearToSrgb(stops.front().g);
        b = LinearToSrgb(stops.front().b);
        a = stops.front().a;
        return;
    }
    if (t >= stops.back().position) {
        r = LinearToSrgb(stops.back().r);
        g = LinearToSrgb(stops.back().g);
        b = LinearToSrgb(stops.back().b);
        a = stops.back().a;
        return;
    }

    for (size_t i = 0; i + 1 < stops.size(); i++) {
        if (t >= stops[i].position && t <= stops[i + 1].position) {
            float range = stops[i + 1].position - stops[i].position;
            float local = (range > 0) ? (t - stops[i].position) / range : 0;
            // Stops already carry linear-light RGB, prepared once when the
            // brush is created. Only the final conversion back to sRGB remains
            // in the pixel loop.
            r = LinearToSrgb(Lerp(stops[i].r, stops[i + 1].r, local));
            g = LinearToSrgb(Lerp(stops[i].g, stops[i + 1].g, local));
            b = LinearToSrgb(Lerp(stops[i].b, stops[i + 1].b, local));
            a = Lerp(stops[i].a, stops[i + 1].a, local);
            return;
        }
    }
    r = LinearToSrgb(stops.back().r);
    g = LinearToSrgb(stops.back().g);
    b = LinearToSrgb(stops.back().b);
    a = stops.back().a;
}

static uint8_t LinearToSrgbByte(float linear)
{
    static const std::array<uint8_t, 65536> table = [] {
        std::array<uint8_t, 65536> result{};
        for (size_t index = 0; index < result.size(); ++index) {
            const float value = static_cast<float>(index) / 65535.0f;
            result[index] = FloatToU8(LinearToSrgb(value));
        }
        return result;
    }();
    const size_t index = static_cast<size_t>(std::clamp(
        linear * 65535.0f + 0.5f, 0.0f, 65535.0f));
    return table[index];
}

static void InterpolateGradientStops8(
    const std::vector<JaliumGradientStop>& stops, float t,
    uint8_t& r, uint8_t& g, uint8_t& b, float& a)
{
    if (stops.empty()) { r = g = b = 0; a = 0.0f; return; }
    t = std::clamp(t, 0.0f, 1.0f);

    const JaliumGradientStop* left = &stops.front();
    const JaliumGradientStop* right = left;
    float local = 0.0f;
    if (t <= stops.front().position) {
        right = left;
    } else if (t >= stops.back().position) {
        left = right = &stops.back();
    } else {
        for (size_t index = 0; index + 1 < stops.size(); ++index) {
            if (t < stops[index].position || t > stops[index + 1].position) continue;
            left = &stops[index];
            right = &stops[index + 1];
            const float range = right->position - left->position;
            local = range > 0.0f ? (t - left->position) / range : 0.0f;
            break;
        }
    }

    r = LinearToSrgbByte(Lerp(left->r, right->r, local));
    g = LinearToSrgbByte(Lerp(left->g, right->g, local));
    b = LinearToSrgbByte(Lerp(left->b, right->b, local));
    a = Lerp(left->a, right->a, local);
}

static constexpr size_t kGradientLutIntervals = 4096;

static void BuildGradientLut(
    const std::vector<JaliumGradientStop>& stops,
    std::vector<uint32_t>& colors,
    std::vector<float>& alphas)
{
    colors.resize(kGradientLutIntervals + 1u);
    alphas.resize(kGradientLutIntervals + 1u);
    for (size_t index = 0; index <= kGradientLutIntervals; ++index) {
        uint8_t r, g, b;
        float a;
        InterpolateGradientStops8(
            stops,
            static_cast<float>(index) / static_cast<float>(kGradientLutIntervals),
            r, g, b, a);
        colors[index] = static_cast<uint32_t>(r) |
            (static_cast<uint32_t>(g) << 8) |
            (static_cast<uint32_t>(b) << 16);
        alphas[index] = a;
    }
}

static void SampleGradientLut(
    const std::vector<uint32_t>& colors,
    const std::vector<float>& alphas,
    float t,
    uint8_t& r, uint8_t& g, uint8_t& b, float& a)
{
    if (colors.size() != kGradientLutIntervals + 1u ||
        alphas.size() != kGradientLutIntervals + 1u) {
        r = g = b = 0;
        a = 0.0f;
        return;
    }
    const float position = std::clamp(t, 0.0f, 1.0f) *
        static_cast<float>(kGradientLutIntervals);
    const size_t lower = std::min(
        static_cast<size_t>(position), kGradientLutIntervals);
    const size_t upper = std::min(lower + 1u, kGradientLutIntervals);
    const float fraction = position - static_cast<float>(lower);
    const uint32_t c0 = colors[lower];
    const uint32_t c1 = colors[upper];
    auto interpolateByte = [&](int shift) {
        const float left = static_cast<float>((c0 >> shift) & 0xFFu);
        const float right = static_cast<float>((c1 >> shift) & 0xFFu);
        return static_cast<uint8_t>(std::clamp(
            left + (right - left) * fraction + 0.5f, 0.0f, 255.0f));
    };
    r = interpolateByte(0);
    g = interpolateByte(8);
    b = interpolateByte(16);
    a = Lerp(alphas[lower], alphas[upper], fraction);
}

SoftwareLinearGradientBrush::SoftwareLinearGradientBrush(
    float sx, float sy, float ex, float ey,
    const JaliumGradientStop* sourceStops, uint32_t count, uint32_t spread)
    : startX(sx), startY(sy), endX(ex), endY(ey), spreadMethod(spread),
      stops(sourceStops, sourceStops + count)
{
    deltaX = endX - startX;
    deltaY = endY - startY;
    const float lengthSquared = deltaX * deltaX + deltaY * deltaY;
    inverseLengthSquared = lengthSquared > 0.0f ? 1.0f / lengthSquared : 0.0f;
    for (auto& stop : stops) {
        stop.r = SrgbToLinear(stop.r);
        stop.g = SrgbToLinear(stop.g);
        stop.b = SrgbToLinear(stop.b);
    }
    BuildGradientLut(stops, colorLut, alphaLut);
}

SoftwareRadialGradientBrush::SoftwareRadialGradientBrush(
    float cx, float cy, float rx, float ry, float ox, float oy,
    const JaliumGradientStop* sourceStops, uint32_t count, uint32_t spread)
    : centerX(cx), centerY(cy), radiusX(rx), radiusY(ry),
      originX(ox), originY(oy), spreadMethod(spread),
      stops(sourceStops, sourceStops + count)
{
    for (auto& stop : stops) {
        stop.r = SrgbToLinear(stop.r);
        stop.g = SrgbToLinear(stop.g);
        stop.b = SrgbToLinear(stop.b);
    }
    BuildGradientLut(stops, colorLut, alphaLut);
}

SoftwareLinearGradientBrush::~SoftwareLinearGradientBrush()
{
    std::lock_guard<std::mutex> lock(rasterCacheMutex_);
    ReleaseSoftwareResourceCache(rasterCacheBytes_);
    rasterCache_.clear();
    rasterCacheBytes_ = 0;
}

SoftwareRadialGradientBrush::~SoftwareRadialGradientBrush()
{
    std::lock_guard<std::mutex> lock(rasterCacheMutex_);
    ReleaseSoftwareResourceCache(rasterCacheBytes_);
    rasterCache_.clear();
    rasterCacheBytes_ = 0;
}

// ============================================================================
// SoftwareFramebuffer
// ============================================================================

void SoftwareFramebuffer::BlendPixel(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    BlendPixelUnchecked(x, y, r, g, b, a);
}

void SoftwareFramebuffer::BlendPixelUnchecked(
    int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    size_t idx = (static_cast<size_t>(y) * width + x) * 4;

    if (a == 255) {
        pixels[idx + 0] = b;
        pixels[idx + 1] = g;
        pixels[idx + 2] = r;
        pixels[idx + 3] = a;
        return;
    }
    if (a == 0) return;

    if (pixels[idx + 3] == 255) {
        const uint32_t inverseAlpha = 255u - a;
        pixels[idx + 0] = static_cast<uint8_t>(
            (static_cast<uint32_t>(b) * a +
             static_cast<uint32_t>(pixels[idx + 0]) * inverseAlpha) / 255u);
        pixels[idx + 1] = static_cast<uint8_t>(
            (static_cast<uint32_t>(g) * a +
             static_cast<uint32_t>(pixels[idx + 1]) * inverseAlpha) / 255u);
        pixels[idx + 2] = static_cast<uint8_t>(
            (static_cast<uint32_t>(r) * a +
             static_cast<uint32_t>(pixels[idx + 2]) * inverseAlpha) / 255u);
        return;
    }

    // Alpha blending using premultiplied alpha, matching D3D12/Metal behavior.
    // Source (r,g,b,a) arrives as straight alpha; convert to premultiplied for blending.
    float sa = a / 255.0f;
    float srcB = b * sa;
    float srcG = g * sa;
    float srcR = r * sa;

    float dstA = pixels[idx + 3] / 255.0f;
    float dstB = pixels[idx + 0] * dstA;  // stored straight → premultiply
    float dstG = pixels[idx + 1] * dstA;
    float dstR = pixels[idx + 2] * dstA;

    float oneMinusSa = 1.0f - sa;
    float outA = sa + dstA * oneMinusSa;
    if (outA < 0.001f) {
        pixels[idx + 0] = pixels[idx + 1] = pixels[idx + 2] = pixels[idx + 3] = 0;
        return;
    }
    // Premultiplied blend: outPre = srcPre + dstPre * (1 - srcA), then un-premultiply.
    float invOutA = 1.0f / outA;
    pixels[idx + 0] = (uint8_t)std::clamp((srcB + dstB * oneMinusSa) * invOutA, 0.0f, 255.0f);
    pixels[idx + 1] = (uint8_t)std::clamp((srcG + dstG * oneMinusSa) * invOutA, 0.0f, 255.0f);
    pixels[idx + 2] = (uint8_t)std::clamp((srcR + dstR * oneMinusSa) * invOutA, 0.0f, 255.0f);
    pixels[idx + 3] = (uint8_t)(outA * 255.0f + 0.5f);
}

void SoftwareFramebuffer::FillOpaqueSpan(
    int32_t y, int32_t x0, int32_t x1, uint32_t packedBgra)
{
    if (y < 0 || y >= height) return;
    x0 = std::max(x0, 0);
    x1 = std::min(x1, width);
    if (x1 <= x0) return;

    // BGRA pixels are naturally 32-bit aligned. Select the widest exact store
    // kernel available on the running CPU, with a scalar tail/fallback that is
    // byte-identical on every architecture.
    auto* destination = reinterpret_cast<uint32_t*>(
        pixels.data() + (static_cast<size_t>(y) * width + x0) * 4);
    size_t count = static_cast<size_t>(x1 - x0);

#if defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
    if (count >= 8 && SoftwareCpuHasAvx2()) {
        const __m256i value = _mm256_set1_epi32(static_cast<int>(packedBgra));
        while (count >= 8) {
            _mm256_storeu_si256(reinterpret_cast<__m256i*>(destination), value);
            destination += 8;
            count -= 8;
        }
        _mm256_zeroupper();
    }
#elif defined(__AVX2__)
    {
        const __m256i value = _mm256_set1_epi32(static_cast<int>(packedBgra));
        while (count >= 8) {
            _mm256_storeu_si256(reinterpret_cast<__m256i*>(destination), value);
            destination += 8;
            count -= 8;
        }
    }
#endif

#if defined(_M_X64) || defined(_M_IX86) || defined(__SSE2__)
    if (SoftwareSimdEnabled()) {
        const __m128i value = _mm_set1_epi32(static_cast<int>(packedBgra));
        while (count >= 4) {
            _mm_storeu_si128(reinterpret_cast<__m128i*>(destination), value);
            destination += 4;
            count -= 4;
        }
    }
#elif defined(__aarch64__) || defined(_M_ARM64)
    if (SoftwareSimdEnabled()) {
        const uint32x4_t value = vdupq_n_u32(packedBgra);
        while (count >= 4) {
            vst1q_u32(destination, value);
            destination += 4;
            count -= 4;
        }
    }
#endif

    std::fill_n(destination, count, packedBgra);
}

void SoftwareFramebuffer::BlendSolidSpan(
    int32_t y, int32_t x0, int32_t x1,
    uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    if (a == 0 || y < 0 || y >= height) return;
    x0 = std::max(x0, 0);
    x1 = std::min(x1, width);
    if (x1 <= x0) return;

    const uint32_t packed = static_cast<uint32_t>(b) |
        (static_cast<uint32_t>(g) << 8) |
        (static_cast<uint32_t>(r) << 16) |
        (static_cast<uint32_t>(a) << 24);
    if (a == 255) {
        FillOpaqueSpan(y, x0, x1, packed);
        return;
    }

    const uint32_t inverseAlpha = 255u - a;
    uint8_t* destination = pixels.data() +
        (static_cast<size_t>(y) * width + x0) * 4;
    for (int32_t x = x0; x < x1; ++x, destination += 4) {
        // Opaque window contents are overwhelmingly the common case. With an
        // opaque destination SrcOver stays opaque and needs neither floating
        // point nor the expensive straight-alpha un-premultiply division.
        if (destination[3] == 255) {
            destination[0] = static_cast<uint8_t>(
                (static_cast<uint32_t>(b) * a +
                 static_cast<uint32_t>(destination[0]) * inverseAlpha) / 255u);
            destination[1] = static_cast<uint8_t>(
                (static_cast<uint32_t>(g) * a +
                 static_cast<uint32_t>(destination[1]) * inverseAlpha) / 255u);
            destination[2] = static_cast<uint8_t>(
                (static_cast<uint32_t>(r) * a +
                 static_cast<uint32_t>(destination[2]) * inverseAlpha) / 255u);
            continue;
        }

        BlendPixelUnchecked(x, y, r, g, b, a);
    }
}

void SoftwareFramebuffer::BlendBgraSpan(
    int32_t y, int32_t x0, int32_t x1, const uint8_t* sourceBgra)
{
    if (!sourceBgra || y < 0 || y >= height) return;
    x0 = std::max(x0, 0);
    x1 = std::min(x1, width);
    if (x1 <= x0) return;

    uint8_t* destination = pixels.data() +
        (static_cast<size_t>(y) * width + x0) * 4u;
    size_t count = static_cast<size_t>(x1 - x0);

#if defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
    if (count >= 8 && SoftwareCpuHasAvx2()) {
        const __m256i zero = _mm256_setzero_si256();
        const __m256i one = _mm256_set1_epi16(1);
        const __m256i alphaMask = _mm256_set1_epi32(
            static_cast<int>(0xFF000000u));
        while (count >= 8) {
            const __m256i source = _mm256_loadu_si256(
                reinterpret_cast<const __m256i*>(sourceBgra));
            const __m256i sourceAlpha = _mm256_and_si256(source, alphaMask);
            const __m256i sourceOpaque =
                _mm256_cmpeq_epi32(sourceAlpha, alphaMask);
            if (_mm256_movemask_epi8(sourceOpaque) == -1) {
                _mm256_storeu_si256(
                    reinterpret_cast<__m256i*>(destination), source);
                sourceBgra += 32;
                destination += 32;
                count -= 8;
                continue;
            }
            const __m256i target = _mm256_loadu_si256(
                reinterpret_cast<const __m256i*>(destination));
            const __m256i targetAlpha = _mm256_and_si256(target, alphaMask);
            const __m256i opaque = _mm256_cmpeq_epi32(targetAlpha, alphaMask);
            if (_mm256_movemask_epi8(opaque) == -1) {
                __m256i alpha = _mm256_srli_epi32(source, 24);
                alpha = _mm256_or_si256(alpha, _mm256_slli_epi32(alpha, 8));
                alpha = _mm256_or_si256(alpha, _mm256_slli_epi32(alpha, 16));
                const __m256i inverse = _mm256_sub_epi8(
                    _mm256_set1_epi8(static_cast<char>(0xFF)), alpha);

                const __m256i sourceLo = _mm256_unpacklo_epi8(source, zero);
                const __m256i sourceHi = _mm256_unpackhi_epi8(source, zero);
                const __m256i targetLo = _mm256_unpacklo_epi8(target, zero);
                const __m256i targetHi = _mm256_unpackhi_epi8(target, zero);
                const __m256i alphaLo = _mm256_unpacklo_epi8(alpha, zero);
                const __m256i alphaHi = _mm256_unpackhi_epi8(alpha, zero);
                const __m256i inverseLo = _mm256_unpacklo_epi8(inverse, zero);
                const __m256i inverseHi = _mm256_unpackhi_epi8(inverse, zero);
                __m256i sumLo = _mm256_add_epi16(
                    _mm256_mullo_epi16(sourceLo, alphaLo),
                    _mm256_mullo_epi16(targetLo, inverseLo));
                __m256i sumHi = _mm256_add_epi16(
                    _mm256_mullo_epi16(sourceHi, alphaHi),
                    _mm256_mullo_epi16(targetHi, inverseHi));
                sumLo = _mm256_add_epi16(sumLo, one);
                sumHi = _mm256_add_epi16(sumHi, one);
                sumLo = _mm256_add_epi16(sumLo, _mm256_srli_epi16(sumLo, 8));
                sumHi = _mm256_add_epi16(sumHi, _mm256_srli_epi16(sumHi, 8));
                sumLo = _mm256_srli_epi16(sumLo, 8);
                sumHi = _mm256_srli_epi16(sumHi, 8);
                __m256i result = _mm256_packus_epi16(sumLo, sumHi);
                result = _mm256_or_si256(result, alphaMask);
                _mm256_storeu_si256(
                    reinterpret_cast<__m256i*>(destination), result);
                sourceBgra += 32;
                destination += 32;
                count -= 8;
                continue;
            }
            break;
        }
        _mm256_zeroupper();
    }
#endif

#if defined(_M_X64) || defined(_M_IX86) || defined(__SSE2__)
    if (SoftwareSimdEnabled()) {
        const __m128i zero = _mm_setzero_si128();
        const __m128i one = _mm_set1_epi16(1);
        const __m128i alphaMask = _mm_set1_epi32(
            static_cast<int>(0xFF000000u));
        while (count >= 4) {
            const __m128i source = _mm_loadu_si128(
                reinterpret_cast<const __m128i*>(sourceBgra));
            const __m128i sourceAlpha = _mm_and_si128(source, alphaMask);
            const __m128i sourceOpaque =
                _mm_cmpeq_epi32(sourceAlpha, alphaMask);
            if (_mm_movemask_epi8(sourceOpaque) == 0xFFFF) {
                _mm_storeu_si128(
                    reinterpret_cast<__m128i*>(destination), source);
                sourceBgra += 16;
                destination += 16;
                count -= 4;
                continue;
            }
            const __m128i target = _mm_loadu_si128(
                reinterpret_cast<const __m128i*>(destination));
            const __m128i targetAlpha = _mm_and_si128(target, alphaMask);
            const __m128i opaque = _mm_cmpeq_epi32(targetAlpha, alphaMask);
            if (_mm_movemask_epi8(opaque) != 0xFFFF) break;

            __m128i alpha = _mm_srli_epi32(source, 24);
            alpha = _mm_or_si128(alpha, _mm_slli_epi32(alpha, 8));
            alpha = _mm_or_si128(alpha, _mm_slli_epi32(alpha, 16));
            const __m128i inverse = _mm_sub_epi8(
                _mm_set1_epi8(static_cast<char>(0xFF)), alpha);
            const __m128i sourceLo = _mm_unpacklo_epi8(source, zero);
            const __m128i sourceHi = _mm_unpackhi_epi8(source, zero);
            const __m128i targetLo = _mm_unpacklo_epi8(target, zero);
            const __m128i targetHi = _mm_unpackhi_epi8(target, zero);
            const __m128i alphaLo = _mm_unpacklo_epi8(alpha, zero);
            const __m128i alphaHi = _mm_unpackhi_epi8(alpha, zero);
            const __m128i inverseLo = _mm_unpacklo_epi8(inverse, zero);
            const __m128i inverseHi = _mm_unpackhi_epi8(inverse, zero);
            __m128i sumLo = _mm_add_epi16(
                _mm_mullo_epi16(sourceLo, alphaLo),
                _mm_mullo_epi16(targetLo, inverseLo));
            __m128i sumHi = _mm_add_epi16(
                _mm_mullo_epi16(sourceHi, alphaHi),
                _mm_mullo_epi16(targetHi, inverseHi));
            sumLo = _mm_add_epi16(sumLo, one);
            sumHi = _mm_add_epi16(sumHi, one);
            sumLo = _mm_add_epi16(sumLo, _mm_srli_epi16(sumLo, 8));
            sumHi = _mm_add_epi16(sumHi, _mm_srli_epi16(sumHi, 8));
            sumLo = _mm_srli_epi16(sumLo, 8);
            sumHi = _mm_srli_epi16(sumHi, 8);
            __m128i result = _mm_packus_epi16(sumLo, sumHi);
            result = _mm_or_si128(result, alphaMask);
            _mm_storeu_si128(reinterpret_cast<__m128i*>(destination), result);
            sourceBgra += 16;
            destination += 16;
            count -= 4;
        }
    }
#endif

#if defined(__aarch64__) || defined(_M_ARM64)
    if (SoftwareSimdEnabled()) {
        static const uint8_t alphaIndicesBytes[16] = {
            3, 3, 3, 3, 7, 7, 7, 7,
            11, 11, 11, 11, 15, 15, 15, 15
        };
        static const uint8_t alphaPositionsBytes[16] = {
            0, 0, 0, 255, 0, 0, 0, 255,
            0, 0, 0, 255, 0, 0, 0, 255
        };
        const uint8x16_t alphaIndices = vld1q_u8(alphaIndicesBytes);
        const uint8x16_t alphaPositions = vld1q_u8(alphaPositionsBytes);
        const uint8x16_t all255 = vdupq_n_u8(255);
        const uint16x8_t one = vdupq_n_u16(1);
        while (count >= 4) {
            const uint8x16_t source = vld1q_u8(sourceBgra);
            const uint8x16_t alpha = vqtbl1q_u8(source, alphaIndices);
            if (vminvq_u8(vceqq_u8(alpha, all255)) == 255) {
                vst1q_u8(destination, source);
                sourceBgra += 16;
                destination += 16;
                count -= 4;
                continue;
            }

            const uint8x16_t target = vld1q_u8(destination);
            const uint8x16_t targetAlpha = vqtbl1q_u8(target, alphaIndices);
            if (vminvq_u8(vceqq_u8(targetAlpha, all255)) != 255) break;
            const uint8x16_t inverse = vsubq_u8(all255, alpha);

            const uint16x8_t sourceLo = vmovl_u8(vget_low_u8(source));
            const uint16x8_t sourceHi = vmovl_u8(vget_high_u8(source));
            const uint16x8_t targetLo = vmovl_u8(vget_low_u8(target));
            const uint16x8_t targetHi = vmovl_u8(vget_high_u8(target));
            const uint16x8_t alphaLo = vmovl_u8(vget_low_u8(alpha));
            const uint16x8_t alphaHi = vmovl_u8(vget_high_u8(alpha));
            const uint16x8_t inverseLo = vmovl_u8(vget_low_u8(inverse));
            const uint16x8_t inverseHi = vmovl_u8(vget_high_u8(inverse));
            uint16x8_t sumLo = vaddq_u16(
                vmulq_u16(sourceLo, alphaLo),
                vmulq_u16(targetLo, inverseLo));
            uint16x8_t sumHi = vaddq_u16(
                vmulq_u16(sourceHi, alphaHi),
                vmulq_u16(targetHi, inverseHi));
            sumLo = vaddq_u16(sumLo, one);
            sumHi = vaddq_u16(sumHi, one);
            sumLo = vaddq_u16(sumLo, vshrq_n_u16(sumLo, 8));
            sumHi = vaddq_u16(sumHi, vshrq_n_u16(sumHi, 8));
            sumLo = vshrq_n_u16(sumLo, 8);
            sumHi = vshrq_n_u16(sumHi, 8);
            uint8x16_t result = vcombine_u8(
                vqmovn_u16(sumLo), vqmovn_u16(sumHi));
            result = vbslq_u8(alphaPositions, all255, result);
            vst1q_u8(destination, result);
            sourceBgra += 16;
            destination += 16;
            count -= 4;
        }
    }
#endif

    for (size_t index = 0; index < count; ++index) {
        const uint8_t alpha = sourceBgra[3];
        if (alpha == 255) {
            std::memcpy(destination, sourceBgra, 4u);
        } else if (alpha != 0 && destination[3] == 255) {
            const uint32_t inverseAlpha = 255u - alpha;
            destination[0] = static_cast<uint8_t>(
                (static_cast<uint32_t>(sourceBgra[0]) * alpha +
                 static_cast<uint32_t>(destination[0]) * inverseAlpha) / 255u);
            destination[1] = static_cast<uint8_t>(
                (static_cast<uint32_t>(sourceBgra[1]) * alpha +
                 static_cast<uint32_t>(destination[1]) * inverseAlpha) / 255u);
            destination[2] = static_cast<uint8_t>(
                (static_cast<uint32_t>(sourceBgra[2]) * alpha +
                 static_cast<uint32_t>(destination[2]) * inverseAlpha) / 255u);
        } else if (alpha != 0) {
            const int32_t x = x1 - static_cast<int32_t>(count) +
                static_cast<int32_t>(index);
            BlendPixelUnchecked(
                x, y, sourceBgra[2], sourceBgra[1], sourceBgra[0], alpha);
        }
        sourceBgra += 4;
        destination += 4;
    }
}

void SoftwareFramebuffer::BlendPixelSubpixel(
    int32_t x, int32_t y,
    uint8_t r, uint8_t g, uint8_t b,
    uint8_t coverageR, uint8_t coverageG, uint8_t coverageB)
{
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    const uint8_t maxCoverage = std::max({coverageR, coverageG, coverageB});
    if (maxCoverage == 0) return;
    const size_t index = (static_cast<size_t>(y) * width + x) * 4;
    const float alphaR = coverageR / 255.0f;
    const float alphaG = coverageG / 255.0f;
    const float alphaB = coverageB / 255.0f;
    const float sourceAlpha = maxCoverage / 255.0f;
    const float destinationAlpha = pixels[index + 3] / 255.0f;
    const float outputAlpha = sourceAlpha + destinationAlpha * (1.0f - sourceAlpha);
    if (outputAlpha <= 0.0001f) return;

    // LCD coverage is independent for each color channel. A single SrcOver
    // alpha cannot represent it, so composite the three premultiplied channels
    // separately and use max(R,G,B) coverage for the framebuffer alpha.
    const float outR = r * alphaR + pixels[index + 2] * destinationAlpha * (1.0f - alphaR);
    const float outG = g * alphaG + pixels[index + 1] * destinationAlpha * (1.0f - alphaG);
    const float outB = b * alphaB + pixels[index] * destinationAlpha * (1.0f - alphaB);
    pixels[index + 2] = static_cast<uint8_t>(std::clamp(outR / outputAlpha, 0.0f, 255.0f));
    pixels[index + 1] = static_cast<uint8_t>(std::clamp(outG / outputAlpha, 0.0f, 255.0f));
    pixels[index] = static_cast<uint8_t>(std::clamp(outB / outputAlpha, 0.0f, 255.0f));
    pixels[index + 3] = static_cast<uint8_t>(outputAlpha * 255.0f + 0.5f);
}

void SoftwareFramebuffer::SetPixel(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    size_t idx = (static_cast<size_t>(y) * width + x) * 4;
    pixels[idx + 0] = b;
    pixels[idx + 1] = g;
    pixels[idx + 2] = r;
    pixels[idx + 3] = a;
}

std::shared_ptr<const SoftwareScaledBitmap> SoftwareBitmap::GetOrCreateScaled(
    uint32_t width, uint32_t height,
    float destinationWidth, float destinationHeight,
    float phaseX, float phaseY, float opacity)
{
    if (dynamic_ || width == 0 || height == 0 || width_ == 0 || height_ == 0 ||
        destinationWidth <= 0.0f || destinationHeight <= 0.0f) return {};
    const uint64_t byteCount64 = static_cast<uint64_t>(width) * height * 4u;
    if (byteCount64 > scaledCacheBudgetBytes_ || byteCount64 > SIZE_MAX) return {};

    {
        std::lock_guard<std::mutex> lock(scaledCacheMutex_);
        for (size_t index = 0; index < scaledCache_.size(); ++index) {
            const auto& candidate = scaledCache_[index];
            if (candidate->width != width || candidate->height != height ||
                std::abs(candidate->destinationWidth - destinationWidth) >= 1e-6f ||
                std::abs(candidate->destinationHeight - destinationHeight) >= 1e-6f ||
                std::abs(candidate->phaseX - phaseX) >= 1e-6f ||
                std::abs(candidate->phaseY - phaseY) >= 1e-6f ||
                std::abs(candidate->opacity - opacity) >= 1e-6f) continue;
            auto hit = candidate;
            if (index + 1u != scaledCache_.size()) {
                scaledCache_.erase(scaledCache_.begin() + index);
                scaledCache_.push_back(hit);
            }
            return hit;
        }
    }

    auto scaled = std::make_shared<SoftwareScaledBitmap>();
    scaled->width = width;
    scaled->height = height;
    scaled->opaque = true;
    scaled->destinationWidth = destinationWidth;
    scaled->destinationHeight = destinationHeight;
    scaled->phaseX = phaseX;
    scaled->phaseY = phaseY;
    scaled->opacity = opacity;
    scaled->pixels.resize(static_cast<size_t>(byteCount64));

    const int32_t sourceWidth = static_cast<int32_t>(width_);
    const int32_t sourceHeight = static_cast<int32_t>(height_);
    for (uint32_t y = 0; y < height; ++y) {
        const float coverageY = std::clamp(
            std::min(static_cast<float>(y) + 1.0f, phaseY + destinationHeight) -
            std::max(static_cast<float>(y), phaseY),
            0.0f, 1.0f);
        const float sourceY =
            ((static_cast<float>(y) + 0.5f - phaseY) / destinationHeight) *
            static_cast<float>(height_) - 0.5f;
        const float sourceYFloor = std::floor(sourceY);
        const int32_t y0 = std::clamp(static_cast<int32_t>(sourceYFloor), 0, sourceHeight - 1);
        const int32_t y1 = std::clamp(static_cast<int32_t>(sourceYFloor) + 1, 0, sourceHeight - 1);
        const float fy = sourceY - sourceYFloor;
        for (uint32_t x = 0; x < width; ++x) {
            const float coverageX = std::clamp(
                std::min(static_cast<float>(x) + 1.0f, phaseX + destinationWidth) -
                std::max(static_cast<float>(x), phaseX),
                0.0f, 1.0f);
            const float sourceX =
                ((static_cast<float>(x) + 0.5f - phaseX) / destinationWidth) *
                static_cast<float>(width_) - 0.5f;
            const float sourceXFloor = std::floor(sourceX);
            const int32_t x0 = std::clamp(static_cast<int32_t>(sourceXFloor), 0, sourceWidth - 1);
            const int32_t x1 = std::clamp(static_cast<int32_t>(sourceXFloor) + 1, 0, sourceWidth - 1);
            const float fx = sourceX - sourceXFloor;
            const float w00 = (1.0f - fx) * (1.0f - fy);
            const float w10 = fx * (1.0f - fy);
            const float w01 = (1.0f - fx) * fy;
            const float w11 = fx * fy;
            const size_t i00 = (static_cast<size_t>(y0) * sourceWidth + x0) * 4u;
            const size_t i10 = (static_cast<size_t>(y0) * sourceWidth + x1) * 4u;
            const size_t i01 = (static_cast<size_t>(y1) * sourceWidth + x0) * 4u;
            const size_t i11 = (static_cast<size_t>(y1) * sourceWidth + x1) * 4u;
            const size_t destination = (static_cast<size_t>(y) * width + x) * 4u;
            for (int channel = 0; channel < 3; ++channel) {
                const float value =
                    pixels_[i00 + channel] * w00 + pixels_[i10 + channel] * w10 +
                    pixels_[i01 + channel] * w01 + pixels_[i11 + channel] * w11;
                scaled->pixels[destination + channel] = static_cast<uint8_t>(
                    std::clamp(value + 0.5f, 0.0f, 255.0f));
            }
            const float sourceAlpha =
                pixels_[i00 + 3] * w00 + pixels_[i10 + 3] * w10 +
                pixels_[i01 + 3] * w01 + pixels_[i11 + 3] * w11;
            const uint8_t alpha = static_cast<uint8_t>(std::clamp(
                sourceAlpha * opacity * coverageX * coverageY + 0.5f,
                0.0f, 255.0f));
            scaled->pixels[destination + 3] = alpha;
            if (alpha != 255) scaled->opaque = false;
        }
    }

    std::lock_guard<std::mutex> lock(scaledCacheMutex_);
    for (const auto& candidate : scaledCache_) {
        if (candidate->width == width && candidate->height == height &&
            std::abs(candidate->destinationWidth - destinationWidth) < 1e-6f &&
            std::abs(candidate->destinationHeight - destinationHeight) < 1e-6f &&
            std::abs(candidate->phaseX - phaseX) < 1e-6f &&
            std::abs(candidate->phaseY - phaseY) < 1e-6f &&
            std::abs(candidate->opacity - opacity) < 1e-6f) {
            return candidate;
        }
    }
    const size_t bytes = scaled->pixels.size();
    while (!scaledCache_.empty() &&
           scaledCacheBytes_ + bytes > scaledCacheBudgetBytes_) {
        if (trackGlobalCache_)
            ReleaseSoftwareResourceCache(scaledCache_.front()->pixels.size());
        scaledCacheBytes_ -= scaledCache_.front()->pixels.size();
        scaledCache_.erase(scaledCache_.begin());
    }
    if (trackGlobalCache_ && !TryReserveSoftwareResourceCache(bytes)) return {};
    scaledCacheBytes_ += bytes;
    scaledCache_.push_back(std::move(scaled));
    return scaledCache_.back();
}

SoftwareBitmap::~SoftwareBitmap()
{
    ClearScaledCache();
}

size_t SoftwareBitmap::ScaledCacheBytes() const
{
    std::lock_guard<std::mutex> lock(scaledCacheMutex_);
    return scaledCacheBytes_;
}

void SoftwareBitmap::ClearScaledCache()
{
    std::lock_guard<std::mutex> lock(scaledCacheMutex_);
    if (trackGlobalCache_)
        ReleaseSoftwareResourceCache(scaledCacheBytes_);
    scaledCache_.clear();
    scaledCacheBytes_ = 0;
}

// ============================================================================
// Brush Sampling
// ============================================================================

// 0=Pad, 1=Repeat, 2=Reflect — same wire values as EngineBrushData::spreadMethod
// and the managed SoftwareVectorRasterizer. Applied to the raw gradient
// parameter BEFORE stop interpolation (InterpolateGradientStops clamps, which
// is exactly Pad, so Pad needs no wrapping here).
static inline float ApplyGradientSpread(float t, uint32_t spreadMethod)
{
    switch (spreadMethod) {
        case 1: // Repeat
            return t - std::floor(t);
        case 2: { // Reflect
            float wrapped = std::fmod(std::fabs(t), 2.0f);
            return wrapped > 1.0f ? 2.0f - wrapped : wrapped;
        }
        default:
            return t;
    }
}

void SoftwareLinearGradientBrush::SampleColor(float px, float py,
    float& outR, float& outG, float& outB, float& outA) const
{
    float t = inverseLengthSquared > 0.0f
        ? ((px - startX) * deltaX + (py - startY) * deltaY) * inverseLengthSquared
        : 0.0f;
    t = ApplyGradientSpread(t, spreadMethod);
    InterpolateGradientStops(stops, t, outR, outG, outB, outA);
}

void SoftwareLinearGradientBrush::SampleColor8(
    float px, float py, uint8_t& outR, uint8_t& outG, uint8_t& outB, float& outA) const
{
    float t = inverseLengthSquared > 0.0f
        ? ((px - startX) * deltaX + (py - startY) * deltaY) * inverseLengthSquared
        : 0.0f;
    t = ApplyGradientSpread(t, spreadMethod);
    SampleGradientLut(colorLut, alphaLut, t, outR, outG, outB, outA);
}

void SoftwareRadialGradientBrush::SampleColor(float px, float py,
    float& outR, float& outG, float& outB, float& outA) const
{
    // WPF focal-point semantics (GradientOrigin): t=0 at the origin, t=1 where
    // the ray origin→P crosses the center/radius ellipse. Solved in the unit
    // circle space of the ellipse; matches the managed SoftwareVectorRasterizer.
    // With origin == center this reduces exactly to the plain distance formula.
    float rx = radiusX > 0 ? radiusX : 1;
    float ry = radiusY > 0 ? radiusY : 1;
    float ux = (px - centerX) / rx;
    float uy = (py - centerY) / ry;
    float fx = (originX - centerX) / rx;
    float fy = (originY - centerY) / ry;
    float dx = ux - fx;
    float dy = uy - fy;
    float a = dx * dx + dy * dy;
    float t;
    if (a <= 1e-12f) {
        t = 0.0f;
    } else {
        float b = 2.0f * (fx * dx + fy * dy);
        float c = fx * fx + fy * fy - 1.0f;
        float disc = std::max(b * b - 4.0f * a * c, 0.0f);
        float s = (-b + std::sqrt(disc)) / (2.0f * a);
        t = s > 1e-12f ? 1.0f / s : 0.0f;
    }
    t = ApplyGradientSpread(t, spreadMethod);
    InterpolateGradientStops(stops, t, outR, outG, outB, outA);
}

void SoftwareRadialGradientBrush::SampleColor8(
    float px, float py, uint8_t& outR, uint8_t& outG, uint8_t& outB, float& outA) const
{
    float rx = radiusX > 0 ? radiusX : 1;
    float ry = radiusY > 0 ? radiusY : 1;
    float ux = (px - centerX) / rx;
    float uy = (py - centerY) / ry;
    float fx = (originX - centerX) / rx;
    float fy = (originY - centerY) / ry;
    float dx = ux - fx;
    float dy = uy - fy;
    float quadratic = dx * dx + dy * dy;
    float t;
    if (quadratic <= 1e-12f) {
        t = 0.0f;
    } else {
        float linear = 2.0f * (fx * dx + fy * dy);
        float constant = fx * fx + fy * fy - 1.0f;
        float discriminant = std::max(
            linear * linear - 4.0f * quadratic * constant, 0.0f);
        float intersection =
            (-linear + std::sqrt(discriminant)) / (2.0f * quadratic);
        t = intersection > 1e-12f ? 1.0f / intersection : 0.0f;
    }
    t = ApplyGradientSpread(t, spreadMethod);
    SampleGradientLut(colorLut, alphaLut, t, outR, outG, outB, outA);
}

static bool GradientRasterMatches(
    const SoftwareGradientRaster& raster,
    float left, float top, float right, float bottom, float opacity)
{
    constexpr float kTolerance = 1e-6f;
    return std::abs(raster.left - left) < kTolerance &&
        std::abs(raster.top - top) < kTolerance &&
        std::abs(raster.right - right) < kTolerance &&
        std::abs(raster.bottom - bottom) < kTolerance &&
        std::abs(raster.opacity - opacity) < kTolerance;
}

static constexpr size_t kMaxGradientRasterCacheBytes = 8u * 1024u * 1024u;

template <typename GradientBrush>
static std::shared_ptr<SoftwareGradientRaster> BuildGradientRaster(
    const GradientBrush& brush,
    float left, float top, float right, float bottom, float opacity)
{
    const int32_t x = static_cast<int32_t>(std::floor(left));
    const int32_t y = static_cast<int32_t>(std::floor(top));
    const int32_t width = static_cast<int32_t>(std::ceil(right)) - x;
    const int32_t height = static_cast<int32_t>(std::ceil(bottom)) - y;
    if (width <= 0 || height <= 0) return {};

    // A single brush is allowed one bounded raster. Larger gradients continue
    // through the incremental scanline sampler instead of consuming an
    // unbounded amount of the renderer's 128 MiB cache budget.
    const uint64_t byteCount = static_cast<uint64_t>(width) *
        static_cast<uint64_t>(height) * 4u;
    if (byteCount > kMaxGradientRasterCacheBytes || byteCount > SIZE_MAX) return {};

    auto raster = std::make_shared<SoftwareGradientRaster>();
    raster->x = x;
    raster->y = y;
    raster->width = width;
    raster->height = height;
    raster->left = left;
    raster->top = top;
    raster->right = right;
    raster->bottom = bottom;
    raster->opacity = opacity;
    raster->opaque = true;
    raster->pixels.resize(static_cast<size_t>(byteCount));

    for (int32_t row = 0; row < height; ++row) {
        const float sampleY = static_cast<float>(y + row) + 0.5f;
        uint8_t* destination = raster->pixels.data() +
            static_cast<size_t>(row) * static_cast<size_t>(width) * 4u;
        for (int32_t column = 0; column < width; ++column, destination += 4) {
            uint8_t red, green, blue;
            float alpha;
            brush.SampleColor8(
                static_cast<float>(x + column) + 0.5f, sampleY,
                red, green, blue, alpha);
            const uint8_t alphaByte = FloatToU8(alpha * opacity);
            destination[0] = blue;
            destination[1] = green;
            destination[2] = red;
            destination[3] = alphaByte;
            raster->opaque = raster->opaque && alphaByte == 255;
        }
    }
    return raster;
}

std::shared_ptr<const SoftwareGradientRaster>
SoftwareLinearGradientBrush::GetOrCreateRaster(
    float left, float top, float right, float bottom, float opacity) const
{
    {
        std::lock_guard<std::mutex> lock(rasterCacheMutex_);
        for (size_t index = 0; index < rasterCache_.size(); ++index) {
            if (!GradientRasterMatches(
                    *rasterCache_[index], left, top, right, bottom, opacity)) continue;
            auto hit = rasterCache_[index];
            if (index + 1u != rasterCache_.size()) {
                rasterCache_.erase(rasterCache_.begin() + index);
                rasterCache_.push_back(hit);
            }
            return hit;
        }
    }

    auto raster = BuildGradientRaster(
        *this, left, top, right, bottom, opacity);
    if (!raster) return {};

    std::lock_guard<std::mutex> lock(rasterCacheMutex_);
    for (size_t index = 0; index < rasterCache_.size(); ++index) {
        if (GradientRasterMatches(
                *rasterCache_[index], left, top, right, bottom, opacity)) {
            return rasterCache_[index];
        }
    }
    const size_t bytes = raster->pixels.size();
    while (!rasterCache_.empty() &&
           rasterCacheBytes_ + bytes > kMaxGradientRasterCacheBytes) {
        ReleaseSoftwareResourceCache(rasterCache_.front()->pixels.size());
        rasterCacheBytes_ -= rasterCache_.front()->pixels.size();
        rasterCache_.erase(rasterCache_.begin());
    }
    if (!TryReserveSoftwareResourceCache(bytes)) return {};
    rasterCacheBytes_ += bytes;
    rasterCache_.push_back(std::move(raster));
    return rasterCache_.back();
}

std::shared_ptr<const SoftwareGradientRaster>
SoftwareRadialGradientBrush::GetOrCreateRaster(
    float left, float top, float right, float bottom, float opacity) const
{
    {
        std::lock_guard<std::mutex> lock(rasterCacheMutex_);
        for (size_t index = 0; index < rasterCache_.size(); ++index) {
            if (!GradientRasterMatches(
                    *rasterCache_[index], left, top, right, bottom, opacity)) continue;
            auto hit = rasterCache_[index];
            if (index + 1u != rasterCache_.size()) {
                rasterCache_.erase(rasterCache_.begin() + index);
                rasterCache_.push_back(hit);
            }
            return hit;
        }
    }

    auto raster = BuildGradientRaster(
        *this, left, top, right, bottom, opacity);
    if (!raster) return {};

    std::lock_guard<std::mutex> lock(rasterCacheMutex_);
    for (size_t index = 0; index < rasterCache_.size(); ++index) {
        if (GradientRasterMatches(
                *rasterCache_[index], left, top, right, bottom, opacity)) {
            return rasterCache_[index];
        }
    }
    const size_t bytes = raster->pixels.size();
    while (!rasterCache_.empty() &&
           rasterCacheBytes_ + bytes > kMaxGradientRasterCacheBytes) {
        ReleaseSoftwareResourceCache(rasterCache_.front()->pixels.size());
        rasterCacheBytes_ -= rasterCache_.front()->pixels.size();
        rasterCache_.erase(rasterCache_.begin());
    }
    if (!TryReserveSoftwareResourceCache(bytes)) return {};
    rasterCacheBytes_ += bytes;
    rasterCache_.push_back(std::move(raster));
    return rasterCache_.back();
}

// ============================================================================
// SoftwareTextFormat
// ============================================================================

JaliumResult SoftwareTextFormat::MeasureText(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;

#ifdef _WIN32
    // Use GDI for accurate text measurement. fontSize is a DIP em size (the
    // DirectWrite convention every backend shares); a negative lfHeight selects
    // the GDI character (em) height, and 1 DIP == 1 px in the 96-DPI layout
    // space this measurement reports in — no point conversion.
    HDC hdc = CreateCompatibleDC(nullptr);
    if (hdc) {
        int fontHeight = -(std::max)(1, (int)(fontSize + 0.5f));
        HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
            fontWeight, (fontStyle == 1 || fontStyle == 2) ? TRUE : FALSE,
            FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
            fontFamily.c_str());
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        TEXTMETRICW tm{};
        BOOL haveTm = GetTextMetricsW(hdc, &tm);
        SIZE extentIncludingTrailingWhitespace{};
        BOOL haveIncludingExtent = GetTextExtentPoint32W(hdc, text, static_cast<int>(textLength), &extentIncludingTrailingWhitespace);

        // DT_EXTERNALLEADING makes DrawText advance lines by
        // tmHeight + tmExternalLeading instead of bare tmHeight, matching how
        // DirectWrite spaces lines (lineGap participates). It must be set both
        // here and in RenderTextWithGDI so measured heights, the reported
        // lineHeight, and the painted line advance are one ruler. DT_NOPREFIX
        // stops GDI from eating '&' as an accelerator marker (mnemonic
        // underlining is handled by the managed AccessText layer).
        RECT rc = { 0, 0, maxWidth > 0 ? (LONG)maxWidth : 10000, maxHeight > 0 ? (LONG)maxHeight : 10000 };
        UINT dtFlags = DT_CALCRECT | DT_WORDBREAK | DT_EXTERNALLEADING | DT_NOPREFIX;
        DrawTextW(hdc, text, textLength, &rc, dtFlags);

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        DeleteDC(hdc);

        if (haveTm && tm.tmHeight > 0) {
            // WPF-style lineHeight = ascent + descent + lineGap, i.e. GDI's
            // tmHeight + tmExternalLeading — exactly the DT_EXTERNALLEADING
            // line advance, so lineCount recovers N exactly from the
            // DT_CALCRECT height. Keeping this and GetFontMetrics on the same
            // GDI ruler is what lets the managed layout box match what
            // RenderTextWithGDI paints.
            metrics->width = (float)(rc.right - rc.left);
            metrics->widthIncludingTrailingWhitespace = haveIncludingExtent
                ? static_cast<float>(extentIncludingTrailingWhitespace.cx)
                : metrics->width;
            metrics->height = (float)(rc.bottom - rc.top);
            metrics->lineHeight = (float)(tm.tmHeight + tm.tmExternalLeading);
            metrics->baseline = (float)tm.tmAscent;
            metrics->ascent = (float)tm.tmAscent;
            metrics->descent = (float)tm.tmDescent;
            metrics->lineGap = (float)tm.tmExternalLeading;
            metrics->lineCount = (uint32_t)((metrics->height + tm.tmExternalLeading) / metrics->lineHeight);
            if (metrics->lineCount == 0) metrics->lineCount = 1;
            return JALIUM_OK;
        }
    }
#endif

    // Fallback: approximate text measurement based on font metrics
    float charWidth = fontSize * 0.6f;
    float lineHeight = fontSize * 1.2f;
    float ascent = fontSize * 0.8f;
    float descent = fontSize * 0.2f;
    float leading = fontSize * 0.2f;

    float totalWidth = textLength * charWidth;
    uint32_t lineCount = 1;

    if (maxWidth > 0 && totalWidth > maxWidth) {
        uint32_t charsPerLine = std::max(1u, (uint32_t)(maxWidth / charWidth));
        lineCount = (textLength + charsPerLine - 1) / charsPerLine;
        totalWidth = std::min(totalWidth, maxWidth);
    }

    float totalHeight = lineCount * lineHeight;
    if (maxHeight > 0) totalHeight = std::min(totalHeight, maxHeight);

    metrics->width = totalWidth;
    metrics->widthIncludingTrailingWhitespace = totalWidth;
    metrics->height = totalHeight;
    metrics->lineHeight = lineHeight;
    metrics->baseline = ascent;
    metrics->ascent = ascent;
    metrics->descent = descent;
    metrics->lineGap = leading;
    metrics->lineCount = lineCount;

    (void)text; (void)maxHeight;
    return JALIUM_OK;
}

JaliumResult SoftwareTextFormat::GetFontUnitMetrics(JaliumFontUnitMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    JaliumTextMetrics line{};
    GetFontMetrics(&line);
    *metrics = {sizeof(JaliumFontUnitMetrics), fontSize * .5f, line.ascent,
        fontSize * .5f, fontSize, line.ascent, line.lineHeight, 0};
#ifdef _WIN32
    HDC dc = CreateCompatibleDC(nullptr);
    if (!dc) return JALIUM_OK;
    HFONT font = CreateFontW(-(std::max)(1, (int)(fontSize + .5f)), 0, 0, 0,
        fontWeight, fontStyle == 1 || fontStyle == 2, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH, fontFamily.c_str());
    HGDIOBJ previous = SelectObject(dc, font);
    OUTLINETEXTMETRICW outline{}; outline.otmSize = sizeof(outline);
    if (GetOutlineTextMetricsW(dc, sizeof(outline), &outline)) {
        metrics->available |= 16;
        if (outline.otmsXHeight > 0) { metrics->xHeight = static_cast<float>(outline.otmsXHeight); metrics->available |= 1; }
        if (outline.otmsCapEmHeight > 0) { metrics->capHeight = static_cast<float>(outline.otmsCapEmHeight); metrics->available |= 2; }
    }
    auto advance = [&](wchar_t character, float& value, uint32_t flag) {
        WORD glyph = 0; SIZE size{};
        if (GetGlyphIndicesW(dc, &character, 1, &glyph, GGI_MARK_NONEXISTING_GLYPHS) != GDI_ERROR && glyph != 0xffff &&
            GetTextExtentPoint32W(dc, &character, 1, &size)) {
            value = static_cast<float>(size.cx); metrics->available |= flag;
        }
    };
    advance(L'0', metrics->zeroAdvance, 4);
    advance(L'\x6c34', metrics->ideographicAdvance, 8);
    SelectObject(dc, previous); DeleteObject(font); DeleteDC(dc);
#endif
    return JALIUM_OK;
}

JaliumResult SoftwareTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::memset(metrics, 0, sizeof(JaliumTextMetrics));

#ifdef _WIN32
    // Same GDI ruler as MeasureText (DIP em height): the managed layout sizes
    // line boxes from these metrics and RenderTextWithGDI paints with the same
    // font, so the two must agree or glyphs get clipped against their own line
    // box. The managed side caches per (family, size, weight, style), so the
    // DC round-trip here is a cache-miss-only cost.
    HDC hdc = CreateCompatibleDC(nullptr);
    if (hdc) {
        int fontHeight = -(std::max)(1, (int)(fontSize + 0.5f));
        HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
            fontWeight, (fontStyle == 1 || fontStyle == 2) ? TRUE : FALSE,
            FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
            fontFamily.c_str());
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        TEXTMETRICW tm{};
        BOOL ok = GetTextMetricsW(hdc, &tm);

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        DeleteDC(hdc);

        if (ok && tm.tmHeight > 0) {
            metrics->lineHeight = (float)(tm.tmHeight + tm.tmExternalLeading);
            metrics->baseline = (float)tm.tmAscent;
            metrics->ascent = (float)tm.tmAscent;
            metrics->descent = (float)tm.tmDescent;
            metrics->lineGap = (float)tm.tmExternalLeading;
            return JALIUM_OK;
        }
    }
#endif

    metrics->lineHeight = fontSize * 1.2f;
    metrics->baseline = fontSize * 0.8f;
    metrics->ascent = fontSize * 0.8f;
    metrics->descent = fontSize * 0.2f;
    metrics->lineGap = fontSize * 0.2f;
    return JALIUM_OK;
}

// ============================================================================
// Helper: Box Blur (separable two-pass)
// ============================================================================

void SoftwareRenderTarget::BoxBlur(std::vector<uint8_t>& pixels, int32_t w, int32_t h, int32_t radius)
{
    if (radius <= 0 || w <= 0 || h <= 0) return;
    // Three-pass box blur approximates Gaussian blur
    blurScratch_.resize(pixels.size());
    auto& temp = blurScratch_;

    auto blurPass = [&](std::vector<uint8_t>& src, std::vector<uint8_t>& dst, bool horizontal) {
        int32_t outerLimit = horizontal ? h : w;
        int32_t innerLimit = horizontal ? w : h;

        auto processOuter = [&](int32_t outerBegin, int32_t outerEnd) {
        for (int32_t outer = outerBegin; outer < outerEnd; outer++) {
            // Running sum for each channel
            int32_t sumR = 0, sumG = 0, sumB = 0, sumA = 0;
            int32_t count = 0;

            // Initialize window for first pixel
            for (int32_t k = -radius; k <= radius; k++) {
                int32_t idx = std::clamp(k, 0, innerLimit - 1);
                size_t pix;
                if (horizontal)
                    pix = ((size_t)outer * w + idx) * 4;
                else
                    pix = ((size_t)idx * w + outer) * 4;
                sumB += src[pix + 0];
                sumG += src[pix + 1];
                sumR += src[pix + 2];
                sumA += src[pix + 3];
                count++;
            }

            for (int32_t inner = 0; inner < innerLimit; inner++) {
                size_t outPix;
                if (horizontal)
                    outPix = ((size_t)outer * w + inner) * 4;
                else
                    outPix = ((size_t)inner * w + outer) * 4;

                dst[outPix + 0] = (uint8_t)(sumB / count);
                dst[outPix + 1] = (uint8_t)(sumG / count);
                dst[outPix + 2] = (uint8_t)(sumR / count);
                dst[outPix + 3] = (uint8_t)(sumA / count);

                // Slide window: add right/bottom, remove left/top
                int32_t addIdx = std::min(inner + radius + 1, innerLimit - 1);
                int32_t remIdx = std::max(inner - radius, 0);

                size_t addPix, remPix;
                if (horizontal) {
                    addPix = ((size_t)outer * w + addIdx) * 4;
                    remPix = ((size_t)outer * w + remIdx) * 4;
                } else {
                    addPix = ((size_t)addIdx * w + outer) * 4;
                    remPix = ((size_t)remIdx * w + outer) * 4;
                }

                if (inner + radius + 1 < innerLimit) {
                    sumB += src[addPix + 0] - src[remPix + 0];
                    sumG += src[addPix + 1] - src[remPix + 1];
                    sumR += src[addPix + 2] - src[remPix + 2];
                    sumA += src[addPix + 3] - src[remPix + 3];
                } else if (inner - radius >= 0) {
                    sumB -= src[remPix + 0];
                    sumG -= src[remPix + 1];
                    sumR -= src[remPix + 2];
                    sumA -= src[remPix + 3];
                    // Add clamped edge pixel
                    size_t edgePix;
                    if (horizontal)
                        edgePix = ((size_t)outer * w + (innerLimit - 1)) * 4;
                    else
                        edgePix = ((size_t)(innerLimit - 1) * w + outer) * 4;
                    sumB += src[edgePix + 0];
                    sumG += src[edgePix + 1];
                    sumR += src[edgePix + 2];
                    sumA += src[edgePix + 3];
                }
            }
        }
        };

        SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
        const uint64_t pixelWork = static_cast<uint64_t>(outerLimit) *
            static_cast<uint64_t>(innerLimit);
        if (workerPool && workerPool->CanParallelize() &&
            pixelWork >= 128u * 1024u && outerLimit >= 16) {
            const int32_t grain = horizontal ? 8 : 16;
            workerPool->ParallelFor(0, outerLimit, grain, processOuter);
        } else {
            processOuter(0, outerLimit);
        }
    };

    // Three-pass box blur (approximates Gaussian)
    for (int pass = 0; pass < 3; pass++) {
        blurPass(pixels, temp, true);   // horizontal
        blurPass(temp, pixels, false);  // vertical
    }
}

void SoftwareRenderTarget::CopyRegion(const SoftwareFramebuffer& src, SoftwareFramebuffer& dst,
    int32_t srcX, int32_t srcY, int32_t w, int32_t h)
{
    if (w <= 0 || h <= 0) {
        dst.Resize(0, 0);
        return;
    }
    dst.Resize(w, h);
    const int32_t sourceLeft = std::max(srcX, 0);
    const int32_t sourceTop = std::max(srcY, 0);
    const int32_t sourceRight = std::min(srcX + w, src.width);
    const int32_t sourceBottom = std::min(srcY + h, src.height);
    if (sourceRight <= sourceLeft || sourceBottom <= sourceTop) return;

    const int32_t destinationX = sourceLeft - srcX;
    const int32_t destinationY = sourceTop - srcY;
    const size_t rowBytes = static_cast<size_t>(sourceRight - sourceLeft) * 4u;
    auto copyRows = [&](int32_t rowBegin, int32_t rowEnd) {
        for (int32_t row = rowBegin; row < rowEnd; ++row) {
            const int32_t sourceRow = sourceTop + row;
            const int32_t destinationRow = destinationY + row;
            const uint8_t* source = src.pixels.data() +
                (static_cast<size_t>(sourceRow) * src.width + sourceLeft) * 4u;
            uint8_t* destination = dst.pixels.data() +
                (static_cast<size_t>(destinationRow) * w + destinationX) * 4u;
            std::memcpy(destination, source, rowBytes);
        }
    };
    const int32_t rows = sourceBottom - sourceTop;
    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    if (workerPool && workerPool->CanParallelize() &&
        static_cast<uint64_t>(rowBytes) * rows >= 512u * 1024u) {
        workerPool->ParallelFor(0, rows, 16, copyRows);
    } else {
        copyRows(0, rows);
    }
}

void SoftwareRenderTarget::RestoreRegion(
    const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY)
{
    RestoreRawRegion(src.pixels.data(), src.width, src.height, dstX, dstY);
}

void SoftwareRenderTarget::RestoreRawRegion(
    const uint8_t* pixels, int32_t width, int32_t height,
    int32_t dstX, int32_t dstY)
{
    if (!pixels || width <= 0 || height <= 0) return;
    const int32_t sourceLeft = std::max(0, -dstX);
    const int32_t sourceTop = std::max(0, -dstY);
    const int32_t sourceRight = std::min(width, fb_.width - dstX);
    const int32_t sourceBottom = std::min(height, fb_.height - dstY);
    if (sourceRight <= sourceLeft || sourceBottom <= sourceTop) return;

    const int32_t destinationX = dstX + sourceLeft;
    const int32_t destinationY = dstY + sourceTop;
    const size_t rowBytes = static_cast<size_t>(sourceRight - sourceLeft) * 4u;
    auto copyRows = [&](int32_t rowBegin, int32_t rowEnd) {
        for (int32_t row = rowBegin; row < rowEnd; ++row) {
            const uint8_t* source = pixels +
                (static_cast<size_t>(sourceTop + row) * width + sourceLeft) * 4u;
            uint8_t* destination = fb_.pixels.data() +
                (static_cast<size_t>(destinationY + row) * fb_.width +
                 destinationX) * 4u;
            std::memcpy(destination, source, rowBytes);
        }
    };

    const int32_t rows = sourceBottom - sourceTop;
    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    if (workerPool && workerPool->CanParallelize() &&
        static_cast<uint64_t>(rowBytes) * rows >= 512u * 1024u) {
        workerPool->ParallelFor(0, rows, 16, copyRows);
    } else {
        copyRows(0, rows);
    }
}

void SoftwareRenderTarget::BlitBuffer(const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY, float opacity)
{
    BlitRawBuffer(src.pixels.data(), src.width, src.height, dstX, dstY, opacity);
}

void SoftwareRenderTarget::BlitRawBuffer(
    const uint8_t* pixels, int32_t width, int32_t height,
    int32_t dstX, int32_t dstY, float opacity)
{
    if (!pixels || width <= 0 || height <= 0) return;
    const int32_t sourceX0 = std::max(0, -dstX);
    const int32_t sourceY0 = std::max(0, -dstY);
    const int32_t sourceX1 = std::min(width, fb_.width - dstX);
    const int32_t sourceY1 = std::min(height, fb_.height - dstY);
    if (sourceX1 <= sourceX0 || sourceY1 <= sourceY0) return;

    auto blendRows = [&](int32_t rowBegin, int32_t rowEnd) {
    for (int32_t row = rowBegin; row < rowEnd; row++) {
        const int32_t dy = dstY + row;
        int32_t col = sourceX0;
        while (col < sourceX1) {
            const int32_t dx = dstX + col;
            size_t srcIdx = ((size_t)row * width + col) * 4;
            if (opacity >= 0.999999f && pixels[srcIdx + 3] == 255) {
                const int32_t runStart = col;
                do {
                    ++col;
                    if (col >= sourceX1) break;
                    srcIdx = ((size_t)row * width + col) * 4;
                } while (pixels[srcIdx + 3] == 255);
                const size_t runBytes = static_cast<size_t>(col - runStart) * 4u;
                const uint8_t* source = pixels +
                    (static_cast<size_t>(row) * width + runStart) * 4u;
                uint8_t* destination = fb_.pixels.data() +
                    (static_cast<size_t>(dy) * fb_.width + dstX + runStart) * 4u;
                std::memcpy(destination, source, runBytes);
                continue;
            }
            uint8_t sb = pixels[srcIdx + 0];
            uint8_t sg = pixels[srcIdx + 1];
            uint8_t sr = pixels[srcIdx + 2];
            uint8_t sa = (uint8_t)(pixels[srcIdx + 3] * opacity);
            if (sa > 0)
                fb_.BlendPixelUnchecked(dx, dy, sr, sg, sb, sa);
            ++col;
        }
    }
    };
    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    const uint64_t pixelWork = static_cast<uint64_t>(sourceX1 - sourceX0) *
        static_cast<uint64_t>(sourceY1 - sourceY0);
    if (workerPool && workerPool->CanParallelize() && pixelWork >= 256u * 1024u) {
        workerPool->ParallelFor(sourceY0, sourceY1, 16, blendRows);
    } else {
        blendRows(sourceY0, sourceY1);
    }
}

static void BlendBufferInto(
    SoftwareFramebuffer& destination,
    const SoftwareFramebuffer& source,
    int32_t destinationX, int32_t destinationY,
    float opacity)
{
    const int32_t sourceX0 = std::max(0, -destinationX);
    const int32_t sourceY0 = std::max(0, -destinationY);
    const int32_t sourceX1 = std::min(
        source.width, destination.width - destinationX);
    const int32_t sourceY1 = std::min(
        source.height, destination.height - destinationY);
    if (sourceX1 <= sourceX0 || sourceY1 <= sourceY0) return;
    for (int32_t row = sourceY0; row < sourceY1; ++row) {
        for (int32_t column = sourceX0; column < sourceX1; ++column) {
            const size_t index =
                (static_cast<size_t>(row) * source.width + column) * 4u;
            const uint8_t alpha = static_cast<uint8_t>(
                source.pixels[index + 3] * opacity);
            if (alpha == 0) continue;
            destination.BlendPixelUnchecked(
                destinationX + column, destinationY + row,
                source.pixels[index + 2], source.pixels[index + 1],
                source.pixels[index], alpha);
        }
    }
}

// ============================================================================
// Helper: Adaptive Bezier Flattening
// ============================================================================

void SoftwareRenderTarget::FlattenCubicBezier(std::vector<float>& pts,
    float x0, float y0, float cp1x, float cp1y,
    float cp2x, float cp2y, float x1, float y1, float tolerance)
{
    // Flatness test: max distance of control points from line (x0,y0)→(x1,y1)
    float dx = x1 - x0, dy = y1 - y0;
    float d1 = std::abs((cp1x - x1) * dy - (cp1y - y1) * dx);
    float d2 = std::abs((cp2x - x1) * dy - (cp2y - y1) * dx);
    float dSq = dx * dx + dy * dy;

    if ((d1 + d2) * (d1 + d2) <= tolerance * tolerance * dSq || dSq < 0.25f) {
        pts.push_back(x1);
        pts.push_back(y1);
        return;
    }

    // Subdivide at t=0.5
    float m01x = (x0 + cp1x) * 0.5f, m01y = (y0 + cp1y) * 0.5f;
    float m12x = (cp1x + cp2x) * 0.5f, m12y = (cp1y + cp2y) * 0.5f;
    float m23x = (cp2x + x1) * 0.5f, m23y = (cp2y + y1) * 0.5f;
    float m012x = (m01x + m12x) * 0.5f, m012y = (m01y + m12y) * 0.5f;
    float m123x = (m12x + m23x) * 0.5f, m123y = (m12y + m23y) * 0.5f;
    float mx = (m012x + m123x) * 0.5f, my = (m012y + m123y) * 0.5f;

    FlattenCubicBezier(pts, x0, y0, m01x, m01y, m012x, m012y, mx, my, tolerance);
    FlattenCubicBezier(pts, mx, my, m123x, m123y, m23x, m23y, x1, y1, tolerance);
}

void SoftwareRenderTarget::FlattenQuadBezier(std::vector<float>& pts,
    float x0, float y0, float cpx, float cpy,
    float x1, float y1, float tolerance)
{
    // Flatness test
    float dx = x1 - x0, dy = y1 - y0;
    float d = std::abs((cpx - x1) * dy - (cpy - y1) * dx);
    float dSq = dx * dx + dy * dy;

    if (d * d <= tolerance * tolerance * dSq || dSq < 0.25f) {
        pts.push_back(x1);
        pts.push_back(y1);
        return;
    }

    // Subdivide at t=0.5
    float m01x = (x0 + cpx) * 0.5f, m01y = (y0 + cpy) * 0.5f;
    float m12x = (cpx + x1) * 0.5f, m12y = (cpy + y1) * 0.5f;
    float mx = (m01x + m12x) * 0.5f, my = (m01y + m12y) * 0.5f;

    FlattenQuadBezier(pts, x0, y0, m01x, m01y, mx, my, tolerance);
    FlattenQuadBezier(pts, mx, my, m12x, m12y, x1, y1, tolerance);
}

// ============================================================================
// Helper: Stroke Outline Generation
// ============================================================================

void SoftwareRenderTarget::GenerateStrokeOutline(const std::vector<float>& pts, uint32_t ptCount,
    float strokeWidth, bool closed, int32_t lineJoin, float miterLimit,
    int32_t lineCap, std::vector<std::vector<float>>& outContours)
{
    if (ptCount < 2) return;
    float halfW = strokeWidth * 0.5f;
    outContours.clear();

    struct Vec2 { float x, y; };

    uint32_t segCount = closed ? ptCount : ptCount - 1;
    std::vector<Vec2> normals(segCount);
    for (uint32_t i = 0; i < segCount; i++) {
        uint32_t j = (i + 1) % ptCount;
        float dx = pts[j * 2] - pts[i * 2];
        float dy = pts[j * 2 + 1] - pts[i * 2 + 1];
        float len = std::sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) len = 1e-6f;
        normals[i] = { -dy / len, dx / len };
    }

    std::vector<float> leftSide, rightSide;

    auto emitJoint = [&](float px, float py, const Vec2& n0, const Vec2& n1) {
        float avgNx = n0.x + n1.x, avgNy = n0.y + n1.y;
        float avgLen = std::sqrt(avgNx * avgNx + avgNy * avgNy);

        if (avgLen < 1e-6f) {
            leftSide.push_back(px + n0.x * halfW);
            leftSide.push_back(py + n0.y * halfW);
            rightSide.push_back(px - n0.x * halfW);
            rightSide.push_back(py - n0.y * halfW);
            return;
        }
        avgNx /= avgLen;
        avgNy /= avgLen;

        float dot = n0.x * n1.x + n0.y * n1.y;
        float miterLen = halfW / std::max(0.001f, std::sqrt(0.5f * (1.0f + dot)));

        if (lineJoin == 2) {
            // Round join
            float angle0 = std::atan2(n0.y, n0.x);
            float angle1 = std::atan2(n1.y, n1.x);
            float diff = angle1 - angle0;
            if (diff > 3.14159f) diff -= 6.28318f;
            if (diff < -3.14159f) diff += 6.28318f;
            int segs = std::max(2, (int)(std::abs(diff) * halfW / 2));
            for (int s = 0; s <= segs; s++) {
                float a = angle0 + diff * s / segs;
                leftSide.push_back(px + std::cos(a) * halfW);
                leftSide.push_back(py + std::sin(a) * halfW);
            }
            for (int s = 0; s <= segs; s++) {
                float a = angle0 + 3.14159f + diff * s / segs;
                rightSide.push_back(px + std::cos(a) * halfW);
                rightSide.push_back(py + std::sin(a) * halfW);
            }
        } else if (lineJoin == 1 || miterLen > halfW * miterLimit) {
            // Bevel join
            leftSide.push_back(px + n0.x * halfW);
            leftSide.push_back(py + n0.y * halfW);
            leftSide.push_back(px + n1.x * halfW);
            leftSide.push_back(py + n1.y * halfW);
            rightSide.push_back(px - n0.x * halfW);
            rightSide.push_back(py - n0.y * halfW);
            rightSide.push_back(px - n1.x * halfW);
            rightSide.push_back(py - n1.y * halfW);
        } else {
            // Miter join
            leftSide.push_back(px + avgNx * miterLen);
            leftSide.push_back(py + avgNy * miterLen);
            rightSide.push_back(px - avgNx * miterLen);
            rightSide.push_back(py - avgNy * miterLen);
        }
    };

    for (uint32_t i = 0; i < ptCount; i++) {
        float px = pts[i * 2], py = pts[i * 2 + 1];

        if (!closed && i == 0) {
            float nx = normals[0].x, ny = normals[0].y;
            if (lineCap == 1) {
                leftSide.push_back(px + nx * halfW - ny * halfW);
                leftSide.push_back(py + ny * halfW + nx * halfW);
            } else if (lineCap == 2) {
                float baseAngle = std::atan2(ny, nx);
                int segs = std::max(4, (int)(halfW * 2));
                for (int s = segs; s >= 0; s--) {
                    float a = baseAngle - 3.14159f * s / segs;
                    leftSide.push_back(px + std::cos(a) * halfW);
                    leftSide.push_back(py + std::sin(a) * halfW);
                }
            } else {
                leftSide.push_back(px + nx * halfW);
                leftSide.push_back(py + ny * halfW);
            }
            rightSide.push_back(px - nx * halfW);
            rightSide.push_back(py - ny * halfW);
        } else if (!closed && i == ptCount - 1) {
            uint32_t lastSeg = segCount - 1;
            float nx = normals[lastSeg].x, ny = normals[lastSeg].y;
            leftSide.push_back(px + nx * halfW);
            leftSide.push_back(py + ny * halfW);
            if (lineCap == 1) {
                rightSide.push_back(px - nx * halfW + ny * halfW);
                rightSide.push_back(py - ny * halfW - nx * halfW);
            } else if (lineCap == 2) {
                float baseAngle = std::atan2(-ny, -nx);
                int segs = std::max(4, (int)(halfW * 2));
                for (int s = 0; s <= segs; s++) {
                    float a = baseAngle - 3.14159f * s / segs;
                    rightSide.push_back(px + std::cos(a) * halfW);
                    rightSide.push_back(py + std::sin(a) * halfW);
                }
            } else {
                rightSide.push_back(px - nx * halfW);
                rightSide.push_back(py - ny * halfW);
            }
        } else {
            uint32_t prevSeg = (i == 0) ? segCount - 1 : i - 1;
            uint32_t nextSeg = i % segCount;
            emitJoint(px, py, normals[prevSeg], normals[nextSeg]);
        }
    }

    if (closed) {
        // For closed paths: output two separate closed contours (outer + inner).
        // rightSide = outer contour (offset away from path, same winding as path).
        // leftSide reversed = inner contour (opposite winding, creates the hole).
        if (rightSide.size() >= 6)
            outContours.push_back(rightSide);
        if (leftSide.size() >= 6) {
            // Reverse leftSide to give opposite winding
            std::vector<float> innerReversed;
            innerReversed.reserve(leftSide.size());
            for (size_t i = leftSide.size(); i >= 2; i -= 2) {
                innerReversed.push_back(leftSide[i - 2]);
                innerReversed.push_back(leftSide[i - 1]);
            }
            outContours.push_back(std::move(innerReversed));
        }
    } else {
        // For open paths: combine left + reversed right into one closed polygon
        std::vector<float> poly;
        poly.reserve(leftSide.size() + rightSide.size());
        for (size_t i = 0; i < leftSide.size(); i++)
            poly.push_back(leftSide[i]);
        for (size_t i = rightSide.size(); i >= 2; i -= 2) {
            poly.push_back(rightSide[i - 2]);
            poly.push_back(rightSide[i - 1]);
        }
        if (poly.size() >= 6)
            outContours.push_back(std::move(poly));
    }
}

void SoftwareRenderTarget::FillMultiContour(const std::vector<std::vector<float>>& contours, Brush* brush)
{
    if (!brush || contours.empty()) return;

    // Collect all edges from all contours, transform, compute bounds
    struct Edge { float x0, y0, x1, y1; };
    std::vector<Edge> allEdges;
    float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;

    for (auto& contour : contours) {
        uint32_t pc = (uint32_t)(contour.size() / 2);
        if (pc < 3) continue;

        std::vector<float> tpts(contour.size());
        for (uint32_t j = 0; j < pc; j++) {
            currentTransform_.Apply(contour[j * 2], contour[j * 2 + 1],
                tpts[j * 2], tpts[j * 2 + 1]);
            minX = std::min(minX, tpts[j * 2]);
            maxX = std::max(maxX, tpts[j * 2]);
            minY = std::min(minY, tpts[j * 2 + 1]);
            maxY = std::max(maxY, tpts[j * 2 + 1]);
        }

        for (uint32_t j = 0; j < pc; j++) {
            uint32_t k = (j + 1) % pc;
            allEdges.push_back({tpts[j * 2], tpts[j * 2 + 1],
                                tpts[k * 2], tpts[k * 2 + 1]});
        }
    }

    if (allEdges.empty()) return;

    int32_t iy0 = std::max(0, (int32_t)minY);
    int32_t iy1 = std::min(height_, (int32_t)(maxY + 1));

    // NonZero winding fill across all contours
    for (int32_t scanY = iy0; scanY < iy1; scanY++) {
        float sy = (float)scanY + 0.5f;

        std::vector<std::pair<float, int>> crossings;
        for (auto& e : allEdges) {
            if ((e.y0 <= sy && e.y1 > sy) || (e.y1 <= sy && e.y0 > sy)) {
                float t = (sy - e.y0) / (e.y1 - e.y0);
                float ix = e.x0 + t * (e.x1 - e.x0);
                int dir = (e.y1 > e.y0) ? 1 : -1;
                crossings.push_back({ix, dir});
            }
        }
        std::sort(crossings.begin(), crossings.end(),
            [](const auto& a, const auto& b) { return a.first < b.first; });

        int winding = 0;
        for (size_t ci = 0; ci < crossings.size(); ci++) {
            int prevW = winding;
            winding += crossings[ci].second;

            // Fill span when transitioning between zero and non-zero
            if ((prevW != 0) && (winding == 0)) {
                // End of filled span — find where it started
                float spanStart = crossings[ci].first;
                int w2 = 0;
                for (size_t k = 0; k <= ci; k++) {
                    int prev2 = w2;
                    w2 += crossings[k].second;
                    if (prev2 == 0 && w2 != 0) spanStart = crossings[k].first;
                }
                int32_t xStart = std::max(0, (int32_t)spanStart);
                int32_t xEnd = std::min(width_ - 1, (int32_t)crossings[ci].first);
                for (int32_t x = xStart; x <= xEnd; x++) {
                    if (!clipStack_.empty() && IsClipped((float)x, (float)scanY)) continue;
                    uint8_t r, g, b, a;
                    GetBrushColor(brush, (float)x, (float)scanY, r, g, b, a);
                    fb_.BlendPixel(x, scanY, r, g, b, a);
                }
            }
        }
    }
}

void SoftwareRenderTarget::ApplyDashPattern(const std::vector<float>& pts, uint32_t ptCount,
    const float* dashPattern, uint32_t dashCount, float dashOffset,
    std::vector<std::vector<float>>& segments)
{
    if (ptCount < 2 || dashCount == 0) return;

    // Compute total pattern length
    float patternLen = 0;
    for (uint32_t i = 0; i < dashCount; i++) patternLen += dashPattern[i];
    if (patternLen <= 0) return;

    // Normalize offset into pattern
    float offset = std::fmod(dashOffset, patternLen);
    if (offset < 0) offset += patternLen;

    // Walk along the polyline, producing dash segments
    float dist = -offset; // start behind by offset
    uint32_t dashIdx = 0;
    bool drawing = true;
    float dashRemain = dashPattern[0];

    // Adjust for offset
    while (dist + dashRemain < 0) {
        dist += dashRemain;
        drawing = !drawing;
        dashIdx = (dashIdx + 1) % dashCount;
        dashRemain = dashPattern[dashIdx];
    }
    if (dist < 0) {
        dashRemain += dist;
        dist = 0;
    }

    std::vector<float> current;
    float accumDist = 0;

    for (uint32_t i = 0; i + 1 < ptCount; i++) {
        float x0 = pts[i * 2], y0 = pts[i * 2 + 1];
        float x1 = pts[(i + 1) * 2], y1 = pts[(i + 1) * 2 + 1];
        float segDx = x1 - x0, segDy = y1 - y0;
        float segLen = std::sqrt(segDx * segDx + segDy * segDy);
        if (segLen < 1e-6f) continue;

        float segConsumed = 0;
        while (segConsumed < segLen) {
            float remain = segLen - segConsumed;
            float take = std::min(remain, dashRemain);
            float t0 = segConsumed / segLen;
            float t1 = (segConsumed + take) / segLen;

            if (drawing) {
                if (current.empty()) {
                    current.push_back(x0 + segDx * t0);
                    current.push_back(y0 + segDy * t0);
                }
                current.push_back(x0 + segDx * t1);
                current.push_back(y0 + segDy * t1);
            }

            segConsumed += take;
            dashRemain -= take;

            if (dashRemain <= 1e-6f) {
                if (drawing && !current.empty()) {
                    segments.push_back(std::move(current));
                    current.clear();
                }
                drawing = !drawing;
                dashIdx = (dashIdx + 1) % dashCount;
                dashRemain = dashPattern[dashIdx];
            }
        }
    }

    if (drawing && !current.empty()) {
        segments.push_back(std::move(current));
    }
}

// ============================================================================
// SoftwareRenderTarget
// ============================================================================

#if defined(JALIUM_SOFTWARE_X11_PRESENT)
namespace {

bool X11EnvironmentFlag(const char* name)
{
    const std::string value = ReadSoftwareEnvironmentVariable(name);
    return !value.empty() && value[0] != '0';
}

unsigned long ScaleChannelToMask(uint8_t value, unsigned long mask)
{
    if (mask == 0) return 0;
    unsigned int shift = 0;
    while (((mask >> shift) & 1ul) == 0ul) ++shift;
    const unsigned long maximum = mask >> shift;
    const unsigned long scaled =
        (static_cast<unsigned long>(value) * maximum + 127ul) / 255ul;
    return (scaled << shift) & mask;
}

std::mutex g_x11ErrorTrapMutex;
Display* g_x11TrappedDisplay = nullptr;
bool g_x11TrappedError = false;
XErrorHandler g_x11PreviousErrorHandler = nullptr;

int HandleTrappedX11Error(Display* display, XErrorEvent* event)
{
    if (display == g_x11TrappedDisplay)
    {
        g_x11TrappedError = true;
        return 0;
    }
    return g_x11PreviousErrorHandler
        ? g_x11PreviousErrorHandler(display, event)
        : 0;
}

class X11ErrorTrap final {
public:
    explicit X11ErrorTrap(Display* display)
        : lock_(g_x11ErrorTrapMutex), display_(display)
    {
        // The platform initializes Xlib threading. Hold the display lock for
        // the whole request/error round trip so errors from another thread on
        // this Display cannot be attributed to this present operation.
        XLockDisplay(display_);
        g_x11TrappedDisplay = display_;
        g_x11TrappedError = false;
        previous_ = XSetErrorHandler(HandleTrappedX11Error);
        g_x11PreviousErrorHandler = previous_;
        // Drain earlier requests so an unrelated stale error cannot be
        // mistaken for an MIT-SHM attach/put failure. The temporary handler is
        // already installed, so a stale error is contained instead of invoking
        // Xlib's process-terminating default handler.
        XSync(display_, False);
        g_x11TrappedError = false;
    }

    ~X11ErrorTrap()
    {
        if (active_) Finish(false);
    }

    bool Finish(bool requestSucceeded)
    {
        if (!active_) return false;
        XSync(display_, False);
        const bool succeeded = requestSucceeded && !g_x11TrappedError;
        XSetErrorHandler(previous_);
        g_x11TrappedDisplay = nullptr;
        g_x11TrappedError = false;
        g_x11PreviousErrorHandler = nullptr;
        active_ = false;
        XUnlockDisplay(display_);
        return succeeded;
    }

private:
    std::unique_lock<std::mutex> lock_;
    Display* display_ = nullptr;
    XErrorHandler previous_ = nullptr;
    bool active_ = true;
};

} // namespace

class X11SoftwarePresenter final {
public:
    X11SoftwarePresenter(Display* display, ::Window window)
        : display_(display), window_(window),
          disableShm_(X11EnvironmentFlag("JALIUM_SOFTWARE_X11_DISABLE_SHM")),
          requireShm_(X11EnvironmentFlag("JALIUM_SOFTWARE_X11_REQUIRE_SHM"))
    {
#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
        shmInfo_.shmid = -1;
        shmInfo_.shmaddr = reinterpret_cast<char*>(-1);
#endif
    }

    ~X11SoftwarePresenter()
    {
        ResetImage();
        if (gc_ && display_) XFreeGC(display_, gc_);
    }

    bool Present(const uint8_t* bgraPixels, int32_t width, int32_t height,
                 int32_t sourceStride, int32_t left, int32_t top,
                 int32_t right, int32_t bottom)
    {
        if (!bgraPixels || width <= 0 || height <= 0 || width > INT32_MAX / 4 ||
            sourceStride < width * 4 || left < 0 || top < 0 ||
            right > width || bottom > height || left >= right || top >= bottom)
            return false;
        if (!EnsureImage(width, height)) return false;

        CopyPixels(bgraPixels, sourceStride, left, top, right, bottom);
        const unsigned int copyWidth = static_cast<unsigned int>(right - left);
        const unsigned int copyHeight = static_cast<unsigned int>(bottom - top);

#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
        if (usingShm_)
        {
            X11ErrorTrap trap(display_);
            const bool requested = XShmPutImage(
                display_, window_, gc_, image_,
                left, top, left, top, copyWidth, copyHeight, False) != False;
            if (trap.Finish(requested)) return true;

            // A remote/misconfigured X server can advertise MIT-SHM yet reject
            // a later put. Tear down the segment and retry this same frame via
            // the universally available XPutImage path.
            shmPermanentlyDisabled_ = true;
            ResetImage();
            if (requireShm_ || !EnsureImage(width, height)) return false;
            CopyPixels(bgraPixels, sourceStride, left, top, right, bottom);
        }
#endif

        // XPutImage errors (most importantly BadMatch from a visual/depth
        // mismatch) are asynchronous. Synchronize so EndDraw reports failure
        // instead of silently leaving a stale window.
        X11ErrorTrap trap(display_);
        XPutImage(display_, window_, gc_, image_,
                  left, top, left, top, copyWidth, copyHeight);
        return trap.Finish(true);
    }

    void InvalidateStorage()
    {
        ResetImage();
    }

private:
    bool EnsureImage(int32_t width, int32_t height)
    {
        XWindowAttributes attributes{};
        if (!display_ || !window_ || !XGetWindowAttributes(display_, window_, &attributes) ||
            !attributes.visual || attributes.depth <= 0)
            return false;

        const bool formatChanged = visual_ != attributes.visual || depth_ != attributes.depth;
        if (formatChanged)
        {
            ResetImage();
            if (gc_)
            {
                XFreeGC(display_, gc_);
                gc_ = nullptr;
            }
            visual_ = attributes.visual;
            depth_ = attributes.depth;
        }
        if (!gc_)
        {
            gc_ = XCreateGC(display_, window_, 0, nullptr);
            if (!gc_) return false;
        }
        if (image_ && imageWidth_ == width && imageHeight_ == height)
            return true;

        ResetImage();
#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
        if (!disableShm_ && !shmPermanentlyDisabled_ && XShmQueryExtension(display_))
        {
            if (CreateShmImage(width, height)) return true;
            shmPermanentlyDisabled_ = true;
        }
        if (requireShm_) return false;
#else
        if (requireShm_) return false;
#endif
        return CreateFallbackImage(width, height);
    }

    bool CreateFallbackImage(int32_t width, int32_t height)
    {
        image_ = XCreateImage(display_, visual_, static_cast<unsigned int>(depth_),
                              ZPixmap, 0, nullptr,
                              static_cast<unsigned int>(width),
                              static_cast<unsigned int>(height), 32, 0);
        if (!image_ || image_->bytes_per_line <= 0)
        {
            if (image_) XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        if (static_cast<size_t>(image_->bytes_per_line) >
            SIZE_MAX / static_cast<size_t>(height))
        {
            XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        const size_t byteCount = static_cast<size_t>(image_->bytes_per_line) * height;
        image_->data = static_cast<char*>(std::calloc(1, byteCount));
        if (!image_->data)
        {
            XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        imageWidth_ = width;
        imageHeight_ = height;
        usingShm_ = false;
        return true;
    }

#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
    bool CreateShmImage(int32_t width, int32_t height)
    {
        image_ = XShmCreateImage(
            display_, visual_, static_cast<unsigned int>(depth_), ZPixmap,
            nullptr, &shmInfo_,
            static_cast<unsigned int>(width), static_cast<unsigned int>(height));
        if (!image_ || image_->bytes_per_line <= 0)
        {
            if (image_) XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }

        if (static_cast<size_t>(image_->bytes_per_line) >
            SIZE_MAX / static_cast<size_t>(height))
        {
            XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        const size_t byteCount = static_cast<size_t>(image_->bytes_per_line) * height;
        shmInfo_.shmid = shmget(IPC_PRIVATE, byteCount, IPC_CREAT | 0600);
        if (shmInfo_.shmid < 0)
        {
            XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        shmInfo_.shmaddr = static_cast<char*>(shmat(shmInfo_.shmid, nullptr, 0));
        if (shmInfo_.shmaddr == reinterpret_cast<char*>(-1))
        {
            shmctl(shmInfo_.shmid, IPC_RMID, nullptr);
            shmInfo_.shmid = -1;
            XDestroyImage(image_);
            image_ = nullptr;
            return false;
        }
        shmInfo_.readOnly = False;
        image_->data = shmInfo_.shmaddr;

        X11ErrorTrap trap(display_);
        const bool requested = XShmAttach(display_, &shmInfo_) != False;
        if (!trap.Finish(requested))
        {
            // Detach defensively in case the request succeeded but a different
            // extension error was observed during the synchronized interval.
            X11ErrorTrap detachTrap(display_);
            XShmDetach(display_, &shmInfo_);
            (void)detachTrap.Finish(true);
            image_->data = nullptr;
            XDestroyImage(image_);
            image_ = nullptr;
            shmdt(shmInfo_.shmaddr);
            shmctl(shmInfo_.shmid, IPC_RMID, nullptr);
            shmInfo_.shmid = -1;
            shmInfo_.shmaddr = reinterpret_cast<char*>(-1);
            return false;
        }

        shmAttached_ = true;
        // The segment remains alive until both client and X server detach; mark
        // it now so a crash cannot leak a persistent SysV shared-memory object.
        shmMarkedForRemoval_ =
            shmctl(shmInfo_.shmid, IPC_RMID, nullptr) == 0;
        imageWidth_ = width;
        imageHeight_ = height;
        usingShm_ = true;
        return true;
    }
#endif

    void CopyPixels(const uint8_t* source, int32_t sourceStride,
                    int32_t left, int32_t top, int32_t right, int32_t bottom)
    {
        const bool standardBgrx = image_->bits_per_pixel == 32 &&
            image_->byte_order == LSBFirst &&
            image_->red_mask == 0x00ff0000ul &&
            image_->green_mask == 0x0000ff00ul &&
            image_->blue_mask == 0x000000fful && depth_ != 32;
        if (standardBgrx)
        {
            const size_t copyBytes = static_cast<size_t>(right - left) * 4u;
            for (int32_t y = top; y < bottom; ++y)
            {
                std::memcpy(
                    image_->data + static_cast<size_t>(y) * image_->bytes_per_line +
                        static_cast<size_t>(left) * 4u,
                    source + static_cast<size_t>(y) * sourceStride +
                        static_cast<size_t>(left) * 4u,
                    copyBytes);
            }
            return;
        }

        const unsigned long colorMask =
            image_->red_mask | image_->green_mask | image_->blue_mask;
        const unsigned long storageMask = depth_ >= static_cast<int>(sizeof(unsigned long) * 8u)
            ? ~0ul
            : ((1ul << depth_) - 1ul);
        const unsigned long alphaMask = depth_ == 32
            ? (storageMask & ~colorMask)
            : 0ul;
        for (int32_t y = top; y < bottom; ++y)
        {
            const uint8_t* sourceRow = source + static_cast<size_t>(y) * sourceStride;
            for (int32_t x = left; x < right; ++x)
            {
                const uint8_t* bgra = sourceRow + static_cast<size_t>(x) * 4u;
                const uint8_t alpha = bgra[3];
                // XRender's depth-32 ARGB visuals require premultiplied color;
                // the software framebuffer deliberately stores straight alpha.
                const uint8_t red = alphaMask
                    ? static_cast<uint8_t>((static_cast<unsigned int>(bgra[2]) * alpha + 127u) / 255u)
                    : bgra[2];
                const uint8_t green = alphaMask
                    ? static_cast<uint8_t>((static_cast<unsigned int>(bgra[1]) * alpha + 127u) / 255u)
                    : bgra[1];
                const uint8_t blue = alphaMask
                    ? static_cast<uint8_t>((static_cast<unsigned int>(bgra[0]) * alpha + 127u) / 255u)
                    : bgra[0];
                unsigned long pixel =
                    ScaleChannelToMask(red, image_->red_mask) |
                    ScaleChannelToMask(green, image_->green_mask) |
                    ScaleChannelToMask(blue, image_->blue_mask);
                if (alphaMask) pixel |= ScaleChannelToMask(alpha, alphaMask);
                XPutPixel(image_, x, y, pixel);
            }
        }
    }

    void ResetImage()
    {
        if (!image_) return;
#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
        if (usingShm_)
        {
            if (shmAttached_ && display_)
            {
                X11ErrorTrap trap(display_);
                XShmDetach(display_, &shmInfo_);
                (void)trap.Finish(true);
            }
            image_->data = nullptr;
            XDestroyImage(image_);
            if (shmInfo_.shmaddr != reinterpret_cast<char*>(-1))
                shmdt(shmInfo_.shmaddr);
            if (shmInfo_.shmid >= 0 && !shmMarkedForRemoval_)
                shmctl(shmInfo_.shmid, IPC_RMID, nullptr);
            shmInfo_ = {};
            shmInfo_.shmid = -1;
            shmInfo_.shmaddr = reinterpret_cast<char*>(-1);
            shmAttached_ = false;
            shmMarkedForRemoval_ = false;
        }
        else
#endif
        {
            XDestroyImage(image_);
        }
        image_ = nullptr;
        imageWidth_ = 0;
        imageHeight_ = 0;
        usingShm_ = false;
    }

    Display* display_ = nullptr;
    ::Window window_ = 0;
    Visual* visual_ = nullptr;
    int depth_ = 0;
    GC gc_ = nullptr;
    XImage* image_ = nullptr;
    int32_t imageWidth_ = 0;
    int32_t imageHeight_ = 0;
    bool usingShm_ = false;
    bool disableShm_ = false;
    bool requireShm_ = false;
    bool shmPermanentlyDisabled_ = false;
#if defined(JALIUM_SOFTWARE_XSHM_PRESENT)
    XShmSegmentInfo shmInfo_{};
    bool shmAttached_ = false;
    bool shmMarkedForRemoval_ = false;
#endif
};
#endif

SoftwareRenderTarget::SoftwareRenderTarget(SoftwareBackend* backend, int32_t width, int32_t height)
    : backend_(backend)
{
    width_ = width;
    height_ = height;
    fb_.Resize(width, height);
    currentTransform_ = SoftwareTransform::Identity();
}

SoftwareRenderTarget::~SoftwareRenderTarget() {
#ifdef _WIN32
    if (cachedTextDC_) {
        DeleteDC(static_cast<HDC>(cachedTextDC_));
        cachedTextDC_ = nullptr;
    }
#endif
}

uint64_t SoftwareRenderTarget::MainFramebufferOwnedBytes() const
{
    return static_cast<uint64_t>(fb_.pixels.capacity()) +
        static_cast<uint64_t>(compactFramebuffer_.prefixPixels.capacity());
}

JaliumResult SoftwareRenderTarget::QueryMainFramebufferOwnedBytes(
    uint64_t* outBytes) const
{
    if (!outBytes) return JALIUM_ERROR_INVALID_ARGUMENT;
    *outBytes = MainFramebufferOwnedBytes();
    return JALIUM_OK;
}

bool SoftwareRenderTarget::HasActiveFramebufferCapture() const
{
    return !retainedCaptureStack_.empty() ||
        !effectCaptureStack_.empty() ||
        transitionCaptureActive_[0] ||
        transitionCaptureActive_[1] ||
        readbackPending_;
}

void SoftwareRenderTarget::ResetCompactFramebufferStorage()
{
    std::vector<uint8_t>().swap(compactFramebuffer_.prefixPixels);
    std::memset(compactFramebuffer_.suffixBgra, 0,
        sizeof(compactFramebuffer_.suffixBgra));
    compactFramebuffer_.suffixStartRow = 0;
    compactFramebuffer_.active = false;
}

size_t SoftwareRenderTarget::RetainedCaptureBufferPoolBytes() const
{
    size_t ownedBytes = 0;
    for (const auto& framebuffer : retainedCaptureBufferPool_) {
        const size_t capacity = framebuffer.pixels.capacity();
        if (ownedBytes > std::numeric_limits<size_t>::max() - capacity)
            return std::numeric_limits<size_t>::max();
        ownedBytes += capacity;
    }
    return ownedBytes;
}

void SoftwareRenderTarget::CacheRetainedCaptureBuffer(
    size_t depth,
    SoftwareFramebuffer&& framebuffer)
{
    if (depth >= kRetainedCaptureBufferCacheDepth ||
        framebuffer.pixels.capacity() > kRetainedCaptureBufferCacheBudget) {
        return;
    }

    try {
        if (retainedCaptureBufferPool_.size() <= depth)
            retainedCaptureBufferPool_.resize(depth + 1u);
    } catch (const std::bad_alloc&) {
        return;
    }

    size_t otherBytes = 0;
    for (size_t index = 0; index < retainedCaptureBufferPool_.size(); ++index) {
        if (index == depth) continue;
        const size_t capacity = retainedCaptureBufferPool_[index].pixels.capacity();
        if (capacity > kRetainedCaptureBufferCacheBudget - otherBytes) {
            otherBytes = kRetainedCaptureBufferCacheBudget;
            break;
        }
        otherBytes += capacity;
    }

    const size_t framebufferBytes = framebuffer.pixels.capacity();
    if (otherBytes <= kRetainedCaptureBufferCacheBudget - framebufferBytes)
        retainedCaptureBufferPool_[depth] = std::move(framebuffer);
}

void SoftwareRenderTarget::ReleaseRetainedCaptureBufferPool()
{
    std::vector<SoftwareFramebuffer>().swap(retainedCaptureBufferPool_);
}

void SoftwareRenderTarget::CopyMainFramebufferBytes(
    uint8_t* destination,
    size_t byteCount) const
{
    if (!destination || byteCount == 0) return;
    if (!compactFramebuffer_.active) {
        std::memcpy(destination, fb_.pixels.data(), byteCount);
        return;
    }

    const size_t prefixBytes = std::min(
        byteCount, compactFramebuffer_.prefixPixels.size());
    if (prefixBytes > 0) {
        std::memcpy(destination,
            compactFramebuffer_.prefixPixels.data(), prefixBytes);
    }
    for (size_t index = prefixBytes; index < byteCount; ++index) {
        destination[index] = compactFramebuffer_.suffixBgra[
            (index - compactFramebuffer_.prefixPixels.size()) & 3u];
    }
}

JaliumResult SoftwareRenderTarget::MaterializeMainFramebuffer()
{
    if (!compactFramebuffer_.active) return JALIUM_OK;

    const size_t rowBytes = static_cast<size_t>(width_) * 4u;
    const size_t logicalBytes = rowBytes * static_cast<size_t>(height_);
    if (compactFramebuffer_.suffixStartRow < 0 ||
        compactFramebuffer_.suffixStartRow > height_) {
        return JALIUM_ERROR_INVALID_STATE;
    }
    const size_t expectedPrefixBytes = rowBytes *
        static_cast<size_t>(compactFramebuffer_.suffixStartRow);
    if (compactFramebuffer_.prefixPixels.size() != expectedPrefixBytes)
        return JALIUM_ERROR_INVALID_STATE;

    std::vector<uint8_t> materialized;
    try {
        MaybeFailFramebufferAllocationForTesting();
        materialized.resize(logicalBytes);
    } catch (const std::bad_alloc&) {
        return JALIUM_ERROR_OUT_OF_MEMORY;
    }

    CopyMainFramebufferBytes(materialized.data(), logicalBytes);
    fb_.pixels.swap(materialized);
    ResetCompactFramebufferStorage();
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::CompactIdleFramebufferStorage()
{
    constexpr uint64_t kMinimumReleasedBytes = 1024u * 1024u;
    if (isDrawing_ || HasActiveFramebufferCapture())
        return JALIUM_ERROR_INVALID_STATE;
    ReleaseRetainedCaptureBufferPool();
    if (compactFramebuffer_.active)
        return JALIUM_OK;
    if (width_ <= 0 || height_ <= 0 || fb_.pixels.empty())
        return JALIUM_ERROR_INVALID_STATE;

    const size_t rowBytes = static_cast<size_t>(width_) * 4u;
    const size_t logicalBytes = rowBytes * static_cast<size_t>(height_);
    if (fb_.pixels.size() != logicalBytes)
        return JALIUM_ERROR_INVALID_STATE;

    auto releaseDenseExcessCapacity = [&]() -> JaliumResult {
        if (fb_.pixels.capacity() <= logicalBytes)
            return JALIUM_OK;

        std::vector<uint8_t> compacted;
        try {
            MaybeFailFramebufferAllocationForTesting();
            compacted.resize(logicalBytes);
            if (logicalBytes > 0) {
                std::memcpy(
                    compacted.data(), fb_.pixels.data(), logicalBytes);
            }
        } catch (const std::bad_alloc&) {
            return JALIUM_ERROR_OUT_OF_MEMORY;
        }
        fb_.pixels.swap(compacted);
        return JALIUM_OK;
    };

    const uint8_t* pixels = fb_.pixels.data();
    const uint8_t* suffixColour = pixels + logicalBytes - 4u;
    int32_t suffixStartRow = height_;
    for (int32_t row = height_ - 1; row >= 0; --row) {
        const uint8_t* rowPixels = pixels + static_cast<size_t>(row) * rowBytes;
        bool rowMatches = true;
        for (int32_t column = 0; column < width_; ++column) {
            const uint8_t* pixel = rowPixels + static_cast<size_t>(column) * 4u;
            if (pixel[0] != suffixColour[0] ||
                pixel[1] != suffixColour[1] ||
                pixel[2] != suffixColour[2] ||
                pixel[3] != suffixColour[3]) {
                rowMatches = false;
                break;
            }
        }
        if (!rowMatches) break;
        suffixStartRow = row;
    }

    if (suffixStartRow == height_)
        return releaseDenseExcessCapacity();
    const size_t prefixBytes = rowBytes * static_cast<size_t>(suffixStartRow);
    const uint64_t denseOwnedBytes =
        static_cast<uint64_t>(fb_.pixels.capacity());
    if (denseOwnedBytes < static_cast<uint64_t>(prefixBytes) +
            kMinimumReleasedBytes) {
        return releaseDenseExcessCapacity();
    }

    std::vector<uint8_t> prefixCandidate;
    try {
        MaybeFailFramebufferAllocationForTesting();
        prefixCandidate.resize(prefixBytes);
        if (prefixBytes > 0)
            std::memcpy(prefixCandidate.data(), pixels, prefixBytes);
    } catch (const std::bad_alloc&) {
        return JALIUM_ERROR_OUT_OF_MEMORY;
    }

    const uint64_t compactOwnedBytes =
        static_cast<uint64_t>(prefixCandidate.capacity());
    if (denseOwnedBytes < compactOwnedBytes + kMinimumReleasedBytes)
        return releaseDenseExcessCapacity();

    compactFramebuffer_.prefixPixels.swap(prefixCandidate);
    std::memcpy(compactFramebuffer_.suffixBgra, suffixColour, 4u);
    compactFramebuffer_.suffixStartRow = suffixStartRow;
    compactFramebuffer_.active = true;
    std::vector<uint8_t>().swap(fb_.pixels);
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::Resize(int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0 || width > INT32_MAX / 4 ||
        static_cast<uint64_t>(width) * static_cast<uint64_t>(height) >
            static_cast<uint64_t>(SIZE_MAX) / 4u)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    if (width == width_ && height == height_) {
        fullInvalidation_ = true;
        hasDirtyRect_ = false;
        return JALIUM_OK;
    }

    const size_t resizedBytes = static_cast<size_t>(width) *
        static_cast<size_t>(height) * 4u;
    const size_t oldLogicalBytes = static_cast<size_t>(width_) *
        static_cast<size_t>(height_) * 4u;
    const size_t preservedBytes = std::min(oldLogicalBytes, resizedBytes);

    if (!compactFramebuffer_.active &&
        resizedBytes <= fb_.pixels.capacity()) {
        fb_.pixels.resize(resizedBytes, 0);
        fb_.width = width;
        fb_.height = height;
    } else {
        SoftwareFramebuffer resized;
        resized.width = width;
        resized.height = height;
        try
        {
            MaybeFailFramebufferAllocationForTesting();
            resized.pixels.reserve(ComputeFramebufferGrowthCapacity(
                fb_.pixels.capacity(), resizedBytes));
            resized.pixels.resize(resizedBytes, 0);
        }
        catch (const std::bad_alloc&)
        {
            return JALIUM_ERROR_OUT_OF_MEMORY;
        }

        if (preservedBytes > 0)
            CopyMainFramebufferBytes(resized.pixels.data(), preservedBytes);
        fb_ = std::move(resized);
    }
    ResetCompactFramebufferStorage();
    width_ = width;
    height_ = height;
    fullInvalidation_ = true;
    hasDirtyRect_ = false;
#if defined(JALIUM_SOFTWARE_X11_PRESENT)
    if (x11Presenter_) x11Presenter_->InvalidateStorage();
#endif
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::BeginDraw()
{
    const JaliumResult materializeResult = MaterializeMainFramebuffer();
    if (materializeResult != JALIUM_OK)
        return materializeResult;

    RefreshSoftwareSimdMode();
    framePixelsVisited_ = 0;
    framePixelsBlended_ = 0;
    frameAaSamples_ = 0;
    frameClipRejectedPixels_ = 0;
    frameStartNs_ = SoftwareNowNs();
    SoftwareWorkerPool* frameWorkerPool =
        backend_ ? backend_->GetWorkerPool() : nullptr;
    frameParallelStartNs_ = frameWorkerPool
        ? frameWorkerPool->TotalParallelNs() : 0;
    // Recover from an abandoned retained-layer capture before accepting a new
    // frame. The first saved framebuffer/clip state is the real parent frame;
    // nested entries only contain intermediate isolated canvases.
    if (!retainedCaptureStack_.empty()) {
        const size_t captureDepth = retainedCaptureStack_.size();
        SoftwareFramebuffer restoredFramebuffer =
            std::move(retainedCaptureStack_.front().savedFramebuffer);
        std::stack<SoftwareClipRect> restoredClips =
            std::move(retainedCaptureStack_.front().savedClips);
        std::vector<SoftwareClipRect> restoredRoundedClips =
            std::move(retainedCaptureStack_.front().savedRoundedClips);

        CacheRetainedCaptureBuffer(captureDepth - 1u, std::move(fb_));
        for (size_t depth = captureDepth - 1u; depth > 0u; --depth) {
            CacheRetainedCaptureBuffer(
                depth - 1u,
                std::move(retainedCaptureStack_[depth].savedFramebuffer));
        }

        fb_ = std::move(restoredFramebuffer);
        clipStack_ = std::move(restoredClips);
        roundedClipStack_ = std::move(restoredRoundedClips);
        retainedCaptureStack_.clear();
    }

    // Recover from an abandoned managed Begin/End pair before a new frame.
    // Restore innermost-to-outermost so the outer saved region wins and the
    // final framebuffer is exactly the state before the first capture.
    if (!effectCaptureStack_.empty()) {
        for (auto state = effectCaptureStack_.rbegin();
             state != effectCaptureStack_.rend(); ++state) {
            RestoreRegion(state->savedRegion, state->pixelX, state->pixelY);
        }
        effectCaptureStack_.clear();
    }
    effectCaptureReady_ = false;

    // Push a root DPI scale transform so all draw calls in DIPs are
    // automatically mapped to physical pixels on high-density displays.
    if (scaleX_ != 1.0f || scaleY_ != 1.0f) {
        SoftwareTransform dpiScale = {{ scaleX_, 0, 0, scaleY_, 0, 0 }};
        float m[6] = { dpiScale.m[0], dpiScale.m[1], dpiScale.m[2],
                        dpiScale.m[3], dpiScale.m[4], dpiScale.m[5] };
        PushTransform(m);
    }
    isDrawing_ = true;
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::EndDraw()
{
    struct DrawingStateReset {
        bool& value;
        ~DrawingStateReset() { value = false; }
    } drawingStateReset{isDrawing_};

    const uint64_t rasterFinishedNs = SoftwareNowNs();
    lastRasterNs_ = frameStartNs_ > 0 && rasterFinishedNs >= frameStartNs_
        ? rasterFinishedNs - frameStartNs_ : 0;
    SoftwareWorkerPool* frameWorkerPool =
        backend_ ? backend_->GetWorkerPool() : nullptr;
    const uint64_t parallelFinishedNs = frameWorkerPool
        ? frameWorkerPool->TotalParallelNs() : frameParallelStartNs_;
    lastParallelNs_ = parallelFinishedNs >= frameParallelStartNs_
        ? parallelFinishedNs - frameParallelStartNs_ : 0;

    // Pop the root DPI scale transform pushed in BeginDraw
    if (scaleX_ != 1.0f || scaleY_ != 1.0f) {
        PopTransform();
    }

    // Software already owns a CPU BGRA8 framebuffer, but preserve the same
    // two-phase semantics as D3D12/Vulkan: a request captures the *next*
    // completed frame, not whichever pixels happen to exist at request time.
    // Keep the snapshot independent from fb_ so a later resize/draw cannot
    // invalidate the dimensions returned by FetchReadback's size query.
    if (readbackPending_) {
        readbackPending_ = false;
        readbackReady_ = false;
        try {
            readbackFb_.width = width_;
            readbackFb_.height = height_;
            readbackFb_.pixels = fb_.pixels;
            readbackReady_ = true;
        } catch (const std::bad_alloc&) {
            readbackFb_.width = 0;
            readbackFb_.height = 0;
            readbackFb_.pixels.clear();
            return JALIUM_ERROR_OUT_OF_MEMORY;
        }
    }

#ifdef _WIN32
    // Upload directly from the framebuffer. A persistent DIBSection duplicates
    // the entire surface (and doubles again at high DPI) even after an idle
    // window has stopped drawing. StretchDIBits accepts a source rectangle in a
    // top-down DIB, so the real dirty rectangle remains the transfer unit
    // without allocating or repacking a second full-size buffer.
    if (hwnd_) {
        if (fullInvalidation_ || hasDirtyRect_) {
            int32_t left = fullInvalidation_ ? 0 :
                std::clamp(dirtyLeft_, 0, width_);
            int32_t top = fullInvalidation_ ? 0 : std::clamp(dirtyTop_, 0, height_);
            int32_t right = fullInvalidation_ ? width_ :
                std::clamp(dirtyRight_, left, width_);
            int32_t bottom = fullInvalidation_ ? height_ : std::clamp(dirtyBottom_, top, height_);
            if (right > left && bottom > top) {
                HDC hdc = GetDC((HWND)hwnd_);
                if (hdc) {
                    BITMAPINFO bitmapInfo{};
                    bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
                    bitmapInfo.bmiHeader.biWidth = width_;
                    bitmapInfo.bmiHeader.biHeight = -(bottom - top);
                    bitmapInfo.bmiHeader.biPlanes = 1;
                    bitmapInfo.bmiHeader.biBitCount = 32;
                    bitmapInfo.bmiHeader.biCompression = BI_RGB;
                    const uint8_t* bandStart = fb_.pixels.data() +
                        static_cast<size_t>(top) * width_ * 4u;
                    const int dirtyWidth = right - left;
                    const int dirtyHeight = bottom - top;
                    const int copiedScanLines = StretchDIBits(
                        hdc,
                        left, top, dirtyWidth, dirtyHeight,
                        left, 0, dirtyWidth, dirtyHeight,
                        bandStart, &bitmapInfo, DIB_RGB_COLORS, SRCCOPY);
                    if (copiedScanLines == 0 ||
                        copiedScanLines == static_cast<int>(GDI_ERROR)) {
                        // Some legacy display drivers do not advertise the
                        // stretch-DIB path. Preserve presentation correctness
                        // with the banded SetDIBitsToDevice fallback.
                        SetDIBitsToDevice(
                            hdc, 0, top, width_, bottom - top,
                            0, 0, 0, bottom - top,
                            bandStart, &bitmapInfo, DIB_RGB_COLORS);
                    }
                    ReleaseDC((HWND)hwnd_, hdc);
                }
            }
            fullInvalidation_ = false;
            hasDirtyRect_ = false;
        }
    }
#else
#ifdef __APPLE__
    if ((surfaceDescriptor_.platform == JALIUM_PLATFORM_MACOS ||
         surfaceDescriptor_.platform == JALIUM_PLATFORM_IOS ||
         surfaceDescriptor_.platform == JALIUM_PLATFORM_TVOS ||
         surfaceDescriptor_.platform == JALIUM_PLATFORM_VISIONOS) &&
        surfaceDescriptor_.handle0 != 0)
    {
        id view = (__bridge id)reinterpret_cast<void*>(surfaceDescriptor_.handle0);
        CALayer* layer = nil;
#if TARGET_OS_OSX
        if ([view isKindOfClass:[NSView class]]) {
            ((NSView*)view).wantsLayer = YES;
            layer = ((NSView*)view).layer;
        }
#else
        if ([view isKindOfClass:[UIView class]]) layer = ((UIView*)view).layer;
#endif
        if (!layer) return JALIUM_ERROR_PRESENT_FAILED;
        CFDataRef bytes = CFDataCreate(kCFAllocatorDefault, fb_.pixels.data(),
            static_cast<CFIndex>(fb_.pixels.size()));
        CGDataProviderRef provider = bytes
            ? CGDataProviderCreateWithCFData(bytes) : nullptr;
        CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGImageRef image = provider ? CGImageCreate(width_, height_, 8, 32,
            static_cast<size_t>(width_) * 4, colorSpace,
            kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little,
            provider, nullptr, false, kCGRenderingIntentDefault) : nullptr;
        if (image) {
            [CATransaction begin];
            [CATransaction setDisableActions:YES];
            layer.contents = (__bridge id)image;
            layer.contentsGravity = kCAGravityTopLeft;
            layer.contentsScale = scaleX_;
            [CATransaction commit];
        }
        if (image) CGImageRelease(image);
        if (colorSpace) CGColorSpaceRelease(colorSpace);
        if (provider) CGDataProviderRelease(provider);
        if (bytes) CFRelease(bytes);
        if (!image) return JALIUM_ERROR_OUT_OF_MEMORY;
        fullInvalidation_ = false;
        hasDirtyRect_ = false;
        return JALIUM_OK;
    }
#endif
#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_LINUX_WAYLAND &&
        surfaceDescriptor_.handle0 != 0 && surfaceDescriptor_.handle1 != 0)
    {
        if (!fullInvalidation_ && !hasDirtyRect_) return JALIUM_OK;
        const int32_t left = fullInvalidation_ ? 0 : dirtyLeft_;
        const int32_t top = fullInvalidation_ ? 0 : dirtyTop_;
        const int32_t right = fullInvalidation_ ? width_ : dirtyRight_;
        const int32_t bottom = fullInvalidation_ ? height_ : dirtyBottom_;
        if (!waylandPresenter_)
            return JALIUM_ERROR_BACKEND_NOT_AVAILABLE;
        const bool presented = waylandPresenter_->Present(
            fb_.pixels.data(), width_, height_, width_ * 4,
            left, top, right, bottom);
        if (presented)
        {
            fullInvalidation_ = false;
            hasDirtyRect_ = false;
        }
        return presented ? JALIUM_OK : JALIUM_ERROR_PRESENT_FAILED;
    }
#endif
#ifdef JALIUM_SOFTWARE_X11_PRESENT
    // Present only the invalidated region. The presenter owns an XImage whose
    // format matches this window's real Visual/Depth and selects MIT-SHM or
    // XPutImage at runtime.
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_LINUX_X11 &&
        surfaceDescriptor_.handle0 != 0 && surfaceDescriptor_.handle1 != 0)
    {
        Display* dpy = reinterpret_cast<Display*>(surfaceDescriptor_.handle0);
        ::Window xwin = static_cast<::Window>(surfaceDescriptor_.handle1);
        if (!fullInvalidation_ && !hasDirtyRect_) return JALIUM_OK;
        const int32_t left = fullInvalidation_ ? 0 : dirtyLeft_;
        const int32_t top = fullInvalidation_ ? 0 : dirtyTop_;
        const int32_t right = fullInvalidation_ ? width_ : dirtyRight_;
        const int32_t bottom = fullInvalidation_ ? height_ : dirtyBottom_;
        if (!x11Presenter_)
            x11Presenter_ = std::make_unique<X11SoftwarePresenter>(dpy, xwin);
        const bool presented = x11Presenter_->Present(
            fb_.pixels.data(), width_, height_, width_ * 4,
            left, top, right, bottom);
        if (!presented) return JALIUM_ERROR_PRESENT_FAILED;
        fullInvalidation_ = false;
        hasDirtyRect_ = false;
        return JALIUM_OK;
    }
#endif
#ifdef __ANDROID__
    // Present to Android ANativeWindow. Success-path logging is throttled to
    // the first few frames per render target (surface bring-up diagnostics
    // only — the previous unconditional per-frame LOGI_SW triple flooded
    // logcat at present rate, and a function-level static spent the budget
    // once per process, leaving rebuilt RTs undiagnosed); failure paths keep
    // logging unconditionally.
    const bool logPresent = presentLogFramesLeft_ > 0;
    if (logPresent)
    {
        --presentLogFramesLeft_;
        LOGI_SW("EndDraw: platform=%d, handle0=%p, fb=%dx%d",
                surfaceDescriptor_.platform, (void*)surfaceDescriptor_.handle0, width_, height_);
    }
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_ANDROID &&
        surfaceDescriptor_.handle0 != 0)
    {
        if (!fullInvalidation_ && !hasDirtyRect_) return JALIUM_OK;
        ANativeWindow* nativeWindow = reinterpret_cast<ANativeWindow*>(surfaceDescriptor_.handle0);

        ARect dirtyBounds{
            fullInvalidation_ ? 0 : std::clamp(dirtyLeft_, 0, width_),
            fullInvalidation_ ? 0 : std::clamp(dirtyTop_, 0, height_),
            fullInvalidation_ ? width_ : std::clamp(dirtyRight_, 0, width_),
            fullInvalidation_ ? height_ : std::clamp(dirtyBottom_, 0, height_)
        };
        ANativeWindow_Buffer buffer;
        int lockResult = ANativeWindow_lock(nativeWindow, &buffer, &dirtyBounds);
        if (lockResult < 0)
        {
            // The frame never reached the surface. Falling through to
            // JALIUM_OK here (as this branch used to) made managed clear its
            // damage and let the render loop go idle on a frame nobody saw —
            // once the CompositionTarget keep-alive expired the window stayed
            // black with nothing left to trigger a repaint. Report the
            // dropped present like the Linux desktop paths above so managed
            // keeps the damage and retries.
            LOGE_SW("ANativeWindow_lock failed: %d", lockResult);
            return JALIUM_ERROR_PRESENT_FAILED;
        }
        if (logPresent)
        {
            LOGI_SW("ANativeWindow_lock result=%d, buffer=%dx%d stride=%d",
                    lockResult, buffer.width, buffer.height, buffer.stride);
        }

        auto* dst = static_cast<uint8_t*>(buffer.bits);
        const auto* src = fb_.pixels.data();
        int32_t copyHeight = std::min(height_, buffer.height);
        int32_t copyWidth = std::min(width_, buffer.width);
        uint32_t dstStride = buffer.stride * 4; // stride is in pixels
        const int32_t copyLeft = std::clamp(dirtyBounds.left, 0, copyWidth);
        const int32_t copyTop = std::clamp(dirtyBounds.top, 0, copyHeight);
        const int32_t copyRight = std::clamp(dirtyBounds.right, copyLeft, copyWidth);
        const int32_t copyBottom = std::clamp(dirtyBounds.bottom, copyTop, copyHeight);

        // ANativeWindow may expand the requested damage. Convert only the
        // returned rectangle; Android preserves the remainder of the buffer.
        // BGRA -> RGBA channel swap + copy (ANativeWindow uses RGBA).
        for (int32_t y = copyTop; y < copyBottom; y++)
        {
            const uint8_t* srcRow = src + y * width_ * 4;
            uint8_t* dstRow = dst + y * dstStride;
            for (int32_t x = copyLeft; x < copyRight; x++)
            {
                dstRow[x * 4 + 0] = srcRow[x * 4 + 2]; // R ← B
                dstRow[x * 4 + 1] = srcRow[x * 4 + 1]; // G ← G
                dstRow[x * 4 + 2] = srcRow[x * 4 + 0]; // B ← R
                dstRow[x * 4 + 3] = srcRow[x * 4 + 3]; // A ← A
            }
        }

        int unlockResult = ANativeWindow_unlockAndPost(nativeWindow);
        if (unlockResult < 0)
        {
            // Same fake-success hazard as a failed lock: the buffer was never
            // posted, so the frame is not on screen.
            LOGE_SW("ANativeWindow_unlockAndPost failed: %d", unlockResult);
            return JALIUM_ERROR_PRESENT_FAILED;
        }
        if (logPresent)
        {
            LOGI_SW("ANativeWindow_unlockAndPost done");
        }
        fullInvalidation_ = false;
        hasDirtyRect_ = false;
        return JALIUM_OK;
    }
#endif
#if !defined(__ANDROID__)
    // Reaching here with a Linux desktop surface means the matching present
    // path was compiled out (libX11/wayland-client dev packages missing at
    // build time) or its handles were invalid. Reporting JALIUM_OK used to
    // leave the window permanently black with zero diagnostics — surface the
    // failure instead.
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_LINUX_X11 ||
        surfaceDescriptor_.platform == JALIUM_PLATFORM_LINUX_WAYLAND)
        return JALIUM_ERROR_BACKEND_NOT_AVAILABLE;
#endif
#endif
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::RequestReadback()
{
    if (width_ <= 0 || height_ <= 0 ||
        (!compactFramebuffer_.active && fb_.pixels.empty()))
        return JALIUM_ERROR_INVALID_STATE;
    readbackPending_ = true;
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::FetchReadback(
    uint8_t* buf, uint32_t bufStride, int32_t* outWidth, int32_t* outHeight)
{
    if (outWidth) *outWidth = 0;
    if (outHeight) *outHeight = 0;
    if (!readbackReady_ || readbackFb_.width <= 0 || readbackFb_.height <= 0 ||
        readbackFb_.pixels.empty())
        return JALIUM_ERROR_INVALID_STATE;

    const uint32_t width = static_cast<uint32_t>(readbackFb_.width);
    const uint32_t height = static_cast<uint32_t>(readbackFb_.height);
    if (!buf) {
        if (outWidth) *outWidth = readbackFb_.width;
        if (outHeight) *outHeight = readbackFb_.height;
        return JALIUM_OK;
    }
    const uint32_t rowBytes = width * 4u;
    if (bufStride < rowBytes)
        return JALIUM_ERROR_INVALID_ARGUMENT;

    for (uint32_t y = 0; y < height; ++y) {
        std::memcpy(buf + static_cast<size_t>(y) * bufStride,
            readbackFb_.pixels.data() + static_cast<size_t>(y) * rowBytes,
            rowBytes);
    }
    if (outWidth) *outWidth = readbackFb_.width;
    if (outHeight) *outHeight = readbackFb_.height;
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::QueryGpuStats(JaliumGpuStats* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumGpuStats{};

    if (ellipseFillMaskCache_) {
        ++out->pathEntries;
        out->pathBytes += static_cast<int64_t>(ellipseFillMaskCache_->ByteSize());
    }
    if (ellipseStrokeMaskCache_) {
        ++out->pathEntries;
        out->pathBytes += static_cast<int64_t>(ellipseStrokeMaskCache_->ByteSize());
    }
    if (roundedRectMaskCache_) {
        out->pathEntries += static_cast<int32_t>(roundedRectMaskCache_->EntryCount());
        out->pathBytes += static_cast<int64_t>(roundedRectMaskCache_->ByteSize());
    }
    if (ellipticalRoundedRectMaskCache_) {
        out->pathEntries += static_cast<int32_t>(
            ellipticalRoundedRectMaskCache_->EntryCount());
        out->pathBytes += static_cast<int64_t>(
            ellipticalRoundedRectMaskCache_->ByteSize());
    }
    if (pathRasterCache_) {
        out->pathEntries += static_cast<int32_t>(pathRasterCache_->EntryCount());
        out->pathBytes += static_cast<int64_t>(pathRasterCache_->ByteSize());
    }
    if (lineRasterCache_) {
        out->pathEntries += static_cast<int32_t>(lineRasterCache_->EntryCount());
        out->pathBytes += static_cast<int64_t>(lineRasterCache_->ByteSize());
    }
    if (textMaskCache_) {
        out->glyphSlotsUsed = static_cast<int32_t>(std::min<size_t>(
            textMaskCache_->EntryCount(), static_cast<size_t>(INT32_MAX)));
        out->glyphBytes = static_cast<int64_t>(textMaskCache_->ByteSize());
        const size_t averageBytes = textMaskCache_->EntryCount() > 0
            ? textMaskCache_->ByteSize() / textMaskCache_->EntryCount()
            : 0u;
        out->glyphSlotsTotal = averageBytes > 0
            ? static_cast<int32_t>((32u * 1024u * 1024u) / averageBytes)
            : 0;
    }
    out->textureCount = 1 + static_cast<int32_t>(retainedLayers_.size());
    out->textureBytes = static_cast<int64_t>(
        MainFramebufferOwnedBytes() + retainedLayerBytes_ + blurScratch_.capacity());
    for (const auto& layer : retainedLayers_) {
        if (layer && layer->bitmap) {
            const size_t scaledBytes = layer->bitmap->ScaledCacheBytes();
            if (scaledBytes > 0) {
                out->textureBytes += static_cast<int64_t>(scaledBytes);
            }
        }
    }
    if (textCompositeCache_) {
        out->textureCount += static_cast<int32_t>(textCompositeCache_->EntryCount());
        out->textureBytes += static_cast<int64_t>(textCompositeCache_->ByteSize());
    }
    if (backdropCache_) {
        out->textureCount += static_cast<int32_t>(backdropCache_->EntryCount() * 2u);
        out->textureBytes += static_cast<int64_t>(backdropCache_->ByteSize());
    }
    if (liquidGlassCache_) {
        out->textureCount += static_cast<int32_t>(liquidGlassCache_->EntryCount() * 2u);
        out->textureBytes += static_cast<int64_t>(liquidGlassCache_->ByteSize());
    }
    if (gradientCompositeCache_) {
        out->textureCount += static_cast<int32_t>(
            gradientCompositeCache_->EntryCount() * 2u);
        out->textureBytes += static_cast<int64_t>(
            gradientCompositeCache_->ByteSize());
    }
    if (bitmapCompositeCache_) {
        out->textureCount += static_cast<int32_t>(
            bitmapCompositeCache_->EntryCount() * 2u);
        out->textureBytes += static_cast<int64_t>(
            bitmapCompositeCache_->ByteSize());
    }
    if (effectResultCache_) {
        out->textureCount += static_cast<int32_t>(effectResultCache_->EntryCount() * 3u);
        out->textureBytes += static_cast<int64_t>(effectResultCache_->ByteSize());
    }
    auto addBuffer = [&](const SoftwareFramebuffer& buffer) {
        if (!buffer.pixels.empty()) {
            ++out->textureCount;
            out->textureBytes += static_cast<int64_t>(buffer.pixels.size());
        }
    };
    addBuffer(effectCaptureFb_);
    addBuffer(readbackFb_);
    addBuffer(transitionCaptureFb_[0]);
    addBuffer(transitionCaptureFb_[1]);
    addBuffer(desktopCaptureFb_);
    for (const auto& buffer : retainedCaptureBufferPool_) {
        if (buffer.pixels.capacity() == 0) continue;
        ++out->textureCount;
        out->textureBytes += static_cast<int64_t>(buffer.pixels.capacity());
    }
    const size_t resourceCacheBytes = SoftwareResourceCacheBytes();
    out->textureBytes += static_cast<int64_t>(resourceCacheBytes);
    out->softwareRasterNs = static_cast<int64_t>(lastRasterNs_);
    out->softwarePixelsVisited = static_cast<int64_t>(framePixelsVisited_);
    out->softwarePixelsBlended = static_cast<int64_t>(framePixelsBlended_);
    out->softwareAaSamples = static_cast<int64_t>(frameAaSamples_);
    out->softwareClipRejectedPixels =
        static_cast<int64_t>(frameClipRejectedPixels_);
    out->softwareParallelNs = static_cast<int64_t>(lastParallelNs_);
    out->softwareCacheBytes = out->glyphBytes + out->pathBytes + out->textureBytes;
    if (effectResultCache_) {
        out->softwareEffectCacheHits =
            static_cast<int64_t>(effectResultCache_->Hits());
        out->softwareEffectCacheMisses =
            static_cast<int64_t>(effectResultCache_->Misses());
        out->softwareEffectCacheEntries = static_cast<int32_t>(
            std::min<size_t>(effectResultCache_->EntryCount(), INT32_MAX));
    }
    if (gradientCompositeCache_) {
        out->softwareGradientCacheEntries = static_cast<int32_t>(
            std::min<size_t>(gradientCompositeCache_->EntryCount(), INT32_MAX));
    }
    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    out->softwareWorkerCount = workerPool
        ? static_cast<int32_t>(workerPool->WorkerCount()) : 0;
    out->softwareWorkerUtilizationPermille = lastRasterNs_ > 0
        ? static_cast<int32_t>(std::min<uint64_t>(
            1000u, lastParallelNs_ * 1000u / lastRasterNs_))
        : 0;
    // The software target owns one persistent framebuffer rather than a GPU
    // swap chain. Reporting one lets the common frame-pacing UI distinguish it
    // from an unavailable snapshot without inventing wait times.
    out->swapBufferCount = 1;
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::ReclaimIdleResources()
{
    ellipseFillMaskCache_.reset();
    ellipseStrokeMaskCache_.reset();
    if (roundedRectMaskCache_) roundedRectMaskCache_->Clear();
    if (ellipticalRoundedRectMaskCache_)
        ellipticalRoundedRectMaskCache_->Clear();
    if (pathRasterCache_) pathRasterCache_->Clear();
    if (lineRasterCache_) lineRasterCache_->Clear();
    if (textMaskCache_) textMaskCache_->Clear();
    if (textCompositeCache_) textCompositeCache_->Clear();
    if (backdropCache_) backdropCache_->Clear();
    if (gradientCompositeCache_) gradientCompositeCache_->Clear();
    if (bitmapCompositeCache_) bitmapCompositeCache_->Clear();
    if (effectResultCache_) effectResultCache_->Clear();
    if (liquidGlassCache_) liquidGlassCache_->Clear();
    for (const auto& layer : retainedLayers_) {
        if (layer && layer->bitmap) layer->bitmap->ClearScaledCache();
    }
    ReleaseRetainedCaptureBufferPool();
    std::vector<uint8_t>().swap(blurScratch_);
    return JALIUM_OK;
}

void SoftwareRenderTarget::Clear(float r, float g, float b, float a)
{
    const uint8_t red = FloatToU8(r);
    const uint8_t green = FloatToU8(g);
    const uint8_t blue = FloatToU8(b);
    const uint8_t alpha = FloatToU8(a);
    const uint32_t packed = static_cast<uint32_t>(blue) |
        (static_cast<uint32_t>(green) << 8) |
        (static_cast<uint32_t>(red) << 16) |
        (static_cast<uint32_t>(alpha) << 24);

    int32_t clearLeft = 0;
    int32_t clearTop = 0;
    int32_t clearRight = width_;
    int32_t clearBottom = height_;
    if (!fullInvalidation_ && hasDirtyRect_) {
        clearLeft = std::clamp(dirtyLeft_, 0, width_);
        clearTop = std::clamp(dirtyTop_, 0, height_);
        clearRight = std::clamp(dirtyRight_, clearLeft, width_);
        clearBottom = std::clamp(dirtyBottom_, clearTop, height_);
    }
    if (clearRight <= clearLeft || clearBottom <= clearTop) return;

    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    const uint64_t pixelCount = static_cast<uint64_t>(clearRight - clearLeft) *
        static_cast<uint64_t>(clearBottom - clearTop);
    framePixelsVisited_ += pixelCount;
    framePixelsBlended_ += pixelCount;
    if (workerPool && workerPool->CanParallelize() &&
        pixelCount >= kParallelClearPixelThreshold &&
        clearBottom - clearTop >= 32) {
        workerPool->ParallelFor(clearTop, clearBottom, 16,
            [this, packed, clearLeft, clearRight](int32_t rowBegin, int32_t rowEnd) {
                for (int32_t row = rowBegin; row < rowEnd; ++row) {
                    fb_.FillOpaqueSpan(row, clearLeft, clearRight, packed);
                }
            });
        return;
    }
    for (int32_t row = clearTop; row < clearBottom; ++row) {
        fb_.FillOpaqueSpan(row, clearLeft, clearRight, packed);
    }
}

bool SoftwareRenderTarget::IsClipped(float px, float py) const
{
    if (clipStack_.empty()) return false;
    // The top entry carries the full rectangle intersection of every level, so
    // one Contains covers all rectangular clipping.
    auto& clip = const_cast<std::stack<SoftwareClipRect>&>(clipStack_).top();
    if (!clip.Contains(px, py)) return true;
    // Corner rounding cannot be folded into an intersection — every live
    // rounded level keeps its own untrimmed rectangle and is tested here. The
    // rectangle part of each is a superset of the top intersection (already
    // passed above), so effectively only the corner circles are evaluated.
    for (const auto& rounded : roundedClipStack_) {
        if (!rounded.Contains(px, py)) return true;
    }
    return false;
}

bool SoftwareRenderTarget::IsInsidePerCornerRoundedRect(float px, float py, float w, float h,
    float tl, float tr, float br, float bl)
{
    if (px < 0 || px > w || py < 0 || py > h) return false;
    // Top-left corner
    if (px < tl && py < tl) {
        float dx = (px - tl) / tl, dy = (py - tl) / tl;
        if (dx * dx + dy * dy > 1.0f) return false;
    }
    // Top-right corner
    if (px > w - tr && py < tr) {
        float dx = (px - (w - tr)) / tr, dy = (py - tr) / tr;
        if (dx * dx + dy * dy > 1.0f) return false;
    }
    // Bottom-right corner
    if (px > w - br && py > h - br) {
        float dx = (px - (w - br)) / br, dy = (py - (h - br)) / br;
        if (dx * dx + dy * dy > 1.0f) return false;
    }
    // Bottom-left corner
    if (px < bl && py > h - bl) {
        float dx = (px - bl) / bl, dy = (py - (h - bl)) / bl;
        if (dx * dx + dy * dy > 1.0f) return false;
    }
    return true;
}

void SoftwareRenderTarget::GetBrushColor(Brush* brush, float px, float py,
    uint8_t& r, uint8_t& g, uint8_t& b, uint8_t& a)
{
    float opacity = currentOpacity_;

    if (auto* solid = dynamic_cast<SoftwareSolidBrush*>(brush)) {
        r = FloatToU8(solid->r);
        g = FloatToU8(solid->g);
        b = FloatToU8(solid->b);
        a = FloatToU8(solid->a * opacity);
    } else if (auto* linear = dynamic_cast<SoftwareLinearGradientBrush*>(brush)) {
        float fr, fg, fb, fa;
        linear->SampleColor(px, py, fr, fg, fb, fa);
        r = FloatToU8(fr);
        g = FloatToU8(fg);
        b = FloatToU8(fb);
        a = FloatToU8(fa * opacity);
    } else if (auto* radial = dynamic_cast<SoftwareRadialGradientBrush*>(brush)) {
        float fr, fg, fb, fa;
        radial->SampleColor(px, py, fr, fg, fb, fa);
        r = FloatToU8(fr);
        g = FloatToU8(fg);
        b = FloatToU8(fb);
        a = FloatToU8(fa * opacity);
    } else {
        r = g = b = 0; a = 255;
    }
}

SoftwareRenderTarget::PreparedPaint SoftwareRenderTarget::PreparePaint(Brush* brush) const
{
    PreparedPaint paint;
    if (!brush) return paint;

    switch (brush->GetType()) {
        case JALIUM_BRUSH_SOLID: {
            const auto* solid = static_cast<const SoftwareSolidBrush*>(brush);
            paint.kind = PreparedPaintKind::Solid;
            paint.r = FloatToU8(solid->r);
            paint.g = FloatToU8(solid->g);
            paint.b = FloatToU8(solid->b);
            paint.a = FloatToU8(solid->a * currentOpacity_);
            paint.packedBgra = static_cast<uint32_t>(paint.b) |
                (static_cast<uint32_t>(paint.g) << 8) |
                (static_cast<uint32_t>(paint.r) << 16) |
                (static_cast<uint32_t>(paint.a) << 24);
            break;
        }
        case JALIUM_BRUSH_LINEAR_GRADIENT:
            paint.kind = PreparedPaintKind::LinearGradient;
            paint.linear = static_cast<const SoftwareLinearGradientBrush*>(brush);
            break;
        case JALIUM_BRUSH_RADIAL_GRADIENT:
            paint.kind = PreparedPaintKind::RadialGradient;
            paint.radial = static_cast<const SoftwareRadialGradientBrush*>(brush);
            break;
        default:
            break;
    }
    return paint;
}

void SoftwareRenderTarget::SamplePaint(
    const PreparedPaint& paint, float px, float py,
    uint8_t& r, uint8_t& g, uint8_t& b, uint8_t& a) const
{
    if (paint.kind == PreparedPaintKind::Solid) {
        r = paint.r; g = paint.g; b = paint.b; a = paint.a;
        return;
    }

    float fa = 0.0f;
    if (paint.kind == PreparedPaintKind::LinearGradient && paint.linear) {
        paint.linear->SampleColor8(px, py, r, g, b, fa);
    } else if (paint.kind == PreparedPaintKind::RadialGradient && paint.radial) {
        paint.radial->SampleColor8(px, py, r, g, b, fa);
    } else {
        r = g = b = a = 0;
        return;
    }
    a = FloatToU8(fa * currentOpacity_);
}

void SoftwareRenderTarget::CompositeSpan(
    int32_t y, int32_t x0, int32_t x1,
    const PreparedPaint& paint, uint8_t coverage)
{
    if (!paint.IsValid() || coverage == 0 || x1 <= x0) return;
    x0 = std::max(x0, 0);
    x1 = std::min(x1, width_);
    if (x1 <= x0 || y < 0 || y >= height_) return;

    if (paint.IsSolid()) {
        const uint8_t alpha = coverage == 255
            ? paint.a
            : static_cast<uint8_t>(
                (static_cast<uint32_t>(paint.a) * coverage + 127u) / 255u);
        fb_.BlendSolidSpan(y, x0, x1, paint.r, paint.g, paint.b, alpha);
        return;
    }

    for (int32_t x = x0; x < x1; ++x) {
        uint8_t r, g, b, a;
        SamplePaint(paint, static_cast<float>(x) + 0.5f,
                    static_cast<float>(y) + 0.5f, r, g, b, a);
        if (coverage != 255) {
            a = static_cast<uint8_t>(
                (static_cast<uint32_t>(a) * coverage + 127u) / 255u);
        }
        fb_.BlendPixelUnchecked(x, y, r, g, b, a);
    }
}

bool SoftwareRenderTarget::TightenToRectClip(
    int32_t& x0, int32_t& y0, int32_t& x1, int32_t& y1) const
{
    const int32_t originalWidth = std::max(x1 - x0, 0);
    const int32_t originalHeight = std::max(y1 - y0, 0);
    const uint64_t originalPixels = static_cast<uint64_t>(originalWidth) *
        static_cast<uint64_t>(originalHeight);
    x0 = std::max(x0, 0);
    y0 = std::max(y0, 0);
    x1 = std::min(x1, width_);
    y1 = std::min(y1, height_);

    if (!fullInvalidation_ && hasDirtyRect_) {
        x0 = std::max(x0, dirtyLeft_);
        y0 = std::max(y0, dirtyTop_);
        x1 = std::min(x1, dirtyRight_);
        y1 = std::min(y1, dirtyBottom_);
    }

    if (!clipStack_.empty()) {
        const auto& clip = clipStack_.top();
        // Clip containment is tested at pixel centres. Convert the half-open
        // float rectangle to the exact half-open integer-centre interval once,
        // instead of calling Contains for every pixel in every primitive.
        x0 = std::max(x0, static_cast<int32_t>(std::ceil(clip.x - 0.5f)));
        y0 = std::max(y0, static_cast<int32_t>(std::ceil(clip.y - 0.5f)));
        x1 = std::min(x1, static_cast<int32_t>(std::ceil(clip.x + clip.w - 0.5f)));
        y1 = std::min(y1, static_cast<int32_t>(std::ceil(clip.y + clip.h - 0.5f)));
    }
    const int32_t clippedWidth = std::max(x1 - x0, 0);
    const int32_t clippedHeight = std::max(y1 - y0, 0);
    const uint64_t clippedPixels = static_cast<uint64_t>(clippedWidth) *
        static_cast<uint64_t>(clippedHeight);
    framePixelsVisited_ += originalPixels;
    framePixelsBlended_ += clippedPixels;
    if (originalPixels > clippedPixels)
        frameClipRejectedPixels_ += originalPixels - clippedPixels;
    return clippedWidth > 0 && clippedHeight > 0;
}

bool SoftwareRenderTarget::TightenSpanToRoundedClips(
    int32_t y, int32_t& x0, int32_t& x1) const
{
    if (x1 <= x0) return false;
    const float sampleY = static_cast<float>(y) + 0.5f;
    for (const auto& clip : roundedClipStack_) {
        if (sampleY < clip.y || sampleY >= clip.y + clip.h) return false;

        const float halfMin = std::min(clip.w, clip.h) * 0.5f;
        const float topLeft = std::min(clip.radiusTL, halfMin);
        const float topRight = std::min(clip.radiusTR, halfMin);
        const float bottomRight = std::min(clip.radiusBR, halfMin);
        const float bottomLeft = std::min(clip.radiusBL, halfMin);
        const float localY = sampleY - clip.y;

        float left = clip.x;
        if (topLeft > 0.0f && localY < topLeft) {
            const float dy = (localY - topLeft) / topLeft;
            left += topLeft *
                (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        } else if (bottomLeft > 0.0f && localY > clip.h - bottomLeft) {
            const float dy = (localY - (clip.h - bottomLeft)) / bottomLeft;
            left += bottomLeft *
                (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        }

        float right = clip.x + clip.w;
        if (topRight > 0.0f && localY < topRight) {
            const float dy = (localY - topRight) / topRight;
            right = clip.x + clip.w - topRight +
                topRight * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        } else if (bottomRight > 0.0f && localY > clip.h - bottomRight) {
            const float dy = (localY - (clip.h - bottomRight)) / bottomRight;
            right = clip.x + clip.w - bottomRight +
                bottomRight * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        }

        x0 = std::max(x0, static_cast<int32_t>(std::ceil(left - 0.5f)));
        x1 = std::min(x1,
            static_cast<int32_t>(std::floor(right - 0.5f)) + 1);
        if (x1 <= x0) return false;
    }
    return true;
}

void SoftwareRenderTarget::DrawHLine(int32_t x0, int32_t x1, int32_t y,
    uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    if (y < 0 || y >= height_) return;
    x0 = std::max(x0, 0);
    x1 = std::min(x1, width_ - 1);
    for (int32_t x = x0; x <= x1; x++) {
        fb_.BlendPixel(x, y, r, g, b, a);
    }
}

// ----------------------------------------------------------------------------
// Sub-pixel coverage rasterizer (shared by the shape primitives)
// ----------------------------------------------------------------------------
// An animated transform feeds a continuous, usually-fractional origin into the
// shape primitives below. The legacy code truncated that origin with an (int)
// cast (`ix = (int32_t)tx`) before stepping integer pixels, which pinned the
// shape to a whole-pixel grid and made smooth animations step 1px at a time.
//
// RasterizeCoverageAA keeps the origin in float. It walks the device pixels the
// shape's float bounding box can touch and, for each, evaluates the caller's
// `inside` predicate at 4x4 sub-sample positions expressed in shape-local space
// (sample center minus the float origin). The fraction of covered sub-samples
// becomes the pixel alpha, so the shape tracks its true sub-pixel position AND
// gains edge anti-aliasing matching the GPU backends. Whole-pixel-aligned
// (static) geometry still renders crisply because an integer origin yields full
// or zero coverage with no partial edge.
template <typename InsidePred>
void SoftwareRenderTarget::RasterizeCoverageAA(
    float devOriginX, float devOriginY,
    float localMinX, float localMinY, float localMaxX, float localMaxY,
    Brush* brush, InsidePred inside)
{
    constexpr int kSub = 4;
    constexpr float kStep = 1.0f / kSub;
    constexpr float kInvSamples = 1.0f / (kSub * kSub);

    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    int32_t px0 = (int32_t)std::floor(devOriginX + localMinX);
    int32_t py0 = (int32_t)std::floor(devOriginY + localMinY);
    int32_t px1 = (int32_t)std::ceil(devOriginX + localMaxX);
    int32_t py1 = (int32_t)std::ceil(devOriginY + localMaxY);
    const uint64_t aaWidth = static_cast<uint64_t>(std::max(px1 - px0, 0));
    const uint64_t aaHeight = static_cast<uint64_t>(std::max(py1 - py0, 0));
    frameAaSamples_ += aaWidth * aaHeight * (kSub * kSub);
    if (!TightenToRectClip(px0, py0, px1, py1)) return;
    const bool hasRoundedClip = !roundedClipStack_.empty();

    for (int32_t py = py0; py < py1; py++) {
        int32_t rowX0 = px0;
        int32_t rowX1 = px1;
        if (hasRoundedClip && !TightenSpanToRoundedClips(py, rowX0, rowX1)) continue;
        for (int32_t px = rowX0; px < rowX1; px++) {

            int hits = 0;
            for (int sy = 0; sy < kSub; sy++) {
                float ly = ((float)py + (sy + 0.5f) * kStep) - devOriginY;
                for (int sx = 0; sx < kSub; sx++) {
                    float lx = ((float)px + (sx + 0.5f) * kStep) - devOriginX;
                    if (inside(lx, ly)) hits++;
                }
            }
            if (hits == 0) continue;

            uint8_t r, g, b, a;
            SamplePaint(paint, (float)px + 0.5f, (float)py + 0.5f, r, g, b, a);
            if (hits < kSub * kSub) {
                a = (uint8_t)((float)a * ((float)hits * kInvSamples) + 0.5f);
            }
            if (a == 0) continue;
            fb_.BlendPixelUnchecked(px, py, r, g, b, a);
        }
    }
}

void SoftwareRenderTarget::FillScanlineRect(float x, float y, float w, float h, Brush* brush)
{
    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);

    // Keep the rectangle edges in float and weight each boundary pixel by the
    // fraction it is actually covered (analytic 1px AA). The origin is no longer
    // truncated to an integer, so an animated rect moves smoothly sub-pixel
    // instead of snapping a whole pixel each frame. min/max also lets a flipped
    // (negative-scale) transform fill correctly.
    float left = std::min(tx, tx2), right = std::max(tx, tx2);
    float top = std::min(ty, ty2), bottom = std::max(ty, ty2);
    if (right - left <= 0.0f || bottom - top <= 0.0f) return;

    int32_t px0 = (int32_t)std::floor(left);
    int32_t py0 = (int32_t)std::floor(top);
    int32_t px1 = (int32_t)std::ceil(right);
    int32_t py1 = (int32_t)std::ceil(bottom);
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    const bool hasRoundedClip = !roundedClipStack_.empty();
    const int32_t fullX0 = std::max(px0, static_cast<int32_t>(std::ceil(left)));
    const int32_t fullX1 = std::min(px1, static_cast<int32_t>(std::floor(right)));

    std::shared_ptr<const SoftwareGradientRaster> gradientRaster;
    if (paint.kind == PreparedPaintKind::LinearGradient && paint.linear) {
        gradientRaster = paint.linear->GetOrCreateRaster(
            left, top, right, bottom, currentOpacity_);
    } else if (paint.kind == PreparedPaintKind::RadialGradient && paint.radial) {
        gradientRaster = paint.radial->GetOrCreateRaster(
            left, top, right, bottom, currentOpacity_);
    }

    if (gradientRaster) {
        std::vector<uint8_t> compositeKey;
        std::vector<uint8_t> destinationBefore;
        const int32_t panelWidth = px1 - px0;
        const int32_t panelHeight = py1 - py0;
        if (!gradientRaster->opaque && panelWidth > 0 && panelHeight > 0) {
            try {
                auto appendValue = [&](const auto& value) {
                    const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
                    compositeKey.insert(
                        compositeKey.end(), bytes, bytes + sizeof(value));
                };
                constexpr uint32_t kGradientCompositeTag = 0x47524443u; // "GRDC"
                appendValue(kGradientCompositeTag);
                appendValue(px0); appendValue(py0);
                appendValue(px1); appendValue(py1);
                const uint32_t roundedCount =
                    static_cast<uint32_t>(roundedClipStack_.size());
                appendValue(roundedCount);
                for (const auto& clip : roundedClipStack_) {
                    appendValue(clip.x); appendValue(clip.y);
                    appendValue(clip.w); appendValue(clip.h);
                    appendValue(clip.radiusTL); appendValue(clip.radiusTR);
                    appendValue(clip.radiusBR); appendValue(clip.radiusBL);
                }

                if (!gradientCompositeCache_)
                    gradientCompositeCache_ =
                        std::make_unique<SoftwareEffectResultCache>();
                if (auto* cached = gradientCompositeCache_->FindGradientComposited(
                        compositeKey, gradientRaster, fb_,
                        px0, py0, panelWidth, panelHeight)) {
                    const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
                    for (int32_t row = 0; row < panelHeight; ++row) {
                        const uint8_t* source = cached->outputPixels.data() +
                            static_cast<size_t>(row) * rowBytes;
                        uint8_t* destination = fb_.pixels.data() +
                            (static_cast<size_t>(py0 + row) * fb_.width + px0) * 4u;
                        std::memcpy(destination, source, rowBytes);
                    }
                    return;
                }

                const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
                destinationBefore.resize(
                    rowBytes * static_cast<size_t>(panelHeight));
                for (int32_t row = 0; row < panelHeight; ++row) {
                    const uint8_t* source = fb_.pixels.data() +
                        (static_cast<size_t>(py0 + row) * fb_.width + px0) * 4u;
                    std::memcpy(
                        destinationBefore.data() + static_cast<size_t>(row) * rowBytes,
                        source, rowBytes);
                }
            } catch (const std::bad_alloc&) {
                gradientCompositeCache_.reset();
                compositeKey.clear();
                destinationBefore.clear();
            }
        }

        auto renderCachedRows = [&](int32_t rowBegin, int32_t rowEnd) {
            for (int32_t row = rowBegin; row < rowEnd; ++row) {
                float coverageY = std::min(static_cast<float>(row) + 1.0f, bottom) -
                    std::max(static_cast<float>(row), top);
                if (coverageY <= 0.0f) continue;
                coverageY = std::min(coverageY, 1.0f);

                int32_t rowX0 = px0;
                int32_t rowX1 = px1;
                if (hasRoundedClip &&
                    !TightenSpanToRoundedClips(row, rowX0, rowX1)) continue;

                const int32_t rowFullX0 = std::max(rowX0, fullX0);
                const int32_t rowFullX1 = std::min(rowX1, fullX1);
                const size_t rasterRow = static_cast<size_t>(
                    row - gradientRaster->y) *
                    static_cast<size_t>(gradientRaster->width) * 4u;

                auto compositePixel = [&](int32_t column, uint8_t alpha) {
                    if (alpha == 0) return;
                    const size_t source = rasterRow +
                        static_cast<size_t>(column - gradientRaster->x) * 4u;
                    fb_.BlendPixelUnchecked(
                        column, row,
                        gradientRaster->pixels[source + 2],
                        gradientRaster->pixels[source + 1],
                        gradientRaster->pixels[source], alpha);
                };

                const uint8_t rowCoverage = FloatToU8(coverageY);
                if (rowFullX1 > rowFullX0) {
                    const size_t source = rasterRow +
                        static_cast<size_t>(rowFullX0 - gradientRaster->x) * 4u;
                    if (rowCoverage == 255 && gradientRaster->opaque) {
                        uint8_t* destination = fb_.pixels.data() +
                            (static_cast<size_t>(row) * fb_.width + rowFullX0) * 4u;
                        std::memcpy(destination,
                            gradientRaster->pixels.data() + source,
                            static_cast<size_t>(rowFullX1 - rowFullX0) * 4u);
                    } else {
                        for (int32_t column = rowFullX0;
                             column < rowFullX1; ++column) {
                            const size_t pixel = rasterRow +
                                static_cast<size_t>(column - gradientRaster->x) * 4u;
                            uint8_t alpha = gradientRaster->pixels[pixel + 3];
                            if (rowCoverage != 255) {
                                alpha = static_cast<uint8_t>(
                                    (static_cast<uint32_t>(alpha) * rowCoverage + 127u) /
                                    255u);
                            }
                            compositePixel(column, alpha);
                        }
                    }
                }

                auto compositeBoundary = [&](int32_t column) {
                    if (column < rowX0 || column >= rowX1) return;
                    float coverageX =
                        std::min(static_cast<float>(column) + 1.0f, right) -
                        std::max(static_cast<float>(column), left);
                    if (coverageX <= 0.0f) return;
                    coverageX = std::min(coverageX, 1.0f);
                    const size_t pixel = rasterRow +
                        static_cast<size_t>(column - gradientRaster->x) * 4u;
                    uint8_t alpha = gradientRaster->pixels[pixel + 3];
                    const float coverage = coverageX * coverageY;
                    if (coverage < 1.0f) {
                        alpha = static_cast<uint8_t>(
                            static_cast<float>(alpha) * coverage + 0.5f);
                    }
                    compositePixel(column, alpha);
                };

                if (rowFullX1 <= rowFullX0) {
                    for (int32_t column = rowX0; column < rowX1; ++column) {
                        compositeBoundary(column);
                    }
                } else {
                    for (int32_t column = rowX0; column < rowFullX0; ++column) {
                        compositeBoundary(column);
                    }
                    for (int32_t column = rowFullX1; column < rowX1; ++column) {
                        compositeBoundary(column);
                    }
                }
            }
        };

        const uint64_t pixelWork = static_cast<uint64_t>(px1 - px0) *
            static_cast<uint64_t>(py1 - py0);
        SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
        if (!hasRoundedClip && workerPool && workerPool->CanParallelize() &&
            pixelWork >= 256u * 1024u && py1 - py0 >= 32) {
            workerPool->ParallelFor(py0, py1, 16, renderCachedRows);
        } else {
            renderCachedRows(py0, py1);
        }

        if (gradientCompositeCache_ && !compositeKey.empty() &&
            !destinationBefore.empty()) {
            try {
                SoftwareEffectResultCache::Entry entry;
                entry.key = std::move(compositeKey);
                entry.contextPixels = std::move(destinationBefore);
                entry.gradientRaster = gradientRaster;
                entry.panelX = px0;
                entry.panelY = py0;
                entry.panelWidth = panelWidth;
                entry.panelHeight = panelHeight;
                const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
                entry.outputPixels.resize(
                    rowBytes * static_cast<size_t>(panelHeight));
                for (int32_t row = 0; row < panelHeight; ++row) {
                    const uint8_t* source = fb_.pixels.data() +
                        (static_cast<size_t>(py0 + row) * fb_.width + px0) * 4u;
                    std::memcpy(
                        entry.outputPixels.data() + static_cast<size_t>(row) * rowBytes,
                        source, rowBytes);
                }
                gradientCompositeCache_->Store(std::move(entry));
            } catch (const std::bad_alloc&) {
                gradientCompositeCache_.reset();
            }
        }
        return;
    }

    auto renderRows = [&](int32_t rowBegin, int32_t rowEnd) {
    for (int32_t row = rowBegin; row < rowEnd; row++) {
        float covY = std::min((float)row + 1.0f, bottom) - std::max((float)row, top);
        if (covY <= 0.0f) continue;
        if (covY > 1.0f) covY = 1.0f;
        int32_t rowX0 = px0;
        int32_t rowX1 = px1;
        if (hasRoundedClip && !TightenSpanToRoundedClips(row, rowX0, rowX1)) continue;
        const int32_t rowFullX0 = std::max(rowX0, fullX0);
        const int32_t rowFullX1 = std::min(rowX1, fullX1);

        // At most one pixel on either side has fractional horizontal
        // coverage. Everything between them is one contiguous span.
        const uint8_t rowCoverage = FloatToU8(covY);
        if (rowFullX1 > rowFullX0) {
            CompositeSpan(row, rowFullX0, rowFullX1, paint, rowCoverage);
        }

        auto compositeBoundary = [&](int32_t col) {
            if (col < rowX0 || col >= rowX1) return;
            float covX = std::min((float)col + 1.0f, right) - std::max((float)col, left);
            if (covX <= 0.0f) return;
            if (covX > 1.0f) covX = 1.0f;
            float cov = covX * covY;
            uint8_t r, g, b, a;
            SamplePaint(paint, (float)col + 0.5f, (float)row + 0.5f, r, g, b, a);
            if (cov < 1.0f) a = (uint8_t)((float)a * cov + 0.5f);
            if (a != 0) fb_.BlendPixelUnchecked(col, row, r, g, b, a);
        };

        if (rowFullX1 <= rowFullX0) {
            for (int32_t col = rowX0; col < rowX1; ++col) compositeBoundary(col);
        } else {
            for (int32_t col = rowX0; col < rowFullX0; ++col) compositeBoundary(col);
            for (int32_t col = rowFullX1; col < rowX1; ++col) compositeBoundary(col);
        }
    }
    };

    const uint64_t pixelWork = static_cast<uint64_t>(px1 - px0) *
        static_cast<uint64_t>(py1 - py0);
    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    if (!hasRoundedClip && workerPool && workerPool->CanParallelize() &&
        pixelWork >= 256u * 1024u && py1 - py0 >= 32) {
        workerPool->ParallelFor(py0, py1, 16, renderRows);
    } else {
        renderRows(py0, py1);
    }
}

void SoftwareRenderTarget::StrokeScanlineRect(float x, float y, float w, float h, Brush* brush, float strokeWidth)
{
    if (!brush) return;

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx, th = ty2 - ty;
    if (tw <= 0.0f || th <= 0.0f) return;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    float tswX = strokeWidth * sx;   // left/right edge thickness (device px)
    float tswY = strokeWidth * sy;   // top/bottom edge thickness (device px)

    // Single coverage pass instead of tiling four FillScanlineRect bands. With
    // analytic edge coverage the old four-band approach double-blended the single
    // boundary row where the top/bottom band met the left/right band, leaving a
    // faint under-covered seam at each corner for fractional stroke/origin. The
    // ring predicate (inside the outer rect AND outside the inner hole) evaluates
    // and blends every pixel exactly once.
    RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
        [tw, th, tswX, tswY](float lx, float ly) -> bool {
            if (lx < 0.0f || lx > tw || ly < 0.0f || ly > th) return false;
            float innerL = tswX, innerR = tw - tswX;
            float innerT = tswY, innerB = th - tswY;
            if (innerR <= innerL || innerB <= innerT) return true;   // stroke fills whole rect
            if (lx >= innerL && lx <= innerR && ly >= innerT && ly <= innerB) return false;
            return true;
        });
}

void SoftwareRenderTarget::DrawBresenhamLine(float x1, float y1, float x2, float y2,
    uint8_t r, uint8_t g, uint8_t b, uint8_t a, float strokeWidth)
{
    float tx1, ty1, tx2, ty2;
    currentTransform_.Apply(x1, y1, tx1, ty1);
    currentTransform_.Apply(x2, y2, tx2, ty2);

    // Float distance-to-segment coverage instead of an integer-seeded Bresenham
    // stamp: the transformed endpoints stay fractional, so an animated/rotated
    // line moves smoothly across the pixel grid and gets a 1px analytic AA edge,
    // matching the GPU backends' feathered lines (no whole-pixel snapping).
    float avgScale = (std::abs(currentTransform_.m[0]) + std::abs(currentTransform_.m[3])) * 0.5f;
    float halfW = std::max(0.5f, strokeWidth * avgScale * 0.5f);

    float minXf = std::min(tx1, tx2) - halfW - 1.0f;
    float maxXf = std::max(tx1, tx2) + halfW + 1.0f;
    float minYf = std::min(ty1, ty2) - halfW - 1.0f;
    float maxYf = std::max(ty1, ty2) + halfW + 1.0f;

    int32_t px0 = std::max(0, (int32_t)std::floor(minXf));
    int32_t py0 = std::max(0, (int32_t)std::floor(minYf));
    int32_t px1 = std::min(width_, (int32_t)std::ceil(maxXf));
    int32_t py1 = std::min(height_, (int32_t)std::ceil(maxYf));

    const SoftwareLineRasterCache::Entry* cached = nullptr;
    try {
        if (!lineRasterCache_)
            lineRasterCache_ = std::make_unique<SoftwareLineRasterCache>();
        cached = lineRasterCache_->Find(
            tx1, ty1, tx2, ty2, halfW, width_, height_);
    } catch (const std::bad_alloc&) {
        lineRasterCache_.reset();
    }

    SoftwareLineRasterCache::Entry uncached;
    if (!cached) {
        uncached.x1 = tx1;
        uncached.y1 = ty1;
        uncached.x2 = tx2;
        uncached.y2 = ty2;
        uncached.halfWidth = halfW;
        uncached.targetWidth = width_;
        uncached.targetHeight = height_;
        uncached.spans.reserve(static_cast<size_t>(std::max(py1 - py0, 0)) * 4u);

        const float segDX = tx2 - tx1;
        const float segDY = ty2 - ty1;
        const float lenSq = segDX * segDX + segDY * segDY;
        for (int32_t py = py0; py < py1; ++py) {
            const float fy = static_cast<float>(py) + 0.5f;
            int32_t runStart = px0;
            float runCoverage = -1.0f;
            auto flush = [&](int32_t runEnd) {
                if (runCoverage > 0.0f && runEnd > runStart) {
                    uncached.spans.push_back({
                        py, runStart, runEnd - runStart, runCoverage });
                }
            };
            for (int32_t px = px0; px < px1; ++px) {
                const float fx = static_cast<float>(px) + 0.5f;
                const float t = lenSq > 0.0f
                    ? std::clamp(
                        ((fx - tx1) * segDX + (fy - ty1) * segDY) / lenSq,
                        0.0f, 1.0f)
                    : 0.0f;
                const float closestX = tx1 + t * segDX;
                const float closestY = ty1 + t * segDY;
                const float deltaX = fx - closestX;
                const float deltaY = fy - closestY;
                const float distance = std::sqrt(
                    deltaX * deltaX + deltaY * deltaY);
                const float coverage = std::clamp(
                    halfW + 0.5f - distance, 0.0f, 1.0f);
                if (coverage != runCoverage) {
                    flush(px);
                    runStart = px;
                    runCoverage = coverage;
                }
            }
            flush(px1);
        }

        if (lineRasterCache_) {
            try {
                cached = lineRasterCache_->Store(uncached);
            } catch (const std::bad_alloc&) {
                lineRasterCache_.reset();
            }
        }
    }

    const auto& spans = cached ? cached->spans : uncached.spans;
    for (const auto& span : spans) {
        int32_t spanX0 = span.x;
        int32_t spanY0 = span.y;
        int32_t spanX1 = span.x + span.width;
        int32_t spanY1 = span.y + 1;
        if (!TightenToRectClip(spanX0, spanY0, spanX1, spanY1)) continue;
        if (!roundedClipStack_.empty() &&
            !TightenSpanToRoundedClips(span.y, spanX0, spanX1)) continue;
        const uint8_t coveredAlpha = span.coverage < 1.0f
            ? static_cast<uint8_t>(static_cast<float>(a) * span.coverage + 0.5f)
            : a;
        if (coveredAlpha != 0) {
            fb_.BlendSolidSpan(
                span.y, spanX0, spanX1, r, g, b, coveredAlpha);
        }
    }
}

void SoftwareRenderTarget::FillRectangle(float x, float y, float w, float h, Brush* brush)
{
    if (!brush) return;
    FillScanlineRect(x, y, w, h, brush);
}

void SoftwareRenderTarget::DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    // The per-corner ring path restricts work to the four stroke bands instead
    // of supersampling the empty centre of the rectangle.
    DrawPerCornerRoundedRectangle(
        x, y, w, h, 0.0f, 0.0f, 0.0f, 0.0f, brush, strokeWidth);
}

void SoftwareRenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
    if (!brush) return;
    rx = std::min(rx, w * 0.5f);
    ry = std::min(ry, h * 0.5f);

    // UI corner radii are circular in the overwhelmingly common case. Reuse
    // the exact per-corner scanline implementation when the current transform
    // preserves that circle; retain the general ellipse predicate below for
    // anisotropic scaling and explicit rx != ry.
    const float matrixScaleX = std::abs(currentTransform_.m[0]);
    const float matrixScaleY = std::abs(currentTransform_.m[3]);
    if (std::abs(currentTransform_.m[1]) < 1e-6f &&
        std::abs(currentTransform_.m[2]) < 1e-6f &&
        std::abs(rx - ry) < 1e-6f &&
        std::abs(matrixScaleX - matrixScaleY) < 1e-6f) {
        FillPerCornerRoundedRectangle(x, y, w, h, rx, rx, rx, rx, brush);
        return;
    }

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    if (tw <= 0.0f || th <= 0.0f) return;
    float trx = rx * sx, try_ = ry * sy;

    const PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;
    const int32_t maskX = static_cast<int32_t>(std::floor(tx));
    const int32_t maskY = static_cast<int32_t>(std::floor(ty));
    const int32_t maskWidth =
        static_cast<int32_t>(std::ceil(tx + tw)) - maskX;
    const int32_t maskHeight =
        static_cast<int32_t>(std::ceil(ty + th)) - maskY;
    int32_t px0 = maskX;
    int32_t py0 = maskY;
    int32_t px1 = maskX + maskWidth;
    int32_t py1 = maskY + maskHeight;
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    try {
        if (!ellipticalRoundedRectMaskCache_)
            ellipticalRoundedRectMaskCache_ =
                std::make_unique<SoftwareEllipticalRoundedRectMaskCache>();
        const auto* mask = ellipticalRoundedRectMaskCache_->GetOrBuild(
            maskWidth, maskHeight,
            tx - static_cast<float>(maskX),
            ty - static_cast<float>(maskY),
            tw, th, trx, try_);
        if (mask) {
            for (const auto& span : mask->spans) {
                const int32_t destinationY = maskY + span.y;
                if (destinationY < py0 || destinationY >= py1) continue;
                int32_t destinationX0 = std::max(px0, maskX + span.x);
                int32_t destinationX1 = std::min(
                    px1, maskX + span.x + span.width);
                if (!roundedClipStack_.empty() &&
                    !TightenSpanToRoundedClips(
                        destinationY, destinationX0, destinationX1)) continue;
                if (destinationX1 <= destinationX0) continue;

                if (paint.IsSolid()) {
                    const uint8_t alpha = span.hits == 16
                        ? paint.a
                        : static_cast<uint8_t>(
                            static_cast<float>(paint.a) *
                            (static_cast<float>(span.hits) / 16.0f) + 0.5f);
                    fb_.BlendSolidSpan(
                        destinationY, destinationX0, destinationX1,
                        paint.r, paint.g, paint.b, alpha);
                } else if (span.hits == 16) {
                    CompositeSpan(
                        destinationY, destinationX0, destinationX1, paint);
                } else {
                    const float coverage =
                        static_cast<float>(span.hits) / 16.0f;
                    for (int32_t destinationX = destinationX0;
                         destinationX < destinationX1; ++destinationX) {
                        uint8_t red, green, blue, alpha;
                        SamplePaint(
                            paint,
                            static_cast<float>(destinationX) + 0.5f,
                            static_cast<float>(destinationY) + 0.5f,
                            red, green, blue, alpha);
                        alpha = static_cast<uint8_t>(
                            static_cast<float>(alpha) * coverage + 0.5f);
                        if (alpha != 0) {
                            fb_.BlendPixelUnchecked(
                                destinationX, destinationY,
                                red, green, blue, alpha);
                        }
                    }
                }
            }
            return;
        }
    } catch (const std::bad_alloc&) {
        ellipticalRoundedRectMaskCache_.reset();
    }

    // Sub-pixel + AA fill: feed the float origin (tx,ty) to RasterizeCoverageAA
    // instead of truncating it, so an animated rounded rect tracks its true
    // position and the corners are anti-aliased like the GPU backends.
    RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
        [tw, th, trx, try_](float cx, float cy) -> bool {
            if (cx < 0.0f || cx > tw || cy < 0.0f || cy > th) return false;
            if (trx <= 0.0f || try_ <= 0.0f) return true;
            if (cx < trx && cy < try_) {
                float dx = (cx - trx) / trx, dy = (cy - try_) / try_;
                return dx * dx + dy * dy <= 1.0f;
            }
            if (cx > tw - trx && cy < try_) {
                float dx = (cx - (tw - trx)) / trx, dy = (cy - try_) / try_;
                return dx * dx + dy * dy <= 1.0f;
            }
            if (cx < trx && cy > th - try_) {
                float dx = (cx - trx) / trx, dy = (cy - (th - try_)) / try_;
                return dx * dx + dy * dy <= 1.0f;
            }
            if (cx > tw - trx && cy > th - try_) {
                float dx = (cx - (tw - trx)) / trx, dy = (cy - (th - try_)) / try_;
                return dx * dx + dy * dy <= 1.0f;
            }
            return true;
        });
}

void SoftwareRenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    rx = std::min(rx, w * 0.5f);
    ry = std::min(ry, h * 0.5f);

    const float matrixScaleX = std::abs(currentTransform_.m[0]);
    const float matrixScaleY = std::abs(currentTransform_.m[3]);
    if (std::abs(currentTransform_.m[1]) < 1e-6f &&
        std::abs(currentTransform_.m[2]) < 1e-6f &&
        std::abs(rx - ry) < 1e-6f &&
        std::abs(matrixScaleX - matrixScaleY) < 1e-6f) {
        DrawPerCornerRoundedRectangle(
            x, y, w, h, rx, rx, rx, rx, brush, strokeWidth);
        return;
    }

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    if (tw <= 0.0f || th <= 0.0f) return;
    float trx = rx * sx, try_ = ry * sy;
    float tsw = strokeWidth * std::min(sx, sy);
    float innerRx = std::max(0.0f, trx - tsw);
    float innerRy = std::max(0.0f, try_ - tsw);

    // Sub-pixel + AA stroke ring: covered when inside the outer rounded rect AND
    // outside the inner one. Float origin → no whole-pixel snapping under
    // animation; partial coverage anti-aliases both ring edges.
    RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
        [tw, th, trx, try_, tsw, innerRx, innerRy](float cx, float cy) -> bool {
            // Inside the outer rounded rect?
            if (cx < 0.0f || cx > tw || cy < 0.0f || cy > th) return false;
            if (trx > 0.0f && try_ > 0.0f) {
                if (cx < trx && cy < try_) {
                    float dx = (cx - trx) / trx, dy = (cy - try_) / try_;
                    if (dx * dx + dy * dy > 1.0f) return false;
                } else if (cx > tw - trx && cy < try_) {
                    float dx = (cx - (tw - trx)) / trx, dy = (cy - try_) / try_;
                    if (dx * dx + dy * dy > 1.0f) return false;
                } else if (cx < trx && cy > th - try_) {
                    float dx = (cx - trx) / trx, dy = (cy - (th - try_)) / try_;
                    if (dx * dx + dy * dy > 1.0f) return false;
                } else if (cx > tw - trx && cy > th - try_) {
                    float dx = (cx - (tw - trx)) / trx, dy = (cy - (th - try_)) / try_;
                    if (dx * dx + dy * dy > 1.0f) return false;
                }
            }
            // Outside the inner rounded rect (i.e. within the stroke ring)?
            float icx = cx - tsw, icy = cy - tsw;
            float innerW = tw - tsw * 2.0f, innerH = th - tsw * 2.0f;
            if (innerW <= 0.0f || innerH <= 0.0f) return true;
            if (icx < 0.0f || icy < 0.0f || icx > innerW || icy > innerH) return true;
            if (innerRx > 0.0f && innerRy > 0.0f) {
                if (icx < innerRx && icy < innerRy) {
                    float dx = (icx - innerRx) / innerRx, dy = (icy - innerRy) / innerRy;
                    return dx * dx + dy * dy > 1.0f;
                } else if (icx > innerW - innerRx && icy < innerRy) {
                    float dx = (icx - (innerW - innerRx)) / innerRx, dy = (icy - innerRy) / innerRy;
                    return dx * dx + dy * dy > 1.0f;
                } else if (icx < innerRx && icy > innerH - innerRy) {
                    float dx = (icx - innerRx) / innerRx, dy = (icy - (innerH - innerRy)) / innerRy;
                    return dx * dx + dy * dy > 1.0f;
                } else if (icx > innerW - innerRx && icy > innerH - innerRy) {
                    float dx = (icx - (innerW - innerRx)) / innerRx, dy = (icy - (innerH - innerRy)) / innerRy;
                    return dx * dx + dy * dy > 1.0f;
                }
            }
            // Inside the inner straight region → part of the hole, not the ring.
            return false;
        });
}

void SoftwareRenderTarget::FillPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush)
{
    if (!brush) return;
    tl = std::min(tl, std::min(w, h) * 0.5f);
    tr = std::min(tr, std::min(w, h) * 0.5f);
    br = std::min(br, std::min(w, h) * 0.5f);
    bl = std::min(bl, std::min(w, h) * 0.5f);

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    float s = std::min(sx, sy);
    float ttl = tl * s, ttr = tr * s, tbr = br * s, tbl = bl * s;

    if (tw <= 0.0f || th <= 0.0f) return;

    if (ttl <= 0.0f && ttr <= 0.0f && tbr <= 0.0f && tbl <= 0.0f) {
        FillScanlineRect(x, y, w, h, brush);
        return;
    }

    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    int32_t px0 = static_cast<int32_t>(std::floor(tx));
    int32_t py0 = static_cast<int32_t>(std::floor(ty));
    int32_t px1 = static_cast<int32_t>(std::ceil(tx + tw));
    int32_t py1 = static_cast<int32_t>(std::ceil(ty + th));
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    if (paint.IsSolid()) {
        const int32_t maskX = static_cast<int32_t>(std::floor(tx));
        const int32_t maskY = static_cast<int32_t>(std::floor(ty));
        const int32_t maskWidth = static_cast<int32_t>(std::ceil(tx + tw)) - maskX;
        const int32_t maskHeight = static_cast<int32_t>(std::ceil(ty + th)) - maskY;
        try {
            if (!roundedRectMaskCache_)
                roundedRectMaskCache_ = std::make_unique<SoftwareRoundedRectMaskCache>();
            const auto* mask = roundedRectMaskCache_->GetOrBuild(
                maskWidth, maskHeight,
                tx - maskX, ty - maskY,
                tw, th, ttl, ttr, tbr, tbl,
                0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 0.0f,
                false);
            if (mask) {
                for (const auto& span : mask->spans) {
                    const int32_t destinationY = maskY + span.y;
                    if (destinationY < py0 || destinationY >= py1) continue;
                    int32_t destinationX0 = std::max(px0, maskX + span.x);
                    int32_t destinationX1 = std::min(
                        px1, maskX + span.x + span.width);
                    if (!roundedClipStack_.empty() &&
                        !TightenSpanToRoundedClips(
                            destinationY, destinationX0, destinationX1)) continue;
                    if (destinationX1 <= destinationX0) continue;
                    const uint8_t alpha = span.hits == 16
                        ? paint.a
                        : static_cast<uint8_t>(
                            static_cast<float>(paint.a) *
                            (static_cast<float>(span.hits) / 16.0f) + 0.5f);
                    fb_.BlendSolidSpan(
                        destinationY, destinationX0, destinationX1,
                        paint.r, paint.g, paint.b, alpha);
                }
                return;
            }
        } catch (const std::bad_alloc&) {
            roundedRectMaskCache_.reset();
        }
    }

    if (!roundedClipStack_.empty()) {
        RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
            [tw, th, ttl, ttr, tbr, tbl](float cx, float cy) -> bool {
                return IsInsidePerCornerRoundedRect(cx, cy, tw, th, ttl, ttr, tbr, tbl);
            });
        return;
    }

    constexpr int kSub = 4;
    constexpr float kStep = 1.0f / static_cast<float>(kSub);
    constexpr float kFirstSample = 0.5f * kStep;
    constexpr float kLastSample = 1.0f - kFirstSample;

    auto intervalAtY = [tw, th, ttl, ttr, tbr, tbl](
        float localY, float& left, float& right) -> bool {
        if (localY < 0.0f || localY > th) return false;

        left = 0.0f;
        if (ttl > 0.0f && localY < ttl) {
            const float dy = (localY - ttl) / ttl;
            left = ttl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        } else if (tbl > 0.0f && localY > th - tbl) {
            const float dy = (localY - (th - tbl)) / tbl;
            left = tbl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        }

        right = tw;
        if (ttr > 0.0f && localY < ttr) {
            const float dy = (localY - ttr) / ttr;
            right = tw - ttr + ttr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        } else if (tbr > 0.0f && localY > th - tbr) {
            const float dy = (localY - (th - tbr)) / tbr;
            right = tw - tbr + tbr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        }
        return right >= left;
    };

    for (int32_t py = py0; py < py1; ++py) {
        float rowLeft[kSub] = {};
        float rowRight[kSub] = {};
        bool rowValid[kSub] = {};
        float maxLeft = 0.0f;
        float minRight = tw;
        int validSubRows = 0;
        for (int sy = 0; sy < kSub; ++sy) {
            const float localY =
                (static_cast<float>(py) + (sy + 0.5f) * kStep) - ty;
            float left = 0.0f, right = 0.0f;
            if (!intervalAtY(localY, left, right)) continue;
            rowLeft[sy] = left;
            rowRight[sy] = right;
            rowValid[sy] = true;
            maxLeft = validSubRows == 0 ? left : std::max(maxLeft, left);
            minRight = validSubRows == 0 ? right : std::min(minRight, right);
            ++validSubRows;
        }
        if (validSubRows == 0) continue;

        // Every horizontal sample in [coreX0, coreX1) is inside each valid
        // sub-row. Its only coverage reduction can therefore be the fractional
        // top/bottom row, represented by one shared span alpha.
        int32_t coreX0 = static_cast<int32_t>(
            std::ceil(tx + maxLeft - kFirstSample));
        int32_t coreX1 = static_cast<int32_t>(
            std::floor(tx + minRight - kLastSample)) + 1;
        coreX0 = std::clamp(coreX0, px0, px1);
        coreX1 = std::clamp(coreX1, px0, px1);

        const uint8_t coreCoverage = static_cast<uint8_t>(
            (validSubRows * 255 + kSub / 2) / kSub);
        if (coreX1 > coreX0) {
            CompositeSpan(py, coreX0, coreX1, paint, coreCoverage);
        }

        auto compositeEdgePixel = [&](int32_t px) {
            int hits = 0;
            for (int sy = 0; sy < kSub; ++sy) {
                if (!rowValid[sy]) continue;
                for (int sx = 0; sx < kSub; ++sx) {
                    const float localX =
                        (static_cast<float>(px) + (sx + 0.5f) * kStep) - tx;
                    if (localX >= rowLeft[sy] && localX <= rowRight[sy]) {
                        ++hits;
                    }
                }
            }
            if (hits == 0) return;
            uint8_t r, g, b, a;
            SamplePaint(paint, static_cast<float>(px) + 0.5f,
                        static_cast<float>(py) + 0.5f, r, g, b, a);
            if (hits < kSub * kSub) {
                a = static_cast<uint8_t>(
                    static_cast<float>(a) *
                    (static_cast<float>(hits) / (kSub * kSub)) + 0.5f);
            }
            if (a != 0) fb_.BlendPixelUnchecked(px, py, r, g, b, a);
        };

        if (coreX1 <= coreX0) {
            for (int32_t px = px0; px < px1; ++px) compositeEdgePixel(px);
        } else {
            for (int32_t px = px0; px < coreX0; ++px) compositeEdgePixel(px);
            for (int32_t px = coreX1; px < px1; ++px) compositeEdgePixel(px);
        }
    }
}

void SoftwareRenderTarget::DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    tl = std::min(tl, std::min(w, h) * 0.5f);
    tr = std::min(tr, std::min(w, h) * 0.5f);
    br = std::min(br, std::min(w, h) * 0.5f);
    bl = std::min(bl, std::min(w, h) * 0.5f);

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    float s = std::min(sx, sy);
    float ttl = tl * s, ttr = tr * s, tbr = br * s, tbl = bl * s;
    float tsw = strokeWidth * s;
    if (tw <= 0.0f || th <= 0.0f) return;

    float iTl = std::max(0.0f, ttl - tsw), iTr = std::max(0.0f, ttr - tsw);
    float iBr = std::max(0.0f, tbr - tsw), iBl = std::max(0.0f, tbl - tsw);
    float innerW = tw - tsw * 2.0f, innerH = th - tsw * 2.0f;

    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    auto insideStroke = [tw, th, ttl, ttr, tbr, tbl, tsw,
                         iTl, iTr, iBr, iBl, innerW, innerH](
        float cx, float cy) -> bool {
        if (!IsInsidePerCornerRoundedRect(cx, cy, tw, th, ttl, ttr, tbr, tbl)) return false;
        if (innerW > 0.0f && innerH > 0.0f &&
            IsInsidePerCornerRoundedRect(
                cx - tsw, cy - tsw, innerW, innerH, iTl, iTr, iBr, iBl)) {
            return false;
        }
        return true;
    };

    int32_t px0 = static_cast<int32_t>(std::floor(tx));
    int32_t py0 = static_cast<int32_t>(std::floor(ty));
    int32_t px1 = static_cast<int32_t>(std::ceil(tx + tw));
    int32_t py1 = static_cast<int32_t>(std::ceil(ty + th));
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    if (paint.IsSolid()) {
        const int32_t maskX = static_cast<int32_t>(std::floor(tx));
        const int32_t maskY = static_cast<int32_t>(std::floor(ty));
        const int32_t maskWidth = static_cast<int32_t>(std::ceil(tx + tw)) - maskX;
        const int32_t maskHeight = static_cast<int32_t>(std::ceil(ty + th)) - maskY;
        try {
            if (!roundedRectMaskCache_)
                roundedRectMaskCache_ = std::make_unique<SoftwareRoundedRectMaskCache>();
            const auto* mask = roundedRectMaskCache_->GetOrBuild(
                maskWidth, maskHeight,
                tx - maskX, ty - maskY,
                tw, th, ttl, ttr, tbr, tbl,
                tsw, innerW, innerH,
                iTl, iTr, iBr, iBl,
                true);
            if (mask) {
                for (const auto& span : mask->spans) {
                    const int32_t destinationY = maskY + span.y;
                    if (destinationY < py0 || destinationY >= py1) continue;
                    int32_t destinationX0 = std::max(px0, maskX + span.x);
                    int32_t destinationX1 = std::min(
                        px1, maskX + span.x + span.width);
                    if (!roundedClipStack_.empty() &&
                        !TightenSpanToRoundedClips(
                            destinationY, destinationX0, destinationX1)) continue;
                    if (destinationX1 <= destinationX0) continue;
                    const uint8_t alpha = span.hits == 16
                        ? paint.a
                        : static_cast<uint8_t>(
                            static_cast<float>(paint.a) *
                            (static_cast<float>(span.hits) / 16.0f) + 0.5f);
                    fb_.BlendSolidSpan(
                        destinationY, destinationX0, destinationX1,
                        paint.r, paint.g, paint.b, alpha);
                }
                return;
            }
        } catch (const std::bad_alloc&) {
            roundedRectMaskCache_.reset();
        }
    }

    if (!roundedClipStack_.empty()) {
        RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush, insideStroke);
        return;
    }

    constexpr int kSub = 4;
    constexpr float kStep = 1.0f / static_cast<float>(kSub);

    auto outerIntervalAtY = [tw, th, ttl, ttr, tbr, tbl](
        float localY, float& left, float& right) -> bool {
        if (localY < 0.0f || localY > th) return false;
        left = 0.0f;
        if (ttl > 0.0f && localY < ttl) {
            const float dy = (localY - ttl) / ttl;
            left = ttl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        } else if (tbl > 0.0f && localY > th - tbl) {
            const float dy = (localY - (th - tbl)) / tbl;
            left = tbl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        }

        right = tw;
        if (ttr > 0.0f && localY < ttr) {
            const float dy = (localY - ttr) / ttr;
            right = tw - ttr + ttr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        } else if (tbr > 0.0f && localY > th - tbr) {
            const float dy = (localY - (th - tbr)) / tbr;
            right = tw - tbr + tbr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        }
        return right >= left;
    };

    auto innerIntervalAtY = [innerW, innerH, iTl, iTr, iBr, iBl, tsw](
        float outerLocalY, float& left, float& right) -> bool {
        if (innerW <= 0.0f || innerH <= 0.0f) return false;
        const float localY = outerLocalY - tsw;
        if (localY < 0.0f || localY > innerH) return false;

        left = tsw;
        if (iTl > 0.0f && localY < iTl) {
            const float dy = (localY - iTl) / iTl;
            left += iTl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        } else if (iBl > 0.0f && localY > innerH - iBl) {
            const float dy = (localY - (innerH - iBl)) / iBl;
            left += iBl * (1.0f - std::sqrt(std::max(0.0f, 1.0f - dy * dy)));
        }

        right = tsw + innerW;
        if (iTr > 0.0f && localY < iTr) {
            const float dy = (localY - iTr) / iTr;
            right = tsw + innerW - iTr +
                iTr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        } else if (iBr > 0.0f && localY > innerH - iBr) {
            const float dy = (localY - (innerH - iBr)) / iBr;
            right = tsw + innerW - iBr +
                iBr * std::sqrt(std::max(0.0f, 1.0f - dy * dy));
        }
        return right >= left;
    };

    for (int32_t py = py0; py < py1; ++py) {
        float outerLeft[kSub] = {};
        float outerRight[kSub] = {};
        float innerLeft[kSub] = {};
        float innerRight[kSub] = {};
        bool outerValid[kSub] = {};
        bool innerValid[kSub] = {};
        bool everySubRowHasInner = true;
        float maxInnerLeft = 0.0f;
        float minInnerRight = tw;
        for (int sy = 0; sy < kSub; ++sy) {
            const float localY =
                (static_cast<float>(py) + (sy + 0.5f) * kStep) - ty;
            outerValid[sy] = outerIntervalAtY(
                localY, outerLeft[sy], outerRight[sy]);
            innerValid[sy] = innerIntervalAtY(
                localY, innerLeft[sy], innerRight[sy]);
            if (!innerValid[sy]) {
                everySubRowHasInner = false;
                continue;
            }
            maxInnerLeft = sy == 0
                ? innerLeft[sy] : std::max(maxInnerLeft, innerLeft[sy]);
            minInnerRight = sy == 0
                ? innerRight[sy] : std::min(minInnerRight, innerRight[sy]);
        }

        int32_t leftEnd = px1;
        int32_t rightStart = px1;
        if (everySubRowHasInner && minInnerRight > maxInnerLeft) {
            // Add one conservative pixel at each side of the inner boundary;
            // only those bands can contain stroke coverage on this row.
            leftEnd = std::clamp(
                static_cast<int32_t>(std::ceil(tx + maxInnerLeft)) + 1,
                px0, px1);
            rightStart = std::clamp(
                static_cast<int32_t>(std::floor(tx + minInnerRight)) - 1,
                px0, px1);
        }

        auto compositeRange = [&](int32_t begin, int32_t end) {
            int32_t solidRunStart = begin;
            int solidRunHits = -1;
            auto flushSolidRun = [&](int32_t runEnd) {
                if (solidRunHits <= 0 || runEnd <= solidRunStart) return;
                const uint8_t alpha = solidRunHits == kSub * kSub
                    ? paint.a
                    : static_cast<uint8_t>(
                        static_cast<float>(paint.a) *
                        (static_cast<float>(solidRunHits) / (kSub * kSub)) + 0.5f);
                fb_.BlendSolidSpan(
                    py, solidRunStart, runEnd, paint.r, paint.g, paint.b, alpha);
            };

            for (int32_t px = begin; px < end; ++px) {
                int hits = 0;
                for (int sy = 0; sy < kSub; ++sy) {
                    if (!outerValid[sy]) continue;
                    for (int sx = 0; sx < kSub; ++sx) {
                        const float localX =
                            (static_cast<float>(px) + (sx + 0.5f) * kStep) - tx;
                        const bool insideOuter =
                            localX >= outerLeft[sy] && localX <= outerRight[sy];
                        const bool insideInner = innerValid[sy] &&
                            localX >= innerLeft[sy] && localX <= innerRight[sy];
                        if (insideOuter && !insideInner) ++hits;
                    }
                }
                if (paint.IsSolid()) {
                    if (hits != solidRunHits) {
                        flushSolidRun(px);
                        solidRunStart = px;
                        solidRunHits = hits;
                    }
                    continue;
                }
                if (hits == 0) continue;
                uint8_t r, g, b, a;
                SamplePaint(paint, static_cast<float>(px) + 0.5f,
                            static_cast<float>(py) + 0.5f, r, g, b, a);
                if (hits < kSub * kSub) {
                    a = static_cast<uint8_t>(
                        static_cast<float>(a) *
                        (static_cast<float>(hits) / (kSub * kSub)) + 0.5f);
                }
                if (a != 0) fb_.BlendPixelUnchecked(px, py, r, g, b, a);
            }
            if (paint.IsSolid()) flushSolidRun(end);
        };

        if (!everySubRowHasInner || rightStart <= leftEnd) {
            compositeRange(px0, px1);
        } else {
            compositeRange(px0, leftEnd);
            compositeRange(rightStart, px1);
        }
    }
}

void SoftwareRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
    if (!brush) return;
    float tx, ty, tx2, ty2;
    currentTransform_.Apply(cx, cy, tx, ty);
    currentTransform_.Apply(cx + rx, cy + ry, tx2, ty2);
    float trx = tx2 - tx, try_ = ty2 - ty;
    if (trx <= 0.0f || try_ <= 0.0f) return;

    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    auto insideEllipse = [trx, try_](float lx, float ly) -> bool {
            float ex = lx / trx, ey = ly / try_;
            return ex * ex + ey * ey <= 1.0f;
        };

    if (!roundedClipStack_.empty()) {
        RasterizeCoverageAA(tx, ty, -trx, -try_, trx, try_, brush, insideEllipse);
        return;
    }

    int32_t px0 = static_cast<int32_t>(std::floor(tx - trx));
    int32_t py0 = static_cast<int32_t>(std::floor(ty - try_));
    int32_t px1 = static_cast<int32_t>(std::ceil(tx + trx));
    int32_t py1 = static_cast<int32_t>(std::ceil(ty + try_));
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    if (paint.IsSolid()) {
        const int32_t maskX = static_cast<int32_t>(std::floor(tx - trx));
        const int32_t maskY = static_cast<int32_t>(std::floor(ty - try_));
        const int32_t maskWidth =
            static_cast<int32_t>(std::ceil(tx + trx)) - maskX;
        const int32_t maskHeight =
            static_cast<int32_t>(std::ceil(ty + try_)) - maskY;
        try {
            if (!ellipseFillMaskCache_)
                ellipseFillMaskCache_ = std::make_unique<SoftwareEllipseMaskCache>();
            const float localCenterX = tx - static_cast<float>(maskX);
            const float localCenterY = ty - static_cast<float>(maskY);
            if (!ellipseFillMaskCache_->Matches(
                    maskWidth, maskHeight, localCenterX, localCenterY,
                    trx, try_, 0.0f, 0.0f, false)) {
                ellipseFillMaskCache_->Build(
                    maskWidth, maskHeight, localCenterX, localCenterY,
                    trx, try_, 0.0f, 0.0f, false);
            }
            for (const auto& span : ellipseFillMaskCache_->Spans()) {
                const int32_t destinationY = maskY + span.y;
                if (destinationY < py0 || destinationY >= py1) continue;
                const int32_t destinationX0 = std::max(px0, maskX + span.x);
                const int32_t destinationX1 = std::min(
                    px1, maskX + span.x + span.width);
                if (destinationX1 <= destinationX0) continue;
                const uint8_t alpha = span.hits == 16
                    ? paint.a
                    : static_cast<uint8_t>(
                        static_cast<float>(paint.a) *
                        (static_cast<float>(span.hits) / 16.0f) + 0.5f);
                fb_.BlendSolidSpan(
                    destinationY, destinationX0, destinationX1,
                    paint.r, paint.g, paint.b, alpha);
            }
            return;
        } catch (const std::bad_alloc&) {
            ellipseFillMaskCache_.reset();
            // Fall through to the allocation-free per-row implementation.
        }
    }

    constexpr int kSub = 4;
    constexpr float kStep = 1.0f / static_cast<float>(kSub);
    constexpr float kFirstSample = 0.5f * kStep;
    constexpr float kLastSample = 1.0f - kFirstSample;

    for (int32_t py = py0; py < py1; ++py) {
        float rowLeft[kSub] = {};
        float rowRight[kSub] = {};
        bool rowValid[kSub] = {};
        float maxLeft = -trx;
        float minRight = trx;
        int validSubRows = 0;
        for (int sy = 0; sy < kSub; ++sy) {
            const float localY =
                (static_cast<float>(py) + (sy + 0.5f) * kStep) - ty;
            const float ny = localY / try_;
            if (ny < -1.0f || ny > 1.0f) continue;
            const float halfWidth = trx *
                std::sqrt(std::max(0.0f, 1.0f - ny * ny));
            const float left = -halfWidth;
            const float right = halfWidth;
            rowLeft[sy] = left;
            rowRight[sy] = right;
            rowValid[sy] = true;
            maxLeft = validSubRows == 0 ? left : std::max(maxLeft, left);
            minRight = validSubRows == 0 ? right : std::min(minRight, right);
            ++validSubRows;
        }
        if (validSubRows == 0) continue;

        int32_t coreX0 = std::clamp(
            static_cast<int32_t>(std::ceil(tx + maxLeft - kFirstSample)),
            px0, px1);
        int32_t coreX1 = std::clamp(
            static_cast<int32_t>(std::floor(tx + minRight - kLastSample)) + 1,
            px0, px1);
        const uint8_t coreCoverage = static_cast<uint8_t>(
            (validSubRows * 255 + kSub / 2) / kSub);
        if (coreX1 > coreX0) CompositeSpan(py, coreX0, coreX1, paint, coreCoverage);

        auto compositeEdgePixel = [&](int32_t px) {
            int hits = 0;
            for (int sy = 0; sy < kSub; ++sy) {
                if (!rowValid[sy]) continue;
                for (int sx = 0; sx < kSub; ++sx) {
                    const float localX =
                        (static_cast<float>(px) + (sx + 0.5f) * kStep) - tx;
                    if (localX >= rowLeft[sy] && localX <= rowRight[sy]) ++hits;
                }
            }
            if (hits == 0) return;
            uint8_t r, g, b, a;
            SamplePaint(paint, static_cast<float>(px) + 0.5f,
                        static_cast<float>(py) + 0.5f, r, g, b, a);
            if (hits < kSub * kSub) {
                a = static_cast<uint8_t>(
                    static_cast<float>(a) *
                    (static_cast<float>(hits) / (kSub * kSub)) + 0.5f);
            }
            if (a != 0) fb_.BlendPixelUnchecked(px, py, r, g, b, a);
        };

        if (coreX1 <= coreX0) {
            for (int32_t px = px0; px < px1; ++px) compositeEdgePixel(px);
        } else {
            for (int32_t px = px0; px < coreX0; ++px) compositeEdgePixel(px);
            for (int32_t px = coreX1; px < px1; ++px) compositeEdgePixel(px);
        }
    }
}

void SoftwareRenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth)
{
    if (!brush) return;

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(cx, cy, tx, ty);
    currentTransform_.Apply(cx + rx, cy + ry, tx2, ty2);
    float trx = tx2 - tx, try_ = ty2 - ty;
    if (trx <= 0.0f || try_ <= 0.0f) return;
    float s = std::min(trx / std::max(rx, 0.001f), try_ / std::max(ry, 0.001f));
    float tsw = strokeWidth * s;
    float innerRx = std::max(0.0f, trx - tsw);
    float innerRy = std::max(0.0f, try_ - tsw);

    PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;

    auto insideStroke = [trx, try_, innerRx, innerRy](float lx, float ly) -> bool {
        float exo = lx / trx, eyo = ly / try_;
        if (exo * exo + eyo * eyo > 1.0f) return false;
        if (innerRx > 0.0f && innerRy > 0.0f) {
            float exi = lx / innerRx, eyi = ly / innerRy;
            if (exi * exi + eyi * eyi <= 1.0f) return false;
        }
        return true;
    };

    if (!roundedClipStack_.empty()) {
        RasterizeCoverageAA(tx, ty, -trx, -try_, trx, try_, brush, insideStroke);
        return;
    }

    int32_t px0 = static_cast<int32_t>(std::floor(tx - trx));
    int32_t py0 = static_cast<int32_t>(std::floor(ty - try_));
    int32_t px1 = static_cast<int32_t>(std::ceil(tx + trx));
    int32_t py1 = static_cast<int32_t>(std::ceil(ty + try_));
    if (!TightenToRectClip(px0, py0, px1, py1)) return;

    if (paint.IsSolid()) {
        const int32_t maskX = static_cast<int32_t>(std::floor(tx - trx));
        const int32_t maskY = static_cast<int32_t>(std::floor(ty - try_));
        const int32_t maskWidth =
            static_cast<int32_t>(std::ceil(tx + trx)) - maskX;
        const int32_t maskHeight =
            static_cast<int32_t>(std::ceil(ty + try_)) - maskY;
        try {
            if (!ellipseStrokeMaskCache_)
                ellipseStrokeMaskCache_ = std::make_unique<SoftwareEllipseMaskCache>();
            const float localCenterX = tx - static_cast<float>(maskX);
            const float localCenterY = ty - static_cast<float>(maskY);
            if (!ellipseStrokeMaskCache_->Matches(
                    maskWidth, maskHeight, localCenterX, localCenterY,
                    trx, try_, innerRx, innerRy, true)) {
                ellipseStrokeMaskCache_->Build(
                    maskWidth, maskHeight, localCenterX, localCenterY,
                    trx, try_, innerRx, innerRy, true);
            }
            for (const auto& span : ellipseStrokeMaskCache_->Spans()) {
                const int32_t destinationY = maskY + span.y;
                if (destinationY < py0 || destinationY >= py1) continue;
                const int32_t destinationX0 = std::max(px0, maskX + span.x);
                const int32_t destinationX1 = std::min(
                    px1, maskX + span.x + span.width);
                if (destinationX1 <= destinationX0) continue;
                const uint8_t alpha = span.hits == 16
                    ? paint.a
                    : static_cast<uint8_t>(
                        static_cast<float>(paint.a) *
                        (static_cast<float>(span.hits) / 16.0f) + 0.5f);
                fb_.BlendSolidSpan(
                    destinationY, destinationX0, destinationX1,
                    paint.r, paint.g, paint.b, alpha);
            }
            return;
        } catch (const std::bad_alloc&) {
            ellipseStrokeMaskCache_.reset();
        }
    }

    constexpr int kSub = 4;
    constexpr float kStep = 1.0f / static_cast<float>(kSub);

    for (int32_t py = py0; py < py1; ++py) {
        float outerLeft[kSub] = {};
        float outerRight[kSub] = {};
        float innerLeft[kSub] = {};
        float innerRight[kSub] = {};
        bool outerValid[kSub] = {};
        bool innerValid[kSub] = {};
        const bool hasInner = innerRx > 0.0f && innerRy > 0.0f;
        bool everySubRowHasInner = hasInner;
        float maxInnerLeft = -innerRx;
        float minInnerRight = innerRx;
        for (int sy = 0; sy < kSub; ++sy) {
            const float localY =
                (static_cast<float>(py) + (sy + 0.5f) * kStep) - ty;
            const float outerNy = localY / try_;
            if (outerNy >= -1.0f && outerNy <= 1.0f) {
                const float halfWidth = trx *
                    std::sqrt(std::max(0.0f, 1.0f - outerNy * outerNy));
                outerLeft[sy] = -halfWidth;
                outerRight[sy] = halfWidth;
                outerValid[sy] = true;
            }

            if (!hasInner) continue;
            const float innerNy = localY / innerRy;
            if (innerNy < -1.0f || innerNy > 1.0f) {
                everySubRowHasInner = false;
                continue;
            }
            const float innerHalfWidth = innerRx *
                std::sqrt(std::max(0.0f, 1.0f - innerNy * innerNy));
            innerLeft[sy] = -innerHalfWidth;
            innerRight[sy] = innerHalfWidth;
            innerValid[sy] = true;
            maxInnerLeft = sy == 0
                ? innerLeft[sy] : std::max(maxInnerLeft, innerLeft[sy]);
            minInnerRight = sy == 0
                ? innerRight[sy] : std::min(minInnerRight, innerRight[sy]);
        }

        int32_t leftEnd = px1;
        int32_t rightStart = px1;
        if (everySubRowHasInner && minInnerRight > maxInnerLeft) {
            leftEnd = std::clamp(
                static_cast<int32_t>(std::ceil(tx + maxInnerLeft)) + 1,
                px0, px1);
            rightStart = std::clamp(
                static_cast<int32_t>(std::floor(tx + minInnerRight)) - 1,
                px0, px1);
        }

        auto compositeRange = [&](int32_t begin, int32_t end) {
            int32_t solidRunStart = begin;
            int solidRunHits = -1;
            auto flushSolidRun = [&](int32_t runEnd) {
                if (solidRunHits <= 0 || runEnd <= solidRunStart) return;
                const uint8_t alpha = solidRunHits == kSub * kSub
                    ? paint.a
                    : static_cast<uint8_t>(
                        static_cast<float>(paint.a) *
                        (static_cast<float>(solidRunHits) / (kSub * kSub)) + 0.5f);
                fb_.BlendSolidSpan(
                    py, solidRunStart, runEnd, paint.r, paint.g, paint.b, alpha);
            };

            for (int32_t px = begin; px < end; ++px) {
                int hits = 0;
                for (int sy = 0; sy < kSub; ++sy) {
                    if (!outerValid[sy]) continue;
                    for (int sx = 0; sx < kSub; ++sx) {
                        const float localX =
                            (static_cast<float>(px) + (sx + 0.5f) * kStep) - tx;
                        const bool insideOuter =
                            localX >= outerLeft[sy] && localX <= outerRight[sy];
                        const bool insideInner = innerValid[sy] &&
                            localX >= innerLeft[sy] && localX <= innerRight[sy];
                        if (insideOuter && !insideInner) ++hits;
                    }
                }
                if (paint.IsSolid()) {
                    if (hits != solidRunHits) {
                        flushSolidRun(px);
                        solidRunStart = px;
                        solidRunHits = hits;
                    }
                    continue;
                }
                if (hits == 0) continue;
                uint8_t r, g, b, a;
                SamplePaint(paint, static_cast<float>(px) + 0.5f,
                            static_cast<float>(py) + 0.5f, r, g, b, a);
                if (hits < kSub * kSub) {
                    a = static_cast<uint8_t>(
                        static_cast<float>(a) *
                        (static_cast<float>(hits) / (kSub * kSub)) + 0.5f);
                }
                if (a != 0) fb_.BlendPixelUnchecked(px, py, r, g, b, a);
            }
            if (paint.IsSolid()) flushSolidRun(end);
        };

        if (!everySubRowHasInner || rightStart <= leftEnd) {
            compositeRange(px0, px1);
        } else {
            compositeRange(px0, leftEnd);
            compositeRange(rightStart, px1);
        }
    }
}

void SoftwareRenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    uint8_t r, g, b, a;
    GetBrushColor(brush, (x1 + x2) * 0.5f, (y1 + y2) * 0.5f, r, g, b, a);
    DrawBresenhamLine(x1, y1, x2, y2, r, g, b, a, strokeWidth);
}

void SoftwareRenderTarget::FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule)
{
    if (!brush || pointCount < 3) return;

    // Transform all points first
    std::vector<float> tpts(pointCount * 2);
    for (uint32_t i = 0; i < pointCount; i++) {
        currentTransform_.Apply(points[i * 2], points[i * 2 + 1], tpts[i * 2], tpts[i * 2 + 1]);
    }

    // Compute bounding box from transformed points
    float minX = tpts[0], maxX = tpts[0];
    float minY = tpts[1], maxY = tpts[1];
    for (uint32_t i = 1; i < pointCount; i++) {
        minX = std::min(minX, tpts[i * 2]);
        maxX = std::max(maxX, tpts[i * 2]);
        minY = std::min(minY, tpts[i * 2 + 1]);
        maxY = std::max(maxY, tpts[i * 2 + 1]);
    }

    // Float bounding box. Span endpoints and scanline extent are kept in float
    // (no (int32_t) truncation), so an animated / rotated / DPI-scaled polygon's
    // fill boundary tracks its true sub-pixel position instead of stepping 1px.
    // 4 vertical sub-scanlines + analytic horizontal coverage give edge AA that
    // matches the rest of the backend and the GPU feathered fills.
    int32_t ix0 = (int32_t)std::floor(minX);
    int32_t ix1 = (int32_t)std::ceil(maxX);
    int32_t iy0 = (int32_t)std::floor(minY);
    int32_t iy1 = (int32_t)std::ceil(maxY);
    if (!TightenToRectClip(ix0, iy0, ix1, iy1)) return;

    const PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;
    const bool hasRoundedClip = !roundedClipStack_.empty();

    const bool useWinding = (fillRule == 1);
    constexpr int kSub = 4;
    constexpr float kInv = 1.0f / kSub;
    const int32_t rowW = ix1 - ix0;
    std::vector<float> cov(static_cast<size_t>(rowW));

    // Accumulate analytic horizontal coverage for one filled span [xL,xR],
    // weighted by the per-sub-scanline weight.
    auto accumulateSpan = [&](float xL, float xR) {
        if (xR <= xL) return;
        int32_t cx0 = std::max(ix0, (int32_t)std::floor(xL));
        int32_t cx1 = std::min(ix1, (int32_t)std::ceil(xR));
        for (int32_t px = cx0; px < cx1; px++) {
            float c = std::min((float)px + 1.0f, xR) - std::max((float)px, xL);
            if (c <= 0.0f) continue;
            if (c > 1.0f) c = 1.0f;
            cov[static_cast<size_t>(px - ix0)] += c * kInv;
        }
    };

    std::vector<float> intersections;
    std::vector<std::pair<float, int>> crossings;

    for (int32_t row = iy0; row < iy1; row++) {
        std::fill(cov.begin(), cov.end(), 0.0f);

        for (int k = 0; k < kSub; k++) {
            float sy = (float)row + (k + 0.5f) * kInv;

            if (useWinding) {
                crossings.clear();
                for (uint32_t i = 0; i < pointCount; i++) {
                    uint32_t j = (i + 1) % pointCount;
                    float y0 = tpts[i * 2 + 1], y1 = tpts[j * 2 + 1];
                    float x0 = tpts[i * 2], x1 = tpts[j * 2];
                    if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy)) {
                        float t = (sy - y0) / (y1 - y0);
                        crossings.push_back({ x0 + t * (x1 - x0), (y1 > y0) ? 1 : -1 });
                    }
                }
                std::sort(crossings.begin(), crossings.end(),
                    [](const auto& a, const auto& b) { return a.first < b.first; });
                int winding = 0;
                float spanStart = 0.0f;
                for (size_t i = 0; i < crossings.size(); i++) {
                    int prev = winding;
                    winding += crossings[i].second;
                    if (prev == 0 && winding != 0) spanStart = crossings[i].first;
                    else if (prev != 0 && winding == 0) accumulateSpan(spanStart, crossings[i].first);
                }
            } else {
                intersections.clear();
                for (uint32_t i = 0; i < pointCount; i++) {
                    uint32_t j = (i + 1) % pointCount;
                    float y0 = tpts[i * 2 + 1], y1 = tpts[j * 2 + 1];
                    float x0 = tpts[i * 2], x1 = tpts[j * 2];
                    if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy)) {
                        float t = (sy - y0) / (y1 - y0);
                        intersections.push_back(x0 + t * (x1 - x0));
                    }
                }
                std::sort(intersections.begin(), intersections.end());
                for (size_t i = 0; i + 1 < intersections.size(); i += 2) {
                    accumulateSpan(intersections[i], intersections[i + 1]);
                }
            }
        }

        for (int32_t px = ix0; px < ix1;) {
            float c = cov[static_cast<size_t>(px - ix0)];
            if (c <= 0.0f) { ++px; continue; }
            if (hasRoundedClip &&
                IsClipped((float)px + 0.5f, (float)row + 0.5f)) {
                ++px;
                continue;
            }

            if (!hasRoundedClip && c >= 0.99999f) {
                int32_t runEnd = px + 1;
                while (runEnd < ix1 &&
                       cov[static_cast<size_t>(runEnd - ix0)] >= 0.99999f) {
                    ++runEnd;
                }
                CompositeSpan(row, px, runEnd, paint);
                px = runEnd;
                continue;
            }

            uint8_t r, g, b, a;
            SamplePaint(paint, (float)px + 0.5f, (float)row + 0.5f, r, g, b, a);
            if (c < 1.0f) a = (uint8_t)((float)a * c + 0.5f);
            if (a != 0) fb_.BlendPixelUnchecked(px, row, r, g, b, a);
            ++px;
        }
    }
}

void SoftwareRenderTarget::DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit)
{
    if (!brush || pointCount < 2) return;
    uint8_t r, g, b, a;
    GetBrushColor(brush, points[0], points[1], r, g, b, a);

    for (uint32_t i = 0; i + 1 < pointCount; i++) {
        DrawBresenhamLine(points[i * 2], points[i * 2 + 1],
                         points[(i + 1) * 2], points[(i + 1) * 2 + 1],
                         r, g, b, a, strokeWidth);
    }
    if (closed && pointCount > 2) {
        DrawBresenhamLine(points[(pointCount - 1) * 2], points[(pointCount - 1) * 2 + 1],
                         points[0], points[1], r, g, b, a, strokeWidth);
    }
}

// Helper: parse path commands into a list of sub-path contours.
// Each contour is {points[], closed}.
struct SubPath {
    std::vector<float> points;
    bool closed = false;
};

static void ParsePathToSubPaths(float startX, float startY,
    const float* commands, uint32_t commandLength,
    std::vector<SubPath>& subPaths)
{
    const float tolerance = 0.25f;
    subPaths.clear();

    SubPath current;
    float subPathStartX = startX, subPathStartY = startY;
    current.points.push_back(startX);
    current.points.push_back(startY);

    uint32_t i = 0;
    while (i < commandLength) {
        int tag = (int)commands[i];
        if (tag == 0 && i + 2 < commandLength) {
            // LineTo
            current.points.push_back(commands[i + 1]);
            current.points.push_back(commands[i + 2]);
            i += 3;
        } else if (tag == 1 && i + 6 < commandLength) {
            // CubicBezierTo
            float px = current.points[current.points.size() - 2];
            float py = current.points[current.points.size() - 1];
            SoftwareRenderTarget::FlattenCubicBezier(current.points, px, py,
                commands[i + 1], commands[i + 2],
                commands[i + 3], commands[i + 4],
                commands[i + 5], commands[i + 6], tolerance);
            i += 7;
        } else if (tag == 2 && i + 2 < commandLength) {
            // MoveTo: finish current sub-path, start new one
            if (current.points.size() >= 4) {
                subPaths.push_back(std::move(current));
            }
            current = SubPath{};
            subPathStartX = commands[i + 1];
            subPathStartY = commands[i + 2];
            current.points.push_back(subPathStartX);
            current.points.push_back(subPathStartY);
            i += 3;
        } else if (tag == 3 && i + 4 < commandLength) {
            // QuadBezierTo
            float px = current.points[current.points.size() - 2];
            float py = current.points[current.points.size() - 1];
            SoftwareRenderTarget::FlattenQuadBezier(current.points, px, py,
                commands[i + 1], commands[i + 2],
                commands[i + 3], commands[i + 4], tolerance);
            i += 5;
        } else if (tag == 5) {
            // ClosePath: close current sub-path
            current.closed = true;
            // Add closing segment back to sub-path start if not already there
            float lastX = current.points[current.points.size() - 2];
            float lastY = current.points[current.points.size() - 1];
            if (std::abs(lastX - subPathStartX) > 0.01f || std::abs(lastY - subPathStartY) > 0.01f) {
                current.points.push_back(subPathStartX);
                current.points.push_back(subPathStartY);
            }
            subPaths.push_back(std::move(current));
            current = SubPath{};
            // Next commands continue from the sub-path start
            current.points.push_back(subPathStartX);
            current.points.push_back(subPathStartY);
            i += 1;
        } else {
            break;
        }
    }

    // Push remaining sub-path
    if (current.points.size() >= 4) {
        subPaths.push_back(std::move(current));
    }
}

void SoftwareRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode)
{
    if (!brush || (!commands && commandLength > 0)) return;
    if (edgeMode < 0) edgeMode = 2;  // Default = Antialiased.

    // Aliased branch: keep the legacy binary scanline (preserves the pixel-art
    // look that some apps may rely on). Plumbed through edgeMode == 1 only.
    if (edgeMode == 1) {
        FillPathAliased(startX, startY, commands, commandLength, brush, fillRule);
        return;
    }

    FillRule rule = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
    const SoftwarePathRasterCache::Entry* cachedRaster = nullptr;
    try {
        if (!pathRasterCache_)
            pathRasterCache_ = std::make_unique<SoftwarePathRasterCache>();
        cachedRaster = pathRasterCache_->Find(
            startX, startY, commands, commandLength,
            currentTransform_, fillRule);
    } catch (const std::bad_alloc&) {
        pathRasterCache_.reset();
    }

    std::vector<PixelRect> uncachedRects;
    if (!cachedRaster) {
        // Flatten and transform only on a cache miss. Gallery graphs and icons
        // redraw identical paths for hundreds of frames; caching the final RLE
        // coverage avoids repeating both curve flattening and scan conversion.
        std::vector<Contour> contours = FlattenPathToContours(
            startX, startY, commands, commandLength, 0.5f);
        if (contours.empty()) return;

        for (auto& contour : contours) {
            for (size_t index = 0; index + 1 < contour.points.size(); index += 2) {
                float transformedX = 0.0f;
                float transformedY = 0.0f;
                currentTransform_.Apply(
                    contour.points[index], contour.points[index + 1],
                    transformedX, transformedY);
                contour.points[index] = transformedX;
                contour.points[index + 1] = transformedY;
            }
        }

        uncachedRects.reserve(256);
        RasterizePathToRects(contours, rule, uncachedRects);
        if (uncachedRects.empty()) return;

        if (pathRasterCache_) {
            try {
                cachedRaster = pathRasterCache_->Store(
                    startX, startY, commands, commandLength,
                    currentTransform_, fillRule, uncachedRects);
            } catch (const std::bad_alloc&) {
                pathRasterCache_.reset();
            }
        }
    }

    const std::vector<PixelRect>& rects = cachedRaster
        ? cachedRaster->rects
        : uncachedRects;

    const PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;
    const bool hasRoundedClip = !roundedClipStack_.empty();

    for (const auto& rect : rects) {
        if (rect.w <= 0 || rect.h <= 0) continue;
        int32_t x0 = std::max(0, rect.x);
        int32_t y0 = std::max(0, rect.y);
        int32_t x1 = std::min(width_,  rect.x + rect.w);
        int32_t y1 = std::min(height_, rect.y + rect.h);
        if (!fullInvalidation_ && hasDirtyRect_) {
            x0 = std::max(x0, dirtyLeft_);
            y0 = std::max(y0, dirtyTop_);
            x1 = std::min(x1, dirtyRight_);
            y1 = std::min(y1, dirtyBottom_);
        }
        if (!clipStack_.empty()) {
            const auto& clip = clipStack_.top();
            // Path coverage is historically sampled at integer pixel
            // coordinates, so retain that exact half-open clip convention.
            x0 = std::max(x0, static_cast<int32_t>(std::ceil(clip.x)));
            y0 = std::max(y0, static_cast<int32_t>(std::ceil(clip.y)));
            x1 = std::min(x1, static_cast<int32_t>(std::ceil(clip.x + clip.w)));
            y1 = std::min(y1, static_cast<int32_t>(std::ceil(clip.y + clip.h)));
        }
        if (x1 <= x0 || y1 <= y0) continue;

        if (!hasRoundedClip) {
            if (paint.IsSolid()) {
                const uint8_t alpha = static_cast<uint8_t>(
                    std::lround(static_cast<float>(paint.a) * rect.alpha));
                for (int32_t y = y0; y < y1; ++y) {
                    fb_.BlendSolidSpan(y, x0, x1, paint.r, paint.g, paint.b, alpha);
                }
            } else {
                for (int32_t y = y0; y < y1; ++y) {
                    for (int32_t x = x0; x < x1; ++x) {
                        uint8_t r, g, b, a;
                        SamplePaint(paint, static_cast<float>(x), static_cast<float>(y),
                                    r, g, b, a);
                        const uint8_t aa = static_cast<uint8_t>(
                            std::lround(static_cast<float>(a) * rect.alpha));
                        fb_.BlendPixelUnchecked(x, y, r, g, b, aa);
                    }
                }
            }
            continue;
        }

        for (int32_t y = y0; y < y1; ++y) {
            for (int32_t x = x0; x < x1; ++x) {
                if (IsClipped((float)x, (float)y)) continue;
                uint8_t r, g, b, a;
                SamplePaint(paint, (float)x, (float)y, r, g, b, a);
                uint8_t aa = (uint8_t)std::lround(a * rect.alpha);
                fb_.BlendPixelUnchecked(x, y, r, g, b, aa);
            }
        }
    }
}

// Legacy binary-coverage scanline fill kept around for EdgeMode.Aliased.
void SoftwareRenderTarget::FillPathAliased(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule)
{
    if (!brush) return;

    // Parse into sub-paths (contours)
    std::vector<SubPath> subPaths;
    ParsePathToSubPaths(startX, startY, commands, commandLength, subPaths);
    if (subPaths.empty()) return;

    float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
    struct Edge { float x0, y0, x1, y1; };
    std::vector<Edge> allEdges;

    for (auto& sp : subPaths) {
        uint32_t pc = (uint32_t)(sp.points.size() / 2);
        if (pc < 2) continue;

        std::vector<float> tpts(sp.points.size());
        for (uint32_t j = 0; j < pc; j++) {
            currentTransform_.Apply(sp.points[j * 2], sp.points[j * 2 + 1],
                tpts[j * 2], tpts[j * 2 + 1]);
            minX = std::min(minX, tpts[j * 2]);
            maxX = std::max(maxX, tpts[j * 2]);
            minY = std::min(minY, tpts[j * 2 + 1]);
            maxY = std::max(maxY, tpts[j * 2 + 1]);
        }

        for (uint32_t j = 0; j < pc; j++) {
            uint32_t k = (j + 1) % pc;
            allEdges.push_back({tpts[j * 2], tpts[j * 2 + 1],
                                tpts[k * 2], tpts[k * 2 + 1]});
        }
    }

    if (allEdges.empty()) return;

    int32_t iy0 = std::max(0, (int32_t)minY);
    int32_t iy1 = std::min(height_, (int32_t)(maxY + 1));
    bool useWinding = (fillRule == 1);

    for (int32_t scanY = iy0; scanY < iy1; scanY++) {
        float sy = (float)scanY + 0.5f;

        if (useWinding) {
            std::vector<std::pair<float, int>> crossings;
            for (auto& e : allEdges) {
                if ((e.y0 <= sy && e.y1 > sy) || (e.y1 <= sy && e.y0 > sy)) {
                    float t = (sy - e.y0) / (e.y1 - e.y0);
                    float ix = e.x0 + t * (e.x1 - e.x0);
                    int dir = (e.y1 > e.y0) ? 1 : -1;
                    crossings.push_back({ix, dir});
                }
            }
            std::sort(crossings.begin(), crossings.end(),
                [](const auto& a, const auto& b) { return a.first < b.first; });

            int winding = 0;
            for (size_t ci = 0; ci < crossings.size(); ci++) {
                int prevW = winding;
                winding += crossings[ci].second;
                if (prevW == 0 && winding != 0) {
                    // Start of filled span
                } else if (prevW != 0 && winding == 0) {
                    float spanStart = crossings[ci].first;
                    int w2 = 0;
                    for (size_t k = 0; k <= ci; k++) {
                        int prev2 = w2;
                        w2 += crossings[k].second;
                        if (prev2 == 0 && w2 != 0) spanStart = crossings[k].first;
                    }
                    int32_t xStart = std::max(0, (int32_t)spanStart);
                    int32_t xEnd = std::min(width_ - 1, (int32_t)crossings[ci].first);
                    for (int32_t x = xStart; x <= xEnd; x++) {
                        if (!clipStack_.empty() && IsClipped((float)x, (float)scanY)) continue;
                        uint8_t r, g, b, a;
                        GetBrushColor(brush, (float)x, (float)scanY, r, g, b, a);
                        fb_.BlendPixel(x, scanY, r, g, b, a);
                    }
                }
            }
        } else {
            std::vector<float> intersections;
            for (auto& e : allEdges) {
                if ((e.y0 <= sy && e.y1 > sy) || (e.y1 <= sy && e.y0 > sy)) {
                    float t = (sy - e.y0) / (e.y1 - e.y0);
                    intersections.push_back(e.x0 + t * (e.x1 - e.x0));
                }
            }
            std::sort(intersections.begin(), intersections.end());
            for (size_t ci = 0; ci + 1 < intersections.size(); ci += 2) {
                int32_t xStart = std::max(0, (int32_t)intersections[ci]);
                int32_t xEnd = std::min(width_ - 1, (int32_t)intersections[ci + 1]);
                for (int32_t x = xStart; x <= xEnd; x++) {
                    if (!clipStack_.empty() && IsClipped((float)x, (float)scanY)) continue;
                    uint8_t r, g, b, a;
                    GetBrushColor(brush, (float)x, (float)scanY, r, g, b, a);
                    fb_.BlendPixel(x, scanY, r, g, b, a);
                }
            }
        }
    }
}

void SoftwareRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset, int32_t edgeMode)
{
    if (!brush || (!commands && commandLength > 0)) return;
    if (edgeMode < 0) edgeMode = 2;  // Default = Antialiased.

    // Aliased branch: keep the legacy outline-polygon / Bresenham path.
    if (edgeMode == 1) {
        StrokePathAliased(startX, startY, commands, commandLength, brush, strokeWidth, closed, lineJoin, miterLimit, lineCap, dashPattern, dashCount, dashOffset);
        return;
    }

    const PreparedPaint paint = PreparePaint(brush);
    if (!paint.IsValid()) return;
    const bool hasRoundedClip = !roundedClipStack_.empty();
    auto compositeRects = [&](const std::vector<PixelRect>& rects) {
        for (const auto& rect : rects) {
            if (rect.w <= 0 || rect.h <= 0) continue;
            int32_t x0 = std::max(0, rect.x);
            int32_t y0 = std::max(0, rect.y);
            int32_t x1 = std::min(width_, rect.x + rect.w);
            int32_t y1 = std::min(height_, rect.y + rect.h);
            if (!fullInvalidation_ && hasDirtyRect_) {
                x0 = std::max(x0, dirtyLeft_);
                y0 = std::max(y0, dirtyTop_);
                x1 = std::min(x1, dirtyRight_);
                y1 = std::min(y1, dirtyBottom_);
            }
            if (!clipStack_.empty()) {
                const auto& clip = clipStack_.top();
                x0 = std::max(x0, static_cast<int32_t>(std::ceil(clip.x)));
                y0 = std::max(y0, static_cast<int32_t>(std::ceil(clip.y)));
                x1 = std::min(x1, static_cast<int32_t>(std::ceil(clip.x + clip.w)));
                y1 = std::min(y1, static_cast<int32_t>(std::ceil(clip.y + clip.h)));
            }
            if (x1 <= x0 || y1 <= y0) continue;

            if (!hasRoundedClip) {
                if (paint.IsSolid()) {
                    const uint8_t alpha = static_cast<uint8_t>(
                        std::lround(static_cast<float>(paint.a) * rect.alpha));
                    for (int32_t y = y0; y < y1; ++y) {
                        fb_.BlendSolidSpan(y, x0, x1,
                            paint.r, paint.g, paint.b, alpha);
                    }
                } else {
                    for (int32_t y = y0; y < y1; ++y) {
                        for (int32_t x = x0; x < x1; ++x) {
                            uint8_t red, green, blue, alpha;
                            SamplePaint(paint, static_cast<float>(x),
                                static_cast<float>(y), red, green, blue, alpha);
                            const uint8_t coveredAlpha = static_cast<uint8_t>(
                                std::lround(static_cast<float>(alpha) * rect.alpha));
                            fb_.BlendPixelUnchecked(
                                x, y, red, green, blue, coveredAlpha);
                        }
                    }
                }
                continue;
            }

            for (int32_t y = y0; y < y1; ++y) {
                for (int32_t x = x0; x < x1; ++x) {
                    if (IsClipped(static_cast<float>(x), static_cast<float>(y))) continue;
                    uint8_t red, green, blue, alpha;
                    SamplePaint(paint, static_cast<float>(x), static_cast<float>(y),
                        red, green, blue, alpha);
                    const uint8_t coveredAlpha = static_cast<uint8_t>(
                        std::lround(static_cast<float>(alpha) * rect.alpha));
                    fb_.BlendPixelUnchecked(
                        x, y, red, green, blue, coveredAlpha);
                }
            }
        }
    };

    const uint32_t keyDashCount = dashPattern ? dashCount : 0u;
    try {
        if (!pathRasterCache_)
            pathRasterCache_ = std::make_unique<SoftwarePathRasterCache>();
        if (const auto* cached = pathRasterCache_->FindStroke(
                startX, startY, commands, commandLength, currentTransform_,
                strokeWidth, closed, lineJoin, miterLimit, lineCap,
                dashPattern, keyDashCount, dashOffset)) {
            compositeRects(cached->rects);
            return;
        }
    } catch (const std::bad_alloc&) {
        pathRasterCache_.reset();
    }

    // Antialiased branch: flatten → ExpandStrokePath collect-mode → analytic AA.
    // Source-space flatten produces contours we then transform into device
    // space before stroke widening, matching the pixel-space pipeline that
    // the GPU backends use.
    std::vector<Contour> contours = FlattenPathToContours(
        startX, startY, commands, commandLength, 0.5f);
    if (contours.empty()) return;

    // Approximate device-space stroke width: use the row-norm of the
    // transform matrix's linear part. SoftwareTransform stores the 3x2
    // matrix as a flat array (m[0..3] are the 2x2 linear part, m[4..5]
    // are tx/ty), so probe by transforming the unit basis vectors.
    float ax = 0.0f, ay = 0.0f;
    float bx = 0.0f, by = 0.0f;
    currentTransform_.Apply(1.0f, 0.0f, ax, ay);
    currentTransform_.Apply(0.0f, 1.0f, bx, by);
    float tx0 = 0.0f, ty0 = 0.0f;
    currentTransform_.Apply(0.0f, 0.0f, tx0, ty0);
    float ex = ax - tx0, ey = ay - ty0;   // image of (1,0)
    float fx = bx - tx0, fy = by - ty0;   // image of (0,1)
    float sxLen = std::sqrt(ex * ex + ey * ey);
    float syLen = std::sqrt(fx * fx + fy * fy);
    float maxScale = std::max(sxLen, syLen);
    float pxStrokeWidth = strokeWidth * maxScale;
    if (pxStrokeWidth <= 0.0f) return;

    auto join = static_cast<ImpellerJoin>(lineJoin);
    auto cap  = static_cast<ImpellerCap>(lineCap);
    // Dash patterns and the analytic stroke widener live entirely in
    // jalium.native.core; we just hand the contours over and collect the
    // expanded stroke shape, then rasterize it with the same scanline pass
    // used by fill.
    std::vector<Contour> strokeContours;
    strokeContours.reserve(contours.size() * 8);

    // ExpandStrokePath in collect-mode uses a templated vertex type only
    // for the binary-mesh output, which we don't consume here — pass a
    // throw-away vertex/index buffer pair to satisfy the API.
    struct ScratchVertex { float x, y, r, g, b, a; };
    std::vector<ScratchVertex> scratchVerts;
    std::vector<uint32_t>       scratchIndices;

    auto transformPointsToDevice = [&](const float* source, uint32_t pointCount,
                                       std::vector<float>& destination) {
        destination.resize(static_cast<size_t>(pointCount) * 2u);
        for (uint32_t i = 0; i < pointCount; ++i) {
            currentTransform_.Apply(source[i * 2], source[i * 2 + 1],
                destination[i * 2], destination[i * 2 + 1]);
        }
    };

    if (dashPattern && dashCount > 0) {
        // Dash lengths are expressed in source/DIP units (the managed caller
        // has already multiplied DashStyle values by the pen thickness). Walk
        // before applying the affine transform so anisotropic transforms do
        // not accidentally reinterpret the pattern in device-space units.
        // Duplicate odd patterns: WPF/CSS semantics repeat the list twice so
        // the next cycle starts with the opposite on/off phase.
        std::vector<float> normalizedDash;
        normalizedDash.reserve((dashCount & 1u) ? dashCount * 2u : dashCount);
        for (uint32_t i = 0; i < dashCount; ++i) {
            const float value = std::isfinite(dashPattern[i])
                ? std::max(dashPattern[i], 0.001f) : 0.001f;
            normalizedDash.push_back(value);
        }
        if (dashCount & 1u) {
            for (uint32_t i = 0; i < dashCount; ++i)
                normalizedDash.push_back(normalizedDash[i]);
        }
        const float normalizedOffset = std::isfinite(dashOffset) ? dashOffset : 0.0f;

        for (const auto& c : contours) {
            if (c.VertexCount() < 2) continue;
            jalium::WalkDashPattern(
                c.points.data(), c.VertexCount(),
                normalizedDash.data(), static_cast<uint32_t>(normalizedDash.size()),
                normalizedOffset,
                [&](const float* subPoints, uint32_t subPointCount, bool, bool) {
                    if (subPointCount < 2) return;
                    std::vector<float> devicePoints;
                    transformPointsToDevice(subPoints, subPointCount, devicePoints);
                    jalium::ExpandStrokePath<ScratchVertex>(
                        scratchVerts, scratchIndices,
                        devicePoints.data(), subPointCount,
                        pxStrokeWidth, join, miterLimit, cap, false,
                        0.0f, 0.0f, 0.0f, 1.0f,
                        &strokeContours);
                });
        }
    } else {
        for (const auto& c : contours) {
            if (c.VertexCount() < 2) continue;
            std::vector<float> devicePoints;
            transformPointsToDevice(c.points.data(), c.VertexCount(), devicePoints);
            jalium::ExpandStrokePath<ScratchVertex>(
                scratchVerts, scratchIndices,
                devicePoints.data(), c.VertexCount(),
                pxStrokeWidth, join, miterLimit, cap, closed,
                0.0f, 0.0f, 0.0f, 1.0f,  // colour ignored in collect-mode
                &strokeContours);
        }
    }

    if (strokeContours.empty()) return;

    std::vector<PixelRect> rects;
    rects.reserve(256);
    RasterizePathToRects(strokeContours, FillRule::NonZero, rects);
    if (rects.empty()) return;

    if (pathRasterCache_) {
        try {
            pathRasterCache_->StoreStroke(
                startX, startY, commands, commandLength, currentTransform_,
                strokeWidth, closed, lineJoin, miterLimit, lineCap,
                dashPattern, keyDashCount, dashOffset, rects);
        } catch (const std::bad_alloc&) {
            pathRasterCache_.reset();
        }
    }
    compositeRects(rects);
}

// Legacy outline-polygon stroke kept for EdgeMode.Aliased.
void SoftwareRenderTarget::StrokePathAliased(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset)
{
    if (!brush) return;

    std::vector<SubPath> subPaths;
    ParsePathToSubPaths(startX, startY, commands, commandLength, subPaths);
    if (subPaths.empty()) return;

    for (auto& sp : subPaths) {
        uint32_t ptCount = (uint32_t)(sp.points.size() / 2);
        if (ptCount < 2) continue;

        bool subClosed = sp.closed || closed;

        if (subClosed && ptCount >= 3) {
            float fx = sp.points[0], fy = sp.points[1];
            float lx = sp.points[(ptCount - 1) * 2], ly = sp.points[(ptCount - 1) * 2 + 1];
            if (std::abs(fx - lx) < 0.01f && std::abs(fy - ly) < 0.01f) {
                ptCount--;
            }
        }
        if (ptCount < 2) continue;

        if (strokeWidth > 2.0f && !dashPattern) {
            std::vector<std::vector<float>> contours;
            GenerateStrokeOutline(sp.points, ptCount, strokeWidth, subClosed, lineJoin, miterLimit, lineCap, contours);
            FillMultiContour(contours, brush);
        } else if (dashPattern && dashCount > 0) {
            std::vector<std::vector<float>> dashSegments;
            ApplyDashPattern(sp.points, ptCount, dashPattern, dashCount, dashOffset, dashSegments);
            for (auto& seg : dashSegments) {
                uint32_t segPts = (uint32_t)(seg.size() / 2);
                if (segPts < 2) continue;
                DrawPolygon(seg.data(), segPts, brush, strokeWidth, false, lineJoin, miterLimit);
            }
        } else {
            DrawPolygon(sp.points.data(), ptCount, brush, strokeWidth, subClosed, lineJoin, miterLimit);
        }
    }
}

void SoftwareRenderTarget::DrawContentBorder(float x, float y, float w, float h,
    float blRadius, float brRadius,
    Brush* fillBrush, Brush* strokeBrush, float strokeWidth)
{
    // Fill with bottom-rounded corners
    if (fillBrush) {
        // Top portion (no rounding)
        FillScanlineRect(x, y, w, h - std::max(blRadius, brRadius), fillBrush);
        // Bottom portion with rounded corners
        FillRoundedRectangle(x, y + h - std::max(blRadius, brRadius) * 2,
                            w, std::max(blRadius, brRadius) * 2,
                            std::max(blRadius, brRadius), std::max(blRadius, brRadius),
                            fillBrush);
    }

    // Stroke U-shape (left + bottom + right)
    if (strokeBrush) {
        uint8_t r, g, b, a;
        GetBrushColor(strokeBrush, x, y, r, g, b, a);
        // Left edge
        DrawBresenhamLine(x, y, x, y + h, r, g, b, a, strokeWidth);
        // Bottom edge
        DrawBresenhamLine(x, y + h, x + w, y + h, r, g, b, a, strokeWidth);
        // Right edge
        DrawBresenhamLine(x + w, y, x + w, y + h, r, g, b, a, strokeWidth);
    }
}

void* SoftwareRenderTarget::RealizeLayerBegin(
    void* existingLayer, float x, float y, float w, float h)
{
    if (w <= 0.0f || h <= 0.0f || !effectCaptureStack_.empty()) return nullptr;

    constexpr float kEpsilon = 0.0001f;
    if (std::abs(currentTransform_.m[1]) > kEpsilon ||
        std::abs(currentTransform_.m[2]) > kEpsilon ||
        std::abs(currentTransform_.m[0]) <= kEpsilon ||
        std::abs(currentTransform_.m[3]) <= kEpsilon) {
        return nullptr;
    }

    float x0f, y0f, x1f, y1f;
    currentTransform_.Apply(x, y, x0f, y0f);
    currentTransform_.Apply(x + w, y + h, x1f, y1f);
    const int32_t x0 = static_cast<int32_t>(std::floor(std::min(x0f, x1f)));
    const int32_t y0 = static_cast<int32_t>(std::floor(std::min(y0f, y1f)));
    const int32_t x1 = static_cast<int32_t>(std::ceil(std::max(x0f, x1f)));
    const int32_t y1 = static_cast<int32_t>(std::ceil(std::max(y0f, y1f)));
    if (x0 < 0 || y0 < 0 || x1 > width_ || y1 > height_ || x1 <= x0 || y1 <= y0) {
        return nullptr;
    }

    SoftwareRetainedLayer* retained = nullptr;
    for (const auto& candidate : retainedLayers_) {
        if (candidate.get() == existingLayer) {
            retained = candidate.get();
            break;
        }
    }

    constexpr size_t kRetainedMemoryBudget = 128u * 1024u * 1024u;
    const size_t layerBytes = static_cast<size_t>(x1 - x0) *
        static_cast<size_t>(y1 - y0) * 4u;
    const size_t surfaceBytes = static_cast<size_t>(width_) *
        static_cast<size_t>(height_) * 4u;
    const size_t captureDepth = retainedCaptureStack_.size();
    const size_t reusableCapacity =
        captureDepth < retainedCaptureBufferPool_.size()
        ? retainedCaptureBufferPool_[captureDepth].pixels.capacity()
        : 0u;
    size_t plannedIsolatedCapacity = reusableCapacity;
    if (surfaceBytes > plannedIsolatedCapacity) {
        plannedIsolatedCapacity = ComputeFramebufferGrowthCapacity(
            reusableCapacity, surfaceBytes);
        if (surfaceBytes <= kRetainedCaptureBufferCacheBudget) {
            plannedIsolatedCapacity = std::min(
                plannedIsolatedCapacity, kRetainedCaptureBufferCacheBudget);
        }
    }
    const size_t pooledBytes = RetainedCaptureBufferPoolBytes();
    const size_t pooledBytesAfterAcquire = pooledBytes >= reusableCapacity
        ? pooledBytes - reusableCapacity
        : 0u;

    // The old layer remains valid until capture successfully completes. Count
    // it, the replacement pixels, active isolated canvases, the next canvas and
    // idle depth-indexed canvases against the hard 128 MiB transient+cache
    // budget. The root saved framebuffer is the main target and remains outside
    // this retained-layer budget, matching the previous policy.
    size_t retainedBudgetBytes = 0;
    auto tryAddRetainedBytes = [&](size_t bytes) {
        if (bytes > kRetainedMemoryBudget - retainedBudgetBytes)
            return false;
        retainedBudgetBytes += bytes;
        return true;
    };
    if (!tryAddRetainedBytes(retainedLayerBytes_) ||
        !tryAddRetainedBytes(layerBytes) ||
        !tryAddRetainedBytes(pooledBytesAfterAcquire)) {
        return nullptr;
    }
    for (const auto& layer : retainedLayers_) {
        if (layer && layer->bitmap &&
            !tryAddRetainedBytes(layer->bitmap->ScaledCacheBytes())) {
            return nullptr;
        }
    }
    if (captureDepth > 0u &&
        !tryAddRetainedBytes(fb_.pixels.capacity())) {
        return nullptr;
    }
    for (size_t depth = 1u; depth < captureDepth; ++depth) {
        if (!tryAddRetainedBytes(
                retainedCaptureStack_[depth].savedFramebuffer.pixels.capacity())) {
            return nullptr;
        }
    }
    if (!tryAddRetainedBytes(plannedIsolatedCapacity))
        return nullptr;

    RetainedCaptureState state;
    try {
        state.capturedPixels.resize(layerBytes);
        retainedCaptureStack_.reserve(captureDepth + 1u);
    } catch (const std::bad_alloc&) {
        return nullptr;
    }

    SoftwareFramebuffer isolated;
    const bool cacheIsolated = captureDepth < kRetainedCaptureBufferCacheDepth;
    if (cacheIsolated) {
        try {
            if (retainedCaptureBufferPool_.size() <= captureDepth)
                retainedCaptureBufferPool_.resize(captureDepth + 1u);
        } catch (const std::bad_alloc&) {
            return nullptr;
        }
        isolated = std::move(retainedCaptureBufferPool_[captureDepth]);
    }

    try {
        if (surfaceBytes > isolated.pixels.capacity()) {
            MaybeFailFramebufferAllocationForTesting();
            isolated.pixels.reserve(plannedIsolatedCapacity);
        }
        isolated.Resize(width_, height_);
        std::fill(isolated.pixels.begin(), isolated.pixels.end(), 0u);
    } catch (const std::bad_alloc&) {
        if (cacheIsolated)
            CacheRetainedCaptureBuffer(captureDepth, std::move(isolated));
        return nullptr;
    }

    if (!retained) {
        try {
            auto created = std::make_unique<SoftwareRetainedLayer>();
            retained = created.get();
            retainedLayers_.push_back(std::move(created));
        } catch (const std::bad_alloc&) {
            if (cacheIsolated)
                CacheRetainedCaptureBuffer(captureDepth, std::move(isolated));
            return nullptr;
        }
    }

    state.layer = retained;
    state.x = x0;
    state.y = y0;
    state.width = x1 - x0;
    state.height = y1 - y0;
    state.savedFramebuffer = std::move(fb_);
    state.savedClips = std::move(clipStack_);
    state.savedRoundedClips = std::move(roundedClipStack_);
    clipStack_ = {};
    roundedClipStack_.clear();
    fb_ = std::move(isolated);
    retainedCaptureStack_.push_back(std::move(state));
    return retained;
}

void SoftwareRenderTarget::RealizeLayerEnd(void* layer)
{
    if (retainedCaptureStack_.empty()) return;

    const size_t captureDepth = retainedCaptureStack_.size() - 1u;
    RetainedCaptureState state = std::move(retainedCaptureStack_.back());
    retainedCaptureStack_.pop_back();
    SoftwareFramebuffer captured = std::move(fb_);
    fb_ = std::move(state.savedFramebuffer);
    clipStack_ = std::move(state.savedClips);
    roundedClipStack_ = std::move(state.savedRoundedClips);

    if (state.layer != layer || !state.layer ||
        captured.width != width_ || captured.height != height_) {
        CacheRetainedCaptureBuffer(captureDepth, std::move(captured));
        return;
    }

    const size_t rowBytes = static_cast<size_t>(state.width) * 4u;
    for (int32_t row = 0; row < state.height; ++row) {
        const uint8_t* source = captured.pixels.data() +
            (static_cast<size_t>(state.y + row) * captured.width + state.x) * 4u;
        uint8_t* destination = state.capturedPixels.data() +
            static_cast<size_t>(row) * rowBytes;
        std::memcpy(destination, source, rowBytes);
    }

    const size_t oldBytes = state.layer->ByteSize();
    try {
        auto bitmap = std::make_unique<SoftwareBitmap>(
            static_cast<uint32_t>(state.width),
            static_cast<uint32_t>(state.height),
            std::move(state.capturedPixels),
            false,
            24u * 1024u * 1024u,
            false);
        state.layer->bitmap = std::move(bitmap);
        retainedLayerBytes_ = retainedLayerBytes_ - oldBytes + state.layer->ByteSize();
    } catch (const std::bad_alloc&) {
        // Keep the previous snapshot valid; managed will retry realization on
        // the next dirty frame if this capture could not be committed.
    }
    CacheRetainedCaptureBuffer(captureDepth, std::move(captured));
}

void SoftwareRenderTarget::CompositeLayer(
    void* layer, float x, float y, float w, float h, float opacity)
{
    if (!layer || w <= 0.0f || h <= 0.0f || opacity <= 0.0f) return;
    SoftwareRetainedLayer* retained = nullptr;
    for (const auto& candidate : retainedLayers_) {
        if (candidate.get() == layer) {
            retained = candidate.get();
            break;
        }
    }
    if (!retained || !retained->bitmap) return;
    DrawBitmap(retained->bitmap.get(), x, y, w, h, opacity);

    // Transformed snapshots are useful for fractional animation phases, but
    // multiple retained layers must not each grow an independent cache toward
    // the global 128 MiB ceiling. Prefer the layer just composited and shed
    // older layers' transformed variants once their aggregate exceeds 32 MiB.
    constexpr size_t kRetainedScaledCacheBudget = 32u * 1024u * 1024u;
    size_t scaledBytes = 0;
    for (const auto& candidate : retainedLayers_) {
        if (candidate && candidate->bitmap)
            scaledBytes += candidate->bitmap->ScaledCacheBytes();
    }
    if (scaledBytes > kRetainedScaledCacheBudget) {
        for (const auto& candidate : retainedLayers_) {
            if (!candidate || candidate.get() == retained || !candidate->bitmap) continue;
            candidate->bitmap->ClearScaledCache();
        }
        scaledBytes = retained->bitmap->ScaledCacheBytes();
        if (scaledBytes > kRetainedScaledCacheBudget)
            retained->bitmap->ClearScaledCache();
    }
}

void SoftwareRenderTarget::DestroyRetainedLayer(void* layer)
{
    if (!layer) return;
    for (const auto& capture : retainedCaptureStack_) {
        if (capture.layer == layer) return;
    }
    for (auto iterator = retainedLayers_.begin();
         iterator != retainedLayers_.end(); ++iterator) {
        if (iterator->get() != layer) continue;
        retainedLayerBytes_ -= (*iterator)->ByteSize();
        retainedLayers_.erase(iterator);
        return;
    }
}

void SoftwareRenderTarget::RenderText(
    const wchar_t* text, uint32_t textLength,
    TextFormat* format,
    float x, float y, float w, float h,
    Brush* brush)
{
    if (!text || textLength == 0 || !format || !brush) return;

    auto* solid = dynamic_cast<SoftwareSolidBrush*>(brush);
    if (!solid) return;

#ifdef JALIUM_HAS_TEXT_ENGINE
    // Path 1: FreeType glyph atlas rendering (preferred on all platforms)
    auto* ftFormat = dynamic_cast<JaliumTextFormat*>(format);
    if (ftFormat && backend_ && backend_->GetTextEngine()) {
        RenderTextWithGlyphAtlas(text, textLength, ftFormat, x, y, w, h, solid);
        return;
    }
#endif

#ifdef _WIN32
    // Path 2: GDI rendering (Windows fallback when TextEngine unavailable)
    auto* stf = dynamic_cast<SoftwareTextFormat*>(format);
    if (stf) {
        RenderTextWithGDI(text, textLength, stf, x, y, w, h, solid);
        return;
    }
#endif

    // Path 3: Placeholder (last resort)
    RenderTextPlaceholder(text, textLength, format, x, y, w, h, solid);
}

// ============================================================================
// Text Rendering Path 1: FreeType Glyph Atlas (cross-platform)
// ============================================================================

#ifdef JALIUM_HAS_TEXT_ENGINE
void SoftwareRenderTarget::RenderTextWithGlyphAtlas(
    const wchar_t* text, uint32_t textLength,
    JaliumTextFormat* ftFormat,
    float x, float y, float w, float h,
    SoftwareSolidBrush* brush)
{
    // Transform origin to screen coordinates
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    // Extract text color
    uint8_t textR = FloatToU8(brush->r);
    uint8_t textG = FloatToU8(brush->g);
    uint8_t textB = FloatToU8(brush->b);
    float textAlpha = brush->a * currentOpacity_;

    // Decompose the active matrix. Managed DrawText cancels a plain uniform
    // scale through the inverse-transform path (the matrix here is identity and
    // glyphs arrive pre-scaled), but the deformation-preserving path and any
    // rotation/skew hand the LIVE matrix down — the glyph run must be laid out
    // in local space, rasterized at the matrix's resolution and mapped through
    // it, or scaled/rotated text renders at 1x upright (the designer-zoom bug).
    const float m0 = currentTransform_.m[0], m1 = currentTransform_.m[1];
    const float m2 = currentTransform_.m[2], m3 = currentTransform_.m[3];
    const bool matrixIsIdentity =
        std::fabs(m0 - 1.0f) <= 1e-4f && std::fabs(m3 - 1.0f) <= 1e-4f &&
        std::fabs(m1) <= 1e-4f && std::fabs(m2) <= 1e-4f;

    // Rasterization scale comes from the matrix basis vectors, NOT from the
    // DPI members: the root DPI scale already travels inside the transform
    // stack, so multiplying scaleY_ on top of managed's pre-scaled font size
    // would double-scale (identity matrix ⇒ scale 1, bit-compatible with the
    // old behaviour on 100% DPI).
    const float scaleXAxis = std::sqrt(m0 * m0 + m1 * m1);
    const float scaleYAxis = std::sqrt(m2 * m2 + m3 * m3);
    float textRenderScale = std::max(scaleXAxis, scaleYAxis);
    if (!std::isfinite(textRenderScale) || textRenderScale <= 1e-6f) return;

    std::vector<TextGlyphQuad> quads;
    if (matrixIsIdentity) {
        // Fast path: quads are generated directly in screen space.
        ftFormat->GenerateGlyphQuads(
            text, textLength, w, h,
            brush->r, brush->g, brush->b, textAlpha,
            tx, ty, quads, 1.0f);
    } else {
        // Layout in local space at the matrix's physical resolution; each quad
        // is then mapped through the residual matrix R = M / renderScale below.
        ftFormat->GenerateGlyphQuads(
            text, textLength, w, h,
            brush->r, brush->g, brush->b, textAlpha,
            0.0f, 0.0f, quads, textRenderScale);
    }

    if (quads.empty()) return;

    // Residual matrix mapping physical-resolution local quads onto the screen.
    // For a uniform scale-only matrix this is ~identity and the axis-aligned
    // blit below stays valid; rotation/skew/anisotropy take the resampling path.
    const float invRenderScale = 1.0f / textRenderScale;
    const float r00 = m0 * invRenderScale, r01 = m1 * invRenderScale;
    const float r10 = m2 * invRenderScale, r11 = m3 * invRenderScale;
    const bool residualIsAxisAligned =
        std::fabs(r00 - 1.0f) <= 2e-3f && std::fabs(r11 - 1.0f) <= 2e-3f &&
        std::fabs(r01) <= 2e-3f && std::fabs(r10) <= 2e-3f;

    if (!matrixIsIdentity && !residualIsAxisAligned) {
        RenderTransformedGlyphQuads(quads, tx, ty, r00, r01, r10, r11,
                                    textR, textG, textB, textAlpha);
        return;
    }

    if (!matrixIsIdentity) {
        // Uniform scale: rebase each local-space quad onto the transformed
        // origin so the fast axis-aligned blit below applies unchanged.
        for (auto& quad : quads) {
            quad.posX += tx;
            quad.posY += ty;
        }
    }

    // Get glyph atlas pixel data
    GlyphAtlas* atlas = backend_->GetTextEngine()->GetGlyphAtlas();
    const uint8_t* atlasData = atlas->GetPixelData();
    uint32_t atlasW = atlas->GetWidth();
    uint32_t atlasH = atlas->GetHeight();

    // Blit each glyph quad from atlas onto framebuffer.
    // Horizontal sub-pixel position is already baked into the atlas raster (8
    // sub-pixel buckets per pixel via subpixelX), so X blits at an integer
    // column. The vertical axis has no such bucket, so each source row's
    // coverage is distributed across the two straddling destination rows by the
    // fractional part of posY. This keeps vertically-animated text moving
    // smoothly instead of snapping a whole pixel per frame; when posY is integer
    // (fracY == 0) the upper-row weight is zero and the blit is identical to the
    // previous single-row path, so static text is unchanged.
    for (const auto& quad : quads) {
        int32_t dstX = static_cast<int32_t>(std::floor(quad.posX));
        int32_t dstY = static_cast<int32_t>(std::floor(quad.posY));
        float fracY = quad.posY - static_cast<float>(dstY);
        float wLo = 1.0f - fracY;
        int32_t qw = static_cast<int32_t>(std::ceil(quad.sizeX));
        int32_t qh = static_cast<int32_t>(std::ceil(quad.sizeY));

        // Atlas source coordinates from UV
        int32_t srcX = static_cast<int32_t>(quad.uvMinX * atlasW);
        int32_t srcY = static_cast<int32_t>(quad.uvMinY * atlasH);

        // Early rejection: quad completely outside framebuffer (+1 row for the
        // vertical sub-pixel spread).
        if (dstX + qw <= 0 || dstX >= width_ || dstY + qh + 1 <= 0 || dstY >= height_)
            continue;

        for (int32_t row = 0; row < qh; ++row) {
            int32_t sy = srcY + row;
            if (sy < 0 || sy >= static_cast<int32_t>(atlasH))
                continue;

            for (int32_t col = 0; col < qw; ++col) {
                int32_t dx = dstX + col;
                int32_t sx = srcX + col;
                if (dx < 0 || dx >= width_ || sx < 0 || sx >= static_cast<int32_t>(atlasW))
                    continue;

                // Atlas is RGBA. Masks use RGB channel coverage + max coverage
                // in A; authored color glyphs contain premultiplied RGBA.
                size_t atlasIdx = (static_cast<size_t>(sy) * atlasW + sx) * 4;
                uint8_t coverage = atlasData[atlasIdx + 3];
                if (coverage == 0) continue;

                const bool colorGlyph = (quad.flags & ATLAS_GLYPH_COLOR) != 0;
                const bool lcdGlyph = (quad.flags & ATLAS_GLYPH_LCD) != 0;

                auto blendSample = [&](int32_t destinationY, float verticalWeight) {
                    if (verticalWeight <= 0.0f || destinationY < 0 || destinationY >= height_ ||
                        IsClipped(dx + 0.5f, destinationY + 0.5f)) return;
                    if (colorGlyph) {
                        const uint8_t sourceA = atlasData[atlasIdx + 3];
                        if (sourceA == 0) return;
                        const uint8_t sourceR = static_cast<uint8_t>(std::min(
                            255u, static_cast<unsigned>(atlasData[atlasIdx]) * 255u / sourceA));
                        const uint8_t sourceG = static_cast<uint8_t>(std::min(
                            255u, static_cast<unsigned>(atlasData[atlasIdx + 1]) * 255u / sourceA));
                        const uint8_t sourceB = static_cast<uint8_t>(std::min(
                            255u, static_cast<unsigned>(atlasData[atlasIdx + 2]) * 255u / sourceA));
                        const uint8_t alpha = static_cast<uint8_t>(
                            std::clamp(textAlpha * verticalWeight * sourceA + 0.5f, 0.0f, 255.0f));
                        fb_.BlendPixel(dx, destinationY, sourceR, sourceG, sourceB, alpha);
                    } else if (lcdGlyph) {
                        const auto channelCoverage = [&](int channel) {
                            return static_cast<uint8_t>(std::clamp(
                                textAlpha * verticalWeight * atlasData[atlasIdx + channel] + 0.5f,
                                0.0f, 255.0f));
                        };
                        fb_.BlendPixelSubpixel(dx, destinationY, textR, textG, textB,
                                               channelCoverage(0), channelCoverage(1), channelCoverage(2));
                    } else {
                        const uint8_t alpha = static_cast<uint8_t>(std::clamp(
                            textAlpha * verticalWeight * coverage + 0.5f, 0.0f, 255.0f));
                        fb_.BlendPixel(dx, destinationY, textR, textG, textB, alpha);
                    }
                };

                int32_t dyLo = dstY + row;
                blendSample(dyLo, wLo);
                int32_t dyHi = dyLo + 1;
                blendSample(dyHi, fracY);
            }
        }
    }
}

void SoftwareRenderTarget::RenderTransformedGlyphQuads(
    const std::vector<TextGlyphQuad>& quads,
    float originX, float originY,
    float r00, float r01, float r10, float r11,
    uint8_t textR, uint8_t textG, uint8_t textB, float textAlpha)
{
    float det = r00 * r11 - r01 * r10;
    if (std::fabs(det) < 1e-9f) return;
    float invDet = 1.0f / det;
    float i00 = r11 * invDet, i01 = -r01 * invDet;
    float i10 = -r10 * invDet, i11 = r00 * invDet;

    GlyphAtlas* atlas = backend_->GetTextEngine()->GetGlyphAtlas();
    const uint8_t* atlasData = atlas->GetPixelData();
    int32_t atlasW = static_cast<int32_t>(atlas->GetWidth());
    int32_t atlasH = static_cast<int32_t>(atlas->GetHeight());
    if (!atlasData || atlasW <= 0 || atlasH <= 0) return;

    for (const auto& quad : quads) {
        float sizeX = quad.sizeX, sizeY = quad.sizeY;
        if (sizeX <= 0 || sizeY <= 0) continue;
        int32_t qw = static_cast<int32_t>(std::ceil(sizeX));
        int32_t qh = static_cast<int32_t>(std::ceil(sizeY));
        int32_t srcX = static_cast<int32_t>(quad.uvMinX * atlasW);
        int32_t srcY = static_cast<int32_t>(quad.uvMinY * atlasH);

        // Screen-space parallelogram: P(s,t) = base + R·(s,t).
        float baseX = originX + quad.posX * r00 + quad.posY * r10;
        float baseY = originY + quad.posX * r01 + quad.posY * r11;
        float ex0 = sizeX * r00, ey0 = sizeX * r01;   // R·(sizeX, 0)
        float ex1 = sizeY * r10, ey1 = sizeY * r11;   // R·(0, sizeY)

        float minX = baseX, maxX = baseX, minY = baseY, maxY = baseY;
        minX = std::min({minX, baseX + ex0, baseX + ex1, baseX + ex0 + ex1});
        maxX = std::max({maxX, baseX + ex0, baseX + ex1, baseX + ex0 + ex1});
        minY = std::min({minY, baseY + ey0, baseY + ey1, baseY + ey0 + ey1});
        maxY = std::max({maxY, baseY + ey0, baseY + ey1, baseY + ey0 + ey1});

        int32_t px0 = std::max(0, static_cast<int32_t>(std::floor(minX)) - 1);
        int32_t py0 = std::max(0, static_cast<int32_t>(std::floor(minY)) - 1);
        int32_t px1 = std::min(width_ - 1, static_cast<int32_t>(std::ceil(maxX)) + 1);
        int32_t py1 = std::min(height_ - 1, static_cast<int32_t>(std::ceil(maxY)) + 1);
        if (px0 > px1 || py0 > py1) continue;

        const bool colorGlyph = (quad.flags & ATLAS_GLYPH_COLOR) != 0;

        // Bilinear sample of the glyph's atlas rect at local coordinates
        // (s, t) ∈ [0, sizeX] × [0, sizeY]; clamped to the quad so neighbouring
        // atlas entries never bleed in.
        auto sampleGlyph = [&](float s, float t, float out[4]) {
            float ax = s - 0.5f;
            float ay = t - 0.5f;
            int32_t x0 = static_cast<int32_t>(std::floor(ax));
            int32_t y0 = static_cast<int32_t>(std::floor(ay));
            float fx = ax - x0;
            float fy = ay - y0;
            int32_t cx0 = std::clamp(x0, 0, qw - 1);
            int32_t cx1 = std::clamp(x0 + 1, 0, qw - 1);
            int32_t cy0 = std::clamp(y0, 0, qh - 1);
            int32_t cy1 = std::clamp(y0 + 1, 0, qh - 1);
            auto texel = [&](int32_t lx, int32_t ly, float weight) {
                int32_t sx = srcX + lx, sy = srcY + ly;
                if (sx < 0 || sx >= atlasW || sy < 0 || sy >= atlasH) return;
                size_t idx = (static_cast<size_t>(sy) * atlasW + sx) * 4;
                out[0] += atlasData[idx + 0] * weight;
                out[1] += atlasData[idx + 1] * weight;
                out[2] += atlasData[idx + 2] * weight;
                out[3] += atlasData[idx + 3] * weight;
            };
            out[0] = out[1] = out[2] = out[3] = 0;
            texel(cx0, cy0, (1 - fx) * (1 - fy));
            texel(cx1, cy0, fx * (1 - fy));
            texel(cx0, cy1, (1 - fx) * fy);
            texel(cx1, cy1, fx * fy);
        };

        for (int32_t py = py0; py <= py1; ++py) {
            for (int32_t px = px0; px <= px1; ++px) {
                float cxp = px + 0.5f;
                float cyp = py + 0.5f;
                float dx = cxp - baseX;
                float dy = cyp - baseY;
                float s = dx * i00 + dy * i10;
                float t = dx * i01 + dy * i11;
                if (s < -0.5f || s > sizeX + 0.5f || t < -0.5f || t > sizeY + 0.5f)
                    continue;
                if (IsClipped(cxp, cyp)) continue;

                float sample[4];
                sampleGlyph(s, t, sample);
                float coverage = sample[3];
                if (coverage <= 0.5f) continue;

                if (colorGlyph) {
                    // Atlas stores premultiplied RGBA; interpolate premultiplied,
                    // then un-premultiply for the straight-alpha BlendPixel.
                    float sa = coverage / 255.0f;
                    float inv = 1.0f / sa;
                    uint8_t cr = static_cast<uint8_t>(std::clamp(sample[0] * inv, 0.0f, 255.0f));
                    uint8_t cg = static_cast<uint8_t>(std::clamp(sample[1] * inv, 0.0f, 255.0f));
                    uint8_t cb = static_cast<uint8_t>(std::clamp(sample[2] * inv, 0.0f, 255.0f));
                    uint8_t alpha = static_cast<uint8_t>(
                        std::clamp(textAlpha * coverage + 0.5f, 0.0f, 255.0f));
                    fb_.BlendPixel(px, py, cr, cg, cb, alpha);
                } else {
                    // LCD stripes cannot survive an arbitrary transform, so both
                    // mask and LCD glyphs blend with max-channel (alpha) coverage.
                    uint8_t alpha = static_cast<uint8_t>(
                        std::clamp(textAlpha * coverage + 0.5f, 0.0f, 255.0f));
                    fb_.BlendPixel(px, py, textR, textG, textB, alpha);
                }
            }
        }
    }
}
#endif

// ============================================================================
// Text Rendering Path 2: GDI (Windows fallback)
// ============================================================================

#ifdef _WIN32
void SoftwareRenderTarget::RenderTextWithGDI(
    const wchar_t* text, uint32_t textLength,
    SoftwareTextFormat* stf,
    float x, float y, float w, float h,
    SoftwareSolidBrush* brush)
{
    uint8_t r = FloatToU8(brush->r);
    uint8_t g = FloatToU8(brush->g);
    uint8_t b = FloatToU8(brush->b);
    uint8_t a = FloatToU8(brush->a * currentOpacity_);

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    if (!cachedTextDC_) {
        cachedTextDC_ = CreateCompatibleDC(nullptr);
    }
    HDC hdc = static_cast<HDC>(cachedTextDC_);
    if (!hdc) return;

    // (w, h) arrive in DIPs while (tx, ty) is already in physical pixels. The
    // physical scale is decomposed from the LIVE matrix, not from the DPI
    // members: the root DPI transform pushed in BeginDraw travels inside the
    // matrix, and so does any user scale (designer zoom, a ScaleTransform, the
    // deformation-preserving text path) — sizing the DIB and the em height by
    // DPI alone rendered such text at 1x, overflowing its scaled container.
    // With only the DPI root transform active the decomposition equals
    // scaleX_/scaleY_ and this is byte-identical to the previous behaviour.
    // The 16384 clamp only guards the "unbounded" 10000-DIP layout fallback
    // from exploding the DIB allocation at high scale factors.
    const float m0 = currentTransform_.m[0], m1 = currentTransform_.m[1];
    const float m2 = currentTransform_.m[2], m3 = currentTransform_.m[3];
    float axisScaleY = std::sqrt(m2 * m2 + m3 * m3);
    if (!std::isfinite(axisScaleY) || axisScaleY <= 1e-6f) return;
    const float r00 = m0 / axisScaleY, r01 = m1 / axisScaleY;
    const float r10 = m2 / axisScaleY, r11 = m3 / axisScaleY;
    const bool residualIsAxisAligned =
        std::fabs(r00 - 1.0f) <= 2e-3f && std::fabs(r11 - 1.0f) <= 2e-3f &&
        std::fabs(r01) <= 2e-3f && std::fabs(r10) <= 2e-3f;

    // The DIB is rasterized uniformly at the Y-axis scale (em size and layout
    // box together, so wrap positions stay consistent); any X-axis stretch or
    // rotation is left in the residual matrix R = M / axisScaleY and applied by
    // the inverse-mapping blit below — matching the GPU backends, where an
    // anisotropic matrix visibly stretches the glyphs.
    int32_t pw = std::min((int32_t)std::ceil(w * axisScaleY), 16384);
    int32_t maxPh = std::min((int32_t)std::ceil(h * axisScaleY), 16384);
    if (pw <= 0 || maxPh <= 0) return;

    // Select the font before allocating the DIB so DT_CALCRECT can reduce a
    // tall layout slot (often the entire page viewport) to the rows the text
    // can actually touch. The old pw*ph allocation and scan turned a 16px label
    // in a 1500x920 slot into 1.38M pixel visits per DrawText call.
    int fontHeight = -(std::max)(1, (int)(stf->fontSize * axisScaleY + 0.5f));
    SoftwareTextMaskCache::Key textMaskKey;
    bool cacheTextMask = residualIsAxisAligned;
    if (cacheTextMask) {
        try {
            if (!textMaskCache_)
                textMaskCache_ = std::make_unique<SoftwareTextMaskCache>();
            const SoftwareTextMaskCache::KeyView textMaskView {
                std::wstring_view(text, textLength),
                std::wstring_view(stf->fontFamily),
                pw,
                maxPh,
                fontHeight,
                stf->fontWeight,
                stf->fontStyle,
                stf->alignment,
            };

            if (const auto* cached = textMaskCache_->Find(textMaskView)) {
                if (cached->width <= 0 || cached->height <= 0) return;
                const int32_t ix = static_cast<int32_t>(std::floor(tx));
                const int32_t iy = static_cast<int32_t>(std::floor(ty));
                const float fracX = tx - static_cast<float>(ix);
                const float fracY = ty - static_cast<float>(iy);
                const bool integerPhase =
                    std::abs(fracX) <= 1e-6f && std::abs(fracY) <= 1e-6f;
                auto cachedLum = [&](int32_t dibX, int32_t dibY) -> float {
                    const int32_t localX = dibX - cached->x;
                    const int32_t localY = dibY - cached->y;
                    if (localX < 0 || localX >= cached->width ||
                        localY < 0 || localY >= cached->height) return 0.0f;
                    const uint16_t sum = cached->channelSums[
                        static_cast<size_t>(localY) * cached->width + localX];
                    return static_cast<float>(sum) / 3.0f;
                };

                int32_t destinationX0 = ix + cached->x;
                int32_t destinationY0 = iy + cached->y;
                int32_t destinationX1 = destinationX0 + cached->width +
                    (integerPhase ? 0 : 1);
                int32_t destinationY1 = destinationY0 + cached->height +
                    (integerPhase ? 0 : 1);
                if (!TightenToRectClip(
                        destinationX0, destinationY0,
                        destinationX1, destinationY1)) return;
                const int32_t rowBegin = destinationY0 - iy;
                const int32_t rowEnd = destinationY1 - iy;
                const bool hasRoundedClip = !roundedClipStack_.empty();
                const int32_t compositeWidth = destinationX1 - destinationX0;
                const int32_t compositeHeight = destinationY1 - destinationY0;
                const SoftwareClipRect* activeClip = clipStack_.empty()
                    ? nullptr : &clipStack_.top();
                std::vector<uint8_t> destinationBefore;
                try {
                    if (!textCompositeCache_)
                        textCompositeCache_ =
                            std::make_unique<SoftwareTextCompositeCache>();
                    if (textCompositeCache_->TryApply(
                            cached->cacheId, r, g, b, a, fracX, fracY,
                            destinationX0, destinationY0,
                            compositeWidth, compositeHeight,
                            activeClip, roundedClipStack_, fb_)) {
                        return;
                    }
                    const size_t rowBytes =
                        static_cast<size_t>(compositeWidth) * 4u;
                    destinationBefore.resize(
                        rowBytes * static_cast<size_t>(compositeHeight));
                    for (int32_t compositeRow = 0;
                         compositeRow < compositeHeight; ++compositeRow) {
                        const uint8_t* source = fb_.pixels.data() +
                            (static_cast<size_t>(destinationY0 + compositeRow) *
                             fb_.width + destinationX0) * 4u;
                        std::memcpy(
                            destinationBefore.data() +
                                static_cast<size_t>(compositeRow) * rowBytes,
                            source, rowBytes);
                    }
                } catch (const std::bad_alloc&) {
                    textCompositeCache_.reset();
                    destinationBefore.clear();
                }

                auto storeComposite = [&] {
                    if (!textCompositeCache_ || destinationBefore.empty()) return;
                    try {
                        SoftwareTextCompositeCache::Entry entry;
                        entry.maskId = cached->cacheId;
                        entry.red = r;
                        entry.green = g;
                        entry.blue = b;
                        entry.alpha = a;
                        entry.phaseX = fracX;
                        entry.phaseY = fracY;
                        entry.x = destinationX0;
                        entry.y = destinationY0;
                        entry.width = compositeWidth;
                        entry.height = compositeHeight;
                        entry.hasClip = activeClip != nullptr;
                        if (activeClip) entry.clip = *activeClip;
                        entry.roundedClips = roundedClipStack_;
                        entry.contextPixels = std::move(destinationBefore);
                        const size_t rowBytes =
                            static_cast<size_t>(compositeWidth) * 4u;
                        entry.outputPixels.resize(
                            rowBytes * static_cast<size_t>(compositeHeight));
                        for (int32_t compositeRow = 0;
                             compositeRow < compositeHeight; ++compositeRow) {
                            const uint8_t* source = fb_.pixels.data() +
                                (static_cast<size_t>(destinationY0 + compositeRow) *
                                 fb_.width + destinationX0) * 4u;
                            std::memcpy(
                                entry.outputPixels.data() +
                                    static_cast<size_t>(compositeRow) * rowBytes,
                                source, rowBytes);
                        }
                        textCompositeCache_->Store(std::move(entry));
                    } catch (const std::bad_alloc&) {
                        textCompositeCache_.reset();
                    }
                };

                if (integerPhase) {

                    // Runs cover contiguous non-zero GDI mask pixels. Walk the
                    // sparse list once so static labels spend no time visiting
                    // whitespace inside their layout boxes.
                    for (const auto& run : cached->coverageRuns) {
                        const int32_t destinationY = iy + cached->y + run.y;
                        if (destinationY < destinationY0 ||
                            destinationY >= destinationY1) continue;
                        int32_t runX0 = ix + cached->x + run.x;
                        int32_t runX1 = runX0 + run.width;
                        runX0 = std::max(runX0, destinationX0);
                        runX1 = std::min(runX1, destinationX1);
                        if (hasRoundedClip && !TightenSpanToRoundedClips(
                                destinationY, runX0, runX1)) continue;
                        if (runX1 <= runX0) continue;
                        const int32_t localX = runX0 - (ix + cached->x);
                        const uint16_t* mask = cached->channelSums.data() +
                            static_cast<size_t>(run.y) * cached->width + localX;
                        uint8_t* destination = fb_.pixels.data() +
                            (static_cast<size_t>(destinationY) * fb_.width + runX0) * 4u;
                        for (int32_t destinationX = runX0;
                             destinationX < runX1;
                             ++destinationX, ++mask, destination += 4) {
                            const uint8_t sourceAlpha = TextMaskAlpha(*mask, a);
                            if (sourceAlpha == 255) {
                                destination[0] = b;
                                destination[1] = g;
                                destination[2] = r;
                                destination[3] = 255;
                            } else if (sourceAlpha != 0 && destination[3] == 255) {
                                const uint32_t inverseAlpha = 255u - sourceAlpha;
                                destination[0] = static_cast<uint8_t>(
                                    (static_cast<uint32_t>(b) * sourceAlpha +
                                     static_cast<uint32_t>(destination[0]) * inverseAlpha) /
                                    255u);
                                destination[1] = static_cast<uint8_t>(
                                    (static_cast<uint32_t>(g) * sourceAlpha +
                                     static_cast<uint32_t>(destination[1]) * inverseAlpha) /
                                    255u);
                                destination[2] = static_cast<uint8_t>(
                                    (static_cast<uint32_t>(r) * sourceAlpha +
                                     static_cast<uint32_t>(destination[2]) * inverseAlpha) /
                                    255u);
                            } else if (sourceAlpha != 0) {
                                fb_.BlendPixelUnchecked(
                                    destinationX, destinationY,
                                    r, g, b, sourceAlpha);
                            }
                        }
                    }

                    storeComposite();
                    return;
                }

                for (int32_t row = rowBegin; row < rowEnd; ++row) {
                    const int32_t destinationY = iy + row;
                    int32_t rowDestinationX0 = destinationX0;
                    int32_t rowDestinationX1 = destinationX1;
                    if (hasRoundedClip && !TightenSpanToRoundedClips(
                            destinationY, rowDestinationX0, rowDestinationX1)) continue;
                    const int32_t rowColBegin = rowDestinationX0 - ix;
                    const int32_t rowColEnd = rowDestinationX1 - ix;

                    const float sourceY = static_cast<float>(row) - fracY;
                    const int32_t sourceY0 = static_cast<int32_t>(std::floor(sourceY));
                    const float fy = sourceY - static_cast<float>(sourceY0);
                    for (int32_t col = rowColBegin; col < rowColEnd; ++col) {
                        const int32_t destinationX = ix + col;
                        const float sourceX = static_cast<float>(col) - fracX;
                        const int32_t sourceX0 = static_cast<int32_t>(std::floor(sourceX));
                        const float fx = sourceX - static_cast<float>(sourceX0);
                        const float lum =
                            cachedLum(sourceX0, sourceY0) * (1.0f - fx) * (1.0f - fy) +
                            cachedLum(sourceX0 + 1, sourceY0) * fx * (1.0f - fy) +
                            cachedLum(sourceX0, sourceY0 + 1) * (1.0f - fx) * fy +
                            cachedLum(sourceX0 + 1, sourceY0 + 1) * fx * fy;
                        if (lum <= 0.0f) continue;
                        const uint8_t sourceAlpha = static_cast<uint8_t>(std::clamp(
                            (lum / 255.0f) * a + 0.5f, 0.0f, 255.0f));
                        if (sourceAlpha != 0) {
                            fb_.BlendPixelUnchecked(
                                destinationX, destinationY, r, g, b, sourceAlpha);
                        }
                    }
                }
                storeComposite();
                return;
            }

            // Only cache misses take ownership of strings. Hits above perform
            // heterogeneous lookup directly over the caller's text span.
            textMaskKey.text.assign(text, text + textLength);
            textMaskKey.fontFamily = stf->fontFamily;
            textMaskKey.pixelWidth = pw;
            textMaskKey.pixelHeight = maxPh;
            textMaskKey.fontHeight = fontHeight;
            textMaskKey.fontWeight = stf->fontWeight;
            textMaskKey.fontStyle = stf->fontStyle;
            textMaskKey.alignment = stf->alignment;
        } catch (const std::bad_alloc&) {
            cacheTextMask = false;
            textMaskCache_.reset();
        }
    }
    HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
        stf->fontWeight, (stf->fontStyle == 1 || stf->fontStyle == 2) ? TRUE : FALSE,
        FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
        stf->fontFamily.c_str());
    if (!hFont) return;
    HGDIOBJ oldFont = SelectObject(hdc, hFont);

    SetTextColor(hdc, RGB(255, 255, 255));
    SetBkMode(hdc, TRANSPARENT);
    constexpr UINT baseFlags = DT_WORDBREAK | DT_EXTERNALLEADING | DT_NOPREFIX;
    RECT measured = { 0, 0, (LONG)pw, (LONG)maxPh };
    DrawTextW(hdc, text, textLength, &measured, baseFlags | DT_LEFT | DT_CALCRECT);
    const int32_t measuredWidth = std::clamp<int32_t>(
        measured.right - measured.left, 1, pw);
    const int32_t measuredHeight = std::max<int32_t>(
        measured.bottom - measured.top, 1);
    int32_t ph = std::min(maxPh, measuredHeight + 1);

    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = pw;
    bmi.bmiHeader.biHeight = -ph;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    HBITMAP hbm = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (hbm && bits) {
        HGDIOBJ oldBm = SelectObject(hdc, hbm);
        std::memset(bits, 0, static_cast<size_t>(pw) * ph * 4u);

        // DT_EXTERNALLEADING keeps the painted line advance on the same ruler
        // as MeasureText / GetFontMetrics (tmHeight + tmExternalLeading);
        // DT_NOPREFIX keeps literal '&' characters (mnemonics are a managed
        // AccessText concern, not GDI's).
        RECT rc = { 0, 0, (LONG)pw, (LONG)ph };
        UINT dtFlags = baseFlags;
        switch (stf->alignment) {
            case 1: dtFlags |= DT_RIGHT; break;
            case 2: dtFlags |= DT_CENTER; break;
            default: dtFlags |= DT_LEFT; break;
        }

        DrawTextW(hdc, text, textLength, &rc, dtFlags);

        // Copy rendered text to framebuffer with alpha blending. The DIB is
        // already rasterized at the matrix's per-axis physical resolution, so
        // the residual matrix (unit-length basis vectors) is identity for any
        // axis-aligned transform — including plain scale — and the fast
        // sub-pixel-phase blit below applies. Rotation / skew leave a real
        // residual and take the inverse-mapping resample instead.
        uint8_t* textBits = static_cast<uint8_t*>(bits);
        int32_t bw = pw, bh = ph;
        auto blockLum = [&](int32_t cc, int32_t rr) -> float {
            if (cc < 0 || cc >= bw || rr < 0 || rr >= bh) return 0.0f;
            int srcIdx = (rr * bw + cc) * 4;
            return (textBits[srcIdx + 2] + textBits[srcIdx + 1] + textBits[srcIdx + 0]) / 3.0f;
        };

        if (cacheTextMask && textMaskCache_) {
            SoftwareTextMaskCache::Entry entry;
            int32_t minX = bw, minY = bh, maxX = -1, maxY = -1;
            for (int32_t row = 0; row < bh; ++row) {
                const uint8_t* source = textBits + static_cast<size_t>(row) * bw * 4u;
                for (int32_t col = 0; col < bw; ++col, source += 4) {
                    if (source[0] == 0 && source[1] == 0 && source[2] == 0) continue;
                    minX = std::min(minX, col);
                    minY = std::min(minY, row);
                    maxX = std::max(maxX, col);
                    maxY = std::max(maxY, row);
                }
            }

            if (maxX >= minX && maxY >= minY) {
                entry.x = minX;
                entry.y = minY;
                entry.width = maxX - minX + 1;
                entry.height = maxY - minY + 1;
                try {
                    entry.channelSums.resize(
                        static_cast<size_t>(entry.width) * entry.height);
                    for (int32_t row = 0; row < entry.height; ++row) {
                        for (int32_t col = 0; col < entry.width; ++col) {
                            const size_t sourceIndex =
                                (static_cast<size_t>(entry.y + row) * bw + entry.x + col) * 4u;
                            entry.channelSums[
                                static_cast<size_t>(row) * entry.width + col] =
                                static_cast<uint16_t>(textBits[sourceIndex]) +
                                static_cast<uint16_t>(textBits[sourceIndex + 1]) +
                                static_cast<uint16_t>(textBits[sourceIndex + 2]);
                        }
                    }
                    entry.coverageRuns.reserve(
                        static_cast<size_t>(entry.height) * 8u);
                    for (int32_t row = 0; row < entry.height; ++row) {
                        int32_t runStart = -1;
                        for (int32_t col = 0; col < entry.width; ++col) {
                            const uint16_t sum = entry.channelSums[
                                static_cast<size_t>(row) * entry.width + col];
                            if (sum != 0) {
                                if (runStart < 0) runStart = col;
                            } else if (runStart >= 0) {
                                entry.coverageRuns.push_back({
                                    row, runStart, col - runStart });
                                runStart = -1;
                            }
                        }
                        if (runStart >= 0) {
                            entry.coverageRuns.push_back({
                                row, runStart, entry.width - runStart });
                        }
                    }
                    textMaskCache_->Insert(std::move(textMaskKey), std::move(entry));
                } catch (const std::bad_alloc&) {
                    textMaskCache_.reset();
                    cacheTextMask = false;
                }
            } else {
                try {
                    textMaskCache_->Insert(std::move(textMaskKey), std::move(entry));
                } catch (const std::bad_alloc&) {
                    textMaskCache_.reset();
                    cacheTextMask = false;
                }
            }
        }

        if (residualIsAxisAligned) {
            // The block is sampled with a fractional (bilinear) phase so an
            // animated text origin moves smoothly sub-pixel instead of snapping
            // to a whole pixel. When (tx,ty) are integer (fracX/fracY == 0) the
            // sampling reduces to the original 1:1 copy, so static text is
            // unchanged.
            int32_t ix = static_cast<int32_t>(std::floor(tx));
            int32_t iy = static_cast<int32_t>(std::floor(ty));
            float fracX = tx - static_cast<float>(ix);
            float fracY = ty - static_cast<float>(iy);
            // Iterate only the DIB rows/cols that land inside the framebuffer —
            // the unbounded-layout fallback (w = h = 10000 DIPs) otherwise spins
            // ~10^8 iterations of pure bounds-check misses per DrawText call. The
            // per-pixel guards stay as a defensive backstop; this only trims the
            // loop ranges.
            int32_t rowBegin = (std::max)(0, -iy);
            int32_t rowEnd = (std::min)(bh, height_ - 1 - iy);
            int32_t colBegin = (std::max)(0, -ix);
            int32_t colEnd = (std::min)(bw, width_ - 1 - ix);
            int32_t inkLeft = 0;
            if (stf->alignment == 1) inkLeft = pw - measuredWidth;
            else if (stf->alignment == 2) inkLeft = (pw - measuredWidth) / 2;
            constexpr int32_t kInkOverhang = 3;
            colBegin = std::max(colBegin, inkLeft - kInkOverhang);
            colEnd = std::min(colEnd, inkLeft + measuredWidth + kInkOverhang);
            for (int32_t row = rowBegin; row <= rowEnd; row++) {
                int32_t dyy = iy + row;
                if (dyy < 0 || dyy >= height_) continue;
                float sv = static_cast<float>(row) - fracY;
                int32_t sv0 = static_cast<int32_t>(std::floor(sv));
                float fv = sv - static_cast<float>(sv0);
                for (int32_t col = colBegin; col <= colEnd; col++) {
                    int32_t dxx = ix + col;
                    if (dxx < 0 || dxx >= width_) continue;
                    if (!clipStack_.empty() && IsClipped(dxx + 0.5f, dyy + 0.5f)) continue;
                    float su = static_cast<float>(col) - fracX;
                    int32_t su0 = static_cast<int32_t>(std::floor(su));
                    float fu = su - static_cast<float>(su0);
                    float lum = blockLum(su0, sv0)         * (1.0f - fu) * (1.0f - fv)
                              + blockLum(su0 + 1, sv0)     * fu          * (1.0f - fv)
                              + blockLum(su0, sv0 + 1)     * (1.0f - fu) * fv
                              + blockLum(su0 + 1, sv0 + 1) * fu          * fv;
                    if (lum <= 0.0f) continue;
                    uint8_t sa = static_cast<uint8_t>(std::clamp((lum / 255.0f) * a + 0.5f, 0.0f, 255.0f));
                    if (sa == 0) continue;
                    fb_.BlendPixel(dxx, dyy, r, g, b, sa);
                }
            }
        } else {
            // Rotated / skewed run: the DIB is a coverage mask in local physical
            // space; map its parallelogram onto the screen and inverse-sample.
            float det = r00 * r11 - r01 * r10;
            if (std::fabs(det) > 1e-9f) {
                float invDet = 1.0f / det;
                float i00 = r11 * invDet, i01 = -r01 * invDet;
                float i10 = -r10 * invDet, i11 = r00 * invDet;

                float ex0 = bw * r00, ey0 = bw * r01;   // R·(bw, 0)
                float ex1 = bh * r10, ey1 = bh * r11;   // R·(0, bh)
                float minX = std::min({tx, tx + ex0, tx + ex1, tx + ex0 + ex1});
                float maxX = std::max({tx, tx + ex0, tx + ex1, tx + ex0 + ex1});
                float minY = std::min({ty, ty + ey0, ty + ey1, ty + ey0 + ey1});
                float maxY = std::max({ty, ty + ey0, ty + ey1, ty + ey0 + ey1});

                int32_t px0 = std::max(0, static_cast<int32_t>(std::floor(minX)) - 1);
                int32_t py0 = std::max(0, static_cast<int32_t>(std::floor(minY)) - 1);
                int32_t px1 = std::min(width_ - 1, static_cast<int32_t>(std::ceil(maxX)) + 1);
                int32_t py1 = std::min(height_ - 1, static_cast<int32_t>(std::ceil(maxY)) + 1);

                for (int32_t py = py0; py <= py1; ++py) {
                    for (int32_t px = px0; px <= px1; ++px) {
                        float cxp = px + 0.5f;
                        float cyp = py + 0.5f;
                        float dx = cxp - tx;
                        float dy = cyp - ty;
                        float u = dx * i00 + dy * i10;
                        float v = dx * i01 + dy * i11;
                        if (u < -0.5f || u > bw + 0.5f || v < -0.5f || v > bh + 0.5f)
                            continue;
                        if (!clipStack_.empty() && IsClipped(cxp, cyp)) continue;
                        float su = u - 0.5f;
                        float sv = v - 0.5f;
                        int32_t su0 = static_cast<int32_t>(std::floor(su));
                        int32_t sv0 = static_cast<int32_t>(std::floor(sv));
                        float fu = su - su0;
                        float fv = sv - sv0;
                        float lum = blockLum(su0, sv0)         * (1.0f - fu) * (1.0f - fv)
                                  + blockLum(su0 + 1, sv0)     * fu          * (1.0f - fv)
                                  + blockLum(su0, sv0 + 1)     * (1.0f - fu) * fv
                                  + blockLum(su0 + 1, sv0 + 1) * fu          * fv;
                        if (lum <= 0.0f) continue;
                        uint8_t sa = static_cast<uint8_t>(std::clamp((lum / 255.0f) * a + 0.5f, 0.0f, 255.0f));
                        if (sa == 0) continue;
                        fb_.BlendPixel(px, py, r, g, b, sa);
                    }
                }
            }
        }

        SelectObject(hdc, oldBm);
        DeleteObject(hbm);
    }
    SelectObject(hdc, oldFont);
    DeleteObject(hFont);
}
#endif

// ============================================================================
// Text Rendering Path 3: Placeholder (last resort fallback)
// ============================================================================

void SoftwareRenderTarget::RenderTextPlaceholder(
    const wchar_t* text, uint32_t textLength,
    TextFormat* format, float x, float y, float w, float h,
    SoftwareSolidBrush* brush)
{
    (void)format; (void)h;

    float charWidth = 12.0f * 0.6f; // approximate
    float baseline = 12.0f * 0.8f;
    float tx, ty;
    currentTransform_.Apply(x, y + baseline, tx, ty);

    uint8_t cr = FloatToU8(brush->r);
    uint8_t cg = FloatToU8(brush->g);
    uint8_t cb = FloatToU8(brush->b);
    uint8_t ca = FloatToU8(brush->a * currentOpacity_ * 0.3f);
    float textWidth = std::min(textLength * charWidth, w);

    for (int32_t col = 0; col < (int32_t)textWidth; col++) {
        fb_.BlendPixel((int32_t)tx + col, (int32_t)ty, cr, cg, cb, ca);
    }
}

void SoftwareRenderTarget::PushTransform(const float* matrix)
{
    transformStack_.push(currentTransform_);
    SoftwareTransform t;
    std::memcpy(t.m, matrix, sizeof(float) * 6);
    currentTransform_ = currentTransform_.Multiply(t);
}

void SoftwareRenderTarget::PopTransform()
{
    if (transformStack_.empty()) return;
    currentTransform_ = transformStack_.top();
    transformStack_.pop();
}

void SoftwareRenderTarget::PushClip(float x, float y, float w, float h)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    // Transform the bottom-right corner to get the scaled width/height
    float tx2, ty2;
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;

    SoftwareClipRect clip;
    if (!clipStack_.empty()) {
        // Intersect with current clip
        auto& top = clipStack_.top();
        clip.x = std::max(tx, top.x);
        clip.y = std::max(ty, top.y);
        float right = std::min(tx + tw, top.x + top.w);
        float bottom = std::min(ty + th, top.y + top.h);
        clip.w = std::max(0.0f, right - clip.x);
        clip.h = std::max(0.0f, bottom - clip.y);
    } else {
        clip = {tx, ty, tw, th};
    }
    clipStack_.push(clip);
}

void SoftwareRenderTarget::PopClip()
{
    if (clipStack_.empty()) return;
    if (clipStack_.top().ownsRounded && !roundedClipStack_.empty())
        roundedClipStack_.pop_back();
    clipStack_.pop();
}

void SoftwareRenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry)
{
    // Symmetric variant: all four corners get the smaller of the two radii so
    // the SDF stays circular, then forward to the per-corner path.
    float r = std::min(rx, ry);
    PushPerCornerRoundedRectClip(x, y, w, h, r, r, r, r);
}

void SoftwareRenderTarget::PushPerCornerRoundedRectClip(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    float tx2, ty2;
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;

    // The stack entry carries only the rectangle intersection. Corner rounding
    // must NOT live on the intersected rect: intersecting can move the rect's
    // edges, which would drag the corner circles away from where this level's
    // own corners actually are — and a nested plain clip would drop an
    // ancestor's rounding entirely. Rounded levels are tracked separately in
    // their own untrimmed rectangles (roundedClipStack_) and tested per pixel
    // by IsClipped.
    SoftwareClipRect clip;
    if (!clipStack_.empty()) {
        auto& top = clipStack_.top();
        clip.x = std::max(tx, top.x);
        clip.y = std::max(ty, top.y);
        float right = std::min(tx + tw, top.x + top.w);
        float bottom = std::min(ty + th, top.y + top.h);
        clip.w = std::max(0.0f, right - clip.x);
        clip.h = std::max(0.0f, bottom - clip.y);
    } else {
        clip = {tx, ty, tw, th};
    }

    if (tl > 0 || tr > 0 || br > 0 || bl > 0) {
        // Corner radii scale with the live matrix (the root DPI transform and
        // any user scale both live in it), taking the smaller axis so
        // non-uniform stretch doesn't produce an ellipse-shaped corner.
        const float* m = currentTransform_.m;
        float axisX = std::sqrt(m[0] * m[0] + m[1] * m[1]);
        float axisY = std::sqrt(m[2] * m[2] + m[3] * m[3]);
        float scale = std::min(axisX, axisY);
        if (!std::isfinite(scale) || scale <= 0) scale = 1.0f;

        SoftwareClipRect rounded{tx, ty, tw, th};
        rounded.radiusTL = tl * scale;
        rounded.radiusTR = tr * scale;
        rounded.radiusBR = br * scale;
        rounded.radiusBL = bl * scale;
        rounded.rx = std::max({ rounded.radiusTL, rounded.radiusTR, rounded.radiusBR, rounded.radiusBL });
        rounded.ry = rounded.rx;
        roundedClipStack_.push_back(rounded);
        clip.ownsRounded = true;
    }
    clipStack_.push(clip);
}

void SoftwareRenderTarget::PunchTransparentRect(float x, float y, float w, float h)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    for (int32_t row = iy; row < iy + ih; row++) {
        for (int32_t col = ix; col < ix + iw; col++) {
            fb_.SetPixel(col, row, 0, 0, 0, 0);
        }
    }
}

void SoftwareRenderTarget::PushOpacity(float opacity)
{
    opacityStack_.push(currentOpacity_);
    currentOpacity_ *= opacity;
}

void SoftwareRenderTarget::PopOpacity()
{
    if (opacityStack_.empty()) return;
    currentOpacity_ = opacityStack_.top();
    opacityStack_.pop();
}

void SoftwareRenderTarget::SetShapeType(int /*type*/, float /*n*/) {}

void SoftwareRenderTarget::SetVSyncEnabled(bool enabled)
{
    vsyncEnabled_ = enabled;
}

void SoftwareRenderTarget::SetDpi(float dpiX, float dpiY)
{
    dpiX_ = dpiX;
    dpiY_ = dpiY;
    scaleX_ = dpiX / 96.0f;
    scaleY_ = dpiY / 96.0f;
}

void SoftwareRenderTarget::AddDirtyRect(float x, float y, float w, float h)
{
    if (fullInvalidation_ || !std::isfinite(x) || !std::isfinite(y) ||
        !std::isfinite(w) || !std::isfinite(h) || w <= 0.0f || h <= 0.0f ||
        width_ <= 0 || height_ <= 0)
        return;

    const double scaleX = std::isfinite(scaleX_) && scaleX_ > 0.0f ? scaleX_ : 1.0;
    const double scaleY = std::isfinite(scaleY_) && scaleY_ > 0.0f ? scaleY_ : 1.0;
    const double leftValue = std::clamp(
        std::floor(static_cast<double>(x) * scaleX), 0.0,
        static_cast<double>(width_));
    const double topValue = std::clamp(
        std::floor(static_cast<double>(y) * scaleY), 0.0,
        static_cast<double>(height_));
    const double rightValue = std::clamp(
        std::ceil((static_cast<double>(x) + w) * scaleX), 0.0,
        static_cast<double>(width_));
    const double bottomValue = std::clamp(
        std::ceil((static_cast<double>(y) + h) * scaleY), 0.0,
        static_cast<double>(height_));
    const int32_t left = static_cast<int32_t>(leftValue);
    const int32_t top = static_cast<int32_t>(topValue);
    const int32_t right = static_cast<int32_t>(rightValue);
    const int32_t bottom = static_cast<int32_t>(bottomValue);
    if (left >= right || top >= bottom) return;

    if (!hasDirtyRect_)
    {
        dirtyLeft_ = left;
        dirtyTop_ = top;
        dirtyRight_ = right;
        dirtyBottom_ = bottom;
        hasDirtyRect_ = true;
        return;
    }
    dirtyLeft_ = std::min(dirtyLeft_, left);
    dirtyTop_ = std::min(dirtyTop_, top);
    dirtyRight_ = std::max(dirtyRight_, right);
    dirtyBottom_ = std::max(dirtyBottom_, bottom);
}

void SoftwareRenderTarget::SetFullInvalidation()
{
    fullInvalidation_ = true;
    hasDirtyRect_ = false;
}

void SoftwareRenderTarget::DrawVideoSurface(VideoSurface* surface,
                                            float x, float y, float w, float h,
                                            float opacity, int /*scalingMode*/)
{
    // Software backend treats a video surface as its embedded SoftwareBitmap.
    // The same composite path the still-image DrawBitmap uses already reads
    // pixels_ directly from the surface's vector — no extra copy.
    if (!surface) return;
    auto* sv = dynamic_cast<SoftwareVideoSurface*>(surface);
    if (!sv) return;
    DrawBitmap(&sv->bitmap, x, y, w, h, opacity);
}

void SoftwareRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
    if (!bitmap) return;
    auto* sb = dynamic_cast<SoftwareBitmap*>(bitmap);
    if (!sb || sb->pixels_.empty()) return;

    if (w <= 0.0f || h <= 0.0f) return;

    // Bitmap destinations are expressed in DIPs, just like every other drawing
    // primitive. Transform all four corners so the root DPI scale (and any
    // element transform) applies to the extent as well as the origin. The old
    // path transformed only (x, y), leaving w/h in physical pixels; at Android
    // densities that rendered images at roughly 1 / density of their layout
    // slot while Borders and other transformed primitives remained full-size.
    float quad[8];
    currentTransform_.Apply(x,     y,     quad[0], quad[1]);
    currentTransform_.Apply(x + w, y,     quad[2], quad[3]);
    currentTransform_.Apply(x + w, y + h, quad[4], quad[5]);
    currentTransform_.Apply(x,     y + h, quad[6], quad[7]);

    float left = quad[0], top = quad[1], right = quad[0], bottom = quad[1];
    for (size_t i = 2; i < 8; i += 2) {
        left = std::min(left, quad[i]);
        top = std::min(top, quad[i + 1]);
        right = std::max(right, quad[i]);
        bottom = std::max(bottom, quad[i + 1]);
    }
    if (!std::isfinite(left) || !std::isfinite(top) ||
        !std::isfinite(right) || !std::isfinite(bottom) ||
        right <= left || bottom <= top)
        return;

    // Device pixels are mapped back into the untransformed destination before
    // UV calculation. This preserves image orientation under negative scales
    // and makes rotation/shear correct instead of stretching the AABB itself.
    const float* m = currentTransform_.m;
    const float determinant = m[0] * m[3] - m[1] * m[2];
    if (!std::isfinite(determinant) || std::abs(determinant) < 1.0e-8f)
        return;
    const float invDeterminant = 1.0f / determinant;
    const SoftwareTransform inverse = {{
        m[3] * invDeterminant,
        -m[1] * invDeterminant,
        -m[2] * invDeterminant,
        m[0] * invDeterminant,
        (m[2] * m[5] - m[3] * m[4]) * invDeterminant,
        (m[1] * m[4] - m[0] * m[5]) * invDeterminant
    }};
    int32_t px0 = std::max(0, (int32_t)std::floor(left));
    int32_t py0 = std::max(0, (int32_t)std::floor(top));
    int32_t px1 = std::min(width_, (int32_t)std::ceil(right));
    int32_t py1 = std::min(height_, (int32_t)std::ceil(bottom));

    const int32_t sw = (int32_t)sb->width_;
    const int32_t shh = (int32_t)sb->height_;
    if (sw <= 0 || shh <= 0) return;
    const float invW = 1.0f / w, invH = 1.0f / h;
    const float globalOpacity = opacity * currentOpacity_;
    if (globalOpacity <= 0.0f) return;
    const bool axisAligned = std::abs(m[1]) < 1.0e-6f && std::abs(m[2]) < 1.0e-6f;

    const bool pixelAlignedAxis = axisAligned && m[0] > 0.0f && m[3] > 0.0f &&
        std::abs(left - std::round(left)) < 1.0e-4f &&
        std::abs(top - std::round(top)) < 1.0e-4f;
    std::shared_ptr<const SoftwareScaledBitmap> scaledBitmap;
    const uint8_t* fastPixels = nullptr;
    int32_t fastWidth = 0;
    int32_t fastHeight = 0;
    bool fastOpaque = false;
    bool fastAlphaPrecomposited = false;
    int32_t fastDestinationLeft = 0;
    int32_t fastDestinationTop = 0;
    if (pixelAlignedAxis) {
        const int32_t destinationWidth = static_cast<int32_t>(std::lround(right - left));
        const int32_t destinationHeight = static_cast<int32_t>(std::lround(bottom - top));
        if (destinationWidth == sw && destinationHeight == shh &&
            (globalOpacity >= 0.999999f || sb->dynamic_)) {
            fastPixels = sb->pixels_.data();
            fastWidth = sw;
            fastHeight = shh;
            fastOpaque = sb->opaque_;
            fastDestinationLeft = static_cast<int32_t>(std::round(left));
            fastDestinationTop = static_cast<int32_t>(std::round(top));
        }
    }
    if (!fastPixels && axisAligned && m[0] > 0.0f && m[3] > 0.0f && !sb->dynamic_) {
        const int32_t rasterLeft = static_cast<int32_t>(std::floor(left));
        const int32_t rasterTop = static_cast<int32_t>(std::floor(top));
        const int32_t rasterWidth = static_cast<int32_t>(std::ceil(right)) - rasterLeft;
        const int32_t rasterHeight = static_cast<int32_t>(std::ceil(bottom)) - rasterTop;
        try {
            scaledBitmap = sb->GetOrCreateScaled(
                static_cast<uint32_t>(std::max(rasterWidth, 0)),
                static_cast<uint32_t>(std::max(rasterHeight, 0)),
                right - left,
                bottom - top,
                left - static_cast<float>(rasterLeft),
                top - static_cast<float>(rasterTop),
                globalOpacity);
            if (scaledBitmap) {
                fastPixels = scaledBitmap->pixels.data();
                fastWidth = static_cast<int32_t>(scaledBitmap->width);
                fastHeight = static_cast<int32_t>(scaledBitmap->height);
                fastOpaque = scaledBitmap->opaque;
                fastAlphaPrecomposited = true;
                fastDestinationLeft = rasterLeft;
                fastDestinationTop = rasterTop;
            }
        } catch (const std::bad_alloc&) {
            scaledBitmap.reset();
        }
    }
    if (fastPixels && globalOpacity > 0.0f) {
        int32_t fastX0 = px0, fastY0 = py0, fastX1 = px1, fastY1 = py1;
        if (!TightenToRectClip(fastX0, fastY0, fastX1, fastY1)) return;
        const bool hasRoundedClip = !roundedClipStack_.empty();

        std::vector<uint8_t> bitmapCompositeKey;
        std::vector<uint8_t> destinationBefore;
        const int32_t panelWidth = fastX1 - fastX0;
        const int32_t panelHeight = fastY1 - fastY0;
        if (scaledBitmap && !fastOpaque && panelWidth > 0 && panelHeight > 0) {
            try {
                auto appendValue = [&](const auto& value) {
                    const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
                    bitmapCompositeKey.insert(
                        bitmapCompositeKey.end(), bytes, bytes + sizeof(value));
                };
                constexpr uint32_t kBitmapCompositeTag = 0x424D5043u; // "BMPC"
                appendValue(kBitmapCompositeTag);
                appendValue(fastX0); appendValue(fastY0);
                appendValue(fastX1); appendValue(fastY1);
                const uint32_t roundedCount =
                    static_cast<uint32_t>(roundedClipStack_.size());
                appendValue(roundedCount);
                for (const auto& clip : roundedClipStack_) {
                    appendValue(clip.x); appendValue(clip.y);
                    appendValue(clip.w); appendValue(clip.h);
                    appendValue(clip.radiusTL); appendValue(clip.radiusTR);
                    appendValue(clip.radiusBR); appendValue(clip.radiusBL);
                }
                if (!bitmapCompositeCache_)
                    bitmapCompositeCache_ =
                        std::make_unique<SoftwareEffectResultCache>();
                if (auto* cached = bitmapCompositeCache_->FindBitmapComposited(
                        bitmapCompositeKey, scaledBitmap, fb_,
                        fastX0, fastY0, panelWidth, panelHeight)) {
                    RestoreRawRegion(
                        cached->outputPixels.data(), panelWidth, panelHeight,
                        fastX0, fastY0);
                    return;
                }

                const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
                destinationBefore.resize(
                    rowBytes * static_cast<size_t>(panelHeight));
                for (int32_t row = 0; row < panelHeight; ++row) {
                    const uint8_t* source = fb_.pixels.data() +
                        (static_cast<size_t>(fastY0 + row) * fb_.width + fastX0) * 4u;
                    std::memcpy(
                        destinationBefore.data() + static_cast<size_t>(row) * rowBytes,
                        source, rowBytes);
                }
            } catch (const std::bad_alloc&) {
                bitmapCompositeCache_.reset();
                bitmapCompositeKey.clear();
                destinationBefore.clear();
            }
        }

        auto copyRows = [&](int32_t begin, int32_t end) {
            for (int32_t dy = begin; dy < end; ++dy) {
                int32_t rowX0 = fastX0;
                int32_t rowX1 = fastX1;
                if (hasRoundedClip &&
                    !TightenSpanToRoundedClips(dy, rowX0, rowX1)) continue;
                const int32_t sourceY = dy - fastDestinationTop;
                const int32_t sourceX = rowX0 - fastDestinationLeft;
                if (sourceY < 0 || sourceY >= fastHeight || sourceX < 0 ||
                    sourceX + (rowX1 - rowX0) > fastWidth) continue;
                const uint8_t* source = fastPixels +
                    (static_cast<size_t>(sourceY) * fastWidth + sourceX) * 4u;
                uint8_t* destination = fb_.pixels.data() +
                    (static_cast<size_t>(dy) * fb_.width + rowX0) * 4u;
                if (fastOpaque && globalOpacity >= 0.999999f) {
                    std::memcpy(
                        destination, source,
                        static_cast<size_t>(rowX1 - rowX0) * 4u);
                    continue;
                }
                if (fastAlphaPrecomposited) {
                    fb_.BlendBgraSpan(dy, rowX0, rowX1, source);
                    continue;
                }
                int32_t dx = rowX0;
                while (dx < rowX1) {
                    const uint8_t sourceAlpha = fastAlphaPrecomposited
                        ? source[3]
                        : static_cast<uint8_t>(std::clamp(
                            source[3] * globalOpacity + 0.5f, 0.0f, 255.0f));
                    if (sourceAlpha == 255) {
                        const int32_t runStart = dx;
                        const uint8_t* runSource = source;
                        do {
                            ++dx;
                            source += 4;
                            if (dx >= rowX1) break;
                            const uint8_t nextAlpha = fastAlphaPrecomposited
                                ? source[3]
                                : static_cast<uint8_t>(std::clamp(
                                    source[3] * globalOpacity + 0.5f,
                                    0.0f, 255.0f));
                            if (nextAlpha != 255) break;
                        } while (true);
                        std::memcpy(
                            destination + static_cast<size_t>(runStart - rowX0) * 4u,
                            runSource,
                            static_cast<size_t>(dx - runStart) * 4u);
                        continue;
                    }
                    if (sourceAlpha != 0) {
                        uint8_t* destinationPixel = destination +
                            static_cast<size_t>(dx - rowX0) * 4u;
                        if (destinationPixel[3] == 255) {
                            const uint32_t inverseAlpha = 255u - sourceAlpha;
                            destinationPixel[0] = static_cast<uint8_t>(
                                (static_cast<uint32_t>(source[0]) * sourceAlpha +
                                 static_cast<uint32_t>(destinationPixel[0]) * inverseAlpha) /
                                255u);
                            destinationPixel[1] = static_cast<uint8_t>(
                                (static_cast<uint32_t>(source[1]) * sourceAlpha +
                                 static_cast<uint32_t>(destinationPixel[1]) * inverseAlpha) /
                                255u);
                            destinationPixel[2] = static_cast<uint8_t>(
                                (static_cast<uint32_t>(source[2]) * sourceAlpha +
                                 static_cast<uint32_t>(destinationPixel[2]) * inverseAlpha) /
                                255u);
                        } else {
                            fb_.BlendPixelUnchecked(
                                dx, dy, source[2], source[1], source[0], sourceAlpha);
                        }
                    }
                    ++dx;
                    source += 4;
                }
            }
        };

        SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
        const uint64_t pixelWork = static_cast<uint64_t>(fastX1 - fastX0) *
            static_cast<uint64_t>(fastY1 - fastY0);
        if (workerPool && workerPool->CanParallelize() &&
            pixelWork >= 256u * 1024u) {
            workerPool->ParallelFor(fastY0, fastY1, 16, copyRows);
        } else {
            copyRows(fastY0, fastY1);
        }

        if (bitmapCompositeCache_ && !bitmapCompositeKey.empty() &&
            !destinationBefore.empty()) {
            try {
                SoftwareEffectResultCache::Entry entry;
                entry.key = std::move(bitmapCompositeKey);
                entry.contextPixels = std::move(destinationBefore);
                entry.scaledBitmap = scaledBitmap;
                entry.panelX = fastX0;
                entry.panelY = fastY0;
                entry.panelWidth = panelWidth;
                entry.panelHeight = panelHeight;
                const size_t rowBytes = static_cast<size_t>(panelWidth) * 4u;
                entry.outputPixels.resize(
                    rowBytes * static_cast<size_t>(panelHeight));
                for (int32_t row = 0; row < panelHeight; ++row) {
                    const uint8_t* source = fb_.pixels.data() +
                        (static_cast<size_t>(fastY0 + row) * fb_.width + fastX0) * 4u;
                    std::memcpy(
                        entry.outputPixels.data() + static_cast<size_t>(row) * rowBytes,
                        source, rowBytes);
                }
                bitmapCompositeCache_->Store(std::move(entry));
            } catch (const std::bad_alloc&) {
                bitmapCompositeCache_.reset();
            }
        }
        return;
    }

    auto isInsideDestination = [&](float deviceX, float deviceY) {
        float localX, localY;
        inverse.Apply(deviceX, deviceY, localX, localY);
        return localX >= x && localX < x + w && localY >= y && localY < y + h;
    };

    for (int32_t dy = py0; dy < py1; dy++) {
        for (int32_t dx = px0; dx < px1; dx++) {
            if (!clipStack_.empty() && IsClipped((float)dx + 0.5f, (float)dy + 0.5f)) continue;

            float coverage;
            if (axisAligned) {
                const float covX = std::clamp(
                    std::min((float)dx + 1.0f, right) - std::max((float)dx, left),
                    0.0f, 1.0f);
                const float covY = std::clamp(
                    std::min((float)dy + 1.0f, bottom) - std::max((float)dy, top),
                    0.0f, 1.0f);
                coverage = covX * covY;
            } else {
                // Keep transformed bitmap edges antialiased without charging
                // the common DPI/translation path for extra samples.
                coverage = 0.25f * (
                    (isInsideDestination((float)dx + 0.25f, (float)dy + 0.25f) ? 1.0f : 0.0f) +
                    (isInsideDestination((float)dx + 0.75f, (float)dy + 0.25f) ? 1.0f : 0.0f) +
                    (isInsideDestination((float)dx + 0.25f, (float)dy + 0.75f) ? 1.0f : 0.0f) +
                    (isInsideDestination((float)dx + 0.75f, (float)dy + 0.75f) ? 1.0f : 0.0f));
            }
            if (coverage <= 0.0f) continue;

            float localX, localY;
            inverse.Apply((float)dx + 0.5f, (float)dy + 0.5f, localX, localY);
            float u = (localX - x) * invW * (float)sw - 0.5f;
            float v = (localY - y) * invH * (float)shh - 0.5f;
            float u0f = std::floor(u);
            float v0f = std::floor(v);
            int32_t u0 = std::clamp((int32_t)u0f, 0, sw - 1);
            int32_t u1 = std::clamp((int32_t)u0f + 1, 0, sw - 1);
            int32_t v0 = std::clamp((int32_t)v0f, 0, shh - 1);
            int32_t v1 = std::clamp((int32_t)v0f + 1, 0, shh - 1);
            float fu = u - u0f;
            float fv = v - v0f;

            auto texel = [&](int32_t sxc, int32_t syc, float& tb, float& tg, float& tr, float& ta) {
                size_t i = ((size_t)syc * (size_t)sw + (size_t)sxc) * 4;
                tb = sb->pixels_[i + 0];
                tg = sb->pixels_[i + 1];
                tr = sb->pixels_[i + 2];
                ta = sb->pixels_[i + 3];
            };
            float b00, g00, r00, a00, b10, g10, r10, a10, b01, g01, r01, a01, b11, g11, r11, a11;
            texel(u0, v0, b00, g00, r00, a00);
            texel(u1, v0, b10, g10, r10, a10);
            texel(u0, v1, b01, g01, r01, a01);
            texel(u1, v1, b11, g11, r11, a11);
            float w00 = (1.0f - fu) * (1.0f - fv), w10 = fu * (1.0f - fv);
            float w01 = (1.0f - fu) * fv,          w11 = fu * fv;
            float sbb = b00 * w00 + b10 * w10 + b01 * w01 + b11 * w11;
            float sg  = g00 * w00 + g10 * w10 + g01 * w01 + g11 * w11;
            float sr  = r00 * w00 + r10 * w10 + r01 * w01 + r11 * w11;
            float sa  = a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11;

            float alpha = sa * globalOpacity * coverage;
            uint8_t fa = (uint8_t)std::clamp(alpha + 0.5f, 0.0f, 255.0f);
            if (fa == 0) continue;
            fb_.BlendPixel(dx, dy,
                (uint8_t)std::clamp(sr + 0.5f, 0.0f, 255.0f),
                (uint8_t)std::clamp(sg + 0.5f, 0.0f, 255.0f),
                (uint8_t)std::clamp(sbb + 0.5f, 0.0f, 255.0f),
                fa);
        }
    }
}

void SoftwareRenderTarget::DrawBackdropFilter(
    float x, float y, float w, float h,
    const char* backdropFilter, const char* material, const char* materialTint,
    float tintOpacity, float blurRadius,
    float cornerRadiusTL, float cornerRadiusTR,
    float cornerRadiusBR, float cornerRadiusBL)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    // Clamp to framebuffer bounds
    int32_t x0 = std::max(0, ix), y0 = std::max(0, iy);
    int32_t x1 = std::min(fb_.width, ix + iw), y1 = std::min(fb_.height, iy + ih);
    int32_t rw = x1 - x0, rh = y1 - y0;
    if (rw <= 0 || rh <= 0) return;

    bool hasCorners = (cornerRadiusTL > 0 || cornerRadiusTR > 0 || cornerRadiusBR > 0 || cornerRadiusBL > 0);

    // Step 1: Copy region and apply blur
    if (blurRadius > 0) {
        SoftwareFramebuffer blurred;
        CopyRegion(fb_, blurred, x0, y0, rw, rh);
        BoxBlur(blurred.pixels, rw, rh, (int32_t)(blurRadius * 0.5f + 0.5f));

        // Write blurred pixels back (respecting rounded corners)
        for (int32_t row = 0; row < rh; row++) {
            for (int32_t col = 0; col < rw; col++) {
                if (hasCorners) {
                    float lx = (float)(x0 + col - ix), ly = (float)(y0 + row - iy);
                    if (!IsInsidePerCornerRoundedRect(lx, ly, w, h,
                        cornerRadiusTL, cornerRadiusTR, cornerRadiusBR, cornerRadiusBL))
                        continue;
                }
                if (!clipStack_.empty() && IsClipped((float)(x0 + col), (float)(y0 + row))) continue;
                size_t srcIdx = ((size_t)row * rw + col) * 4;
                fb_.SetPixel(x0 + col, y0 + row,
                    blurred.pixels[srcIdx + 2], blurred.pixels[srcIdx + 1],
                    blurred.pixels[srcIdx + 0], blurred.pixels[srcIdx + 3]);
            }
        }
    }

    // Step 2: Parse tint color from materialTint (hex like "#RRGGBB")
    uint8_t tintR = 128, tintG = 128, tintB = 128;
    if (materialTint && materialTint[0] == '#' && std::strlen(materialTint) >= 7) {
        auto hex2 = [](const char* s) -> uint8_t {
            auto c = [](char ch) -> int { return (ch >= 'a') ? ch - 'a' + 10 : (ch >= 'A') ? ch - 'A' + 10 : ch - '0'; };
            return (uint8_t)(c(s[0]) * 16 + c(s[1]));
        };
        tintR = hex2(materialTint + 1);
        tintG = hex2(materialTint + 3);
        tintB = hex2(materialTint + 5);
    }

    // Step 3: Apply tint overlay
    if (tintOpacity > 0) {
        uint8_t a = FloatToU8(tintOpacity * currentOpacity_);
        for (int32_t row = y0; row < y1; row++) {
            for (int32_t col = x0; col < x1; col++) {
                if (hasCorners) {
                    float lx = (float)(col - ix), ly = (float)(row - iy);
                    if (!IsInsidePerCornerRoundedRect(lx, ly, w, h,
                        cornerRadiusTL, cornerRadiusTR, cornerRadiusBR, cornerRadiusBL))
                        continue;
                }
                if (!clipStack_.empty() && IsClipped((float)col, (float)row)) continue;
                fb_.BlendPixel(col, row, tintR, tintG, tintB, a);
            }
        }
    }
}

// lowbias32 integer hash on the device pixel — the same function the D3D12 and
// Vulkan backdrop shaders use, so the grain pattern matches across backends.
static inline uint32_t BackdropHashPixel(uint32_t px, uint32_t py, uint32_t salt)
{
    uint32_t n = px * 1597334677u ^ py * 3812015801u ^ salt * 2654435761u;
    n ^= n >> 16;
    n *= 0x7feb352du;
    n ^= n >> 15;
    n *= 0x846ca68bu;
    n ^= n >> 16;
    return n;
}

static inline float BackdropHash01(uint32_t px, uint32_t py, uint32_t salt)
{
    return (float)BackdropHashPixel(px, py, salt) * (1.0f / 4294967295.0f);
}

// Colour pipeline — the SAME order on every backend (D3D12 snapshot-backdrop
// PS, Vulkan backdrop_quad.frag.hlsl). CSS backdrop-filter semantics: the
// filters act on the backdrop, the tint composites on top:
//   blur -> brightness -> contrast -> saturation -> hueRotation -> grayscale
//        -> sepia -> invert -> tint -> luminosity -> noise
static inline void ApplyBackdropColorPipeline(const JaliumBackdropMaterialDesc& m,
                                              float& r, float& g, float& b)
{
    const float brightness = std::max(0.0f, m.brightness);
    r *= brightness; g *= brightness; b *= brightness;

    const float contrast = std::max(0.0f, m.contrast);
    r = (r - 0.5f) * contrast + 0.5f;
    g = (g - 0.5f) * contrast + 0.5f;
    b = (b - 0.5f) * contrast + 0.5f;

    const float saturation = std::max(0.0f, m.saturation);
    float luma = r * 0.299f + g * 0.587f + b * 0.114f;
    r = luma + (r - luma) * saturation;
    g = luma + (g - luma) * saturation;
    b = luma + (b - luma) * saturation;

    if (std::fabs(m.hueRotation) > 0.0001f) {
        const float yv = r * 0.299f + g * 0.587f + b * 0.114f;
        const float iv = r * 0.596f - g * 0.274f - b * 0.322f;
        const float qv = r * 0.211f - g * 0.523f + b * 0.312f;
        const float c = std::cos(m.hueRotation);
        const float s = std::sin(m.hueRotation);
        const float i2 = iv * c - qv * s;
        const float q2 = iv * s + qv * c;
        r = yv + 0.956f * i2 + 0.621f * q2;
        g = yv - 0.272f * i2 - 0.647f * q2;
        b = yv - 1.106f * i2 + 1.703f * q2;
    }

    const float grayscale = std::clamp(m.grayscale, 0.0f, 1.0f);
    luma = r * 0.299f + g * 0.587f + b * 0.114f;
    r = Lerp(r, luma, grayscale);
    g = Lerp(g, luma, grayscale);
    b = Lerp(b, luma, grayscale);

    const float sepia = std::clamp(m.sepia, 0.0f, 1.0f);
    if (sepia > 0.0f) {
        const float sr = r * 0.393f + g * 0.769f + b * 0.189f;
        const float sg = r * 0.349f + g * 0.686f + b * 0.168f;
        const float sb = r * 0.272f + g * 0.534f + b * 0.131f;
        r = Lerp(r, sr, sepia);
        g = Lerp(g, sg, sepia);
        b = Lerp(b, sb, sepia);
    }

    const float invert = std::clamp(m.invert, 0.0f, 1.0f);
    r = Lerp(r, 1.0f - r, invert);
    g = Lerp(g, 1.0f - g, invert);
    b = Lerp(b, 1.0f - b, invert);

    const float tintA = std::clamp(m.tintA, 0.0f, 1.0f);
    r = Lerp(r, std::clamp(m.tintR, 0.0f, 1.0f), tintA);
    g = Lerp(g, std::clamp(m.tintG, 0.0f, 1.0f), tintA);
    b = Lerp(b, std::clamp(m.tintB, 0.0f, 1.0f), tintA);

    const float luminosity = std::max(0.0f, m.luminosity);
    r *= luminosity; g *= luminosity; b *= luminosity;
}

void SoftwareRenderTarget::DrawBackdropMaterial(const JaliumBackdropMaterialDesc& m)
{
    const float w = m.width, h = m.height;
    if (w <= 0.0f || h <= 0.0f || m.opacity <= 0.0f) return;

    float tx, ty;
    currentTransform_.Apply(m.x, m.y, tx, ty);
    const int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    const int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    // Clamp the panel to the framebuffer.
    int32_t x0 = std::max(0, ix), y0 = std::max(0, iy);
    int32_t x1 = std::min(fb_.width, ix + iw), y1 = std::min(fb_.height, iy + ih);
    if (!fullInvalidation_ && hasDirtyRect_) {
        x0 = std::max(x0, dirtyLeft_);
        y0 = std::max(y0, dirtyTop_);
        x1 = std::min(x1, dirtyRight_);
        y1 = std::min(y1, dirtyBottom_);
    }
    const int32_t rw = x1 - x0, rh = y1 - y0;
    if (rw <= 0 || rh <= 0) return;

    const bool hasCorners = (m.cornerRadiusTL > 0 || m.cornerRadiusTR > 0 ||
                             m.cornerRadiusBR > 0 || m.cornerRadiusBL > 0);

    // Kernel: the GPU backends run a Gaussian with sigma = radius / 3 (Box =
    // box-equivalent sigma = radius / sqrt(3), explicit material sigma wins).
    // Three box passes of half-width k have sigma ~= k + 0.5, so pick k from
    // the target sigma rather than the old, unfounded radius / 2.
    const float sxScale = std::sqrt(currentTransform_.m[0] * currentTransform_.m[0] + currentTransform_.m[1] * currentTransform_.m[1]);
    const float syScale = std::sqrt(currentTransform_.m[2] * currentTransform_.m[2] + currentTransform_.m[3] * currentTransform_.m[3]);
    const float effectScale = std::max(0.0001f, std::min(sxScale > 0.0f ? sxScale : 1.0f, syScale > 0.0f ? syScale : 1.0f));
    float sigma = std::max(0.0f, m.blurRadius) / 3.0f;
    if (m.blurType == JALIUM_BACKDROP_BLUR_BOX) {
        sigma = std::max(0.0f, m.blurRadius) / 1.7320508f;
    } else if (m.blurSigma > 0.0f) {
        sigma = m.blurSigma;
    }
    sigma *= effectScale;
    const int32_t boxRadius = sigma > 0.25f ? std::max(1, (int32_t)std::lround(sigma - 0.5f)) : 0;

    // Blur the panel plus a kernel apron so edge pixels blur with their true
    // neighbourhood instead of the clamped edge, then read back the panel.
    const int32_t apron = boxRadius * 3 + 1;
    const int32_t sx0 = std::max(0, x0 - apron), sy0 = std::max(0, y0 - apron);
    const int32_t sx1 = std::min(fb_.width, x1 + apron), sy1 = std::min(fb_.height, y1 + apron);
    const int32_t sw = sx1 - sx0, sh = sy1 - sy0;
    if (sw <= 0 || sh <= 0) return;

    SoftwareFramebuffer blurred;
    CopyRegion(fb_, blurred, sx0, sy0, sw, sh);
    const bool cacheable = roundedClipStack_.empty();
    std::vector<uint8_t> backdropSource;
    if (cacheable) {
        try {
            if (!backdropCache_)
                backdropCache_ = std::make_unique<SoftwareBackdropCache>();
            const SoftwareClipRect* activeClip =
                clipStack_.empty() ? nullptr : &clipStack_.top();
            if (auto* cached = backdropCache_->Find(
                    m, currentTransform_, currentOpacity_, activeClip, blurred.pixels)) {
                if (cached->panelX == x0 && cached->panelY == y0 &&
                    cached->panelWidth == rw && cached->panelHeight == rh &&
                    cached->outputPixels.size() ==
                        static_cast<size_t>(rw) * rh * 4u) {
                    auto copyRows = [&](int32_t begin, int32_t end) {
                        for (int32_t row = begin; row < end; ++row) {
                            const uint8_t* source = cached->outputPixels.data() +
                                static_cast<size_t>(row) * rw * 4u;
                            uint8_t* destination = fb_.pixels.data() +
                                (static_cast<size_t>(y0 + row) * fb_.width + x0) * 4u;
                            std::memcpy(destination, source, static_cast<size_t>(rw) * 4u);
                        }
                    };
                    SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
                    if (workerPool && workerPool->CanParallelize() &&
                        static_cast<uint64_t>(rw) * rh >= 256u * 1024u) {
                        workerPool->ParallelFor(0, rh, 16, copyRows);
                    } else {
                        copyRows(0, rh);
                    }
                    return;
                }
            }
            backdropSource = blurred.pixels;
        } catch (const std::bad_alloc&) {
            backdropCache_.reset();
            backdropSource.clear();
        }
    }
    if (boxRadius > 0) {
        BoxBlur(blurred.pixels, sw, sh, boxRadius);
    }

    const float opacity = std::clamp(m.opacity, 0.0f, 1.0f) * currentOpacity_;
    const float noise = std::clamp(m.noiseIntensity, 0.0f, 1.0f);
    const float alphaFloor = 0.08f + std::clamp(m.tintA, 0.0f, 1.0f) * 0.25f;

    for (int32_t row = 0; row < rh; row++) {
        const int32_t py = y0 + row;
        for (int32_t col = 0; col < rw; col++) {
            const int32_t px = x0 + col;
            if (hasCorners) {
                const float lx = (float)(px - ix), ly = (float)(py - iy);
                if (!IsInsidePerCornerRoundedRect(lx, ly, w, h,
                        m.cornerRadiusTL, m.cornerRadiusTR, m.cornerRadiusBR, m.cornerRadiusBL))
                    continue;
            }
            if (!clipStack_.empty() && IsClipped((float)px, (float)py)) continue;

            const size_t srcIdx = ((size_t)(py - sy0) * sw + (px - sx0)) * 4;
            const float srcA = blurred.pixels[srcIdx + 3] / 255.0f;
            // The framebuffer is premultiplied; the pipeline runs on straight colour.
            const float inv = srcA > 0.0f ? 1.0f / srcA : 0.0f;
            float r = (blurred.pixels[srcIdx + 2] / 255.0f) * inv;
            float g = (blurred.pixels[srcIdx + 1] / 255.0f) * inv;
            float b = (blurred.pixels[srcIdx + 0] / 255.0f) * inv;

            ApplyBackdropColorPipeline(m, r, g, b);

            if (noise > 0.0f) {
                const float grain = (BackdropHash01((uint32_t)px, (uint32_t)py, 3u) - 0.5f) * noise;
                r += grain; g += grain; b += grain;
            }

            const float outA = std::max(srcA, alphaFloor) * opacity;
            fb_.BlendPixel(px, py, FloatToU8(r), FloatToU8(g), FloatToU8(b), FloatToU8(outA));
        }
    }

    if (cacheable && backdropCache_ && !backdropSource.empty()) {
        try {
            SoftwareBackdropCache::Entry entry;
            entry.descriptor = m;
            std::memcpy(entry.transform, currentTransform_.m, sizeof(entry.transform));
            entry.ambientOpacity = currentOpacity_;
            entry.hasClip = !clipStack_.empty();
            if (entry.hasClip) entry.clip = clipStack_.top();
            entry.panelX = x0;
            entry.panelY = y0;
            entry.panelWidth = rw;
            entry.panelHeight = rh;
            entry.sourcePixels = std::move(backdropSource);
            entry.outputPixels.resize(static_cast<size_t>(rw) * rh * 4u);
            for (int32_t row = 0; row < rh; ++row) {
                const uint8_t* source = fb_.pixels.data() +
                    (static_cast<size_t>(y0 + row) * fb_.width + x0) * 4u;
                uint8_t* destination = entry.outputPixels.data() +
                    static_cast<size_t>(row) * rw * 4u;
                std::memcpy(destination, source, static_cast<size_t>(rw) * 4u);
            }
            backdropCache_->Store(std::move(entry));
        } catch (const std::bad_alloc&) {
            backdropCache_.reset();
        }
    }
}

void SoftwareRenderTarget::DrawGlowingBorderHighlight(
    float x, float y, float w, float h,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float, float dimOpacity,
    float screenWidth, float screenHeight)
{
    // Dim overlay
    uint8_t da = FloatToU8(dimOpacity * currentOpacity_);
    for (int32_t row = 0; row < (int32_t)screenHeight && row < height_; row++) {
        for (int32_t col = 0; col < (int32_t)screenWidth && col < width_; col++) {
            fb_.BlendPixel(col, row, 0, 0, 0, da);
        }
    }

    // Glow border
    float alpha = 0.5f + 0.5f * sinf(animationPhase * 2.0f * 3.14159f);
    uint8_t gr = FloatToU8(glowColorR);
    uint8_t gg = FloatToU8(glowColorG);
    uint8_t gb = FloatToU8(glowColorB);
    uint8_t ga = FloatToU8(alpha * currentOpacity_);
    DrawBresenhamLine(x, y, x + w, y, gr, gg, gb, ga, strokeWidth);
    DrawBresenhamLine(x + w, y, x + w, y + h, gr, gg, gb, ga, strokeWidth);
    DrawBresenhamLine(x + w, y + h, x, y + h, gr, gg, gb, ga, strokeWidth);
    DrawBresenhamLine(x, y + h, x, y, gr, gg, gb, ga, strokeWidth);
}

void SoftwareRenderTarget::DrawGlowingBorderTransition(
    float fromX, float fromY, float fromW, float fromH,
    float toX, float toY, float toW, float toH,
    float headProgress, float tailProgress,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float trailLength, float dimOpacity,
    float screenWidth, float screenHeight)
{
    float t = (headProgress + tailProgress) * 0.5f;
    float x = fromX + (toX - fromX) * t;
    float y = fromY + (toY - fromY) * t;
    float w = fromW + (toW - fromW) * t;
    float h = fromH + (toH - fromH) * t;
    DrawGlowingBorderHighlight(x, y, w, h, animationPhase,
        glowColorR, glowColorG, glowColorB, strokeWidth, trailLength, dimOpacity,
        screenWidth, screenHeight);
}

void SoftwareRenderTarget::DrawRippleEffect(
    float x, float y, float w, float h,
    float rippleProgress,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float dimOpacity,
    float screenWidth, float screenHeight)
{
    float expansion = rippleProgress * 20.0f;
    float alpha = (1.0f - rippleProgress);
    DrawGlowingBorderHighlight(
        x - expansion, y - expansion,
        w + expansion * 2, h + expansion * 2,
        0, glowColorR, glowColorG, glowColorB,
        strokeWidth * (1.0f - rippleProgress * 0.5f), 0,
        dimOpacity * alpha, screenWidth, screenHeight);
}

// ============================================================================
// PushClipAliased
// ============================================================================

void SoftwareRenderTarget::PushClipAliased(float x, float y, float w, float h)
{
    // Same as PushClip for software backend (no AA distinction)
    PushClip(x, y, w, h);
}

// ============================================================================
// FillEllipseBatch
// ============================================================================

void SoftwareRenderTarget::FillEllipseBatch(const float* data, uint32_t count)
{
    if (!data) return;
    // data layout: [cx, cy, rx, ry, packedColor] × count
    for (uint32_t i = 0; i < count; i++) {
        float cx = data[i * 5 + 0];
        float cy = data[i * 5 + 1];
        float rx = data[i * 5 + 2];
        float ry = data[i * 5 + 3];
        // Unpack RGBA from float
        uint32_t packed;
        std::memcpy(&packed, &data[i * 5 + 4], sizeof(uint32_t));
        float cr = ((packed >> 0) & 0xFF) / 255.0f;
        float cg = ((packed >> 8) & 0xFF) / 255.0f;
        float cb = ((packed >> 16) & 0xFF) / 255.0f;
        float ca = ((packed >> 24) & 0xFF) / 255.0f;

        // Rasterize the ellipse with a float centre + AA coverage so animated
        // particles move smoothly instead of snapping their centre to whole
        // pixels. Radii stay in caller units (the batch is pre-scaled, as before).
        float tx, ty;
        currentTransform_.Apply(cx, cy, tx, ty);
        if (rx <= 0.0f || ry <= 0.0f) continue;

        SoftwareSolidBrush particleBrush(cr, cg, cb, ca);
        RasterizeCoverageAA(tx, ty, -rx, -ry, rx, ry, &particleBrush,
            [rx, ry](float lx, float ly) -> bool {
                float ex = lx / rx, ey = ly / ry;
                return ex * ex + ey * ey <= 1.0f;
            });
    }
}

// ============================================================================
// Effect Capture Pipeline
// ============================================================================

void SoftwareRenderTarget::BeginEffectCapture(float x, float y, float w, float h)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    EffectCaptureState state;
    state.x = tx;
    state.y = ty;
    state.width = w;
    state.height = h;

    // Clear the capture region so we render effect content in isolation
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);
    state.pixelX = ix;
    state.pixelY = iy;
    CopyRegion(fb_, state.savedRegion, ix, iy, iw, ih);
    effectCaptureStack_.push_back(std::move(state));
    effectCaptureReady_ = false;

    const int32_t clearLeft = std::max(0, ix);
    const int32_t clearTop = std::max(0, iy);
    const int32_t clearRight = std::min(fb_.width, ix + iw);
    const int32_t clearBottom = std::min(fb_.height, iy + ih);
    const size_t rowBytes = clearRight > clearLeft
        ? static_cast<size_t>(clearRight - clearLeft) * 4u
        : 0u;
    for (int32_t row = clearTop; row < clearBottom; ++row) {
        uint8_t* destination = fb_.pixels.data() +
            (static_cast<size_t>(row) * fb_.width + clearLeft) * 4u;
        std::memset(destination, 0, rowBytes);
    }
}

void SoftwareRenderTarget::EndEffectCapture()
{
    if (effectCaptureStack_.empty()) {
        effectCaptureReady_ = false;
        return;
    }

    EffectCaptureState state = std::move(effectCaptureStack_.back());
    effectCaptureStack_.pop_back();

    // Copy the rendered content from capture region into effectCaptureFb_
    int32_t ix = (int32_t)state.x, iy = (int32_t)state.y;
    int32_t iw = (int32_t)(state.width + 0.5f), ih = (int32_t)(state.height + 0.5f);
    CopyRegion(fb_, effectCaptureFb_, ix, iy, iw, ih);

    // Restore only the bytes hidden by this scope. The framebuffer outside the
    // capture never moved or copied; nested captures therefore restore into the
    // ancestor's in-progress image exactly as before.
    RestoreRegion(state.savedRegion, state.pixelX, state.pixelY);
    lastEffectCaptureX_ = state.x;
    lastEffectCaptureY_ = state.y;
    lastEffectCaptureW_ = state.width;
    lastEffectCaptureH_ = state.height;
    effectCaptureReady_ = true;
}

void SoftwareRenderTarget::DrawBlurEffect(float x, float y, float w, float h, float radius,
    float uvOffsetX, float uvOffsetY)
{
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;

    // Apply blur to the captured content
    SoftwareFramebuffer blurred;
    blurred.width = effectCaptureFb_.width;
    blurred.height = effectCaptureFb_.height;
    blurred.pixels = effectCaptureFb_.pixels;

    if (radius > 0) {
        BoxBlur(blurred.pixels, blurred.width, blurred.height, (int32_t)(radius * 0.5f + 0.5f));
    }

    // The capture buffer includes effect padding. Composite its top-left at
    // the captured origin; its content already begins uvOffset pixels inside.
    int32_t dstX = (int32_t)lastEffectCaptureX_;
    int32_t dstY = (int32_t)lastEffectCaptureY_;
    BlitBuffer(blurred, dstX, dstY, currentOpacity_);
}

void SoftwareRenderTarget::DrawDropShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    const int32_t shadowX = static_cast<int32_t>(lastEffectCaptureX_ + offsetX);
    const int32_t shadowY = static_cast<int32_t>(lastEffectCaptureY_ + offsetY);
    const int32_t originalX = static_cast<int32_t>(lastEffectCaptureX_);
    const int32_t originalY = static_cast<int32_t>(lastEffectCaptureY_);
    const int32_t unionX = std::min(shadowX, originalX);
    const int32_t unionY = std::min(shadowY, originalY);
    const int32_t unionRight = std::max(shadowX + iw, originalX + iw);
    const int32_t unionBottom = std::max(shadowY + ih, originalY + ih);
    const int32_t unionWidth = unionRight - unionX;
    const int32_t unionHeight = unionBottom - unionY;

    std::vector<uint8_t> cacheKey;
    try {
        cacheKey.reserve(64);
        auto appendValue = [&](const auto& value) {
            const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
            cacheKey.insert(cacheKey.end(), bytes, bytes + sizeof(value));
        };
        constexpr uint32_t kDropShadowTag = 0x44534844u; // "DSHD"
        appendValue(kDropShadowTag);
        appendValue(iw); appendValue(ih);
        appendValue(blurRadius);
        appendValue(offsetX); appendValue(offsetY);
        appendValue(r); appendValue(g); appendValue(b); appendValue(a);
        appendValue(cornerTL); appendValue(cornerTR);
        appendValue(cornerBR); appendValue(cornerBL);
        appendValue(currentOpacity_);
        appendValue(unionX); appendValue(unionY);

        if (!effectResultCache_)
            effectResultCache_ = std::make_unique<SoftwareEffectResultCache>();
        if (unionWidth > 0 && unionHeight > 0) {
            if (auto* cached = effectResultCache_->Find(
                    cacheKey, effectCaptureFb_.pixels)) {
                if (cached->panelX == unionX && cached->panelY == unionY &&
                    cached->panelWidth == unionWidth &&
                    cached->panelHeight == unionHeight &&
                    cached->outputPixels.size() ==
                        static_cast<size_t>(unionWidth) * unionHeight * 4u) {
                    BlitRawBuffer(
                        cached->outputPixels.data(), unionWidth, unionHeight,
                        unionX, unionY, 1.0f);
                    return;
                }
            }
        }
    } catch (const std::bad_alloc&) {
        effectResultCache_.reset();
        cacheKey.clear();
    }

    // Create shadow mask from captured alpha channel
    SoftwareFramebuffer shadow;
    shadow.Resize(iw, ih);
    uint8_t sr = FloatToU8(r), sg = FloatToU8(g), sb = FloatToU8(b);
    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            size_t idx = ((size_t)row * iw + col) * 4;
            uint8_t alpha = effectCaptureFb_.pixels[idx + 3];
            float sa = (alpha / 255.0f) * a;
            shadow.pixels[idx + 0] = (uint8_t)(sb * sa);
            shadow.pixels[idx + 1] = (uint8_t)(sg * sa);
            shadow.pixels[idx + 2] = (uint8_t)(sr * sa);
            shadow.pixels[idx + 3] = (uint8_t)(sa * 255.0f);
        }
    }

    // Blur the shadow
    if (blurRadius > 0) {
        BoxBlur(shadow.pixels, iw, ih, (int32_t)(blurRadius * 0.5f + 0.5f));
    }

    // Draw shadow (offset)
    BlitBuffer(shadow, shadowX, shadowY, currentOpacity_);

    // Draw original content on top
    BlitBuffer(effectCaptureFb_, originalX, originalY, currentOpacity_);

    if (effectResultCache_ && !cacheKey.empty() &&
        unionWidth > 0 && unionHeight > 0) {
        try {
            SoftwareFramebuffer overlay;
            overlay.Resize(unionWidth, unionHeight);
            BlendBufferInto(
                overlay, shadow,
                shadowX - unionX, shadowY - unionY, currentOpacity_);
            BlendBufferInto(
                overlay, effectCaptureFb_,
                originalX - unionX, originalY - unionY, currentOpacity_);
            SoftwareEffectResultCache::Entry entry;
            entry.key = std::move(cacheKey);
            entry.sourcePixels = effectCaptureFb_.pixels;
            entry.panelX = unionX;
            entry.panelY = unionY;
            entry.panelWidth = unionWidth;
            entry.panelHeight = unionHeight;
            entry.outputPixels = std::move(overlay.pixels);
            effectResultCache_->Store(std::move(entry));
        } catch (const std::bad_alloc&) {
            effectResultCache_.reset();
        }
    }
}

void SoftwareRenderTarget::DrawOuterGlowEffect(float x, float y, float w, float h,
    float glowSize, float r, float g, float b, float a, float intensity,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    (void)uvOffsetX; (void)uvOffsetY;
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    // Expand the buffer for glow spread
    int32_t expand = (int32_t)(glowSize + 0.5f);
    int32_t gw = iw + expand * 2;
    int32_t gh = ih + expand * 2;
    const int32_t glowX = static_cast<int32_t>(lastEffectCaptureX_) - expand;
    const int32_t glowY = static_cast<int32_t>(lastEffectCaptureY_) - expand;
    const int32_t panelX = glowX;
    const int32_t panelY = glowY;
    const int32_t panelWidth = gw;
    const int32_t panelHeight = gh;

    std::vector<uint8_t> cacheKey;
    try {
        auto appendValue = [&](const auto& value) {
            const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
            cacheKey.insert(cacheKey.end(), bytes, bytes + sizeof(value));
        };
        constexpr uint32_t kOuterGlowTag = 0x4F474C57u; // "OGLW"
        appendValue(kOuterGlowTag);
        appendValue(iw); appendValue(ih); appendValue(expand);
        appendValue(glowSize);
        appendValue(r); appendValue(g); appendValue(b); appendValue(a);
        appendValue(intensity); appendValue(uvOffsetX); appendValue(uvOffsetY);
        appendValue(cornerTL); appendValue(cornerTR);
        appendValue(cornerBR); appendValue(cornerBL);
        appendValue(currentOpacity_);
        appendValue(panelX); appendValue(panelY);
        if (!effectResultCache_)
            effectResultCache_ = std::make_unique<SoftwareEffectResultCache>();
        if (panelWidth > 0 && panelHeight > 0) {
            if (auto* cached = effectResultCache_->Find(
                    cacheKey, effectCaptureFb_.pixels)) {
                if (cached->panelX == panelX && cached->panelY == panelY &&
                    cached->panelWidth == panelWidth &&
                    cached->panelHeight == panelHeight &&
                    cached->outputPixels.size() ==
                        static_cast<size_t>(panelWidth) * panelHeight * 4u) {
                    BlitRawBuffer(
                        cached->outputPixels.data(), panelWidth, panelHeight,
                        panelX, panelY, 1.0f);
                    return;
                }
            }
        }
    } catch (const std::bad_alloc&) {
        effectResultCache_.reset();
        cacheKey.clear();
    }

    // Create glow mask from alpha, placed centered in expanded buffer
    SoftwareFramebuffer glow;
    glow.Resize(gw, gh);
    uint8_t gr = FloatToU8(r), gg = FloatToU8(g), gb = FloatToU8(b);
    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            size_t srcIdx = ((size_t)row * iw + col) * 4;
            uint8_t alpha = effectCaptureFb_.pixels[srcIdx + 3];
            float ga = (alpha / 255.0f) * a * intensity;
            size_t dstIdx = ((size_t)(row + expand) * gw + col + expand) * 4;
            glow.pixels[dstIdx + 0] = (uint8_t)(gb * std::min(1.0f, ga));
            glow.pixels[dstIdx + 1] = (uint8_t)(gg * std::min(1.0f, ga));
            glow.pixels[dstIdx + 2] = (uint8_t)(gr * std::min(1.0f, ga));
            glow.pixels[dstIdx + 3] = FloatToU8(std::min(1.0f, ga));
        }
    }

    // Blur the glow
    BoxBlur(glow.pixels, gw, gh, expand);

    // Draw glow (shifted by expand)
    BlitBuffer(glow, glowX, glowY, currentOpacity_);

    // Draw original content on top
    BlitBuffer(effectCaptureFb_, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);

    if (effectResultCache_ && !cacheKey.empty() &&
        panelWidth > 0 && panelHeight > 0) {
        try {
            SoftwareFramebuffer overlay;
            overlay.Resize(panelWidth, panelHeight);
            BlendBufferInto(overlay, glow, 0, 0, currentOpacity_);
            BlendBufferInto(
                overlay, effectCaptureFb_,
                static_cast<int32_t>(lastEffectCaptureX_) - panelX,
                static_cast<int32_t>(lastEffectCaptureY_) - panelY,
                currentOpacity_);
            SoftwareEffectResultCache::Entry entry;
            entry.key = std::move(cacheKey);
            entry.sourcePixels = effectCaptureFb_.pixels;
            entry.outputPixels = std::move(overlay.pixels);
            entry.panelX = panelX;
            entry.panelY = panelY;
            entry.panelWidth = panelWidth;
            entry.panelHeight = panelHeight;
            effectResultCache_->Store(std::move(entry));
        } catch (const std::bad_alloc&) {
            effectResultCache_.reset();
        }
    }
}

void SoftwareRenderTarget::DrawInnerShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    const int32_t panelX = std::max(0, static_cast<int32_t>(lastEffectCaptureX_));
    const int32_t panelY = std::max(0, static_cast<int32_t>(lastEffectCaptureY_));
    const int32_t panelRight = std::min(
        fb_.width, static_cast<int32_t>(lastEffectCaptureX_) + iw);
    const int32_t panelBottom = std::min(
        fb_.height, static_cast<int32_t>(lastEffectCaptureY_) + ih);
    const int32_t panelWidth = panelRight - panelX;
    const int32_t panelHeight = panelBottom - panelY;

    std::vector<uint8_t> cacheKey;
    SoftwareFramebuffer destinationBefore;
    try {
        auto appendValue = [&](const auto& value) {
            const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
            cacheKey.insert(cacheKey.end(), bytes, bytes + sizeof(value));
        };
        constexpr uint32_t kInnerShadowTag = 0x49534844u; // "ISHD"
        appendValue(kInnerShadowTag);
        appendValue(iw); appendValue(ih);
        appendValue(blurRadius); appendValue(offsetX); appendValue(offsetY);
        appendValue(r); appendValue(g); appendValue(b); appendValue(a);
        appendValue(cornerTL); appendValue(cornerTR);
        appendValue(cornerBR); appendValue(cornerBL);
        appendValue(currentOpacity_);
        if (!effectResultCache_)
            effectResultCache_ = std::make_unique<SoftwareEffectResultCache>();
        if (panelWidth > 0 && panelHeight > 0) {
            if (auto* cached = effectResultCache_->FindComposited(
                    cacheKey, effectCaptureFb_.pixels, fb_,
                    panelX, panelY, panelWidth, panelHeight)) {
                RestoreRawRegion(
                    cached->outputPixels.data(), panelWidth, panelHeight,
                    panelX, panelY);
                return;
            }
            CopyRegion(
                fb_, destinationBefore,
                panelX, panelY, panelWidth, panelHeight);
        }
    } catch (const std::bad_alloc&) {
        effectResultCache_.reset();
        cacheKey.clear();
        destinationBefore.Resize(0, 0);
    }

    // Draw original content first
    BlitBuffer(effectCaptureFb_, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);

    // Create inverted alpha mask (shadow where content exists, weighted by distance from edge)
    SoftwareFramebuffer shadow;
    shadow.Resize(iw, ih);
    uint8_t sr = FloatToU8(r), sg = FloatToU8(g), sb = FloatToU8(b);

    int32_t offX = (int32_t)offsetX, offY = (int32_t)offsetY;
    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            // Sample from offset position
            int32_t sx = col - offX, sy = row - offY;
            float srcAlpha = 0;
            if (sx >= 0 && sx < iw && sy >= 0 && sy < ih) {
                size_t srcIdx = ((size_t)sy * iw + sx) * 4;
                srcAlpha = effectCaptureFb_.pixels[srcIdx + 3] / 255.0f;
            }
            // Inner shadow: visible where source has alpha AND offset source is transparent
            size_t myIdx = ((size_t)row * iw + col) * 4;
            float myAlpha = effectCaptureFb_.pixels[myIdx + 3] / 255.0f;
            float shadowA = myAlpha * (1.0f - srcAlpha) * a;

            size_t dstIdx = ((size_t)row * iw + col) * 4;
            shadow.pixels[dstIdx + 0] = (uint8_t)(sb * shadowA);
            shadow.pixels[dstIdx + 1] = (uint8_t)(sg * shadowA);
            shadow.pixels[dstIdx + 2] = (uint8_t)(sr * shadowA);
            shadow.pixels[dstIdx + 3] = FloatToU8(shadowA);
        }
    }

    // Blur the inner shadow
    if (blurRadius > 0) {
        BoxBlur(shadow.pixels, iw, ih, (int32_t)(blurRadius * 0.5f + 0.5f));
    }

    // Composite inner shadow on top (clipped to original alpha)
    for (int32_t row = 0; row < ih; row++) {
        int32_t dy = (int32_t)lastEffectCaptureY_ + row;
        if (dy < 0 || dy >= fb_.height) continue;
        for (int32_t col = 0; col < iw; col++) {
            int32_t dx = (int32_t)lastEffectCaptureX_ + col;
            if (dx < 0 || dx >= fb_.width) continue;

            size_t srcIdx = ((size_t)row * iw + col) * 4;
            size_t origIdx = ((size_t)row * iw + col) * 4;
            float origAlpha = effectCaptureFb_.pixels[origIdx + 3] / 255.0f;
            uint8_t sa = (uint8_t)(shadow.pixels[srcIdx + 3] * origAlpha * currentOpacity_);
            if (sa > 0) {
                fb_.BlendPixel(dx, dy, shadow.pixels[srcIdx + 2],
                    shadow.pixels[srcIdx + 1], shadow.pixels[srcIdx + 0], sa);
            }
        }
    }

    if (effectResultCache_ && !cacheKey.empty() &&
        !destinationBefore.pixels.empty()) {
        try {
            SoftwareFramebuffer output;
            CopyRegion(fb_, output, panelX, panelY, panelWidth, panelHeight);
            SoftwareEffectResultCache::Entry entry;
            entry.key = std::move(cacheKey);
            entry.sourcePixels = effectCaptureFb_.pixels;
            entry.contextPixels = std::move(destinationBefore.pixels);
            entry.outputPixels = std::move(output.pixels);
            entry.panelX = panelX;
            entry.panelY = panelY;
            entry.panelWidth = panelWidth;
            entry.panelHeight = panelHeight;
            effectResultCache_->Store(std::move(entry));
        } catch (const std::bad_alloc&) {
            effectResultCache_.reset();
        }
    }
}

void SoftwareRenderTarget::DrawColorMatrixEffect(float x, float y, float w, float h,
    const float* matrix)
{
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty() || !matrix) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    // Apply 5x4 color matrix transform to each pixel
    // Matrix layout (row-major): [R_in * m[0] + G_in * m[1] + B_in * m[2] + A_in * m[3] + m[4]] = R_out
    SoftwareFramebuffer result;
    result.width = iw;
    result.height = ih;
    result.pixels = effectCaptureFb_.pixels;

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            size_t idx = ((size_t)row * iw + col) * 4;
            float oB = result.pixels[idx + 0] / 255.0f;
            float oG = result.pixels[idx + 1] / 255.0f;
            float oR = result.pixels[idx + 2] / 255.0f;
            float oA = result.pixels[idx + 3] / 255.0f;

            float nR = oR * matrix[0] + oG * matrix[1] + oB * matrix[2] + oA * matrix[3] + matrix[4];
            float nG = oR * matrix[5] + oG * matrix[6] + oB * matrix[7] + oA * matrix[8] + matrix[9];
            float nB = oR * matrix[10] + oG * matrix[11] + oB * matrix[12] + oA * matrix[13] + matrix[14];
            float nA = oR * matrix[15] + oG * matrix[16] + oB * matrix[17] + oA * matrix[18] + matrix[19];

            result.pixels[idx + 0] = FloatToU8(nB);
            result.pixels[idx + 1] = FloatToU8(nG);
            result.pixels[idx + 2] = FloatToU8(nR);
            result.pixels[idx + 3] = FloatToU8(nA);
        }
    }

    BlitBuffer(result, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);
}

void SoftwareRenderTarget::DrawEmbossEffect(float x, float y, float w, float h,
    float amount, float lightDirX, float lightDirY, float relief)
{
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    // Normalize light direction
    float ldLen = std::sqrt(lightDirX * lightDirX + lightDirY * lightDirY);
    if (ldLen < 1e-6f) { lightDirX = 1; lightDirY = 0; }
    else { lightDirX /= ldLen; lightDirY /= ldLen; }

    int32_t offX = (int32_t)(lightDirX * relief + 0.5f);
    int32_t offY = (int32_t)(lightDirY * relief + 0.5f);
    if (offX == 0 && offY == 0) offX = 1;

    SoftwareFramebuffer result;
    result.width = iw;
    result.height = ih;
    result.pixels = effectCaptureFb_.pixels;

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            size_t idx = ((size_t)row * iw + col) * 4;

            // Get luminance of current and offset pixels
            auto getLum = [&](int32_t c, int32_t r) -> float {
                c = std::clamp(c, 0, iw - 1);
                r = std::clamp(r, 0, ih - 1);
                size_t i = ((size_t)r * iw + c) * 4;
                return (effectCaptureFb_.pixels[i + 0] + effectCaptureFb_.pixels[i + 1] + effectCaptureFb_.pixels[i + 2]) / (3.0f * 255.0f);
            };

            float lumCur = getLum(col, row);
            float lumOff = getLum(col + offX, row + offY);
            float diff = (lumCur - lumOff) * amount;

            // Apply emboss: shift brightness
            float oR = effectCaptureFb_.pixels[idx + 2] / 255.0f + diff;
            float oG = effectCaptureFb_.pixels[idx + 1] / 255.0f + diff;
            float oB = effectCaptureFb_.pixels[idx + 0] / 255.0f + diff;

            result.pixels[idx + 0] = FloatToU8(oB);
            result.pixels[idx + 1] = FloatToU8(oG);
            result.pixels[idx + 2] = FloatToU8(oR);
            // Alpha unchanged
        }
    }

    BlitBuffer(result, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);
}

void SoftwareRenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    // Custom shaders cannot be executed in software — just blit the captured content as-is
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;
    BlitBuffer(effectCaptureFb_, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);
}

void SoftwareRenderTarget::DrawShaderEffectFromSource(float x, float y, float w, float h,
    const char* hlslSource, const float* constants, uint32_t constantFloatCount)
{
    // The software backend cannot execute HLSL. Match the bytecode path and
    // degrade to a transparent pass-through instead of losing captured content.
    (void)x; (void)y; (void)w; (void)h;
    (void)hlslSource; (void)constants; (void)constantFloatCount;
    if (!effectCaptureReady_ || effectCaptureFb_.pixels.empty()) return;
    BlitBuffer(effectCaptureFb_, (int32_t)lastEffectCaptureX_,
        (int32_t)lastEffectCaptureY_, currentOpacity_);
}

// ============================================================================
// Desktop Capture
// ============================================================================

void SoftwareRenderTarget::CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height)
{
#ifdef _WIN32
    HDC screenDC = GetDC(nullptr);
    if (!screenDC) return;

    HDC memDC = CreateCompatibleDC(screenDC);
    HBITMAP hBmp = CreateCompatibleBitmap(screenDC, width, height);
    HGDIOBJ oldBmp = SelectObject(memDC, hBmp);

    BitBlt(memDC, 0, 0, width, height, screenDC, screenX, screenY, SRCCOPY);

    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = width;
    bmi.bmiHeader.biHeight = -height; // top-down
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    desktopCaptureFb_.Resize(width, height);
    GetDIBits(memDC, hBmp, 0, height, desktopCaptureFb_.pixels.data(), &bmi, DIB_RGB_COLORS);

    SelectObject(memDC, oldBmp);
    DeleteObject(hBmp);
    DeleteDC(memDC);
    ReleaseDC(nullptr, screenDC);
#else
    desktopCaptureFb_.Resize(width, height);
    desktopCaptureFb_.Clear(64, 64, 64, 255);
#endif
}

void SoftwareRenderTarget::DrawDesktopBackdrop(
    float x, float y, float w, float h,
    float blurRadius,
    float tintR, float tintG, float tintB, float tintOpacity,
    float noiseIntensity, float saturation)
{
    if (desktopCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t dstX = (int32_t)tx, dstY = (int32_t)ty;
    int32_t iw = desktopCaptureFb_.width, ih = desktopCaptureFb_.height;

    // Copy and blur
    SoftwareFramebuffer blurred;
    blurred.width = iw;
    blurred.height = ih;
    blurred.pixels = desktopCaptureFb_.pixels;

    if (blurRadius > 0) {
        BoxBlur(blurred.pixels, iw, ih, (int32_t)(blurRadius * 0.5f + 0.5f));
    }

    // Apply saturation adjustment
    if (std::abs(saturation - 1.0f) > 0.01f) {
        for (int32_t row = 0; row < ih; row++) {
            for (int32_t col = 0; col < iw; col++) {
                size_t idx = ((size_t)row * iw + col) * 4;
                float bv = blurred.pixels[idx + 0] / 255.0f;
                float gv = blurred.pixels[idx + 1] / 255.0f;
                float rv = blurred.pixels[idx + 2] / 255.0f;
                float lum = rv * 0.299f + gv * 0.587f + bv * 0.114f;
                rv = lum + (rv - lum) * saturation;
                gv = lum + (gv - lum) * saturation;
                bv = lum + (bv - lum) * saturation;
                blurred.pixels[idx + 0] = FloatToU8(bv);
                blurred.pixels[idx + 1] = FloatToU8(gv);
                blurred.pixels[idx + 2] = FloatToU8(rv);
            }
        }
    }

    // Blit blurred desktop
    BlitBuffer(blurred, dstX, dstY, currentOpacity_);

    // Apply tint overlay
    if (tintOpacity > 0) {
        uint8_t ta = FloatToU8(tintOpacity * currentOpacity_);
        uint8_t tr = FloatToU8(tintR), tg = FloatToU8(tintG), tb = FloatToU8(tintB);
        for (int32_t row = 0; row < ih && dstY + row < fb_.height; row++) {
            for (int32_t col = 0; col < iw && dstX + col < fb_.width; col++) {
                fb_.BlendPixel(dstX + col, dstY + row, tr, tg, tb, ta);
            }
        }
    }

    // Apply noise overlay
    if (noiseIntensity > 0) {
        uint32_t seed = 12345;
        for (int32_t row = 0; row < ih && dstY + row < fb_.height; row++) {
            for (int32_t col = 0; col < iw && dstX + col < fb_.width; col++) {
                // Simple PRNG for noise
                seed = seed * 1103515245 + 12345;
                float noise = ((seed >> 16) & 0x7FFF) / (float)0x7FFF;
                uint8_t nv = (uint8_t)(noise * 255.0f * noiseIntensity);
                uint8_t na = (uint8_t)(noiseIntensity * 30 * currentOpacity_);
                fb_.BlendPixel(dstX + col, dstY + row, nv, nv, nv, na);
            }
        }
    }
}

// ============================================================================
// Transition Capture & Shader
// ============================================================================

void SoftwareRenderTarget::BeginTransitionCapture(int slot, float x, float y, float w, float h)
{
    if (slot < 0 || slot > 1) return;
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    transitionX_[slot] = tx;
    transitionY_[slot] = ty;
    transitionW_[slot] = w;
    transitionH_[slot] = h;
    transitionCaptureActive_[slot] = true;

    // Clear region for capturing
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);
    for (int32_t row = std::max(0, iy); row < std::min(fb_.height, iy + ih); row++) {
        for (int32_t col = std::max(0, ix); col < std::min(fb_.width, ix + iw); col++) {
            fb_.SetPixel(col, row, 0, 0, 0, 0);
        }
    }
}

void SoftwareRenderTarget::EndTransitionCapture(int slot)
{
    if (slot < 0 || slot > 1 || !transitionCaptureActive_[slot]) return;

    int32_t ix = (int32_t)transitionX_[slot], iy = (int32_t)transitionY_[slot];
    int32_t iw = (int32_t)(transitionW_[slot] + 0.5f), ih = (int32_t)(transitionH_[slot] + 0.5f);
    CopyRegion(fb_, transitionCaptureFb_[slot], ix, iy, iw, ih);
    transitionCaptureActive_[slot] = false;
}

void SoftwareRenderTarget::DrawTransitionShader(float x, float y, float w, float h, float progress, int mode)
{
    if (transitionCaptureFb_[0].pixels.empty() || transitionCaptureFb_[1].pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t dstX = (int32_t)tx, dstY = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    auto& oldFb = transitionCaptureFb_[0];
    auto& newFb = transitionCaptureFb_[1];

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            int32_t dx = dstX + col, dy = dstY + row;
            if (dx < 0 || dx >= fb_.width || dy < 0 || dy >= fb_.height) continue;

            // Sample from both captures
            float u = (float)col / iw, v = (float)row / ih;
            int32_t srcCol0 = std::min((int32_t)(u * oldFb.width), oldFb.width - 1);
            int32_t srcRow0 = std::min((int32_t)(v * oldFb.height), oldFb.height - 1);
            int32_t srcCol1 = std::min((int32_t)(u * newFb.width), newFb.width - 1);
            int32_t srcRow1 = std::min((int32_t)(v * newFb.height), newFb.height - 1);

            size_t idx0 = ((size_t)srcRow0 * oldFb.width + srcCol0) * 4;
            size_t idx1 = ((size_t)srcRow1 * newFb.width + srcCol1) * 4;

            float t = progress; // blending factor

            // Mode-specific transition
            switch (mode) {
            case 1: // Wipe left-to-right
                t = (u < progress) ? 1.0f : 0.0f;
                break;
            case 2: // Wipe top-to-bottom
                t = (v < progress) ? 1.0f : 0.0f;
                break;
            case 3: // Circular reveal
            {
                float cx = 0.5f, cy = 0.5f;
                float dist = std::sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) / 0.707f;
                t = (dist < progress) ? 1.0f : 0.0f;
                break;
            }
            case 4: // Dissolve (noise-based)
            {
                uint32_t hash = (uint32_t)(col * 73856093 ^ row * 19349663);
                float noise = (hash & 0xFFFF) / 65535.0f;
                t = (noise < progress) ? 1.0f : 0.0f;
                break;
            }
            case 5: // Slide left
            {
                int32_t offset = (int32_t)((1.0f - progress) * iw);
                int32_t newCol = col - offset;
                if (newCol >= 0 && newCol < newFb.width) {
                    idx1 = ((size_t)srcRow1 * newFb.width + newCol) * 4;
                    t = 1.0f;
                } else {
                    int32_t oldCol = col + (int32_t)(progress * iw);
                    if (oldCol >= 0 && oldCol < oldFb.width) {
                        idx0 = ((size_t)srcRow0 * oldFb.width + oldCol) * 4;
                    }
                    t = 0.0f;
                }
                break;
            }
            default: // Crossfade (mode 0 and default)
                break;
            }

            // Lerp between old and new
            float it = 1.0f - t;
            uint8_t rb = (uint8_t)(oldFb.pixels[idx0 + 0] * it + newFb.pixels[idx1 + 0] * t);
            uint8_t rg = (uint8_t)(oldFb.pixels[idx0 + 1] * it + newFb.pixels[idx1 + 1] * t);
            uint8_t rr = (uint8_t)(oldFb.pixels[idx0 + 2] * it + newFb.pixels[idx1 + 2] * t);
            uint8_t ra = (uint8_t)(oldFb.pixels[idx0 + 3] * it + newFb.pixels[idx1 + 3] * t);

            fb_.SetPixel(dx, dy, rr, rg, rb, ra);
        }
    }
}

void SoftwareRenderTarget::DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity)
{
    if (slot < 0 || slot > 1 || transitionCaptureFb_[slot].pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    BlitBuffer(transitionCaptureFb_[slot], (int32_t)tx, (int32_t)ty, opacity * currentOpacity_);
}

// ============================================================================
// Liquid Glass Approximation
// ============================================================================

void SoftwareRenderTarget::DrawLiquidGlass(
    float x, float y, float w, float h,
    float cornerRadius,
    float blurRadius,
    float refractionAmount,
    float chromaticAberration,
    float tintR, float tintG, float tintB, float tintOpacity,
    float lightX, float lightY,
    float highlightBoost,
    int shapeType,
    float shapeExponent,
    int neighborCount,
    float fusionRadius,
    const float* neighborData)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    // Clamp to fb bounds
    int32_t x0 = std::max(0, ix), y0 = std::max(0, iy);
    int32_t x1 = std::min(fb_.width, ix + iw), y1 = std::min(fb_.height, iy + ih);
    int32_t rw = x1 - x0, rh = y1 - y0;
    if (rw <= 0 || rh <= 0) return;

    float cr = std::min(cornerRadius, std::min(w, h) * 0.5f);

    // Step 1: Capture and blur background
    SoftwareFramebuffer blurred;
    CopyRegion(fb_, blurred, x0, y0, rw, rh);
    std::vector<uint8_t> liquidKey;
    std::vector<uint8_t> liquidSource;
    try {
        auto appendValue = [&](const auto& value) {
            const uint8_t* bytes = reinterpret_cast<const uint8_t*>(&value);
            liquidKey.insert(liquidKey.end(), bytes, bytes + sizeof(value));
        };
        appendValue(x); appendValue(y); appendValue(w); appendValue(h);
        appendValue(cornerRadius); appendValue(blurRadius);
        appendValue(refractionAmount); appendValue(chromaticAberration);
        appendValue(tintR); appendValue(tintG); appendValue(tintB); appendValue(tintOpacity);
        appendValue(lightX); appendValue(lightY); appendValue(highlightBoost);
        appendValue(shapeType); appendValue(shapeExponent);
        appendValue(neighborCount); appendValue(fusionRadius);
        appendValue(currentOpacity_);
        for (float component : currentTransform_.m) appendValue(component);
        const bool hasClip = !clipStack_.empty();
        appendValue(hasClip);
        if (hasClip) {
            const auto& clip = clipStack_.top();
            appendValue(clip.x); appendValue(clip.y);
            appendValue(clip.w); appendValue(clip.h);
        }
        const uint32_t roundedCount = static_cast<uint32_t>(roundedClipStack_.size());
        appendValue(roundedCount);
        for (const auto& clip : roundedClipStack_) {
            appendValue(clip.x); appendValue(clip.y);
            appendValue(clip.w); appendValue(clip.h);
            appendValue(clip.radiusTL); appendValue(clip.radiusTR);
            appendValue(clip.radiusBR); appendValue(clip.radiusBL);
        }
        const int safeNeighborCount = std::clamp(neighborCount, 0, 4);
        if (neighborData && safeNeighborCount > 0) {
            const uint8_t* neighborBytes = reinterpret_cast<const uint8_t*>(neighborData);
            liquidKey.insert(
                liquidKey.end(), neighborBytes,
                neighborBytes + static_cast<size_t>(safeNeighborCount) * 5u * sizeof(float));
        }

        if (!liquidGlassCache_)
            liquidGlassCache_ = std::make_unique<SoftwareEffectResultCache>();
        if (auto* cached = liquidGlassCache_->Find(liquidKey, blurred.pixels)) {
            if (cached->panelX == x0 && cached->panelY == y0 &&
                cached->panelWidth == rw && cached->panelHeight == rh &&
                cached->outputPixels.size() == static_cast<size_t>(rw) * rh * 4u) {
                auto copyRows = [&](int32_t begin, int32_t end) {
                    for (int32_t row = begin; row < end; ++row) {
                        const uint8_t* source = cached->outputPixels.data() +
                            static_cast<size_t>(row) * rw * 4u;
                        uint8_t* destination = fb_.pixels.data() +
                            (static_cast<size_t>(y0 + row) * fb_.width + x0) * 4u;
                        std::memcpy(destination, source, static_cast<size_t>(rw) * 4u);
                    }
                };
                SoftwareWorkerPool* workerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
                if (workerPool && workerPool->CanParallelize() &&
                    static_cast<uint64_t>(rw) * rh >= 256u * 1024u) {
                    workerPool->ParallelFor(0, rh, 16, copyRows);
                } else {
                    copyRows(0, rh);
                }
                return;
            }
        }
        liquidSource = blurred.pixels;
    } catch (const std::bad_alloc&) {
        liquidGlassCache_.reset();
        liquidKey.clear();
        liquidSource.clear();
    }
    if (blurRadius > 0) {
        BoxBlur(blurred.pixels, rw, rh, (int32_t)(blurRadius * 0.5f + 0.5f));
    }

    // Step 2: Composite: blurred background + refraction distortion + tint + highlight
    auto compositeRows = [&](int32_t rowBegin, int32_t rowEnd) {
    for (int32_t row = rowBegin; row < rowEnd; row++) {
        for (int32_t col = 0; col < rw; col++) {
            float lx = (float)(x0 + col - ix), ly = (float)(y0 + row - iy);

            // Check rounded rect containment
            if (cr > 0 && !IsInsidePerCornerRoundedRect(lx, ly, w, h, cr, cr, cr, cr))
                continue;

            if (!clipStack_.empty() && IsClipped((float)(x0 + col), (float)(y0 + row))) continue;

            // Normalized coordinates within the glass element
            float nu = lx / w, nv = ly / h;

            // Simple refraction distortion (SDF-based displacement)
            float distFromEdge = std::min({lx, ly, w - lx, h - ly}) / (w * 0.5f);
            distFromEdge = std::clamp(distFromEdge, 0.0f, 1.0f);
            float refrX = (nu - 0.5f) * refractionAmount * (1.0f - distFromEdge);
            float refrY = (nv - 0.5f) * refractionAmount * (1.0f - distFromEdge);

            // Sample blurred background with displacement
            int32_t srcCol = std::clamp((int32_t)(col + refrX * rw), 0, rw - 1);
            int32_t srcRow = std::clamp((int32_t)(row + refrY * rh), 0, rh - 1);
            size_t srcIdx = ((size_t)srcRow * rw + srcCol) * 4;

            float rb = blurred.pixels[srcIdx + 0] / 255.0f;
            float rg = blurred.pixels[srcIdx + 1] / 255.0f;
            float rr = blurred.pixels[srcIdx + 2] / 255.0f;

            // Apply chromatic aberration (shift R and B channels slightly)
            if (chromaticAberration > 0) {
                int32_t caOffset = (int32_t)(chromaticAberration * 2);
                int32_t rCol = std::clamp(srcCol + caOffset, 0, rw - 1);
                int32_t bCol = std::clamp(srcCol - caOffset, 0, rw - 1);
                size_t rIdx = ((size_t)srcRow * rw + rCol) * 4;
                size_t bIdx = ((size_t)srcRow * rw + bCol) * 4;
                rr = blurred.pixels[rIdx + 2] / 255.0f;
                rb = blurred.pixels[bIdx + 0] / 255.0f;
            }

            // Apply tint
            rr = rr * (1.0f - tintOpacity) + tintR * tintOpacity;
            rg = rg * (1.0f - tintOpacity) + tintG * tintOpacity;
            rb = rb * (1.0f - tintOpacity) + tintB * tintOpacity;

            // Highlight: bright specular spot based on light direction
            if (highlightBoost > 0) {
                float hlX = nu - lightX, hlY = nv - lightY;
                float hlDist = std::sqrt(hlX * hlX + hlY * hlY);
                float hl = std::max(0.0f, 1.0f - hlDist * 3.0f) * highlightBoost;
                rr = std::min(1.0f, rr + hl);
                rg = std::min(1.0f, rg + hl);
                rb = std::min(1.0f, rb + hl);
            }

            fb_.SetPixel(x0 + col, y0 + row, FloatToU8(rr), FloatToU8(rg), FloatToU8(rb), 255);
        }
    }
    };
    SoftwareWorkerPool* liquidWorkerPool = backend_ ? backend_->GetWorkerPool() : nullptr;
    if (liquidWorkerPool && liquidWorkerPool->CanParallelize() &&
        static_cast<uint64_t>(rw) * rh >= 128u * 1024u) {
        liquidWorkerPool->ParallelFor(0, rh, 8, compositeRows);
    } else {
        compositeRows(0, rh);
    }

    // Step 3: Inner shadow for depth effect
    uint8_t edgeAlpha = FloatToU8(0.15f * currentOpacity_);
    auto shadowRows = [&](int32_t rowBegin, int32_t rowEnd) {
    for (int32_t row = rowBegin; row < rowEnd; row++) {
        for (int32_t col = 0; col < rw; col++) {
            float lx = (float)(x0 + col - ix), ly = (float)(y0 + row - iy);
            if (cr > 0 && !IsInsidePerCornerRoundedRect(lx, ly, w, h, cr, cr, cr, cr))
                continue;

            float edgeDist = std::min({lx, ly, w - lx, h - ly});
            if (edgeDist < 3.0f) {
                uint8_t ea = (uint8_t)(edgeAlpha * (1.0f - edgeDist / 3.0f));
                fb_.BlendPixel(x0 + col, y0 + row, 0, 0, 0, ea);
            }
        }
    }
    };
    if (liquidWorkerPool && liquidWorkerPool->CanParallelize() &&
        static_cast<uint64_t>(rw) * rh >= 128u * 1024u) {
        liquidWorkerPool->ParallelFor(0, rh, 8, shadowRows);
    } else {
        shadowRows(0, rh);
    }

    if (liquidGlassCache_ && !liquidSource.empty() && !liquidKey.empty()) {
        try {
            SoftwareEffectResultCache::Entry entry;
            entry.key = std::move(liquidKey);
            entry.sourcePixels = std::move(liquidSource);
            entry.panelX = x0;
            entry.panelY = y0;
            entry.panelWidth = rw;
            entry.panelHeight = rh;
            entry.outputPixels.resize(static_cast<size_t>(rw) * rh * 4u);
            for (int32_t row = 0; row < rh; ++row) {
                const uint8_t* source = fb_.pixels.data() +
                    (static_cast<size_t>(y0 + row) * fb_.width + x0) * 4u;
                uint8_t* destination = entry.outputPixels.data() +
                    static_cast<size_t>(row) * rw * 4u;
                std::memcpy(destination, source, static_cast<size_t>(rw) * 4u);
            }
            liquidGlassCache_->Store(std::move(entry));
        } catch (const std::bad_alloc&) {
            liquidGlassCache_.reset();
        }
    }
}

// ============================================================================
// SoftwareBackend
// ============================================================================

SoftwareBackend::SoftwareBackend()
{
    workerPool_ = std::make_unique<SoftwareWorkerPool>();
#ifdef JALIUM_HAS_TEXT_ENGINE
    textEngine_ = std::make_unique<TextEngine>();
    if (textEngine_->Initialize() != JALIUM_OK)
        textEngine_.reset();
#endif
}

SoftwareBackend::~SoftwareBackend() = default;

RenderTarget* SoftwareBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    auto* rt = new SoftwareRenderTarget(this, width, height);
#ifdef _WIN32
    rt->hwnd_ = hwnd;
#else
    (void)hwnd;
#endif
    return rt;
}

RenderTarget* SoftwareBackend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height)
{
    return CreateRenderTarget(hwnd, width, height);
}

RenderTarget* SoftwareBackend::CreateRenderTargetForSurface(
    const JaliumSurfaceDescriptor* surface, int32_t width, int32_t height)
{
    auto* rt = new SoftwareRenderTarget(this, width, height);
    if (surface) {
        rt->surfaceDescriptor_ = *surface;
#ifdef _WIN32
        if (surface->platform == JALIUM_PLATFORM_WINDOWS)
            rt->hwnd_ = reinterpret_cast<void*>(surface->handle0);
#endif
#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
        if (surface->platform == JALIUM_PLATFORM_LINUX_WAYLAND &&
            surface->handle0 != 0 && surface->handle1 != 0 &&
            surface->handle2 != 0)
        {
            rt->waylandPresenter_ = WaylandShmPresenter::Create(
                reinterpret_cast<wl_display*>(surface->handle0),
                reinterpret_cast<wl_surface*>(surface->handle1),
                reinterpret_cast<wl_shm*>(surface->handle2));
        }
#endif
    }
    return rt;
}

Brush* SoftwareBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    return new SoftwareSolidBrush(r, g, b, a);
}

Brush* SoftwareBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
{
    return new SoftwareLinearGradientBrush(startX, startY, endX, endY, stops, stopCount,
        spreadMethod <= 2 ? spreadMethod : 0);
}

Brush* SoftwareBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
{
    return new SoftwareRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount,
        spreadMethod <= 2 ? spreadMethod : 0);
}

TextFormat* SoftwareBackend::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
#ifdef JALIUM_HAS_TEXT_ENGINE
    if (textEngine_)
        return textEngine_->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
#endif
    return new SoftwareTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
}

Bitmap* SoftwareBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize)
{
    if (!data || dataSize == 0) return nullptr;

#ifdef _WIN32
    // Use WIC to decode image data
    ComPtr<IWICImagingFactory> wicFactory;
    HRESULT hr = CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&wicFactory));
    if (FAILED(hr) || !wicFactory) return nullptr;

    // SHCreateMemStream copied the caller's const buffer into a private stream.
    // Preserve that ownership contract instead of lending writable access to
    // IWICStream::InitializeFromMemory: CreateStreamOnHGlobal owns this one-shot
    // copy after success and frees it when the final IStream reference is released.
    HGLOBAL streamStorage = GlobalAlloc(GMEM_MOVEABLE, dataSize);
    if (!streamStorage) return nullptr;

    void* streamBytes = GlobalLock(streamStorage);
    if (!streamBytes) {
        GlobalFree(streamStorage);
        return nullptr;
    }
    std::memcpy(streamBytes, data, dataSize);
    GlobalUnlock(streamStorage);

    ComPtr<IStream> stream;
    IStream* rawStream = nullptr;
    hr = CreateStreamOnHGlobal(streamStorage, TRUE, &rawStream);
    if (FAILED(hr)) {
        if (rawStream)
            rawStream->Release();
        else
            GlobalFree(streamStorage);
        return nullptr;
    }
    if (!rawStream) {
        GlobalFree(streamStorage);
        return nullptr;
    }

    // GlobalAlloc may reserve a block larger than the requested byte count.
    // Keep the stream's logical length identical to the caller's encoded data.
    ULARGE_INTEGER streamSize{};
    streamSize.QuadPart = dataSize;
    hr = rawStream->SetSize(streamSize);
    if (FAILED(hr)) {
        rawStream->Release(); // Also frees streamStorage (fDeleteOnRelease=TRUE).
        return nullptr;
    }
    stream.Attach(rawStream);

    ComPtr<IWICBitmapDecoder> decoder;
    hr = wicFactory->CreateDecoderFromStream(
        stream.Get(), nullptr, WICDecodeMetadataCacheOnDemand, &decoder);
    if (FAILED(hr) || !decoder) return nullptr;

    ComPtr<IWICBitmapFrameDecode> frame;
    hr = decoder->GetFrame(0, &frame);
    if (FAILED(hr) || !frame) return nullptr;

    ComPtr<IWICFormatConverter> converter;
    hr = wicFactory->CreateFormatConverter(&converter);
    if (FAILED(hr) || !converter) return nullptr;

    hr = converter->Initialize(
        frame.Get(), GUID_WICPixelFormat32bppBGRA,
        WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    if (FAILED(hr)) return nullptr;

    UINT width = 0, height = 0;
    converter->GetSize(&width, &height);
    if (width == 0 || height == 0) return nullptr;

    std::vector<uint8_t> pixels(width * height * 4);
    hr = converter->CopyPixels(
        nullptr, width * 4, (UINT)pixels.size(), pixels.data());
    if (FAILED(hr)) return nullptr;

    return new SoftwareBitmap(width, height, std::move(pixels));
#elif defined(__linux__) || defined(__ANDROID__)
    // Cross-platform: use stb_image for decoding
    int imgWidth = 0, imgHeight = 0, channels = 0;
    stbi_uc* decoded = stbi_load_from_memory(data, static_cast<int>(dataSize),
        &imgWidth, &imgHeight, &channels, STBI_rgb_alpha);
    if (!decoded || imgWidth <= 0 || imgHeight <= 0) {
        if (decoded) stbi_image_free(decoded);
        return nullptr;
    }

    // Convert RGBA -> BGRA for consistency
    size_t pixelDataSize = static_cast<size_t>(imgWidth) * imgHeight * 4u;
    std::vector<uint8_t> bgraPixels(pixelDataSize);
    for (size_t offset = 0; offset + 3 < pixelDataSize; offset += 4u) {
        bgraPixels[offset + 0] = decoded[offset + 2]; // B
        bgraPixels[offset + 1] = decoded[offset + 1]; // G
        bgraPixels[offset + 2] = decoded[offset + 0]; // R
        bgraPixels[offset + 3] = decoded[offset + 3]; // A
    }
    stbi_image_free(decoded);

    return new SoftwareBitmap(static_cast<uint32_t>(imgWidth), static_cast<uint32_t>(imgHeight),
                              std::move(bgraPixels));
#else
    (void)dataSize;
    return nullptr;
#endif
}

Bitmap* SoftwareBackend::CreateBitmapFromPixels(
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride)
{
    PackedBgraLayout layout{};
    if (!pixels || !TryComputePackedBgraLayout(width, height, stride, layout)) {
        return nullptr;
    }

    std::vector<uint8_t> pixelData(layout.packedBytes);
    for (uint32_t y = 0; y < height; y++) {
        std::memcpy(pixelData.data() + static_cast<size_t>(y) * layout.rowBytes,
                    pixels + static_cast<size_t>(y) * stride,
                    layout.rowBytes);
    }

    return new SoftwareBitmap(width, height, std::move(pixelData));
}

VideoSurface* SoftwareBackend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                  uint32_t /*formatHint*/)
{
    PackedBgraLayout layout{};
    if (!TryComputeTightlyPackedBgraLayout(width, height, layout)) return nullptr;
    return new SoftwareVideoSurface(width, height);
}

IRenderBackend* CreateSoftwareBackend()
{
    return new SoftwareBackend();
}

} // namespace jalium
