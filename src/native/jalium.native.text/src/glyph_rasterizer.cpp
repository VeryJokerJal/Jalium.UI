#include "glyph_rasterizer.h"
#include "font_face.h"

#include "jalium_scanline_rasterizer.h"   // RasterizePathToRects, PixelRect, FillRule

#include <algorithm>
#include <cmath>

namespace jalium {

GlyphRasterizer::GlyphRasterizer()
    : subpixelMode_(SubpixelMode::None)   // grayscale on every platform (LCD deferred)
{
}

GlyphRasterizer::~GlyphRasterizer() = default;

RasterizedGlyph GlyphRasterizer::Rasterize(
    FontFace* face,
    uint32_t glyphIndex,
    float fontSizePx,
    uint8_t subpixelX)
{
    RasterizedGlyph result{};
    if (!face || glyphIndex == 0 || fontSizePx <= 0.0f) return result;

    const float upem = static_cast<float>(face->UnitsPerEm());
    if (upem <= 0.0f) return result;
    const float scale = fontSizePx / upem;

    // Advance is informational here (layout consumes ShapedGlyph::advanceX); set
    // it for completeness at the same unhinted linear scale.
    result.advanceX = face->GetAdvance(static_cast<uint16_t>(glyphIndex)) * scale;

    // Flatten tolerance in font units so on-screen chord error stays ~0.25 px.
    float tolFU = 0.25f / scale;
    if (tolFU < 1.0f) tolFU = 1.0f;

    GlyphOutline gl;
    if (!face->GetGlyphContours(static_cast<uint16_t>(glyphIndex), tolFU, gl) || gl.contours.empty()) {
        // Whitespace / empty glyph: advance is set, but there is no bitmap.
        result.width = 0;
        result.height = 0;
        return result;
    }

    // Transform font-unit, y-up contours into device pixels:
    //   X = x*scale + subpixelX/8    (fractional pen placement)
    //   Y = -y*scale                 (y-up font -> y-down bitmap; baseline at Y=0)
    const float subX = static_cast<float>(subpixelX) * 0.125f;
    float minX = 1e30f, minY = 1e30f, maxX = -1e30f, maxY = -1e30f;
    for (auto& c : gl.contours) {
        for (uint32_t i = 0; i < c.VertexCount(); ++i) {
            float X = c.points[i * 2] * scale + subX;
            float Y = -c.points[i * 2 + 1] * scale;
            c.points[i * 2]     = X;
            c.points[i * 2 + 1] = Y;
            minX = std::min(minX, X); maxX = std::max(maxX, X);
            minY = std::min(minY, Y); maxY = std::max(maxY, Y);
        }
    }
    if (!(maxX > minX) || !(maxY > minY)) { result.width = 0; result.height = 0; return result; }

    // Integer bitmap box with 1px padding so analytic-AA edge feathering (which
    // can spill one pixel past the contour bbox) is never clipped.
    constexpr int PAD = 1;
    int x0 = static_cast<int>(std::floor(minX)) - PAD;
    int y0 = static_cast<int>(std::floor(minY)) - PAD;
    int x1 = static_cast<int>(std::ceil(maxX)) + PAD;
    int y1 = static_cast<int>(std::ceil(maxY)) + PAD;
    // Cap the device box to the atlas bound BEFORE deriving metrics, so bearings,
    // W/H, the translated contours and the rasterized coverage all agree — an
    // oversized glyph is clipped consistently, never misplaced (pixels vs UVs).
    if (x1 - x0 > 4095) x1 = x0 + 4095;
    if (y1 - y0 > 4095) y1 = y0 + 4095;
    int W = x1 - x0, H = y1 - y0;
    if (W <= 0 || H <= 0) { result.width = 0; result.height = 0; return result; }

    // FreeType bitmap_left / bitmap_top semantics (consumed by GenerateGlyphQuads:
    // posX = floor(penX) + offsetX + bearingX; posY = baseline + offsetY - bearingY).
    result.bearingX = x0;       // device X of bitmap column 0 (signed)
    result.bearingY = -y0;      // pixels from baseline up to bitmap row 0 (positive up)
    result.width  = W;
    result.height = H;
    result.hasSubpixel = false;

    // Move contours into 0-based bitmap space.
    for (auto& c : gl.contours)
        for (uint32_t i = 0; i < c.VertexCount(); ++i) {
            c.points[i * 2]     -= static_cast<float>(x0);
            c.points[i * 2 + 1] -= static_cast<float>(y0);
        }

    std::vector<PixelRect> rects;
    RasterizePathToRects(gl.contours, FillRule::NonZero, rects);

    // Composite disjoint coverage rects as premultiplied grayscale (R=G=B=A).
    result.pixels.assign(static_cast<size_t>(W) * H * 4, 0);
    for (const auto& r : rects) {
        int rx0 = r.x, ry0 = r.y, rx1 = r.x + r.w, ry1 = r.y + r.h;
        if (rx0 < 0) rx0 = 0;
        if (ry0 < 0) ry0 = 0;
        if (rx1 > W) rx1 = W;
        if (ry1 > H) ry1 = H;
        float a = r.alpha < 0.f ? 0.f : (r.alpha > 1.f ? 1.f : r.alpha);
        uint8_t v = static_cast<uint8_t>(std::lround(a * 255.0f));
        if (v == 0) continue;
        for (int yy = ry0; yy < ry1; ++yy) {
            uint8_t* px = result.pixels.data() + (static_cast<size_t>(yy) * W + rx0) * 4;
            for (int xx = rx0; xx < rx1; ++xx) {
                px[0] = v; px[1] = v; px[2] = v; px[3] = v;
                px += 4;
            }
        }
    }
    return result;
}

} // namespace jalium
