#include "jalium_platform.h"
#include "jalium_api.h"
#import <AppKit/AppKit.h>
#import <CoreText/CoreText.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>

namespace {
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
struct Fixture { NSString* payload = nil; };
int32_t Query(JaliumAccessibilityRequest* request, void* context)
{
    if (request->nodeId != 1 && request->nodeId != 2) return 0;
    auto& fixture = *static_cast<Fixture*>(context);
    if (request->operation == JALIUM_AX_INFO) {
        request->role = request->nodeId == 1 ? 32 : 4; request->parentId = request->nodeId == 1 ? 0 : 1;
        request->childCount = request->nodeId == 1 ? 1 : 0;
        request->flags = JALIUM_AX_ENABLED | (request->nodeId == 2 ? JALIUM_AX_TEXT | JALIUM_AX_STYLED_TEXT : 0);
        request->textCount = 4; request->width = 200; request->height = 40; return 1;
    }
    if (request->operation == JALIUM_AX_ATTACHED) return 1;
    if (request->operation == JALIUM_AX_CHILD && request->nodeId == 1 && request->index == 0) { request->resultId = 2; return 1; }
    if (request->operation == JALIUM_AX_TEXT_STYLES) {
        request->textCount = fixture.payload.length;
        if (!request->text) return 1;
        if (request->textCapacity < request->textCount) return 0;
        [fixture.payload getCharacters:request->text range:NSMakeRange(0, request->textCount)]; return 1;
    }
    return 0;
}
NSString* Payload(NSString* family, int weight, int italic, id width = nil)
{
    const unichar text[] = {'M','i','i','i'};
    NSString* text16 = [[NSData dataWithBytes:text length:sizeof(text)] base64EncodedStringWithOptions:0];
    NSMutableDictionary* style = [@{@"start":@0,@"length":@4,@"family":family,@"size":@21,
        @"weight":@(weight),@"italic":@(italic),@"underline":@0,@"strike":@0,@"alignment":@0,@"direction":@0,@"language":NSNull.null} mutableCopy];
    if (width) style[@"width"] = width;
    NSData* data = [NSJSONSerialization dataWithJSONObject:@{@"version":@1,@"text16":text16,@"runs":@[style]} options:0 error:nil];
    return [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
}

NSFont* SystemReference(int weight, int style, double width)
{
    // Requests cross the renderer's float ABI; preserve that precision when
    // constructing an independent AppKit reference and its variable PS name.
    const CGFloat semibold = static_cast<float>(NSFontWeightSemibold), bold = static_cast<float>(NSFontWeightBold);
    NSFont* base = [NSFont systemFontOfSize:21 weight:weight == 700 ? bold : weight == 650 ? (semibold + bold) / 2 : NSFontWeightRegular];
    if (width == 100) return style ? [NSFontManager.sharedFontManager convertFont:base toHaveTrait:NSItalicFontMask] : base;
    NSMutableDictionary* values = [CFBridgingRelease(CTFontCopyVariation((__bridge CTFontRef)base)) mutableCopy] ?: [NSMutableDictionary new];
    values[@(0x77647468)] = @(static_cast<CGFloat>(static_cast<float>(width)));
    CTFontDescriptorRef descriptor = CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge id)kCTFontVariationAttribute:values});
    NSFont* result = CFBridgingRelease(CTFontCreateCopyWithAttributes((__bridge CTFontRef)base,21,nullptr,descriptor));
    CFRelease(descriptor); return result;
}

NSAttributedString* ReferenceExport(NSFont* font, double shear)
{
    NSMutableDictionary* attributes = [@{NSFontAttributeName:font} mutableCopy];
    if (shear) attributes[NSObliquenessAttributeName] = @(shear);
    NSAttributedString* source = [[NSAttributedString alloc] initWithString:@"Miii" attributes:attributes];
    // Compare the same native RTF quantization and font-name reconstruction.
    NSData* rtf = [source RTFFromRange:NSMakeRange(0,source.length) documentAttributes:@{}];
    return [[NSAttributedString alloc] initWithRTF:rtf documentAttributes:nil];
}

std::vector<uint8_t> Pixels(NSAttributedString* text, int scale)
{
    const size_t width = 160 * scale, height = 64 * scale;
    std::vector<uint8_t> pixels(width * height * 4,0);
    CGColorSpaceRef colorSpace = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(pixels.data(),width,height,8,width * 4,colorSpace,
        kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big);
    CGColorSpaceRelease(colorSpace); Check(context != nullptr,"RTF pixel context");
    CGContextScaleCTM(context,scale,scale);
    [NSGraphicsContext saveGraphicsState];
    NSGraphicsContext.currentContext = [NSGraphicsContext graphicsContextWithCGContext:context flipped:NO];
    [text drawAtPoint:NSMakePoint(10,20)];
    [NSGraphicsContext restoreGraphicsState]; CGContextRelease(context); return pixels;
}

void VerifyTransform(NSAccessibilityElement* element, NSFont* expected, double shear, bool italic)
{
    NSAttributedString* axText = [element accessibilityAttributedStringForRange:NSMakeRange(0,4)];
    Check(axText != nil,"transform AX text missing");
    NSDictionary* ax = [axText attributesAtIndex:0 effectiveRange:nil];
    NSData* rtf = [element accessibilityRTFForRange:NSMakeRange(0,4)];
    NSAttributedString* decoded = rtf ? [[NSAttributedString alloc] initWithRTF:rtf documentAttributes:nil] : nil;
    Check([decoded.string isEqual:@"Miii"],"transform RTF changes text");
    NSDictionary* actual = [decoded attributesAtIndex:0 effectiveRange:nil];
    NSFont* font = actual[NSFontAttributeName];
    NSAttributedString* reference = ReferenceExport(expected,shear);
    NSFont* referenceFont = [reference attributesAtIndex:0 effectiveRange:nil][NSFontAttributeName];
    // AppKit canonicalizes UI font aliases to the variable PostScript name.
    Check([font.fontName isEqual:referenceFont.fontName] && font.pointSize == referenceFont.pointSize,"transform RTF changes font instance");
    Check([ax[NSAccessibilityFontTextAttribute][NSAccessibilityFontNameKey] isEqual:expected.fontName],"transform AX changes font instance");
    const double exportedShear = [actual[NSObliquenessAttributeName] doubleValue];
    bool correctItalic = true, actualItalic = false;
    if (@available(macOS 26.0,*)) {
        actualItalic = [ax[NSAccessibilityFontItalicAttribute] boolValue];
        correctItalic = actualItalic == italic;
    }
    if (!correctItalic || std::abs(exportedShear - shear) >= .0005)
        std::fprintf(stderr,"Transform mismatch: font=%s AXItalic=%d expected=%d RTFShear=%.6f expected=%.6f\n",
            font.fontName.UTF8String,actualItalic,italic,exportedShear,shear);
    Check(correctItalic,"AX does not report the drawn italic shape");
    Check(std::abs(exportedShear - shear) < .0005,"RTF loses synthesized font shear");
    CGAffineTransform matrix = CTFontGetMatrix((__bridge CTFontRef)font);
    Check(CGAffineTransformIsIdentity(matrix),"RTF doubles the font matrix and obliqueness");
    Check(!actual[NSExpansionAttributeName] || [actual[NSExpansionAttributeName] doubleValue] == 0,"variable width is replaced by glyph expansion");
    for (int scale : {1,2}) Check(Pixels(decoded,scale) == Pixels(reference,scale),"RTF pixels differ from native obliqueness/variation reference");
}
}

int main()
{
    @autoreleasepool {
        Check(jalium_platform_init() == JALIUM_OK, "platform init");
        [NSApp setActivationPolicy:NSApplicationActivationPolicyProhibited];
        JaliumWindowParams parameters{}; parameters.title = reinterpret_cast<const uint16_t*>(u"Window AX font matching");
        parameters.x = parameters.y = JALIUM_DEFAULT_POS; parameters.width = 420; parameters.height = 260;
        auto window = std::unique_ptr<JaliumPlatformWindow, decltype(&jalium_window_destroy)>(jalium_window_create(&parameters),jalium_window_destroy);
        Check(window != nullptr, "native Window fixture"); Fixture fixture;
        jalium_apple_window_set_accessibility(window.get(), Query, &fixture); jalium_apple_window_show(window.get(),0);
        NSView* view = (__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window.get()));
        NSAccessibilityElement* root = view.accessibilityChildren.firstObject;
        NSAccessibilityElement* element = root.accessibilityChildren.firstObject; Check(element != nil,"styled editor fixture");
        NSFont* privateReference = [NSFont fontWithName:@"Andale Mono" size:21];
        NSURL* url = CFBridgingRelease(CTFontCopyAttribute((__bridge CTFontRef)privateReference,kCTFontURLAttribute));
        NSData* bytes = [NSData dataWithContentsOfURL:url];
        auto privateFont = std::unique_ptr<JaliumFontResource, decltype(&jalium_font_resource_release)>(
            jalium_font_resource_register(reinterpret_cast<const wchar_t*>(u"WindowAXPrivate121"),static_cast<const uint8_t*>(bytes.bytes),bytes.length),jalium_font_resource_release);
        Check(privateFont != nullptr,"private font fixture");
        int failures = 0, cases = 0;
        auto run = [&](const char* name, auto test) { ++cases; try { test(); std::printf("PASS: %s\n",name); }
            catch (const std::exception& e) { ++failures; std::fprintf(stderr,"FAIL: %s: %s\n",name,e.what()); } };
        struct FontCase { const char* name; NSString* family; int weight; int italic; NSNumber* width; NSString* expected; };
        for (const auto& test : {
            FontCase{"private alias",@"WindowAXPrivate121",400,0,nil,privateReference.fontName},
            FontCase{"installed condensed bold",@"Helvetica Neue",700,0,@75,@"HelveticaNeue-CondensedBold"},
            FontCase{"installed condensed black",@"Helvetica Neue",900,0,@87.5,@"HelveticaNeue-CondensedBlack"},
            FontCase{"explicit face",@"Arial-BoldItalicMT",400,0,nil,@"Arial-BoldItalicMT"},
            FontCase{"generic monospace",@"monospace",400,0,@75,([NSFont fontWithName:@"Menlo" size:21]).fontName}}) {
            run(test.name,[&] {
                fixture.payload = Payload(test.family,test.weight,test.italic,test.width);
                NSAttributedString* text = [element accessibilityAttributedStringForRange:NSMakeRange(0,4)];
                NSDictionary* ax = [text attributesAtIndex:0 effectiveRange:nil][NSAccessibilityFontTextAttribute];
                Check([ax[NSAccessibilityFontNameKey] isEqual:test.expected],"AX identifies a substituted face");
                NSData* rtf = [element accessibilityRTFForRange:NSMakeRange(0,4)];
                NSAttributedString* decoded = [[NSAttributedString alloc] initWithRTF:rtf documentAttributes:nil];
                NSFont* font = [decoded attributesAtIndex:0 effectiveRange:nil][NSFontAttributeName];
                Check([font.fontName isEqual:test.expected],"RTF identifies a substituted face");
                Check([decoded.string isEqual:@"Miii"] && font.pointSize == 21,"font export changes text or point size");
            });
        }
        for (id invalid in @[@(-1),@"75",NSNull.null,@[]]) run("invalid width",[&] {
            fixture.payload = Payload(@"Arial",400,0,invalid);
            Check([element accessibilityAttributedStringForRange:NSMakeRange(0,4)] == nil &&
                [element accessibilityRTFForRange:NSMakeRange(0,4)] == nil,"malformed width is accepted");
        });
        for (int weight : {400,650,700}) for (double width : {75.0,83.2,92.8,100.0,112.5}) for (int style : {0,1,2}) {
            char name[128]; std::snprintf(name,sizeof(name),"system RTF transform weight=%d width=%.1f style=%d",weight,width,style);
            run(name,[&] {
                fixture.payload = Payload(@"SF Pro",weight,style,@(width));
                VerifyTransform(element,SystemReference(weight,style,width),style && width != 100 ? std::tan(12.0*M_PI/180) : 0,style != 0);
            });
        }
        for (double width : {75.0,100.0,112.5}) for (int style : {0,1,2}) {
            char name[128]; std::snprintf(name,sizeof(name),"private RTF transform width=%.1f style=%d",width,style);
            run(name,[&] {
                fixture.payload = Payload(@"WindowAXPrivate121",400,style,@(width));
                VerifyTransform(element,privateReference,style ? std::tan(12.0*M_PI/180) : 0,style != 0);
            });
        }
        for (int style : {1,2}) run("legacy private font has no synthesized matrix",[&] {
            fixture.payload = Payload(@"WindowAXPrivate121",400,style);
            VerifyTransform(element,privateReference,0,false);
        });
        std::printf("Window AX font checks: %d/%d passed\n",cases-failures,cases);return failures ? 1 : 0;
    }
}
