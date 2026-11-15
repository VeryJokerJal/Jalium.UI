#include <array>
#include "metal_backend.h"
#include "metal_text_paragraph.h"
#ifndef JALIUM_APPLE_TEXT_ONLY
#include "metal_internal.h"
#include "metal_shader_compiler.h"
#endif
#include "jalium_string_util.h"
#include "jalium_font_math.h"
#include "jalium_font_features.h"
#include "metal_font_visibility.h"
#include "metal_font_size_adjust.h"
#include "jalium_font_resource.h"
#include "jalium_apple_font.h"
#include "apple_vertical_features.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>
#include <mutex>
#include <string>
#include <tuple>
#include <utility>
#include <unordered_map>
#if defined(__aarch64__)
#include <arm_neon.h>
#endif

#ifdef __APPLE__
#import <TargetConditionals.h>
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#endif
#import <CoreGraphics/CoreGraphics.h>
#import <CoreText/CoreText.h>
#include "metal_font_caps.h"
#include "metal_font_position.h"
#include "metal_font_emoji.h"
#import <ImageIO/ImageIO.h>
#import <IOSurface/IOSurfaceRef.h>
#endif

namespace jalium {

namespace {

#ifdef JALIUM_APPLE_TEXT_ONLY
std::atomic<uint64_t> gMetalTextFormatId{1};
#endif

#ifndef JALIUM_APPLE_TEXT_ONLY
uint32_t NormalizeSpread(uint32_t value) noexcept
{
    return value <= 2 ? value : 0;
}

#endif

#ifdef __APPLE__
#ifdef JALIUM_APPLE_TEXT_ONLY
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

#endif

void ReleaseCF(const void* object)
{
    if (object) CFRelease(object);
}

#ifndef JALIUM_APPLE_TEXT_ONLY
constexpr const char* kInkShaderSource = R"METAL(
#include <metal_stdlib>
using namespace metal;
struct StrokePoint { float2 position; float pressure; float pad; };
struct InkVertexOut {
    float4 position [[position]];
    float2 pixel [[user(locn0)]];
};
vertex InkVertexOut jalium_ink_vertex(constant uchar* rawConstants [[buffer(0)]],
    uint vid [[vertex_id]])
{
    constant float* f = reinterpret_cast<constant float*>(rawConstants);
    float2 viewport=max(float2(f[16],f[17]),float2(1.0));
    float2 positions[3]={float2(-1.0,1.0),float2(3.0,1.0),float2(-1.0,-3.0)};
    float2 pixels[3]={float2(0.0),float2(viewport.x*2.0,0.0),
        float2(0.0,viewport.y*2.0)};
    InkVertexOut output;output.position=float4(positions[vid],0.0,1.0);
    output.pixel=pixels[vid];return output;
}
kernel void jalium_ink(
    texture2d<float, access::read_write> target [[texture(0)]],
    const device StrokePoint* points [[buffer(0)]],
    constant uchar* rawConstants [[buffer(1)]],
    constant uint& blendMode [[buffer(2)]],
    uint2 localGid [[thread_position_in_grid]])
{
    constant float* f = reinterpret_cast<constant float*>(rawConstants);
    uint2 bboxMin = uint2(max(floor(float2(f[8],f[9])),float2(0.0)));
    uint2 bboxMax = uint2(max(ceil(float2(f[10],f[11])),float2(bboxMin)));
    uint2 gid = localGid + bboxMin;
    if (gid.x >= target.get_width() || gid.y >= target.get_height()) return;
    if (gid.x >= bboxMax.x || gid.y >= bboxMax.y) return;
    constant uint* u = reinterpret_cast<constant uint*>(rawConstants);
    float4 color = float4(f[0], f[1], f[2], f[3]);
    float width = max(f[4], 0.25);
    uint count = u[12];
    bool ignorePressure = u[14] != 0u;
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
#endif

} // namespace

#ifndef JALIUM_APPLE_TEXT_ONLY
MetalLinearGradientBrush::MetalLinearGradientBrush(float sx, float sy, float ex,
    float ey, const JaliumGradientStop* values, uint32_t count, uint32_t spread)
    : startX(sx), startY(sy), endX(ex), endY(ey),
      spreadMethod(NormalizeSpread(spread))
{
    if (values && count) stops.assign(values, values + count);
    std::stable_sort(stops.begin(), stops.end(), [](const auto& a, const auto& b) { return a.position < b.position; });
}

MetalRadialGradientBrush::MetalRadialGradientBrush(float cx, float cy, float rx,
    float ry, float ox, float oy, const JaliumGradientStop* values,
    uint32_t count, uint32_t spread)
    : centerX(cx), centerY(cy), radiusX(rx), radiusY(ry), originX(ox),
      originY(oy), spreadMethod(NormalizeSpread(spread))
{
    if (values && count) stops.assign(values, values + count);
    std::stable_sort(stops.begin(), stops.end(), [](const auto& a, const auto& b) { return a.position < b.position; });
}

#endif

#ifdef JALIUM_APPLE_TEXT_ONLY
struct MetalTextFormat::Impl {
    FontResourceReference registeredFont;
    std::vector<FontResourceReference> fallbackResources;
#ifdef __APPLE__
    CTFontRef font = nullptr;
    CTFontRef originalFont = nullptr;
    CFCharacterSetRef characterRestriction = nullptr;
    CFCharacterSetRef invisibleCharacters = nullptr;
    using FontOwner = std::shared_ptr<const void>;
    FontOwner variationBaseFont;
    std::vector<FontOwner> cascadeVariationBases;
    std::vector<bool> cascadePrivateFonts;
    std::vector<std::pair<FontOwner, FontOwner>> cascadeFonts;
    std::vector<FontOwner> cascadeCharacters;
    std::vector<bool> cascadeDisablesKerning;
    bool disablesKerning = false;
    bool automaticOptical = false;
    std::vector<bool> cascadeAutomaticOptical;
    JaliumFontFaceMetrics faceMetrics = font_size_adjust::InitialMetrics();
    std::vector<JaliumFontFaceMetrics> cascadeFaceMetrics;
    uint32_t opticalPolicy = 0;
    font_size_adjust::Policy sizePolicy;
    font_size_adjust::Policy SizingPolicy() const {
        auto result=sizePolicy;result.size=size;if(!result.metric)result.optical=opticalPolicy;return result;
    }
    bool HasFaceDescriptors() const {
        return faceMetrics.sizeAdjust!=1||HasAppleFontMetrics(faceMetrics)||std::any_of(cascadeFaceMetrics.begin(),cascadeFaceMetrics.end(),
            [](const auto& value){return value.sizeAdjust!=1||HasAppleFontMetrics(value);});
    }
    bool SizingActive() const { return sizePolicy.metric||HasFaceDescriptors(); }
    std::vector<JaliumFontVariation> sizeAxes;
    mutable FontOwner sizedMetrics;
    mutable uint64_t sizedMetricsRevision = 0;
    mutable double sizedMetricsUsed = 0;
    mutable std::mutex sizedMetricsGate;
    CTFontRef MetricsFont() const {
        if (!sizePolicy.metric && font_size_adjust::IsInitial(faceMetrics)) return font;
        std::lock_guard<std::mutex> guard(sizedMetricsGate);
        auto revision=generation.load(std::memory_order_acquire);
        if (!sizedMetrics || sizedMetricsRevision!=revision) {
            auto next=font_size_adjust::Copy(font,SizingPolicy(),sizeAxes,true,automaticOptical,&sizedMetricsUsed,faceMetrics,bool(registeredFont));
            if(!next)return font;
            sizedMetrics=FontOwner(next,[](const void* p){CFRelease(p);});sizedMetricsRevision=revision;
        }
        return static_cast<CTFontRef>(sizedMetrics.get());
    }
    AppleFontLineMetrics LineMetrics() const {
        auto selected=MetricsFont();
        double used=!sizePolicy.metric&&font_size_adjust::IsInitial(faceMetrics)?size:sizedMetricsUsed;
        auto result=ApplyAppleFontLineMetrics(selected,faceMetrics,used);
        result.overridden=result.overridden||HasFaceDescriptors();return result;
    }
    CFDictionaryRef SizingAttributes() const {
        if (!SizingActive()) return nullptr;
        auto policy=SizingPolicy();
        auto release=[](const void* p){if(p)CFRelease(p);};using Owner=std::unique_ptr<const void,decltype(release)>;
        std::vector<uint8_t> bytes(sizeof(policy)+sizeAxes.size()*sizeof(JaliumFontVariation));
        std::memcpy(bytes.data(),&policy,sizeof(policy));if(!sizeAxes.empty())std::memcpy(bytes.data()+sizeof(policy),sizeAxes.data(),sizeAxes.size()*sizeof(JaliumFontVariation));
        Owner data(CFDataCreate(kCFAllocatorDefault,bytes.data(),bytes.size()),release);
        Owner fonts(CFArrayCreateMutable(kCFAllocatorDefault,0,&kCFTypeArrayCallBacks),release),automatic(CFArrayCreateMutable(kCFAllocatorDefault,0,&kCFTypeArrayCallBacks),release),descriptors(CFArrayCreateMutable(kCFAllocatorDefault,0,&kCFTypeArrayCallBacks),release),coverage(CFArrayCreateMutable(kCFAllocatorDefault,0,&kCFTypeArrayCallBacks),release);
        Owner privateFonts(CFArrayCreateMutable(kCFAllocatorDefault,0,&kCFTypeArrayCallBacks),release);
        if(!data||!fonts||!automatic||!descriptors||!coverage||!privateFonts)return nullptr;
        auto append=[&](CTFontRef value,bool autoOptical,const JaliumFontFaceMetrics& metrics,CFCharacterSetRef restriction,bool privateFace) {
            if(!value)return true;
            Owner description(CFDataCreate(kCFAllocatorDefault,reinterpret_cast<const UInt8*>(&metrics),sizeof(metrics)),release);
            Owner characters(restriction?CFRetain(restriction):CTFontCopyCharacterSet(value),release);
            if(!description||!characters)return false;
            CFArrayAppendValue(const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(coverage.get())),characters.get());
            CFArrayAppendValue(const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(fonts.get())),value);
            CFArrayAppendValue(const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(descriptors.get())),description.get());
            if(privateFace)CFArrayAppendValue(const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(privateFonts.get())),value);
            if(autoOptical)CFArrayAppendValue(const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(automatic.get())),value);
            return true;
        };
        if(!append(font,automaticOptical,faceMetrics,characterRestriction,bool(registeredFont))||!append(originalFont,automaticOptical,faceMetrics,characterRestriction,bool(registeredFont)))return nullptr;
        for(size_t i=0;i<cascadeFonts.size();++i){
            bool autoOptical=i<cascadeAutomaticOptical.size()&&cascadeAutomaticOptical[i];
            auto metrics=i<cascadeFaceMetrics.size()?cascadeFaceMetrics[i]:font_size_adjust::InitialMetrics();
            auto restriction=i<cascadeCharacters.size()?static_cast<CFCharacterSetRef>(cascadeCharacters[i].get()):nullptr;
            bool privateFace=i<cascadePrivateFonts.size()&&cascadePrivateFonts[i];
            if(!append(static_cast<CTFontRef>(cascadeFonts[i].first.get()),autoOptical,metrics,restriction,privateFace)||
               !append(static_cast<CTFontRef>(cascadeFonts[i].second.get()),autoOptical,metrics,restriction,privateFace))return nullptr;
        }
        const void* keys[]={CFSTR("policy"),CFSTR("fonts"),CFSTR("automatic"),CFSTR("descriptors"),CFSTR("coverage"),CFSTR("private")};const void* values[]={data.get(),fonts.get(),automatic.get(),descriptors.get(),coverage.get(),privateFonts.get()};
        return CFDictionaryCreate(kCFAllocatorDefault,keys,values,6,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks);
    }
    uint32_t capsVariant = 0;
    bool capsSynthesis = true;
    int32_t capsSmallOverride = -1;
    std::vector<JaliumFontFeature> requestedFeatures;
    std::vector<std::array<int64_t,2>> cascadeVerticalOverrides;
    std::array<int64_t,2> VerticalOverrides() const { return {FeatureOverride(0x76657274u,-1),FeatureOverride(0x76727432u,-1)}; }
    int64_t FeatureOverride(uint32_t tag, int64_t fallback) const {
        for (const auto& feature : requestedFeatures) if (feature.tag == tag) fallback = feature.value;
        return fallback;
    }
    int64_t PositionFeatureOverride() const {
        return (positionFlags & 2) ? FeatureOverride(positionVariant == 1 ? 0x73756273u : 0x73757073u,
            (positionFlags & 4) ? 1 : 0) : -1;
    }
    int64_t CapsFeatureOverride() const {
        return capsSmallOverride < 0 ? -1 : FeatureOverride(0x736d6370u, capsSmallOverride);
    }
    std::string capsLanguage;
    uint32_t capsBoundaries = 0, capsContinuation = 0;
    uint32_t emojiVariant = 0, emojiAfter = 0;
    uint32_t positionVariant = 0, positionFlags = 1, positionRunMode = 0;
    bool fallbackCanonicalRequired = false;
    std::vector<FontCascadeEntry> CanonicalFaces() const
    {
        if (!characterRestriction && !fallbackCanonicalRequired && !HasFaceDescriptors()) return {};
        std::vector<FontCascadeEntry> faces{{font, originalFont ? originalFont : font, characterRestriction, disablesKerning}};
        for (size_t i = 0; i < cascadeFonts.size(); ++i)
            faces.push_back({cascadeFonts[i].first.get(), cascadeFonts[i].second.get(), cascadeCharacters[i].get(), cascadeDisablesKerning[i]});
        return faces;
    }
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
    uint64_t cacheId = gMetalTextFormatId.fetch_add(1,std::memory_order_relaxed);
    std::atomic<uint64_t> generation{1};

#ifdef __APPLE__
    CTParagraphStyleRef CreateParagraphStyle() const
    {
        CTTextAlignment ctAlignment = kCTTextAlignmentLeft;
        if (alignment == 1) ctAlignment = kCTTextAlignmentRight;
        else if (alignment == 2) ctAlignment = kCTTextAlignmentCenter;
        else if (alignment == 3) ctAlignment = kCTTextAlignmentJustified;

        // The shared ABI uses 0=wrap and 1=no-wrap, as DirectWrite does.
        CTLineBreakMode breakMode = wrapping == 1 ? kCTLineBreakByClipping :
            wrapping == 2 ? kCTLineBreakByCharWrapping : kCTLineBreakByWordWrapping;
        if (trimming != 0) breakMode = kCTLineBreakByTruncatingTail;

        // CoreText otherwise adds automatic leading beyond CTFont's metrics.
        // Use the same line box reported to managed layout and caret hit tests.
        CGFloat minLine = lineSpacingMethod != 0 && lineSpacing > 0
            ? lineSpacing : LineMetrics().ascent + LineMetrics().descent + LineMetrics().leading;
        CGFloat maxLine = minLine;
        CTParagraphStyleSetting settings[] = {
            {kCTParagraphStyleSpecifierAlignment, sizeof(ctAlignment), &ctAlignment},
            {kCTParagraphStyleSpecifierLineBreakMode, sizeof(breakMode), &breakMode},
            {kCTParagraphStyleSpecifierMinimumLineHeight, sizeof(minLine), &minLine},
            {kCTParagraphStyleSpecifierMaximumLineHeight, sizeof(maxLine), &maxLine},
        };
        return CTParagraphStyleCreate(settings, 4);
    }

    CFAttributedStringRef CreateAttributed(const wchar_t* text,
        uint32_t length, CGColorRef color = nullptr, AppleCapsMap* capsMap = nullptr) const
    {
        if (!font || !text) return nullptr;
        CFStringRef string = CreateCFString(text, length);
        if (!string) return nullptr;
        CTParagraphStyleRef paragraph = CreateParagraphStyle();
        CFMutableDictionaryRef attributes = CFDictionaryCreateMutable(
            kCFAllocatorDefault, 3, &kCFTypeDictionaryKeyCallBacks,
            &kCFTypeDictionaryValueCallBacks);
        CFDictionarySetValue(attributes, kCTFontAttributeName, font);
        if (disablesKerning) {
            int32_t zero = 0;
            CFNumberRef number = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &zero);
            if (number) { CFDictionarySetValue(attributes, kCTKernAttributeName, number); CFRelease(number); }
        }
        if (paragraph)
            CFDictionarySetValue(attributes, kCTParagraphStyleAttributeName, paragraph);
        if (color)
            CFDictionarySetValue(attributes, kCTForegroundColorAttributeName, color);
        CFAttributedStringRef result = CFAttributedStringCreate(
            kCFAllocatorDefault, string, attributes);
        if (result && (SizingActive() || invisibleCharacters || characterRestriction || fallbackCanonicalRequired || capsVariant || positionVariant || emojiVariant || emojiAfter)) {
            CFMutableAttributedStringRef visible = CFAttributedStringCreateMutableCopy(kCFAllocatorDefault, 0, result);
            ReleaseCF(result); result = visible;
            if (visible) {
                ApplyCanonicalFontFallbacks(visible, CFRangeMake(0, CFStringGetLength(string)), CanonicalFaces());
                ApplyFontVisibility(visible, CFRangeMake(0, CFStringGetLength(string)), invisibleCharacters);
                if(SizingActive()){
                    auto settings=SizingAttributes();
                    if(!settings){ReleaseCF(result);ReleaseCF(attributes);ReleaseCF(paragraph);ReleaseCF(string);return nullptr;}
                    CFAttributedStringSetAttribute(visible,CFRangeMake(0,CFStringGetLength(string)),font_size_adjust::Key(),settings);CFRelease(settings);
                    auto adjusted=CreateAppleSizeAdjustedText(visible);ReleaseCF(result);result=adjusted;
                    visible=const_cast<CFMutableAttributedStringRef>(adjusted);
                    if(!visible){ReleaseCF(attributes);ReleaseCF(paragraph);ReleaseCF(string);return nullptr;}
                }
                AppleCapsMap local;
                auto& mapping = capsMap ? *capsMap : local;
                SetAppleEmojiAttributes(visible, CFRangeMake(0, CFStringGetLength(string)), emojiVariant, emojiAfter);
                if (positionVariant) {
                    SetApplePositionAttributes(visible, CFRangeMake(0, CFStringGetLength(string)), positionVariant, positionFlags, positionRunMode, PositionFeatureOverride());
                    if (capsMap && !capsMap->original)
                        capsMap->original = static_cast<CFAttributedStringRef>(CFRetain(visible));
                    auto positioned = CreateApplePositionText(visible);
                    ReleaseCF(result); result = positioned;
                    visible = const_cast<CFMutableAttributedStringRef>(positioned);
                    if (!visible) { ReleaseCF(attributes); ReleaseCF(paragraph); ReleaseCF(string); return nullptr; }
                }
                if (capsVariant) {
                    CFStringRef language = CFStringCreateWithBytes(kCFAllocatorDefault,
                        reinterpret_cast<const UInt8*>(capsLanguage.data()), capsLanguage.size(), kCFStringEncodingASCII, false);
                    SetAppleCapsAttributes(visible, CFRangeMake(0, CFStringGetLength(string)), capsVariant, capsSynthesis, language, CapsFeatureOverride(), capsBoundaries, capsContinuation);
                    ReleaseCF(language);
                    auto transformed = CreateAppleCapsText(visible, mapping);
                    ReleaseCF(result); result = transformed;
                }
                if (result && (emojiVariant || emojiAfter)) {
                    auto transformed = CreateAppleEmojiText(result, mapping);
                    ReleaseCF(result); result = transformed;
                }
                if(result&&SizingActive()){
                    auto finalized=FinalizeAppleSizeAdjustedText(result);ReleaseCF(result);result=finalized;
                }
            }
        }
        ReleaseCF(attributes);
        ReleaseCF(paragraph);
        ReleaseCF(string);
        return result;
    }
#endif
};

JaliumResult MetalTextFormat::SetFontEmoji(uint32_t variant, uint32_t after)
{
#ifdef __APPLE__
    if (!IsValid() || variant > 3 || after > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
    impl_->emojiVariant = variant; impl_->emojiAfter = after;
    impl_->generation.fetch_add(1, std::memory_order_release); return JALIUM_OK;
#else
    (void)variant; (void)after; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

void MetalTextFormat::ApplyEmojiAttributes(void* attributed, uint32_t start, uint32_t length) const
{
#ifdef __APPLE__
    SetAppleEmojiAttributes(static_cast<CFMutableAttributedStringRef>(attributed), CFRangeMake(start, length), impl_->emojiVariant, impl_->emojiAfter);
#else
    (void)attributed; (void)start; (void)length;
#endif
}

void MetalTextFormat::ApplyVerticalFeatureAttributes(void* attributed, uint32_t start, uint32_t length) const
{
#ifdef __APPLE__
    if (!IsValid() || !attributed || !length) return;
    auto text=static_cast<CFMutableAttributedStringRef>(attributed);
    auto apply=[&](CFRange range,const std::array<int64_t,2>& values) {
        auto data=CFDataCreate(kCFAllocatorDefault,reinterpret_cast<const UInt8*>(values.data()),sizeof(int64_t)*2);
        if(!data)throw std::bad_alloc();
        CFAttributedStringSetAttribute(text,range,CFSTR("JaliumVerticalFeatureOverrides"),data);CFRelease(data);
    };
    apply(CFRangeMake(start,length),impl_->VerticalOverrides());
    // Raw settings live on source attributes because CoreText drops custom
    // font-descriptor keys and collapses the reserved vert/vrt2 feature pair.
    std::vector<FontCascadeEntry> faces{{impl_->font,impl_->originalFont?impl_->originalFont:impl_->font,impl_->characterRestriction,impl_->disablesKerning}};
    for(size_t i=0;i<impl_->cascadeFonts.size();++i)
        faces.push_back({impl_->cascadeFonts[i].first.get(),impl_->cascadeFonts[i].second.get(),impl_->cascadeCharacters[i].get(),impl_->cascadeDisablesKerning[i]});
    ForEachCanonicalFontRange(text,CFRangeMake(start,length),faces,[&](size_t selected,CFRange range) {
        if(selected>0 && selected-1<impl_->cascadeVerticalOverrides.size())apply(range,impl_->cascadeVerticalOverrides[selected-1]);
    });
#else
    (void)attributed;(void)start;(void)length;
#endif
}

JaliumResult MetalTextFormat::SetFontPosition(uint32_t variant, uint32_t flags, uint32_t runMode)
{
#ifdef __APPLE__
    if (!IsValid() || variant > 2 || flags > 7 || ((flags & 4) && !(flags & 2)) || runMode > 3 || (!variant && runMode))
        return JALIUM_ERROR_INVALID_ARGUMENT;
    impl_->positionVariant = variant; impl_->positionFlags = flags; impl_->positionRunMode = runMode;
    impl_->generation.fetch_add(1, std::memory_order_release); return JALIUM_OK;
#else
    (void)variant; (void)flags; (void)runMode; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

void MetalTextFormat::ApplyPositionAttributes(void* attributed, uint32_t start, uint32_t length) const
{
#ifdef __APPLE__
    SetApplePositionAttributes(static_cast<CFMutableAttributedStringRef>(attributed), CFRangeMake(start, length),
        impl_->positionVariant, impl_->positionFlags, impl_->positionRunMode, impl_->PositionFeatureOverride());
#else
    (void)attributed; (void)start; (void)length;
#endif
}

JaliumResult MetalTextFormat::SetFontCaps(uint32_t variant, uint32_t flags, const char* language, uint32_t languageLength)
{
#ifdef __APPLE__
    if (!IsValid() || variant > 6 || flags > 7 || ((flags & 4) && !(flags & 2)) || languageLength > 128 || (languageLength && !language)) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::string next = languageLength ? std::string(language, languageLength) : std::string();
    impl_->capsLanguage.swap(next); impl_->capsVariant = variant; impl_->capsSynthesis = (flags & 1) != 0;
    impl_->capsSmallOverride = (flags & 2) ? ((flags & 4) ? 1 : 0) : -1;
    impl_->capsBoundaries = 0; impl_->capsContinuation = 0;
    impl_->generation.fetch_add(1, std::memory_order_release);
    return JALIUM_OK;
#else
    (void)variant; (void)flags; (void)language; (void)languageLength; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetFontCapsContinuation(uint32_t operation)
{
#ifdef __APPLE__
    if (!IsValid() || operation > 2) return JALIUM_ERROR_INVALID_ARGUMENT;
    impl_->capsContinuation = operation;
    impl_->generation.fetch_add(1, std::memory_order_release);
    return JALIUM_OK;
#else
    (void)operation; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetFontCapsContext(uint32_t boundaries)
{
#ifdef __APPLE__
    if (!IsValid() || (boundaries & ~63u)) return JALIUM_ERROR_INVALID_ARGUMENT;
    impl_->capsBoundaries = boundaries;
    impl_->generation.fetch_add(1, std::memory_order_release);
    return JALIUM_OK;
#else
    (void)boundaries; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

void MetalTextFormat::ApplyCapsAttributes(void* attributed, uint32_t start, uint32_t length) const
{
#ifdef __APPLE__
    if (!impl_->capsVariant || !length) return;
    CFStringRef language = CFStringCreateWithBytes(kCFAllocatorDefault,
        reinterpret_cast<const UInt8*>(impl_->capsLanguage.data()), impl_->capsLanguage.size(), kCFStringEncodingASCII, false);
    SetAppleCapsAttributes(static_cast<CFMutableAttributedStringRef>(attributed), CFRangeMake(start, length),
        impl_->capsVariant, impl_->capsSynthesis, language, impl_->CapsFeatureOverride(), impl_->capsBoundaries, impl_->capsContinuation);
    ReleaseCF(language);
#else
    (void)attributed; (void)start; (void)length;
#endif
}

MetalTextFormat::MetalTextFormat(const wchar_t* family, float size,
    int32_t weight, int32_t style) : MetalTextFormat(family, size, weight, style, 100, false) {}

MetalTextFormat::MetalTextFormat(const wchar_t* family, float size,
    int32_t weight, int32_t style, float width) : MetalTextFormat(family, size, weight, style, width, true) {}

MetalTextFormat::MetalTextFormat(const wchar_t* family, float size,
    int32_t weight, int32_t style, float width, bool matchWidth) : impl_(std::make_unique<Impl>())
{
    impl_->size = size;
#ifdef __APPLE__
    CFStringRef familyName = family ? CreateCFString(family) : nullptr;
    impl_->font = CreateAppleTextFont(familyName, size, weight, style, width, matchWidth, &impl_->registeredFont);
    ReleaseCF(familyName);
    if (impl_->font) impl_->variationBaseFont = Impl::FontOwner(CFRetain(impl_->font), [](const void* item) { CFRelease(item); });
#else
    (void)family; (void)weight; (void)style; (void)width; (void)matchWidth;
#endif
}

MetalTextFormat::~MetalTextFormat()
{
#ifdef __APPLE__
    if (impl_ && impl_->font) CFRelease(impl_->font);
    if (impl_ && impl_->originalFont) CFRelease(impl_->originalFont);
    if (impl_ && impl_->characterRestriction) CFRelease(impl_->characterRestriction);
    if (impl_ && impl_->invisibleCharacters) CFRelease(impl_->invisibleCharacters);
#endif
}

bool MetalTextFormat::IsKerningDisabled() const
{
#ifdef __APPLE__
    return impl_ && impl_->disablesKerning;
#else
    return false;
#endif
}

JaliumResult MetalTextFormat::GetFontFeatureSupport(uint32_t tables, const uint32_t* tags, uint32_t count, uint8_t* supported) const
{
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_NOT_SUPPORTED;
    std::vector<uint32_t> available;
    auto release = [](const void* value) { if (value) CFRelease(value); };
    const CTFontTableTag tableTags[] = {kCTFontTableGSUB, kCTFontTableGPOS};
    for (unsigned i = 0; i < 2; ++i) {
        if (!(tables & (1u << i))) continue;
        std::unique_ptr<const void, decltype(release)> owner(CTFontCopyTable(impl_->font, tableTags[i], kCTFontTableOptionNoOptions), release);
        auto table = static_cast<CFDataRef>(owner.get());
        if (table) font_features::Collect(CFDataGetBytePtr(table), static_cast<size_t>(CFDataGetLength(table)), available);
    }
    font_features::Query(available, tags, count, supported);
    return JALIUM_OK;
#else
    (void)tables; (void)tags; (void)count; (void)supported;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

#ifdef __APPLE__
namespace {
CTFontRef CopyTextFontAttributes(CTFontRef font, CFDictionaryRef attributes)
{
    auto release = [](const void* value) { if (value) CFRelease(value); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    Owner original(CTFontCopyFontDescriptor(font), release);
    Owner descriptor(original ? CTFontDescriptorCreateCopyWithAttributes(static_cast<CTFontDescriptorRef>(original.get()), attributes) : nullptr, release);
    Owner graphics(CTFontCopyGraphicsFont(font, nullptr), release);
    auto matrix = CTFontGetMatrix(font);
    return descriptor && graphics ? CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),
        CTFontGetSize(font), &matrix, static_cast<CTFontDescriptorRef>(descriptor.get())) : nullptr;
}
}
#endif

JaliumResult MetalTextFormat::SetFontFaceMetrics(const JaliumFontFaceMetrics* metrics,float* usedSize)
{
#ifdef __APPLE__
    if(!IsValid()||!metrics||!usedSize||!font_size_adjust::ValidMetrics(*metrics))return JALIUM_ERROR_INVALID_ARGUMENT;
    auto release=[](const void* value){if(value)CFRelease(value);};using Owner=std::unique_ptr<const void,decltype(release)>;
    JaliumFontFaceMetrics clean{};clean.structSize=sizeof(clean);clean.sizeAdjust=metrics->sizeAdjust;
    clean.ascent=metrics->ascent;clean.descent=metrics->descent;clean.lineGap=metrics->lineGap;
    clean.verticalAscent=metrics->verticalAscent;clean.verticalDescent=metrics->verticalDescent;clean.verticalLineGap=metrics->verticalLineGap;
    Owner data(CFDataCreate(kCFAllocatorDefault,reinterpret_cast<const UInt8*>(&clean),sizeof(clean)),release);
    const void* key=font_size_adjust::FaceKey();const void* value=data.get();
    Owner attributes(data?CFDictionaryCreate(kCFAllocatorDefault,&key,&value,1,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks):nullptr,release);
    if(!attributes)return JALIUM_ERROR_OUT_OF_MEMORY;
    auto copy=[&](CTFontRef font){return font?CopyTextFontAttributes(font,static_cast<CFDictionaryRef>(attributes.get())):nullptr;};
    Owner font(copy(impl_->font),release),original(copy(impl_->originalFont),release);
    Owner variation(copy(static_cast<CTFontRef>(impl_->variationBaseFont.get())),release);
    if(!font||(impl_->originalFont&&!original)||(impl_->variationBaseFont&&!variation))return JALIUM_ERROR_OUT_OF_MEMORY;
    double used=0;Owner probe(font_size_adjust::Copy(static_cast<CTFontRef>(font.get()),impl_->SizingPolicy(),impl_->sizeAxes,true,impl_->automaticOptical,&used,clean,bool(impl_->registeredFont)),release);
    if(!probe)return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CFRelease(impl_->font);impl_->font=static_cast<CTFontRef>(font.release());
    if(impl_->originalFont)CFRelease(impl_->originalFont);impl_->originalFont=static_cast<CTFontRef>(original.release());
    if(variation)impl_->variationBaseFont=Impl::FontOwner(variation.release(),release);
    impl_->faceMetrics=clean;impl_->generation.fetch_add(1,std::memory_order_release);*usedSize=static_cast<float>(used);return JALIUM_OK;
#else
    (void)metrics;(void)usedSize;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
JaliumResult MetalTextFormat::GetFontInkBounds(const wchar_t* text,uint32_t length,JaliumTextRangeMetrics* bounds)
{
#ifdef __APPLE__
    if(!IsValid()||!text||!bounds)return JALIUM_ERROR_INVALID_ARGUMENT;*bounds={};
    AppleCapsMap mapping;auto attributed=impl_->CreateAttributed(text,length,nullptr,&mapping);
    if(!attributed)return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    // Query ink relative to the glyph baseline. Paragraph line constraints can
    // offset a CTLine's bounds even though they do not change the glyph paths.
    auto query=CFAttributedStringCreateMutableCopy(kCFAllocatorDefault,0,attributed);CFRelease(attributed);
    if(!query)return JALIUM_ERROR_OUT_OF_MEMORY;
    CFAttributedStringRemoveAttribute(query,CFRangeMake(0,CFAttributedStringGetLength(query)),kCTParagraphStyleAttributeName);
    auto line=CTLineCreateWithAttributedString(query);if(!line){CFRelease(query);return JALIUM_ERROR_RESOURCE_CREATION_FAILED;}
    auto rect=CTLineGetBoundsWithOptions(line,kCTLineBoundsUseGlyphPathBounds);
    if(!CGRectIsNull(rect)&&!CGRectIsInfinite(rect)&&std::isfinite(rect.origin.x)&&std::isfinite(rect.origin.y)&&std::isfinite(rect.size.width)&&std::isfinite(rect.size.height)) {
        bounds->textPosition=0;bounds->length=length;bounds->x=CGRectGetMinX(rect);bounds->y=-CGRectGetMaxY(rect);
        bounds->width=rect.size.width;bounds->height=rect.size.height;
    }
    CFRelease(line);CFRelease(query);return JALIUM_OK;
#else
    (void)text;(void)length;(void)bounds;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
JaliumResult MetalTextFormat::GetFontAspectMetric(uint32_t metric,double* aspect)
{
#ifdef __APPLE__
    if(!IsValid()||!aspect||metric<1||metric>5)return JALIUM_ERROR_INVALID_ARGUMENT;
    auto policy=impl_->SizingPolicy();policy.metric=0;policy.aspect=0;double used=0;
    auto selected=font_size_adjust::Copy(impl_->originalFont?impl_->originalFont:impl_->font,policy,impl_->sizeAxes,true,impl_->automaticOptical,&used,impl_->faceMetrics,bool(impl_->registeredFont));
    if(!selected)return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    *aspect=used>0?font_size_adjust::Aspect(selected,metric)*used/impl_->size:0;CFRelease(selected);return JALIUM_OK;
#else
    (void)metric;(void)aspect;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
JaliumResult MetalTextFormat::SetFontSizeAdjust(uint32_t metric,double aspect,uint32_t opticalMode,const JaliumFontVariation* axes,uint32_t count,float* usedSize)
{
#ifdef __APPLE__
    if(!IsValid()||!usedSize||metric>5||opticalMode>2||!std::isfinite(aspect)||aspect<0||count>16384||(count&&!axes))return JALIUM_ERROR_INVALID_ARGUMENT;
    for(uint32_t i=0;i<count;++i){if(!std::isfinite(axes[i].value))return JALIUM_ERROR_INVALID_ARGUMENT;for(unsigned shift=0;shift<32;shift+=8){auto ch=(axes[i].tag>>shift)&255u;if(ch<32||ch>126)return JALIUM_ERROR_INVALID_ARGUMENT;}}
    std::vector<JaliumFontVariation> requested;if(count)requested.assign(axes,axes+count);
    font_size_adjust::Policy policy{metric,opticalMode,aspect,impl_->size,count,0};double used=impl_->size;
    if(metric||!font_size_adjust::IsInitial(impl_->faceMetrics)){auto probe=font_size_adjust::Copy(impl_->font,policy,requested,true,impl_->automaticOptical,&used,impl_->faceMetrics,bool(impl_->registeredFont));if(!probe)return JALIUM_ERROR_RESOURCE_CREATION_FAILED;CFRelease(probe);}
    impl_->sizePolicy=policy;impl_->sizeAxes.swap(requested);impl_->generation.fetch_add(1,std::memory_order_release);*usedSize=static_cast<float>(used);return JALIUM_OK;
#else
    (void)metric;(void)aspect;(void)opticalMode;(void)axes;(void)count;(void)usedSize;return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}
void MetalTextFormat::ApplySizeAdjustAttributes(void* attributed,uint32_t start,uint32_t length) const
{
#ifdef __APPLE__
    if(!IsValid()||!attributed||!impl_->SizingActive())return;
    auto settings=impl_->SizingAttributes();if(!settings)throw std::bad_alloc();
    CFAttributedStringSetAttribute(static_cast<CFMutableAttributedStringRef>(attributed),CFRangeMake(start,length),font_size_adjust::Key(),settings);CFRelease(settings);
#else
    (void)attributed;(void)start;(void)length;
#endif
}

JaliumResult MetalTextFormat::SetFontVariations(const JaliumFontVariation* axes, uint32_t count)
{
    return SetFontOpticalVariations(axes, count, 0);
}

JaliumResult MetalTextFormat::SetFontOpticalVariations(const JaliumFontVariation* axes, uint32_t count, uint32_t mode)
{
#ifdef __APPLE__
    if (!IsValid() || mode > 2 || count > 16384 || (count && !axes)) return JALIUM_ERROR_INVALID_ARGUMENT;
    for (uint32_t i = 0; i < count; ++i) {
        if (!std::isfinite(axes[i].value)) return JALIUM_ERROR_INVALID_ARGUMENT;
        for (unsigned shift = 0; shift < 32; shift += 8)
            if (((axes[i].tag >> shift) & 255u) < 32 || ((axes[i].tag >> shift) & 255u) > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
    }
    std::unordered_map<uint32_t, double> requested;
    for (uint32_t i = 0; i < count; ++i) requested[axes[i].tag] = axes[i].value;
    auto release = [](const void* value) { if (value) CFRelease(value); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    auto copy = [&](CTFontRef font, CTFontRef base, CFArrayRef cascade = nullptr, double scale = 1, bool privateFace = false) -> CTFontRef {
        Owner available(CTFontCopyVariationAxes(base), release);
        auto supported = static_cast<CFArrayRef>(available.get());
        Owner defaults(CTFontCopyVariation(base), release);
        Owner coordinates(CFDictionaryCreateMutable(kCFAllocatorDefault, 0,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        if (!coordinates) return nullptr;
        auto values = const_cast<CFMutableDictionaryRef>(static_cast<CFDictionaryRef>(coordinates.get()));
        for (CFIndex index = 0; supported && index < CFArrayGetCount(supported); ++index) {
            auto axis = static_cast<CFDictionaryRef>(CFArrayGetValueAtIndex(supported, index));
            auto key = static_cast<CFNumberRef>(CFDictionaryGetValue(axis, kCTFontVariationAxisIdentifierKey));
            int64_t identifier = 0;
            if (!key || !CFNumberGetValue(key, kCFNumberSInt64Type, &identifier)) continue;
            auto number = [&](CFStringRef name) { double value = 0; CFNumberGetValue(
                static_cast<CFNumberRef>(CFDictionaryGetValue(axis, name)), kCFNumberDoubleType, &value); return value; };
            double value = number(kCTFontVariationAxisDefaultValueKey);
            if (defaults && CFDictionaryGetValue(static_cast<CFDictionaryRef>(defaults.get()), key))
                CFNumberGetValue(static_cast<CFNumberRef>(CFDictionaryGetValue(static_cast<CFDictionaryRef>(defaults.get()), key)), kCFNumberDoubleType, &value);
            if (identifier == 0x6f70737a && mode != 0)
                value = mode == 1 ? font_size_adjust::BoundSize(static_cast<long double>(impl_->size)*scale) : number(kCTFontVariationAxisDefaultValueKey);
            if (auto found = requested.find(static_cast<uint32_t>(identifier)); found != requested.end()) value = found->second;
            value = std::clamp(value, number(kCTFontVariationAxisMinimumValueKey), number(kCTFontVariationAxisMaximumValueKey));
            Owner coordinate(CFNumberCreate(kCFAllocatorDefault, kCFNumberDoubleType, &value), release);
            if (!coordinate) return nullptr;
            CFDictionarySetValue(values, key, coordinate.get());
        }
        // CoreText has a separate optical-size control. Its inherited "auto"
        // value can override a raw opsz coordinate, particularly in SF fonts.
        Owner optical(CTFontCopyAttribute(base, kCTFontOpticalSizeAttribute), release);
        if (mode != 0 || requested.find(0x6f70737a) != requested.end()) {
            int32_t tag = 0x6f70737a; Owner key(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &tag), release);
            auto value = CFDictionaryGetValue(values, key.get());
            if (value) optical.reset(CFRetain(value));
        }
        Owner original(CTFontCopyFontDescriptor(font), release);
        const void* keys[] = {kCTFontVariationAttribute, kCTFontOpticalSizeAttribute, kCTFontCascadeListAttribute};
        const void* items[] = {coordinates.get(), optical ? optical.get() : kCFNull, cascade};
        Owner attributes(CFDictionaryCreate(kCFAllocatorDefault, keys, items, cascade ? 3 : 2,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        Owner descriptor(original && attributes ? CTFontDescriptorCreateCopyWithAttributes(
            static_cast<CTFontDescriptorRef>(original.get()), static_cast<CFDictionaryRef>(attributes.get())) : nullptr, release);
        // A registered webfont must retain its graphics face; descriptor-name
        // rematching can silently substitute a system font.
        Owner graphics(CTFontCopyGraphicsFont(base, nullptr), release);
        auto matrix = CTFontGetMatrix(font);
        // Copy variable instances from their live font identity. Recreating
        // one from its named-instance descriptor can rematch a system face
        // before shaping, even when a private CGFont was supplied.
        if (privateFace && supported && CFArrayGetCount(supported))
            return descriptor ? CTFontCreateCopyWithAttributes(font, CTFontGetSize(font), &matrix,
                static_cast<CTFontDescriptorRef>(descriptor.get())) : nullptr;
        return descriptor && graphics ? CTFontCreateWithGraphicsFont(static_cast<CGFontRef>(const_cast<void*>(graphics.get())),
            CTFontGetSize(font), &matrix, static_cast<CTFontDescriptorRef>(descriptor.get())) : nullptr;
    };
    std::vector<std::pair<Impl::FontOwner, Impl::FontOwner>> replacements;
    Owner cascadeOwner(CFArrayCreateMutable(kCFAllocatorDefault, impl_->cascadeFonts.size(), &kCFTypeArrayCallBacks), release);
    auto cascade = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(cascadeOwner.get()));
    if (!cascade) return JALIUM_ERROR_OUT_OF_MEMORY;
    for (size_t i = 0; i < impl_->cascadeFonts.size(); ++i) {
        const auto& entry = impl_->cascadeFonts[i]; auto base = static_cast<CTFontRef>(impl_->cascadeVariationBases[i].get());
        double factor=i<impl_->cascadeFaceMetrics.size()?impl_->cascadeFaceMetrics[i].sizeAdjust:1;
        bool privateFace = i < impl_->cascadePrivateFonts.size() && impl_->cascadePrivateFonts[i];
        Impl::FontOwner effective(copy(static_cast<CTFontRef>(entry.first.get()), base,nullptr,factor,privateFace), release);
        Impl::FontOwner original(copy(static_cast<CTFontRef>(entry.second.get()), base,nullptr,factor,privateFace), release);
        if (!effective || !original) return JALIUM_ERROR_OUT_OF_MEMORY;
        Owner descriptor(CTFontCopyFontDescriptor(static_cast<CTFontRef>(effective.get())), release);
        if (!descriptor) return JALIUM_ERROR_OUT_OF_MEMORY;
        CFArrayAppendValue(cascade, descriptor.get());
        replacements.push_back({std::move(effective), std::move(original)});
    }
    auto base = static_cast<CTFontRef>(impl_->variationBaseFont.get());
    Owner font(copy(impl_->font, base, impl_->cascadeFonts.empty() ? nullptr : cascade,impl_->faceMetrics.sizeAdjust,bool(impl_->registeredFont)), release);
    Owner original(impl_->originalFont ? copy(impl_->originalFont, base,nullptr,impl_->faceMetrics.sizeAdjust,bool(impl_->registeredFont)) : nullptr, release);
    if (!font || (impl_->originalFont && !original)) return JALIUM_ERROR_OUT_OF_MEMORY;
    CFRelease(impl_->font); impl_->font = static_cast<CTFontRef>(font.release());
    if (impl_->originalFont) CFRelease(impl_->originalFont);
    impl_->originalFont = static_cast<CTFontRef>(original.release());
    impl_->cascadeFonts = std::move(replacements);
    impl_->opticalPolicy=mode;
    impl_->automaticOptical=mode==1 && requested.find(0x6f70737a)==requested.end();
    impl_->cascadeAutomaticOptical.assign(impl_->cascadeFonts.size(),impl_->automaticOptical);
    impl_->generation.fetch_add(1, std::memory_order_release); return JALIUM_OK;
#else
    (void)axes; (void)count; (void)mode; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetFontFeatures(const JaliumFontFeature* features, uint32_t count)
{
#ifdef __APPLE__
    if (!IsValid() || count > 16384 || (count && !features)) return JALIUM_ERROR_INVALID_ARGUMENT;
    bool disablesKerning = false;
    for (uint32_t i = 0; i < count; ++i) if (features[i].tag == 0x6b65726eu) disablesKerning = features[i].value == 0;
    std::vector<JaliumFontFeature> requestedFeatures;
    if (count) requestedFeatures.assign(features, features + count);
    std::vector<bool> cascadeDisablesKerning(impl_->cascadeFonts.size(), disablesKerning);
    auto release = [](const void* value) { if (value) CFRelease(value); };
    using Owner = std::unique_ptr<const void, decltype(release)>;
    Owner settingsOwner(CFArrayCreateMutable(kCFAllocatorDefault, count, &kCFTypeArrayCallBacks), release);
    auto settings = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(settingsOwner.get()));
    if (!settings) return JALIUM_ERROR_OUT_OF_MEMORY;
    for (uint32_t i = 0; i < count; ++i) {
        UInt8 bytes[4];
        for (unsigned j = 0; j < 4; ++j) {
            bytes[j] = static_cast<UInt8>(features[i].tag >> (24 - j * 8));
            if (bytes[j] < 32 || bytes[j] > 126) return JALIUM_ERROR_INVALID_ARGUMENT;
        }
        Owner tag(CFStringCreateWithBytes(kCFAllocatorDefault, bytes, 4, kCFStringEncodingASCII, false), release);
        int64_t value = features[i].value;
        Owner number(CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt64Type, &value), release);
        const void* keys[] = {kCTFontOpenTypeFeatureTag, kCTFontOpenTypeFeatureValue};
        const void* values[] = {tag.get(), number.get()};
        if (!tag || !number) return JALIUM_ERROR_OUT_OF_MEMORY;
        Owner entry(CFDictionaryCreate(kCFAllocatorDefault, keys, values, 2,
            &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        if (!entry) return JALIUM_ERROR_OUT_OF_MEMORY;
        CFArrayAppendValue(settings, entry.get());
    }
    std::array<int64_t,2> verticalOverrides{-1,-1};
    for (uint32_t i=0;i<count;++i) {
        if(features[i].tag==0x76657274u)verticalOverrides[0]=features[i].value;
        if(features[i].tag==0x76727432u)verticalOverrides[1]=features[i].value;
    }
    std::vector<std::array<int64_t,2>> cascadeVerticalOverrides(impl_->cascadeFonts.size(),verticalOverrides);
    auto copy = [&](CTFontRef font, CFArrayRef cascade = nullptr) -> CTFontRef {
        if (!font) return nullptr;
        return CopyAppleFontFeatures(font, settings, cascade);
    };
    // Prepare every replacement first. Allocation failures keep the entire old
    // cascade, character coverage and retained paragraph fonts intact.
    std::vector<std::pair<Impl::FontOwner, Impl::FontOwner>> cascadeFonts;
    Owner cascadeOwner(CFArrayCreateMutable(kCFAllocatorDefault, impl_->cascadeFonts.size(), &kCFTypeArrayCallBacks), release);
    auto cascade = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(cascadeOwner.get()));
    if (!cascade) return JALIUM_ERROR_OUT_OF_MEMORY;
    for (const auto& entry : impl_->cascadeFonts) {
        Impl::FontOwner effective(copy(static_cast<CTFontRef>(entry.first.get())), release);
        Impl::FontOwner original(copy(static_cast<CTFontRef>(entry.second.get())), release);
        if (!effective || !original) return JALIUM_ERROR_OUT_OF_MEMORY;
        Owner descriptor(CTFontCopyFontDescriptor(static_cast<CTFontRef>(effective.get())), release);
        if (!descriptor) return JALIUM_ERROR_OUT_OF_MEMORY;
        CFArrayAppendValue(cascade, descriptor.get());
        cascadeFonts.push_back({std::move(effective), std::move(original)});
    }
    Owner font(copy(impl_->font, impl_->cascadeFonts.empty() ? nullptr : cascade), release);
    Owner original(impl_->originalFont ? copy(impl_->originalFont) : nullptr, release);
    if (!font || (impl_->originalFont && !original)) return JALIUM_ERROR_OUT_OF_MEMORY;
    CFRelease(impl_->font); impl_->font = static_cast<CTFontRef>(font.release());
    if (impl_->originalFont) CFRelease(impl_->originalFont);
    impl_->originalFont = static_cast<CTFontRef>(original.release());
    impl_->cascadeFonts = std::move(cascadeFonts);
    impl_->disablesKerning = disablesKerning;
    impl_->cascadeDisablesKerning.swap(cascadeDisablesKerning);
    impl_->requestedFeatures.swap(requestedFeatures);
    impl_->cascadeVerticalOverrides.swap(cascadeVerticalOverrides);
    impl_->generation.fetch_add(1, std::memory_order_relaxed);
    return JALIUM_OK;
#else
    (void)features; (void)count; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetFontFallbacks(TextFormat* const* formats, uint32_t count)
{
#ifdef __APPLE__
    if (!IsValid() || (count && !formats)) return JALIUM_ERROR_INVALID_ARGUMENT;
    std::vector<FontResourceReference> resources;
    std::vector<std::pair<Impl::FontOwner, Impl::FontOwner>> cascadeFonts;
    std::vector<Impl::FontOwner> cascadeCharacters;
    std::vector<Impl::FontOwner> cascadeVariationBases;
    std::vector<bool> cascadePrivateFonts;
    std::vector<bool> cascadeDisablesKerning;
    std::vector<bool> cascadeAutomaticOptical;
    std::vector<JaliumFontFaceMetrics> cascadeFaceMetrics;
    std::vector<std::array<int64_t,2>> cascadeVerticalOverrides;
    bool canonicalRequired = false;
    resources.reserve(count);
    auto release = [](const void* value) { if (value) CFRelease(value); };
    std::unique_ptr<const void, decltype(release)> listOwner(CFArrayCreateMutable(kCFAllocatorDefault,
        count, &kCFTypeArrayCallBacks), release);
    auto list = const_cast<CFMutableArrayRef>(static_cast<CFArrayRef>(listOwner.get()));
    if (!list) return JALIUM_ERROR_OUT_OF_MEMORY;
    for (uint32_t i = 0; i < count; ++i) {
        auto format = dynamic_cast<MetalTextFormat*>(formats[i]);
        if (!format || !format->IsValid() || format == this || format->impl_->size != impl_->size)
            return JALIUM_ERROR_INVALID_ARGUMENT;
        std::unique_ptr<const void, decltype(release)> descriptor(CTFontCopyFontDescriptor(format->impl_->font), release);
        if (!descriptor) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        CFArrayAppendValue(list, descriptor.get());
        auto own = [](const void* value) { return value ? Impl::FontOwner(CFRetain(value), [](const void* item) { CFRelease(item); }) : Impl::FontOwner{}; };
        cascadeFonts.emplace_back(own(format->impl_->font), own(format->impl_->originalFont ? format->impl_->originalFont : format->impl_->font));
        cascadeCharacters.push_back(own(format->impl_->characterRestriction));
        cascadeVariationBases.push_back(own(format->impl_->font));
        cascadePrivateFonts.push_back(bool(format->impl_->registeredFont));
        cascadePrivateFonts.insert(cascadePrivateFonts.end(),format->impl_->cascadePrivateFonts.begin(),format->impl_->cascadePrivateFonts.end());
        for (const auto& entry : format->impl_->cascadeFonts) cascadeVariationBases.push_back(entry.first);
        cascadeDisablesKerning.push_back(format->impl_->disablesKerning);
        cascadeVerticalOverrides.push_back(format->impl_->VerticalOverrides());
        cascadeVerticalOverrides.insert(cascadeVerticalOverrides.end(),format->impl_->cascadeVerticalOverrides.begin(),format->impl_->cascadeVerticalOverrides.end());
        cascadeAutomaticOptical.push_back(format->impl_->automaticOptical);
        cascadeFaceMetrics.push_back(format->impl_->faceMetrics);
        cascadeFaceMetrics.insert(cascadeFaceMetrics.end(),format->impl_->cascadeFaceMetrics.begin(),format->impl_->cascadeFaceMetrics.end());
        cascadeAutomaticOptical.insert(cascadeAutomaticOptical.end(),format->impl_->cascadeAutomaticOptical.begin(),format->impl_->cascadeAutomaticOptical.end());
        cascadeFonts.insert(cascadeFonts.end(), format->impl_->cascadeFonts.begin(), format->impl_->cascadeFonts.end());
        cascadeCharacters.insert(cascadeCharacters.end(), format->impl_->cascadeCharacters.begin(), format->impl_->cascadeCharacters.end());
        cascadeDisablesKerning.insert(cascadeDisablesKerning.end(), format->impl_->cascadeDisablesKerning.begin(), format->impl_->cascadeDisablesKerning.end());
        canonicalRequired |= format->impl_->characterRestriction != nullptr || format->impl_->fallbackCanonicalRequired;
        if (format->impl_->registeredFont) resources.push_back(format->impl_->registeredFont);
        resources.insert(resources.end(), format->impl_->fallbackResources.begin(), format->impl_->fallbackResources.end());
    }
    CTFontRef original = impl_->originalFont ? impl_->originalFont : impl_->font;
    CTFontRef next = nullptr;
    if (count || impl_->characterRestriction) {
        const void* keys[2]{}; const void* values[2]{}; CFIndex attributeCount = 0;
        if (count) { keys[attributeCount] = kCTFontCascadeListAttribute; values[attributeCount++] = list; }
        if (impl_->characterRestriction) {
            keys[attributeCount] = kCTFontCharacterSetAttribute; values[attributeCount++] = impl_->characterRestriction;
        }
        std::unique_ptr<const void, decltype(release)> attributes(CFDictionaryCreate(kCFAllocatorDefault,
            keys, values, attributeCount, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
        if (!attributes) return JALIUM_ERROR_OUT_OF_MEMORY;
        std::unique_ptr<const void, decltype(release)> descriptor(CTFontDescriptorCreateWithAttributes(
            static_cast<CFDictionaryRef>(attributes.get())), release);
        if (!descriptor) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
        next = CopyTextFontAttributes(original, static_cast<CFDictionaryRef>(attributes.get()));
    } else next = static_cast<CTFontRef>(CFRetain(original));
    if (!next) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    if (!impl_->originalFont) impl_->originalFont = static_cast<CTFontRef>(CFRetain(original));
    CFRelease(impl_->font); impl_->font = next;
    impl_->fallbackResources.swap(resources);
    impl_->cascadeFonts.swap(cascadeFonts); impl_->fallbackCanonicalRequired = canonicalRequired;
    impl_->cascadeCharacters.swap(cascadeCharacters); impl_->cascadeVariationBases.swap(cascadeVariationBases); impl_->cascadeDisablesKerning.swap(cascadeDisablesKerning); impl_->cascadeAutomaticOptical.swap(cascadeAutomaticOptical);impl_->cascadeFaceMetrics.swap(cascadeFaceMetrics);
    impl_->cascadeVerticalOverrides.swap(cascadeVerticalOverrides);
    impl_->cascadePrivateFonts.swap(cascadePrivateFonts);
    impl_->generation.fetch_add(1, std::memory_order_release);
    return JALIUM_OK;
#else
    (void)formats; (void)count;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetUnicodeRanges(const JaliumUnicodeRange* ranges, uint32_t count, bool restricted)
{
#ifdef __APPLE__
    if (!IsValid() || (count && !ranges)) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto release = [](const void* value) { if (value) CFRelease(value); };
    CTFontRef original = impl_->originalFont ? impl_->originalFont : impl_->font;
    std::unique_ptr<const void, decltype(release)> actual(CTFontCopyCharacterSet(original), release);
    std::unique_ptr<const void, decltype(release)> selected(CFCharacterSetCreateMutable(kCFAllocatorDefault), release);
    if (!actual || !selected) return JALIUM_ERROR_OUT_OF_MEMORY;
    auto mask = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(selected.get()));
    if (restricted) {
        for (uint32_t i = 0; i < count; ++i) {
            if (ranges[i].first > ranges[i].last || ranges[i].last > 0x10ffff) return JALIUM_ERROR_INVALID_ARGUMENT;
            CFCharacterSetAddCharactersInRange(mask, CFRangeMake(ranges[i].first, ranges[i].last - ranges[i].first + 1));
        }
        CFCharacterSetIntersect(mask, static_cast<CFCharacterSetRef>(actual.get()));
    } else CFCharacterSetUnion(mask, static_cast<CFCharacterSetRef>(actual.get()));
    const void* key = kCTFontCharacterSetAttribute; const void* value = mask;
    std::unique_ptr<const void, decltype(release)> attributes(CFDictionaryCreate(kCFAllocatorDefault, &key, &value, 1,
        &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks), release);
    if (!attributes) return JALIUM_ERROR_OUT_OF_MEMORY;
    std::unique_ptr<const void, decltype(release)> descriptor(CTFontDescriptorCreateWithAttributes(static_cast<CFDictionaryRef>(attributes.get())), release);
    if (!descriptor) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CTFontRef next = CopyTextFontAttributes(impl_->font, static_cast<CFDictionaryRef>(attributes.get()));
    if (!next) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    if (!impl_->originalFont) impl_->originalFont = static_cast<CTFontRef>(CFRetain(original));
    CFRelease(impl_->font); impl_->font = next;
    if (impl_->characterRestriction) CFRelease(impl_->characterRestriction);
    impl_->characterRestriction = restricted ? static_cast<CFCharacterSetRef>(CFRetain(mask)) : nullptr;
    impl_->generation.fetch_add(1, std::memory_order_release);
    return JALIUM_OK;
#else
    (void)ranges; (void)count; (void)restricted; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::GetCharacterCoverage(const uint32_t* characters, uint32_t count, uint8_t* supported) const
{
#ifdef __APPLE__
    if (!IsValid() || (count && (!characters || !supported))) return JALIUM_ERROR_INVALID_ARGUMENT;
    CFCharacterSetRef set = impl_->characterRestriction ? static_cast<CFCharacterSetRef>(CFRetain(impl_->characterRestriction)) : CTFontCopyCharacterSet(impl_->font);
    if (!set) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    for (uint32_t i = 0; i < count; ++i) supported[i] = CFCharacterSetIsLongCharacterMember(set, characters[i]) ? 1 : 0;
    CFRelease(set); return JALIUM_OK;
#else
    (void)characters; (void)count; (void)supported; return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::SetFontDisplay(const JaliumFontDisplayEntry* entries, uint32_t count)
{
#ifdef __APPLE__
    if (!IsValid() || (count && !entries)) return JALIUM_ERROR_INVALID_ARGUMENT;
    auto release = [](const void* value) { if (value) CFRelease(value); };
    std::unique_ptr<const void, decltype(release)> priorOwner(CFCharacterSetCreateMutable(kCFAllocatorDefault), release);
    std::unique_ptr<const void, decltype(release)> hiddenOwner(CFCharacterSetCreateMutable(kCFAllocatorDefault), release);
    if (!priorOwner || !hiddenOwner) return JALIUM_ERROR_OUT_OF_MEMORY;
    auto prior = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(priorOwner.get()));
    auto hidden = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(hiddenOwner.get()));
    for (uint32_t i = 0; i < count; ++i) {
        const auto& entry = entries[i];
        if (entry.format) {
            auto face = dynamic_cast<MetalTextFormat*>(reinterpret_cast<TextFormat*>(entry.format));
            if (!face || face == this || !face->IsValid() || face->impl_->size != impl_->size) return JALIUM_ERROR_INVALID_ARGUMENT;
            std::unique_ptr<const void, decltype(release)> coverage(CTFontCopyCharacterSet(face->impl_->font), release);
            if (!coverage) return JALIUM_ERROR_OUT_OF_MEMORY;
            std::unique_ptr<const void, decltype(release)> eligibleOwner(CFCharacterSetCreateMutable(kCFAllocatorDefault), release);
            if (!eligibleOwner) return JALIUM_ERROR_OUT_OF_MEMORY;
            auto eligible = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(eligibleOwner.get()));
            for (uint32_t j = 0; j < entry.rangeCount; ++j)
                CFCharacterSetAddCharactersInRange(eligible, CFRangeMake(entry.ranges[j].first, entry.ranges[j].last - entry.ranges[j].first + 1));
            CFCharacterSetIntersect(eligible, static_cast<CFCharacterSetRef>(coverage.get()));
            CFCharacterSetUnion(prior, eligible);
        } else {
            std::unique_ptr<const void, decltype(release)> rangeOwner(CFCharacterSetCreateMutable(kCFAllocatorDefault), release);
            if (!rangeOwner) return JALIUM_ERROR_OUT_OF_MEMORY;
            auto range = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(rangeOwner.get()));
            for (uint32_t j = 0; j < entry.rangeCount; ++j)
                CFCharacterSetAddCharactersInRange(range, CFRangeMake(entry.ranges[j].first, entry.ranges[j].last - entry.ranges[j].first + 1));
            if (entry.flags & 2) {
                std::unique_ptr<const void, decltype(release)> complementOwner(CFCharacterSetCreateMutableCopy(kCFAllocatorDefault, prior), release);
                if (!complementOwner) return JALIUM_ERROR_OUT_OF_MEMORY;
                auto complement = const_cast<CFMutableCharacterSetRef>(static_cast<CFCharacterSetRef>(complementOwner.get()));
                CFCharacterSetInvert(complement); CFCharacterSetIntersect(range, complement); CFCharacterSetUnion(hidden, range);
            }
            // Even a visible pending face claims its range: fallback cannot
            // trigger or inherit a later waiting face's independent block period.
            for (uint32_t j = 0; j < entry.rangeCount; ++j)
                CFCharacterSetAddCharactersInRange(prior, CFRangeMake(entry.ranges[j].first, entry.ranges[j].last - entry.ranges[j].first + 1));
        }
    }
    if (impl_->invisibleCharacters) CFRelease(impl_->invisibleCharacters);
    bool hasHidden = false;
    for (CFIndex plane = 0; plane <= 16; ++plane) hasHidden |= CFCharacterSetHasMemberInPlane(hidden, plane);
    impl_->invisibleCharacters = hasHidden ? static_cast<CFCharacterSetRef>(CFRetain(hidden)) : nullptr;
    impl_->generation.fetch_add(1, std::memory_order_release); return JALIUM_OK;
#else
    (void)entries; (void)count; return JALIUM_ERROR_NOT_SUPPORTED;
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

void MetalTextFormat::SetAlignment(int32_t value) { impl_->alignment = value;impl_->generation.fetch_add(1); }
void MetalTextFormat::SetParagraphAlignment(int32_t value) { impl_->paragraphAlignment = value;impl_->generation.fetch_add(1); }
void MetalTextFormat::SetTrimming(int32_t value) { impl_->trimming = value;impl_->generation.fetch_add(1); }
void MetalTextFormat::SetWordWrapping(int32_t value) { impl_->wrapping = value;impl_->generation.fetch_add(1); }
void MetalTextFormat::SetLineSpacing(int32_t method, float spacing, float baseline)
{
    impl_->lineSpacingMethod = method;
    impl_->lineSpacing = spacing;
    impl_->lineSpacingBaseline = baseline;
    impl_->generation.fetch_add(1);
}
void MetalTextFormat::SetMaxLines(uint32_t value) { impl_->maxLines = value;impl_->generation.fetch_add(1); }

uint64_t MetalTextFormat::CacheIdentity() const
{return impl_->cacheId^(impl_->generation.load(std::memory_order_acquire)*0x9e3779b97f4a7c15ull);}

JaliumResult MetalTextFormat::MeasureText(const wchar_t* text,
    uint32_t textLength, float maxWidth, float maxHeight,
    JaliumTextMetrics* metrics)
{
    if (!metrics || !text || textLength == 0) return JALIUM_ERROR_INVALID_ARGUMENT;
    *metrics = {};
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    AppleCapsMap caps;
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength, nullptr, &caps);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CTFramesetterRef framesetter = CTFramesetterCreateWithAttributedString(attributed);
    CGFloat ascent = impl_->LineMetrics().ascent;
    CGFloat descent = impl_->LineMetrics().descent;
    CGFloat leading = impl_->LineMetrics().leading;
    float lineHeight = impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0
        ? impl_->lineSpacing : static_cast<float>(ascent + descent + leading);
    const CGFloat width = std::isfinite(maxWidth) && maxWidth > 0 ? maxWidth : 1000000.0;
    const CGFloat height = std::isfinite(maxHeight) && maxHeight > 0 ? maxHeight : 1000000.0;
    CGPathRef path = framesetter ? CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr) : nullptr;
    CTFrameRef frame = path ? CTFramesetterCreateFrame(framesetter, CFRangeMake(0, 0), path, nullptr) : nullptr;
    if (!frame) {
        ReleaseCF(path); ReleaseCF(framesetter); ReleaseCF(attributed);
        return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    }
    AppleCapsLines capsLines(frame, attributed, caps, width, height, impl_->wrapping, impl_->alignment, impl_->font,
        impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();
    CFIndex count = CFArrayGetCount(lines);
    if (impl_->maxLines > 0) count = std::min<CFIndex>(count, impl_->maxLines);
    double measuredWidth = 0, inclusiveWidth = 0, overriddenHeight = 0;
    for (CFIndex i = 0; i < count; ++i) {
        auto line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, i));
        // These are logical advances, not bitmap extents. Rounding each
        // character here accumulates an error against CoreText's caret offsets.
        const double advance = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        inclusiveWidth = std::max(inclusiveWidth, advance);
        measuredWidth = std::max(measuredWidth, advance - CTLineGetTrailingWhitespaceWidth(line));
        if(capsLines.HasMetricOverrides()) {
            CGFloat a=0,d=0,g=0;capsLines.Metrics(line,&a,&d,&g);
            float rowHeight=impl_->lineSpacingMethod!=0&&impl_->lineSpacing>0?impl_->lineSpacing:static_cast<float>(a+d+g);
            overriddenHeight+=rowHeight;lineHeight=std::max(lineHeight,rowHeight);
            if(i==0){ascent=a;descent=d;leading=g;}
        }
    }
    CFStringRef string = CFAttributedStringGetString(attributed);
    const CFIndex length = CFStringGetLength(string);
    CFRange visible = capsLines.Visible();
    if (count && length > 0 && visible.location + visible.length == length &&
        (!impl_->maxLines || count < impl_->maxLines)) {
        const UniChar last = CFStringGetCharacterAtIndex(string, length - 1);
        // A terminal paragraph break has an additional empty insertion row.
        if (last == '\r' || last == '\n' || last == 0x2028 || last == 0x2029) ++count;
    }
    const uint32_t lineCount = std::max(1u, static_cast<uint32_t>(count));
    metrics->width = static_cast<float>(measuredWidth);
    metrics->widthIncludingTrailingWhitespace = static_cast<float>(inclusiveWidth);
    metrics->height = std::min(static_cast<float>(height), capsLines.HasMetricOverrides()?static_cast<float>(overriddenHeight+std::max<CFIndex>(0,lineCount-CFArrayGetCount(lines))*lineHeight):lineHeight * lineCount);
    metrics->lineHeight = lineHeight;
    metrics->baseline = impl_->lineSpacingBaseline > 0
        ? impl_->lineSpacingBaseline : static_cast<float>(ascent);
    metrics->ascent = static_cast<float>(ascent);
    metrics->descent = static_cast<float>(descent);
    metrics->lineGap = static_cast<float>(leading);
    metrics->lineCount = lineCount;
    ReleaseCF(frame); ReleaseCF(path); ReleaseCF(framesetter);
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
    CGFloat ascent = impl_->LineMetrics().ascent;
    CGFloat descent = impl_->LineMetrics().descent;
    CGFloat leading = impl_->LineMetrics().leading;
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

JaliumResult MetalTextFormat::GetFontUnitMetrics(JaliumFontUnitMetrics* metrics)
{
    if (!metrics) return JALIUM_ERROR_INVALID_ARGUMENT;
    *metrics = {sizeof(JaliumFontUnitMetrics), impl_->size * .5f, impl_->size,
        impl_->size * .5f, impl_->size, impl_->size, impl_->size * 1.2f, 0};
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    auto font = impl_->MetricsFont();
    metrics->available = 16;
    metrics->ascent = CTFontGetAscent(font);
    metrics->lineHeight = impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing :
        CTFontGetAscent(font) + CTFontGetDescent(font) + CTFontGetLeading(font);
    const CGFloat xHeight = CTFontGetXHeight(font), capHeight = CTFontGetCapHeight(font);
    if (xHeight > 0) { metrics->xHeight = xHeight; metrics->available |= 1; }
    if (capHeight > 0) { metrics->capHeight = capHeight; metrics->available |= 2; }
    auto advance = [&](wchar_t character, float& result, uint32_t flag) {
        // One-character shaping uses the same restricted, ordered fallback as
        // painting, with no neighbours to introduce kerning or ligatures.
        CFAttributedStringRef attributed = impl_->CreateAttributed(&character, 1);
        CTLineRef line = attributed ? CTLineCreateWithAttributedString(attributed) : nullptr;
        if (line && CTLineGetGlyphCount(line) > 0) {
            const double value = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
            if (std::isfinite(value) && value >= 0) { result = value; metrics->available |= flag; }
        }
        ReleaseCF(line); ReleaseCF(attributed);
    };
    advance(L'0', metrics->zeroAdvance, 4); advance(L'\x6c34', metrics->ideographicAdvance, 8);
    metrics->underlinePosition = -CTFontGetUnderlinePosition(font);
    metrics->underlineThickness = CTFontGetUnderlineThickness(font);
    if (std::isfinite(metrics->underlinePosition)) metrics->available |= 32;
    if (std::isfinite(metrics->underlineThickness) && metrics->underlineThickness > 0) metrics->available |= 64;
    return JALIUM_OK;
#else
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::GetFontMathConstants(JaliumFontMathConstants* constants)
{
    if (!constants) return JALIUM_ERROR_INVALID_ARGUMENT;
    *constants = {sizeof(JaliumFontMathConstants), 0, 0, 0};
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    constexpr CTFontTableTag mathTag = 0x4d415448u; // 'MATH'
    CFDataRef table = CTFontCopyTable(impl_->font, mathTag, 0);
    if (table) {
        font_math::Read(CFDataGetBytePtr(table),
            static_cast<size_t>(CFDataGetLength(table)), constants);
        CFRelease(table);
    }
    return JALIUM_OK;
#else
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
    AppleCapsMap caps;
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength, nullptr, &caps);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    if (caps.Active() && CFAttributedStringGetLength(attributed) == 0) {
        result->caretHeight = static_cast<float>(impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        result->isInside = pointX == 0 && pointY >= 0 && pointY <= result->caretHeight;
        ReleaseCF(attributed); return JALIUM_OK;
    }
    CTFramesetterRef setter = CTFramesetterCreateWithAttributedString(attributed);
    const CGFloat width = maxWidth > 0 ? maxWidth : 1000000.0;
    const CGFloat height = maxHeight > 0 ? std::ceil(maxHeight) : 1000000.0;
    CGPathRef path = CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr);
    CTFrameRef frame = CTFramesetterCreateFrame(setter, CFRangeMake(0, 0), path, nullptr);
    AppleCapsLines capsLines(frame, attributed, caps, width, height, impl_->wrapping, impl_->alignment, impl_->font,
        impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();
    CFIndex count = CFArrayGetCount(lines);
    std::vector<CGPoint> origins(static_cast<size_t>(std::max<CFIndex>(count, 1)));
    if (count > 0) capsLines.Origins(CFRangeMake(0, count), origins.data());
    CFIndex selected = count > 0 ? count - 1 : 0;
    for (CFIndex i = 0; i < count; ++i) {
        CGFloat ascent = 0, descent = 0, leading = 0;
        CTLineRef line = static_cast<CTLineRef>(const_cast<void*>(CFArrayGetValueAtIndex(lines, i)));
        capsLines.Metrics(line, &ascent, &descent, &leading);
        float top = static_cast<float>(height - origins[i].y - ascent);
        float bottom = static_cast<float>(height - origins[i].y + descent + leading);
        if (pointY >= top && pointY <= bottom) { selected = i; break; }
    }
    if (count > 0) {
        CTLineRef line = static_cast<CTLineRef>(const_cast<void*>(CFArrayGetValueAtIndex(lines, selected)));
        CGPoint position = CGPointMake(pointX - origins[selected].x, 0);
        CFIndex index = CTLineGetStringIndexForPosition(line, position);
        if (index == kCFNotFound) index = CFAttributedStringGetLength(attributed);
        if (caps.Active() && index < CFAttributedStringGetLength(attributed)) {
            auto group = caps.Cluster(CFAttributedStringGetString(attributed), index);
            double first = CTLineGetOffsetForStringIndex(line, group.location, nullptr);
            double last = CTLineGetOffsetForStringIndex(line, group.location + group.length, nullptr);
            index = std::abs(position.x - first) <= std::abs(position.x - last) ? group.location : group.location + group.length;
        }
        result->textPosition = Utf16IndexToWide(text, textLength, caps.ToSource(index));
        CGFloat secondary = 0;
        CGFloat primary = CTLineGetOffsetForStringIndex(line, index, &secondary);
        // CoreText returns the nearest insertion index already. Its secondary
        // bidi caret is not a trailing-hit flag; advancing again skips a cluster.
        result->isTrailingHit = 0;
        result->isInside = pointX >= origins[selected].x && pointX <= width ? 1 : 0;
        CGFloat ascent = 0, descent = 0, leading = 0;
        capsLines.Metrics(line, &ascent, &descent, &leading);
        result->caretX = static_cast<float>(primary + origins[selected].x);
        result->caretY = static_cast<float>(height - origins[selected].y - ascent);
        result->caretHeight = static_cast<float>(ascent + descent + leading);
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
    AppleCapsMap caps;
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength, nullptr, &caps);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    if (caps.Active() && CFAttributedStringGetLength(attributed) == 0) {
        result->textPosition = textPosition; result->isTrailingHit = isTrailingHit != 0; result->isInside = 1;
        result->caretHeight = static_cast<float>(impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        ReleaseCF(attributed); return JALIUM_OK;
    }
    CTFramesetterRef setter=CTFramesetterCreateWithAttributedString(attributed);
    CGFloat width=maxWidth>0?maxWidth:1000000.0;
    CGFloat height=maxHeight>0?std::ceil(maxHeight):1000000.0;
    CGPathRef path=CGPathCreateWithRect(CGRectMake(0,0,width,height),nullptr);
    CTFrameRef frame=CTFramesetterCreateFrame(setter,CFRangeMake(0,0),path,nullptr);
    AppleCapsLines capsLines(frame, attributed, caps, width, height, impl_->wrapping, impl_->alignment, impl_->font,
        impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();CFIndex lineCount=CFArrayGetCount(lines);
    if(impl_->maxLines>0)lineCount=std::min<CFIndex>(lineCount,impl_->maxLines);
    if(lineCount==0){ReleaseCF(frame);ReleaseCF(path);ReleaseCF(setter);
        ReleaseCF(attributed);return JALIUM_OK;}
    std::vector<CGPoint> origins(static_cast<size_t>(lineCount));
    capsLines.Origins(CFRangeMake(0,lineCount),origins.data());
    CFIndex index = caps.ToPaint(WideIndexToUtf16(text, textLength, textPosition));
    CFStringRef string = CFAttributedStringGetString(attributed);
    CFIndex anchorIndex = index;
    if (index < CFStringGetLength(string)) {
        CFRange cluster = caps.Cluster(string, index);
        anchorIndex = cluster.location;
        index = cluster.location + (isTrailingHit ? cluster.length : 0);
    }
    CFIndex selected=lineCount-1;
    for(CFIndex i=0;i<lineCount;++i){CTLineRef candidate=static_cast<CTLineRef>(
            const_cast<void*>(CFArrayGetValueAtIndex(lines,i)));
        CFRange range=CTLineGetStringRange(candidate);
        if(anchorIndex>=range.location && (anchorIndex<range.location+range.length ||
            (anchorIndex==CFStringGetLength(string) && anchorIndex==range.location+range.length))){selected=i;break;}}
    CTLineRef line=static_cast<CTLineRef>(const_cast<void*>(
        CFArrayGetValueAtIndex(lines,selected)));
    CGFloat secondary = 0;
    CGFloat primary = CTLineGetOffsetForStringIndex(line, index, &secondary);
    if (anchorIndex < CFStringGetLength(string)) {
        // At a bidi run boundary CoreText's primary insertion caret can be on
        // the other run. Ask for the logical edge of this actual character.
        __block CGFloat edge = primary;
        CFIndex edgeIndex = caps.Active() && isTrailingHit ? index - 1 : anchorIndex;
        CTLineEnumerateCaretOffsets(line, ^(double offset, CFIndex charIndex, bool leadingEdge, bool* stop) {
            if (charIndex == edgeIndex && leadingEdge == (isTrailingHit == 0)) {
                edge = offset;
                *stop = true;
            }
        });
        primary = edge;
    }
    CGFloat ascent=0,descent=0,leading=0;
    capsLines.Metrics(line, &ascent, &descent, &leading);
    result->textPosition = textPosition;
    result->isTrailingHit = isTrailingHit != 0;
    result->isInside = 1;
    result->caretX = static_cast<float>(origins[selected].x + primary);
    result->caretY = static_cast<float>(height-origins[selected].y-ascent);
    result->caretHeight = static_cast<float>(ascent+descent+leading);
    ReleaseCF(frame);ReleaseCF(path);ReleaseCF(setter);ReleaseCF(attributed);
    return JALIUM_OK;
#else
    (void)maxWidth; (void)maxHeight; (void)isTrailingHit;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::HitTestTextRange(const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight, uint32_t textPosition, uint32_t length,
    JaliumTextRangeMetrics* result)
{
    if (result) *result = {};
    if (!result || !text || textPosition > textLength || length > textLength - textPosition ||
        !std::isfinite(maxWidth) || !std::isfinite(maxHeight))
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    if (!textLength) {
        result->height = static_cast<float>(impl_->LineMetrics().ascent +
            impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        return JALIUM_OK;
    }
    auto release = [](const void* value) { ReleaseCF(value); };
    AppleCapsMap caps;
    std::unique_ptr<const void, decltype(release)> attributed(impl_->CreateAttributed(text, textLength, nullptr, &caps), release);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    auto attr = static_cast<CFAttributedStringRef>(attributed.get());
    if (caps.Active() && CFAttributedStringGetLength(attr) == 0) {
        result->textPosition = textPosition; result->length = length;
        result->height = static_cast<float>(impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        return JALIUM_OK;
    }
    std::unique_ptr<const void, decltype(release)> setter(CTFramesetterCreateWithAttributedString(attr), release);
    if (!setter) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CGFloat width = maxWidth > 0 ? maxWidth : 1000000.0;
    CGFloat height = maxHeight > 0 ? std::ceil(maxHeight) : 1000000.0;
    std::unique_ptr<const void, decltype(release)> path(CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr), release);
    if (!path) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    std::unique_ptr<const void, decltype(release)> frame(CTFramesetterCreateFrame(
        static_cast<CTFramesetterRef>(setter.get()), CFRangeMake(0, 0), static_cast<CGPathRef>(path.get()), nullptr), release);
    if (!frame) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    auto ctFrame = static_cast<CTFrameRef>(frame.get());
    AppleCapsLines capsLines(ctFrame, attr, caps, width, height, impl_->wrapping, impl_->alignment, impl_->font,
        impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();
    CFIndex count = CFArrayGetCount(lines);
    if (impl_->maxLines) count = std::min<CFIndex>(count, impl_->maxLines);
    CFStringRef string = CFAttributedStringGetString(attr);
    CFIndex start = caps.ToPaint(WideIndexToUtf16(text, textLength, textPosition));
    CFIndex end = caps.ToPaint(WideIndexToUtf16(text, textLength, textPosition + length), true);
    if (start < CFStringGetLength(string))
        start = caps.Cluster(string, start).location;
    if (length && end > 0) {
        CFRange last = caps.Cluster(string, end - 1);
        end = last.location + last.length;
    } else end = start;
    for (CFIndex i = 0; i < count; ++i) {
        auto line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, i));
        CFRange row = CTLineGetStringRange(line);
        CFIndex rowEnd = row.location + row.length;
        if (start < row.location || start > rowEnd || (start == rowEnd && start < CFStringGetLength(string)))
            continue;
        end = std::min(end, rowEnd);
        CGPoint origin{};
        capsLines.Origins(CFRangeMake(i, 1), &origin);
        CGFloat ascent = 0, descent = 0, leading = 0;
        capsLines.Metrics(line, &ascent, &descent, &leading);
        __block double left = CTLineGetOffsetForStringIndex(line, start, nullptr);
        __block double right = left;
        __block bool found = false;
        if (length) {
            // One layout and one traversal of its caret edges. Re-shaping the
            // paragraph once per grapheme makes long AppKit queries quadratic.
            CTLineEnumerateCaretOffsets(line, ^(double offset, CFIndex index, bool, bool*) {
                if (index < start || index >= end) return;
                if (!found) { left = right = offset; found = true; }
                else { left = std::min(left, offset); right = std::max(right, offset); }
            });
        } else if (start < CFStringGetLength(string)) {
            CTLineEnumerateCaretOffsets(line, ^(double offset, CFIndex index, bool leadingEdge, bool* stop) {
                if (index == start && leadingEdge) { left = right = offset; *stop = true; }
            });
        }
        result->textPosition = Utf16IndexToWide(text, textLength, caps.ToSource(start));
        result->length = Utf16IndexToWide(text, textLength, caps.ToSource(end)) - result->textPosition;
        result->x = static_cast<float>(origin.x + left);
        result->y = static_cast<float>(height - origin.y - ascent);
        result->width = static_cast<float>(right - left);
        result->height = static_cast<float>(ascent + descent + leading);
        return JALIUM_OK;
    }
    return JALIUM_ERROR_INVALID_ARGUMENT;
#else
    (void)maxWidth; (void)maxHeight;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

JaliumResult MetalTextFormat::GetLineMetrics(const wchar_t* text, uint32_t textLength,
    float maxWidth, float maxHeight, uint32_t textPosition, int32_t backwardAffinity,
    JaliumTextLineMetrics* result)
{
    if (result) *result = {};
    if (!result || !text || textPosition > textLength ||
        !std::isfinite(maxWidth) || !std::isfinite(maxHeight))
        return JALIUM_ERROR_INVALID_ARGUMENT;
#ifdef __APPLE__
    if (!IsValid()) return JALIUM_ERROR_INVALID_STATE;
    if (!textLength) {
        result->height = static_cast<float>(impl_->LineMetrics().ascent +
            impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        return JALIUM_OK;
    }
    auto release = [](const void* value) { ReleaseCF(value); };
    AppleCapsMap caps;
    std::unique_ptr<const void, decltype(release)> attributed(impl_->CreateAttributed(text, textLength, nullptr, &caps), release);
    if (!attributed) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    auto attr = static_cast<CFAttributedStringRef>(attributed.get());
    if (caps.Active() && CFAttributedStringGetLength(attr) == 0) {
        result->length = textLength; result->rightCaretPosition = textLength; result->rightBackwardAffinity = 1;
        result->height = static_cast<float>(impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        return JALIUM_OK;
    }
    std::unique_ptr<const void, decltype(release)> setter(CTFramesetterCreateWithAttributedString(attr), release);
    if (!setter) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    CGFloat width = maxWidth > 0 ? maxWidth : 1000000.0;
    CGFloat height = maxHeight > 0 ? std::ceil(maxHeight) : 1000000.0;
    std::unique_ptr<const void, decltype(release)> path(CGPathCreateWithRect(CGRectMake(0, 0, width, height), nullptr), release);
    if (!path) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    std::unique_ptr<const void, decltype(release)> frame(CTFramesetterCreateFrame(
        static_cast<CTFramesetterRef>(setter.get()), CFRangeMake(0, 0), static_cast<CGPathRef>(path.get()), nullptr), release);
    if (!frame) return JALIUM_ERROR_RESOURCE_CREATION_FAILED;
    auto ctFrame = static_cast<CTFrameRef>(frame.get());
    AppleCapsLines capsLines(ctFrame, attr, caps, width, height, impl_->wrapping, impl_->alignment, impl_->font,
        impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0 ? impl_->lineSpacing : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();
    CFIndex count = CFArrayGetCount(lines);
    if (impl_->maxLines) count = std::min<CFIndex>(count, impl_->maxLines);
    CFStringRef string = CFAttributedStringGetString(attr);
    CFIndex textEnd = CFStringGetLength(string);
    CFIndex insertion = caps.ToPaint(WideIndexToUtf16(text, textLength, textPosition));
    if (insertion < textEnd)
        insertion = caps.Cluster(string, insertion).location;
    auto isBreak = [](UniChar c) { return c == '\r' || c == '\n' || c == 0x2028 || c == 0x2029; };
    for (CFIndex i = 0; i < count; ++i) {
        auto line = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, i));
        CFRange row = CTLineGetStringRange(line);
        CFIndex rowEnd = row.location + row.length;
        if (insertion < row.location || insertion > rowEnd) continue;
        if (insertion == rowEnd && insertion < textEnd && !backwardAffinity) continue;
        // Affinity only crosses a shared soft boundary, never a paragraph break.
        if (insertion == rowEnd && row.length && isBreak(CFStringGetCharacterAtIndex(string, rowEnd - 1)))
            continue;
        CFIndex contentEnd = rowEnd;
        while (contentEnd > row.location && isBreak(CFStringGetCharacterAtIndex(string, contentEnd - 1)))
            --contentEnd;
        CGPoint origin{};
        capsLines.Origins(CFRangeMake(i, 1), &origin);
        CGFloat ascent = 0, descent = 0, leading = 0;
        capsLines.Metrics(line, &ascent, &descent, &leading);
        __block double left = CTLineGetOffsetForStringIndex(line, row.location, nullptr);
        __block double right = left;
        __block CFIndex leftIndex = row.location, rightIndex = row.location;
        __block bool leftBackward = false, rightBackward = false, found = false;
        // Enumerate the actual glyph edges: logical start/end are reversed in
        // RTL and may be interior visual carets in mixed-direction paragraphs.
        const auto* capsMapping = &caps;
        CTLineEnumerateCaretOffsets(line, ^(double offset, CFIndex index, bool leadingEdge, bool*) {
            if (index < row.location || index >= contentEnd) return;
            CFRange cluster = capsMapping->Cluster(string, index);
            if ((leadingEdge && index != cluster.location) || (!leadingEdge && index + 1 != cluster.location + cluster.length)) return;
            CFIndex caret = cluster.location + (leadingEdge ? 0 : cluster.length);
            if (!found || offset < left || (offset == left && leadingEdge)) {
                left = offset; leftIndex = caret; leftBackward = !leadingEdge;
            }
            if (!found || offset > right || (offset == right && !leadingEdge)) {
                right = offset; rightIndex = caret; rightBackward = !leadingEdge;
            }
            found = true;
        });
        result->textPosition = Utf16IndexToWide(text, textLength, caps.ToSource(row.location));
        result->length = Utf16IndexToWide(text, textLength, caps.ToSource(contentEnd)) - result->textPosition;
        result->leftCaretPosition = Utf16IndexToWide(text, textLength, caps.ToSource(leftIndex));
        result->rightCaretPosition = Utf16IndexToWide(text, textLength, caps.ToSource(rightIndex));
        result->leftBackwardAffinity = leftBackward;
        result->rightBackwardAffinity = rightBackward;
        result->x = static_cast<float>(origin.x + left);
        result->y = static_cast<float>(height - origin.y - ascent);
        result->width = static_cast<float>(right - left);
        result->height = static_cast<float>(ascent + descent + leading);
        return JALIUM_OK;
    }
    // CoreText omits the empty row after a terminal paragraph separator.
    if (count > 0 && insertion == textEnd && isBreak(CFStringGetCharacterAtIndex(string, textEnd - 1)) &&
        (!impl_->maxLines || count < impl_->maxLines)) {
        auto last = static_cast<CTLineRef>(CFArrayGetValueAtIndex(lines, count - 1));
        CGPoint origin{};
        capsLines.Origins(CFRangeMake(count - 1, 1), &origin);
        CGFloat ascent = 0, descent = 0, leading = 0;
        capsLines.Metrics(last, &ascent, &descent, &leading);
        result->textPosition = result->leftCaretPosition = result->rightCaretPosition = textLength;
        result->height = static_cast<float>(impl_->LineMetrics().ascent +
            impl_->LineMetrics().descent + impl_->LineMetrics().leading);
        result->y = static_cast<float>(height - origin.y + descent + leading);
        return JALIUM_OK;
    }
    return JALIUM_ERROR_INVALID_ARGUMENT;
#else
    (void)maxWidth; (void)maxHeight; (void)backwardAffinity;
    return JALIUM_ERROR_NOT_SUPPORTED;
#endif
}

TextParagraph* MetalTextFormat::CreateParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
    int32_t alignment, int32_t direction)
{
    return CreateParagraphWithWrapping(text, length, spans, spanCount, width, minLineHeight,
        alignment, direction, 0);
}

TextParagraph* MetalTextFormat::CreateParagraphWithWrapping(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
    int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateParagraphCore(text, length, spans, spanCount, width, minLineHeight, alignment, direction, wrapping, false); }

TextParagraph* MetalTextFormat::CreateVerticalParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float inlineSize, float minColumnWidth,
    int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateParagraphCore(text, length, spans, spanCount, inlineSize, minColumnWidth, alignment, direction, wrapping, false, true); }

TextParagraph* MetalTextFormat::CreateAdvancedVerticalParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, const JaliumTextAdvanceUnit* units, uint32_t unitCount,
    float inlineSize, float minColumnWidth, int32_t direction, bool contextPaint)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphCore(text, length, spans, spanCount,
        inlineSize, minColumnWidth, 0, direction, 1, contextPaint, true));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && apple->ApplyAdvances(units, unitCount) ? paragraph.release() : nullptr;
}

TextParagraph* MetalTextFormat::CreateUprightParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float inlineSize, float minColumnWidth,
    int32_t alignment, int32_t direction, int32_t wrapping)
{ return CreateParagraphCore(text, length, spans, spanCount, inlineSize, minColumnWidth, alignment, direction, wrapping, false, true, true); }

TextParagraph* MetalTextFormat::CreateAdvancedUprightParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, const JaliumTextAdvanceUnit* units, uint32_t unitCount,
    float inlineSize, float minColumnWidth, int32_t direction, bool contextPaint)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphCore(text, length, spans, spanCount,
        inlineSize, minColumnWidth, 0, direction, 1, contextPaint, true, true));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && apple->ApplyAdvances(units, unitCount) ? paragraph.release() : nullptr;
}

TextParagraph* MetalTextFormat::CreateParagraphCore(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
    int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms)
{ return CreateParagraphCore(text, length, spans, spanCount, width, minLineHeight, alignment, direction, wrapping, contextPaint, verticalForms, false); }

TextParagraph* MetalTextFormat::CreateCombinedParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount,
    const JaliumTextCombineUnit* combined, uint32_t combinedCount,
    const JaliumTextAdvanceUnit* advances, uint32_t advanceCount,
    float inlineSize, float minColumnWidth, int32_t alignment, int32_t direction,
    int32_t wrapping, int32_t orientation, bool contextPaint)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphCore(text, length, spans, spanCount,
        inlineSize, minColumnWidth, alignment, direction, wrapping, contextPaint,
        orientation != 2, orientation == 1, combined, combinedCount));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && (!advanceCount || apple->ApplyAdvances(advances, advanceCount)) ? paragraph.release() : nullptr;
}

TextParagraph* MetalTextFormat::CreateParagraphCore(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
    int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms, bool uprightForms)
{
    return CreateParagraphCore(text, length, spans, spanCount, width, minLineHeight,
        alignment, direction, wrapping, contextPaint, verticalForms, uprightForms, nullptr, 0);
}

TextParagraph* MetalTextFormat::CreateParagraphCore(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, float width, float minLineHeight,
    int32_t alignment, int32_t direction, int32_t wrapping, bool contextPaint, bool verticalForms, bool uprightForms,
    const JaliumTextCombineUnit* combined, uint32_t combinedCount)
{
#ifdef __APPLE__
    if (!IsValid()) return nullptr;
    std::vector<void*> fonts;
    std::vector<void*> visibility;
    std::vector<std::vector<FontCascadeEntry>> canonical;
    fonts.reserve(spanCount);
    for (uint32_t i = 0; i < spanCount; ++i) {
        auto format = dynamic_cast<MetalTextFormat*>(reinterpret_cast<TextFormat*>(spans[i].format));
        if (!format || !format->IsValid()) return nullptr;
        fonts.push_back(const_cast<void*>(static_cast<const void*>(format->impl_->font)));
        visibility.push_back(const_cast<void*>(static_cast<const void*>(format->impl_->invisibleCharacters)));
        canonical.push_back(format->impl_->CanonicalFaces());
    }
    JaliumTextMetrics defaultMetrics{};GetFontMetrics(&defaultMetrics);
    auto paragraph = std::make_unique<MetalTextParagraph>(text, length,
        const_cast<void*>(static_cast<const void*>(impl_->MetricsFont())), fonts, spans, spanCount,
        width, minLineHeight, alignment, direction, wrapping, visibility, canonical, contextPaint,
        impl_->HasFaceDescriptors()?&defaultMetrics:nullptr, verticalForms, uprightForms, combined, combinedCount);
    return paragraph->IsValid() ? paragraph.release() : nullptr;
#else
    (void)text; (void)length; (void)spans; (void)spanCount; (void)width; (void)minLineHeight;
    (void)alignment; (void)direction; (void)wrapping; (void)contextPaint; (void)verticalForms; (void)uprightForms;
    (void)combined; (void)combinedCount;
    return nullptr;
#endif
}

TextParagraph* MetalTextFormat::CreateSpacedParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount,
    const JaliumTextSpacingUnit* units, uint32_t unitCount,
    float width, float minLineHeight, int32_t direction)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphWithWrapping(text, length, spans,
        spanCount, width, minLineHeight, 0, direction, 1));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && apple->ApplySpacing(units, unitCount) ? paragraph.release() : nullptr;
}

TextParagraph* MetalTextFormat::CreateContextParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, const JaliumTextSpacingUnit* units, uint32_t unitCount,
    float width, float minLineHeight, int32_t direction)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphCore(text, length, spans,
        spanCount, width, minLineHeight, 0, direction, 1, true));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && apple->ApplySpacing(units, unitCount) ? paragraph.release() : nullptr;
}

TextParagraph* MetalTextFormat::CreateAdvancedParagraph(const uint16_t* text, uint32_t length,
    const JaliumTextSpan* spans, uint32_t spanCount, const JaliumTextAdvanceUnit* units, uint32_t unitCount,
    float width, float minLineHeight, int32_t direction, bool contextPaint)
{
    std::unique_ptr<TextParagraph> paragraph(CreateParagraphCore(text, length, spans,
        spanCount, width, minLineHeight, 0, direction, 1, contextPaint));
    auto apple = dynamic_cast<MetalTextParagraph*>(paragraph.get());
    return apple && apple->ApplyAdvances(units, unitCount) ? paragraph.release() : nullptr;
}

bool MetalTextFormat::Rasterize(const wchar_t* text, uint32_t textLength,
    float x, float y, float width, float height,
    const float* deviceTransform,
    float clipX, float clipY, float clipWidth, float clipHeight,
    float r, float g, float b, float a,
    std::vector<uint8_t>& pixels, uint32_t& pixelWidth,
    uint32_t& pixelHeight, float& deviceX, float& deviceY,
    const uint32_t* ranges, uint32_t rangeCount, int32_t visibility) const
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

    // Color glyphs keep their palette and do not use the foreground color's
    // alpha. Apply brush opacity to the context for both color and outline runs.
    CGColorRef color = CGColorCreateGenericRGB(r, g, b, 1);
    AppleCapsMap caps;
    CFAttributedStringRef attributed = impl_->CreateAttributed(text, textLength, color, &caps);
    std::vector<uint32_t> mappedRanges;
    if (caps.Active()) { mappedRanges = caps.PaintRanges(ranges, rangeCount); ranges = mappedRanges.data(); rangeCount = mappedRanges.size() / 2; }
    CTFramesetterRef setter = attributed
        ? CTFramesetterCreateWithAttributedString(attributed) : nullptr;
    // Editors round their line boxes to device-independent pixels. CoreText
    // rejects a whole line when that box is slightly below the font's natural
    // height (PingFang 16: 22 DIP versus 22.3999). Fit at least the first line;
    // the caller's clip still governs visible pixels, and converting through
    // frameHeight below keeps its baseline at the same drawing origin.
    const CGFloat naturalLineHeight = impl_->lineSpacingMethod != 0 && impl_->lineSpacing > 0
        ? impl_->lineSpacing
        : impl_->LineMetrics().ascent + impl_->LineMetrics().descent + impl_->LineMetrics().leading;
    const CGFloat frameHeight = std::ceil(std::max<CGFloat>(height, naturalLineHeight));
    CGPathRef path = setter
        ? CGPathCreateWithRect(CGRectMake(0, 0, width, frameHeight), nullptr) : nullptr;
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
    AppleCapsLines capsLines(frame, attributed, caps, width, frameHeight, impl_->wrapping, impl_->alignment, impl_->font, naturalLineHeight, true, impl_->LineMetrics());
    CFArrayRef lines = capsLines.Lines();
    CFIndex lineCount = lines ? CFArrayGetCount(lines) : 0;
    if (impl_->maxLines > 0)
        lineCount = std::min<CFIndex>(lineCount, impl_->maxLines);
    std::vector<CGPoint> lineOrigins(static_cast<size_t>(lineCount));
    if (lineCount > 0) {
        capsLines.Origins(CFRangeMake(0, lineCount), lineOrigins.data());
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

    // CoreText lays frames from the top. DirectWrite paragraph alignment is a
    // vertical alignment contract (Near/Far/Center), so translate the selected
    // line set inside the requested layout height before computing device
    // bounds and drawing it.
    double verticalOffset = 0.0;
    double availableVertical = std::max(0.0,
        static_cast<double>(height) - static_cast<double>(textBounds.size.height));
    if (impl_->paragraphAlignment == 1) verticalOffset = -availableVertical;
    else if (impl_->paragraphAlignment == 2) verticalOffset = -availableVertical * 0.5;
    textBounds.origin.y += verticalOffset;

    const float m11 = deviceTransform[0], m12 = deviceTransform[1];
    const float m21 = deviceTransform[2], m22 = deviceTransform[3];
    const float dx = deviceTransform[4], dy = deviceTransform[5];
    auto mapPoint = [&](double coreTextX, double coreTextY) {
        // Core Text is y-up inside the frame; Jalium drawing coordinates are
        // y-down.  Convert through (localY = height - coreTextY), then apply
        // the complete local-to-device matrix (DPI included).
        const double localX = static_cast<double>(x) + coreTextX;
        const double localY = static_cast<double>(y) + frameHeight - coreTextY;
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

    // Device coordinates and texture rows run downwards, while the bitmap
    // CGContext runs upwards. Reflect the device Y coordinate in the cropped
    // bitmap height so glyphs stay upright, including under rotation and DPI.
    CGAffineTransform textToBitmap = CGAffineTransformMake(
        m11, -m12, -m21, m22,
        m11 * x + m21 * (y + frameHeight) + dx - rasterLeft,
        pixelHeight - (m12 * x + m22 * (y + frameHeight) + dy - rasterTop));
    CGContextConcatCTM(context, textToBitmap);
    CGContextSetTextMatrix(context, CGAffineTransformIdentity);
    CGContextSetAlpha(context, std::clamp(a, 0.0f, 1.0f));
    ConfigureFontPositioning(context, UsesFractionalFontPositioning(*this));
    for(CFIndex index=0;index<lineCount;++index){
        CTLineRef line=static_cast<CTLineRef>(const_cast<void*>(
            CFArrayGetValueAtIndex(lines,index)));
        CGPoint origin=lineOrigins[static_cast<size_t>(index)];
        CFArrayRef runs = CTLineGetGlyphRuns(line);
        for (CFIndex runIndex = 0; runIndex < CFArrayGetCount(runs); ++runIndex) {
            auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(runs, runIndex));
            if (!IsFontRunInvisible(CTRunGetAttributes(run)))
                DrawFontRunRanges(run, context, CGPointMake(origin.x, origin.y + verticalOffset),
                    CFAttributedStringGetString(attributed), ranges, rangeCount, visibility);
        }
    }
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

#endif

#ifndef JALIUM_APPLE_TEXT_ONLY
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

void* MetalBitmap::EnsureTexture(void* deviceHandle, void* commandQueueHandle)
{
#ifdef __APPLE__
    std::scoped_lock lock(impl_->textureMutex);
    if (!impl_->texture) {
        id<MTLDevice> device = (__bridge id<MTLDevice>)deviceHandle;
        id<MTLCommandQueue> queue = (__bridge id<MTLCommandQueue>)commandQueueHandle;
        if (!device || !queue || impl_->pixels.empty()) return nullptr;
        MTLTextureDescriptor* descriptor = [MTLTextureDescriptor
            texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
            width:impl_->width height:impl_->height mipmapped:YES];
        descriptor.storageMode = MTLStorageModeShared;
        descriptor.usage = MTLTextureUsageShaderRead;
        id<MTLTexture> texture = [device newTextureWithDescriptor:descriptor];
        if (!texture) return nullptr;
        [texture replaceRegion:MTLRegionMake2D(0, 0, impl_->width, impl_->height)
            mipmapLevel:0 withBytes:impl_->pixels.data() bytesPerRow:impl_->width * 4];

        if (texture.mipmapLevelCount > 1) {
            // Submit before publishing the texture. Every render target for this
            // backend uses this same queue and commits its draw after EnsureTexture
            // returns, so even another window sees completed mip levels before
            // sampling. A separate buffer also avoids interrupting an open render
            // encoder. No CPU mip loop, extra full-size copy, or synchronous GPU wait.
            id<MTLCommandBuffer> upload = [queue commandBuffer];
            id<MTLBlitCommandEncoder> blit = [upload blitCommandEncoder];
            if (!blit) return nullptr;
            upload.label = @"Jalium Bitmap Mipmaps";
            [blit generateMipmapsForTexture:texture];
            [blit endEncoding];
            [upload commit];
        }
        impl_->texture = texture;
        // replaceRegion copies the base pixels synchronously; the command buffer
        // retains the texture until mip generation finishes.
        std::vector<uint8_t>().swap(impl_->pixels);
    }
    return (__bridge void*)impl_->texture;
#else
    (void)deviceHandle;
    (void)commandQueueHandle;
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
    bool videoRange = false;
    uint32_t yuvMatrix = 1; // 0=BT.601, 1=BT.709, 2=BT.2020
    void* lifetimeContext = nullptr;
    void (*lifetimeRetain)(void*) = nullptr;
    void (*lifetimeRelease)(void*) = nullptr;
#ifdef __APPLE__
    id<MTLTexture> texture = nil;
    id<MTLTexture> planeY = nil;
    id<MTLTexture> planeUV = nil;
    CVPixelBufferRef pixelBuffer = nullptr;
    CVMetalTextureCacheRef textureCache = nullptr;
    CVMetalTextureRef cvTexture0 = nullptr;
    CVMetalTextureRef cvTexture1 = nullptr;
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
        impl_->lifetimeRetain = retain;
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
            impl_->videoRange = pixelFormat == kCVPixelFormatType_420YpCbCr8BiPlanarVideoRange ||
                pixelFormat == kCVPixelFormatType_420YpCbCr10BiPlanarVideoRange;
            CFTypeRef matrix=CVBufferGetAttachment(impl_->pixelBuffer,
                kCVImageBufferYCbCrMatrixKey,nullptr);
            if(matrix==kCVImageBufferYCbCrMatrix_ITU_R_601_4)impl_->yuvMatrix=0;
            else if(matrix==kCVImageBufferYCbCrMatrix_ITU_R_2020)impl_->yuvMatrix=2;
            if (pixelFormat == kCVPixelFormatType_32BGRA) {
                CVMetalTextureRef cvTexture = nullptr;
                if (CVMetalTextureCacheCreateTextureFromImage(kCFAllocatorDefault,
                    impl_->textureCache, impl_->pixelBuffer, nullptr,
                    MTLPixelFormatBGRA8Unorm, descriptor.width, descriptor.height,
                    0, &cvTexture) == kCVReturnSuccess && cvTexture) {
                    impl_->texture = CVMetalTextureGetTexture(cvTexture);
                    impl_->cvTexture0 = cvTexture;
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
                if (y) { impl_->planeY = CVMetalTextureGetTexture(y);impl_->cvTexture0=y; }
                if (uv) { impl_->planeUV = CVMetalTextureGetTexture(uv);impl_->cvTexture1=uv; }
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
                if (y) { impl_->planeY = CVMetalTextureGetTexture(y);impl_->cvTexture0=y; }
                if (uv) { impl_->planeUV = CVMetalTextureGetTexture(uv);impl_->cvTexture1=uv; }
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
    if (impl_->cvTexture0) CFRelease(impl_->cvTexture0);
    if (impl_->cvTexture1) CFRelease(impl_->cvTexture1);
    if (impl_->textureCache) CFRelease(impl_->textureCache);
    if (impl_->pixelBuffer) CFRelease(impl_->pixelBuffer);
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
uint32_t MetalVideoSurface::YuvMatrix() const{return impl_?impl_->yuvMatrix:1;}
bool MetalVideoSurface::IsVideoRange() const{return impl_&&impl_->videoRange;}

void MetalVideoSurface::RetainForCommandBuffer(void* commandBufferHandle) const
{
#ifdef __APPLE__
    id<MTLCommandBuffer> command = (__bridge id<MTLCommandBuffer>)commandBufferHandle;
    if (!command || !impl_) return;
    void* context = impl_->lifetimeContext;
    auto retain = impl_->lifetimeRetain;
    auto release = impl_->lifetimeRelease;
    if (context && retain && release) {
        retain(context);
        [command addCompletedHandler:^(id<MTLCommandBuffer>) { release(context); }];
    }
    CVPixelBufferRef pixelBuffer = impl_->pixelBuffer;
    if (pixelBuffer) {
        CFRetain(pixelBuffer);
        [command addCompletedHandler:^(id<MTLCommandBuffer>) { CFRelease(pixelBuffer); }];
    }
    IOSurfaceRef ioSurface = impl_->ioSurface;
    if (ioSurface) {
        CFRetain(ioSurface);
        [command addCompletedHandler:^(id<MTLCommandBuffer>) { CFRelease(ioSurface); }];
    }
#else
    (void)commandBufferHandle;
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

TextFormat* MetalBackend::CreateTextFormatWithWidth(const wchar_t* family, float size,
    int32_t weight, int32_t style, float width)
{
    if (!family || !std::isfinite(size) || size <= 0 || !std::isfinite(width) || width < 0) return nullptr;
    auto* result = new MetalTextFormat(family, size, weight, style, width);
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
        // CGImage's top row already becomes the first bitmap-buffer row.
        // Metal samples this top-down buffer without an AppKit coordinate flip.
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
    for (uint32_t y = 0; y < height; ++y) {
        const uint8_t* source = pixels + static_cast<size_t>(y) * stride;
        uint8_t* destination = copy.data() + static_cast<size_t>(y) * width * 4;
        uint32_t x = 0;
#if defined(__aarch64__)
        // Eight BGRA pixels at a time. Rounded division by 255 is exact for
        // every 8-bit color/alpha pair, including transparent colored pixels.
        // Keep the row stride and scalar tail independent of vector alignment.
        const auto premultiply = [](uint8x8_t color, uint8x8_t alpha) {
            const auto rounded = vaddq_u16(vmull_u8(color, alpha), vdupq_n_u16(128));
            return vshrn_n_u16(vaddq_u16(rounded, vshrq_n_u16(rounded, 8)), 8);
        };
        for (; width - x >= 8; x += 8) {
            auto pixels8 = vld4_u8(source + x * 4);
            for (int channel = 0; channel < 3; ++channel)
                pixels8.val[channel] = premultiply(pixels8.val[channel], pixels8.val[3]);
            vst4_u8(destination + x * 4, pixels8);
        }
#endif
        for (; x < width; ++x) {
            const uint32_t alpha = source[x * 4 + 3];
            for (uint32_t channel = 0; channel < 3; ++channel)
                destination[x * 4 + channel] = static_cast<uint8_t>(
                    (source[x * 4 + channel] * alpha + 127) / 255);
            destination[x * 4 + 3] = static_cast<uint8_t>(alpha);
        }
    }
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
    id<MTLLibrary> supportLibrary = [impl_->device newLibraryWithSource:
        [NSString stringWithUTF8String:kInkShaderSource] options:nil error:&error];
    if (!supportLibrary) return nullptr;
    auto result = std::make_unique<MetalBrushShader>();
    result->key = key; result->source = source; result->blendMode = blendMode;
    std::string msl,entry,diagnostics;
    if(CompileMetalBrushShader(source,msl,entry,diagnostics)){
        id<MTLLibrary> fragmentLibrary=[impl_->device newLibraryWithSource:
            [NSString stringWithUTF8String:msl.c_str()] options:nil error:&error];
        id<MTLFunction> vertex=[supportLibrary newFunctionWithName:@"jalium_ink_vertex"];
        id<MTLFunction> fragment=fragmentLibrary?[fragmentLibrary newFunctionWithName:
            [NSString stringWithUTF8String:entry.c_str()]]:nil;
        if(vertex&&fragment){MTLRenderPipelineDescriptor* descriptor=[MTLRenderPipelineDescriptor new];
            descriptor.vertexFunction=vertex;descriptor.fragmentFunction=fragment;
            auto* color=descriptor.colorAttachments[0];color.pixelFormat=MTLPixelFormatBGRA8Unorm;
            color.blendingEnabled=YES;color.rgbBlendOperation=MTLBlendOperationAdd;
            color.alphaBlendOperation=MTLBlendOperationAdd;
            color.sourceRGBBlendFactor=blendMode==2?MTLBlendFactorZero:MTLBlendFactorOne;
            color.sourceAlphaBlendFactor=blendMode==2?MTLBlendFactorZero:MTLBlendFactorOne;
            color.destinationRGBBlendFactor=blendMode==1?MTLBlendFactorOne:
                MTLBlendFactorOneMinusSourceAlpha;
            color.destinationAlphaBlendFactor=blendMode==1?MTLBlendFactorOne:
                MTLBlendFactorOneMinusSourceAlpha;
            result->pipeline=[impl_->device newRenderPipelineStateWithDescriptor:descriptor error:&error];}
    }
    if(!result->pipeline){id<MTLFunction> fallback=[supportLibrary newFunctionWithName:@"jalium_ink"];
        if(fallback)result->fallbackPipeline=[impl_->device
            newComputePipelineStateWithFunction:fallback error:&error];}
    if(!result->pipeline&&!result->fallbackPipeline)return nullptr;
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
        !layer->texture || (!brush->pipeline&&!brush->fallbackPipeline))
        return JALIUM_INK_DISPATCH_ERROR_INVALID_ARG;
    std::scoped_lock layerLock(layer->mutex);
    std::array<uint8_t, 80> patched{};
    std::memcpy(patched.data(), constants, 80);
    auto* values = reinterpret_cast<float*>(patched.data());
    auto* uints = reinterpret_cast<uint32_t*>(patched.data());
    uints[12] = pointCount;
    values[16] = static_cast<float>(layer->width);
    values[17] = static_cast<float>(layer->height);
    id<MTLCommandBuffer> command = [impl_->queue commandBuffer];
    if (!command) return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
    uint32_t minX=static_cast<uint32_t>(std::clamp(std::floor(values[8]),0.0f,
        static_cast<float>(layer->width)));
    uint32_t minY=static_cast<uint32_t>(std::clamp(std::floor(values[9]),0.0f,
        static_cast<float>(layer->height)));
    uint32_t maxX=static_cast<uint32_t>(std::clamp(std::ceil(values[10]),
        static_cast<float>(minX),static_cast<float>(layer->width)));
    uint32_t maxY=static_cast<uint32_t>(std::clamp(std::ceil(values[11]),
        static_cast<float>(minY),static_cast<float>(layer->height)));
    if(maxX==minX||maxY==minY)return JALIUM_INK_DISPATCH_OK;
    if(brush->pipeline){
        id<MTLBuffer> pointBuffer=[impl_->device newBufferWithBytes:strokePoints
            length:static_cast<NSUInteger>(pointCount)*16 options:MTLResourceStorageModeShared];
        if(!pointBuffer)return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        MTLRenderPassDescriptor* pass=[MTLRenderPassDescriptor renderPassDescriptor];
        pass.colorAttachments[0].texture=layer->texture;
        pass.colorAttachments[0].loadAction=MTLLoadActionLoad;
        pass.colorAttachments[0].storeAction=MTLStoreActionStore;
        id<MTLRenderCommandEncoder> encoder=[command renderCommandEncoderWithDescriptor:pass];
        if(!encoder)return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        [encoder setRenderPipelineState:brush->pipeline];
        MTLViewport viewport{0,0,(double)layer->width,(double)layer->height,0,1};
        MTLScissorRect scissor{minX,minY,maxX-minX,maxY-minY};
        [encoder setViewport:viewport];[encoder setScissorRect:scissor];
        [encoder setVertexBytes:patched.data() length:patched.size() atIndex:0];
        [encoder setFragmentBytes:patched.data() length:patched.size() atIndex:0];
        std::array<uint8_t,16> emptyParams{};
        if(extraParams&&extraParamsSize<=4096){std::vector<uint8_t> padded(
                std::max<uint32_t>(extraParamsSize,16));
            std::memcpy(padded.data(),extraParams,extraParamsSize);
            [encoder setFragmentBytes:padded.data() length:padded.size() atIndex:1];}
        else if(extraParams&&extraParamsSize){id<MTLBuffer> extras=[impl_->device
                newBufferWithBytes:extraParams length:extraParamsSize
                options:MTLResourceStorageModeShared];[encoder setFragmentBuffer:extras offset:0 atIndex:1];}
        else [encoder setFragmentBytes:emptyParams.data() length:emptyParams.size() atIndex:1];
        [encoder setFragmentBuffer:pointBuffer offset:0 atIndex:2];
        [encoder drawPrimitives:MTLPrimitiveTypeTriangle vertexStart:0 vertexCount:3];
        [encoder endEncoding];
    }else{
        id<MTLComputeCommandEncoder> encoder=[command computeCommandEncoder];
        if(!encoder)return JALIUM_INK_DISPATCH_ERROR_TRANSIENT;
        [encoder setComputePipelineState:brush->fallbackPipeline];
        [encoder setTexture:layer->texture atIndex:0];
        [encoder setBytes:strokePoints length:static_cast<NSUInteger>(pointCount)*16 atIndex:0];
        [encoder setBytes:patched.data() length:patched.size() atIndex:1];
        uint32_t blend=static_cast<uint32_t>(brush->blendMode);
        [encoder setBytes:&blend length:sizeof(blend) atIndex:2];
        MTLSize groups=MTLSizeMake((maxX-minX+7)/8,(maxY-minY+7)/8,1);
        [encoder dispatchThreadgroups:groups threadsPerThreadgroup:MTLSizeMake(8,8,1)];
        [encoder endEncoding];
    }
    [command commit];
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

#endif // !JALIUM_APPLE_TEXT_ONLY

} // namespace jalium
