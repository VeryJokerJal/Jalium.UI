#include "jalium_api.h"
#import <AppKit/AppKit.h>
#import <CoreText/CoreText.h>
#include <mach-o/dyld.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <vector>

namespace {
int checks=0;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);++checks;}
const wchar_t* U(const char16_t* text){return reinterpret_cast<const wchar_t*>(text);}
struct Surface {
    JaliumContext* context=jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    NSView* view=[[NSView alloc]initWithFrame:NSMakeRect(0,0,220,140)];
    JaliumRenderTarget* target=nullptr;
    explicit Surface(int scale){
        Check(context!=nullptr,"Software context without a Metal backend");view.wantsLayer=YES;
        JaliumSurfaceDescriptor d{};d.platform=JALIUM_PLATFORM_MACOS;d.kind=JALIUM_SURFACE_KIND_NATIVE_WINDOW;d.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
        target=jalium_render_target_create_for_surface(context,&d,220*scale,140*scale);Check(target!=nullptr,"Software surface");jalium_render_target_set_dpi(target,96*scale,96*scale);
    }
    ~Surface(){jalium_render_target_destroy(target);jalium_context_destroy(context);}
    void Begin(){Check(jalium_render_target_request_readback(target)==JALIUM_OK,"Request Software readback");Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"Begin Software frame");jalium_render_target_clear(target,0,0,0,0);}
    std::vector<uint8_t> End(int scale){Check(jalium_render_target_end_draw(target)==JALIUM_OK,"End Software frame");std::vector<uint8_t> p(220*140*scale*scale*4);int32_t w,h;Check(jalium_render_target_fetch_readback(target,p.data(),220*scale*4,&w,&h)==JALIUM_OK&&w==220*scale&&h==140*scale,"Software readback dimensions");return p;}
};
void GlyphsAndMetrics(Surface& s,int scale){
    auto* font=jalium_text_format_create(s.context,U(u"Helvetica Neue"),40,400,0);auto* brush=jalium_brush_create_solid(s.context,1,0,0,1);Check(font&&brush,"Native Software font");
    JaliumTextMetrics metrics{};Check(jalium_text_format_measure_text(font,U(u"Miii"),4,1000,1000,&metrics)==JALIUM_OK,"Software text metrics");
    CTFontRef ref=CTFontCreateWithName(CFSTR("HelveticaNeue"),40,nullptr);CFAttributedStringRef a=CFAttributedStringCreate(kCFAllocatorDefault,CFSTR("Miii"),(__bridge CFDictionaryRef)@{(__bridge id)kCTFontAttributeName:(__bridge id)ref});CTLineRef line=CTLineCreateWithAttributedString(a);Check(std::abs(metrics.width-CTLineGetTypographicBounds(line,nullptr,nullptr,nullptr))<.001,"Software advance matches independent CoreText");CFRelease(line);CFRelease(a);CFRelease(ref);
    s.Begin();jalium_draw_text(s.target,U(u"Miii"),4,font,20,20,180,100,brush);auto p=s.End(scale);int rows=0;for(int y=0;y<140*scale;++y){bool ink=false;for(int x=0;x<220*scale;++x)ink|=p[(y*220*scale+x)*4+3]!=0;rows+=ink;}Check(rows>=25*scale,"Software paints real glyph contours instead of a placeholder line");
    uint32_t range[]={0,4};Check(jalium_render_target_set_text_glyph_ranges(s.target,range,1,2)==JALIUM_OK,"Software glyph-range capability");s.Begin();jalium_draw_text(s.target,U(u"Miii"),4,font,20,20,180,100,brush);p=s.End(scale);Check(std::all_of(p.begin(),p.end(),[](uint8_t v){return v==0;}),"Software exclusion clears every glyph pixel");Check(jalium_render_target_set_text_glyph_ranges(s.target,nullptr,0,0)==JALIUM_OK,"Reset Software filter");
    JaliumTextSpan span{font,0,4,1,0,0,1};auto* paragraph=jalium_text_paragraph_create_with_wrapping(font,reinterpret_cast<const uint16_t*>(u"Miii"),4,&span,1,180,1,0,0,1);Check(paragraph&&jalium_text_paragraph_line_count(paragraph)==1,"Software immutable CoreText paragraph");s.Begin();Check(jalium_render_target_draw_paragraph_line(s.target,paragraph,0,20,20,1)==JALIUM_OK,"Software paragraph rasterization");p=s.End(scale);Check(std::any_of(p.begin(),p.end(),[](uint8_t v){return v!=0;}),"Software paragraph has actual ink");jalium_text_paragraph_destroy(paragraph);jalium_brush_destroy(brush);jalium_text_format_destroy(font);
}
void DpiAndTranslation(Surface& s,int scale){
    auto* brush=jalium_brush_create_solid(s.context,1,0,0,1);float translate[]={1,0,0,1,7,11};s.Begin();jalium_push_transform(s.target,translate);jalium_draw_fill_rectangle(s.target,2,3,4,5,brush);jalium_pop_transform(s.target);auto p=s.End(scale);int left=220*scale,top=140*scale,right=0,bottom=0;for(int y=0;y<140*scale;++y)for(int x=0;x<220*scale;++x)if(p[(y*220*scale+x)*4+3]){left=std::min(left,x);right=std::max(right,x+1);top=std::min(top,y);bottom=std::max(bottom,y+1);}Check(left==9*scale&&top==14*scale&&right==13*scale&&bottom==19*scale,"Parent DPI scales child translation in device coordinates");jalium_brush_destroy(brush);
}
void PresentationAlpha(Surface& s,int scale){
    s.Begin();jalium_render_target_clear(s.target,.2f,.4f,.8f,.5f);auto p=s.End(scale);Check(std::abs(p[0]-204)<=1&&std::abs(p[1]-102)<=1&&std::abs(p[2]-51)<=1&&std::abs(p[3]-128)<=1,"Software readback remains straight BGRA");CGImageRef image=(__bridge CGImageRef)s.view.layer.contents;Check(image&&CGImageGetAlphaInfo(image)==kCGImageAlphaFirst,"CALayer image declares straight framebuffer alpha");
    std::vector<uint8_t> output(4*220*140*scale*scale);CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);CGContextRef context=CGBitmapContextCreate(output.data(),220*scale,140*scale,8,220*scale*4,space,kCGImageAlphaPremultipliedFirst|kCGBitmapByteOrder32Little);Check(context!=nullptr,"Independent premultiplied compositor");CGContextSetBlendMode(context,kCGBlendModeCopy);CGContextDrawImage(context,CGRectMake(0,0,220*scale,140*scale),image);Check(std::abs(output[0]-102)<=1&&std::abs(output[1]-51)<=1&&std::abs(output[2]-26)<=1&&std::abs(output[3]-128)<=1,"Presented translucent colors multiply alpha exactly once");CGContextRelease(context);CGColorSpaceRelease(space);
}
}
int main(){@autoreleasepool{try{[NSApplication sharedApplication];for(int scale:{1,2}){Surface s(scale);GlyphsAndMetrics(s,scale);DpiAndTranslation(s,scale);PresentationAlpha(s,scale);}bool metalLoaded=false;for(uint32_t i=0;i<_dyld_image_count();++i)metalLoaded|=std::strstr(_dyld_get_image_name(i),"libjalium.native.metal.dylib")!=nullptr;Check(!metalLoaded,"Software text does not load the Metal backend");std::printf("PASS Apple Software text: %d checks; independent CoreText, device geometry and CALayer alpha; no Metal backend\n",checks);return 0;}catch(const std::exception& e){std::fprintf(stderr,"FAIL Apple Software text: %s\n",e.what());return 1;}}}
