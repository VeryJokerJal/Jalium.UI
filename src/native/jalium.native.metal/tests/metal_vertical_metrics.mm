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
    const char* folder = std::getenv("JALIUM_VERTICAL_METRIC_SCREENSHOT_DIR");
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

CTFontRef ReferenceFont(NSString* path, double weight, double em=32) {
    auto data = [NSData dataWithContentsOfFile:path];
    auto provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    auto graphics = provider ? CGFontCreateWithDataProvider(provider) : nullptr;
    Check(graphics != nullptr, "Independent original geometric font");
    auto font = CTFontCreateWithGraphicsFont(graphics, em, nullptr, nullptr);
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
struct Cell { NSString* text; double advance; };
struct Sample { NSString* name; std::u16string source; std::vector<Cell> cells; bool variable=false; int weight=400; bool mark=false; bool features=true; uint32_t optical=0; double spacing=0; bool cascade=false; double extraWidth=0; bool uprightReference=false; double adjustedEm=0; };
// Literal programs from FontGvarMetrics/build-fixtures.py. No production
// gvar parser or font-origin helper participates in this expected value.
bool ReferenceOrigin(const Sample& sample,CTFontRef font,CGGlyph glyph,double& value){
 if(!vertical_reference::DefaultOrigin(font,glyph,value))return false;
 if(![sample.name hasPrefix:@"Phantom"]||(glyph!=2&&glyph!=7))return true;
 double c=(sample.weight-400)/300.0;
 if([sample.name isEqualToString:@"PhantomAvar"]||[sample.name isEqualToString:@"PhantomAvar2"]){if(c>0)c=c<=.5?c*.5:.25+(c-.5)*1.5;if([sample.name isEqualToString:@"PhantomAvar2"]&&c>0)c=std::min(1.0,c*1.5);}
 double top=100*c;
 if([sample.name isEqualToString:@"PhantomTopOnly"])top=200*c;
 if([sample.name isEqualToString:@"PhantomBottomOnly"]||[sample.name isEqualToString:@"PhantomUnreferenced"])top=0;
 if([sample.name isEqualToString:@"PhantomIntermediate"]){double m=std::abs(c);top=m<=.25||m>=.75?0:(c<0?-1:1)*100*(m<=.5?(m-.25)/.25:(.75-m)/.25);}
 if([sample.name isEqualToString:@"PhantomMultiAxis"]){double other=(sample.extraWidth-100)/50;top=c*other>0?(c<0?-1:1)*100*std::abs(c*other):0;}
 if([sample.name isEqualToString:@"PhantomWithVvar"])top=glyph==2?0:100*std::max(c,0.0);
 if(glyph==7&&[sample.name isEqualToString:@"PhantomComposite"])top=150*c;
 if(glyph==7&&[sample.name isEqualToString:@"PhantomLastMetrics"])top=-250;
 value+=top*CTFontGetSize(font)/1000;return true;
}
std::vector<uint8_t> Reference(CTFontRef font,const std::vector<Cell>& cells,float baseline,bool mark,bool upright,const Sample& sample){
 std::vector<uint8_t> pixels(Width*Height*4);auto space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);auto context=CGBitmapContextCreate(pixels.data(),Width,Height,8,Width*4,space,static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|kCGBitmapByteOrder32Little);Check(context,"Reference context");CGContextSetAllowsFontSubpixelPositioning(context,true);CGContextSetShouldSubpixelPositionFonts(context,true);CGContextSetAllowsFontSubpixelQuantization(context,false);CGContextSetShouldSubpixelQuantizeFonts(context,false);CGContextSetRGBFillColor(context,0,0,0,1);double offset=0;
 for(auto cell:cells){
  auto selected=font;
  if(mark){auto d=CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge NSString*)kCTFontFixedAdvanceAttribute:@38.4});selected=CTFontCreateCopyWithAttributes(font,0,nullptr,d);CFRelease(d);}
  NSMutableDictionary* attributes=[@{(__bridge NSString*)kCTFontAttributeName:(__bridge id)selected,(__bridge NSString*)kCTVerticalFormsAttributeName:@(!upright),(__bridge NSString*)kCTBaselineClassAttributeName:(__bridge id)kCTBaselineClassIdeographicCentered,(__bridge NSString*)kCTBaselineReferenceInfoAttributeName:@{(__bridge NSString*)kCTBaselineClassIdeographicCentered:@0}} mutableCopy];
  double supplied=0;
  if(upright&&ReferenceOrigin(sample,selected,0,supplied)){
   [attributes removeObjectForKey:(__bridge id)kCTBaselineClassAttributeName];[attributes removeObjectForKey:(__bridge id)kCTBaselineReferenceInfoAttributeName];
  }
  auto text=[[NSAttributedString alloc]initWithString:cell.text attributes:attributes];
  auto line=CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)text);double origin=cell.advance/2;
  ReferenceOrigin(sample,selected,vertical_reference::FirstGlyph(line),origin);
  CGContextSaveGState(context);if(upright)CGContextTranslateCTM(context,80-baseline-CTLineGetTypographicBounds(line,nullptr,nullptr,nullptr)/2,Height-12-offset-origin);else CGContextConcatCTM(context,CGAffineTransformMake(0,-1,1,0,80-baseline,Height-12));CGContextSetTextMatrix(context,CGAffineTransformIdentity);CGContextSetTextPosition(context,upright?0:offset,0);
  if(upright)CTLineDraw(line,context);else vertical_reference::DrawVertical(line,context,[&](CTFontRef f,CGGlyph glyph,double& expected){return ReferenceOrigin(sample,f,glyph,expected);},mark);
  CGContextRestoreGState(context);CFRelease(line);if(mark)CFRelease(selected);offset+=cell.advance;
 }
 CGContextRelease(context);CGColorSpaceRelease(space);return pixels;
}
void Verify(JaliumContext* context,ValidationSurface& surface,NSString* folder,const Sample& sample,bool upright,bool split){
 auto file=[folder stringByAppendingPathComponent:[sample.name stringByAppendingString:@".ttf"]];auto data=[NSData dataWithContentsOfFile:file];Check(data.length>12,"Metric fixture data");std::u16string family(sample.name.length,0);[sample.name getCharacters:reinterpret_cast<unichar*>(family.data()) range:NSMakeRange(0,family.size())];
 TestOwner<JaliumFontResource,jalium_font_resource_release> resource(jalium_font_resource_register(Managed(family.c_str()),static_cast<const uint8_t*>(data.bytes),data.length),jalium_font_resource_release);Check(resource!=nullptr,"Register metric font");TestOwner<JaliumTextFormat,jalium_text_format_destroy> font(jalium_text_format_create(context,Managed(family.c_str()),32,400,0),jalium_text_format_destroy);Check(font!=nullptr,"Metric format");
 if(sample.cascade){TestOwner<JaliumTextFormat,jalium_text_format_destroy> parent(jalium_text_format_create(context,Managed(u"Arial"),32,400,0),jalium_text_format_destroy);Check(parent!=nullptr,"Cascade primary format");JaliumUnicodeRange range{0,127};Check(jalium_text_format_set_unicode_ranges(parent.get(),&range,1,1)==JALIUM_OK,"Cascade primary Unicode coverage");auto child=font.get();Check(jalium_text_format_set_font_fallbacks(parent.get(),&child,1)==JALIUM_OK,"Private variable fallback");font=std::move(parent);}
 JaliumFontVariation axes[]={{0x77676874,double(sample.weight)},{0x77647468,sample.extraWidth}};if(sample.variable)Check(jalium_text_format_set_font_optical_variations(font.get(),axes,sample.extraWidth?2:1,sample.optical)==JALIUM_OK,"Metric variation request");
 const JaliumFontFeature features[]={{0x6c696761,1},{0x63616c74,1},{0x766b726e,1}};if(sample.features)Check(jalium_text_format_set_font_features(font.get(),features,3)==JALIUM_OK,"Context, ligature and vertical kerning features");
 if(sample.adjustedEm){float used=0;Check(jalium_text_format_set_font_size_adjust(font.get(),5,1.2,sample.optical,axes,sample.extraWidth?2:1,&used)==JALIUM_OK,"Variable vertical size adjustment");if(!sample.cascade)Near(used,sample.adjustedEm,"Size adjustment uses the actual gvar or VVAR advance");}
 std::vector<JaliumTextSpan> spans;if(split)for(uint32_t at=0;at<sample.source.size();++at)spans.push_back({font.get(),at,1,0,0,0,1});else spans.push_back({font.get(),0,static_cast<uint32_t>(sample.source.size()),0,0,0,1});std::vector<JaliumTextAdvanceUnit> units;for(uint32_t at=0;at<sample.source.size();){uint32_t length=sample.mark?2:1;units.push_back({at,length,float(sample.spacing),0,0,0});at+=length;}
 TestOwner<JaliumTextParagraph,jalium_text_paragraph_destroy> paragraph(upright?((split||sample.spacing!=0)?jalium_text_paragraph_create_upright_advanced(font.get(),reinterpret_cast<const uint16_t*>(sample.source.data()),sample.source.size(),spans.data(),spans.size(),units.data(),units.size(),10000,40,0,split?1:0):jalium_text_paragraph_create_upright(font.get(),reinterpret_cast<const uint16_t*>(sample.source.data()),sample.source.size(),spans.data(),spans.size(),10000,40,0,0,1)):((split||sample.spacing!=0)?jalium_text_paragraph_create_vertical_advanced(font.get(),reinterpret_cast<const uint16_t*>(sample.source.data()),sample.source.size(),spans.data(),spans.size(),units.data(),units.size(),10000,40,0,split?1:0):jalium_text_paragraph_create_vertical(font.get(),reinterpret_cast<const uint16_t*>(sample.source.data()),sample.source.size(),spans.data(),spans.size(),10000,40,0,0,1)),jalium_text_paragraph_destroy);Check(paragraph!=nullptr,"Metric paragraph");JaliumParagraphLineMetrics line{};Check(jalium_text_paragraph_get_line(paragraph.get(),0,&line)==JALIUM_OK,"Metric line");double advance=0;for(auto cell:sample.cells)advance+=cell.advance;
 NSString* prefix=[NSString stringWithFormat:@"%@-%d-%d-%d-%d-%u-%g-%d-%g-%d-%d",sample.name,upright,split,sample.weight,sample.features,sample.optical,sample.spacing,sample.cascade,sample.extraWidth,sample.uprightReference,sample.adjustedEm!=0];std::fprintf(stdout,"CASE %s width%g expected%g\n",prefix.UTF8String,line.line.width,advance);std::fflush(stdout);Near(line.line.width,advance,"Vertical line uses actual substituted glyph metrics and VVAR");JaliumParagraphCaret caret{};Check(jalium_text_paragraph_get_caret(paragraph.get(),0,sample.source.size(),1,&caret)==JALIUM_OK,"Metric terminal caret");Near(caret.x,advance,"Terminal caret includes entire metric cell");
 uint32_t count=0;Check(jalium_text_paragraph_get_selection(paragraph.get(),0,0,sample.source.size(),nullptr,0,&count)==JALIUM_OK&&(count>0||advance==0),"Metric source selection count");std::vector<JaliumTextRangeMetrics> selection(count);Check(jalium_text_paragraph_get_selection(paragraph.get(),0,0,sample.source.size(),selection.data(),count,&count)==JALIUM_OK,"Metric source selection");double selectedAdvance=0;for(auto& rectangle:selection)selectedAdvance+=rectangle.width;Near(selectedAdvance,advance,"Selection covers the full corrected source extent");
 if(![sample.name isEqualToString:@"MetricsMultiple"]&&![sample.name isEqualToString:@"MetricsLigature"]){uint32_t sourceAt=0;double cellAt=0;for(auto cell:sample.cells){Check(jalium_text_paragraph_get_caret(paragraph.get(),0,sourceAt,0,&caret)==JALIUM_OK,"Metric cell start caret");Near(caret.x,cellAt,"Cell insertion follows literal vertical advances");if(cell.advance>0){JaliumParagraphCaret hit{};Check(jalium_text_paragraph_hit_test(paragraph.get(),0,cellAt+cell.advance*.25,&hit)==JALIUM_OK,"Metric cell hit test");Check(hit.textPosition==sourceAt||hit.textPosition==sourceAt+cell.text.length,"Hit belongs to the original composed source cell");}sourceAt+=cell.text.length;cellAt+=cell.advance;}}
 auto reference=ReferenceFont(file,sample.variable?sample.weight:0,sample.adjustedEm?sample.adjustedEm:32);if(sample.extraWidth){auto descriptor=CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge NSString*)kCTFontVariationAttribute:@{@(0x77676874):@(sample.weight),@(0x77647468):@(sample.extraWidth)}});auto varied=CTFontCreateCopyWithAttributes(reference,0,nullptr,descriptor);CFRelease(descriptor);CFRelease(reference);reference=varied;}auto expected=Reference(reference,sample.cells,line.baseline,sample.mark,sample.uprightReference,sample);CFRelease(reference);const float transform[]={0,1,-1,0,80,12};auto actual=surface.Draw(paragraph.get(),transform);auto backendExpected=expected;if(!metal)for(size_t at=0;at<backendExpected.size();at+=4){unsigned alpha=backendExpected[at+3];if(alpha)for(int c=0;c<3;++c)backendExpected[at+c]=std::min(255u,(unsigned(backendExpected[at+c])*255+alpha/2)/alpha);}
 Save(actual,[prefix stringByAppendingString:@"-actual.png"]);Save(expected,[prefix stringByAppendingString:@"-reference.png"]);size_t differences=0;for(size_t at=0;at<actual.size();++at)differences+=actual[at]!=backendExpected[at];if(differences)std::fprintf(stderr,"%s: %zu different metric bytes\n",prefix.UTF8String,differences);Check(differences==0,"Actual backend matches independent literal glyphs on the vmtx grid");
}
void VerifyWrapping(JaliumContext* context, NSString* folder, bool upright) {
    for (const auto& item : std::vector<std::pair<NSString*, std::u16string>>{
        {@"MetricsMixed", u"国国"}, {@"MetricsMixed", u"文文"},
        {@"MetricsMixed", u"中文国。"}, {@"MetricsMixed", u"中文\r\n国。"},
        {@"MetricsMark", u"中\u3099中\u3099"}, {@"MetricsMultiple", u"中中"},
        {@"MetricsLigature", u"中中"}, {@"MetricsContext", u"中文"}}) {
        const auto& [name, source] = item;
        auto data = [NSData dataWithContentsOfFile:[folder stringByAppendingPathComponent:[name stringByAppendingString:@".ttf"]]];
        std::u16string family(name.length, 0); [name getCharacters:reinterpret_cast<unichar*>(family.data()) range:NSMakeRange(0, family.size())];
        TestOwner<JaliumFontResource, jalium_font_resource_release> resource(jalium_font_resource_register(Managed(family.c_str()), static_cast<const uint8_t*>(data.bytes), data.length), jalium_font_resource_release);
        TestOwner<JaliumTextFormat, jalium_text_format_destroy> font(jalium_text_format_create(context, Managed(family.c_str()), 32, 400, 0), jalium_text_format_destroy);
        Check(font != nullptr, "Wrap metric font");
        const JaliumFontFeature features[] = {{0x6c696761, 1}, {0x63616c74, 1}};
        Check(jalium_text_format_set_font_features(font.get(), features, 2) == JALIUM_OK, "Wrap shaping features");
        JaliumTextSpan span{font.get(), 0, static_cast<uint32_t>(source.size()), 0, 0, 0, 1};
        const bool hard = source.find(u'\r') != source.npos;
        const bool negative = source == u"文文";
        const bool ligature = [name isEqualToString:@"MetricsLigature"];
        const bool contextual = [name isEqualToString:@"MetricsContext"];
        const float width = hard ? 1000 : negative ? 50 : (ligature || contextual) ? 40 : source.size() == 4 && ![name isEqualToString:@"MetricsMark"] ? 80 : 75;
        for (int alignment : {0, 1, 2}) {
            TestOwner<JaliumTextParagraph, jalium_text_paragraph_destroy> paragraph(upright ?
                jalium_text_paragraph_create_upright(font.get(), reinterpret_cast<const uint16_t*>(source.data()), source.size(), &span, 1, width, 40, alignment, 0, 0) :
                jalium_text_paragraph_create_vertical(font.get(), reinterpret_cast<const uint16_t*>(source.data()), source.size(), &span, 1, width, 40, alignment, 0, 0), jalium_text_paragraph_destroy);
            Check(paragraph != nullptr, "Wrapped metric paragraph");
            const unsigned expectedCount = negative ? 1 : 2;
            std::fprintf(stdout, "WRAP %s align%d upright%d width%g lines%u\n", name.UTF8String, alignment, upright, width, jalium_text_paragraph_line_count(paragraph.get())); std::fflush(stdout);
            Check(jalium_text_paragraph_line_count(paragraph.get()) == expectedCount, "Breaks use actual metric cell extents");
            for (unsigned lineIndex = 0; lineIndex < expectedCount; ++lineIndex) {
                JaliumParagraphLineMetrics line{};
                Check(jalium_text_paragraph_get_line(paragraph.get(), lineIndex, &line) == JALIUM_OK, "Wrapped line metrics");
                // CoreText retains calt lookahead across this soft break. The
                // ligature splits, while the contextual first glyph stays 。.
                const double advance = negative ? 38.4 : [name isEqualToString:@"MetricsMark"] ? 38.4 : [name isEqualToString:@"MetricsMultiple"] ? 44.8 : ligature ? 38.4 : contextual ? (lineIndex ? 19.2 : 25.6) : source == u"国国" ? 51.2 : lineIndex ? 76.8 : 57.6;
                Near(line.line.width, advance, "Wrapped line is reshaped using its actual glyph advances");
                const double x = (width - advance) * (alignment == 1 ? 1 : alignment == 2 ? .5 : 0);
                Near(line.line.x, x, "Intrinsic tracking participates in alignment");
                JaliumParagraphCaret caret{};
                Check(jalium_text_paragraph_get_caret(paragraph.get(), lineIndex, line.line.textPosition + line.line.length, 1, &caret) == JALIUM_OK, "Wrapped end caret");
                Near(caret.x, x + advance, "Wrapped end caret follows corrected advance");
            }
        }
    }
}
}
int main(int argc,const char**argv){@autoreleasepool{metal=argc>1&&std::string(argv[1])=="--metal";try{Check(argc>2,"Metric fixture directory");[NSApplication sharedApplication];TestOwner<JaliumContext,jalium_context_destroy> context(jalium_context_create(metal?JALIUM_BACKEND_METAL:JALIUM_BACKEND_SOFTWARE),jalium_context_destroy);Check(context!=nullptr,"Metric context");ValidationSurface surface(context.get());auto folder=[NSString stringWithUTF8String:argv[2]];
 std::vector<Sample> samples={
  {@"MetricsMixed",u"中文国。",{{@"中",38.4},{@"文",19.2},{@"国",51.2},{@"。",25.6}}},
  {@"MetricsKern",u"中文",{{@"中",35.2},{@"文",19.2}}},
  {@"MetricsLigature",u"中中",{{@"国",51.2}}},
  {@"MetricsContext",u"中文",{{@"。",25.6},{@"文",19.2}}},
  {@"MetricsMultiple",u"中",{{@"。",25.6},{@"文",19.2}}},
  {@"MetricsMark",u"中\u3099",{{@"中\u3099",38.4}},false,400,true},
 };
 for(double spacing:{6.0,-4.0})samples.push_back({@"MetricsMultiple",u"中中",{{@"。",25.6},{@"文",19.2+spacing},{@"。",25.6},{@"文",19.2}},false,400,false,true,0,spacing});
 int pairs=0;for(auto& sample:samples)for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,folder,sample,upright,split);++pairs;}
 for(NSString* name in @[@"VerticalVariable", @"VerticalMapped", @"VerticalAvar", @"VerticalAvar2"])
 for(int weight:{400,550,700})for(uint32_t optical:{0u,1u,2u})for(bool features:{false,true}){
  const double cell=weight==400?38.4:weight==700?44.8:[name isEqualToString:@"VerticalAvar"]?40:[name isEqualToString:@"VerticalAvar2"]?40.8:41.6;
  Sample sample{name,u"水水",{{@"水",cell},{@"水",cell}},true,weight,false,features,optical};auto sourceFolder=[[folder stringByDeletingLastPathComponent]stringByAppendingPathComponent:@"FontSizeAdjust"];for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,sourceFolder,sample,upright,split);++pairs;}}
 for(NSString* name in @[@"VerticalVariable",@"VerticalMapped",@"VerticalAvar",@"VerticalAvar2"]){const double cell=[name isEqualToString:@"VerticalAvar"]?40:[name isEqualToString:@"VerticalAvar2"]?40.8:41.6;Sample sample{name,u"水水",{{@"水",cell},{@"水",cell}},true,550,false,false,1,0,true};auto sourceFolder=[[folder stringByDeletingLastPathComponent]stringByAppendingPathComponent:@"FontSizeAdjust"];for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,sourceFolder,sample,upright,split);++pairs;}}

 auto gvarFolder=[[folder stringByDeletingLastPathComponent]stringByAppendingPathComponent:@"FontGvarMetrics"];
 auto gvarCell=[](NSString* name,int weight,double width,bool ascii){
  if([name isEqualToString:@"ZeroAdvance"])return 0.0;
  if([name isEqualToString:@"MissingVertical"])return 32.0;
  double coordinate=(weight-400)/300.0;
  if([name isEqualToString:@"PhantomAvar"]||[name isEqualToString:@"PhantomAvar2"]){if(coordinate>0)coordinate=coordinate<=.5?coordinate*.5:.25+(coordinate-.5)*1.5;if([name isEqualToString:@"PhantomAvar2"]&&coordinate>0)coordinate=std::min(1.0,coordinate*1.5);}
  double delta=coordinate*200;
  if([name isEqualToString:@"PhantomUnreferenced"]||(!ascii&&[name isEqualToString:@"PhantomLastMetrics"]))delta=0;
  if(!ascii&&[name isEqualToString:@"PhantomComposite"])delta=coordinate*300;
  if([name isEqualToString:@"PhantomWithVvar"])delta=ascii?0:std::max(0.0,coordinate)*200;
  if([name isEqualToString:@"PhantomIntermediate"]){double magnitude=std::abs(coordinate);delta=magnitude<=.25||magnitude>=.75?0:(coordinate<0?-1:1)*200*(magnitude<=.5?(magnitude-.25)/.25:(.75-magnitude)/.25);}
  if([name isEqualToString:@"PhantomMultiAxis"]){double other=(width-100)/50;delta=coordinate*other>0?(coordinate<0?-1:1)*200*std::abs(coordinate*other):0;}
  return (1200+delta)*32/1000;
 };
 for(NSString* name in @[@"PhantomEmbedded",@"PhantomShared",@"PhantomAllPoints",@"PhantomShortOffsets",@"PhantomTopOnly",@"PhantomBottomOnly",@"PhantomUnreferenced",@"PhantomIntermediate",@"PhantomComposite",@"PhantomUseMyMetrics",@"PhantomLastMetrics",@"PhantomAvar",@"PhantomAvar2",@"PhantomMultiAxis",@"PhantomWithVvar",@"ZeroAdvance",@"MissingVertical"]){
  for(int weight:{100,250,400,475,550,625,700}){
   if(([name isEqualToString:@"ZeroAdvance"]||[name isEqualToString:@"MissingVertical"])&&weight!=400)continue;
   double width=[name isEqualToString:@"PhantomMultiAxis"]?125:0;
   bool variable=![name isEqualToString:@"ZeroAdvance"]&&![name isEqualToString:@"MissingVertical"];
   double cell=gvarCell(name,weight,width,false);
   Sample sample{name,u"水水",{{@"水",cell},{@"水",cell}},variable,weight,false,false,1,0,false,width};
   for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,gvarFolder,sample,upright,split);++pairs;}
   cell=gvarCell(name,weight,width,true);sample.source=u"AA";sample.cells={{@"A",cell},{@"A",cell}};sample.uprightReference=true;
   for(bool split:{false,true}){Verify(context.get(),surface,gvarFolder,sample,true,split);++pairs;}
  }
 }
 for(double width:{50.0,100.0,150.0})for(int weight:{100,400,550,700}){
  double cell=gvarCell(@"PhantomMultiAxis",weight,width,false);
  Sample sample{@"PhantomMultiAxis",u"水水",{{@"水",cell},{@"水",cell}},true,weight,false,true,2,0,false,width};
  for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,gvarFolder,sample,upright,split);++pairs;}
 }
 for(NSString* name in @[@"PhantomEmbedded",@"PhantomAvar2",@"PhantomComposite",@"PhantomUseMyMetrics",@"PhantomWithVvar"])
 for(int weight:{550,700})for(bool features:{false,true}) {
  double natural=gvarCell(name,weight,0,false);
  double em=static_cast<double>(32.0L*1.2/(natural/32));
  Sample sample{name,u"水水",{{@"水",38.4},{@"水",38.4}},true,weight,false,features,1,0,false,0,false,em};
  for(bool cascade:{false,true}){sample.cascade=cascade;for(bool upright:{false,true})for(bool split:{false,true}){Verify(context.get(),surface,gvarFolder,sample,upright,split);++pairs;}}
  double a=gvarCell(name,weight,0,true)*em/32;sample.source=u"AA";sample.cells={{@"A",a},{@"A",a}};sample.cascade=false;sample.uprightReference=true;
  for(bool split:{false,true}){Verify(context.get(),surface,gvarFolder,sample,true,split);++pairs;}
 }
 for(bool upright:{false,true})VerifyWrapping(context.get(),folder,upright);
 std::printf("PASS vertical metrics: %d checks; %d independent grid/backend pixel pairs\n",checks,pairs);return 0;
 }catch(const std::exception&e){std::fprintf(stderr,"FAIL vertical metrics: %s\n",e.what());return 1;}}}
