#include "metal_backend.h"
#include <algorithm>
#include <cstring>
#include <cmath>

#ifdef __APPLE__
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <AppKit/AppKit.h>
#import <CoreText/CoreText.h>
#import <ImageIO/ImageIO.h>
#endif

namespace jalium {

static int32_t ClampInt(int32_t value, int32_t minValue, int32_t maxValue)
{
    return value < minValue ? minValue : (value > maxValue ? maxValue : value);
}

static void CopyRegion(
    const std::vector<uint8_t>& src,
    std::vector<uint8_t>& dst,
    int32_t width,
    int32_t height,
    int32_t srcX,
    int32_t srcY,
    int32_t dstX,
    int32_t dstY,
    int32_t regionW,
    int32_t regionH)
{
    if (regionW <= 0 || regionH <= 0) return;
    const int32_t stride = width * 4;
    for (int32_t row = 0; row < regionH; ++row) {
        int32_t sy = srcY + row;
        int32_t dy = dstY + row;
        if (sy < 0 || sy >= height || dy < 0 || dy >= height) continue;
        int32_t copyW = regionW;
        int32_t sx = srcX;
        int32_t dx = dstX;
        if (sx < 0) { copyW += sx; dx -= sx; sx = 0; }
        if (dx < 0) { copyW += dx; sx -= dx; dx = 0; }
        if (sx + copyW > width) copyW = width - sx;
        if (dx + copyW > width) copyW = width - dx;
        if (copyW <= 0) continue;
        std::memcpy(
            dst.data() + (size_t)dy * stride + dx * 4,
            src.data() + (size_t)sy * stride + sx * 4,
            (size_t)copyW * 4);
    }
}

static void BlendRegion(
    const std::vector<uint8_t>& src,
    int32_t srcWidth,
    int32_t srcHeight,
    std::vector<uint8_t>& dst,
    int32_t dstWidth,
    int32_t dstHeight,
    int32_t dstX,
    int32_t dstY,
    float opacity)
{
    if (opacity <= 0.0f || srcWidth <= 0 || srcHeight <= 0) return;
    const int32_t srcStride = srcWidth * 4;
    const int32_t dstStride = dstWidth * 4;

    for (int32_t row = 0; row < srcHeight; ++row) {
        int32_t dy = dstY + row;
        if (dy < 0 || dy >= dstHeight) continue;
        for (int32_t col = 0; col < srcWidth; ++col) {
            int32_t dx = dstX + col;
            if (dx < 0 || dx >= dstWidth) continue;
            size_t srcIdx = (size_t)row * srcStride + col * 4;
            size_t dstIdx = (size_t)dy * dstStride + dx * 4;

            float srcB = src[srcIdx + 0] / 255.0f;
            float srcG = src[srcIdx + 1] / 255.0f;
            float srcR = src[srcIdx + 2] / 255.0f;
            float srcA = src[srcIdx + 3] / 255.0f * opacity;
            float dstB = dst[dstIdx + 0] / 255.0f;
            float dstG = dst[dstIdx + 1] / 255.0f;
            float dstR = dst[dstIdx + 2] / 255.0f;
            float dstA = dst[dstIdx + 3] / 255.0f;

            float outA = srcA + dstA * (1.0f - srcA);
            float outR = srcR * srcA + dstR * dstA * (1.0f - srcA);
            float outG = srcG * srcA + dstG * dstA * (1.0f - srcA);
            float outB = srcB * srcA + dstB * dstA * (1.0f - srcA);

            if (outA > 0.0f) {
                outR /= outA;
                outG /= outA;
                outB /= outA;
            }

            dst[dstIdx + 0] = (uint8_t)(std::clamp(outB, 0.0f, 1.0f) * 255.0f);
            dst[dstIdx + 1] = (uint8_t)(std::clamp(outG, 0.0f, 1.0f) * 255.0f);
            dst[dstIdx + 2] = (uint8_t)(std::clamp(outR, 0.0f, 1.0f) * 255.0f);
            dst[dstIdx + 3] = (uint8_t)(std::clamp(outA, 0.0f, 1.0f) * 255.0f);
        }
    }
}

static void ApplySepiaVignette(
    std::vector<uint8_t>& pixels,
    int32_t width,
    int32_t height,
    float intensity,
    float radius,
    float softness)
{
    if (width <= 0 || height <= 0) return;
    const float invW = 1.0f / width;
    const float invH = 1.0f / height;
    radius = std::clamp(radius, 0.0f, 1.0f);
    softness = std::clamp(softness, 0.0f, 1.0f);
    float edge0 = radius - softness;
    if (edge0 < 0.0f) edge0 = 0.0f;

    for (int32_t y = 0; y < height; ++y) {
        for (int32_t x = 0; x < width; ++x) {
            size_t idx = (size_t)y * width * 4 + x * 4;
            float alpha = pixels[idx + 3] / 255.0f;
            if (alpha <= 0.0f) continue;

            float b = pixels[idx + 0] / 255.0f;
            float g = pixels[idx + 1] / 255.0f;
            float r = pixels[idx + 2] / 255.0f;
            float gray = r * 0.299f + g * 0.587f + b * 0.114f;
            float sr = gray * 1.2f;
            float sg = gray * 1.0f;
            float sb = gray * 0.8f;
            float outR = r + (sr - r) * intensity;
            float outG = g + (sg - g) * intensity;
            float outB = b + (sb - b) * intensity;

            float uvx = (x + 0.5f) * invW;
            float uvy = (y + 0.5f) * invH;
            float dx = uvx - 0.5f;
            float dy = uvy - 0.5f;
            float dist = std::sqrt(dx * dx + dy * dy);
            float vignette = 0.0f;
            if (dist <= edge0) {
                vignette = 1.0f;
            } else if (dist >= radius) {
                vignette = 0.0f;
            } else {
                float t = (dist - edge0) / (radius - edge0);
                vignette = 1.0f - t * t * (3.0f - 2.0f * t);
            }

            outR = std::clamp(outR * vignette, 0.0f, 1.0f);
            outG = std::clamp(outG * vignette, 0.0f, 1.0f);
            outB = std::clamp(outB * vignette, 0.0f, 1.0f);

            pixels[idx + 0] = (uint8_t)(outB * 255.0f);
            pixels[idx + 1] = (uint8_t)(outG * 255.0f);
            pixels[idx + 2] = (uint8_t)(outR * 255.0f);
        }
    }
}

// ============================================================================
// MetalTextFormat
// ============================================================================

#ifdef __APPLE__
JaliumResult MetalTextFormat::MeasureText(
    const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics || !text || textLength == 0)
        return JALIUM_ERROR_INVALID_ARGUMENT;

    // Convert wchar_t to CFString
    CFMutableStringRef cfStr = CFStringCreateMutable(kCFAllocatorDefault, 0);
    for (uint32_t i = 0; i < textLength; i++) {
        UniChar ch = (UniChar)text[i];
        CFStringAppendCharacters(cfStr, &ch, 1);
    }

    // Create attributed string with our font
    CFMutableDictionaryRef attrs = CFDictionaryCreateMutable(
        kCFAllocatorDefault, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFDictionarySetValue(attrs, kCTFontAttributeName, font);

    CFAttributedStringRef attrStr = CFAttributedStringCreate(kCFAllocatorDefault, cfStr, attrs);

    // Create framesetter for layout
    CTFramesetterRef framesetter = CTFramesetterCreateWithAttributedString(attrStr);
    CFRange fitRange;
    CGSize constraints = CGSizeMake(
        maxWidth > 0 ? maxWidth : CGFLOAT_MAX,
        maxHeight > 0 ? maxHeight : CGFLOAT_MAX);
    CGSize suggestedSize = CTFramesetterSuggestFrameSizeWithConstraints(
        framesetter, CFRangeMake(0, 0), nullptr, constraints, &fitRange);

    // Count lines
    CGPathRef path = CGPathCreateWithRect(
        CGRectMake(0, 0, constraints.width, constraints.height), nullptr);
    CTFrameRef frame = CTFramesetterCreateFrame(framesetter, CFRangeMake(0, 0), path, nullptr);
    CFArrayRef lines = CTFrameGetLines(frame);
    CFIndex lineCount = CFArrayGetCount(lines);

    // Get font metrics
    CGFloat ascent = CTFontGetAscent(font);
    CGFloat descent = CTFontGetDescent(font);
    CGFloat leading = CTFontGetLeading(font);

    metrics->width = (float)suggestedSize.width;
    metrics->height = (float)suggestedSize.height;
    metrics->lineHeight = (float)(ascent + descent + leading);
    metrics->baseline = (float)ascent;
    metrics->ascent = (float)ascent;
    metrics->descent = (float)descent;
    metrics->lineGap = (float)leading;
    metrics->lineCount = (uint32_t)lineCount;

    CFRelease(frame);
    CGPathRelease(path);
    CFRelease(framesetter);
    CFRelease(attrStr);
    CFRelease(attrs);
    CFRelease(cfStr);

    return JALIUM_OK;
}

JaliumResult MetalTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;

    CGFloat ascent = CTFontGetAscent(font);
    CGFloat descent = CTFontGetDescent(font);
    CGFloat leading = CTFontGetLeading(font);

    metrics->width = 0;
    metrics->height = 0;
    metrics->lineHeight = (float)(ascent + descent + leading);
    metrics->baseline = (float)ascent;
    metrics->ascent = (float)ascent;
    metrics->descent = (float)descent;
    metrics->lineGap = (float)leading;
    metrics->lineCount = 0;

    return JALIUM_OK;
}

// Builds a single-line CTLine from the given UTF-16 text using this format's font.
// Caller owns the returned CTLineRef (must CFRelease). Returns nullptr on failure.
// NOTE: characters are appended as UniChar (UTF-16 code units), mirroring
// MetalTextFormat::MeasureText so string indices align with the managed layer's
// UTF-16 column offsets. Non-BMP code points are not handled here (matching the
// existing measurement path); the editor hit-testing tests operate on BMP text.
static CTLineRef CreateCTLineForText(CTFontRef font, const wchar_t* text, uint32_t textLength)
{
    if (!font || !text || textLength == 0)
        return nullptr;

    CFMutableStringRef cfStr = CFStringCreateMutable(kCFAllocatorDefault, 0);
    for (uint32_t i = 0; i < textLength; i++) {
        UniChar ch = (UniChar)text[i];
        CFStringAppendCharacters(cfStr, &ch, 1);
    }

    CFMutableDictionaryRef attrs = CFDictionaryCreateMutable(
        kCFAllocatorDefault, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFDictionarySetValue(attrs, kCTFontAttributeName, font);

    CFAttributedStringRef attrStr = CFAttributedStringCreate(kCFAllocatorDefault, cfStr, attrs);
    CTLineRef line = CTLineCreateWithAttributedString(attrStr);

    CFRelease(attrStr);
    CFRelease(attrs);
    CFRelease(cfStr);
    return line;
}

JaliumResult MetalTextFormat::HitTestPoint(
    const wchar_t* text, uint32_t textLength,
    float /*maxWidth*/, float /*maxHeight*/, float pointX, float /*pointY*/,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
    if (!text || textLength == 0)
        return JALIUM_OK;

    CTLineRef line = CreateCTLineForText(font, text, textLength);
    if (!line)
        return JALIUM_OK;

    // CTLineGetStringIndexForPosition snaps to the nearest character boundary,
    // returning the insertion index for the click — exactly the caret column the
    // managed layer expects (so isTrailingHit stays 0).
    CFIndex index = CTLineGetStringIndexForPosition(line, CGPointMake(pointX, 0));
    if (index == kCFNotFound)
        index = 0;
    if (index < 0) index = 0;
    if (index > (CFIndex)textLength) index = (CFIndex)textLength;

    CGFloat caretX = CTLineGetOffsetForStringIndex(line, index, nullptr);
    CGFloat width = (CGFloat)CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);

    result->textPosition = (uint32_t)index;
    result->isTrailingHit = 0;
    result->isInside = (pointX >= 0 && pointX <= width) ? 1 : 0;
    result->caretX = (float)caretX;
    result->caretY = 0;
    result->caretHeight = (float)(CTFontGetAscent(font) + CTFontGetDescent(font));

    CFRelease(line);
    return JALIUM_OK;
}

JaliumResult MetalTextFormat::HitTestTextPosition(
    const wchar_t* text, uint32_t textLength,
    float /*maxWidth*/, float /*maxHeight*/, uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result) return JALIUM_ERROR_INVALID_ARGUMENT;
    memset(result, 0, sizeof(*result));
    if (!text || textLength == 0)
        return JALIUM_OK;

    CTLineRef line = CreateCTLineForText(font, text, textLength);
    if (!line)
        return JALIUM_OK;

    // Resolve to the caret's string index: a trailing hit asks for the edge after
    // the character at textPosition.
    CFIndex index = (CFIndex)textPosition + (isTrailingHit ? 1 : 0);
    if (index < 0) index = 0;
    if (index > (CFIndex)textLength) index = (CFIndex)textLength;

    CGFloat caretX = CTLineGetOffsetForStringIndex(line, index, nullptr);

    result->textPosition = (uint32_t)index;
    result->isTrailingHit = isTrailingHit ? 1 : 0;
    result->isInside = 1;
    result->caretX = (float)caretX;
    result->caretY = 0;
    result->caretHeight = (float)(CTFontGetAscent(font) + CTFontGetDescent(font));

    CFRelease(line);
    return JALIUM_OK;
}
#else
JaliumResult MetalTextFormat::MeasureText(
    const wchar_t*, uint32_t, float, float, JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::memset(metrics, 0, sizeof(JaliumTextMetrics));
    metrics->lineHeight = fontSize_ * 1.2f;
    metrics->baseline = fontSize_;
    metrics->ascent = fontSize_;
    metrics->descent = fontSize_ * 0.2f;
    metrics->lineCount = 1;
    return JALIUM_OK;
}

JaliumResult MetalTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::memset(metrics, 0, sizeof(JaliumTextMetrics));
    metrics->lineHeight = fontSize_ * 1.2f;
    metrics->baseline = fontSize_;
    metrics->ascent = fontSize_;
    metrics->descent = fontSize_ * 0.2f;
    return JALIUM_OK;
}
#endif

// ============================================================================
// MetalRenderTarget
// ============================================================================

MetalRenderTarget::MetalRenderTarget(int32_t width, int32_t height)
{
    width_ = width;
    height_ = height;
    framebuffer_.resize(static_cast<size_t>(width) * height * 4, 0);
}

MetalRenderTarget::~MetalRenderTarget()
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextRelease(cgContext_);
        cgContext_ = nullptr;
    }
    // Metal objects are Objective-C objects, released via ARC
    // or explicit CFRelease / release calls depending on context.
    metalLayer_ = nullptr;
    device_ = nullptr;
    commandQueue_ = nullptr;
#endif
}

bool MetalRenderTarget::Initialize(void* nsWindow)
{
#ifdef __APPLE__
    @autoreleasepool {
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        if (!device) return false;
        device_ = (__bridge_retained void*)device;

        id<MTLCommandQueue> queue = [device newCommandQueue];
        if (!queue) return false;
        commandQueue_ = (__bridge_retained void*)queue;

        // Get or create the Metal layer on the NSWindow's contentView
        NSWindow* window = (__bridge NSWindow*)nsWindow;
        NSView* contentView = [window contentView];
        if (!contentView) return false;
        nsView_ = (__bridge void*)contentView;

        [contentView setWantsLayer:YES];
        CAMetalLayer* layer = [CAMetalLayer layer];
        layer.device = device;
        layer.pixelFormat = MTLPixelFormatBGRA8Unorm;
        layer.framebufferOnly = NO;
        layer.drawableSize = CGSizeMake(width_, height_);
        [contentView setLayer:layer];
        metalLayer_ = (__bridge_retained void*)layer;

        // Create CGContext for 2D rendering
        CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        cgContext_ = CGBitmapContextCreate(
            framebuffer_.data(), width_, height_, 8, width_ * 4,
            colorSpace,
            kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
        CGColorSpaceRelease(colorSpace);

        if (!cgContext_) return false;

        // Flip coordinate system (CG is bottom-up, we want top-down)
        CGContextTranslateCTM(cgContext_, 0, height_);
        CGContextScaleCTM(cgContext_, 1, -1);

        return true;
    }
#else
    (void)nsWindow;
    return false;
#endif
}

JaliumResult MetalRenderTarget::Resize(int32_t width, int32_t height)
{
    width_ = width;
    height_ = height;
    framebuffer_.resize(static_cast<size_t>(width) * height * 4, 0);

#ifdef __APPLE__
    if (cgContext_) {
        CGContextRelease(cgContext_);
        cgContext_ = nullptr;
    }

    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    cgContext_ = CGBitmapContextCreate(
        framebuffer_.data(), width_, height_, 8, width_ * 4,
        colorSpace,
        kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    CGColorSpaceRelease(colorSpace);

    if (!cgContext_) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;

    CGContextTranslateCTM(cgContext_, 0, height_);
    CGContextScaleCTM(cgContext_, 1, -1);

    if (metalLayer_) {
        CAMetalLayer* layer = (__bridge CAMetalLayer*)metalLayer_;
        layer.drawableSize = CGSizeMake(width, height);
    }
#endif

    return JALIUM_OK;
}

JaliumResult MetalRenderTarget::BeginDraw()
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
    }
#endif
    return JALIUM_OK;
}

JaliumResult MetalRenderTarget::EndDraw()
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextRestoreGState(cgContext_);
    }

    // Present the framebuffer via Metal
    @autoreleasepool {
        if (metalLayer_ && commandQueue_) {
            CAMetalLayer* layer = (__bridge CAMetalLayer*)metalLayer_;
            id<CAMetalDrawable> drawable = [layer nextDrawable];
            if (drawable) {
                id<MTLTexture> texture = drawable.texture;

                // Upload framebuffer to Metal texture
                MTLRegion region = MTLRegionMake2D(0, 0, width_, height_);
                [texture replaceRegion:region
                           mipmapLevel:0
                             withBytes:framebuffer_.data()
                           bytesPerRow:width_ * 4];

                id<MTLCommandQueue> queue = (__bridge id<MTLCommandQueue>)commandQueue_;
                id<MTLCommandBuffer> commandBuffer = [queue commandBuffer];
                [commandBuffer presentDrawable:drawable];
                [commandBuffer commit];
            }
        }
    }
#endif
    return JALIUM_OK;
}

void MetalRenderTarget::BeginEffectCapture(float x, float y, float w, float h)
{
    if (effectCaptureActive_ || w <= 0 || h <= 0) return;

    effectCaptureX_ = x;
    effectCaptureY_ = y;
    effectCaptureW_ = w;
    effectCaptureH_ = h;
    savedFramebuffer_ = framebuffer_;
    effectCaptureFb_.clear();
    effectCaptureActive_ = true;

#ifdef __APPLE__
    if (cgContext_) {
        CGPoint pt = CGPointApplyAffineTransform(CGPointMake(x, y), CGContextGetCTM(cgContext_));
        int32_t ix = (int32_t)std::floor(pt.x + 0.5f);
        int32_t iy = (int32_t)std::floor(pt.y + 0.5f);
        int32_t iw = (int32_t)(w + 0.5f);
        int32_t ih = (int32_t)(h + 0.5f);
        int32_t maxX = width_;
        int32_t maxY = height_;
        const int32_t stride = width_ * 4;
        ix = ClampInt(ix, 0, maxX);
        iy = ClampInt(iy, 0, maxY);
        iw = ClampInt(iw, 0, maxX - ix);
        ih = ClampInt(ih, 0, maxY - iy);
        for (int32_t row = 0; row < ih; ++row) {
            size_t base = (size_t)(iy + row) * stride + ix * 4;
            std::memset(framebuffer_.data() + base, 0, (size_t)iw * 4);
        }
    }
#else
    int32_t ix = (int32_t)(x + 0.5f);
    int32_t iy = (int32_t)(y + 0.5f);
    int32_t iw = (int32_t)(w + 0.5f);
    int32_t ih = (int32_t)(h + 0.5f);
    ix = ClampInt(ix, 0, width_);
    iy = ClampInt(iy, 0, height_);
    iw = ClampInt(iw, 0, width_ - ix);
    ih = ClampInt(ih, 0, height_ - iy);
    const int32_t stride = width_ * 4;
    for (int32_t row = 0; row < ih; ++row) {
        size_t base = (size_t)(iy + row) * stride + ix * 4;
        std::memset(framebuffer_.data() + base, 0, (size_t)iw * 4);
    }
#endif
}

void MetalRenderTarget::EndEffectCapture()
{
    if (!effectCaptureActive_) return;

#ifdef __APPLE__
    if (cgContext_) {
        CGPoint pt = CGPointApplyAffineTransform(CGPointMake(effectCaptureX_, effectCaptureY_), CGContextGetCTM(cgContext_));
        int32_t ix = (int32_t)std::floor(pt.x + 0.5f);
        int32_t iy = (int32_t)std::floor(pt.y + 0.5f);
        int32_t iw = (int32_t)(effectCaptureW_ + 0.5f);
        int32_t ih = (int32_t)(effectCaptureH_ + 0.5f);
        ix = ClampInt(ix, 0, width_);
        iy = ClampInt(iy, 0, height_);
        iw = ClampInt(iw, 0, width_ - ix);
        ih = ClampInt(ih, 0, height_ - iy);
        effectCaptureFb_.resize((size_t)iw * ih * 4);
        for (int32_t row = 0; row < ih; ++row) {
            size_t srcBase = (size_t)(iy + row) * width_ * 4 + ix * 4;
            size_t dstBase = (size_t)row * iw * 4;
            std::memcpy(effectCaptureFb_.data() + dstBase, framebuffer_.data() + srcBase, (size_t)iw * 4);
        }
    }
#else
    int32_t ix = (int32_t)(effectCaptureX_ + 0.5f);
    int32_t iy = (int32_t)(effectCaptureY_ + 0.5f);
    int32_t iw = (int32_t)(effectCaptureW_ + 0.5f);
    int32_t ih = (int32_t)(effectCaptureH_ + 0.5f);
    ix = ClampInt(ix, 0, width_);
    iy = ClampInt(iy, 0, height_);
    iw = ClampInt(iw, 0, width_ - ix);
    ih = ClampInt(ih, 0, height_ - iy);
    effectCaptureFb_.resize((size_t)iw * ih * 4);
    for (int32_t row = 0; row < ih; ++row) {
        size_t srcBase = (size_t)(iy + row) * width_ * 4 + ix * 4;
        size_t dstBase = (size_t)row * iw * 4;
        std::memcpy(effectCaptureFb_.data() + dstBase, framebuffer_.data() + srcBase, (size_t)iw * 4);
    }
#endif

    framebuffer_.swap(savedFramebuffer_);
    effectCaptureActive_ = false;
}

void MetalRenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode,
    uint32_t shaderBytecodeSize,
    const float* constants,
    uint32_t constantFloatCount)
{
    if (effectCaptureFb_.empty()) return;

    // Detect the demo-specific Metal shader marker.
    const char marker[] = "JALIUM_METAL_SHADER_SEPIA_VIGNETTE";
    bool isDemoMetalShader = shaderBytecodeSize >= sizeof(marker) &&
        std::memcmp(shaderBytecode, marker, sizeof(marker)) == 0;

    if (!isDemoMetalShader) {
        // Fallback: draw captured content unchanged.
        int32_t ix = (int32_t)(effectCaptureX_ + 0.5f);
        int32_t iy = (int32_t)(effectCaptureY_ + 0.5f);
        int32_t iw = (int32_t)(effectCaptureW_ + 0.5f);
        int32_t ih = (int32_t)(effectCaptureH_ + 0.5f);
        BlendRegion(effectCaptureFb_, iw, ih, framebuffer_, width_, height_, ix, iy, currentOpacity_);
        return;
    }

    float intensity = 0.0f;
    float radius = 0.75f;
    float softness = 0.45f;
    if (constants && constantFloatCount >= 1) {
        intensity = constants[0];
    }
    if (constants && constantFloatCount >= 6) {
        radius = constants[4];
        softness = constants[5];
    }

    int32_t iw = (int32_t)(effectCaptureW_ + 0.5f);
    int32_t ih = (int32_t)(effectCaptureH_ + 0.5f);
    ApplySepiaVignette(effectCaptureFb_, iw, ih, intensity, radius, softness);

    int32_t ix = (int32_t)(effectCaptureX_ + 0.5f);
    int32_t iy = (int32_t)(effectCaptureY_ + 0.5f);
    BlendRegion(effectCaptureFb_, iw, ih, framebuffer_, width_, height_, ix, iy, currentOpacity_);
}

void MetalRenderTarget::Clear(float r, float g, float b, float a)
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        // Reset transform for full clear
        CGAffineTransform t = CGContextGetCTM(cgContext_);
        CGContextConcatCTM(cgContext_, CGAffineTransformInvert(t));
        CGContextSetRGBFillColor(cgContext_, r, g, b, a);
        CGContextFillRect(cgContext_, CGRectMake(0, 0, width_, height_));
        CGContextRestoreGState(cgContext_);
    }
#else
    // Software fallback for framebuffer clear
    uint8_t rb = (uint8_t)(std::clamp(r, 0.0f, 1.0f) * 255.0f);
    uint8_t gb = (uint8_t)(std::clamp(g, 0.0f, 1.0f) * 255.0f);
    uint8_t bb = (uint8_t)(std::clamp(b, 0.0f, 1.0f) * 255.0f);
    uint8_t ab = (uint8_t)(std::clamp(a, 0.0f, 1.0f) * 255.0f);
    if (rb == 0 && gb == 0 && bb == 0 && ab == 0) {
        memset(framebuffer_.data(), 0, framebuffer_.size());
    } else {
        // Fill the first row, then memcpy to all remaining rows
        size_t stride = (size_t)width_ * 4;
        for (size_t i = 0; i < stride; i += 4) {
            framebuffer_[i + 0] = bb;
            framebuffer_[i + 1] = gb;
            framebuffer_[i + 2] = rb;
            framebuffer_[i + 3] = ab;
        }
        for (size_t row = 1; row < height_; ++row) {
            memcpy(framebuffer_.data() + row * stride, framebuffer_.data(), stride);
        }
    }
#endif
}

#ifdef __APPLE__
void MetalRenderTarget::ApplyBrush(CGContextRef ctx, Brush* brush, bool forStroke)
{
    if (!brush) return;

    if (auto* solid = dynamic_cast<MetalSolidBrush*>(brush)) {
        float opacity = currentOpacity_;
        if (forStroke) {
            CGContextSetRGBStrokeColor(ctx, solid->r, solid->g, solid->b, solid->a * opacity);
        } else {
            CGContextSetRGBFillColor(ctx, solid->r, solid->g, solid->b, solid->a * opacity);
        }
    }
    // Gradient brushes are applied via ApplyGradientFill after clipping to the path
}

void MetalRenderTarget::ApplyGradientFill(CGContextRef ctx, Brush* brush)
{
    if (auto* linear = dynamic_cast<MetalLinearGradientBrush*>(brush)) {
        size_t count = linear->stops.size();
        std::vector<CGFloat> components(count * 4);
        std::vector<CGFloat> locations(count);
        for (size_t i = 0; i < count; i++) {
            components[i * 4 + 0] = linear->stops[i].r;
            components[i * 4 + 1] = linear->stops[i].g;
            components[i * 4 + 2] = linear->stops[i].b;
            components[i * 4 + 3] = linear->stops[i].a * currentOpacity_;
            locations[i] = linear->stops[i].position;
        }
        CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGGradientRef gradient = CGGradientCreateWithColorComponents(
            cs, components.data(), locations.data(), count);
        CGContextDrawLinearGradient(ctx, gradient,
            CGPointMake(linear->startX, linear->startY),
            CGPointMake(linear->endX, linear->endY),
            kCGGradientDrawsBeforeStartLocation | kCGGradientDrawsAfterEndLocation);
        CGGradientRelease(gradient);
        CGColorSpaceRelease(cs);
    } else if (auto* radial = dynamic_cast<MetalRadialGradientBrush*>(brush)) {
        size_t count = radial->stops.size();
        std::vector<CGFloat> components(count * 4);
        std::vector<CGFloat> locations(count);
        for (size_t i = 0; i < count; i++) {
            components[i * 4 + 0] = radial->stops[i].r;
            components[i * 4 + 1] = radial->stops[i].g;
            components[i * 4 + 2] = radial->stops[i].b;
            components[i * 4 + 3] = radial->stops[i].a * currentOpacity_;
            locations[i] = radial->stops[i].position;
        }
        CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGGradientRef gradient = CGGradientCreateWithColorComponents(
            cs, components.data(), locations.data(), count);
        float maxRadius = std::max(radial->radiusX, radial->radiusY);
        CGContextDrawRadialGradient(ctx, gradient,
            CGPointMake(radial->originX, radial->originY), 0,
            CGPointMake(radial->centerX, radial->centerY), maxRadius,
            kCGGradientDrawsBeforeStartLocation | kCGGradientDrawsAfterEndLocation);
        CGGradientRelease(gradient);
        CGColorSpaceRelease(cs);
    }
}

CGPathRef MetalRenderTarget::CreateRoundedRectPath(float x, float y, float w, float h, float rx, float ry)
{
    CGMutablePathRef path = CGPathCreateMutable();
    rx = std::min(rx, w * 0.5f);
    ry = std::min(ry, h * 0.5f);

    CGPathMoveToPoint(path, nullptr, x + rx, y);
    CGPathAddLineToPoint(path, nullptr, x + w - rx, y);
    CGPathAddArcToPoint(path, nullptr, x + w, y, x + w, y + ry, rx);
    CGPathAddLineToPoint(path, nullptr, x + w, y + h - ry);
    CGPathAddArcToPoint(path, nullptr, x + w, y + h, x + w - rx, y + h, rx);
    CGPathAddLineToPoint(path, nullptr, x + rx, y + h);
    CGPathAddArcToPoint(path, nullptr, x, y + h, x, y + h - ry, rx);
    CGPathAddLineToPoint(path, nullptr, x, y + ry);
    CGPathAddArcToPoint(path, nullptr, x, y, x + rx, y, rx);
    CGPathCloseSubpath(path);

    return path;
}

CGPathRef MetalRenderTarget::CreatePerCornerRoundedRectPath(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
    // Cap each corner radius to half the smaller side so a single oversized
    // corner can't run off the opposite edge.
    const float halfMin = std::min(w, h) * 0.5f;
    tl = std::max(0.0f, std::min(tl, halfMin));
    tr = std::max(0.0f, std::min(tr, halfMin));
    br = std::max(0.0f, std::min(br, halfMin));
    bl = std::max(0.0f, std::min(bl, halfMin));

    CGMutablePathRef path = CGPathCreateMutable();
    CGPathMoveToPoint(path, nullptr, x + tl, y);
    CGPathAddLineToPoint(path, nullptr, x + w - tr, y);
    CGPathAddArcToPoint(path, nullptr, x + w, y, x + w, y + tr, tr);
    CGPathAddLineToPoint(path, nullptr, x + w, y + h - br);
    CGPathAddArcToPoint(path, nullptr, x + w, y + h, x + w - br, y + h, br);
    CGPathAddLineToPoint(path, nullptr, x + bl, y + h);
    CGPathAddArcToPoint(path, nullptr, x, y + h, x, y + h - bl, bl);
    CGPathAddLineToPoint(path, nullptr, x, y + tl);
    CGPathAddArcToPoint(path, nullptr, x, y, x + tl, y, tl);
    CGPathCloseSubpath(path);
    return path;
}

CGPathRef MetalRenderTarget::BuildCommandPath(float startX, float startY,
    const float* commands, uint32_t commandLength, bool closed)
{
    CGMutablePathRef path = CGPathCreateMutable();
    CGPathMoveToPoint(path, nullptr, startX, startY);

    uint32_t i = 0;
    while (i < commandLength) {
        int tag = (int)commands[i];
        if (tag == 0 && i + 2 < commandLength) {
            // LineTo
            CGPathAddLineToPoint(path, nullptr, commands[i + 1], commands[i + 2]);
            i += 3;
        } else if (tag == 1 && i + 6 < commandLength) {
            // BezierTo
            CGPathAddCurveToPoint(path, nullptr,
                commands[i + 1], commands[i + 2],
                commands[i + 3], commands[i + 4],
                commands[i + 5], commands[i + 6]);
            i += 7;
        } else if (tag == 2 && i + 2 < commandLength) {
            // MoveTo: new sub-path
            CGPathMoveToPoint(path, nullptr, commands[i + 1], commands[i + 2]);
            i += 3;
        } else if (tag == 3 && i + 4 < commandLength) {
            // QuadTo
            CGPathAddQuadCurveToPoint(path, nullptr,
                commands[i + 1], commands[i + 2],
                commands[i + 3], commands[i + 4]);
            i += 5;
        } else if (tag == 5) {
            // ClosePath
            CGPathCloseSubpath(path);
            i += 1;
        } else {
            break;
        }
    }

    if (closed) CGPathCloseSubpath(path);
    return path;
}
#endif

void MetalRenderTarget::FillRectangle(float x, float y, float w, float h, Brush* brush)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    CGContextSaveGState(cgContext_);

    if (dynamic_cast<MetalSolidBrush*>(brush)) {
        ApplyBrush(cgContext_, brush, false);
        CGContextFillRect(cgContext_, CGRectMake(x, y, w, h));
    } else {
        CGContextClipToRect(cgContext_, CGRectMake(x, y, w, h));
        ApplyGradientFill(cgContext_, brush);
    }

    CGContextRestoreGState(cgContext_);
#else
    (void)x; (void)y; (void)w; (void)h; (void)brush;
#endif
}

void MetalRenderTarget::DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGContextStrokeRect(cgContext_, CGRectMake(x, y, w, h));
#else
    (void)x; (void)y; (void)w; (void)h; (void)brush; (void)strokeWidth;
#endif
}

void MetalRenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    CGContextSaveGState(cgContext_);
    CGPathRef path = CreateRoundedRectPath(x, y, w, h, rx, ry);

    if (dynamic_cast<MetalSolidBrush*>(brush)) {
        ApplyBrush(cgContext_, brush, false);
        CGContextAddPath(cgContext_, path);
        CGContextFillPath(cgContext_);
    } else {
        CGContextAddPath(cgContext_, path);
        CGContextClip(cgContext_);
        ApplyGradientFill(cgContext_, brush);
    }

    CGPathRelease(path);
    CGContextRestoreGState(cgContext_);
#else
    (void)x; (void)y; (void)w; (void)h; (void)rx; (void)ry; (void)brush;
#endif
}

void MetalRenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGPathRef path = CreateRoundedRectPath(x, y, w, h, rx, ry);
    CGContextAddPath(cgContext_, path);
    CGContextStrokePath(cgContext_);
    CGPathRelease(path);
#else
    (void)x; (void)y; (void)w; (void)h; (void)rx; (void)ry; (void)brush; (void)strokeWidth;
#endif
}

void MetalRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    CGContextSaveGState(cgContext_);
    CGRect rect = CGRectMake(cx - rx, cy - ry, rx * 2, ry * 2);

    if (dynamic_cast<MetalSolidBrush*>(brush)) {
        ApplyBrush(cgContext_, brush, false);
        CGContextFillEllipseInRect(cgContext_, rect);
    } else {
        CGContextAddEllipseInRect(cgContext_, rect);
        CGContextClip(cgContext_);
        ApplyGradientFill(cgContext_, brush);
    }

    CGContextRestoreGState(cgContext_);
#else
    (void)cx; (void)cy; (void)rx; (void)ry; (void)brush;
#endif
}

void MetalRenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGContextStrokeEllipseInRect(cgContext_, CGRectMake(cx - rx, cy - ry, rx * 2, ry * 2));
#else
    (void)cx; (void)cy; (void)rx; (void)ry; (void)brush; (void)strokeWidth;
#endif
}

void MetalRenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGContextMoveToPoint(cgContext_, x1, y1);
    CGContextAddLineToPoint(cgContext_, x2, y2);
    CGContextStrokePath(cgContext_);
#else
    (void)x1; (void)y1; (void)x2; (void)y2; (void)brush; (void)strokeWidth;
#endif
}

void MetalRenderTarget::FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush || pointCount < 3) return;
    CGContextSaveGState(cgContext_);

    CGContextMoveToPoint(cgContext_, points[0], points[1]);
    for (uint32_t i = 1; i < pointCount; i++) {
        CGContextAddLineToPoint(cgContext_, points[i * 2], points[i * 2 + 1]);
    }
    CGContextClosePath(cgContext_);

    if (dynamic_cast<MetalSolidBrush*>(brush)) {
        ApplyBrush(cgContext_, brush, false);
        if (fillRule == 0) {
            CGContextEOFillPath(cgContext_);
        } else {
            CGContextFillPath(cgContext_);
        }
    } else {
        if (fillRule == 0) {
            CGContextEOClip(cgContext_);
        } else {
            CGContextClip(cgContext_);
        }
        ApplyGradientFill(cgContext_, brush);
    }

    CGContextRestoreGState(cgContext_);
#else
    (void)points; (void)pointCount; (void)brush; (void)fillRule;
#endif
}

void MetalRenderTarget::DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush || pointCount < 2) return;
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);

    CGContextMoveToPoint(cgContext_, points[0], points[1]);
    for (uint32_t i = 1; i < pointCount; i++) {
        CGContextAddLineToPoint(cgContext_, points[i * 2], points[i * 2 + 1]);
    }
    if (closed) CGContextClosePath(cgContext_);
    CGContextStrokePath(cgContext_);
#else
    (void)points; (void)pointCount; (void)brush; (void)strokeWidth; (void)closed;
#endif
}

void MetalRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    (void)edgeMode;  // Metal backend AA: CoreGraphics enables AA on graphics state below.
    CGContextSaveGState(cgContext_);

    CGPathRef path = BuildCommandPath(startX, startY, commands, commandLength, true);
    CGContextAddPath(cgContext_, path);

    if (dynamic_cast<MetalSolidBrush*>(brush)) {
        ApplyBrush(cgContext_, brush, false);
        if (fillRule == 0) {
            CGContextEOFillPath(cgContext_);
        } else {
            CGContextFillPath(cgContext_);
        }
    } else {
        if (fillRule == 0) {
            CGContextEOClip(cgContext_);
        } else {
            CGContextClip(cgContext_);
        }
        ApplyGradientFill(cgContext_, brush);
    }

    CGPathRelease(path);
    CGContextRestoreGState(cgContext_);
#else
    (void)startX; (void)startY; (void)commands; (void)commandLength; (void)brush; (void)fillRule;
#endif
}

void MetalRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset, int32_t edgeMode)
{
#ifdef __APPLE__
    if (!cgContext_ || !brush) return;
    (void)edgeMode;  // Metal backend AA: CoreGraphics enables AA on graphics state below.
    ApplyBrush(cgContext_, brush, true);
    CGContextSetLineWidth(cgContext_, strokeWidth);

    CGPathRef path = BuildCommandPath(startX, startY, commands, commandLength, closed);
    CGContextAddPath(cgContext_, path);
    CGContextStrokePath(cgContext_);
    CGPathRelease(path);
#else
    (void)startX; (void)startY; (void)commands; (void)commandLength; (void)brush; (void)strokeWidth; (void)closed;
#endif
}

void MetalRenderTarget::DrawContentBorder(float x, float y, float w, float h,
    float blRadius, float brRadius,
    Brush* fillBrush, Brush* strokeBrush, float strokeWidth)
{
#ifdef __APPLE__
    if (!cgContext_) return;

    // Fill with bottom-rounded corners
    if (fillBrush) {
        CGContextSaveGState(cgContext_);
        CGMutablePathRef path = CGPathCreateMutable();
        CGPathMoveToPoint(path, nullptr, x, y);
        CGPathAddLineToPoint(path, nullptr, x + w, y);
        CGPathAddLineToPoint(path, nullptr, x + w, y + h - brRadius);
        CGPathAddArcToPoint(path, nullptr, x + w, y + h, x + w - brRadius, y + h, brRadius);
        CGPathAddLineToPoint(path, nullptr, x + blRadius, y + h);
        CGPathAddArcToPoint(path, nullptr, x, y + h, x, y + h - blRadius, blRadius);
        CGPathCloseSubpath(path);

        if (dynamic_cast<MetalSolidBrush*>(fillBrush)) {
            ApplyBrush(cgContext_, fillBrush, false);
            CGContextAddPath(cgContext_, path);
            CGContextFillPath(cgContext_);
        } else {
            CGContextAddPath(cgContext_, path);
            CGContextClip(cgContext_);
            ApplyGradientFill(cgContext_, fillBrush);
        }
        CGPathRelease(path);
        CGContextRestoreGState(cgContext_);
    }

    // Stroke U-shape (left + bottom + right, no top)
    if (strokeBrush) {
        ApplyBrush(cgContext_, strokeBrush, true);
        CGContextSetLineWidth(cgContext_, strokeWidth);

        CGContextMoveToPoint(cgContext_, x, y);
        CGContextAddLineToPoint(cgContext_, x, y + h - blRadius);
        CGContextAddArcToPoint(cgContext_, x, y + h, x + blRadius, y + h, blRadius);
        CGContextAddLineToPoint(cgContext_, x + w - brRadius, y + h);
        CGContextAddArcToPoint(cgContext_, x + w, y + h, x + w, y + h - brRadius, brRadius);
        CGContextAddLineToPoint(cgContext_, x + w, y);
        CGContextStrokePath(cgContext_);
    }
#else
    (void)x; (void)y; (void)w; (void)h; (void)blRadius; (void)brRadius;
    (void)fillBrush; (void)strokeBrush; (void)strokeWidth;
#endif
}

void MetalRenderTarget::RenderText(
    const wchar_t* text, uint32_t textLength,
    TextFormat* format,
    float x, float y, float w, float h,
    Brush* brush)
{
#ifdef __APPLE__
    if (!cgContext_ || !text || textLength == 0 || !format || !brush) return;

    auto* mtf = dynamic_cast<MetalTextFormat*>(format);
    if (!mtf || !mtf->font) return;

    CGContextSaveGState(cgContext_);

    // CoreText renders bottom-up; we need to flip for our top-down coordinate system
    CGContextSaveGState(cgContext_);
    CGContextTranslateCTM(cgContext_, x, y + h);
    CGContextScaleCTM(cgContext_, 1, -1);

    // Create attributed string
    CFMutableStringRef cfStr = CFStringCreateMutable(kCFAllocatorDefault, 0);
    for (uint32_t i = 0; i < textLength; i++) {
        UniChar ch = (UniChar)text[i];
        CFStringAppendCharacters(cfStr, &ch, 1);
    }

    CFMutableDictionaryRef attrs = CFDictionaryCreateMutable(
        kCFAllocatorDefault, 2, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    CFDictionarySetValue(attrs, kCTFontAttributeName, mtf->font);

    // Set text color
    if (auto* solid = dynamic_cast<MetalSolidBrush*>(brush)) {
        CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGFloat components[] = { solid->r, solid->g, solid->b, solid->a * currentOpacity_ };
        CGColorRef color = CGColorCreate(cs, components);
        CFDictionarySetValue(attrs, kCTForegroundColorAttributeName, color);
        CGColorRelease(color);
        CGColorSpaceRelease(cs);
    } else {
        // Fallback text color for non-solid brushes or missing brush state.
        CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGFloat components[] = { 0.0f, 0.0f, 0.0f, currentOpacity_ };
        CGColorRef color = CGColorCreate(cs, components);
        CFDictionarySetValue(attrs, kCTForegroundColorAttributeName, color);
        CGColorRelease(color);
        CGColorSpaceRelease(cs);
    }

    // Set paragraph style for alignment and trimming
    CTParagraphStyleSetting settings[2];
    int settingCount = 0;

    CTTextAlignment ctAlignment;
    switch (mtf->alignment) {
        case 1: ctAlignment = kCTTextAlignmentRight; break;
        case 2: ctAlignment = kCTTextAlignmentCenter; break;
        case 3: ctAlignment = kCTTextAlignmentJustified; break;
        default: ctAlignment = kCTTextAlignmentLeft; break;
    }
    settings[settingCount++] = {
        kCTParagraphStyleSpecifierAlignment,
        sizeof(ctAlignment), &ctAlignment
    };

    CTLineBreakMode lineBreakMode;
    switch (mtf->trimming) {
        case 1: lineBreakMode = kCTLineBreakByCharWrapping; break;
        case 2: lineBreakMode = kCTLineBreakByWordWrapping; break;
        default: lineBreakMode = kCTLineBreakByClipping; break;
    }
    settings[settingCount++] = {
        kCTParagraphStyleSpecifierLineBreakMode,
        sizeof(lineBreakMode), &lineBreakMode
    };

    CTParagraphStyleRef paragraphStyle = CTParagraphStyleCreate(settings, settingCount);
    CFDictionarySetValue(attrs, kCTParagraphStyleAttributeName, paragraphStyle);

    CFAttributedStringRef attrStr = CFAttributedStringCreate(kCFAllocatorDefault, cfStr, attrs);

    // Draw using CTFramesetter for proper line wrapping
    CTFramesetterRef framesetter = CTFramesetterCreateWithAttributedString(attrStr);
    CGPathRef path = CGPathCreateWithRect(CGRectMake(0, 0, w, h), nullptr);
    CTFrameRef frame = CTFramesetterCreateFrame(framesetter, CFRangeMake(0, 0), path, nullptr);
    CTFrameDraw(frame, cgContext_);

    CFRelease(frame);
    CGPathRelease(path);
    CFRelease(framesetter);
    CFRelease(attrStr);
    CFRelease(paragraphStyle);
    CFRelease(attrs);
    CFRelease(cfStr);

    CGContextRestoreGState(cgContext_);
    CGContextRestoreGState(cgContext_);
#else
    (void)text; (void)textLength; (void)format;
    (void)x; (void)y; (void)w; (void)h; (void)brush;
#endif
}

void MetalRenderTarget::PushTransform(const float* matrix)
{
    // Save current state
    MetalTransformState state;
    std::memcpy(state.matrix, matrix, sizeof(float) * 6);
    transformStack_.push(state);

#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        // Apply 3x2 column-major matrix: [m11,m12, m21,m22, m31,m32]
        CGAffineTransform t = CGAffineTransformMake(
            matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
        CGContextConcatCTM(cgContext_, t);
    }
#endif
}

void MetalRenderTarget::PopTransform()
{
    if (transformStack_.empty()) return;
    transformStack_.pop();

#ifdef __APPLE__
    if (cgContext_) {
        CGContextRestoreGState(cgContext_);
    }
#endif
}

void MetalRenderTarget::PushClip(float x, float y, float w, float h)
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        CGContextClipToRect(cgContext_, CGRectMake(x, y, w, h));
    }
#else
    (void)x; (void)y; (void)w; (void)h;
#endif
    clipDepth_++;
}

void MetalRenderTarget::PopClip()
{
    if (clipDepth_ <= 0) return;
    clipDepth_--;

#ifdef __APPLE__
    if (cgContext_) {
        CGContextRestoreGState(cgContext_);
    }
#endif
}

void MetalRenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry)
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        CGPathRef path = CreateRoundedRectPath(x, y, w, h, rx, ry);
        CGContextAddPath(cgContext_, path);
        CGContextClip(cgContext_);
        CGPathRelease(path);
    }
#else
    (void)x; (void)y; (void)w; (void)h; (void)rx; (void)ry;
#endif
    clipDepth_++;
}

void MetalRenderTarget::PushPerCornerRoundedRectClip(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        CGPathRef path = CreatePerCornerRoundedRectPath(x, y, w, h, tl, tr, br, bl);
        CGContextAddPath(cgContext_, path);
        CGContextClip(cgContext_);
        CGPathRelease(path);
    }
#else
    (void)x; (void)y; (void)w; (void)h; (void)tl; (void)tr; (void)br; (void)bl;
#endif
    clipDepth_++;
}

void MetalRenderTarget::PunchTransparentRect(float x, float y, float w, float h)
{
#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        CGContextSetBlendMode(cgContext_, kCGBlendModeClear);
        CGContextFillRect(cgContext_, CGRectMake(x, y, w, h));
        CGContextRestoreGState(cgContext_);
    }
#else
    (void)x; (void)y; (void)w; (void)h;
#endif
}

void MetalRenderTarget::PushOpacity(float opacity)
{
    opacityStack_.push(currentOpacity_);
    currentOpacity_ *= opacity;

#ifdef __APPLE__
    if (cgContext_) {
        CGContextSaveGState(cgContext_);
        CGContextSetAlpha(cgContext_, currentOpacity_);
    }
#endif
}

void MetalRenderTarget::PopOpacity()
{
    if (opacityStack_.empty()) return;

#ifdef __APPLE__
    if (cgContext_) {
        CGContextRestoreGState(cgContext_);
    }
#endif

    currentOpacity_ = opacityStack_.top();
    opacityStack_.pop();
}

void MetalRenderTarget::SetShapeType(int /*type*/, float /*n*/) {}

void MetalRenderTarget::SetVSyncEnabled(bool enabled)
{
    vsyncEnabled_ = enabled;
#ifdef __APPLE__
    if (metalLayer_) {
        CAMetalLayer* layer = (__bridge CAMetalLayer*)metalLayer_;
        layer.displaySyncEnabled = enabled;
    }
#endif
}

void MetalRenderTarget::SetDpi(float dpiX, float dpiY)
{
    dpiX_ = dpiX;
    dpiY_ = dpiY;
#ifdef __APPLE__
    if (metalLayer_) {
        CAMetalLayer* layer = (__bridge CAMetalLayer*)metalLayer_;
        layer.contentsScale = dpiX / 96.0;
    }
#endif
}

void MetalRenderTarget::AddDirtyRect(float x, float y, float w, float h)
{
    // Dirty rect tracking - Metal backend does full redraws for simplicity
    (void)x; (void)y; (void)w; (void)h;
}

void MetalRenderTarget::SetFullInvalidation()
{
    fullInvalidation_ = true;
}

void MetalRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
#ifdef __APPLE__
    if (!cgContext_ || !bitmap) return;

    auto* mb = dynamic_cast<MetalBitmap*>(bitmap);
    if (!mb || !mb->cgImage) return;

    CGContextSaveGState(cgContext_);
    CGContextSetAlpha(cgContext_, opacity * currentOpacity_);

    // CGContextDrawImage uses bottom-up coordinates; flip locally
    CGContextTranslateCTM(cgContext_, x, y + h);
    CGContextScaleCTM(cgContext_, 1, -1);
    CGContextDrawImage(cgContext_, CGRectMake(0, 0, w, h), mb->cgImage);

    CGContextRestoreGState(cgContext_);
#else
    (void)bitmap; (void)x; (void)y; (void)w; (void)h; (void)opacity;
#endif
}

void MetalRenderTarget::DrawBackdropFilter(
    float x, float y, float w, float h,
    const char*, const char*, const char*,
    float tintOpacity, float blurRadius,
    float, float, float, float)
{
    // Basic tint overlay — advanced blur/material effects require compute shaders
#ifdef __APPLE__
    if (!cgContext_) return;
    CGContextSaveGState(cgContext_);
    CGContextSetRGBFillColor(cgContext_, 0.5, 0.5, 0.5, tintOpacity * currentOpacity_);
    CGContextFillRect(cgContext_, CGRectMake(x, y, w, h));
    CGContextRestoreGState(cgContext_);
#else
    (void)x; (void)y; (void)w; (void)h; (void)tintOpacity; (void)blurRadius;
#endif
}

void MetalRenderTarget::DrawGlowingBorderHighlight(
    float x, float y, float w, float h,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float, float dimOpacity,
    float screenWidth, float screenHeight)
{
#ifdef __APPLE__
    if (!cgContext_) return;
    CGContextSaveGState(cgContext_);

    // Dim overlay
    CGContextSetRGBFillColor(cgContext_, 0, 0, 0, dimOpacity * currentOpacity_);
    CGContextFillRect(cgContext_, CGRectMake(0, 0, screenWidth, screenHeight));

    // Punch hole for highlighted element
    CGContextSetBlendMode(cgContext_, kCGBlendModeClear);
    CGContextFillRect(cgContext_, CGRectMake(x, y, w, h));
    CGContextSetBlendMode(cgContext_, kCGBlendModeNormal);

    // Animated glow border
    float alpha = 0.5f + 0.5f * sinf(animationPhase * 2.0f * (float)M_PI);
    CGContextSetRGBStrokeColor(cgContext_, glowColorR, glowColorG, glowColorB, alpha * currentOpacity_);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGContextStrokeRect(cgContext_, CGRectMake(x, y, w, h));

    CGContextRestoreGState(cgContext_);
#else
    (void)x; (void)y; (void)w; (void)h; (void)animationPhase;
    (void)glowColorR; (void)glowColorG; (void)glowColorB;
    (void)strokeWidth; (void)dimOpacity; (void)screenWidth; (void)screenHeight;
#endif
}

void MetalRenderTarget::DrawGlowingBorderTransition(
    float fromX, float fromY, float fromW, float fromH,
    float toX, float toY, float toW, float toH,
    float headProgress, float tailProgress,
    float animationPhase,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float, float dimOpacity,
    float screenWidth, float screenHeight)
{
#ifdef __APPLE__
    if (!cgContext_) return;
    CGContextSaveGState(cgContext_);

    // Interpolate between from/to rectangles
    float t = (headProgress + tailProgress) * 0.5f;
    float cx = fromX + (toX - fromX) * t;
    float cy = fromY + (toY - fromY) * t;
    float cw = fromW + (toW - fromW) * t;
    float ch = fromH + (toH - fromH) * t;

    // Dim overlay
    CGContextSetRGBFillColor(cgContext_, 0, 0, 0, dimOpacity * currentOpacity_);
    CGContextFillRect(cgContext_, CGRectMake(0, 0, screenWidth, screenHeight));

    // Punch hole
    CGContextSetBlendMode(cgContext_, kCGBlendModeClear);
    CGContextFillRect(cgContext_, CGRectMake(cx, cy, cw, ch));
    CGContextSetBlendMode(cgContext_, kCGBlendModeNormal);

    // Glow
    float alpha = 0.5f + 0.5f * sinf(animationPhase * 2.0f * (float)M_PI);
    CGContextSetRGBStrokeColor(cgContext_, glowColorR, glowColorG, glowColorB, alpha * currentOpacity_);
    CGContextSetLineWidth(cgContext_, strokeWidth);
    CGContextStrokeRect(cgContext_, CGRectMake(cx, cy, cw, ch));

    CGContextRestoreGState(cgContext_);
#else
    (void)fromX; (void)fromY; (void)fromW; (void)fromH;
    (void)toX; (void)toY; (void)toW; (void)toH;
    (void)headProgress; (void)tailProgress; (void)animationPhase;
    (void)glowColorR; (void)glowColorG; (void)glowColorB;
    (void)strokeWidth; (void)dimOpacity; (void)screenWidth; (void)screenHeight;
#endif
}

void MetalRenderTarget::DrawRippleEffect(
    float x, float y, float w, float h,
    float rippleProgress,
    float glowColorR, float glowColorG, float glowColorB,
    float strokeWidth, float dimOpacity,
    float screenWidth, float screenHeight)
{
#ifdef __APPLE__
    if (!cgContext_) return;
    CGContextSaveGState(cgContext_);

    // Expanding ripple from element border
    float expansion = rippleProgress * 20.0f;
    float alpha = (1.0f - rippleProgress) * currentOpacity_;

    // Dim
    CGContextSetRGBFillColor(cgContext_, 0, 0, 0, dimOpacity * (1.0f - rippleProgress) * currentOpacity_);
    CGContextFillRect(cgContext_, CGRectMake(0, 0, screenWidth, screenHeight));

    // Ripple border
    CGContextSetRGBStrokeColor(cgContext_, glowColorR, glowColorG, glowColorB, alpha);
    CGContextSetLineWidth(cgContext_, strokeWidth * (1.0f - rippleProgress * 0.5f));
    CGContextStrokeRect(cgContext_, CGRectMake(
        x - expansion, y - expansion,
        w + expansion * 2, h + expansion * 2));

    CGContextRestoreGState(cgContext_);
#else
    (void)x; (void)y; (void)w; (void)h; (void)rippleProgress;
    (void)glowColorR; (void)glowColorG; (void)glowColorB;
    (void)strokeWidth; (void)dimOpacity; (void)screenWidth; (void)screenHeight;
#endif
}

// ============================================================================
// MetalBackend
// ============================================================================

MetalBackend::MetalBackend() = default;

MetalBackend::~MetalBackend()
{
#ifdef __APPLE__
    device_ = nullptr;
#endif
}

bool MetalBackend::Initialize()
{
    if (initialized_) return true;

#ifdef __APPLE__
    @autoreleasepool {
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        if (!device) return false;
        device_ = (__bridge_retained void*)device;
        initialized_ = true;
        return true;
    }
#else
    return false;
#endif
}

JaliumResult MetalBackend::CheckDeviceStatus()
{
#ifdef __APPLE__
    return device_ ? JALIUM_OK : JALIUM_ERROR_DEVICE_LOST;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

RenderTarget* MetalBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    if (!Initialize()) return nullptr;

    auto* rt = new MetalRenderTarget(width, height);
    if (!rt->Initialize(hwnd)) {
        delete rt;
        return nullptr;
    }
    return rt;
}

RenderTarget* MetalBackend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height)
{
    // On macOS, composition is handled by CAMetalLayer which is the default path
    return CreateRenderTarget(hwnd, width, height);
}

Brush* MetalBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    return new MetalSolidBrush(r, g, b, a);
}

Brush* MetalBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new MetalLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* MetalBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new MetalRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
}

TextFormat* MetalBackend::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
#ifdef __APPLE__
    // Convert wchar_t font family name to CFString
    CFMutableStringRef cfFamily = CFStringCreateMutable(kCFAllocatorDefault, 0);
    if (fontFamily) {
        for (int i = 0; fontFamily[i]; i++) {
            UniChar ch = (UniChar)fontFamily[i];
            CFStringAppendCharacters(cfFamily, &ch, 1);
        }
    } else {
        CFStringAppend(cfFamily, CFSTR(".AppleSystemUIFont"));
    }

    // Map font weight to CoreText weight trait
    CGFloat ctWeight = 0.0;
    if (fontWeight <= 100) ctWeight = -0.8;
    else if (fontWeight <= 200) ctWeight = -0.6;
    else if (fontWeight <= 300) ctWeight = -0.4;
    else if (fontWeight <= 400) ctWeight = 0.0;
    else if (fontWeight <= 500) ctWeight = 0.23;
    else if (fontWeight <= 600) ctWeight = 0.3;
    else if (fontWeight <= 700) ctWeight = 0.4;
    else if (fontWeight <= 800) ctWeight = 0.56;
    else ctWeight = 0.62;

    // Create font with traits
    CTFontSymbolicTraits symbolicTraits = 0;
    if (fontStyle == 1 || fontStyle == 2) {
        symbolicTraits |= kCTFontItalicTrait;
    }
    if (fontWeight >= 700) {
        symbolicTraits |= kCTFontBoldTrait;
    }

    CTFontRef baseFont = CTFontCreateWithName(cfFamily, fontSize, nullptr);
    CTFontRef font = baseFont;

    if (symbolicTraits != 0) {
        CTFontRef traitFont = CTFontCreateCopyWithSymbolicTraits(
            baseFont, fontSize, nullptr, symbolicTraits, symbolicTraits);
        if (traitFont) {
            CFRelease(baseFont);
            font = traitFont;
        }
    }

    CFRelease(cfFamily);
    return new MetalTextFormat(font, fontSize);
#else
    (void)fontFamily; (void)fontWeight; (void)fontStyle;
    return new MetalTextFormat(fontSize);
#endif
}

Bitmap* MetalBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize)
{
#ifdef __APPLE__
    if (!data || dataSize == 0) return nullptr;

    CFDataRef cfData = CFDataCreate(kCFAllocatorDefault, data, dataSize);
    CGImageSourceRef imageSource = CGImageSourceCreateWithData(cfData, nullptr);
    if (!imageSource) {
        CFRelease(cfData);
        return nullptr;
    }

    CGImageRef cgImage = CGImageSourceCreateImageAtIndex(imageSource, 0, nullptr);
    if (!cgImage) {
        CFRelease(imageSource);
        CFRelease(cfData);
        return nullptr;
    }

    uint32_t width = (uint32_t)CGImageGetWidth(cgImage);
    uint32_t height = (uint32_t)CGImageGetHeight(cgImage);

    // Decode to BGRA8
    std::vector<uint8_t> pixels(width * height * 4);
    CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef ctx = CGBitmapContextCreate(
        pixels.data(), width, height, 8, width * 4,
        cs, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    CGContextDrawImage(ctx, CGRectMake(0, 0, width, height), cgImage);
    CGContextRelease(ctx);
    CGColorSpaceRelease(cs);

    auto* bitmap = new MetalBitmap(width, height, std::move(pixels), cgImage);
    CGImageRelease(cgImage);  // MetalBitmap retains cgImage in its constructor, safe to release here
    CFRelease(imageSource);
    CFRelease(cfData);

    return bitmap;
#else
    (void)data; (void)dataSize;
    return nullptr;
#endif
}

Bitmap* MetalBackend::CreateBitmapFromPixels(
    const uint8_t* pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride)
{
#ifdef __APPLE__
    if (!pixels || width == 0 || height == 0) return nullptr;

    std::vector<uint8_t> pixelData(width * height * 4);
    for (uint32_t y = 0; y < height; y++) {
        std::memcpy(pixelData.data() + y * width * 4,
                    pixels + y * stride,
                    width * 4);
    }

    CGColorSpaceRef cs = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef ctx = CGBitmapContextCreate(
        pixelData.data(), width, height, 8, width * 4,
        cs, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    CGImageRef cgImage = CGBitmapContextCreateImage(ctx);
    CGContextRelease(ctx);
    CGColorSpaceRelease(cs);

    auto* bitmap = new MetalBitmap(width, height, std::move(pixelData), cgImage);
    CGImageRelease(cgImage);
    return bitmap;
#else
    (void)pixels; (void)width; (void)height; (void)stride;
    return nullptr;
#endif
}

IRenderBackend* CreateMetalBackend()
{
    return new MetalBackend();
}

} // namespace jalium
