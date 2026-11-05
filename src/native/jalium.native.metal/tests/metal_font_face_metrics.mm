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
int checks = 0, pixelChecks = 0;
void Check(bool value, const char* message) { if (!value) { std::fprintf(stderr,"At assertion %d pixel %d\n", checks,pixelChecks); throw std::runtime_error(message); } ++checks; }
void Near(double actual, double expected, const char* message) {
    if (std::abs(actual - expected) > .003) std::fprintf(stderr, "%s: %g != %g\n", message, actual, expected);
    Check(std::abs(actual - expected) < .003, message);
}
const wchar_t* Managed(const char16_t* value) { return reinterpret_cast<const wchar_t*>(value); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
Format Font(JaliumContext* context, const char16_t* family) {
    Format font(jalium_text_format_create(context, Managed(family), 32, 400, 0), jalium_text_format_destroy);
    Check(font != nullptr, "Size adjustment format unavailable");
    jalium_text_format_set_word_wrapping(font.get(), 1);
    return font;
}
CGFontRef Graphics(NSString* path) {
    NSData* data = [NSData dataWithContentsOfFile:path]; Check(data.length > 12, "Fixture unavailable");
    auto provider = CGDataProviderCreateWithCFData((__bridge CFDataRef)data);
    auto font = CGFontCreateWithDataProvider(provider); CGDataProviderRelease(provider);
    Check(font != nullptr, "Independent graphics face unavailable"); return font;
}
CTFontRef Raw(CGFontRef graphics, double size) { return CTFontCreateWithGraphicsFont(graphics, size, nullptr, nullptr); }
NSAttributedString* Pair(CTFontRef first, CTFontRef second) {
    NSMutableAttributedString* text = [[NSMutableAttributedString alloc] initWithString:@"xA" attributes:@{NSFontAttributeName:(__bridge id)first, NSForegroundColorAttributeName:NSColor.blackColor}];
    [text addAttribute:NSFontAttributeName value:(__bridge id)second range:NSMakeRange(1, 1)]; return text;
}
JaliumFontFaceMetrics Descriptor(double scale=1,double a=-1,double d=-1,double g=-1) {
    JaliumFontFaceMetrics value{}; value.structSize=sizeof(value);value.sizeAdjust=scale;
    value.ascent=a;value.descent=d;value.lineGap=g;value.verticalAscent=value.verticalDescent=value.verticalLineGap=-1;return value;
}
void Set(JaliumTextFormat* font,const JaliumFontFaceMetrics& value,double expected) {
    float used{};Check(jalium_text_format_set_font_face_metrics(font,&value,&used)==0,"Descriptor setup failed");Near(used,expected,"Descriptor used size");
}
void Pixels(JaliumTextParagraph* paragraph,CTLineRef reference,double baseline) {
    auto* native=dynamic_cast<jalium::MetalTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph));Check(native!=nullptr,"Paragraph provider");
    JaliumParagraphLineMetrics logical{};Check(jalium_text_paragraph_get_line(paragraph,0,&logical)==0,"Paragraph logical line");Near(logical.baseline,baseline,"Independent baseline");
    for(int dpi:{1,2})for(bool skew:{false,true}) {
        float matrix[]={float(dpi),skew?.2f*dpi:0,skew?-.15f*dpi:0,float(dpi),6,9};
        std::vector<uint8_t> actual;uint32_t width{},height{};float left{},top{};
        // Sufficient origin padding deliberately exposes overflow from zero line boxes.
        Check(native->RasterizeLine(0,24,128,1,matrix,1024*dpi,512*dpi,actual,width,height,left,top),"Rasterization");
        std::vector<uint8_t> pixels(width*height*4);auto space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        auto bitmap=CGBitmapContextCreate(pixels.data(),width,height,8,width*4,space,static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|static_cast<CGBitmapInfo>(kCGBitmapByteOrder32Little));Check(bitmap!=nullptr,"Reference bitmap");
        CGContextSetAllowsFontSubpixelPositioning(bitmap,true);CGContextSetShouldSubpixelPositionFonts(bitmap,true);CGContextSetAllowsFontSubpixelQuantization(bitmap,false);CGContextSetShouldSubpixelQuantizeFonts(bitmap,false);
        double x=24,y=128+baseline;CGContextConcatCTM(bitmap,CGAffineTransformMake(matrix[0],-matrix[1],-matrix[2],matrix[3],matrix[0]*x+matrix[2]*y+matrix[4]-left,height-(matrix[1]*x+matrix[3]*y+matrix[5]-top)));
        CGContextSetTextMatrix(bitmap,CGAffineTransformIdentity);CTLineDraw(reference,bitmap);
        if(actual!=pixels) {size_t differences=0;int largest=0;for(size_t i=0;i<actual.size();++i){if(actual[i]!=pixels[i])++differences;largest=std::max(largest,std::abs(int(actual[i])-int(pixels[i])));}std::fprintf(stderr,"Raster difference: %zu bytes, largest channel delta %d\n",differences,largest);}
        Check(actual==pixels&&std::any_of(actual.begin(),actual.end(),[](uint8_t v){return v!=0;}),"Glyph ink differs from independent CoreText");++pixelChecks;
        CGContextRelease(bitmap);CGColorSpaceRelease(space);
    }
}
void Compare(JaliumTextFormat* font,NSAttributedString* expected,double a,double d,double g,const std::u16string& text=u"xA") {
    auto line=CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)expected);JaliumTextMetrics metrics{};
    Check(jalium_text_format_measure_text(font,Managed(text.c_str()),text.size(),10000,1000,&metrics)==0,"Measurement");
    Near(metrics.width,CTLineGetTypographicBounds(line,nullptr,nullptr,nullptr),"Independent glyph advance");Near(metrics.ascent,a,"Logical ascent");Near(metrics.descent,d,"Logical descent");Near(metrics.lineGap,g,"Logical gap");Near(metrics.height,a+d+g,"Logical height");
    for(uint32_t index=0;index<=text.size();++index) {
        JaliumTextHitTestResult hit{};Check(jalium_text_format_hit_test_text_position(font,Managed(text.c_str()),text.size(),10000,1000,index,0,&hit)==0,"Source caret");Near(hit.caretX,CTLineGetOffsetForStringIndex(line,index,nullptr),"Source caret advance");
    }
    JaliumTextSpan span{font,0,static_cast<uint32_t>(text.size()),0,0,0,1};
    auto paragraph=jalium_text_paragraph_create_with_wrapping(font,reinterpret_cast<const uint16_t*>(text.data()),text.size(),&span,1,10000,0,0,0,1);Check(paragraph!=nullptr,"Paragraph creation");
    JaliumParagraphLineMetrics lm{};Check(jalium_text_paragraph_get_line(paragraph,0,&lm)==0,"Paragraph line");Near(lm.line.height,a+d+g,"Paragraph logical height");Pixels(paragraph,line,a);
    JaliumTextRangeMetrics selection{};uint32_t count{};Check(jalium_text_paragraph_get_selection(paragraph,0,0,text.size(),&selection,1,&count)==0&&count==1,"Selection source");Near(selection.width,metrics.width,"Selection width");Near(selection.height,a+d+g,"Selection logical height");
    JaliumTextRangeMetrics ink{};Check(jalium_text_format_get_font_ink_bounds(font,Managed(text.c_str()),text.size(),&ink)==0,"Ink bounds");
    auto bounds=CTLineGetBoundsWithOptions(line,kCTLineBoundsUseGlyphPathBounds);Near(ink.x,CGRectGetMinX(bounds),"Ink x");Near(ink.y,-CGRectGetMaxY(bounds),"Ink y");Near(ink.height,bounds.size.height,"Ink height");
    CFRelease(line);jalium_text_paragraph_destroy(paragraph);
}
CTFontRef OpticalReference(CGFontRef graphics, double size, double optical) {
    NSArray* axes = CFBridgingRelease(CGFontCopyVariationAxes(graphics));
    NSData* table = CFBridgingRelease(CGFontCopyTableForTag(graphics, 0x66766172));
    const auto* bytes = static_cast<const uint8_t*>(table.bytes);
    auto u16 = [&](size_t at) { Check(at+2 <= table.length, "Optical table bounds"); return uint32_t(bytes[at])<<8 | bytes[at+1]; };
    auto u32 = [&](size_t at) { return u16(at)<<16 | u16(at+2); };
    auto base = Raw(graphics, 32); NSDictionary* defaults = CFBridgingRelease(CTFontCopyVariation(base));
    NSMutableDictionary* coordinates = [NSMutableDictionary new];
    for (NSUInteger i = 0; i < axes.count; ++i) {
        uint32_t tag = u32(u16(4)+i*u16(10)); NSDictionary* axis = axes[i];
        double value = tag == 0x6f70737a ? optical : defaults[@(tag)] ? [defaults[@(tag)] doubleValue] : [axis[(__bridge id)kCGFontVariationAxisDefaultValue] doubleValue];
        value = std::clamp(value, [axis[(__bridge id)kCGFontVariationAxisMinValue] doubleValue], [axis[(__bridge id)kCGFontVariationAxisMaxValue] doubleValue]);
        coordinates[axis[(__bridge id)kCGFontVariationAxisName]] = @(value);
        if (tag == 0x6f70737a) optical = value;
    }
    auto selected = CGFontCreateCopyWithVariations(graphics, (__bridge CFDictionaryRef)coordinates);
    auto descriptor = CTFontDescriptorCreateWithAttributes((__bridge CFDictionaryRef)@{(__bridge id)kCTFontOpticalSizeAttribute:@(optical)});
    auto result = CTFontCreateWithGraphicsFont(selected, size, nullptr, descriptor);
    CFRelease(base); CFRelease(descriptor); CGFontRelease(selected); return result;
}
void OpticalSizing(JaliumContext* context) {
    NSString* path=@"/System/Library/Fonts/SFNS.ttf";NSData* data=[NSData dataWithContentsOfFile:path];auto resource=jalium_font_resource_register(Managed(u"MetricsOptical"),static_cast<const uint8_t*>(data.bytes),data.length);Check(resource!=nullptr,"Optical resource");auto graphics=Graphics(path);
    NSArray* axes=CFBridgingRelease(CGFontCopyVariationAxes(graphics));double defaultOptical=0;
    for(NSDictionary* axis in axes)if([axis[(__bridge id)kCGFontVariationAxisName] isEqual:@"Optical Size"])defaultOptical=[axis[(__bridge id)kCGFontVariationAxisDefaultValue] doubleValue];Check(defaultOptical>0,"Optical default");
    for(double scale:{.5,1.5,2.})for(uint32_t mode:{1u,2u})for(double optical:{0.,6.,128.})for(bool property:{false,true}) {
        auto font=Font(context,u"MetricsOptical");Set(font.get(),Descriptor(scale,1,.5,.2),32*scale);
        JaliumFontVariation axis{0x6f70737a,optical};uint32_t count=optical?1:0;Check(jalium_text_format_set_font_optical_variations(font.get(),&axis,count,mode)==0,"Descriptor optical setup");
        double used=32*scale;
        if(property) {auto before=OpticalReference(graphics,used,optical?optical:mode==1?used:defaultOptical);double aspect=CTFontGetCapHeight(before)/used;CFRelease(before);float actual{};Check(jalium_text_format_set_font_size_adjust(font.get(),2,aspect*1.5,mode,&axis,count,&actual)==0,"Descriptor property optical");used=48;Near(actual,used,"Optical final used size");}
        auto expected=OpticalReference(graphics,used,optical?optical:mode==1?used:defaultOptical);Compare(font.get(),Pair(expected,expected),used,.5*used,.2*used);CFRelease(expected);
    }
    CGFontRelease(graphics);jalium_font_resource_release(resource);
}
void Run(JaliumContext* context,NSString* root) {
    std::vector<JaliumFontResource*> resources;const char16_t* aliases[]={u"MetricsPrimary",u"MetricsSecondary"};NSArray* names=@[@"Primary",@"Secondary"];
    for(NSUInteger i=0;i<2;++i) {NSData* data=[NSData dataWithContentsOfFile:[root stringByAppendingPathComponent:[names[i] stringByAppendingString:@".ttf"]]];auto resource=jalium_font_resource_register(Managed(aliases[i]),static_cast<const uint8_t*>(data.bytes),data.length);Check(resource!=nullptr,"Fixture registration");resources.push_back(resource);}
    auto primary=Graphics([root stringByAppendingPathComponent:@"Primary.ttf"]),secondary=Graphics([root stringByAppendingPathComponent:@"Secondary.ttf"]);
    for(double scale:{.5,1.,1.5,2.})for(bool property:{false,true})for(int mode:{0,1,2,3}) {
        auto first=Font(context,aliases[0]),second=Font(context,aliases[1]);double used=property?48:32*scale;double otherUsed=property?96:64;
        auto descriptor=Descriptor(scale,mode==0?-1:mode==3?0:1,mode==0||mode==1?-1:mode==3?0:.5,mode<2?-1:mode==3?0:.2);
        Set(first.get(),descriptor,32*scale);auto secondDescriptor=Descriptor(2,mode==0?-1:mode==3?0:.5,mode<2?-1:mode==3?0:.25,mode<2?-1:mode==3?0:.1);Set(second.get(),secondDescriptor,64);
        JaliumTextFormat* cascade[]={second.get()};Check(jalium_text_format_set_font_fallbacks(first.get(),cascade,1)==0,"Cascade");
        if(property){float actual{};Check(jalium_text_format_set_font_size_adjust(first.get(),1,.75,2,nullptr,0,&actual)==0,"Property after descriptor");Near(actual,48,"Property cancels descriptor");}
        auto one=Raw(primary,used),two=Raw(secondary,otherUsed);
        double a=std::max(mode==0?.75*used:mode==3?0:used,mode==0?.375*otherUsed:mode==3?0:.5*otherUsed);
        double d=std::max(mode<2?.25*used:mode==3?0:.5*used,mode<2?.125*otherUsed:mode==3?0:.25*otherUsed);
        double g=mode<2?0:mode==3?0:std::max(.2*used,.1*otherUsed);
        Compare(first.get(),Pair(one,two),a,d,g);CFRelease(one);CFRelease(two);
    }
    {
        auto first=Font(context,aliases[0]),second=Font(context,aliases[1]);Set(first.get(),Descriptor(1.5,1,.5,.2),48);Set(second.get(),Descriptor(2,.5,.25,.1),64);
        JaliumTextFormat* cascade[]={second.get()};Check(jalium_text_format_set_font_fallbacks(first.get(),cascade,1)==0,"Caps descriptor cascade");Check(jalium_text_format_set_font_caps(first.get(),1,1,nullptr,0)==0,"Caps descriptor setup");
        for(uint32_t position:{0u,1u,2u}) {
            Check(jalium_text_format_set_font_position(first.get(),position,1,0)==0,"Position descriptor setup");double scale=position?.65:1;
            // The existing synthesis API stores each intermediate size as float.
            // Reproduce those independent literal steps before changing faces.
            float positioned=static_cast<float>(48*scale),capital=static_cast<float>(positioned*.8);
            auto x=Raw(secondary,double(capital)*64/48),a=Raw(secondary,static_cast<float>(64*scale));
            NSMutableAttributedString* expected=[[NSMutableAttributedString alloc]initWithString:@"XA" attributes:@{NSFontAttributeName:(__bridge id)x,NSForegroundColorAttributeName:NSColor.blackColor}];[expected addAttribute:NSFontAttributeName value:(__bridge id)a range:NSMakeRange(1,1)];
            if(position)[expected addAttribute:(__bridge NSString*)kCTBaselineOffsetAttributeName value:@(position==1?-12.8:22.4) range:NSMakeRange(0,2)];Compare(first.get(),expected,48,24,9.6);CFRelease(x);CFRelease(a);
        }
    }
    // Omitted overrides retain the selected face's natural axes. Invalid setters
    // must be atomic, including oversized ABI or non-finite descriptor values.
    auto font=Font(context,aliases[0]);auto stable=Descriptor(1.5,1,.5,.2);Set(font.get(),stable,48);
    for(double invalid:{-2.,std::numeric_limits<double>::infinity(),std::numeric_limits<double>::quiet_NaN()}) {
        for(int member=0;member<7;++member) {auto bad=stable;double* fields[]={&bad.sizeAdjust,&bad.ascent,&bad.descent,&bad.lineGap,&bad.verticalAscent,&bad.verticalDescent,&bad.verticalLineGap};*fields[member]=invalid;float result{};Check(jalium_text_format_set_font_face_metrics(font.get(),&bad,&result)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid descriptor accepted");}
    }
    auto raw=Raw(primary,48);NSAttributedString* expected=[[NSAttributedString alloc]initWithString:@"x" attributes:@{NSFontAttributeName:(__bridge id)raw,NSForegroundColorAttributeName:NSColor.blackColor}];Compare(font.get(),expected,48,24,9.6,u"x");CFRelease(raw);
    auto bad=stable;bad.structSize=sizeof(bad)-1;float value{};Check(jalium_text_format_set_font_face_metrics(font.get(),&bad,&value)==JALIUM_ERROR_INVALID_ARGUMENT,"ABI bounds");
    // Empty and trailing-break rows retain their own strut, including exact zero.
    for(double factor:{0.,.5,1.}) {
        Set(font.get(),Descriptor(1,factor,factor*.5,factor*.2),32);
        const uint16_t empty[]={0};auto paragraph=jalium_text_paragraph_create_with_wrapping(font.get(),empty,0,nullptr,0,200,0,0,0,1);Check(paragraph!=nullptr,"Empty paragraph");
        JaliumParagraphLineMetrics row{};Check(jalium_text_paragraph_get_line(paragraph,0,&row)==0,"Empty row");Near(row.line.height,54.4*factor,"Empty row metric");Near(row.baseline,32*factor,"Empty baseline");jalium_text_paragraph_destroy(paragraph);
        JaliumTextMetrics text{};Check(jalium_text_format_measure_text(font.get(),Managed(u"x\nx\n"),4,200,1000,&text)==0,"Multiline format");Check(text.lineCount==3,"Trailing empty row count");Near(text.height,54.4*factor*3,"Multiline height");
    }
    {
        auto unicode=Font(context,aliases[0]);auto raw=Raw(primary,32);NSAttributedString* sample=[[NSAttributedString alloc]initWithString:@"x😀" attributes:@{NSFontAttributeName:(__bridge id)raw}];auto line=CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)sample);auto expected=CTLineGetBoundsWithOptions(line,kCTLineBoundsUseGlyphPathBounds);
        JaliumTextRangeMetrics bounds{};Check(jalium_text_format_get_font_ink_bounds(unicode.get(),Managed(u"x😀"),3,&bounds)==0&&bounds.length==3,"Ink UTF-16 source range");Near(bounds.x,CGRectGetMinX(expected),"Unicode ink x");Near(bounds.y,-CGRectGetMaxY(expected),"Unicode ink y");Near(bounds.width,expected.size.width,"Unicode ink width");Near(bounds.height,expected.size.height,"Unicode ink height");
        Check(jalium_text_format_get_font_ink_bounds(unicode.get(),Managed(u""),0,&bounds)==0&&bounds.length==0&&bounds.width==0&&bounds.height==0,"Empty ink");
        Check(jalium_text_format_get_font_ink_bounds(unicode.get(),Managed(u"x"),0xffffffffu,&bounds)==JALIUM_ERROR_INVALID_ARGUMENT,"Ink length bounds");CFRelease(line);CFRelease(raw);
    }
    CGFontRelease(primary);CGFontRelease(secondary);for(auto resource:resources)jalium_font_resource_release(resource);
}
}
int main(int argc,char** argv) {
    @autoreleasepool {try {Check(argc==2,"Fixture directory required");[NSApplication sharedApplication];NSString* root=[NSString stringWithUTF8String:argv[1]];
        static_assert(sizeof(JaliumFontFaceMetrics)==64);
        for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE}) {auto context=jalium_context_create(backend);Check(context!=nullptr,"Backend context");Run(context,root);OpticalSizing(context);jalium_context_destroy(context);}
        std::printf("Font face metrics: %d checks; %d independent raster comparisons\n",checks,pixelChecks);return 0;
    }catch(const std::exception& e){std::fprintf(stderr,"FAIL %s\n",e.what());return 1;}}
}
