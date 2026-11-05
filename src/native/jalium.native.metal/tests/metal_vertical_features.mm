#include "jalium_api.h"
#include "vertical_origin_reference.h"
#include "apple_text_paragraph.h"
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>
#import <QuartzCore/CAMetalLayer.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
int checks = 0;
bool metal = false;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
void Near(double actual, double expected, const char* message) {
    if (std::abs(actual - expected) > .01) {
        std::fprintf(stderr, "%s: %.6f != %.6f\n", message, actual, expected);
        throw std::runtime_error(message);
    }
    ++checks;
}
template<class T, void(*Destroy)(T*)> using TestOwner = std::unique_ptr<T, decltype(Destroy)>;
const wchar_t* Managed(const char16_t* value) { return reinterpret_cast<const wchar_t*>(value); }
constexpr int Width = 128, Height = 600;

struct ValidationSurface {
    NSView* view;
    TestOwner<JaliumRenderTarget, jalium_render_target_destroy> target{nullptr, jalium_render_target_destroy};
    explicit ValidationSurface(JaliumContext* context) {
        view = [[NSView alloc] initWithFrame:NSMakeRect(0, 0, Width, Height)];
        view.wantsLayer = YES;
        view.layer = metal ? [CAMetalLayer layer] : [CALayer layer];
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context, &descriptor, Width, Height));
        Check(target != nullptr, "Actual native surface target");
        jalium_render_target_set_dpi(target.get(), 96, 96);
    }
    std::vector<uint8_t> Draw(JaliumTextParagraph* paragraph, const float* transform) {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "Request actual backend readback");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "Begin actual backend frame");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
        jalium_push_transform(target.get(), transform);
        Check(jalium_render_target_draw_paragraph_line(target.get(), paragraph, 0, 0, 0, 1) == JALIUM_OK, "Public backend paragraph draw");
        jalium_pop_transform(target.get());
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "End actual backend frame");
        std::vector<uint8_t> pixels(Width * Height * 4); int32_t width = 0, height = 0;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), Width * 4, &width, &height) == JALIUM_OK &&
            width == Width && height == Height, "Read actual backend pixels");
        return pixels;
    }
};


void Save(const std::vector<uint8_t>& pixels, NSString* name) {
    const char* folder = std::getenv("JALIUM_VERTICAL_FEATURE_SCREENSHOT_DIR");
    if (!folder) return;
    NSString* root = [[NSString stringWithUTF8String:folder] stringByAppendingPathComponent:metal ? @"Metal" : @"Software"];
    [[NSFileManager defaultManager] createDirectoryAtPath:root withIntermediateDirectories:YES attributes:nil error:nil];
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(const_cast<uint8_t*>(pixels.data()), Width, Height, 8, Width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    auto image = CGBitmapContextCreateImage(context);
    auto destination = CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:[root stringByAppendingPathComponent:name]], CFSTR("public.png"), 1, nullptr);
    Check(destination, "Screenshot destination");
    CGImageDestinationAddImage(destination, image, nullptr);
    Check(CGImageDestinationFinalize(destination), "Write screenshot");
    CFRelease(destination); CGImageRelease(image); CGContextRelease(context); CGColorSpaceRelease(space);
}

struct Fixture { NSString* name; NSString* original; bool vertical, rotation, variable; };
CTFontRef ReferenceFont(NSString* path, double weight) {
    auto data = [NSData dataWithContentsOfFile:path];
    auto provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    auto graphics = provider ? CGFontCreateWithDataProvider(provider) : nullptr;
    Check(graphics != nullptr, "Independent original geometric font");
    auto font = CTFontCreateWithGraphicsFont(graphics, 32, nullptr, nullptr);
    CGFontRelease(graphics); CGDataProviderRelease(provider);
    if (!weight) return font;
    const int32_t tag = 0x77676874;
    auto key = CFNumberCreate(kCFAllocatorDefault, kCFNumberSInt32Type, &tag);
    auto value = CFNumberCreate(kCFAllocatorDefault, kCFNumberDoubleType, &weight);
    const void* keys[] = {key}; const void* values[] = {value};
    auto axes = CFDictionaryCreate(kCFAllocatorDefault, keys, values, 1, &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    const void* attribute = kCTFontVariationAttribute; const void* axisValues = axes;
    auto changes = CFDictionaryCreate(kCFAllocatorDefault,&attribute,&axisValues,1,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks);
    auto descriptor = CTFontDescriptorCreateWithAttributes(changes);
    auto selected = CTFontCreateCopyWithAttributes(font,0,nullptr,descriptor);
    CFRelease(changes); CFRelease(descriptor);
    CFRelease(font); CFRelease(axes); CFRelease(key); CFRelease(value);
    return selected;
}
CTLineRef ReferenceGlyphLine(CTFontRef font, int glyph, bool upright, bool variable) {
    // Literal glyph sources come from the ORIGINAL Jalium geometric font's
    // cmap. That font has no GSUB table, so no vertical feature can interfere.
    NSString* source = glyph == 6 ? @"水" : glyph == 4 ? @"x" : glyph == 5 ? @"0" : variable ? @"A" : @"H";
    NSMutableDictionary* attributes = [@{
        (__bridge NSString*)kCTFontAttributeName:(__bridge id)font,
        (__bridge NSString*)kCTVerticalFormsAttributeName:@(!upright),
        (__bridge NSString*)kCTBaselineClassAttributeName:(__bridge id)kCTBaselineClassIdeographicCentered,
        (__bridge NSString*)kCTBaselineReferenceInfoAttributeName:@{(__bridge NSString*)kCTBaselineClassIdeographicCentered:@0}
    } mutableCopy];
    double supplied=0;
    if(upright&&vertical_reference::DefaultOrigin(font,glyph+(variable&&glyph>=4?1:0),supplied)){
        [attributes removeObjectForKey:(__bridge id)kCTBaselineClassAttributeName];
        [attributes removeObjectForKey:(__bridge id)kCTBaselineReferenceInfoAttributeName];
    }
    auto text=[[NSAttributedString alloc]initWithString:source attributes:attributes];
    auto line = CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)text);
    auto run = static_cast<CTRunRef>(CFArrayGetValueAtIndex(CTLineGetGlyphRuns(line), 0)); CGGlyph actual = 0;
    CTRunGetGlyphs(run, CFRangeMake(0,1), &actual); Check(actual == glyph + (variable && glyph >= 4 ? 1 : 0), "Independent literal glyph identity");
    return line;
}
std::vector<uint8_t> ReferencePixels(CTFontRef font, int glyph, bool upright, int cells, double cell, float baseline,
    bool variable = false, const std::vector<int>& glyphs = {}) {
    std::vector<uint8_t> pixels(Width * Height * 4);
    auto space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context = CGBitmapContextCreate(pixels.data(), Width, Height, 8, Width * 4, space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst) | kCGBitmapByteOrder32Little);
    Check(context, "Independent reference bitmap");
    CGContextSetAllowsFontSubpixelPositioning(context, true); CGContextSetShouldSubpixelPositionFonts(context, true);
    CGContextSetAllowsFontSubpixelQuantization(context, false); CGContextSetShouldSubpixelQuantizeFonts(context, false);
    CGContextSetRGBFillColor(context, 0,0,0,1);
    for (int index = 0; index < cells; ++index) {
        auto line = ReferenceGlyphLine(font, glyphs.empty() ? glyph : glyphs.at(index), upright, variable);
        const double horizontal = CTLineGetTypographicBounds(line, nullptr, nullptr, nullptr);
        CGContextSaveGState(context);
        double origin=cell/2;
        vertical_reference::DefaultOrigin(font,vertical_reference::FirstGlyph(line),origin);
        if (upright) CGContextTranslateCTM(context, 80 - baseline - horizontal / 2, Height - 12 - index * cell - origin);
        else CGContextConcatCTM(context, CGAffineTransformMake(0,-1,1,0,80-baseline,Height-12));
        CGContextSetTextMatrix(context, CGAffineTransformIdentity); CGContextSetTextPosition(context, 0,0);
        if(upright)CTLineDraw(line,context);else vertical_reference::DrawVertical(line,context,vertical_reference::DefaultOrigin);
        CGContextRestoreGState(context);
        CFRelease(line);
    }
    CGContextRelease(context); CGColorSpaceRelease(space);
    return pixels;
}
void Verify(JaliumContext* context, ValidationSurface& surface, NSString* folder, const Fixture& fixture,
    bool upright, int mode, int weight, bool split) {
    auto path = [folder stringByAppendingPathComponent:[fixture.name stringByAppendingString:@".ttf"]];
    auto data = [NSData dataWithContentsOfFile:path]; Check(data.length > 12, "Vertical feature fixture");
    std::u16string family(fixture.name.length, 0);
    [fixture.name getCharacters:reinterpret_cast<unichar*>(family.data()) range:NSMakeRange(0,family.size())];
    TestOwner<JaliumFontResource,jalium_font_resource_release> resource(jalium_font_resource_register(Managed(family.c_str()),
        static_cast<const uint8_t*>(data.bytes), data.length),jalium_font_resource_release);
    Check(resource != nullptr,"Register geometric feature font");
    TestOwner<JaliumTextFormat,jalium_text_format_destroy> font(jalium_text_format_create(context,Managed(family.c_str()),32,400,0),jalium_text_format_destroy);
    Check(font != nullptr,"Feature format");
    JaliumFontVariation axis{0x77676874, double(weight)};
    if (fixture.variable) Check(jalium_text_format_set_font_optical_variations(font.get(),&axis,1,0)==JALIUM_OK,"Explicit wght variation");
    std::vector<JaliumFontFeature> features;
    if (mode==1 || mode==3 || mode==4) features.push_back({0x76657274,0});
    if (mode==2 || mode==6) features.push_back({0x76657274,1});
    if (mode==2 || mode==4) features.push_back({0x76727432,0});
    if (mode==3 || mode==6 || mode==7) features.push_back({0x76727432,1});
    if (mode==5) features.push_back({0x73616c74,1});
    Check(jalium_text_format_set_font_features(font.get(),features.data(),features.size())==JALIUM_OK,"Author feature settings");
    const std::u16string source = upright ? u"A—(" : u"中";
    std::vector<JaliumTextSpan> spans;
    if (split) for(uint32_t at=0;at<source.size();++at) spans.push_back({font.get(),at,1,0,0,0,1});
    else spans.push_back({font.get(),0,static_cast<uint32_t>(source.size()),0,0,0,1});
    std::vector<JaliumTextAdvanceUnit> units;
    for(uint32_t at=0;at<source.size();++at)units.push_back({at,1,0,0,0,0});
    TestOwner<JaliumTextParagraph,jalium_text_paragraph_destroy> paragraph(
        upright ? (split ? jalium_text_paragraph_create_upright_advanced(font.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),spans.data(),spans.size(),units.data(),units.size(),10000,40,0,1)
                         : jalium_text_paragraph_create_upright(font.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),spans.data(),spans.size(),10000,40,0,0,1))
                : (split ? jalium_text_paragraph_create_vertical_advanced(font.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),spans.data(),spans.size(),units.data(),units.size(),10000,40,0,1)
                         : jalium_text_paragraph_create_vertical(font.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),spans.data(),spans.size(),10000,40,0,0,1)),jalium_text_paragraph_destroy);
    Check(paragraph != nullptr,"Public feature paragraph");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(),0,&metrics)==JALIUM_OK,"Feature line metrics");
    int glyph = 2;
    if (mode==5) glyph=4;
    // The high-weight conditional vert uses lookup 3, after vrt2 lookup 2.
    // When both are enabled, vrt2 has already replaced its input glyph.
    else if (fixture.variable && weight==700 && (mode==6 || mode==7)) glyph=4;
    else if (mode!=1 && mode!=3 && mode!=4 && fixture.vertical) glyph=fixture.variable && weight==700 ? 5 : 6;
    else if ((mode==3 || mode==6 || mode==7) && fixture.rotation) glyph=4;
    const double cell = glyph==6 && fixture.variable ? (weight==550?41.6:38.4) : 38.4;
    if (upright) Near(metrics.line.width,cell*source.size(),"Vertical advance follows substituted glyph and VVAR");
    auto native = dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph.get()));
    Check(native,"Apple feature paragraph"); const float transform[]={0,1,-1,0,80,12};
    std::vector<uint8_t> cropped;uint32_t width=0,height=0;float x=0,y=0;
    Check(native->RasterizeLine(0,0,0,1,transform,Width,Height,cropped,width,height,x,y),"Feature rasterization");
    std::vector<uint8_t> actual(Width*Height*4);
    for(uint32_t row=0;row<height;++row)std::copy_n(cropped.data()+row*width*4,width*4,actual.data()+((size_t(y+row)*Width)+size_t(x))*4);
    auto reference = ReferenceFont([[folder stringByDeletingLastPathComponent]stringByAppendingPathComponent:[@"FontSizeAdjust/" stringByAppendingString:[fixture.original stringByAppendingString:@".ttf"]]],fixture.variable?weight:0);
    auto expected=ReferencePixels(reference,glyph,upright,source.size(),cell,metrics.baseline,fixture.variable);CFRelease(reference);
    NSString* prefix=[NSString stringWithFormat:@"%@-%d-%d-%d-%d",fixture.name,upright,mode,weight,split];
    Save(actual,[prefix stringByAppendingString:@"-actual.png"]);Save(expected,[prefix stringByAppendingString:@"-reference.png"]);
    size_t different=0;for(size_t at=0;at<actual.size();++at)different+=actual[at]!=expected[at];
    if(different)std::fprintf(stderr,"%s: %zu different native bytes; expected glyph %d\n",prefix.UTF8String,different,glyph);
    Check(different==0,"Native pixels match literal glyphs from original font without GSUB");
    auto backend=surface.Draw(paragraph.get(),transform);auto backendExpected=expected;
    if(!metal)for(size_t at=0;at<backendExpected.size();at+=4){unsigned alpha=backendExpected[at+3];if(alpha)for(int c=0;c<3;++c)backendExpected[at+c]=std::min(255u,(unsigned(backendExpected[at+c])*255+alpha/2)/alpha);}
    different=0;for(size_t at=0;at<actual.size();++at)different+=backend[at]!=backendExpected[at];
    if(different)std::fprintf(stderr,"%s: %zu different backend bytes\n",prefix.UTF8String,different);
    Check(different==0,"Public backend feature frame matches independent reference");
}
void VerifyIndependentOverrides(JaliumContext* context, ValidationSurface& surface, NSString* folder, int cascade) {
    auto data = [NSData dataWithContentsOfFile:[folder stringByAppendingPathComponent:@"VerticalBoth.ttf"]];
    TestOwner<JaliumFontResource,jalium_font_resource_release> resource(jalium_font_resource_register(
        Managed(u"IndependentVertical"), static_cast<const uint8_t*>(data.bytes), data.length),jalium_font_resource_release);
    Check(resource != nullptr,"Register independent override font");
    auto make = [&]() { return TestOwner<JaliumTextFormat,jalium_text_format_destroy>(
        jalium_text_format_create(context,Managed(u"IndependentVertical"),32,400,0),jalium_text_format_destroy); };
    auto root=make(), first=make(), second=make();
    Check(root && first && second,"Independent override formats");
    const JaliumFontFeature on[]={{0x76657274,1},{0x76727432,0}},off[]={{0x76657274,0},{0x76727432,0}};
    Check(jalium_text_format_set_font_features(first.get(),on,2)==JALIUM_OK &&
        jalium_text_format_set_font_features(second.get(),off,2)==JALIUM_OK,"Opposite raw settings with collapsed CoreText descriptors");
    const std::u16string source=u"A(";
    std::vector<JaliumTextSpan> spans;
    if (cascade) {
        const JaliumUnicodeRange a{65,65}, bracket{40,40};
        Check(jalium_text_format_set_unicode_ranges(root.get(),nullptr,0,1)==JALIUM_OK &&
            jalium_text_format_set_unicode_ranges(first.get(),&a,1,1)==JALIUM_OK &&
            jalium_text_format_set_unicode_ranges(second.get(),&bracket,1,1)==JALIUM_OK,"Per-face coverage masks");
        JaliumTextFormat* children[]={first.get(),second.get()};
        if(cascade==2) Check(jalium_text_format_set_font_fallbacks(first.get(),children+1,1)==JALIUM_OK,"Nested fallback snapshot");
        Check(jalium_text_format_set_font_fallbacks(root.get(),children,cascade==2?1:2)==JALIUM_OK,"Per-face feature snapshot");
        spans.push_back({root.get(),0,2,0,0,0,1});
    } else {
        spans.push_back({first.get(),0,1,0,0,0,1});
        spans.push_back({second.get(),1,1,0,0,0,1});
    }
    TestOwner<JaliumTextParagraph,jalium_text_paragraph_destroy> paragraph(jalium_text_paragraph_create_upright(
        root.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),spans.data(),spans.size(),
        10000,40,0,0,1),jalium_text_paragraph_destroy);
    Check(paragraph!=nullptr,"Independent override paragraph");
    JaliumParagraphLineMetrics metrics{};
    Check(jalium_text_paragraph_get_line(paragraph.get(),0,&metrics)==JALIUM_OK,"Independent override metrics");
    Near(metrics.line.width,76.8,"Independent override vertical cells");
    auto reference=ReferenceFont([[folder stringByDeletingLastPathComponent]stringByAppendingPathComponent:@"FontSizeAdjust/Primary.ttf"],0);
    auto expected=ReferencePixels(reference,6,true,2,38.4,metrics.baseline,false,{6,2});CFRelease(reference);
    auto backendExpected=expected;
    if(!metal)for(size_t at=0;at<backendExpected.size();at+=4){unsigned alpha=backendExpected[at+3];if(alpha)for(int c=0;c<3;++c)backendExpected[at+c]=std::min(255u,(unsigned(backendExpected[at+c])*255+alpha/2)/alpha);}
    const float transform[]={0,1,-1,0,80,12};
    for(bool mutate:{false,true}) {
        if(mutate) Check(jalium_text_format_set_font_features(first.get(),off,2)==JALIUM_OK &&
            jalium_text_format_set_font_features(second.get(),on,2)==JALIUM_OK &&
            jalium_text_format_set_font_features(root.get(),off,2)==JALIUM_OK,"Mutate source formats after paragraph snapshot");
        auto actual=surface.Draw(paragraph.get(),transform);
        NSString* name=[NSString stringWithFormat:@"IndependentOverrides-%d-%d",cascade,mutate];
        Save(actual,[name stringByAppendingString:@"-actual.png"]);
        Save(expected,[name stringByAppendingString:@"-reference.png"]);
        Check(actual==backendExpected,"Adjacent and fallback raw features survive equal descriptors and format mutations");
    }
}
}
int main(int argc,const char**argv){@autoreleasepool{
 metal=argc>1&&std::string(argv[1])=="--metal";
 try{
  Check(argc>2,"Fixture directory argument");[NSApplication sharedApplication];
  TestOwner<JaliumContext,jalium_context_destroy> context(jalium_context_create(metal?JALIUM_BACKEND_METAL:JALIUM_BACKEND_SOFTWARE),jalium_context_destroy);Check(context!=nullptr,"Native feature context");ValidationSurface surface(context.get());
  const Fixture fixtures[]={{@"VerticalBoth",@"Primary",true,true,false},{@"VerticalOnly",@"Primary",true,false,false},{@"RotationOnly",@"Primary",false,true,false},{@"VerticalVariable",@"VerticalVariable",true,true,true}};
  int pairs=0;
  for(const auto& fixture:fixtures)for(bool upright:{false,true})for(int mode=0;mode<8;++mode){
   if(mode==5&&!upright)continue;
   for(int weight:fixture.variable?std::vector<int>{400,550,700}:std::vector<int>{400})for(bool split:{false,true}){
    Verify(context.get(),surface,[NSString stringWithUTF8String:argv[2]],fixture,upright,mode,weight,split);++pairs;
   }
  }
  for(int cascade=0;cascade<3;++cascade){VerifyIndependentOverrides(context.get(),surface,[NSString stringWithUTF8String:argv[2]],cascade);pairs+=2;}
  std::printf("PASS vertical features: %d checks; %d independent literal-glyph/native/public-backend pixel pairs\n",checks,pairs);return 0;
 }catch(const std::exception&e){std::fprintf(stderr,"FAIL vertical features: %s\n",e.what());return 1;}
}}
