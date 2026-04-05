#include "software_backend.h"
#include <algorithm>
#include <cstring>
#include <cmath>
#include <cstdlib>

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
    // Use GDI for accurate text measurement
    HDC hdc = CreateCompatibleDC(nullptr);
    if (hdc) {
        int fontHeight = -(int)(fontSize * 96.0f / 72.0f);
        HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
            fontWeight, (fontStyle == 1 || fontStyle == 2) ? TRUE : FALSE,
            FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
            fontFamily.c_str());
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        RECT rc = { 0, 0, maxWidth > 0 ? (LONG)maxWidth : 10000, maxHeight > 0 ? (LONG)maxHeight : 10000 };
        UINT dtFlags = DT_CALCRECT | DT_WORDBREAK;
        DrawTextW(hdc, text, textLength, &rc, dtFlags);

        TEXTMETRICW tm;
        GetTextMetricsW(hdc, &tm);

        metrics->width = (float)(rc.right - rc.left);
        metrics->height = (float)(rc.bottom - rc.top);
        metrics->lineHeight = (float)tm.tmHeight;
        metrics->baseline = (float)tm.tmAscent;
        metrics->ascent = (float)tm.tmAscent;
        metrics->descent = (float)tm.tmDescent;
        metrics->lineGap = (float)tm.tmExternalLeading;
        metrics->lineCount = (metrics->lineHeight > 0) ? (uint32_t)(metrics->height / metrics->lineHeight) : 1;
        if (metrics->lineCount == 0) metrics->lineCount = 1;

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        DeleteDC(hdc);
        return JALIUM_OK;
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

SoftwareRenderTarget::SoftwareRenderTarget(int32_t width, int32_t height)
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
    width_ = width;
    height_ = height;
    fb_.Resize(width, height);
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::BeginDraw()
{
    return JALIUM_OK;
}

JaliumResult SoftwareRenderTarget::EndDraw()
{
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
#endif
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

void SoftwareRenderTarget::FillScanlineRect(float x, float y, float w, float h, Brush* brush)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    int32_t ix = (int32_t)tx;
    int32_t iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f);
    int32_t ih = (int32_t)(h + 0.5f);

    for (int32_t row = iy; row < iy + ih; row++) {
        for (int32_t col = ix; col < ix + iw; col++) {
            if (!clipStack_.empty() && IsClipped((float)col, (float)row)) continue;
            uint8_t r, g, b, a;
            GetBrushColor(brush, (float)col, (float)row, r, g, b, a);
            fb_.BlendPixel(col, row, r, g, b, a);
        }
    }
}

void SoftwareRenderTarget::StrokeScanlineRect(float x, float y, float w, float h, Brush* brush, float strokeWidth)
{
    // Top edge
    FillScanlineRect(x, y, w, strokeWidth, brush);
    // Bottom edge
    FillScanlineRect(x, y + h - strokeWidth, w, strokeWidth, brush);
    // Left edge
    FillScanlineRect(x, y + strokeWidth, strokeWidth, h - strokeWidth * 2, brush);
    // Right edge
    FillScanlineRect(x + w - strokeWidth, y + strokeWidth, strokeWidth, h - strokeWidth * 2, brush);
}

void SoftwareRenderTarget::DrawBresenhamLine(float x1, float y1, float x2, float y2,
    uint8_t r, uint8_t g, uint8_t b, uint8_t a, float strokeWidth)
{
    float tx1, ty1, tx2, ty2;
    currentTransform_.Apply(x1, y1, tx1, ty1);
    currentTransform_.Apply(x2, y2, tx2, ty2);

    int32_t ix1 = (int32_t)tx1, iy1 = (int32_t)ty1;
    int32_t ix2 = (int32_t)tx2, iy2 = (int32_t)ty2;

    int32_t dx = std::abs(ix2 - ix1);
    int32_t dy = std::abs(iy2 - iy1);
    int32_t sx = ix1 < ix2 ? 1 : -1;
    int32_t sy = iy1 < iy2 ? 1 : -1;
    int32_t err = dx - dy;

    int32_t halfStroke = std::max(0, (int32_t)(strokeWidth * 0.5f));

    while (true) {
        for (int32_t dy2 = -halfStroke; dy2 <= halfStroke; dy2++) {
            for (int32_t dx2 = -halfStroke; dx2 <= halfStroke; dx2++) {
                if (!clipStack_.empty() && IsClipped((float)(ix1 + dx2), (float)(iy1 + dy2))) continue;
                fb_.BlendPixel(ix1 + dx2, iy1 + dy2, r, g, b, a);
            }
        }

        if (ix1 == ix2 && iy1 == iy2) break;
        int32_t e2 = 2 * err;
        if (e2 > -dy) { err -= dy; ix1 += sx; }
        if (e2 < dx) { err += dx; iy1 += sy; }
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

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            float px = (float)col, py = (float)row;
            // Check if inside rounded rect
            float cx = px, cy = py;
            bool inside = true;
            // Check corners
            if (cx < rx && cy < ry) {
                float dx = (cx - rx) / rx;
                float dy = (cy - ry) / ry;
                inside = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx > w - rx && cy < ry) {
                float dx = (cx - (w - rx)) / rx;
                float dy = (cy - ry) / ry;
                inside = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx < rx && cy > h - ry) {
                float dx = (cx - rx) / rx;
                float dy = (cy - (h - ry)) / ry;
                inside = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx > w - rx && cy > h - ry) {
                float dx = (cx - (w - rx)) / rx;
                float dy = (cy - (h - ry)) / ry;
                inside = (dx * dx + dy * dy) <= 1.0f;
            }

            if (inside) {
                int32_t fx = ix + col, fy = iy + row;
                if (!clipStack_.empty() && IsClipped((float)fx, (float)fy)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)fx, (float)fy, r, g, b, a);
                fb_.BlendPixel(fx, fy, r, g, b, a);
            }
        }
    }
}

void SoftwareRenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    rx = std::min(rx, w * 0.5f);
    ry = std::min(ry, h * 0.5f);
    float sw = strokeWidth;
    float innerRx = std::max(0.0f, rx - sw);
    float innerRy = std::max(0.0f, ry - sw);

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);
    int32_t isw = (int32_t)(sw + 0.5f);

    // Rasterize only the stroke ring by testing outer and inner rounded rects
    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            float cx = (float)col, cy = (float)row;
            // Check if inside outer rounded rect
            bool insideOuter = true;
            if (cx < rx && cy < ry) {
                float dx = (cx - rx) / rx; float dy = (cy - ry) / ry;
                insideOuter = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx > w - rx && cy < ry) {
                float dx = (cx - (w - rx)) / rx; float dy = (cy - ry) / ry;
                insideOuter = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx < rx && cy > h - ry) {
                float dx = (cx - rx) / rx; float dy = (cy - (h - ry)) / ry;
                insideOuter = (dx * dx + dy * dy) <= 1.0f;
            } else if (cx > w - rx && cy > h - ry) {
                float dx = (cx - (w - rx)) / rx; float dy = (cy - (h - ry)) / ry;
                insideOuter = (dx * dx + dy * dy) <= 1.0f;
            }
            if (!insideOuter) continue;

            // Check if outside inner rounded rect (i.e., in the stroke ring)
            float icx = cx - sw, icy = cy - sw;
            float innerW = w - sw * 2, innerH = h - sw * 2;
            bool insideInner = false;
            if (innerW > 0 && innerH > 0 && icx >= 0 && icy >= 0 && icx <= innerW && icy <= innerH) {
                insideInner = true;
                if (icx < innerRx && icy < innerRy && innerRx > 0 && innerRy > 0) {
                    float dx = (icx - innerRx) / innerRx; float dy = (icy - innerRy) / innerRy;
                    insideInner = (dx * dx + dy * dy) <= 1.0f;
                } else if (icx > innerW - innerRx && icy < innerRy && innerRx > 0 && innerRy > 0) {
                    float dx = (icx - (innerW - innerRx)) / innerRx; float dy = (icy - innerRy) / innerRy;
                    insideInner = (dx * dx + dy * dy) <= 1.0f;
                } else if (icx < innerRx && icy > innerH - innerRy && innerRx > 0 && innerRy > 0) {
                    float dx = (icx - innerRx) / innerRx; float dy = (icy - (innerH - innerRy)) / innerRy;
                    insideInner = (dx * dx + dy * dy) <= 1.0f;
                } else if (icx > innerW - innerRx && icy > innerH - innerRy && innerRx > 0 && innerRy > 0) {
                    float dx = (icx - (innerW - innerRx)) / innerRx; float dy = (icy - (innerH - innerRy)) / innerRy;
                    insideInner = (dx * dx + dy * dy) <= 1.0f;
                }
            }

            if (!insideInner) {
                int32_t fx = ix + col, fy = iy + row;
                if (!clipStack_.empty() && IsClipped((float)fx, (float)fy)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)fx, (float)fy, r, g, b, a);
                fb_.BlendPixel(fx, fy, r, g, b, a);
            }
        }
    }
}

void SoftwareRenderTarget::FillPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush)
{
    if (!brush) return;
    tl = std::min(tl, std::min(w, h) * 0.5f);
    tr = std::min(tr, std::min(w, h) * 0.5f);
    br = std::min(br, std::min(w, h) * 0.5f);
    bl = std::min(bl, std::min(w, h) * 0.5f);

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            if (IsInsidePerCornerRoundedRect((float)col, (float)row, w, h, tl, tr, br, bl)) {
                int32_t fx = ix + col, fy = iy + row;
                if (!clipStack_.empty() && IsClipped((float)fx, (float)fy)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)fx, (float)fy, r, g, b, a);
                fb_.BlendPixel(fx, fy, r, g, b, a);
            }
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
    float sw = strokeWidth;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    float iTl = std::max(0.0f, tl - sw), iTr = std::max(0.0f, tr - sw);
    float iBr = std::max(0.0f, br - sw), iBl = std::max(0.0f, bl - sw);
    float innerW = w - sw * 2, innerH = h - sw * 2;

    for (int32_t row = 0; row < ih; row++) {
        for (int32_t col = 0; col < iw; col++) {
            bool insideOuter = IsInsidePerCornerRoundedRect((float)col, (float)row, w, h, tl, tr, br, bl);
            if (!insideOuter) continue;

            bool insideInner = false;
            if (innerW > 0 && innerH > 0) {
                float icx = (float)col - sw, icy = (float)row - sw;
                insideInner = IsInsidePerCornerRoundedRect(icx, icy, innerW, innerH, iTl, iTr, iBr, iBl);
            }
            if (insideInner) continue;

            int32_t fx = ix + col, fy = iy + row;
            if (!clipStack_.empty() && IsClipped((float)fx, (float)fy)) continue;
            uint8_t r, g, b, a;
            GetBrushColor(brush, (float)fx, (float)fy, r, g, b, a);
            fb_.BlendPixel(fx, fy, r, g, b, a);
        }
    }
}

void SoftwareRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
    if (!brush) return;
    float tx, ty;
    currentTransform_.Apply(cx, cy, tx, ty);
    int32_t irx = (int32_t)(rx + 0.5f), iry = (int32_t)(ry + 0.5f);

    for (int32_t dy = -iry; dy <= iry; dy++) {
        for (int32_t dx = -irx; dx <= irx; dx++) {
            float ex = (float)dx / rx;
            float ey = (float)dy / ry;
            if (ex * ex + ey * ey <= 1.0f) {
                int32_t px = (int32_t)tx + dx;
                int32_t py = (int32_t)ty + dy;
                if (!clipStack_.empty() && IsClipped((float)px, (float)py)) continue;
                uint8_t r, g, b, a;
                GetBrushColor(brush, (float)px, (float)py, r, g, b, a);
                fb_.BlendPixel(px, py, r, g, b, a);
            }
        }
    }
}

void SoftwareRenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth)
{
    if (!brush) return;
    uint8_t r, g, b, a;
    GetBrushColor(brush, cx, cy, r, g, b, a);

    float tx, ty;
    currentTransform_.Apply(cx, cy, tx, ty);

    // Midpoint ellipse algorithm for outline
    float outerRx = rx, outerRy = ry;
    float innerRx = std::max(0.0f, rx - strokeWidth);
    float innerRy = std::max(0.0f, ry - strokeWidth);

    int32_t irx = (int32_t)(outerRx + 0.5f), iry = (int32_t)(outerRy + 0.5f);
    for (int32_t dy = -iry; dy <= iry; dy++) {
        for (int32_t dx = -irx; dx <= irx; dx++) {
            float eOuter = ((float)dx / outerRx) * ((float)dx / outerRx) +
                           ((float)dy / outerRy) * ((float)dy / outerRy);
            float eInner = (innerRx > 0 && innerRy > 0) ?
                ((float)dx / innerRx) * ((float)dx / innerRx) +
                ((float)dy / innerRy) * ((float)dy / innerRy) : 2.0f;

            if (eOuter <= 1.0f && eInner > 1.0f) {
                int32_t px = (int32_t)tx + dx;
                int32_t py = (int32_t)ty + dy;
                if (!clipStack_.empty() && IsClipped((float)px, (float)py)) continue;
                fb_.BlendPixel(px, py, r, g, b, a);
            }
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

    int32_t iy0 = (int32_t)minY, iy1 = (int32_t)(maxY + 1);
    iy0 = std::max(iy0, 0); iy1 = std::min(iy1, height_);

    bool useWinding = (fillRule == 1);

    for (int32_t scanY = iy0; scanY < iy1; scanY++) {
        float sy = (float)scanY + 0.5f;

        if (useWinding) {
            // Winding number rule: track crossing directions
            std::vector<std::pair<float, int>> crossings; // (x, direction)
            for (uint32_t i = 0; i < pointCount; i++) {
                uint32_t j = (i + 1) % pointCount;
                float y0 = tpts[i * 2 + 1], y1 = tpts[j * 2 + 1];
                float x0 = tpts[i * 2], x1 = tpts[j * 2];

                if ((y0 <= sy && y1 > sy) || (y1 <= sy && y0 > sy)) {
                    float t = (sy - y0) / (y1 - y0);
                    float ix = x0 + t * (x1 - x0);
                    int dir = (y1 > y0) ? 1 : -1;
                    crossings.push_back({ix, dir});
                }
            }
            std::sort(crossings.begin(), crossings.end(),
                [](const auto& a, const auto& b) { return a.first < b.first; });

            int winding = 0;
            for (size_t i = 0; i < crossings.size(); i++) {
                int prevWinding = winding;
                winding += crossings[i].second;
                // Fill when winding number transitions to/from zero
                if (prevWinding == 0 && winding != 0) {
                    // Start of filled span
                } else if (prevWinding != 0 && winding == 0 && i > 0) {
                    // Find the start of this span
                    float spanStart = crossings[i - 1].first;
                    // Backtrack to find where winding became non-zero
                    int w2 = 0;
                    for (size_t k = 0; k <= i; k++) {
                        int prev2 = w2;
                        w2 += crossings[k].second;
                        if (prev2 == 0 && w2 != 0) {
                            spanStart = crossings[k].first;
                        }
                    }
                    int32_t xStart = std::max(0, (int32_t)spanStart);
                    int32_t xEnd = std::min(width_ - 1, (int32_t)crossings[i].first);
                    for (int32_t x = xStart; x <= xEnd; x++) {
                        if (!clipStack_.empty() && IsClipped((float)x, (float)scanY)) continue;
                        uint8_t r, g, b, a;
                        GetBrushColor(brush, (float)x, (float)scanY, r, g, b, a);
                        fb_.BlendPixel(x, scanY, r, g, b, a);
                    }
                }
            }
        } else {
            // Even-odd rule (original behavior)
            std::vector<float> intersections;
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
                int32_t xStart = std::max(0, (int32_t)intersections[i]);
                int32_t xEnd = std::min(width_ - 1, (int32_t)intersections[i + 1]);
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

void SoftwareRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule)
{
    if (!brush) return;

    // Parse into sub-paths (contours)
    std::vector<SubPath> subPaths;
    ParsePathToSubPaths(startX, startY, commands, commandLength, subPaths);
    if (subPaths.empty()) return;

    // For fill: collect ALL edges from all contours and do scanline fill.
    // Each contour is a closed polygon for fill purposes.
    // Merge all contour points, tracking contour boundaries for edge generation.

    // Compute bounding box across all contours
    float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
    struct Edge { float x0, y0, x1, y1; };
    std::vector<Edge> allEdges;

    for (auto& sp : subPaths) {
        uint32_t pc = (uint32_t)(sp.points.size() / 2);
        if (pc < 2) continue;

        // Transform points
        std::vector<float> tpts(sp.points.size());
        for (uint32_t j = 0; j < pc; j++) {
            currentTransform_.Apply(sp.points[j * 2], sp.points[j * 2 + 1],
                tpts[j * 2], tpts[j * 2 + 1]);
            minX = std::min(minX, tpts[j * 2]);
            maxX = std::max(maxX, tpts[j * 2]);
            minY = std::min(minY, tpts[j * 2 + 1]);
            maxY = std::max(maxY, tpts[j * 2 + 1]);
        }

        // Add edges for this contour (always closed for fill)
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
                    // Find span start (backtrack to where winding became non-zero)
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
            // Even-odd rule
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

void SoftwareRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset)
{
    if (!brush) return;

    // Parse into sub-paths
    std::vector<SubPath> subPaths;
    ParsePathToSubPaths(startX, startY, commands, commandLength, subPaths);
    if (subPaths.empty()) return;

    // Stroke each sub-path independently
    for (auto& sp : subPaths) {
        uint32_t ptCount = (uint32_t)(sp.points.size() / 2);
        if (ptCount < 2) continue;

        bool subClosed = sp.closed || closed;

        // Remove duplicated closing point if present
        if (subClosed && ptCount >= 3) {
            float fx = sp.points[0], fy = sp.points[1];
            float lx = sp.points[(ptCount - 1) * 2], ly = sp.points[(ptCount - 1) * 2 + 1];
            if (std::abs(fx - lx) < 0.01f && std::abs(fy - ly) < 0.01f) {
                ptCount--;
            }
        }
        if (ptCount < 2) continue;

        // For thick strokes (> 2px), use outline polygon approach for proper joins.
        // For thin strokes, use direct line drawing (more reliable at small sizes).
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
                // Draw each dash segment
                DrawPolygon(seg.data(), segPts, brush, strokeWidth, false, lineJoin, miterLimit);
            }
        } else {
            // Direct line drawing for each segment of the sub-path
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

    auto* stf = dynamic_cast<SoftwareTextFormat*>(format);
    if (!stf) return;

    // Use platform text rendering if available, otherwise draw placeholder rectangles
#if defined(_WIN32)
    // Use GDI for text rendering with cached HDC
    uint8_t r, g, b, a;
    GetBrushColor(brush, x, y, r, g, b, a);

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    if (!cachedTextDC_) {
        cachedTextDC_ = CreateCompatibleDC(nullptr);
    }
    HDC hdc = static_cast<HDC>(cachedTextDC_);
    if (!hdc) return;
    BITMAPINFO bmi{};
    bmi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bmi.bmiHeader.biWidth = (int32_t)w;
    bmi.bmiHeader.biHeight = -(int32_t)h;
    bmi.bmiHeader.biPlanes = 1;
    bmi.bmiHeader.biBitCount = 32;
    bmi.bmiHeader.biCompression = BI_RGB;

    void* bits = nullptr;
    HBITMAP hbm = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &bits, nullptr, 0);
    if (hbm && bits) {
        HGDIOBJ oldBm = SelectObject(hdc, hbm);

        int fontHeight = -(int)(stf->fontSize * dpiY_ / 72.0f);
        HFONT hFont = CreateFontW(fontHeight, 0, 0, 0,
            stf->fontWeight, (stf->fontStyle == 1 || stf->fontStyle == 2) ? TRUE : FALSE,
            FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH,
            stf->fontFamily.c_str());
        HGDIOBJ oldFont = SelectObject(hdc, hFont);

        SetTextColor(hdc, RGB(r, g, b));
        SetBkMode(hdc, TRANSPARENT);

        RECT rc = { 0, 0, (LONG)w, (LONG)h };
        UINT dtFlags = DT_WORDBREAK;
        switch (stf->alignment) {
            case 1: dtFlags |= DT_RIGHT; break;
            case 2: dtFlags |= DT_CENTER; break;
            default: dtFlags |= DT_LEFT; break;
        }

        DrawTextW(hdc, text, textLength, &rc, dtFlags);

        // Copy rendered text to framebuffer with alpha blending
        uint8_t* textBits = (uint8_t*)bits;
        int32_t ix = (int32_t)tx, iy = (int32_t)ty;
        for (int32_t row = 0; row < (int32_t)h && iy + row < height_; row++) {
            for (int32_t col = 0; col < (int32_t)w && ix + col < width_; col++) {
                int srcIdx = (row * (int32_t)w + col) * 4;
                uint8_t sr = textBits[srcIdx + 2];
                uint8_t sg = textBits[srcIdx + 1];
                uint8_t sb = textBits[srcIdx + 0];
                // Use luminance as alpha for ClearType
                uint8_t sa = (uint8_t)std::min(255, (sr + sg + sb) / 3);
                if (sa > 0) {
                    sa = (uint8_t)((sa / 255.0f) * a);
                    fb_.BlendPixel(ix + col, iy + row, r, g, b, sa);
                }
            }
        }

        SelectObject(hdc, oldFont);
        DeleteObject(hFont);
        SelectObject(hdc, oldBm);
        DeleteObject(hbm);
    }
#else
    // Fallback: draw placeholder rectangles representing text bounds
    auto* solid = dynamic_cast<SoftwareSolidBrush*>(brush);
    if (!solid) return;

    float charWidth = stf->fontSize * 0.6f;
    float lineHeight = stf->fontSize * 1.2f;
    float baseline = stf->fontSize * 0.8f;
    float tx, ty;
    currentTransform_.Apply(x, y + baseline, tx, ty);

    // Draw a thin underline to indicate text position
    uint8_t cr = FloatToU8(solid->r);
    uint8_t cg = FloatToU8(solid->g);
    uint8_t cb = FloatToU8(solid->b);
    uint8_t ca = FloatToU8(solid->a * currentOpacity_ * 0.3f);
    float textWidth = std::min(textLength * charWidth, w);
    for (int32_t col = 0; col < (int32_t)textWidth; col++) {
        fb_.BlendPixel((int32_t)tx + col, (int32_t)ty, cr, cg, cb, ca);
    }

    (void)h; (void)lineHeight;
#endif
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

    SoftwareClipRect clip;
    if (!clipStack_.empty()) {
        // Intersect with current clip
        auto& top = clipStack_.top();
        clip.x = std::max(tx, top.x);
        clip.y = std::max(ty, top.y);
        float right = std::min(tx + w, top.x + top.w);
        float bottom = std::min(ty + h, top.y + top.h);
        clip.w = std::max(0.0f, right - clip.x);
        clip.h = std::max(0.0f, bottom - clip.y);
    } else {
        clip = {tx, ty, w, h};
    }
    clipStack_.push(clip);
}

void SoftwareRenderTarget::PopClip()
{
    if (!clipStack_.empty()) clipStack_.pop();
}

void SoftwareRenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry)
{
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);

    SoftwareClipRect clip;
    if (!clipStack_.empty()) {
        auto& top = clipStack_.top();
        clip.x = std::max(tx, top.x);
        clip.y = std::max(ty, top.y);
        float right = std::min(tx + w, top.x + top.w);
        float bottom = std::min(ty + h, top.y + top.h);
        clip.w = std::max(0.0f, right - clip.x);
        clip.h = std::max(0.0f, bottom - clip.y);
    } else {
        clip = {tx, ty, w, h};
    }
    clip.rx = rx;
    clip.ry = ry;
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
}

void SoftwareRenderTarget::AddDirtyRect(float x, float y, float w, float h)
{
    (void)x; (void)y; (void)w; (void)h;
}

void SoftwareRenderTarget::SetFullInvalidation()
{
    fullInvalidation_ = true;
}

void SoftwareRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
    if (!bitmap) return;
    auto* sb = dynamic_cast<SoftwareBitmap*>(bitmap);
    if (!sb || sb->pixels_.empty()) return;

    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    for (int32_t row = 0; row < ih; row++) {
        int32_t srcRow = (int32_t)((float)row / ih * sb->height_);
        if (srcRow >= (int32_t)sb->height_) continue;

        for (int32_t col = 0; col < iw; col++) {
            int32_t srcCol = (int32_t)((float)col / iw * sb->width_);
            if (srcCol >= (int32_t)sb->width_) continue;

            size_t srcIdx = ((size_t)srcRow * sb->width_ + srcCol) * 4;
            uint8_t sb_ = sb->pixels_[srcIdx + 0];
            uint8_t sg = sb->pixels_[srcIdx + 1];
            uint8_t sr = sb->pixels_[srcIdx + 2];
            uint8_t sa = (uint8_t)(sb->pixels_[srcIdx + 3] * opacity * currentOpacity_);

            int32_t dx = ix + col, dy = iy + row;
            if (!clipStack_.empty() && IsClipped((float)dx, (float)dy)) continue;
            fb_.BlendPixel(dx, dy, sr, sg, sb_, sa);
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

        // Rasterize ellipse directly with the color
        float tx, ty;
        currentTransform_.Apply(cx, cy, tx, ty);
        int32_t irx = (int32_t)(rx + 0.5f), iry = (int32_t)(ry + 0.5f);
        uint8_t r = FloatToU8(cr), g = FloatToU8(cg), b = FloatToU8(cb), a = FloatToU8(ca * currentOpacity_);

        for (int32_t dy = -iry; dy <= iry; dy++) {
            for (int32_t dx = -irx; dx <= irx; dx++) {
                float ex = (float)dx / rx;
                float ey = (float)dy / ry;
                if (ex * ex + ey * ey <= 1.0f) {
                    int32_t px = (int32_t)tx + dx;
                    int32_t py = (int32_t)ty + dy;
                    if (!clipStack_.empty() && IsClipped((float)px, (float)py)) continue;
                    fb_.BlendPixel(px, py, r, g, b, a);
                }
            }
        }
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
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
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

RenderTarget* SoftwareBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    auto* rt = new SoftwareRenderTarget(width, height);
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

IRenderBackend* CreateSoftwareBackend()
{
    return new SoftwareBackend();
}

} // namespace jalium
