#include "software_backend.h"
#include "jalium_scanline_rasterizer.h"   // PixelRect / RasterizePathToRects
#include "jalium_impeller_stroke.h"       // ExpandStrokePath (collect-contours mode)
#include "jalium_triangulate.h"           // FlattenPathToContours
#include <algorithm>
#include <cstring>
#include <cmath>
#include <cstdlib>
#include <memory>
#include <mutex>

#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
#include "wayland_shm_present.h"
#include <wayland-client.h>
#endif

#ifdef _WIN32
#include <Windows.h>
#include <wincodec.h>
#include <Shlwapi.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#pragma comment(lib, "Shlwapi.lib")
#endif

#ifdef __APPLE__
#include <CoreGraphics/CoreGraphics.h>
#include <CoreText/CoreText.h>
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

// ============================================================================
// Utility
// ============================================================================

static inline uint8_t FloatToU8(float v) {
    return (uint8_t)(std::clamp(v, 0.0f, 1.0f) * 255.0f + 0.5f);
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
        r = stops.front().r; g = stops.front().g; b = stops.front().b; a = stops.front().a;
        return;
    }
    if (t >= stops.back().position) {
        r = stops.back().r; g = stops.back().g; b = stops.back().b; a = stops.back().a;
        return;
    }

    for (size_t i = 0; i + 1 < stops.size(); i++) {
        if (t >= stops[i].position && t <= stops[i + 1].position) {
            float range = stops[i + 1].position - stops[i].position;
            float local = (range > 0) ? (t - stops[i].position) / range : 0;
            // Interpolate in linear light space for perceptually correct gradients,
            // matching D2D's D2D1_GAMMA_2_2 gradient stop collection behavior.
            float lr0 = SrgbToLinear(stops[i].r), lr1 = SrgbToLinear(stops[i + 1].r);
            float lg0 = SrgbToLinear(stops[i].g), lg1 = SrgbToLinear(stops[i + 1].g);
            float lb0 = SrgbToLinear(stops[i].b), lb1 = SrgbToLinear(stops[i + 1].b);
            r = LinearToSrgb(Lerp(lr0, lr1, local));
            g = LinearToSrgb(Lerp(lg0, lg1, local));
            b = LinearToSrgb(Lerp(lb0, lb1, local));
            a = Lerp(stops[i].a, stops[i + 1].a, local);
            return;
        }
    }
    r = stops.back().r; g = stops.back().g; b = stops.back().b; a = stops.back().a;
}

// ============================================================================
// SoftwareFramebuffer
// ============================================================================

void SoftwareFramebuffer::BlendPixel(int32_t x, int32_t y, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
{
    if (x < 0 || x >= width || y < 0 || y >= height) return;
    size_t idx = (static_cast<size_t>(y) * width + x) * 4;

    if (a == 255) {
        pixels[idx + 0] = b;
        pixels[idx + 1] = g;
        pixels[idx + 2] = r;
        pixels[idx + 3] = a;
        return;
    }
    if (a == 0) return;

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

// ============================================================================
// Brush Sampling
// ============================================================================

void SoftwareLinearGradientBrush::SampleColor(float px, float py,
    float& outR, float& outG, float& outB, float& outA) const
{
    float dx = endX - startX;
    float dy = endY - startY;
    float lenSq = dx * dx + dy * dy;
    float t = (lenSq > 0) ? ((px - startX) * dx + (py - startY) * dy) / lenSq : 0;
    InterpolateGradientStops(stops, t, outR, outG, outB, outA);
}

void SoftwareRadialGradientBrush::SampleColor(float px, float py,
    float& outR, float& outG, float& outB, float& outA) const
{
    float dx = (px - centerX) / (radiusX > 0 ? radiusX : 1);
    float dy = (py - centerY) / (radiusY > 0 ? radiusY : 1);
    float t = std::sqrt(dx * dx + dy * dy);
    InterpolateGradientStops(stops, t, outR, outG, outB, outA);
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
    std::vector<uint8_t> temp(pixels.size());

    auto blurPass = [&](std::vector<uint8_t>& src, std::vector<uint8_t>& dst, bool horizontal) {
        int32_t outerLimit = horizontal ? h : w;
        int32_t innerLimit = horizontal ? w : h;

        for (int32_t outer = 0; outer < outerLimit; outer++) {
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

    // Three-pass box blur (approximates Gaussian)
    for (int pass = 0; pass < 3; pass++) {
        blurPass(pixels, temp, true);   // horizontal
        blurPass(temp, pixels, false);  // vertical
    }
}

void SoftwareRenderTarget::CopyRegion(const SoftwareFramebuffer& src, SoftwareFramebuffer& dst,
    int32_t srcX, int32_t srcY, int32_t w, int32_t h)
{
    dst.Resize(w, h);
    for (int32_t row = 0; row < h; row++) {
        int32_t sy = srcY + row;
        if (sy < 0 || sy >= src.height) continue;
        for (int32_t col = 0; col < w; col++) {
            int32_t sx = srcX + col;
            if (sx < 0 || sx >= src.width) continue;
            size_t srcIdx = ((size_t)sy * src.width + sx) * 4;
            size_t dstIdx = ((size_t)row * w + col) * 4;
            dst.pixels[dstIdx + 0] = src.pixels[srcIdx + 0];
            dst.pixels[dstIdx + 1] = src.pixels[srcIdx + 1];
            dst.pixels[dstIdx + 2] = src.pixels[srcIdx + 2];
            dst.pixels[dstIdx + 3] = src.pixels[srcIdx + 3];
        }
    }
}

void SoftwareRenderTarget::BlitBuffer(const SoftwareFramebuffer& src, int32_t dstX, int32_t dstY, float opacity)
{
    for (int32_t row = 0; row < src.height; row++) {
        int32_t dy = dstY + row;
        if (dy < 0 || dy >= fb_.height) continue;
        for (int32_t col = 0; col < src.width; col++) {
            int32_t dx = dstX + col;
            if (dx < 0 || dx >= fb_.width) continue;
            size_t srcIdx = ((size_t)row * src.width + col) * 4;
            uint8_t sb = src.pixels[srcIdx + 0];
            uint8_t sg = src.pixels[srcIdx + 1];
            uint8_t sr = src.pixels[srcIdx + 2];
            uint8_t sa = (uint8_t)(src.pixels[srcIdx + 3] * opacity);
            if (sa > 0)
                fb_.BlendPixel(dx, dy, sr, sg, sb, sa);
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
    const char* value = std::getenv(name);
    return value && value[0] != '\0' && value[0] != '0';
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

JaliumResult SoftwareRenderTarget::Resize(int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0 || width > INT32_MAX / 4 ||
        static_cast<uint64_t>(width) * static_cast<uint64_t>(height) >
            static_cast<uint64_t>(SIZE_MAX) / 4u)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    try
    {
        fb_.Resize(width, height);
    }
    catch (const std::bad_alloc&)
    {
        return JALIUM_ERROR_OUT_OF_MEMORY;
    }
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
    // Push a root DPI scale transform so all draw calls in DIPs are
    // automatically mapped to physical pixels on high-density displays.
    if (scaleX_ != 1.0f || scaleY_ != 1.0f) {
        SoftwareTransform dpiScale = {{ scaleX_, 0, 0, scaleY_, 0, 0 }};
        float m[6] = { dpiScale.m[0], dpiScale.m[1], dpiScale.m[2],
                        dpiScale.m[3], dpiScale.m[4], dpiScale.m[5] };
        PushTransform(m);
    }
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::EndDraw()
{
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
    // Present to window via GDI
    if (hwnd_) {
        HDC hdc = GetDC((HWND)hwnd_);
        if (hdc) {
            BITMAPINFO bmi{};
            bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
            bmi.bmiHeader.biWidth = width_;
            bmi.bmiHeader.biHeight = -height_; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;

            SetDIBitsToDevice(hdc, 0, 0, width_, height_, 0, 0, 0, height_,
                fb_.pixels.data(), &bmi, DIB_RGB_COLORS);
            ReleaseDC((HWND)hwnd_, hdc);
        }
    }
#else
#ifdef JALIUM_SOFTWARE_WAYLAND_PRESENT
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_LINUX_WAYLAND &&
        surfaceDescriptor_.handle0 != 0 && surfaceDescriptor_.handle1 != 0)
    {
        if (!waylandPresenter_)
            return JALIUM_ERROR_BACKEND_NOT_AVAILABLE;
        const bool presented = waylandPresenter_->Present(
            fb_.pixels.data(), width_, height_, width_ * 4);
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
    // Present to Android ANativeWindow
    LOGI_SW("EndDraw: platform=%d, handle0=%p, fb=%dx%d",
            surfaceDescriptor_.platform, (void*)surfaceDescriptor_.handle0, width_, height_);
    if (surfaceDescriptor_.platform == JALIUM_PLATFORM_ANDROID &&
        surfaceDescriptor_.handle0 != 0)
    {
        ANativeWindow* nativeWindow = reinterpret_cast<ANativeWindow*>(surfaceDescriptor_.handle0);

        ANativeWindow_Buffer buffer;
        int lockResult = ANativeWindow_lock(nativeWindow, &buffer, nullptr);
        LOGI_SW("ANativeWindow_lock result=%d, buffer=%dx%d stride=%d", lockResult, buffer.width, buffer.height, buffer.stride);
        if (lockResult >= 0)
        {
            auto* dst = static_cast<uint8_t*>(buffer.bits);
            const auto* src = fb_.pixels.data();
            int32_t copyHeight = std::min(height_, buffer.height);
            int32_t copyWidth = std::min(width_, buffer.width);
            uint32_t dstStride = buffer.stride * 4; // stride is in pixels

            // BGRA → RGBA channel swap + copy (ANativeWindow uses RGBA)
            for (int32_t y = 0; y < copyHeight; y++)
            {
                const uint8_t* srcRow = src + y * width_ * 4;
                uint8_t* dstRow = dst + y * dstStride;
                for (int32_t x = 0; x < copyWidth; x++)
                {
                    dstRow[x * 4 + 0] = srcRow[x * 4 + 2]; // R ← B
                    dstRow[x * 4 + 1] = srcRow[x * 4 + 1]; // G ← G
                    dstRow[x * 4 + 2] = srcRow[x * 4 + 0]; // B ← R
                    dstRow[x * 4 + 3] = srcRow[x * 4 + 3]; // A ← A
                }
            }

            ANativeWindow_unlockAndPost(nativeWindow);
            LOGI_SW("ANativeWindow_unlockAndPost done");
        }
        else
        {
            LOGE_SW("ANativeWindow_lock failed: %d", lockResult);
        }
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
    if (width_ <= 0 || height_ <= 0 || fb_.pixels.empty())
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

void SoftwareRenderTarget::Clear(float r, float g, float b, float a)
{
    fb_.Clear(FloatToU8(r), FloatToU8(g), FloatToU8(b), FloatToU8(a));
}

bool SoftwareRenderTarget::IsClipped(float px, float py) const
{
    if (clipStack_.empty()) return false;
    // Clip stack uses intersection — only need to check top (already intersected).
    // But rounded-rect clips need per-pixel testing since intersection doesn't shrink radii.
    auto& clip = const_cast<std::stack<SoftwareClipRect>&>(clipStack_).top();
    return !clip.Contains(px, py);
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

    int32_t px0 = std::max(0, (int32_t)std::floor(devOriginX + localMinX));
    int32_t py0 = std::max(0, (int32_t)std::floor(devOriginY + localMinY));
    int32_t px1 = std::min(width_, (int32_t)std::ceil(devOriginX + localMaxX));
    int32_t py1 = std::min(height_, (int32_t)std::ceil(devOriginY + localMaxY));

    for (int32_t py = py0; py < py1; py++) {
        for (int32_t px = px0; px < px1; px++) {
            if (!clipStack_.empty() && IsClipped((float)px + 0.5f, (float)py + 0.5f)) continue;

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
            GetBrushColor(brush, (float)px + 0.5f, (float)py + 0.5f, r, g, b, a);
            if (hits < kSub * kSub) {
                a = (uint8_t)((float)a * ((float)hits * kInvSamples) + 0.5f);
            }
            if (a == 0) continue;
            fb_.BlendPixel(px, py, r, g, b, a);
        }
    }
}

void SoftwareRenderTarget::FillScanlineRect(float x, float y, float w, float h, Brush* brush)
{
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

    int32_t px0 = std::max(0, (int32_t)std::floor(left));
    int32_t py0 = std::max(0, (int32_t)std::floor(top));
    int32_t px1 = std::min(width_, (int32_t)std::ceil(right));
    int32_t py1 = std::min(height_, (int32_t)std::ceil(bottom));

    for (int32_t row = py0; row < py1; row++) {
        float covY = std::min((float)row + 1.0f, bottom) - std::max((float)row, top);
        if (covY <= 0.0f) continue;
        if (covY > 1.0f) covY = 1.0f;
        for (int32_t col = px0; col < px1; col++) {
            if (!clipStack_.empty() && IsClipped((float)col + 0.5f, (float)row + 0.5f)) continue;
            float covX = std::min((float)col + 1.0f, right) - std::max((float)col, left);
            if (covX <= 0.0f) continue;
            if (covX > 1.0f) covX = 1.0f;
            float cov = covX * covY;
            uint8_t r, g, b, a;
            GetBrushColor(brush, (float)col + 0.5f, (float)row + 0.5f, r, g, b, a);
            if (cov < 1.0f) a = (uint8_t)((float)a * cov + 0.5f);
            if (a == 0) continue;
            fb_.BlendPixel(col, row, r, g, b, a);
        }
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

    float segDX = tx2 - tx1, segDY = ty2 - ty1;
    float lenSq = segDX * segDX + segDY * segDY;

    for (int32_t py = py0; py < py1; py++) {
        float fy = (float)py + 0.5f;
        for (int32_t px = px0; px < px1; px++) {
            float fx = (float)px + 0.5f;
            float t = (lenSq > 0.0f)
                ? std::clamp(((fx - tx1) * segDX + (fy - ty1) * segDY) / lenSq, 0.0f, 1.0f)
                : 0.0f;
            float cxp = tx1 + t * segDX, cyp = ty1 + t * segDY;
            float ddx = fx - cxp, ddy = fy - cyp;
            float dist = std::sqrt(ddx * ddx + ddy * ddy);
            float cov = halfW + 0.5f - dist;
            if (cov <= 0.0f) continue;
            if (cov > 1.0f) cov = 1.0f;
            if (!clipStack_.empty() && IsClipped(fx, fy)) continue;
            uint8_t aa = (cov < 1.0f) ? (uint8_t)((float)a * cov + 0.5f) : a;
            if (aa == 0) continue;
            fb_.BlendPixel(px, py, r, g, b, aa);
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
    StrokeScanlineRect(x, y, w, h, brush, strokeWidth);
}

void SoftwareRenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
    if (!brush) return;
    rx = std::min(rx, w * 0.5f);
    ry = std::min(ry, h * 0.5f);

    float tx, ty, tx2, ty2;
    currentTransform_.Apply(x, y, tx, ty);
    currentTransform_.Apply(x + w, y + h, tx2, ty2);
    float tw = tx2 - tx;
    float th = ty2 - ty;
    float sx = (w > 0) ? (tw / w) : 1.0f;
    float sy = (h > 0) ? (th / h) : 1.0f;
    if (tw <= 0.0f || th <= 0.0f) return;
    float trx = rx * sx, try_ = ry * sy;

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

    // Sub-pixel + AA fill of a per-corner rounded rect (float origin, no snap).
    RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
        [tw, th, ttl, ttr, tbr, tbl](float cx, float cy) -> bool {
            return IsInsidePerCornerRoundedRect(cx, cy, tw, th, ttl, ttr, tbr, tbl);
        });
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

    // Sub-pixel + AA stroke ring of a per-corner rounded rect (float origin).
    RasterizeCoverageAA(tx, ty, 0.0f, 0.0f, tw, th, brush,
        [tw, th, ttl, ttr, tbr, tbl, tsw, iTl, iTr, iBr, iBl, innerW, innerH](float cx, float cy) -> bool {
            if (!IsInsidePerCornerRoundedRect(cx, cy, tw, th, ttl, ttr, tbr, tbl)) return false;
            if (innerW > 0.0f && innerH > 0.0f &&
                IsInsidePerCornerRoundedRect(cx - tsw, cy - tsw, innerW, innerH, iTl, iTr, iBr, iBl))
                return false;
            return true;
        });
}

void SoftwareRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
    if (!brush) return;
    float tx, ty, tx2, ty2;
    currentTransform_.Apply(cx, cy, tx, ty);
    currentTransform_.Apply(cx + rx, cy + ry, tx2, ty2);
    float trx = tx2 - tx, try_ = ty2 - ty;
    if (trx <= 0.0f || try_ <= 0.0f) return;

    // Sub-pixel + AA fill: origin is the float ellipse centre; samples are taken
    // relative to it, so an animated ellipse tracks its true position smoothly.
    RasterizeCoverageAA(tx, ty, -trx, -try_, trx, try_, brush,
        [trx, try_](float lx, float ly) -> bool {
            float ex = lx / trx, ey = ly / try_;
            return ex * ex + ey * ey <= 1.0f;
        });
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

    // Sub-pixel + AA stroke ring: covered when inside the outer ellipse AND
    // outside the inner one. Float centre → no whole-pixel snapping when the
    // ellipse (e.g. a Slider thumb ring, Calendar today-ring) is animated.
    RasterizeCoverageAA(tx, ty, -trx, -try_, trx, try_, brush,
        [trx, try_, innerRx, innerRy](float lx, float ly) -> bool {
            float exo = lx / trx, eyo = ly / try_;
            if (exo * exo + eyo * eyo > 1.0f) return false;
            if (innerRx > 0.0f && innerRy > 0.0f) {
                float exi = lx / innerRx, eyi = ly / innerRy;
                if (exi * exi + eyi * eyi <= 1.0f) return false;
            }
            return true;
        });
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
    int32_t ix0 = std::max(0, (int32_t)std::floor(minX));
    int32_t ix1 = std::min(width_, (int32_t)std::ceil(maxX));
    int32_t iy0 = std::max(0, (int32_t)std::floor(minY));
    int32_t iy1 = std::min(height_, (int32_t)std::ceil(maxY));
    if (ix1 <= ix0 || iy1 <= iy0) return;

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

        for (int32_t px = ix0; px < ix1; px++) {
            float c = cov[static_cast<size_t>(px - ix0)];
            if (c <= 0.0f) continue;
            if (!clipStack_.empty() && IsClipped((float)px + 0.5f, (float)row + 0.5f)) continue;
            uint8_t r, g, b, a;
            GetBrushColor(brush, (float)px + 0.5f, (float)row + 0.5f, r, g, b, a);
            if (c < 1.0f) a = (uint8_t)((float)a * c + 0.5f);
            if (a == 0) continue;
            fb_.BlendPixel(px, row, r, g, b, a);
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
    if (!brush) return;
    if (edgeMode < 0) edgeMode = 2;  // Default = Antialiased.

    // Aliased branch: keep the legacy binary scanline (preserves the pixel-art
    // look that some apps may rely on). Plumbed through edgeMode == 1 only.
    if (edgeMode == 1) {
        FillPathAliased(startX, startY, commands, commandLength, brush, fillRule);
        return;
    }

    // Antialiased branch: flatten the command buffer into source-space
    // contours, transform each contour point into device space, then run
    // RasterizePathToRects (the analytic-coverage scanline used by every
    // GPU backend) and blend the resulting rects into the framebuffer.
    std::vector<Contour> contours = FlattenPathToContours(
        startX, startY, commands, commandLength, 0.5f);
    if (contours.empty()) return;

    for (auto& c : contours) {
        for (size_t i = 0; i + 1 < c.points.size(); i += 2) {
            float tx = 0.0f, ty = 0.0f;
            currentTransform_.Apply(c.points[i], c.points[i + 1], tx, ty);
            c.points[i]     = tx;
            c.points[i + 1] = ty;
        }
    }

    FillRule rule = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
    std::vector<PixelRect> rects;
    rects.reserve(256);
    RasterizePathToRects(contours, rule, rects);
    if (rects.empty()) return;

    for (const auto& rect : rects) {
        if (rect.w <= 0 || rect.h <= 0) continue;
        int32_t x0 = std::max(0, rect.x);
        int32_t y0 = std::max(0, rect.y);
        int32_t x1 = std::min(width_,  rect.x + rect.w);
        int32_t y1 = std::min(height_, rect.y + rect.h);
        for (int32_t y = y0; y < y1; ++y) {
            for (int32_t x = x0; x < x1; ++x) {
                if (!clipStack_.empty() && IsClipped((float)x, (float)y)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)x, (float)y, r, g, b, a);
                uint8_t aa = (uint8_t)std::lround(a * rect.alpha);
                fb_.BlendPixel(x, y, r, g, b, aa);
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
    if (!brush) return;
    if (edgeMode < 0) edgeMode = 2;  // Default = Antialiased.

    // Aliased branch: keep the legacy outline-polygon / Bresenham path.
    if (edgeMode == 1) {
        StrokePathAliased(startX, startY, commands, commandLength, brush, strokeWidth, closed, lineJoin, miterLimit, lineCap, dashPattern, dashCount, dashOffset);
        return;
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

    for (const auto& rect : rects) {
        if (rect.w <= 0 || rect.h <= 0) continue;
        int32_t x0 = std::max(0, rect.x);
        int32_t y0 = std::max(0, rect.y);
        int32_t x1 = std::min(width_,  rect.x + rect.w);
        int32_t y1 = std::min(height_, rect.y + rect.h);
        for (int32_t y = y0; y < y1; ++y) {
            for (int32_t x = x0; x < x1; ++x) {
                if (!clipStack_.empty() && IsClipped((float)x, (float)y)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)x, (float)y, r, g, b, a);
                uint8_t aa = (uint8_t)std::lround(a * rect.alpha);
                fb_.BlendPixel(x, y, r, g, b, aa);
            }
        }
    }
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

    // Generate positioned glyph quads via HarfBuzz shaping + FreeType rasterization.
    // When DPI scaling is active, pass the scale so glyphs are rasterized at
    // physical pixel resolution rather than DIP resolution.
    float textRenderScale = (scaleX_ != 1.0f || scaleY_ != 1.0f) ? scaleY_ : 1.0f;
    std::vector<TextGlyphQuad> quads;
    ftFormat->GenerateGlyphQuads(
        text, textLength, w, h,
        brush->r, brush->g, brush->b, textAlpha,
        tx, ty, quads, textRenderScale);

    if (quads.empty()) return;

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

    // (w, h) arrive in DIPs while (tx, ty) is already in physical pixels — the
    // root transform pushed in BeginDraw carries the DPI scale. The blit below
    // copies DIB texels 1:1 onto framebuffer pixels, so the DIB must be sized
    // in physical pixels and the em height scaled the same way, or high-DPI
    // text is rasterized at DIP resolution and truncated. At 96 DPI both
    // factors are 1 and this is byte-identical to the DIP-sized path. The
    // 16384 clamp only guards the "unbounded" 10000-DIP layout fallback from
    // exploding the DIB allocation at high scale factors.
    int32_t pw = std::min((int32_t)std::ceil(w * scaleX_), 16384);
    int32_t ph = std::min((int32_t)std::ceil(h * scaleY_), 16384);
    if (pw <= 0 || ph <= 0) return;

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

        // fontSize is a DIP em size (DirectWrite convention shared by every
        // backend); scale straight to physical pixels. The previous
        // dpiY_ / 72 form treated it as a point size, rendering every glyph
        // 4/3 too large and clipping it against its own line box.
        int fontHeight = -(std::max)(1, (int)(stf->fontSize * scaleY_ + 0.5f));
        HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
            stf->fontWeight, (stf->fontStyle == 1 || stf->fontStyle == 2) ? TRUE : FALSE,
            FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
            stf->fontFamily.c_str());
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        // Always rasterize white-on-black: the DIB is consumed purely as a
        // coverage mask (the blit below turns per-pixel luminance into alpha
        // and applies the brush colour itself). Drawing with the brush colour
        // here made coverage proportional to brightness — black text vanished
        // entirely and dark text turned ghost-translucent.
        SetTextColor(hdc, RGB(255, 255, 255));
        SetBkMode(hdc, TRANSPARENT);

        // DT_EXTERNALLEADING keeps the painted line advance on the same ruler
        // as MeasureText / GetFontMetrics (tmHeight + tmExternalLeading);
        // DT_NOPREFIX keeps literal '&' characters (mnemonics are a managed
        // AccessText concern, not GDI's).
        RECT rc = { 0, 0, (LONG)pw, (LONG)ph };
        UINT dtFlags = DT_WORDBREAK | DT_EXTERNALLEADING | DT_NOPREFIX;
        switch (stf->alignment) {
            case 1: dtFlags |= DT_RIGHT; break;
            case 2: dtFlags |= DT_CENTER; break;
            default: dtFlags |= DT_LEFT; break;
        }

        DrawTextW(hdc, text, textLength, &rc, dtFlags);

        // Copy rendered text to framebuffer with alpha blending. The block is
        // sampled with a fractional (bilinear) phase so an animated text origin
        // moves smoothly sub-pixel instead of snapping to a whole pixel. When
        // (tx,ty) are integer (fracX/fracY == 0) the sampling reduces to the
        // original 1:1 copy, so static text is unchanged.
        uint8_t* textBits = static_cast<uint8_t*>(bits);
        int32_t bw = pw, bh = ph;
        int32_t ix = static_cast<int32_t>(std::floor(tx));
        int32_t iy = static_cast<int32_t>(std::floor(ty));
        float fracX = tx - static_cast<float>(ix);
        float fracY = ty - static_cast<float>(iy);
        auto blockLum = [&](int32_t cc, int32_t rr) -> float {
            if (cc < 0 || cc >= bw || rr < 0 || rr >= bh) return 0.0f;
            int srcIdx = (rr * bw + cc) * 4;
            return (textBits[srcIdx + 2] + textBits[srcIdx + 1] + textBits[srcIdx + 0]) / 3.0f;
        };
        // Iterate only the DIB rows/cols that land inside the framebuffer —
        // the unbounded-layout fallback (w = h = 10000 DIPs) otherwise spins
        // ~10^8 iterations of pure bounds-check misses per DrawText call. The
        // per-pixel guards stay as a defensive backstop; this only trims the
        // loop ranges.
        int32_t rowBegin = (std::max)(0, -iy);
        int32_t rowEnd = (std::min)(bh, height_ - 1 - iy);
        int32_t colBegin = (std::max)(0, -ix);
        int32_t colEnd = (std::min)(bw, width_ - 1 - ix);
        for (int32_t row = rowBegin; row <= rowEnd; row++) {
            int32_t dyy = iy + row;
            if (dyy < 0 || dyy >= height_) continue;
            float sv = static_cast<float>(row) - fracY;
            int32_t sv0 = static_cast<int32_t>(std::floor(sv));
            float fv = sv - static_cast<float>(sv0);
            for (int32_t col = colBegin; col <= colEnd; col++) {
                int32_t dxx = ix + col;
                if (dxx < 0 || dxx >= width_) continue;
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

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        SelectObject(hdc, oldBm);
        DeleteObject(hbm);
    }
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
    if (!clipStack_.empty()) clipStack_.pop();
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
    // Use the smaller of X/Y scale so non-uniform stretch doesn't produce an
    // ellipse-shaped clip — the corners of a Border are conceptually circular.
    float scale = std::min(scaleX_, scaleY_);
    clip.radiusTL = tl * scale;
    clip.radiusTR = tr * scale;
    clip.radiusBR = br * scale;
    clip.radiusBL = bl * scale;
    clip.rx = std::max({ clip.radiusTL, clip.radiusTR, clip.radiusBR, clip.radiusBL });
    clip.ry = clip.rx;
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

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    if (w <= 0.0f || h <= 0.0f) return;

    // Keep the destination origin in float and bilinear-sample the source, so an
    // animated image translates smoothly sub-pixel instead of snapping its
    // top-left to a whole pixel; boundary pixels are weighted by analytic edge
    // coverage. The destination extent stays the caller's w/h (matching the
    // previous behaviour) — only the origin handling changes.
    float left = tx, top = ty, right = tx + w, bottom = ty + h;
    int32_t px0 = std::max(0, (int32_t)std::floor(left));
    int32_t py0 = std::max(0, (int32_t)std::floor(top));
    int32_t px1 = std::min(width_, (int32_t)std::ceil(right));
    int32_t py1 = std::min(height_, (int32_t)std::ceil(bottom));

    const int32_t sw = (int32_t)sb->width_;
    const int32_t shh = (int32_t)sb->height_;
    if (sw <= 0 || shh <= 0) return;
    const float invW = 1.0f / w, invH = 1.0f / h;
    const float globalOpacity = opacity * currentOpacity_;

    for (int32_t dy = py0; dy < py1; dy++) {
        float covY = std::min((float)dy + 1.0f, bottom) - std::max((float)dy, top);
        if (covY <= 0.0f) continue;
        if (covY > 1.0f) covY = 1.0f;
        float v = ((float)dy + 0.5f - top) * invH * (float)shh - 0.5f;
        float v0f = std::floor(v);
        int32_t v0 = std::clamp((int32_t)v0f, 0, shh - 1);
        int32_t v1 = std::clamp((int32_t)v0f + 1, 0, shh - 1);
        float fv = v - v0f;

        for (int32_t dx = px0; dx < px1; dx++) {
            if (!clipStack_.empty() && IsClipped((float)dx + 0.5f, (float)dy + 0.5f)) continue;
            float covX = std::min((float)dx + 1.0f, right) - std::max((float)dx, left);
            if (covX <= 0.0f) continue;
            if (covX > 1.0f) covX = 1.0f;

            float u = ((float)dx + 0.5f - left) * invW * (float)sw - 0.5f;
            float u0f = std::floor(u);
            int32_t u0 = std::clamp((int32_t)u0f, 0, sw - 1);
            int32_t u1 = std::clamp((int32_t)u0f + 1, 0, sw - 1);
            float fu = u - u0f;

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

            float alpha = sa * globalOpacity * covX * covY;
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
    effectCaptureX_ = tx;
    effectCaptureY_ = ty;
    effectCaptureW_ = w;
    effectCaptureH_ = h;

    // Save current framebuffer state
    savedFb_.width = fb_.width;
    savedFb_.height = fb_.height;
    savedFb_.pixels = fb_.pixels;

    // Clear the capture region so we render effect content in isolation
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);
    for (int32_t row = std::max(0, iy); row < std::min(fb_.height, iy + ih); row++) {
        for (int32_t col = std::max(0, ix); col < std::min(fb_.width, ix + iw); col++) {
            fb_.SetPixel(col, row, 0, 0, 0, 0);
        }
    }
    effectCaptureActive_ = true;
}

void SoftwareRenderTarget::EndEffectCapture()
{
    if (!effectCaptureActive_) return;

    // Copy the rendered content from capture region into effectCaptureFb_
    int32_t ix = (int32_t)effectCaptureX_, iy = (int32_t)effectCaptureY_;
    int32_t iw = (int32_t)(effectCaptureW_ + 0.5f), ih = (int32_t)(effectCaptureH_ + 0.5f);
    CopyRegion(fb_, effectCaptureFb_, ix, iy, iw, ih);

    // Restore the original framebuffer
    fb_.pixels = savedFb_.pixels;
    effectCaptureActive_ = false;
}

void SoftwareRenderTarget::DrawBlurEffect(float x, float y, float w, float h, float radius,
    float uvOffsetX, float uvOffsetY)
{
    if (effectCaptureFb_.pixels.empty()) return;

    // Apply blur to the captured content
    SoftwareFramebuffer blurred;
    blurred.width = effectCaptureFb_.width;
    blurred.height = effectCaptureFb_.height;
    blurred.pixels = effectCaptureFb_.pixels;

    if (radius > 0) {
        BoxBlur(blurred.pixels, blurred.width, blurred.height, (int32_t)(radius * 0.5f + 0.5f));
    }

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t dstX = (int32_t)(tx + uvOffsetX);
    int32_t dstY = (int32_t)(ty + uvOffsetY);
    BlitBuffer(blurred, dstX, dstY, currentOpacity_);
}

void SoftwareRenderTarget::DrawDropShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

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
    int32_t dstX = (int32_t)(tx + offsetX + uvOffsetX);
    int32_t dstY = (int32_t)(ty + offsetY + uvOffsetY);
    BlitBuffer(shadow, dstX, dstY, currentOpacity_);

    // Draw original content on top
    int32_t origX = (int32_t)(tx + uvOffsetX);
    int32_t origY = (int32_t)(ty + uvOffsetY);
    BlitBuffer(effectCaptureFb_, origX, origY, currentOpacity_);
}

void SoftwareRenderTarget::DrawOuterGlowEffect(float x, float y, float w, float h,
    float glowSize, float r, float g, float b, float a, float intensity,
    float uvOffsetX, float uvOffsetY,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    // uvOffsetX/uvOffsetY are unused by the software backend: it captures the
    // element into a single framebuffer (effectCaptureFb_) already positioned at
    // the element origin, so there is no atlas-style UV offset to compensate for.
    (void)uvOffsetX; (void)uvOffsetY;
    if (effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    // Expand the buffer for glow spread
    int32_t expand = (int32_t)(glowSize + 0.5f);
    int32_t gw = iw + expand * 2;
    int32_t gh = ih + expand * 2;

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
    int32_t dstX = (int32_t)tx - expand;
    int32_t dstY = (int32_t)ty - expand;
    BlitBuffer(glow, dstX, dstY, currentOpacity_);

    // Draw original content on top
    BlitBuffer(effectCaptureFb_, (int32_t)tx, (int32_t)ty, currentOpacity_);
}

void SoftwareRenderTarget::DrawInnerShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    if (effectCaptureFb_.pixels.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t iw = effectCaptureFb_.width;
    int32_t ih = effectCaptureFb_.height;

    // Draw original content first
    BlitBuffer(effectCaptureFb_, (int32_t)tx, (int32_t)ty, currentOpacity_);

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
        int32_t dy = (int32_t)ty + row;
        if (dy < 0 || dy >= fb_.height) continue;
        for (int32_t col = 0; col < iw; col++) {
            int32_t dx = (int32_t)tx + col;
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
}

void SoftwareRenderTarget::DrawColorMatrixEffect(float x, float y, float w, float h,
    const float* matrix)
{
    if (effectCaptureFb_.pixels.empty() || !matrix) return;

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

    BlitBuffer(result, (int32_t)tx, (int32_t)ty, currentOpacity_);
}

void SoftwareRenderTarget::DrawEmbossEffect(float x, float y, float w, float h,
    float amount, float lightDirX, float lightDirY, float relief)
{
    if (effectCaptureFb_.pixels.empty()) return;

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

    BlitBuffer(result, (int32_t)tx, (int32_t)ty, currentOpacity_);
}

void SoftwareRenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    // Custom shaders cannot be executed in software — just blit the captured content as-is
    if (effectCaptureFb_.pixels.empty()) return;
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    BlitBuffer(effectCaptureFb_, (int32_t)tx, (int32_t)ty, currentOpacity_);
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
    if (blurRadius > 0) {
        BoxBlur(blurred.pixels, rw, rh, (int32_t)(blurRadius * 0.5f + 0.5f));
    }

    // Step 2: Composite: blurred background + refraction distortion + tint + highlight
    for (int32_t row = 0; row < rh; row++) {
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

    // Step 3: Inner shadow for depth effect
    uint8_t edgeAlpha = FloatToU8(0.15f * currentOpacity_);
    for (int32_t row = 0; row < rh; row++) {
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
}

// ============================================================================
// SoftwareBackend
// ============================================================================

SoftwareBackend::SoftwareBackend()
{
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
    uint32_t /*spreadMethod*/)
{
    return new SoftwareLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* SoftwareBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new SoftwareRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
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

    ComPtr<IStream> stream;
    stream.Attach(SHCreateMemStream(data, dataSize));
    if (!stream) return nullptr;

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
    if (!pixels || width == 0 || height == 0) return nullptr;

    std::vector<uint8_t> pixelData(width * height * 4);
    for (uint32_t y = 0; y < height; y++) {
        std::memcpy(pixelData.data() + y * width * 4,
                    pixels + y * stride,
                    width * 4);
    }

    return new SoftwareBitmap(width, height, std::move(pixelData));
}

VideoSurface* SoftwareBackend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                  uint32_t /*formatHint*/)
{
    if (width == 0 || height == 0) return nullptr;
    return new SoftwareVideoSurface(width, height);
}

IRenderBackend* CreateSoftwareBackend()
{
    return new SoftwareBackend();
}

} // namespace jalium
