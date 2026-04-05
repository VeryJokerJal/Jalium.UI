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
    // Check all clip rects (simplified: only check top)
    auto& clip = const_cast<std::stack<SoftwareClipRect>&>(clipStack_).top();
    return !clip.Contains(px, py);
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

void SoftwareRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule)
{
    if (!brush) return;

    // Flatten path to polygon points, then fill
    std::vector<float> flatPoints;
    flatPoints.push_back(startX);
    flatPoints.push_back(startY);

    uint32_t i = 0;
    while (i < commandLength) {
        int tag = (int)commands[i];
        if (tag == 0 && i + 2 < commandLength) {
            flatPoints.push_back(commands[i + 1]);
            flatPoints.push_back(commands[i + 2]);
            i += 3;
        } else if (tag == 1 && i + 6 < commandLength) {
            // Flatten cubic bezier into line segments
            float px = flatPoints[flatPoints.size() - 2];
            float py = flatPoints[flatPoints.size() - 1];
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];

            const int segments = 24;
            for (int s = 1; s <= segments; s++) {
                float t = (float)s / segments;
                float it = 1 - t;
                float bx = it * it * it * px + 3 * it * it * t * cp1x + 3 * it * t * t * cp2x + t * t * t * ex;
                float by = it * it * it * py + 3 * it * it * t * cp1y + 3 * it * t * t * cp2y + t * t * t * ey;
                flatPoints.push_back(bx);
                flatPoints.push_back(by);
            }
            i += 7;
        } else if (tag == 2 && i + 2 < commandLength) {
            // MoveTo: new sub-path — for software backend, just add points inline
            flatPoints.push_back(commands[i + 1]);
            flatPoints.push_back(commands[i + 2]);
            i += 3;
        } else if (tag == 3 && i + 4 < commandLength) {
            // QuadTo: flatten quadratic bezier
            float px = flatPoints[flatPoints.size() - 2];
            float py = flatPoints[flatPoints.size() - 1];
            float cpx = commands[i + 1], cpy = commands[i + 2];
            float ex = commands[i + 3], ey = commands[i + 4];
            const int segments = 16;
            for (int s = 1; s <= segments; s++) {
                float t = (float)s / segments;
                float it = 1 - t;
                flatPoints.push_back(it * it * px + 2 * it * t * cpx + t * t * ex);
                flatPoints.push_back(it * it * py + 2 * it * t * cpy + t * t * ey);
            }
            i += 5;
        } else if (tag == 5) {
            // ClosePath — no-op for fill
            i += 1;
        } else {
            break;
        }
    }

    FillPolygon(flatPoints.data(), (uint32_t)(flatPoints.size() / 2), brush, fillRule);
}

void SoftwareRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset)
{
    if (!brush) return;

    // Flatten path to polygon points, then stroke
    std::vector<float> flatPoints;
    flatPoints.push_back(startX);
    flatPoints.push_back(startY);

    uint32_t i = 0;
    while (i < commandLength) {
        int tag = (int)commands[i];
        if (tag == 0 && i + 2 < commandLength) {
            flatPoints.push_back(commands[i + 1]);
            flatPoints.push_back(commands[i + 2]);
            i += 3;
        } else if (tag == 1 && i + 6 < commandLength) {
            float px = flatPoints[flatPoints.size() - 2];
            float py = flatPoints[flatPoints.size() - 1];
            float cp1x = commands[i + 1], cp1y = commands[i + 2];
            float cp2x = commands[i + 3], cp2y = commands[i + 4];
            float ex = commands[i + 5], ey = commands[i + 6];

            const int segments = 24;
            for (int s = 1; s <= segments; s++) {
                float t = (float)s / segments;
                float it = 1 - t;
                float bx = it * it * it * px + 3 * it * it * t * cp1x + 3 * it * t * t * cp2x + t * t * t * ex;
                float by = it * it * it * py + 3 * it * it * t * cp1y + 3 * it * t * t * cp2y + t * t * t * ey;
                flatPoints.push_back(bx);
                flatPoints.push_back(by);
            }
            i += 7;
        } else if (tag == 2 && i + 2 < commandLength) {
            // MoveTo: new sub-path
            flatPoints.push_back(commands[i + 1]);
            flatPoints.push_back(commands[i + 2]);
            i += 3;
        } else if (tag == 3 && i + 4 < commandLength) {
            // QuadTo: flatten quadratic bezier
            float px = flatPoints[flatPoints.size() - 2];
            float py = flatPoints[flatPoints.size() - 1];
            float cpx = commands[i + 1], cpy = commands[i + 2];
            float ex = commands[i + 3], ey = commands[i + 4];
            const int segments = 16;
            for (int s = 1; s <= segments; s++) {
                float t = (float)s / segments;
                float it = 1 - t;
                flatPoints.push_back(it * it * px + 2 * it * t * cpx + t * t * ex);
                flatPoints.push_back(it * it * py + 2 * it * t * cpy + t * t * ey);
            }
            i += 5;
        } else if (tag == 5) {
            // ClosePath
            closed = true;
            i += 1;
        } else {
            break;
        }
    }

    DrawPolygon(flatPoints.data(), (uint32_t)(flatPoints.size() / 2), brush, strokeWidth, closed);
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
    const char*, const char*, const char*,
    float tintOpacity, float,
    float, float, float, float)
{
    // Simple tint overlay for software rasterizer
    uint8_t a = FloatToU8(tintOpacity * currentOpacity_);
    float tx, ty;
    currentTransform_.Apply(x, y, tx, ty);
    int32_t ix = (int32_t)tx, iy = (int32_t)ty;
    int32_t iw = (int32_t)(w + 0.5f), ih = (int32_t)(h + 0.5f);

    for (int32_t row = iy; row < iy + ih; row++) {
        for (int32_t col = ix; col < ix + iw; col++) {
            fb_.BlendPixel(col, row, 128, 128, 128, a);
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
