#include "software_backend.h"
#include "jalium_text_options.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <memory>
#include <vector>

namespace {

struct RenderResult {
    bool hasInk = false;
    bool hasColoredFringe = false;
    bool hasIntermediateGray = false;
};

RenderResult RenderMode(int32_t mode)
{
    jalium::SoftwareBackend backend;
    std::unique_ptr<jalium::RenderTarget> target(backend.CreateRenderTarget(nullptr, 96, 64));
    std::unique_ptr<jalium::TextFormat> format(backend.CreateTextFormat(L"DejaVu Sans", 25.0f, 400, 0));
    std::unique_ptr<jalium::Brush> brush(backend.CreateSolidBrush(0.0f, 0.0f, 0.0f, 1.0f));
    if (!target || !format || !brush) return {};
    format->SetTextRenderingMode(mode);
    target->BeginDraw();
    target->Clear(1, 1, 1, 1);
    target->RenderText(L"A", 1, format.get(), 8, 8, 64, 48, brush.get());
    target->EndDraw();

    auto* software = dynamic_cast<jalium::SoftwareRenderTarget*>(target.get());
    if (!software) return {};
    const auto& framebuffer = software->GetFramebuffer();
    RenderResult result;
    for (size_t i = 0; i + 3 < framebuffer.pixels.size(); i += 4) {
        const uint8_t blue = framebuffer.pixels[i];
        const uint8_t green = framebuffer.pixels[i + 1];
        const uint8_t red = framebuffer.pixels[i + 2];
        if (red != 255 || green != 255 || blue != 255) result.hasInk = true;
        if (red != green || green != blue) result.hasColoredFringe = true;
        if (red == green && green == blue && red > 0 && red < 255) result.hasIntermediateGray = true;
    }
    return result;
}

bool TestReadbackContract()
{
    jalium::SoftwareBackend backend;
    std::unique_ptr<jalium::RenderTarget> target(
        backend.CreateRenderTarget(nullptr, 8, 4));
    if (!target) return false;

    if (target->RequestReadback() != JALIUM_OK) return false;
    if (target->BeginDraw() != JALIUM_OK) return false;
    target->Clear(0.25f, 0.5f, 0.75f, 1.0f);
    if (target->EndDraw() != JALIUM_OK) return false;

    int32_t width = 0, height = 0;
    if (target->FetchReadback(nullptr, 0, &width, &height) != JALIUM_OK ||
        width != 8 || height != 4)
        return false;

    std::vector<uint8_t> shortBuffer(8u * 4u * 4u);
    if (target->FetchReadback(shortBuffer.data(), 8u * 4u - 1u,
            &width, &height) != JALIUM_ERROR_INVALID_ARGUMENT)
        return false;

    const uint32_t stride = 8u * 4u + 7u;
    std::vector<uint8_t> pixels(static_cast<size_t>(stride) * 4u, 0xCD);
    if (target->FetchReadback(pixels.data(), stride, &width, &height) != JALIUM_OK ||
        width != 8 || height != 4)
        return false;

    // SoftwareFramebuffer is top-down BGRA8. FloatToU8 rounds to nearest.
    for (int y = 0; y < height; ++y) {
        for (int x = 0; x < width; ++x) {
            const size_t i = static_cast<size_t>(y) * stride + static_cast<size_t>(x) * 4u;
            if (pixels[i] != 191 || pixels[i + 1] != 128 ||
                pixels[i + 2] != 64 || pixels[i + 3] != 255)
                return false;
        }
        for (uint32_t p = 8u * 4u; p < stride; ++p) {
            if (pixels[static_cast<size_t>(y) * stride + p] != 0xCD)
                return false;
        }
    }

    // A resize after capture must not change the completed capture's size.
    if (target->Resize(3, 2) != JALIUM_OK) return false;
    width = height = 0;
    if (target->FetchReadback(nullptr, 0, &width, &height) != JALIUM_OK ||
        width != 8 || height != 4)
        return false;

    // The next request replaces it only when the next EndDraw completes.
    if (target->RequestReadback() != JALIUM_OK ||
        target->BeginDraw() != JALIUM_OK)
        return false;
    target->Clear(1.0f, 0.0f, 0.0f, 1.0f);
    if (target->EndDraw() != JALIUM_OK) return false;
    if (target->FetchReadback(nullptr, 0, &width, &height) != JALIUM_OK ||
        width != 3 || height != 2)
        return false;
    return true;
}

bool TestBitmapDestinationUsesDpiTransform()
{
    constexpr int32_t framebufferWidth = 48;
    constexpr int32_t framebufferHeight = 36;

    jalium::SoftwareBackend backend;
    std::unique_ptr<jalium::RenderTarget> target(
        backend.CreateRenderTarget(nullptr, framebufferWidth, framebufferHeight));
    const uint8_t sourcePixels[] = {
        0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF,
        0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF
    };
    std::unique_ptr<jalium::Bitmap> bitmap(
        backend.CreateBitmapFromPixels(sourcePixels, 2, 2, 2 * 4));
    if (!target || !bitmap) return false;

    target->SetDpi(288.0f, 288.0f);
    if (target->BeginDraw() != JALIUM_OK) return false;
    target->Clear(0, 0, 0, 0);
    target->DrawBitmap(bitmap.get(), 2.0f, 3.0f, 10.0f, 6.0f, 1.0f);
    if (target->EndDraw() != JALIUM_OK) return false;

    auto* software = dynamic_cast<jalium::SoftwareRenderTarget*>(target.get());
    if (!software) return false;
    const auto& framebuffer = software->GetFramebuffer();
    auto alphaAt = [&](int32_t px, int32_t py) {
        return framebuffer.pixels[
            (static_cast<size_t>(py) * framebuffer.width + px) * 4u + 3u];
    };

    // The 10x6 DIP destination starts at (2,3). At 3x DPI it must cover the
    // physical rectangle [6,36) x [9,27), not the old 10x6-pixel rectangle.
    return alphaAt(6, 9) == 255 &&
           alphaAt(35, 26) == 255 &&
           alphaAt(36, 20) == 0 &&
           alphaAt(20, 27) == 0;
}

bool TestDashedStrokeUsesAnalyticCoverage()
{
    jalium::SoftwareBackend backend;
    std::unique_ptr<jalium::RenderTarget> target(
        backend.CreateRenderTarget(nullptr, 80, 40));
    std::unique_ptr<jalium::Brush> brush(
        backend.CreateSolidBrush(0.0f, 0.0f, 0.0f, 1.0f));
    if (!target || !brush || target->BeginDraw() != JALIUM_OK) return false;

    target->Clear(0, 0, 0, 0);
    const float commands[] = { 0.0f, 72.0f, 28.0f }; // LineTo
    const float dashes[] = { 6.0f, 8.0f };
    target->StrokePath(8.0f, 8.0f, commands, 3, brush.get(), 3.0f,
        false, 0, 10.0f, 0, dashes, 2, 0.0f, 2);
    if (target->EndDraw() != JALIUM_OK) return false;

    auto* software = dynamic_cast<jalium::SoftwareRenderTarget*>(target.get());
    if (!software) return false;
    const auto& framebuffer = software->GetFramebuffer();

    int partialCoverage = 0;
    for (size_t i = 3; i < framebuffer.pixels.size(); i += 4) {
        const uint8_t alpha = framebuffer.pixels[i];
        if (alpha > 0 && alpha < 255) ++partialCoverage;
    }
    if (partialCoverage < 4) return false; // aliased fallback has no coverage ramp

    auto maxAlphaNearArcLength = [&](float distance) {
        constexpr float x0 = 8.0f, y0 = 8.0f;
        constexpr float dx = 64.0f, dy = 20.0f;
        const float length = std::sqrt(dx * dx + dy * dy);
        const int cx = static_cast<int>(std::lround(x0 + dx * distance / length));
        const int cy = static_cast<int>(std::lround(y0 + dy * distance / length));
        uint8_t maximum = 0;
        for (int y = std::max(0, cy - 1); y <= std::min(framebuffer.height - 1, cy + 1); ++y) {
            for (int x = std::max(0, cx - 1); x <= std::min(framebuffer.width - 1, cx + 1); ++x) {
                maximum = std::max(maximum,
                    framebuffer.pixels[(static_cast<size_t>(y) * framebuffer.width + x) * 4u + 3u]);
            }
        }
        return maximum;
    };

    // First 6 source units are on; the following 8 are off. Sample well away
    // from both butt caps so feather coverage cannot bridge the expected gap.
    return maxAlphaNearArcLength(3.0f) > 128 &&
           maxAlphaNearArcLength(10.0f) < 32 &&
           maxAlphaNearArcLength(17.0f) > 128;
}

} // namespace

int main()
{
    const RenderResult aliased = RenderMode(JALIUM_TEXT_AA_ALIASED);
    const RenderResult grayscale = RenderMode(JALIUM_TEXT_AA_GRAYSCALE);
    const RenderResult clearType = RenderMode(JALIUM_TEXT_AA_CLEARTYPE);
    if (!aliased.hasInk || !grayscale.hasInk || !clearType.hasInk) {
        std::cerr << "FAIL: one or more software AA modes emitted no text pixels\n";
        return 1;
    }
    if (aliased.hasColoredFringe || grayscale.hasColoredFringe) {
        std::cerr << "FAIL: aliased/grayscale software text produced RGB fringe\n";
        return 1;
    }
    if (!grayscale.hasIntermediateGray) {
        std::cerr << "FAIL: grayscale software text lacks partial coverage\n";
        return 1;
    }
    if (!clearType.hasColoredFringe) {
        std::cerr << "FAIL: ClearType software text was not composited per RGB channel\n";
        return 1;
    }
    if (!TestReadbackContract()) {
        std::cerr << "FAIL: software two-phase BGRA readback contract\n";
        return 1;
    }
    if (!TestBitmapDestinationUsesDpiTransform()) {
        std::cerr << "FAIL: software bitmap destination ignored DPI transform\n";
        return 1;
    }
    if (!TestDashedStrokeUsesAnalyticCoverage()) {
        std::cerr << "FAIL: dashed software stroke did not preserve analytic AA/gaps\n";
        return 1;
    }
    std::cout << "PASS: software text AA, frame readback, DPI bitmap transform, and dashed analytic stroke\n";
    return 0;
}
