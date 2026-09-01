#include "metal_backend.h"
#include "metal_internal.h"
#include "jalium_string_util.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>
#include <mutex>
#include <string>
#include <utility>

#ifdef __APPLE__
#import <TargetConditionals.h>
#import <CoreGraphics/CoreGraphics.h>
#import <CoreText/CoreText.h>
#import <ImageIO/ImageIO.h>
#import <IOSurface/IOSurface.h>
#endif

namespace jalium {

namespace {

uint32_t NormalizeSpread(uint32_t value) noexcept
{
    return value <= 2 ? value : 0;
}

#ifdef __APPLE__
CFStringRef CreateCFString(const wchar_t* value, uint32_t length)
{
    if (!value) return nullptr;
    std::vector<UniChar> utf16;
    utf16.reserve(length + 1);
    for (uint32_t i = 0; i < length; ++i) {
        uint32_t cp = static_cast<uint32_t>(value[i]);
        if constexpr (sizeof(wchar_t) == 2) {
            utf16.push_back(static_cast<UniChar>(cp));
        } else if (cp <= 0xffffu) {
            if (cp >= 0xd800u && cp <= 0xdfffu) cp = 0xfffdu;
            utf16.push_back(static_cast<UniChar>(cp));
        } else if (cp <= 0x10ffffu) {
            cp -= 0x10000u;
            utf16.push_back(static_cast<UniChar>(0xd800u + (cp >> 10)));
            utf16.push_back(static_cast<UniChar>(0xdc00u + (cp & 0x3ffu)));
        } else {
            utf16.push_back(static_cast<UniChar>(0xfffdu));
        }
    }
    return CFStringCreateWithCharacters(kCFAllocatorDefault, utf16.data(),
        static_cast<CFIndex>(utf16.size()));
}

CFStringRef CreateCFString(const wchar_t* value)
{
    if (!value) return nullptr;
    uint32_t length = 0;
    while (value[length] != 0 && length < (1u << 20)) ++length;
    return CreateCFString(value, length);
}

uint32_t Utf16IndexToWide(const wchar_t* text, uint32_t length, CFIndex utf16Index)
{
    if (utf16Index <= 0) return 0;
    if constexpr (sizeof(wchar_t) == 2) {
        return static_cast<uint32_t>(std::min<CFIndex>(utf16Index, length));
    }
    CFIndex consumed = 0;
    for (uint32_t i = 0; i < length; ++i) {
        uint32_t cp = static_cast<uint32_t>(text[i]);
        CFIndex units = cp > 0xffffu && cp <= 0x10ffffu ? 2 : 1;
        if (consumed + units > utf16Index) return i;
        consumed += units;
        if (consumed == utf16Index) return i + 1;
    }
    return length;
}

CFIndex WideIndexToUtf16(const wchar_t* text, uint32_t length, uint32_t wideIndex)
{
    wideIndex = std::min(wideIndex, length);
    if constexpr (sizeof(wchar_t) == 2) return static_cast<CFIndex>(wideIndex);
    CFIndex result = 0;
    for (uint32_t i = 0; i < wideIndex; ++i) {
        uint32_t cp = static_cast<uint32_t>(text[i]);
        result += cp > 0xffffu && cp <= 0x10ffffu ? 2 : 1;
    }
    return result;
}

void ReleaseCF(const void* object)
{
    if (object) CFRelease(object);
}

constexpr const char* kInkShaderSource = R"METAL(
#include <metal_stdlib>
using namespace metal;
struct StrokePoint { float2 position; float pressure; float pad; };
kernel void jalium_ink(
    texture2d<float, access::read_write> target [[texture(0)]],
    const device StrokePoint* points [[buffer(0)]],
    constant uchar* rawConstants [[buffer(1)]],
    constant uint& blendMode [[buffer(2)]],
    uint2 gid [[thread_position_in_grid]])
{
    if (gid.x >= target.get_width() || gid.y >= target.get_height()) return;
    constant float* f = reinterpret_cast<constant float*>(rawConstants);
    constant uint* u = reinterpret_cast<constant uint*>(rawConstants);
    float4 color = float4(f[0], f[1], f[2], f[3]);
    float width = max(f[4], 0.25);
    uint count = u[12];
    bool ignorePressure = u[18] != 0u;
    if (count == 0u) return;
    float2 pixel = float2(gid) + 0.5;
    float coverage = 0.0;
    if (count == 1u) {
        float pressure = ignorePressure ? 1.0 : max(points[0].pressure, 0.01);
        coverage = 1.0 - smoothstep(width * pressure * 0.5 - 0.75,
                                   width * pressure * 0.5 + 0.75,
                                   distance(pixel, points[0].position));
    } else {
        for (uint i = 0u; i + 1u < count; ++i) {
            float2 a = points[i].position;
            float2 b = points[i + 1u].position;
            float2 ab = b - a;
            float t = clamp(dot(pixel - a, ab) / max(dot(ab, ab), 1e-6), 0.0, 1.0);
            float pressure = ignorePressure ? 1.0
                : max(mix(points[i].pressure, points[i + 1u].pressure, t), 0.01);
            float radius = width * pressure * 0.5;
            float c = 1.0 - smoothstep(radius - 0.75, radius + 0.75,
                                       distance(pixel, a + ab * t));
            coverage = max(coverage, c);
        }
    }
    if (coverage <= 0.0) return;
    color *= coverage;
    float4 dst = target.read(gid);
    float4 outColor;
    if (blendMode == 1u) outColor = min(dst + color, 1.0);
    else if (blendMode == 2u) outColor = dst * (1.0 - color.a);
    else outColor = color + dst * (1.0 - color.a);
    target.write(outColor, gid);
}
)METAL";
#endif

} // namespace

MetalLinearGradientBrush::MetalLinearGradientBrush(float sx, float sy, float ex,
    float ey, const JaliumGradientStop* values, uint32_t count, uint32_t spread)
    : startX(sx), startY(sy), endX(ex), endY(ey),
      spreadMethod(NormalizeSpread(spread))
{
    if (values && count) stops.assign(values, values + count);
}

MetalRadialGradientBrush::MetalRadialGradientBrush(float cx, float cy, float rx,
    float ry, float ox, float oy, const JaliumGradientStop* values,
    uint32_t count, uint32_t spread)
    : centerX(cx), centerY(cy), radiusX(rx), radiusY(ry), originX(ox),
      originY(oy), spreadMethod(NormalizeSpread(spread))
{
    if (values && count) stops.assign(values, values + count);
}

struct MetalTextFormat::Impl {
#ifdef __APPLE__
    CTFontRef font = nullptr;
#endif
    float size = 12.0f;
    int32_t alignment = 0;
    int32_t paragraphAlignment = 0;
    int32_t trimming = 0;
    int32_t wrapping = 0;
    int32_t lineSpacingMethod = 0;
    float lineSpacing = 0;
    float lineSpacingBaseline = 0;
    uint32_t maxLines = 0;

#ifdef __APPLE__
    CTParagraphStyleRef CreateParagraphStyle() const
    {
        CTTextAlignment ctAlignment = kCTTextAlignmentLeft;
        if (alignment == 1) ctAlignment = kCTTextAlignmentRight;
        else if (alignment == 2) ctAlignment = kCTTextAlignmentCenter;
        else if (alignment == 3) ctAlignment = kCTTextAlignmentJustified;

        CTLineBreakMode breakMode = kCTLineBreakByClipping;
        if (wrapping != 0) breakMode = kCTLineBreakByWordWrapping;
        if (trimming == 1) breakMode = kCTLineBreakByTruncatingCharacter;
        else if (trimming >= 2) breakMode = kCTLineBreakByTruncatingTail;

        CGFloat minLine = lineSpacingMethod != 0 && lineSpacing > 0
            ? lineSpacing : 0;
        CGFloat maxLine = minLine;
        CTParagraphStyleSetting settings[] = {
            {kCTParagraphStyleSpecifierAlignment, sizeof(ctAlignment), &ctAlignment},
            {kCTParagraphStyleSpecifierLineBreakMode, sizeof(breakMode), &breakMode},
            {kCTParagraphStyleSpecifierMinimumLineHeight, sizeof(minLine), &minLine},
            {kCTParagraphStyleSpecifierMaximumLineHeight, sizeof(maxLine), &maxLine},
        };
        const size_t count = minLine > 0 ? 4 : 2;
        return CTParagraphStyleCreate(settings, count);
    }

    CFAttributedStringRef CreateAttributed(const wchar_t* text,
        uint32_t length, CGColorRef color = nullptr) const
    {
        if (!font || !text) return nullptr;
        CFStringRef string = CreateCFString(text, length);
        if (!string) return nullptr;
        CTParagraphStyleRef paragraph = CreateParagraphStyle();
        CFMutableDictionaryRef attributes = CFDictionaryCreateMutable(
            kCFAllocatorDefault, 3, &kCFTypeDictionaryKeyCallBacks,
            &kCFTypeDictionaryValueCallBacks);
        CFDictionarySetValue(attributes, kCTFontAttributeName, font);
        if (paragraph)
            CFDictionarySetValue(attributes, kCTParagraphStyleAttributeName, paragraph);
        if (color)
            CFDictionarySetValue(attributes, kCTForegroundColorAttributeName, color);
        CFAttributedStringRef result = CFAttributedStringCreate(
            kCFAllocatorDefault, string, attributes);
        ReleaseCF(attributes);
        ReleaseCF(paragraph);
        ReleaseCF(string);
        return result;
    }
#endif
};

MetalTextFormat::MetalTextFormat(const wchar_t* family, float size,
    int32_t weight, int32_t style) : impl_(std::make_unique<Impl>())
{
    impl_->size = size;
#ifdef __APPLE__
    CFStringRef familyName = family ? CreateCFString(family) : nullptr;
    if (!familyName || CFStringGetLength(familyName) == 0) {
        ReleaseCF(familyName);
        familyName = CFRetain(CFSTR(".AppleSystemUIFont"));
    }
    CTFontRef base = CTFontCreateWithName(familyName, size, nullptr);
    ReleaseCF(familyName);
    if (!base) return;

    CTFontSymbolicTraits desired = 0;
    if (style == 1 || style == 2) desired |= kCTFontItalicTrait;
    if (weight >= 600) desired |= kCTFontBoldTrait;
    CTFontRef traits = desired == 0 ? nullptr : CTFontCreateCopyWithSymbolicTraits(
        base, size, nullptr, desired, desired);
    impl_->font = traits ? traits : base;
    if (traits) CFRelease(base);
#else
    (void)family; (void)weight; (void)style;
#endif
}

MetalTextFormat::~MetalTextFormat()
{
#ifdef __APPLE__
    if (impl_ && impl_->font) CFRelease(impl_->font);
#endif
}

bool MetalTextFormat::IsValid() const
{
#ifdef __APPLE__
    return impl_ && impl_->font;
#else
    return false;
#endif
}

void MetalTextFormat::SetAlignment(int32_t value) { impl_->alignment = value; }
void MetalTextFormat::SetParagraphAlignment(int32_t value) { impl_->paragraphAlignment = value; }
void MetalTextFormat::SetTrimming(int32_t value) { impl_->trimming = value; }
void MetalTextFormat::SetWordWrapping(int32_t value) { impl_->wrapping = value; }
void MetalTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    impl_->lineSpacingMethod = method;
    impl_->lineSpacing = spacing;
    impl_->lineSpacingBaseline = baseline;
}
void MetalTextFormat::SetMaxLines(uint32_t value) { impl_->maxLines = value; }

JaliumResult MetalTextFormat::MeasureText(const wchar_t* text,
    uint32_t textLength, float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics || !text || textLength == 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    *metrics = {};
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CTFramesetterRef framesetter = CTFramesetterCreateWithAttributedString(attributed);
    CGSize constraint = CGSizeMake(maxWidth > 0 ? maxWidth : CGFLOAT_MAX,
        maxHeight > 0 ? maxHeight : CGFLOAT_MAX);
    CFRange fitted{};
    CGSize suggested = CTFramesetterSuggestFrameSizeWithConstraints(framesetter,
        CFRangeMake(0, 0), nullptr, constraint, &fitted);
    CGFloat ascent = CTFontGetAscent(impl_->font);
    CGFloat descent = CTFontGetDescent(impl_->font);
    CGFloat leading = CTFontGetLeading(impl_->font);
    float lineHeight = impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0
        ? impl_->lineSpacing : static_cast<float>(ascent + descent + leading);
    uint32_t lineCount = std::max(1u, static_cast<uint32_t>(
        std::ceil(suggested.height / std::max(lineHeight, 0.001f))));
    if (impl_->maxLines > 0) lineCount = std::min(lineCount, impl_->maxLines);
    metrics->width = static_cast<float>(std::ceil(suggested.width));
    metrics->widthIncludingTrailingWhitespace = metrics->width;
    metrics->height = std::min(static_cast<float>(std::ceil(suggested.height)),
        lineHeight * lineCount);
    metrics->lineHeight = lineHeight;
    metrics->baseline = impl_->lineSpacingBaseline > 0
        ? impl_->lineSpacingBaseline : static_cast<float>(ascent);
    metrics->ascent = static_cast<float>(ascent);
    metrics->descent = static_cast<float>(descent);
    metrics->lineGap = static_cast<float>(leading);
    metrics->lineCount = lineCount;
    ReleaseCF(framesetter);
    ReleaseCF(attributed);
    return JALIUM_OK;
#else
    metrics->lineHeight = impl_->size * 1.2f;
    metrics->height = metrics->lineHeight;
    metrics->baseline = impl_->size;
    metrics->ascent = impl_->size;
    metrics->descent = impl_->size * 0.2f;
    metrics->lineCount = 1;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::GetFontMetrics(JaliumTextMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    *metrics = {};
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    CGFloat ascent = CTFontGetAscent(impl_->font);
    CGFloat descent = CTFontGetDescent(impl_->font);
    CGFloat leading = CTFontGetLeading(impl_->font);
    metrics->lineHeight = impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0
        ? impl_->lineSpacing : static_cast<float>(ascent + descent + leading);
    metrics->baseline = impl_->lineSpacingBaseline > 0
        ? impl_->lineSpacingBaseline : static_cast<float>(ascent);
    metrics->ascent = static_cast<float>(ascent);
    metrics->descent = static_cast<float>(descent);
    metrics->lineGap = static_cast<float>(leading);
    return JALIUM_OK;
#else
    metrics->lineHeight = impl_->size * 1.2f;
    metrics->baseline = metrics->ascent = impl_->size;
    metrics->descent = impl_->size * 0.2f;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::HitTestPoint(const wchar_t* text,
    uint32_t textLength, float maxWidth, float maxHeight, float pointX,
    float pointY, JaliumTextHitTestResult* result)
{
    if (!result || !text || textLength == 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = {};
#ifdef __APPLE__
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CTFramesetterRef setter = CTFramesetterCreateWithAttributedString(attributed);
    const CGFloat width = maxWidth > 0 ? maxWidth : 1000000.0;
    const CGFloat height = maxHeight > 0 ? maxHeight : 1000000.0;
    CGPathRef path = CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr);
    CTFrameRef frame = CTFramesetterCreateFrame(setter, CFRangeMake(0, 0), path, nullptr);
    CFArrayRef lines = CTFrameGetLines(frame);
    CFIndex count = CFArrayGetCount(lines);
    std::vector<CGPoint> origins(static_cast<size_t>(std::max<CFIndex>(count, 1)));
    if (count > 0) CTFrameGetLineOrigins(frame, CFRangeMake(0, count), origins.data());
    CFIndex selected = count > 0 ? count - 1 : 0;
    for (CFIndex i = 0; i < count; ++i) {
        CGFloat ascent = 0, descent = 0, leading = 0;
        CTLineRef line = static_cast<CTLineRef>(const_cast<void*>(CFArrayGetValueAtIndex(lines, i)));
        CTLineGetTypographicBounds(line, &ascent, &descent, &leading);
        float top = static_cast<float>(height - origins[i].y - ascent);
        float bottom = static_cast<float>(height - origins[i].y + descent + leading);
        if (pointY >= top && pointY <= bottom) { selected = i; break; }
    }
    if (count > 0) {
        CTLineRef line = static_cast<CTLineRef>(const_cast<void*>(CFArrayGetValueAtIndex(lines, selected)));
        CGPoint position = CGPointMake(pointX - origins[selected].x, 0);
        CFIndex index = CTLineGetStringIndexForPosition(line, position);
        if (index == kCFNotFound) index = CFAttributedStringGetLength(attributed);
        result->textPosition = Utf16IndexToWide(text, textLength, index);
        CGFloat secondary = 0;
        CGFloat primary = CTLineGetOffsetForStringIndex(line, index, &secondary);
        result->isTrailingHit = secondary > primary ? 1 : 0;
        result->isInside = pointX >= origins[selected].x && pointX <= width ? 1 : 0;
        result->x = static_cast<float>(primary + origins[selected].x);
        result->y = static_cast<float>(height - origins[selected].y - impl_->size);
        result->width = std::max(1.0f, static_cast<float>(std::abs(secondary - primary)));
        result->height = impl_->size * 1.2f;
    }
    ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter); ReleaseCF(attributed);
    return JALIUM_OK;
#else
    (void)maxWidth; (void)maxHeight; (void)pointX; (void)pointY;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::HitTestTextPosition(const wchar_t* text,
    uint32_t textLength, float maxWidth, float maxHeight,
    uint32_t textPosition, int32_t isTrailingHit,
    JaliumTextHitTestResult* result)
{
    if (!result || !text || textPosition > textLength)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    *result = {};
#ifdef __APPLE__
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CTLineRef line = CTLineCreateWithAttributedString(attributed);
    CFIndex index = WideIndexToUtf16(text, textLength, textPosition);
    CGFloat secondary = 0;
    CGFloat primary = CTLineGetOffsetForStringIndex(line, index, &secondary);
    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit != 0;
    result->isInside = 1;
    result->x = static_cast<float>(isTrailingHit ? std::max(primary, secondary) : primary);
    result->y = 0;
    result->width = std::max(1.0f, static_cast<float>(std::abs(secondary - primary)));
    result->height = impl_->size * 1.2f;
    ReleaseCF(line); ReleaseCF(attributed);
    (void)maxWidth; (void)maxHeight;
    return JALIUM_OK;
#else
    (void)maxWidth; (void)maxHeight; (void)isTrailingHit;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

bool MetalTextFormat::Rasterize(const wchar_t* text, uint32_t textLength,
    float x, float y, float width, float height,
    const float* deviceTransform,
    float clipX, float clipY, float clipWidth, float clipHeight,
    float r, float g, float b, float a,
    std::vector<uint8_t>& pixels, uint32_t& pixelWidth,
    uint32_t& pixelHeight, float& deviceX, float& deviceY) const
{
    pixels.clear(); pixelWidth = pixelHeight = 0; deviceX = deviceY = 0;
#ifdef __APPLE__
    if (!IsValid() || !text || textLength == 0 || !deviceTransform ||
        !(width > 0) || !(height > 0) || !(clipWidth > 0) || !(clipHeight > 0))
        return false;
    if (!std::isfinite(x) || !std::isfinite(y) ||
        !std::isfinite(width) || !std::isfinite(height) ||
        !std::isfinite(clipX) || !std::isfinite(clipY) ||
        !std::isfinite(clipWidth) || !std::isfinite(clipHeight))
        return false;

    for (int i = 0; i < 6; ++i) {
        if (!std::isfinite(deviceTransform[i])) return false;
    }

    CGColorRef color = CGColorCreateGenericRGB(r, g, b, a);
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength, color);
    CTFramesetterRef setter = attributed
        ? CTFramesetterCreateWithAttributedString(attributed) : nullptr;
    CGPathRef path = setter
        ? CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr) : nullptr;
    CTFrameRef frame = path
        ? CTFramesetterCreateFrame(setter, CFRangeMake(0, 0), path, nullptr) : nullptr;
    if (!color || !attributed || !setter || !path || !frame) {
        ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter);
        ReleaseCF(attributed); ReleaseCF(color);
        return false;
    }

    // Core Text reports each line's bounds relative to that line's origin and
    // each origin relative to the frame path.  Union those bounds before
    // allocating the bitmap: TextBlock deliberately uses a 10000-DIP fallback
    // layout width for unconstrained lines, and allocating that transparent
    // tail for every run is both wasteful and especially bad after rotation.
    CGRect textBounds = CGRectNull;
    CFArrayRef lines = CTFrameGetLines(frame);
    CFIndex lineCount = lines ? CFArrayGetCount(lines) : 0;
    std::vector<CGPoint> lineOrigins(static_cast<size_t>(lineCount));
    if (lineCount > 0) {
        CTFrameGetLineOrigins(frame, CFRangeMake(0, lineCount), lineOrigins.data());
    }
    for (CFIndex index = 0; index < lineCount; ++index) {
        auto line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, index));
        CGRect bounds = CTLineGetBoundsWithOptions(
            line, static_cast<CTLineBoundsOptions>(0));
        const auto glyphBoundsOptions = static_cast<CTLineBoundsOptions>(
            kCTLineBoundsUseGlyphPathBounds | kCTLineBoundsIncludeLanguageExtents);
        CGRect glyphBounds = CTLineGetBoundsWithOptions(line, glyphBoundsOptions);
        if (!CGRectIsNull(glyphBounds) && !CGRectIsInfinite(glyphBounds)) {
            bounds = CGRectIsNull(bounds) ? glyphBounds : CGRectUnion(bounds, glyphBounds);
        }
        if (CGRectIsNull(bounds) || CGRectIsInfinite(bounds)) continue;
        bounds.origin.x += lineOrigins[static_cast<size_t>(index)].x;
        bounds.origin.y += lineOrigins[static_cast<size_t>(index)].y;
        textBounds = CGRectIsNull(textBounds) ? bounds : CGRectUnion(textBounds, bounds);
    }
    if (CGRectIsNull(textBounds) || CGRectIsInfinite(textBounds) ||
        !(textBounds.size.width > 0) || !(textBounds.size.height > 0)) {
        ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter);
        ReleaseCF(attributed); ReleaseCF(color);
        return false;
    }

    const float m11 = deviceTransform[0], m12 = deviceTransform[1];
    const float m21 = deviceTransform[2], m22 = deviceTransform[3];
    const float dx = deviceTransform[4], dy = deviceTransform[5];
    auto mapPoint = [&](double coreTextX, double coreTextY) {
        // Core Text is y-up inside the frame; Jalium drawing coordinates are
        // y-down.  Convert through (localY = height - coreTextY), then apply
        // the complete local-to-device matrix (DPI included).
        const double localX = static_cast<double>(x) + coreTextX;
        const double localY = static_cast<double>(y) + height - coreTextY;
        return CGPointMake(
            m11 * localX + m21 * localY + dx,
            m12 * localX + m22 * localY + dy);
    };

    const double left = CGRectGetMinX(textBounds);
    const double top = CGRectGetMinY(textBounds);
    const double right = CGRectGetMaxX(textBounds);
    const double bottom = CGRectGetMaxY(textBounds);
    const CGPoint corners[4] = {
        mapPoint(left, top), mapPoint(right, top),
        mapPoint(right, bottom), mapPoint(left, bottom)};
    double minX = corners[0].x, maxX = corners[0].x;
    double minY = corners[0].y, maxY = corners[0].y;
    for (int i = 1; i < 4; ++i) {
        minX = std::min(minX, corners[i].x); maxX = std::max(maxX, corners[i].x);
        minY = std::min(minY, corners[i].y); maxY = std::max(maxY, corners[i].y);
    }
    if (!std::isfinite(minX) || !std::isfinite(maxX) ||
        !std::isfinite(minY) || !std::isfinite(maxY)) {
        ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter);
        ReleaseCF(attributed); ReleaseCF(color);
        return false;
    }

    // Two device pixels cover antialiasing, italic overhang and CoreText's
    // hinted image bounds.  Crop to the active Metal scissor before allocating
    // so a long line or a rotated 10000-DIP layout constraint cannot create an
    // off-screen texture.
    constexpr double padding = 2.0;
    double rasterLeft = std::max(std::floor(minX - padding), static_cast<double>(clipX));
    double rasterTop = std::max(std::floor(minY - padding), static_cast<double>(clipY));
    double rasterRight = std::min(std::ceil(maxX + padding),
        static_cast<double>(clipX + clipWidth));
    double rasterBottom = std::min(std::ceil(maxY + padding),
        static_cast<double>(clipY + clipHeight));
    double rasterWidth = rasterRight - rasterLeft;
    double rasterHeight = rasterBottom - rasterTop;
    if (!(rasterWidth > 0) || !(rasterHeight > 0) ||
        rasterWidth > 32768.0 || rasterHeight > 32768.0) {
        ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter);
        ReleaseCF(attributed); ReleaseCF(color);
        return false;
    }

    pixelWidth = static_cast<uint32_t>(rasterWidth);
    pixelHeight = static_cast<uint32_t>(rasterHeight);
    const size_t rowBytes = static_cast<size_t>(pixelWidth) * 4;
    if (pixelHeight > std::numeric_limits<size_t>::max() / rowBytes) {
        ReleaseCF(frame); ReleaseCF(path); ReleaseCF(setter);
        ReleaseCF(attributed); ReleaseCF(color);
        pixelWidth = pixelHeight = 0;
        return false;
    }
    pixels.assign(rowBytes * pixelHeight, 0);
    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = colorSpace ? CGBitmapContextCreate(
        pixels.data(), pixelWidth, pixelHeight, 8, rowBytes, colorSpace,
        kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little) : nullptr;
    if (!context) {
        ReleaseCF(context); ReleaseCF(colorSpace); ReleaseCF(frame);
        ReleaseCF(path); ReleaseCF(setter); ReleaseCF(attributed); ReleaseCF(color);
        pixels.clear(); pixelWidth = pixelHeight = 0;
        return false;
    }

    // Draw glyph outlines through their FINAL device matrix.  The previous
    // path rendered an upright bitmap and rotated that texture afterwards,
    // which introduced a second bilinear sample and visibly softened small CJK
    // strokes.  This CTM maps CoreText frame coordinates straight into the
    // cropped, device-aligned bitmap, so the texture can be copied 1:1.
    CGAffineTransform textToBitmap = CGAffineTransformMake(
        m11, m12, -m21, -m22,
        m11 * x + m21 * (y + height) + dx - rasterLeft,
        m12 * x + m22 * (y + height) + dy - rasterTop);
    CGContextConcatCTM(context, textToBitmap);
    CGContextSetTextMatrix(context, CGAffineTransformIdentity);
    CTFrameDraw(frame, context);
    deviceX = static_cast<float>(rasterLeft);
    deviceY = static_cast<float>(rasterTop);
    ReleaseCF(context); ReleaseCF(colorSpace); ReleaseCF(frame); ReleaseCF(path);
    ReleaseCF(setter); ReleaseCF(attributed); ReleaseCF(color);
    return true;
#else
    (void)text; (void)textLength; (void)x; (void)y; (void)width; (void)height;
    (void)deviceTransform; (void)clipX; (void)clipY; (void)clipWidth; (void)clipHeight;
    (void)r; (void)g; (void)b; (void)a;
    return false;
#endif
}

struct MetalBitmap::Impl {
    uint32_t width = 0;
    uint32_t height = 0;
    std::vector<uint8_t> pixels;
#ifdef __APPLE__
    id<MTLTexture> texture = nil;
    std::mutex textureMutex;
#endif
};

MetalBitmap::MetalBitmap(uint32_t width, uint32_t height,
    std::vector<uint8_t>&& pixels) : impl_(std::make_unique<Impl>())
{
    impl_->width = width;
    impl_->height = height;
    impl_->pixels = std::move(pixels);
}
MetalBitmap::~MetalBitmap() = default;
uint32_t MetalBitmap::GetWidth() const { return impl_->width; }
uint32_t MetalBitmap::GetHeight() const { return impl_->height; }

void* MetalBitmap::EnsureTexture(void* deviceHandle)
{
#ifdef __APPLE__
    std::scoped_lock lock(impl_->textureMutex);
    if (!impl_->texture) {
        id<MTLDevice> device = (__bridge id<MTLDevice>)deviceHandle;
        if (!device || impl_->pixels.empty()) return nullptr;
        MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
            texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
            width:impl_->width height:impl_->height mipmapped:NO];
        descriptor.storageMode = MTLStorageModeShared;
        descriptor.usage = MTLTextureUsageShaderRead;
        impl_->texture = [device newTextureWithDescriptor:descriptor];
        if (impl_->texture) {
            [impl_->texture replaceRegion:MTLRegionMake2D(0, 0, impl_->width,
                impl_->height) mipmapLevel:0 withBytes:impl_->pixels.data()
                bytesPerRow:impl_->width * 4];
        }
    }
    return (__bridge void*)impl_->texture;
#else
    (void)deviceHandle;
    return nullptr;
#endif
}

struct MetalVideoSurface::Impl {
    uint32_t width = 0;
    uint32_t height = 0;
    uint32_t format = JALIUM_VS_FORMAT_BGRA8;
    JaliumVideoSurfaceKind kind = JALIUM_VS_KIND_BGRA8_CPU;
    uint32_t stride = 0;
    std::vector<uint8_t> staging;
    bool locked = false;
    void* lifetimeContext = nullptr;
    void (*lifetimeRelease)(void*) = nullptr;
#ifdef __APPLE__
    id<MTLTexture> texture = nil;
    id<MTLTexture> planeY = nil;
    id<MTLTexture> planeUV = nil;
    CVPixelBufferRef pixelBuffer = nullptr;
    CVMetalTextureCacheRef textureCache = nullptr;
    IOSurfaceRef ioSurface = nullptr;
#endif
};

MetalVideoSurface::MetalVideoSurface(void* deviceHandle, uint32_t width,
    uint32_t height, uint32_t formatHint) : impl_(std::make_unique<Impl>())
{
    impl_->width = width; impl_->height = height; impl_->format = formatHint;
    impl_->stride = width * 4;
    if (formatHint != JALIUM_VS_FORMAT_BGRA8 || width == 0 || height == 0) return;
    impl_->staging.resize(static_cast<size_t>(impl_->stride) * height);
#ifdef __APPLE__
    id<MTLDevice> device = (__bridge id<MTLDevice>)deviceHandle;
    if (!device) return;
    MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
        width:width height:height mipmapped:NO];
    descriptor.storageMode = MTLStorageModeShared;
    descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite |
        MTLTextureUsageRenderTarget;
    impl_->texture = [device newTextureWithDescriptor:descriptor];
#else
    (void)deviceHandle;
#endif
}

MetalVideoSurface::MetalVideoSurface(void* deviceHandle,
    const JaliumVideoSurfaceDescriptor& descriptor) : impl_(std::make_unique<Impl>())
{
    impl_->width = descriptor.width; impl_->height = descriptor.height;
    impl_->format = descriptor.format_hint;
    impl_->kind = descriptor.kind;
    if (descriptor.lifetime_retain_callback && descriptor.lifetime_context) {
        auto retain = reinterpret_cast<void(*)(void*)>(
            static_cast<uintptr_t>(descriptor.lifetime_retain_callback));
        impl_->lifetimeContext = reinterpret_cast<void*>(
            static_cast<uintptr_t>(descriptor.lifetime_context));
        impl_->lifetimeRelease = reinterpret_cast<void(*)(void*)>(
            static_cast<uintptr_t>(descriptor.lifetime_release_callback));
        retain(impl_->lifetimeContext);
    }
#ifdef __APPLE__
    id<MTLDevice> device = (__bridge id<MTLDevice>)deviceHandle;
    if (!device || descriptor.width == 0 || descriptor.height == 0) return;
    if (descriptor.kind == JALIUM_VS_KIND_METAL_TEXTURE) {
        impl_->texture = (__bridge id<MTLTexture>)reinterpret_cast<void*>(
            static_cast<uintptr_t>(descriptor.handle0));
    } else if (descriptor.kind == JALIUM_VS_KIND_CVPIXELBUFFER) {
        impl_->pixelBuffer = reinterpret_cast<CVPixelBufferRef>(
            static_cast<uintptr_t>(descriptor.handle0));
        if (impl_->pixelBuffer) CFRetain(impl_->pixelBuffer);
        if (impl_->pixelBuffer && CVMetalTextureCacheCreate(kCFAllocatorDefault,
            nullptr, device, nullptr, &impl_->textureCache) == kCVReturnSuccess) {
            OSType pixelFormat = CVPixelBufferGetPixelFormatType(impl_->pixelBuffer);
            if (pixelFormat == kCVPixelFormatType_32BGRA) {
                CVMetalTextureRef cvTexture = nullptr;
                if (CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatBGRA8Unorm, descriptor.width, descriptor.height,
                    0, &cvTexture) == kCVReturnSuccess && cvTexture) {
                    impl_->texture = CVMetalTextureGetTexture(cvTexture);
                    CFRelease(cvTexture);
                }
            } else if (pixelFormat == kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange ||
                       pixelFormat == kCVPixelFormatType_420YpCbCr8BiPlanarFullRange) {
                CVMetalTextureRef y = nullptr, uv = nullptr;
                CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatR8Unorm, descriptor.width, descriptor.height,
                    0, &y);
                CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatRG8Unorm, descriptor.width / 2,
                    descriptor.height / 2, 1, &uv);
                if (y) { impl_->planeY = CVMetalTextureGetTexture(y); CFRelease(y); }
                if (uv) { impl_->planeUV = CVMetalTextureGetTexture(uv); CFRelease(uv); }
            } else if (pixelFormat == kCVPixelFormatType_420YpCbCr10BiPlanarVideoRange ||
                       pixelFormat == kCVPixelFormatType_420YpCbCr10BiPlanarFullRange) {
                CVMetalTextureRef y = nullptr, uv = nullptr;
                CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatR16Unorm, descriptor.width, descriptor.height,
                    0, &y);
                CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatRG16Unorm, descriptor.width / 2,
                    descriptor.height / 2, 1, &uv);
                if (y) { impl_->planeY = CVMetalTextureGetTexture(y); CFRelease(y); }
                if (uv) { impl_->planeUV = CVMetalTextureGetTexture(uv); CFRelease(uv); }
                impl_->format = JALIUM_VS_FORMAT_P010;
            }
        }
    } else if (descriptor.kind == JALIUM_VS_KIND_IOSURFACE) {
        impl_->ioSurface = IOSurfaceLookup(static_cast<IOSurfaceID>(descriptor.handle0));
        if (impl_->ioSurface) {
            MTLTextureDescriptor* td = [MTLTextureDescriptor
                texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                width:descriptor.width height:descriptor.height mipmapped:NO];
            td.usage = MTLTextureUsageShaderRead;
            impl_->texture = [device newTextureWithDescriptor:td
                iosurface:impl_->ioSurface plane:0];
        }
    }
#else
    (void)deviceHandle;
#endif
}

MetalVideoSurface::~MetalVideoSurface()
{
#ifdef __APPLE__
    if (impl_->pixelBuffer) CFRelease(impl_->pixelBuffer);
    if (impl_->textureCache) CFRelease(impl_->textureCache);
    if (impl_->ioSurface) CFRelease(impl_->ioSurface);
#endif
    if (impl_->lifetimeRelease && impl_->lifetimeContext)
        impl_->lifetimeRelease(impl_->lifetimeContext);
}

bool MetalVideoSurface::IsValid() const
{
#ifdef __APPLE__
    return impl_ && (impl_->texture || (impl_->planeY && impl_->planeUV));
#else
    return false;
#endif
}
uint32_t MetalVideoSurface::GetWidth() const { return impl_->width; }
uint32_t MetalVideoSurface::GetHeight() const { return impl_->height; }
JaliumVideoSurfaceKind MetalVideoSurface::GetKind() const { return impl_->kind; }
void* MetalVideoSurface::TextureHandle(uint32_t plane) const
{
#ifdef __APPLE__
    id<MTLTexture> texture = plane == 1 ? impl_->planeUV
        : (plane == 0 && impl_->planeY ? impl_->planeY : impl_->texture);
    return (__bridge void*)texture;
#else
    (void)plane;
    return nullptr;
#endif
}
bool MetalVideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride || impl_->kind != JALIUM_VS_KIND_BGRA8_CPU ||
        impl_->locked || impl_->staging.empty()) return false;
    impl_->locked = true;
    *outPtr = impl_->staging.data(); *outStride = impl_->stride;
    return true;
}
bool MetalVideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* dirty)
{
    if (!impl_->locked || impl_->kind != JALIUM_VS_KIND_BGRA8_CPU) return false;
    impl_->locked = false;
#ifdef __APPLE__
    if (!impl_->texture) return false;
    int32_t x = 0, y = 0, w = static_cast<int32_t>(impl_->width),
        h = static_cast<int32_t>(impl_->height);
    if (dirty) {
        x = std::clamp(dirty->x, 0, static_cast<int32_t>(impl_->width));
        y = std::clamp(dirty->y, 0, static_cast<int32_t>(impl_->height));
        w = std::clamp(dirty->width, 0, static_cast<int32_t>(impl_->width) - x);
        h = std::clamp(dirty->height, 0, static_cast<int32_t>(impl_->height) - y);
    }
    if (w == 0 || h == 0) return true;
    MTLRegion region = MTLRegionMake2D(x, y, w, h);
    const uint8_t* source = impl_->staging.data() +
        static_cast<size_t>(y) * impl_->stride + static_cast<size_t>(x) * 4;
    [impl_->texture replaceRegion:region mipmapLevel:0 withBytes:source
        bytesPerRow:impl_->stride];
    return true;
#else
    (void)dirty;
    return false;
#endif
}

struct MetalBackend::Impl {
    JaliumGpuPreference preference = JALIUM_GPU_PREFERENCE_AUTO;
    std::atomic<int64_t> deviceError{0};
#ifdef __APPLE__
    id<MTLDevice> device = nil;
    id<MTLCommandQueue> queue = nil;
#endif
    bool initialized = false;
};

MetalBackend::MetalBackend() : impl_(std::make_unique<Impl>()) {}
MetalBackend::~MetalBackend() = default;

bool MetalBackend::Initialize()
{
    if (impl_->initialized) return true;
#ifdef __APPLE__
    @autoreleasepool {
        impl_->device = MTLCreateSystemDefaultDevice();
        if (!impl_->device) return false;
        impl_->queue = [impl_->device newCommandQueue];
        if (!impl_->queue) { impl_->device = nil; return false; }
        impl_->queue.label = @"Jalium Metal Command Queue";
        impl_->initialized = true;
        return true;
    }
#else
    return false;
#endif
}

JaliumResult MetalBackend::CheckDeviceStatus()
{
#ifdef __APPLE__
    if (!impl_->device || impl_->deviceError.load(std::memory_order_acquire) != 0)
        return JALIUM_ERROR_DEVICE_LOST;
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalBackend::SetGpuPreference(JaliumGpuPreference preference)
{
    if (preference < JALIUM_GPU_PREFERENCE_AUTO ||
        preference > JALIUM_GPU_PREFERENCE_MINIMUM_POWER)
        return JALIUM_ERROR_INVALID_ARGUMENT;
    if (impl_->initialized) return JALIUM_ERROR_INVALID_STATE;
    impl_->preference = preference;
    return JALIUM_OK;
}

JaliumResult MetalBackend::GetAdapterInfo(JaliumAdapterInfo* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = {};
#ifdef __APPLE__
    if (!impl_->device) return JALIUM_ERROR_INVALID_STATE;
    Utf8ToFixedUtf16(impl_->device.name.UTF8String, out->name);
    out->adapterType = JALIUM_GPU_ADAPTER_TYPE_INTEGRATED;
    out->vendorId = 0x106b; // Apple Inc.
    if ([impl_->device respondsToSelector:@selector(recommendedMaxWorkingSetSize)])
        out->sharedSystemMemory = impl_->device.recommendedMaxWorkingSetSize;
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

RenderTarget* MetalBackend::CreateRenderTarget(void* handle, int32_t width,
    int32_t height)
{
#ifdef __APPLE__
    if (!Initialize() || !handle || width <= 0 || height <= 0) return nullptr;
    auto* target = new MetalRenderTarget(this, width, height, false);
    if (!target->Initialize(handle)) { delete target; return nullptr; }
    return target;
#else
    (void)handle; (void)width; (void)height;
    return nullptr;
#endif
}

RenderTarget* MetalBackend::CreateRenderTargetForComposition(void* handle,
    int32_t width, int32_t height)
{
#ifdef __APPLE__
    if (!Initialize() || !handle || width <= 0 || height <= 0) return nullptr;
    auto* target = new MetalRenderTarget(this, width, height, true);
    if (!target->Initialize(handle)) { delete target; return nullptr; }
    return target;
#else
    (void)handle; (void)width; (void)height;
    return nullptr;
#endif
}

RenderTarget* MetalBackend::CreateRenderTargetForSurface(
    const JaliumSurfaceDescriptor* surface, int32_t width, int32_t height)
{
#ifdef __APPLE__
    if (!Initialize() || !surface || surface->handle0 == 0 || width <= 0 || height <= 0)
        return nullptr;
    if (surface->platform != JALIUM_PLATFORM_MACOS &&
        surface->platform != JALIUM_PLATFORM_IOS &&
        surface->platform != JALIUM_PLATFORM_TVOS &&
        surface->platform != JALIUM_PLATFORM_VISIONOS) return nullptr;
    auto* target = new MetalRenderTarget(this, width, height,
        surface->kind == JALIUM_SURFACE_KIND_COMPOSITION_TARGET);
    if (!target->Initialize(surface)) { delete target; return nullptr; }
    return target;
#else
    (void)surface; (void)width; (void)height;
    return nullptr;
#endif
}

RenderTarget* MetalBackend::CreateRenderTargetForCompositionSurface(
    const JaliumSurfaceDescriptor* surface, int32_t width, int32_t height)
{
#ifdef __APPLE__
    if (!surface) return nullptr;
    JaliumSurfaceDescriptor copy = *surface;
    copy.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    return CreateRenderTargetForSurface(&copy, width, height);
#else
    (void)surface; (void)width; (void)height;
    return nullptr;
#endif
}

Brush* MetalBackend::CreateSolidBrush(float r, float g, float b, float a)
{ return new MetalSolidBrush(r, g, b, a); }
Brush* MetalBackend::CreateLinearGradientBrush(float sx, float sy, float ex,
    float ey, const JaliumGradientStop* stops, uint32_t count, uint32_t spread)
{ return stops && count ? new MetalLinearGradientBrush(sx, sy, ex, ey, stops, count, spread) : nullptr; }
Brush* MetalBackend::CreateRadialGradientBrush(float cx, float cy, float rx,
    float ry, float ox, float oy, const JaliumGradientStop* stops,
    uint32_t count, uint32_t spread)
{ return stops && count ? new MetalRadialGradientBrush(cx, cy, rx, ry, ox, oy, stops, count, spread) : nullptr; }
TextFormat* MetalBackend::CreateTextFormat(const wchar_t* family, float size,
    int32_t weight, int32_t style)
{
    if (!family || !std::isfinite(size) || size <= 0) return nullptr;
    auto* result = new MetalTextFormat(family, size, weight, style);
    if (!result->IsValid()) { delete result; return nullptr; }
    return result;
}

Bitmap* MetalBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t size)
{
#ifdef __APPLE__
    if (!data || size == 0) return nullptr;
    CFDataRef bytes = CFDataCreate(kCFAllocatorDefault, data, size);
    CGImageSourceRef source = bytes ? CGImageSourceCreateWithData(bytes, nullptr) : nullptr;
    CGImageRef image = source ? CGImageSourceCreateImageAtIndex(source, 0, nullptr) : nullptr;
    if (!image) { ReleaseCF(source); ReleaseCF(bytes); return nullptr; }
    size_t width = CGImageGetWidth(image), height = CGImageGetHeight(image);
    if (width == 0 || height == 0 || width > 32768 || height > 32768) {
        ReleaseCF(image); ReleaseCF(source); ReleaseCF(bytes); return nullptr;
    }
    std::vector<uint8_t> pixels(width * height * 4);
    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(pixels.data(), width, height,
        8, width * 4, colorSpace,
        kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    if (context) {
        CGContextTranslateCTM(context, 0, height);
        CGContextScaleCTM(context, 1, -1);
        CGContextDrawImage(context, CGRectMake(0, 0, width, height), image);
    }
    ReleaseCF(context); ReleaseCF(colorSpace); ReleaseCF(image);
    ReleaseCF(source); ReleaseCF(bytes);
    if (!context) return nullptr;
    return new MetalBitmap(static_cast<uint32_t>(width),
        static_cast<uint32_t>(height), std::move(pixels));
#else
    (void)data; (void)size;
    return nullptr;
#endif
}

Bitmap* MetalBackend::CreateBitmapFromPixels(const uint8_t* pixels,
    uint32_t width, uint32_t height, uint32_t stride)
{
    if (!pixels || width == 0 || height == 0 || stride < width * 4) return nullptr;
    std::vector<uint8_t> copy(static_cast<size_t>(width) * height * 4);
    for (uint32_t y = 0; y < height; ++y)
        std::memcpy(copy.data() + static_cast<size_t>(y) * width * 4,
            pixels + static_cast<size_t>(y) * stride, static_cast<size_t>(width) * 4);
    return new MetalBitmap(width, height, std::move(copy));
}

VideoSurface* MetalBackend::CreateVideoSurface(uint32_t width, uint32_t height,
    uint32_t formatHint)
{
    if (!Initialize()) return nullptr;
    auto* surface = new MetalVideoSurface(DeviceHandle(), width, height, formatHint);
    if (!surface->IsValid()) { delete surface; return nullptr; }
    return surface;
}

VideoSurface* MetalBackend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!Initialize() || !descriptor) return nullptr;
    if (descriptor->kind != JALIUM_VS_KIND_IOSURFACE &&
        descriptor->kind != JALIUM_VS_KIND_METAL_TEXTURE &&
        descriptor->kind != JALIUM_VS_KIND_CVPIXELBUFFER) return nullptr;
    auto* surface = new MetalVideoSurface(DeviceHandle(), *descriptor);
    if (!surface->IsValid()) { delete surface; return nullptr; }
    return surface;
}

void* MetalBackend::CreateInkLayerBitmap(uint32_t width, uint32_t height)
{
#ifdef __APPLE__
    if (!Initialize() || width == 0 || height == 0) return nullptr;
    auto layer = std::make_unique<MetalInkLayer>();
    layer->device = impl_->device; layer->width = width; layer->height = height;
    MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
        width:width height:height mipmapped:NO];
    descriptor.storageMode = MTLStorageModeShared;
    descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite |
        MTLTextureUsageRenderTarget;
    layer->texture = [impl_->device newTextureWithDescriptor:descriptor];
    if (!layer->texture) return nullptr;
    std::vector<uint8_t> zero(static_cast<size_t>(width) * height * 4);
    [layer->texture replaceRegion:MTLRegionMake2D(0, 0, width, height)
        mipmapLevel:0 withBytes:zero.data() bytesPerRow:width * 4];
    return layer.release();
#else
    (void)width; (void)height;
    return nullptr;
#endif
}
void MetalBackend::DestroyInkLayerBitmap(void* bitmap)
{ delete static_cast<MetalInkLayer*>(bitmap); }
int32_t MetalBackend::ResizeInkLayerBitmap(void* bitmap, uint32_t width,
    uint32_t height)
{
#ifdef __APPLE__
    auto* layer = static_cast<MetalInkLayer*>(bitmap);
    if (!layer || width == 0 || height == 0) return -1;
    std::scoped_lock lock(layer->mutex);
    MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
        texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
        width:width height:height mipmapped:NO];
    descriptor.storageMode = MTLStorageModeShared;
    descriptor.usage = MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite |
        MTLTextureUsageRenderTarget;
    id<MTLTexture> replacement = [layer->device newTextureWithDescriptor:descriptor];
    if (!replacement) return -1;
    layer->texture = replacement; layer->width = width; layer->height = height;
    ++layer->generation;
    ClearInkLayerBitmap(layer, 0, 0, 0, 0);
    return 0;
#else
    (void)bitmap; (void)width; (void)height;
    return -1;
#endif
}
void MetalBackend::ClearInkLayerBitmap(void* bitmap, float r, float g, float b,
    float a)
{
#ifdef __APPLE__
    auto* layer = static_cast<MetalInkLayer*>(bitmap);
    if (!layer || !layer->texture) return;
    uint8_t pixel[4] = {
        static_cast<uint8_t>(std::clamp(b * a, 0.0f, 1.0f) * 255.0f + 0.5f),
        static_cast<uint8_t>(std::clamp(g * a, 0.0f, 1.0f) * 255.0f + 0.5f),
        static_cast<uint8_t>(std::clamp(r * a, 0.0f, 1.0f) * 255.0f + 0.5f),
        static_cast<uint8_t>(std::clamp(a, 0.0f, 1.0f) * 255.0f + 0.5f)};
    std::vector<uint8_t> row(static_cast<size_t>(layer->width) * 4);
    for (uint32_t x = 0; x < layer->width; ++x)
        std::memcpy(row.data() + static_cast<size_t>(x) * 4, pixel, 4);
    std::vector<uint8_t> image(static_cast<size_t>(layer->width) * layer->height * 4);
    for (uint32_t y = 0; y < layer->height; ++y)
        std::memcpy(image.data() + static_cast<size_t>(y) * row.size(), row.data(), row.size());
    [layer->texture replaceRegion:MTLRegionMake2D(0, 0, layer->width, layer->height)
        mipmapLevel:0 withBytes:image.data() bytesPerRow:layer->width * 4];
#else
    (void)bitmap; (void)r; (void)g; (void)b; (void)a;
#endif
}

void* MetalBackend::CreateBrushShader(const char* key, const char* source,
    int32_t blendMode)
{
#ifdef __APPLE__
    if (!Initialize() || !key || !source || blendMode < 0 || blendMode > 2) return nullptr;
    NSError* error = nil;
    id<MTLLibrary> library = [impl_->device newLibraryWithSource:
        [NSString stringWithUTF8String:kInkShaderSource] options:nil error:&error];
    if (!library) return nullptr;
    id<MTLFunction> function = [library newFunctionWithName:@"jalium_ink"];
    if (!function) return nullptr;
    id<MTLComputePipelineState> pipeline =
        [impl_->device newComputePipelineStateWithFunction:function error:&error];
    if (!pipeline) return nullptr;
    auto result = std::make_unique<MetalBrushShader>();
    result->key = key; result->source = source; result->blendMode = blendMode;
    result->pipeline = pipeline;
    return result.release();
#else
    (void)key; (void)source; (void)blendMode;
    return nullptr;
#endif
}
void MetalBackend::DestroyBrushShader(void* shader)
{ delete static_cast<MetalBrushShader*>(shader); }

int32_t MetalBackend::DispatchBrush(void* bitmap, void* shader,
    const void* strokePoints, uint32_t pointCount, const void* constants,
    const void* extraParams, uint32_t extraParamsSize)
{
#ifdef __APPLE__
    auto* layer = static_cast<MetalInkLayer*>(bitmap);
    auto* brush = static_cast<MetalBrushShader*>(shader);
    if (!layer || !brush || !strokePoints || pointCount == 0 || !constants ||
        !layer->texture || !brush->pipeline) return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
    std::array<uint8_t, 80> patched{};
    std::memcpy(patched.data(), constants, 80);
    auto* values = reinterpret_cast<float*>(patched.data());
    auto* uints = reinterpret_cast<uint32_t*>(patched.data());
    uints[12] = pointCount;
    values[16] = static_cast<float>(layer->width);
    values[17] = static_cast<float>(layer->height);
    id<MTLCommandBuffer> command = [impl_->queue commandBuffer];
    id<MTLComputeCommandEncoder> encoder = [command computeCommandEncoder];
    if (!command || !encoder) return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
    [encoder setComputePipelineState:brush->pipeline];
    [encoder setTexture:layer->texture atIndex:0];
    [encoder setBytes:strokePoints length:static_cast<NSUInteger>(pointCount) * 16 atIndex:0];
    [encoder setBytes:patched.data() length:patched.size() atIndex:1];
    uint32_t blend = static_cast<uint32_t>(brush->blendMode);
    [encoder setBytes:&blend length:sizeof(blend) atIndex:2];
    MTLSize threads = MTLSizeMake(8, 8, 1);
    MTLSize groups = MTLSizeMake((layer->width + 7) / 8,
        (layer->height + 7) / 8, 1);
    [encoder dispatchThreadgroups:groups threadsPerThreadgroup:threads];
    [encoder endEncoding];
    [command commit];
    (void)extraParams; (void)extraParamsSize;
    return JALIUM_INK_DISPATCH_OK;
#else
    (void)bitmap; (void)shader; (void)strokePoints; (void)pointCount;
    (void)constants; (void)extraParams; (void)extraParamsSize;
    return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
#endif
}

void* MetalBackend::DeviceHandle() const
{
#ifdef __APPLE__
    return (__bridge void*)impl_->device;
#else
    return nullptr;
#endif
}
void* MetalBackend::CommandQueueHandle() const
{
#ifdef __APPLE__
    return (__bridge void*)impl_->queue;
#else
    return nullptr;
#endif
}
void MetalBackend::NoteDeviceError(int64_t code)
{ if (code != 0) impl_->deviceError.store(code, std::memory_order_release); }

IRenderBackend* CreateMetalBackend() { return new MetalBackend(); }

} // namespace jalium
