#include "jalium_api.h"
#include "../src/metal_text_paragraph.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
namespace {
int checks = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;
Format Font(JaliumContext* context, const char16_t* family, int weight = 400) {
    Format result(jalium_text_format_create(context, Managed(family), 32, weight, 0), jalium_text_format_destroy);
    Check(result != nullptr, "Variation format unavailable"); jalium_text_format_set_word_wrapping(result.get(), 1); return result;
}
CTFontRef Reference(CGFontRef graphics, const std::vector<JaliumFontVariation>& requests) {
    CTFontRef base = CTFontCreateWithGraphicsFont(graphics, 32, nullptr, nullptr);
    // CGFont uses unlocalized names; pair them with raw fvar records rather
    // than the localized names returned by CTFontCopyVariationAxes.
    NSArray* axes = CFBridgingRelease(CGFontCopyVariationAxes(graphics));
    NSData* table = CFBridgingRelease(CGFontCopyTableForTag(graphics,0x66766172));
    const auto* bytes = static_cast<const uint8_t*>(table.bytes);
    auto u16 = [&](size_t at) { Check(at+2<=table.length,"fvar reference bounds"); return uint32_t(bytes[at])<<8|bytes[at+1]; };
    auto u32 = [&](size_t at) { return u16(at)<<16|u16(at+2); };
    Check(table.length>=16 && axes.count==u16(8),"fvar reference axes mismatch");
    NSDictionary* defaults = CFBridgingRelease(CTFontCopyVariation(base));
    NSMutableDictionary* coordinates = [NSMutableDictionary new]; NSNumber* optical = nil;
    for (NSUInteger index=0;index<axes.count;++index) {
        NSDictionary* axis=axes[index]; uint32_t tag=u32(u16(4)+index*u16(10));
        double value = defaults[@(tag)] ? [defaults[@(tag)] doubleValue] : [axis[(__bridge id)kCGFontVariationAxisDefaultValue] doubleValue]; bool explicitValue=false;
        for (const auto& request : requests) if (request.tag == tag) {value=request.value;explicitValue=true;}
        value = std::clamp(value, [axis[(__bridge id)kCGFontVariationAxisMinValue] doubleValue], [axis[(__bridge id)kCGFontVariationAxisMaxValue] doubleValue]);
        coordinates[axis[(__bridge id)kCGFontVariationAxisName]] = @(value);
        if(tag==0x6f70737a && explicitValue)optical=@(value);
    }
    CGFontRef selected = CGFontCreateCopyWithVariations(graphics, (__bridge CFDictionaryRef)coordinates);
    Check(selected != nullptr, "Independent CGFont variation selection unavailable");
    CTFontDescriptorRef descriptor = optical ? CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge id)kCTFontOpticalSizeAttribute:optical}) : nullptr;
    CTFontRef result = CTFontCreateWithGraphicsFont(selected, 32, nullptr, descriptor); if(descriptor)CFRelease(descriptor); CGFontRelease(selected); CFRelease(base); return result;
}
void Compare(JaliumTextFormat* actual, CTFontRef expected) {
    const std::u16string sample = u"ABMABM";
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:@"ABMABM" attributes:@{NSFontAttributeName:(__bridge id)expected}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    double width = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr); JaliumTextMetrics measured{};
    Check(jalium_text_format_measure_text(actual, Managed(sample.c_str()), sample.size(), 100000, 1000, &measured) == JALIUM_OK, "Variation measure failed");
    if (std::abs(measured.width-width) > .003) std::fprintf(stderr,"Variation width: actual %g expected %g\n",measured.width,width);
    Check(std::abs(measured.width-width) < .003, "Variation advance differs from independent CGFont reference");
    JaliumTextSpan span{actual,0,static_cast<uint32_t>(sample.size()),0,0,0,1};
    Paragraph paragraph(jalium_text_paragraph_create_with_wrapping(actual,reinterpret_cast<const uint16_t*>(sample.data()),sample.size(),&span,1,100000,48,0,0,1),jalium_text_paragraph_destroy);
    Check(paragraph != nullptr,"Variation paragraph unavailable"); JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(),0,&metrics)==JALIUM_OK && std::abs(metrics.line.width-width)<.003,"Variation paragraph advance differs");
    for (uint32_t i=0;i<=sample.size();++i) {
        CGFloat secondary=0, x=CTLineGetOffsetForStringIndex(line,i,&secondary); JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_text_position(actual,Managed(sample.c_str()),sample.size(),100000,1000,i,0,&hit)==JALIUM_OK && std::abs(hit.caretX-x)<.003,"Variation caret differs from CoreText");
    }
    CFRelease(line);
}
void ComparePixels(JaliumTextFormat* format, CTFontRef reference, int dpi, bool transformed)
{
    const std::u16string sample = u"ABMABM";
    NSString* text = [[NSString alloc] initWithCharacters:reinterpret_cast<const unichar*>(sample.data()) length:sample.size()];
    CGColorRef color = CGColorCreateGenericRGB(0, 0, 0, 1);
    NSAttributedString* attributed = [[NSAttributedString alloc] initWithString:text attributes:@{
        (__bridge id)kCTFontAttributeName:(__bridge id)reference,
        (__bridge id)kCTForegroundColorAttributeName:(__bridge id)color}];
    CTLineRef line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)attributed);
    JaliumTextSpan span{format, 0, static_cast<uint32_t>(sample.size()), 0, 0, 0, 1};
    auto paragraph = jalium_text_paragraph_create_with_wrapping(format, reinterpret_cast<const uint16_t*>(sample.data()),
        sample.size(), &span, 1, 100000, 48, 0, 0, 1);
    Check(paragraph != nullptr, "Variation pixel paragraph creation failed");
    auto* native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph));
    Check(native != nullptr, "Variation pixel paragraph provider unavailable");
    float matrix[] = {float(dpi), transformed ? .2f * dpi : 0, transformed ? -.15f * dpi : 0, float(dpi), 6, 9};
    std::vector<uint8_t> actual; uint32_t width{}, height{}; float left{}, top{};
    Check(native->RasterizeLine(0, 12, 12, 1, matrix, 720 * dpi, 192 * dpi, actual, width, height, left, top),
        "Variation pixel rasterization failed");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph, 0, &metrics) == JALIUM_OK, "Variation pixel baseline unavailable");
    std::vector<uint8_t> expected(width * height * 4);
    CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(expected.data(), width, height, 8, width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | static_cast<CGBitmapInfo>(kCGBitmapByteOrder32Little));
    Check(context != nullptr, "Variation reference pixel context unavailable");
    // Ideal metrics retain fractional glyph positions in both axes. Match that
    // contract in the independent CTLineDraw reference, not CG's quantized default.
    CGContextSetAllowsFontSubpixelPositioning(context, true);
    CGContextSetShouldSubpixelPositionFonts(context, true);
    CGContextSetAllowsFontSubpixelQuantization(context, false);
    CGContextSetShouldSubpixelQuantizeFonts(context, false);
    const float baselineX = 12, baselineY = 12 + metrics.baseline;
    CGAffineTransform transform = CGAffineTransformMake(matrix[0], -matrix[1], -matrix[2], matrix[3],
        matrix[0] * baselineX + matrix[2] * baselineY + matrix[4] - left,
        height - (matrix[1] * baselineX + matrix[3] * baselineY + matrix[5] - top));
    CGContextConcatCTM(context, transform); CGContextSetTextMatrix(context, CGAffineTransformIdentity);
    CGContextSetTextPosition(context, 0, 0); CTLineDraw(line, context);
    Check(actual == expected && std::count_if(actual.begin(), actual.end(), [](uint8_t value) { return value != 0; }) > 100,
        "Variation paragraph glyph pixels differ from independent CTLineDraw");
    CGContextRelease(context); CGColorSpaceRelease(space); CFRelease(line); CGColorRelease(color);
    jalium_text_paragraph_destroy(paragraph);
}

void Run(JaliumContext* context, NSData* data, const char16_t* alias) {
    auto resource = jalium_font_resource_register(Managed(alias),static_cast<const uint8_t*>(data.bytes),data.length);
    Check(resource != nullptr,"Variable font registration failed");
    CGDataProviderRef provider=CGDataProviderCreateWithCFData((__bridge CFDataRef)data); CGFontRef graphics=CGFontCreateWithDataProvider(provider);
    std::vector<std::vector<JaliumFontVariation>> settings={ {},{{0x55505744,75.5}},{{0x55505744,350}},{{0x55505744,-100}},{{0x55505744,600}},
        {{0x55505744,50},{0x55505744,250}},{{0x55505744,150},{0x46565454,10},{0x564d3242,700},{0x6f70737a,128}},
        {{0x55505744,175},{0x77776874,1e300}},{{0x55505744,350},{0x75707764,0}},{{0x6f70737a,6}},{{0x6f70737a,128}},
        {{0x77676874,900},{0x77647468,125}},{{0x77676874,150},{0x77647468,75}},{{0x77676874,-1e300},{0x77647468,1e300}} };
    auto actual=Font(context,alias);
    for (const auto& axes:settings) {
        Check(jalium_text_format_set_font_variations(actual.get(),axes.data(),axes.size())==JALIUM_OK,"Variation selection failed");
        CTFontRef expected=Reference(graphics,axes); Compare(actual.get(),expected);
        for(int dpi:{1,2})for(bool transformed:{false,true})ComparePixels(actual.get(),expected,dpi,transformed);
        CFRelease(expected);
    }
    JaliumFontFeature feature{0x6b65726e,0}; Check(jalium_text_format_set_font_features(actual.get(),&feature,1)==JALIUM_OK,"Feature composition failed");
    Check(jalium_text_format_set_font_features(actual.get(),nullptr,0)==JALIUM_OK,"Feature clear failed");
    Check(jalium_text_format_set_font_variations(actual.get(),nullptr,0)==JALIUM_OK,"Variation reset failed");
    CTFontRef reset=Reference(graphics,{}); Compare(actual.get(),reset);CFRelease(reset);
    JaliumTextMetrics before{},after{};jalium_text_format_measure_text(actual.get(),Managed(u"ABMABM"),6,100000,1000,&before);
    for (double value:{std::numeric_limits<double>::infinity(),-std::numeric_limits<double>::infinity(),std::numeric_limits<double>::quiet_NaN()}) {
        JaliumFontVariation bad{0x55505744,value};Check(jalium_text_format_set_font_variations(actual.get(),&bad,1)==JALIUM_ERROR_INVALID_ARGUMENT,"Nonfinite axis accepted");
    }
    JaliumFontVariation bad{0x01020304,100};Check(jalium_text_format_set_font_variations(actual.get(),&bad,1)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid tag accepted");
    Check(jalium_text_format_set_font_variations(actual.get(),nullptr,1)==JALIUM_ERROR_INVALID_ARGUMENT,"Null axis buffer accepted");
    jalium_text_format_measure_text(actual.get(),Managed(u"ABMABM"),6,100000,1000,&after);Check(before.width==after.width,"Invalid variation changed prior state");
    auto primary=Font(context,alias);auto fallback=Font(context,alias);
    const JaliumFontVariation originalAxes[]={{0x55505744,250},{0x77676874,750}};
    Check(jalium_text_format_set_font_variations(fallback.get(),originalAxes,2)==JALIUM_OK,"Fallback coordinates unavailable");
    Check(jalium_text_format_set_unicode_ranges(primary.get(),nullptr,0,1)==JALIUM_OK,"Primary range mask unavailable");
    JaliumTextFormat* fallbacks[]={fallback.get()};Check(jalium_text_format_set_font_fallbacks(primary.get(),fallbacks,1)==JALIUM_OK,"Variation cascade unavailable");
    const JaliumFontVariation overrideAxes[]={{0x55505744,175},{0x77676874,150}};
    Check(jalium_text_format_set_font_variations(primary.get(),overrideAxes,2)==JALIUM_OK,"Cascade variation replacement failed");
    CTFontRef replaced=Reference(graphics,{overrideAxes,overrideAxes+2});Compare(primary.get(),replaced);CFRelease(replaced);
    Check(jalium_text_format_set_font_variations(primary.get(),nullptr,0)==JALIUM_OK,"Cascade variation reset failed");
    CTFontRef restored=Reference(graphics,{originalAxes,originalAxes+2});Compare(primary.get(),restored);CFRelease(restored);
    CGFontRelease(graphics);CGDataProviderRelease(provider);jalium_font_resource_release(resource);
}
}
int main(int argc,char** argv) {
    @autoreleasepool { try {
        Check(argc==2,"Variable fixture path required");[NSApplication sharedApplication];
        NSData* data=[NSData dataWithContentsOfFile:[NSString stringWithUTF8String:argv[1]]];Check(data.length>12,"Variable fixture unavailable");
        for(JaliumBackend backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE}) {
            auto context=jalium_context_create(backend);Check(context!=nullptr,"Variable context unavailable");
            Run(context,data,u"VariationFixture");
            Run(context,[NSData dataWithContentsOfFile:@"/System/Library/Fonts/SFNS.ttf"],u"VariationWeightFixture");
            jalium_context_destroy(context);
        }
        std::printf("Variable font selection: %d checks; 224 independent raster comparisons\n",checks);return 0;
    } catch(const std::exception& error) { std::fprintf(stderr,"FAIL %s\n",error.what());return 1; } }
}
