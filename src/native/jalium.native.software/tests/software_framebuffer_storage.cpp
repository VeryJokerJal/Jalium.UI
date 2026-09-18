#include "jalium_api.h"

#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <vector>

extern "C" void jalium_software_init();

namespace {

constexpr int32_t kWidth = 784;
constexpr int32_t kHeight = 592;
constexpr uint64_t kMinimumReleasedBytes = 1024u * 1024u;
constexpr const char* kAllocationFailureVariable =
    "JALIUM_SOFTWARE_TEST_FAIL_FRAMEBUFFER_ALLOCATION";

constexpr float ByteValue(uint8_t value)
{
    return static_cast<float>(value) / 255.0f;
}

bool SetEnvironmentValue(const char* name, const char* value)
{
#ifdef _WIN32
    return _putenv_s(name, value ? value : "") == 0;
#else
    return value ? setenv(name, value, 1) == 0 : unsetenv(name) == 0;
#endif
}

class ScopedEnvironmentVariable {
public:
    ScopedEnvironmentVariable(const char* name, const char* value)
        : name_(name)
    {
        const char* previous = std::getenv(name);
        if (previous) {
            hadPrevious_ = true;
            previous_ = previous;
        }
        valid_ = SetEnvironmentValue(name, value);
    }

    ~ScopedEnvironmentVariable()
    {
        if (hadPrevious_)
            (void)SetEnvironmentValue(name_.c_str(), previous_.c_str());
        else
            (void)SetEnvironmentValue(name_.c_str(), nullptr);
    }

    bool IsValid() const { return valid_; }

private:
    std::string name_;
    std::string previous_;
    bool hadPrevious_ = false;
    bool valid_ = false;
};

struct ContextDeleter {
    void operator()(JaliumContext* context) const
    {
        jalium_context_destroy(context);
    }
};

struct RenderTargetDeleter {
    void operator()(JaliumRenderTarget* target) const
    {
        jalium_render_target_destroy(target);
    }
};

struct BrushDeleter {
    void operator()(JaliumBrush* brush) const
    {
        jalium_brush_destroy(brush);
    }
};

using ContextPtr = std::unique_ptr<JaliumContext, ContextDeleter>;
using RenderTargetPtr = std::unique_ptr<JaliumRenderTarget, RenderTargetDeleter>;
using BrushPtr = std::unique_ptr<JaliumBrush, BrushDeleter>;

struct PixelCapture {
    int32_t width = 0;
    int32_t height = 0;
    std::vector<uint8_t> pixels;
};

bool Expect(bool condition, const char* message)
{
    if (!condition)
        std::cerr << "  " << message << '\n';
    return condition;
}

bool ExpectResult(
    JaliumResult actual,
    JaliumResult expected,
    const char* stage)
{
    if (actual == expected) return true;
    std::cerr << "  " << stage << " returned " << static_cast<int>(actual)
              << ", expected " << static_cast<int>(expected) << '\n';
    return false;
}

RenderTargetPtr CreateTarget(
    JaliumContext* context,
    int32_t width = kWidth,
    int32_t height = kHeight)
{
    JaliumSurfaceDescriptor surface{};
    surface.platform = JALIUM_PLATFORM_UNKNOWN;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = 1;
    return RenderTargetPtr(jalium_render_target_create_for_surface(
        context, &surface, width, height));
}

bool QueryOwnedBytes(JaliumRenderTarget* target, uint64_t& bytes)
{
    bytes = 0;
    const JaliumResult result =
        jalium_render_target_query_main_framebuffer_owned_bytes(target, &bytes);
    if (result == JALIUM_OK) return true;
    std::cerr << "  owned-byte query failed: " << static_cast<int>(result) << '\n';
    return false;
}

bool DrawScene(
    JaliumContext* context,
    JaliumRenderTarget* target,
    int32_t topRows)
{
    BrushPtr topBrush(jalium_brush_create_solid(
        context, ByteValue(221), ByteValue(47), ByteValue(103), 1.0f));
    if (!topBrush) return Expect(false, "failed to create scene brush");
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK,
            "scene BeginDraw")) {
        return false;
    }

    // Non-opaque alpha is intentional: compact storage must preserve and compare
    // all four BGRA bytes rather than assuming an opaque window background.
    jalium_render_target_clear(
        target, ByteValue(17), ByteValue(83), ByteValue(191), ByteValue(64));
    if (topRows > 0) {
        jalium_draw_fill_rectangle(
            target, 0.0f, 0.0f,
            static_cast<float>(kWidth), static_cast<float>(topRows),
            topBrush.get());
    }
    return ExpectResult(
        jalium_render_target_end_draw(target), JALIUM_OK,
        "scene EndDraw");
}

bool DrawPatch(
    JaliumContext* context,
    JaliumRenderTarget* target)
{
    constexpr float x = 17.0f;
    constexpr float y = 401.0f;
    constexpr float width = 29.0f;
    constexpr float height = 23.0f;
    BrushPtr patchBrush(jalium_brush_create_solid(
        context, ByteValue(31), ByteValue(211), ByteValue(79), 1.0f));
    if (!patchBrush) return Expect(false, "failed to create patch brush");

    jalium_render_target_add_dirty_rect(target, x, y, width, height);
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK,
            "patch BeginDraw")) {
        return false;
    }
    jalium_draw_fill_rectangle(
        target, x, y, width, height, patchBrush.get());
    return ExpectResult(
        jalium_render_target_end_draw(target), JALIUM_OK,
        "patch EndDraw");
}

bool DrawResizePattern(
    JaliumContext* context,
    JaliumRenderTarget* target,
    int32_t width,
    int32_t height)
{
    BrushPtr accent(jalium_brush_create_solid(
        context, ByteValue(231), ByteValue(71), ByteValue(19), 1.0f));
    if (!accent) return Expect(false, "failed to create resize-pattern brush");
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK,
            "resize-pattern BeginDraw")) {
        return false;
    }
    jalium_render_target_clear(
        target, ByteValue(29), ByteValue(107), ByteValue(173), ByteValue(191));
    jalium_draw_fill_rectangle(
        target, 0.0f, 0.0f,
        static_cast<float>(std::max(1, width / 2)),
        static_cast<float>(height), accent.get());
    return ExpectResult(
        jalium_render_target_end_draw(target), JALIUM_OK,
        "resize-pattern EndDraw");
}

bool DrawTailMarker(
    JaliumContext* context,
    JaliumRenderTarget* target,
    int32_t width,
    int32_t height)
{
    BrushPtr marker(jalium_brush_create_solid(
        context, ByteValue(7), ByteValue(241), ByteValue(113), 1.0f));
    if (!marker) return Expect(false, "failed to create tail-marker brush");
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK,
            "tail-marker BeginDraw")) {
        return false;
    }
    jalium_draw_fill_rectangle(
        target,
        static_cast<float>(width - 1),
        static_cast<float>(height - 1),
        1.0f, 1.0f, marker.get());
    return ExpectResult(
        jalium_render_target_end_draw(target), JALIUM_OK,
        "tail-marker EndDraw");
}

bool FetchReadyReadback(JaliumRenderTarget* target, PixelCapture& capture)
{
    capture = {};
    if (!ExpectResult(
            jalium_render_target_fetch_readback(
                target, nullptr, 0, &capture.width, &capture.height),
            JALIUM_OK,
            "readback size query")) {
        return false;
    }
    if (capture.width <= 0 || capture.height <= 0)
        return Expect(false, "readback returned invalid dimensions");

    const size_t rowBytes = static_cast<size_t>(capture.width) * 4u;
    const size_t totalBytes = rowBytes * static_cast<size_t>(capture.height);
    capture.pixels.resize(totalBytes);
    return ExpectResult(
        jalium_render_target_fetch_readback(
            target, capture.pixels.data(), static_cast<uint32_t>(rowBytes),
            &capture.width, &capture.height),
        JALIUM_OK,
        "readback fetch");
}

bool CaptureCurrentFrame(JaliumRenderTarget* target, PixelCapture& capture)
{
    if (!ExpectResult(
            jalium_render_target_request_readback(target), JALIUM_OK,
            "request readback")) {
        return false;
    }
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK,
            "readback BeginDraw")) {
        return false;
    }
    if (!ExpectResult(
            jalium_render_target_end_draw(target), JALIUM_OK,
            "readback EndDraw")) {
        return false;
    }
    return FetchReadyReadback(target, capture);
}

bool ExpectPixelsEqual(
    const PixelCapture& expected,
    const PixelCapture& actual,
    const char* stage)
{
    if (expected.width != actual.width || expected.height != actual.height) {
        std::cerr << "  " << stage << " dimensions differ: "
                  << expected.width << 'x' << expected.height << " vs "
                  << actual.width << 'x' << actual.height << '\n';
        return false;
    }
    if (expected.pixels == actual.pixels) return true;

    const size_t limit = std::min(expected.pixels.size(), actual.pixels.size());
    size_t mismatch = 0;
    while (mismatch < limit &&
           expected.pixels[mismatch] == actual.pixels[mismatch]) {
        ++mismatch;
    }
    std::cerr << "  " << stage << " differs at byte " << mismatch;
    if (mismatch < limit) {
        std::cerr << ": expected " << static_cast<int>(expected.pixels[mismatch])
                  << ", actual " << static_cast<int>(actual.pixels[mismatch]);
    }
    std::cerr << '\n';
    return false;
}

bool ArmAllocationFailure()
{
    return SetEnvironmentValue(kAllocationFailureVariable, "1");
}

void ClearAllocationFailure()
{
    (void)SetEnvironmentValue(kAllocationFailureVariable, nullptr);
}

bool TestThresholdAndByteExactRestoration(JaliumContext* context)
{
    // 334 bottom rows save 1,047,424 bytes at width 784: 1,152 bytes below
    // the one-MiB commit threshold, so storage must remain dense.
    auto belowThreshold = CreateTarget(context);
    if (!belowThreshold || !DrawScene(context, belowThreshold.get(), 258))
        return Expect(false, "failed to prepare below-threshold target");
    uint64_t belowBefore = 0;
    uint64_t belowAfter = 0;
    if (!QueryOwnedBytes(belowThreshold.get(), belowBefore) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                belowThreshold.get()),
            JALIUM_OK,
            "below-threshold compact") ||
        !QueryOwnedBytes(belowThreshold.get(), belowAfter) ||
        !Expect(belowBefore == belowAfter,
            "below-threshold frame released storage")) {
        return false;
    }

    // 335 rows save 1,050,560 bytes: 1,984 bytes above the threshold.
    auto aboveThreshold = CreateTarget(context);
    if (!aboveThreshold || !DrawScene(context, aboveThreshold.get(), 257))
        return Expect(false, "failed to prepare above-threshold target");
    PixelCapture expected;
    if (!CaptureCurrentFrame(aboveThreshold.get(), expected)) return false;

    const size_t bottomPixel =
        (static_cast<size_t>(kHeight - 1) * kWidth) * 4u;
    if (!Expect(expected.pixels[bottomPixel + 3] != 0 &&
                expected.pixels[bottomPixel + 3] != 255,
            "test scene did not retain a non-opaque alpha byte")) {
        return false;
    }

    uint64_t denseBytes = 0;
    uint64_t compactBytes = 0;
    if (!QueryOwnedBytes(aboveThreshold.get(), denseBytes) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                aboveThreshold.get()),
            JALIUM_OK,
            "above-threshold compact") ||
        !QueryOwnedBytes(aboveThreshold.get(), compactBytes) ||
        !Expect(denseBytes >= compactBytes + kMinimumReleasedBytes,
            "above-threshold frame did not release one MiB")) {
        return false;
    }
    std::cout << "owned-bytes threshold-case: dense=" << denseBytes
              << " compact=" << compactBytes
              << " released=" << (denseBytes - compactBytes) << '\n';

    if (!ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                aboveThreshold.get()),
            JALIUM_OK,
            "repeated compact")) {
        return false;
    }
    uint64_t repeatedBytes = 0;
    if (!QueryOwnedBytes(aboveThreshold.get(), repeatedBytes) ||
        !Expect(repeatedBytes == compactBytes,
            "repeated compact changed owned storage")) {
        return false;
    }

    PixelCapture restored;
    if (!CaptureCurrentFrame(aboveThreshold.get(), restored) ||
        !ExpectPixelsEqual(expected, restored, "materialized frame")) {
        return false;
    }

    auto typicalFrame = CreateTarget(context);
    if (!typicalFrame || !DrawScene(context, typicalFrame.get(), 32))
        return Expect(false, "failed to prepare 32-row synthetic frame");
    uint64_t typicalDenseBytes = 0;
    uint64_t typicalCompactBytes = 0;
    if (!QueryOwnedBytes(typicalFrame.get(), typicalDenseBytes) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                typicalFrame.get()),
            JALIUM_OK,
            "32-row synthetic compact") ||
        !QueryOwnedBytes(typicalFrame.get(), typicalCompactBytes) ||
        !Expect(typicalDenseBytes >=
                typicalCompactBytes + kMinimumReleasedBytes,
            "32-row synthetic frame did not compact")) {
        return false;
    }
    std::cout << "owned-bytes 32-row-synthetic: dense="
              << typicalDenseBytes
              << " compact=" << typicalCompactBytes
              << " released="
              << (typicalDenseBytes - typicalCompactBytes) << '\n';
    return true;
}

bool TestAlphaParticipatesInSuffixScan(JaliumContext* context)
{
    auto target = CreateTarget(context);
    if (!target)
        return Expect(false, "failed to create alpha-scan target");

    BrushPtr topBrush(jalium_brush_create_solid(
        context, ByteValue(221), ByteValue(47), ByteValue(103), 1.0f));
    if (!topBrush ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "alpha scene BeginDraw")) {
        return false;
    }
    // Use black for the suffix RGB so compositing another black source changes
    // only alpha even after straight-alpha round trips.
    jalium_render_target_clear(
        target.get(), 0.0f, 0.0f, 0.0f, ByteValue(64));
    jalium_draw_fill_rectangle(
        target.get(), 0.0f, 0.0f,
        static_cast<float>(kWidth), 257.0f, topBrush.get());
    if (!ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "alpha scene EndDraw")) {
        return false;
    }

    // Composite the same RGB over only the final row with a different alpha.
    // The resulting adjacent rows have byte-identical BGR and different A. If
    // suffix scanning ignored alpha, this frame would incorrectly compact.
    BrushPtr alphaBrush(jalium_brush_create_solid(
        context, 0.0f, 0.0f, 0.0f, ByteValue(128)));
    if (!alphaBrush) return Expect(false, "failed to create alpha brush");
    if (!ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "alpha BeginDraw")) {
        return false;
    }
    jalium_draw_fill_rectangle(
        target.get(), 0.0f, static_cast<float>(kHeight - 1),
        static_cast<float>(kWidth), 1.0f, alphaBrush.get());
    if (!ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "alpha EndDraw")) {
        return false;
    }

    PixelCapture pixels;
    if (!CaptureCurrentFrame(target.get(), pixels)) return false;
    const size_t previousRow =
        (static_cast<size_t>(kHeight - 2) * kWidth) * 4u;
    const size_t finalRow =
        (static_cast<size_t>(kHeight - 1) * kWidth) * 4u;
    if (!Expect(
            std::memcmp(
                pixels.pixels.data() + previousRow,
                pixels.pixels.data() + finalRow,
                3u) == 0,
            "alpha-scan rows do not share the same BGR bytes") ||
        !Expect(
            pixels.pixels[previousRow + 3] != pixels.pixels[finalRow + 3],
            "alpha-scan rows do not differ in alpha")) {
        return false;
    }

    uint64_t before = 0;
    uint64_t after = 0;
    return QueryOwnedBytes(target.get(), before) &&
        ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_OK,
            "alpha-sensitive compact") &&
        QueryOwnedBytes(target.get(), after) &&
        Expect(before == after,
            "suffix scan ignored the alpha-byte difference");
}

bool TestPartialDirtyAndReadbackAfterCompact(JaliumContext* context)
{
    auto compactTarget = CreateTarget(context);
    auto referenceTarget = CreateTarget(context);
    if (!compactTarget || !referenceTarget ||
        !DrawScene(context, compactTarget.get(), 257) ||
        !DrawScene(context, referenceTarget.get(), 257)) {
        return Expect(false, "failed to prepare partial-dirty targets");
    }

    uint64_t before = 0;
    uint64_t compact = 0;
    if (!QueryOwnedBytes(compactTarget.get(), before) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                compactTarget.get()),
            JALIUM_OK,
            "dirty compact") ||
        !QueryOwnedBytes(compactTarget.get(), compact) ||
        !Expect(before >= compact + kMinimumReleasedBytes,
            "dirty test target did not compact")) {
        return false;
    }

    if (!DrawPatch(context, compactTarget.get()) ||
        !DrawPatch(context, referenceTarget.get())) {
        return false;
    }

    PixelCapture expected;
    PixelCapture actual;
    return CaptureCurrentFrame(referenceTarget.get(), expected) &&
        CaptureCurrentFrame(compactTarget.get(), actual) &&
        ExpectPixelsEqual(expected, actual, "partial dirty/readback frame");
}

bool TestDrawingCaptureAndReadbackGates(JaliumContext* context)
{
    auto target = CreateTarget(context);
    if (!target || !DrawScene(context, target.get(), 257))
        return Expect(false, "failed to prepare gate target");
    PixelCapture expected;
    if (!CaptureCurrentFrame(target.get(), expected)) return false;

    if (!ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "gate BeginDraw") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_ERROR_INVALID_STATE,
            "compact while drawing") ||
        !ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "gate EndDraw")) {
        return false;
    }

    // Leave a capture scope open across EndDraw. Compact must reject the idle
    // target until the next BeginDraw's existing recovery path restores it.
    if (!ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "capture BeginDraw")) {
        return false;
    }
    jalium_effect_begin_capture(target.get(), 5.0f, 7.0f, 32.0f, 24.0f);
    if (!ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "capture abandoned EndDraw") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_ERROR_INVALID_STATE,
            "compact with abandoned capture") ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "capture recovery BeginDraw") ||
        !ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "capture recovery EndDraw")) {
        return false;
    }

    PixelCapture recovered;
    if (!CaptureCurrentFrame(target.get(), recovered) ||
        !ExpectPixelsEqual(expected, recovered, "capture recovery frame")) {
        return false;
    }

    // Preserve the pre-existing native recovery contract as well: a second
    // BeginDraw can recover an abandoned Begin/effect-capture pair even when no
    // intervening EndDraw cleared the new storage-only drawing marker.
    if (!ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "re-entry first BeginDraw")) {
        return false;
    }
    jalium_effect_begin_capture(target.get(), 9.0f, 11.0f, 17.0f, 13.0f);
    if (!ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "re-entry recovery BeginDraw") ||
        !ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "re-entry recovery EndDraw")) {
        return false;
    }
    PixelCapture reentryRecovered;
    if (!CaptureCurrentFrame(target.get(), reentryRecovered) ||
        !ExpectPixelsEqual(expected, reentryRecovered, "re-entry recovery frame")) {
        return false;
    }

    // Pending readback is also a capture. Compact must leave the dense frame
    // alone until the armed EndDraw has copied it.
    if (!ExpectResult(
            jalium_render_target_request_readback(target.get()), JALIUM_OK,
            "gate request readback") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_ERROR_INVALID_STATE,
            "compact with pending readback") ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "pending readback BeginDraw") ||
        !ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "pending readback EndDraw")) {
        return false;
    }
    PixelCapture pendingCapture;
    if (!FetchReadyReadback(target.get(), pendingCapture) ||
        !ExpectPixelsEqual(expected, pendingCapture, "pending readback frame")) {
        return false;
    }

    uint64_t before = 0;
    uint64_t after = 0;
    return QueryOwnedBytes(target.get(), before) &&
        ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_OK,
            "compact after capture recovery") &&
        QueryOwnedBytes(target.get(), after) &&
        Expect(before >= after + kMinimumReleasedBytes,
            "target did not compact after capture recovery");
}

bool TestPendingReadbackResizeCompatibility(JaliumContext* context)
{
    auto target = CreateTarget(context);
    if (!target || !DrawScene(context, target.get(), 257))
        return Expect(false, "failed to prepare pending-readback resize target");

    // RequestReadback historically arms the next completed frame. A resize in
    // between is allowed, and that next frame is captured at the new dimensions.
    if (!ExpectResult(
            jalium_render_target_request_readback(target.get()), JALIUM_OK,
            "resize request readback") ||
        !ExpectResult(
            jalium_render_target_resize(target.get(), 640, 480), JALIUM_OK,
            "resize with pending readback") ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "resized readback BeginDraw") ||
        !ExpectResult(
            jalium_render_target_end_draw(target.get()), JALIUM_OK,
            "resized readback EndDraw")) {
        return false;
    }

    PixelCapture capture;
    return FetchReadyReadback(target.get(), capture) &&
        Expect(capture.width == 640 && capture.height == 480,
            "pending readback did not capture resized dimensions");
}

bool TestRetainedAndTransitionCaptureGates(JaliumContext* context)
{
    auto retainedTarget = CreateTarget(context);
    if (!retainedTarget || !DrawScene(context, retainedTarget.get(), 257))
        return Expect(false, "failed to prepare retained-capture target");

    if (!ExpectResult(
            jalium_render_target_begin_draw(retainedTarget.get()), JALIUM_OK,
            "retained BeginDraw")) {
        return false;
    }
    void* layer = jalium_render_target_realize_layer_begin(
        retainedTarget.get(), nullptr, 8.0f, 8.0f, 48.0f, 40.0f);
    if (!Expect(layer != nullptr, "retained capture did not begin") ||
        !ExpectResult(
            jalium_render_target_end_draw(retainedTarget.get()), JALIUM_OK,
            "retained abandoned EndDraw") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                retainedTarget.get()),
            JALIUM_ERROR_INVALID_STATE,
            "compact with retained capture") ||
        !ExpectResult(
            jalium_render_target_begin_draw(retainedTarget.get()), JALIUM_OK,
            "retained recovery BeginDraw") ||
        !ExpectResult(
            jalium_render_target_end_draw(retainedTarget.get()), JALIUM_OK,
            "retained recovery EndDraw")) {
        return false;
    }
    jalium_render_target_destroy_retained_layer(retainedTarget.get(), layer);

    auto transitionTarget = CreateTarget(context);
    if (!transitionTarget || !DrawScene(context, transitionTarget.get(), 257))
        return Expect(false, "failed to prepare transition-capture target");
    if (!ExpectResult(
            jalium_render_target_begin_draw(transitionTarget.get()), JALIUM_OK,
            "transition BeginDraw")) {
        return false;
    }
    jalium_transition_begin_capture(
        transitionTarget.get(), 0, 4.0f, 6.0f, 36.0f, 28.0f);
    if (!ExpectResult(
            jalium_render_target_end_draw(transitionTarget.get()), JALIUM_OK,
            "transition abandoned EndDraw") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                transitionTarget.get()),
            JALIUM_ERROR_INVALID_STATE,
            "compact with transition capture") ||
        !ExpectResult(
            jalium_render_target_begin_draw(transitionTarget.get()), JALIUM_OK,
            "transition cleanup BeginDraw")) {
        return false;
    }
    jalium_transition_end_capture(transitionTarget.get(), 0);
    return ExpectResult(
        jalium_render_target_end_draw(transitionTarget.get()), JALIUM_OK,
        "transition cleanup EndDraw");
}

bool TestResizeCapacityReuseAndIdleRelease(JaliumContext* context)
{
    constexpr int32_t kInitialWidth = 8;
    constexpr int32_t kInitialHeight = 2;
    constexpr int32_t kShrinkWidth = 8;
    constexpr int32_t kShrinkHeight = 1;
    constexpr uint64_t kGrowthSlackLimit = 16u * 1024u * 1024u;
    constexpr uint64_t kGrowthAlignmentAllowance = 64u * 1024u;

    auto target = CreateTarget(context, kInitialWidth, kInitialHeight);
    if (!target || !DrawResizePattern(
            context, target.get(), kInitialWidth, kInitialHeight)) {
        return Expect(false, "failed to prepare resize-capacity target");
    }

    PixelCapture original;
    uint64_t initialCapacity = 0;
    if (!CaptureCurrentFrame(target.get(), original) ||
        !QueryOwnedBytes(target.get(), initialCapacity) ||
        !Expect(initialCapacity >= original.pixels.size(),
            "initial owned bytes are smaller than the logical framebuffer") ||
        !Expect(initialCapacity / 4u + 1u <=
                static_cast<uint64_t>(std::numeric_limits<int32_t>::max() / 4),
            "initial capacity is too large for the focused growth test")) {
        return false;
    }

    const int32_t growWidth =
        static_cast<int32_t>(initialCapacity / 4u + 1u);
    const int32_t growHeight = 1;
    const uint64_t growBytes =
        static_cast<uint64_t>(growWidth) * growHeight * 4u;
    if (!ExpectResult(
            jalium_render_target_resize(
                target.get(), growWidth, growHeight),
            JALIUM_OK,
            "growth resize")) {
        return false;
    }

    uint64_t grownCapacity = 0;
    PixelCapture grown;
    if (!QueryOwnedBytes(target.get(), grownCapacity) ||
        !Expect(grownCapacity >= growBytes,
            "growth resize capacity is smaller than its logical bytes") ||
        !Expect(grownCapacity > growBytes,
            "growth resize did not retain geometric headroom") ||
        !Expect(grownCapacity <=
                growBytes + kGrowthSlackLimit + kGrowthAlignmentAllowance,
            "growth resize exceeded the bounded-capacity allowance") ||
        !CaptureCurrentFrame(target.get(), grown) ||
        !Expect(grown.width == growWidth && grown.height == growHeight,
            "growth resize returned the wrong dimensions") ||
        !Expect(grown.pixels.size() >= original.pixels.size(),
            "growth resize unexpectedly shortened the logical byte prefix") ||
        !Expect(std::memcmp(
                grown.pixels.data(), original.pixels.data(),
                original.pixels.size()) == 0,
            "growth resize did not preserve the linear byte prefix") ||
        !Expect(std::all_of(
                grown.pixels.begin() +
                    static_cast<std::ptrdiff_t>(original.pixels.size()),
                grown.pixels.end(),
                [](uint8_t value) { return value == 0; }),
            "growth resize did not zero the new byte tail")) {
        return false;
    }

    if (!Expect(ArmAllocationFailure(),
            "failed to arm in-capacity resize allocation trap")) {
        return false;
    }
    for (int iteration = 0; iteration < 256; ++iteration) {
        if (!ExpectResult(
                jalium_render_target_resize(
                    target.get(), kShrinkWidth, kShrinkHeight),
                JALIUM_OK,
                "in-capacity shrink") ||
            !ExpectResult(
                jalium_render_target_resize(
                    target.get(), growWidth, growHeight),
                JALIUM_OK,
                "in-capacity regrow")) {
            std::cerr << "  resize oscillation failed at iteration "
                      << iteration << '\n';
            ClearAllocationFailure();
            return false;
        }
    }
    uint64_t reusedCapacity = 0;
    PixelCapture regrown;
    const size_t shrinkBytes =
        static_cast<size_t>(kShrinkWidth) * kShrinkHeight * 4u;
    if (!QueryOwnedBytes(target.get(), reusedCapacity) ||
        !Expect(reusedCapacity == grownCapacity,
            "in-capacity oscillation changed owned capacity") ||
        !CaptureCurrentFrame(target.get(), regrown) ||
        !Expect(std::memcmp(
                regrown.pixels.data(), original.pixels.data(), shrinkBytes) == 0,
            "in-capacity regrow did not preserve the surviving prefix") ||
        !Expect(std::all_of(
                regrown.pixels.begin() + static_cast<std::ptrdiff_t>(shrinkBytes),
                regrown.pixels.end(),
                [](uint8_t value) { return value == 0; }),
            "in-capacity regrow did not zero the reconstructed tail")) {
        ClearAllocationFailure();
        return false;
    }

    if (!Expect(grownCapacity / 4u + 1u <=
            static_cast<uint64_t>(std::numeric_limits<int32_t>::max() / 4),
            "grown capacity is too large for the allocation-failure probe")) {
        ClearAllocationFailure();
        return false;
    }
    const int32_t overCapacityWidth =
        static_cast<int32_t>(grownCapacity / 4u + 1u);
    const JaliumResult overCapacityResult = jalium_render_target_resize(
        target.get(), overCapacityWidth, 1);
    ClearAllocationFailure();

    uint64_t afterFailureCapacity = 0;
    PixelCapture afterFailure;
    if (!ExpectResult(
            overCapacityResult, JALIUM_ERROR_OUT_OF_MEMORY,
            "over-capacity allocation trap") ||
        !QueryOwnedBytes(target.get(), afterFailureCapacity) ||
        !Expect(afterFailureCapacity == grownCapacity,
            "failed over-capacity resize changed owned capacity") ||
        !CaptureCurrentFrame(target.get(), afterFailure) ||
        !ExpectPixelsEqual(regrown, afterFailure,
            "failed over-capacity resize frame")) {
        return false;
    }

    if (!DrawTailMarker(
            context, target.get(), growWidth, growHeight)) {
        return false;
    }
    PixelCapture beforeCompactPixels;
    uint64_t beforeCompactBytes = 0;
    uint64_t afterCompactBytes = 0;
    if (!CaptureCurrentFrame(target.get(), beforeCompactPixels) ||
        !QueryOwnedBytes(target.get(), beforeCompactBytes) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(target.get()),
            JALIUM_OK,
            "capacity-release compact") ||
        !QueryOwnedBytes(target.get(), afterCompactBytes) ||
        !Expect(afterCompactBytes >= growBytes,
            "capacity-release compact discarded logical dense bytes") ||
        !Expect(afterCompactBytes < beforeCompactBytes,
            "capacity-release compact retained geometric headroom")) {
        return false;
    }

    PixelCapture afterCompactPixels;
    if (!CaptureCurrentFrame(target.get(), afterCompactPixels) ||
        !ExpectPixelsEqual(
            beforeCompactPixels, afterCompactPixels,
            "capacity-release compact frame")) {
        return false;
    }

    std::cout << "resize-capacity: logical=" << growBytes
              << " retained=" << grownCapacity
              << " compacted=" << afterCompactBytes << '\n';
    return true;
}

bool RealizeNestedLayers(
    JaliumRenderTarget* target,
    void*& outerLayer,
    void*& innerLayer,
    const char* stage)
{
    outerLayer = nullptr;
    innerLayer = nullptr;
    if (!ExpectResult(
            jalium_render_target_begin_draw(target), JALIUM_OK, stage)) {
        return false;
    }

    outerLayer = jalium_render_target_realize_layer_begin(
        target, nullptr, 8.0f, 8.0f, 64.0f, 48.0f);
    if (!outerLayer) {
        (void)jalium_render_target_end_draw(target);
        return Expect(false, "outer retained capture did not begin");
    }

    innerLayer = jalium_render_target_realize_layer_begin(
        target, nullptr, 12.0f, 12.0f, 24.0f, 16.0f);
    if (!innerLayer) {
        jalium_render_target_realize_layer_end(target, outerLayer);
        (void)jalium_render_target_end_draw(target);
        jalium_render_target_destroy_retained_layer(target, outerLayer);
        outerLayer = nullptr;
        return Expect(false, "inner retained capture did not begin");
    }

    jalium_render_target_realize_layer_end(target, innerLayer);
    jalium_render_target_realize_layer_end(target, outerLayer);
    if (!ExpectResult(
            jalium_render_target_end_draw(target), JALIUM_OK, stage)) {
        jalium_render_target_destroy_retained_layer(target, innerLayer);
        jalium_render_target_destroy_retained_layer(target, outerLayer);
        innerLayer = nullptr;
        outerLayer = nullptr;
        return false;
    }
    return true;
}

bool TestRetainedCaptureBufferReuse(JaliumContext* context)
{
    constexpr int32_t kCaptureWidth = 320;
    constexpr int32_t kCaptureHeight = 240;
    auto target = CreateTarget(context, kCaptureWidth, kCaptureHeight);
    if (!target)
        return Expect(false, "failed to create retained-buffer target");

    void* warmOuter = nullptr;
    void* warmInner = nullptr;
    if (!RealizeNestedLayers(
            target.get(), warmOuter, warmInner,
            "warm retained capture")) {
        return false;
    }
    jalium_render_target_destroy_retained_layer(target.get(), warmInner);
    jalium_render_target_destroy_retained_layer(target.get(), warmOuter);

    if (!Expect(ArmAllocationFailure(),
            "failed to arm retained-buffer reuse allocation trap")) {
        return false;
    }
    for (int iteration = 0; iteration < 32; ++iteration) {
        void* reusedOuter = nullptr;
        void* reusedInner = nullptr;
        if (!RealizeNestedLayers(
                target.get(), reusedOuter, reusedInner,
                "reused retained capture")) {
            std::cerr << "  retained capture reuse failed at iteration "
                      << iteration << '\n';
            ClearAllocationFailure();
            return false;
        }
        jalium_render_target_destroy_retained_layer(
            target.get(), reusedInner);
        jalium_render_target_destroy_retained_layer(
            target.get(), reusedOuter);
    }

    uint64_t mainCapacity = 0;
    if (!QueryOwnedBytes(target.get(), mainCapacity) ||
        !Expect(mainCapacity / 4u + 1u <=
            static_cast<uint64_t>(std::numeric_limits<int32_t>::max() / 4),
            "retained-buffer target is too large for the allocation trap")) {
        ClearAllocationFailure();
        return false;
    }

    const JaliumResult allocationTrapResult = jalium_render_target_resize(
        target.get(), static_cast<int32_t>(mainCapacity / 4u + 1u), 1);
    ClearAllocationFailure();
    if (!ExpectResult(
            allocationTrapResult, JALIUM_ERROR_OUT_OF_MEMORY,
            "retained-buffer reuse allocation trap")) {
        return false;
    }

    if (!ExpectResult(
            jalium_render_target_reclaim_idle_resources(target.get()),
            JALIUM_OK,
            "retained-buffer idle reclaim") ||
        !Expect(ArmAllocationFailure(),
            "failed to arm retained-buffer reclaim allocation trap") ||
        !ExpectResult(
            jalium_render_target_begin_draw(target.get()), JALIUM_OK,
            "retained-buffer reclaim BeginDraw")) {
        ClearAllocationFailure();
        return false;
    }

    void* reclaimedLayer = jalium_render_target_realize_layer_begin(
        target.get(), nullptr, 8.0f, 8.0f, 64.0f, 48.0f);
    if (reclaimedLayer)
        jalium_render_target_realize_layer_end(target.get(), reclaimedLayer);
    const JaliumResult reclaimedEndResult =
        jalium_render_target_end_draw(target.get());
    ClearAllocationFailure();
    if (reclaimedLayer)
        jalium_render_target_destroy_retained_layer(target.get(), reclaimedLayer);
    if (!Expect(reclaimedLayer == nullptr,
            "idle reclaim did not release the retained capture buffer") ||
        !ExpectResult(
            reclaimedEndResult, JALIUM_OK,
            "retained-buffer reclaim EndDraw")) {
        return false;
    }

    void* recoveredOuter = nullptr;
    void* recoveredInner = nullptr;
    if (!RealizeNestedLayers(
            target.get(), recoveredOuter, recoveredInner,
            "retained capture after reclaim")) {
        return false;
    }
    jalium_render_target_destroy_retained_layer(target.get(), recoveredInner);
    jalium_render_target_destroy_retained_layer(target.get(), recoveredOuter);
    std::cout << "retained-capture buffers: nested reuse and idle release verified\n";
    return true;
}

bool TestStrongCommitAllocationFailures(JaliumContext* context)
{
    // Candidate-prefix allocation failure must leave the dense frame untouched.
    auto compactFailure = CreateTarget(context);
    if (!compactFailure || !DrawScene(context, compactFailure.get(), 257))
        return Expect(false, "failed to prepare compact-OOM target");
    PixelCapture compactExpected;
    uint64_t compactBefore = 0;
    if (!CaptureCurrentFrame(compactFailure.get(), compactExpected) ||
        !QueryOwnedBytes(compactFailure.get(), compactBefore) ||
        !Expect(ArmAllocationFailure(), "failed to arm compact OOM")) {
        return false;
    }
    const JaliumResult compactResult =
        jalium_render_target_compact_idle_framebuffer_storage(compactFailure.get());
    ClearAllocationFailure();
    uint64_t compactAfter = 0;
    PixelCapture compactActual;
    if (!ExpectResult(compactResult, JALIUM_ERROR_OUT_OF_MEMORY, "compact OOM") ||
        !QueryOwnedBytes(compactFailure.get(), compactAfter) ||
        !Expect(compactBefore == compactAfter,
            "compact OOM changed owned storage") ||
        !CaptureCurrentFrame(compactFailure.get(), compactActual) ||
        !ExpectPixelsEqual(compactExpected, compactActual, "compact OOM frame")) {
        return false;
    }

    // Dense materialization failure must preserve the compact representation.
    auto materializeFailure = CreateTarget(context);
    if (!materializeFailure || !DrawScene(context, materializeFailure.get(), 257))
        return Expect(false, "failed to prepare materialize-OOM target");
    PixelCapture materializeExpected;
    if (!CaptureCurrentFrame(materializeFailure.get(), materializeExpected) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                materializeFailure.get()),
            JALIUM_OK,
            "materialize test compact")) {
        return false;
    }
    uint64_t materializeCompactBytes = 0;
    if (!QueryOwnedBytes(materializeFailure.get(), materializeCompactBytes) ||
        !Expect(ArmAllocationFailure(), "failed to arm materialize OOM")) {
        return false;
    }
    const JaliumResult beginResult =
        jalium_render_target_begin_draw(materializeFailure.get());
    ClearAllocationFailure();
    uint64_t materializeAfterFailure = 0;
    PixelCapture materializeActual;
    if (!ExpectResult(beginResult, JALIUM_ERROR_OUT_OF_MEMORY, "materialize OOM") ||
        !QueryOwnedBytes(materializeFailure.get(), materializeAfterFailure) ||
        !Expect(materializeCompactBytes == materializeAfterFailure,
            "materialize OOM changed compact storage") ||
        !CaptureCurrentFrame(materializeFailure.get(), materializeActual) ||
        !ExpectPixelsEqual(
            materializeExpected, materializeActual, "materialize OOM frame")) {
        return false;
    }

    // Resize allocates a complete candidate before mutating either dense or
    // compact state. Its OOM path must retain old pixels and dimensions, then a
    // later successful resize must work directly from compact storage.
    auto resizeFailure = CreateTarget(context);
    if (!resizeFailure || !DrawScene(context, resizeFailure.get(), 257))
        return Expect(false, "failed to prepare resize-OOM target");
    PixelCapture resizeExpected;
    if (!CaptureCurrentFrame(resizeFailure.get(), resizeExpected) ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                resizeFailure.get()),
            JALIUM_OK,
            "resize test compact")) {
        return false;
    }
    uint64_t resizeCompactBytes = 0;
    if (!QueryOwnedBytes(resizeFailure.get(), resizeCompactBytes) ||
        !Expect(ArmAllocationFailure(), "failed to arm resize OOM")) {
        return false;
    }
    const JaliumResult resizeResult =
        jalium_render_target_resize(resizeFailure.get(), 640, 480);
    ClearAllocationFailure();
    uint64_t resizeAfterFailure = 0;
    PixelCapture resizeActual;
    if (!ExpectResult(resizeResult, JALIUM_ERROR_OUT_OF_MEMORY, "resize OOM") ||
        !QueryOwnedBytes(resizeFailure.get(), resizeAfterFailure) ||
        !Expect(resizeCompactBytes == resizeAfterFailure,
            "resize OOM changed compact storage") ||
        !CaptureCurrentFrame(resizeFailure.get(), resizeActual) ||
        !ExpectPixelsEqual(resizeExpected, resizeActual, "resize OOM frame") ||
        !ExpectResult(
            jalium_render_target_compact_idle_framebuffer_storage(
                resizeFailure.get()),
            JALIUM_OK,
            "resize recompact") ||
        !ExpectResult(
            jalium_render_target_resize(resizeFailure.get(), 640, 480),
            JALIUM_OK,
            "resize after OOM")) {
        return false;
    }

    PixelCapture resized;
    if (!CaptureCurrentFrame(resizeFailure.get(), resized) ||
        !Expect(resized.width == 640 && resized.height == 480,
            "successful compact resize returned wrong dimensions")) {
        return false;
    }
    uint64_t resizedBytes = 0;
    return QueryOwnedBytes(resizeFailure.get(), resizedBytes) &&
        Expect(resizedBytes >= 640u * 480u * 4u,
            "successful resize did not retain a full dense framebuffer");
}

} // namespace

int main(int argc, char** argv)
{
    const bool productionSmoke = argc == 2 &&
        std::strcmp(argv[1], "--production-smoke") == 0;
    if (argc > 1 && !productionSmoke) {
        std::cerr << "usage: software_framebuffer_storage "
                     "[--production-smoke]\n";
        return 2;
    }

    ScopedEnvironmentVariable backendOverride(
        "JALIUM_RENDER_BACKEND", "software");
    ScopedEnvironmentVariable threadOverride(
        "JALIUM_SOFTWARE_THREADS", "1");
    ScopedEnvironmentVariable failureOverride(
        kAllocationFailureVariable, nullptr);
    if (!backendOverride.IsValid() || !threadOverride.IsValid() ||
        !failureOverride.IsValid()) {
        std::cerr << "FAIL: could not configure software framebuffer tests\n";
        return 1;
    }

    jalium_software_init();
    ContextPtr context(jalium_context_create(JALIUM_BACKEND_SOFTWARE));
    if (!context ||
        jalium_context_get_backend(context.get()) != JALIUM_BACKEND_SOFTWARE) {
        std::cerr << "FAIL: could not create software context\n";
        return 1;
    }

    if (!TestThresholdAndByteExactRestoration(context.get())) {
        std::cerr << "FAIL: threshold or byte-exact restoration\n";
        return 1;
    }
    if (!TestAlphaParticipatesInSuffixScan(context.get())) {
        std::cerr << "FAIL: alpha-sensitive suffix scan\n";
        return 1;
    }
    if (!TestPartialDirtyAndReadbackAfterCompact(context.get())) {
        std::cerr << "FAIL: partial dirty/readback after compact\n";
        return 1;
    }
    if (!TestDrawingCaptureAndReadbackGates(context.get())) {
        std::cerr << "FAIL: drawing/capture/readback gates\n";
        return 1;
    }
    if (!TestPendingReadbackResizeCompatibility(context.get())) {
        std::cerr << "FAIL: pending readback resize compatibility\n";
        return 1;
    }
    if (!TestRetainedAndTransitionCaptureGates(context.get())) {
        std::cerr << "FAIL: retained/transition capture gates\n";
        return 1;
    }
    if (!productionSmoke &&
        !TestResizeCapacityReuseAndIdleRelease(context.get())) {
        std::cerr << "FAIL: resize capacity reuse or idle release\n";
        return 1;
    }
    if (!productionSmoke &&
        !TestRetainedCaptureBufferReuse(context.get())) {
        std::cerr << "FAIL: retained capture buffer reuse\n";
        return 1;
    }
    if (!productionSmoke &&
        !TestStrongCommitAllocationFailures(context.get())) {
        std::cerr << "FAIL: framebuffer strong-commit OOM behavior\n";
        return 1;
    }

    std::cout << "PASS: software framebuffer storage compaction\n";
    return 0;
}
