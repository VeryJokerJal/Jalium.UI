#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <stdexcept>
#include <vector>

namespace {
constexpr int W=256,H=128;
const wchar_t* U(const char16_t* text){return reinterpret_cast<const wchar_t*>(text);}
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
struct Surface {
    JaliumContext* context=jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    NSView* view=[[NSView alloc]initWithFrame:NSMakeRect(0,0,W,H)];
    JaliumRenderTarget* target=nullptr; int dpi;
    explicit Surface(int scale):dpi(scale){
        Check(context!=nullptr,"Software context");view.wantsLayer=YES;
        JaliumSurfaceDescriptor descriptor{};descriptor.platform=JALIUM_PLATFORM_MACOS;
        descriptor.kind=JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
        target=jalium_render_target_create_for_surface(context,&descriptor,W*dpi,H*dpi);
        Check(target!=nullptr,"Software surface");jalium_render_target_set_dpi(target,96*dpi,96*dpi);
    }
    ~Surface(){jalium_render_target_destroy(target);jalium_context_destroy(context);}
    std::vector<uint8_t> Capture(JaliumTextFormat* font,JaliumBrush* brush,
        const std::array<float,6>& matrix,float opacity,int range,int kind){
        Check(jalium_render_target_request_readback(target)==JALIUM_OK,"Request readback");
        Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"Begin frame");
        if(kind==2)jalium_render_target_clear(target,241/255.f,245/255.f,241/255.f,1);
        else jalium_render_target_clear(target,0,0,0,0);
        jalium_push_transform(target,matrix.data());
        jalium_push_rounded_rect_clip(target,4,4,234,112,11,11);
        jalium_push_opacity(target,kind==0?1:opacity);
        const uint32_t spans[]{0,5};
        Check(jalium_render_target_set_text_glyph_ranges(target,range?spans:nullptr,range?1:0,range)==JALIUM_OK,"Glyph ranges");
        if(kind==1)jalium_draw_fill_rectangle(target,-4096,-4096,8192,8192,brush);
        else {
            constexpr char16_t text[]=u"CSS A\u0301 \u03a9 \u4e2d\u6587";
            jalium_draw_text(target,U(text),sizeof(text)/sizeof(char16_t)-1,font,12,10,220,90,brush);
        }
        Check(jalium_render_target_set_text_glyph_ranges(target,nullptr,0,0)==JALIUM_OK,"Reset glyph ranges");
        jalium_pop_opacity(target);jalium_pop_clip(target);jalium_pop_transform(target);
        Check(jalium_render_target_end_draw(target)==JALIUM_OK,"End frame");
        int32_t width=0,height=0;std::vector<uint8_t> pixels(W*H*dpi*dpi*4);
        Check(jalium_render_target_fetch_readback(target,pixels.data(),W*dpi*4,&width,&height)==JALIUM_OK&&width==W*dpi&&height==H*dpi,"Readback dimensions");
        return pixels;
    }
};
}

int main(){@autoreleasepool{
    [NSApplication sharedApplication];int count=0,failed=0,maximum=0;
    const std::array<std::array<float,6>,4> transforms{{
        {1,0,0,1,0,0},{1,0,0,1,2.375f,3.625f},
        {.94f,.24f,-.24f,.94f,28,1},{.82f,.12f,.22f,1.04f,8,3}}};
    for(int dpi:{1,2}){
        Surface surface(dpi);
        auto* font=jalium_text_format_create(surface.context,U(u"Georgia"),28,400,0);
        auto* white=jalium_brush_create_solid(surface.context,1,1,1,1);Check(font&&white,"Native font and coverage brush");
        for(int radial:{0,1})for(int spread:{0,1,2}){
            const JaliumGradientStop stops[]{{0,.85f,.12f,.18f,.35f},{.43f,.1f,.7f,.35f,1},{1,.1f,.3f,.9f,.8f}};
            auto* brush=radial?jalium_brush_create_radial_gradient(surface.context,98,44,70,36,83,37,stops,3,spread)
                :jalium_brush_create_linear_gradient(surface.context,7,5,96,47,stops,3,spread);
            Check(brush!=nullptr,"Gradient brush");
            Check(jalium_brush_set_color_interpolation_mode(brush,spread%2)==JALIUM_OK,"Interpolation mode");
            const JaliumMatrix paint{.9f,.15f,-.12f,1.1f,7,3};
            Check(jalium_brush_set_paint_transform(brush,&paint)==JALIUM_OK,"Paint transform");
            for(size_t transform=0;transform<transforms.size();++transform)
            for(float opacity:{1.f,.53f})for(int range:{0,1,2}){
                ++count;auto mask=surface.Capture(font,white,transforms[transform],opacity,range,0);
                auto paintPixels=surface.Capture(font,brush,transforms[transform],opacity,range,1);
                auto actual=surface.Capture(font,brush,transforms[transform],opacity,range,2);
                int ink=0,difference=0;
                for(size_t at=0;at<actual.size();at+=4){
                    const int alpha=static_cast<int>(std::lround(mask[at+3]*paintPixels[at+3]/255.0));
                    if(mask[at+3])++ink;
                    for(int channel=0;channel<3;++channel){
                        const int paper=channel==1?245:241;
                        const int expected=(paintPixels[at+channel]*alpha+paper*(255-alpha))/255;
                        difference=std::max(difference,std::abs(expected-actual[at+channel]));
                    }
                    difference=std::max(difference,std::abs(255-actual[at+3]));
                }
                maximum=std::max(maximum,difference);
                if(ink<30||difference>3){++failed;if(failed<=8)std::fprintf(stderr,"FAIL gradient text: dpi=%d radial=%d spread=%d transform=%zu opacity=%.2f range=%d ink=%d delta=%d\n",dpi,radial,spread,transform,opacity,range,ink,difference);}
            }
            jalium_brush_destroy(brush);
        }
        jalium_brush_destroy(white);jalium_text_format_destroy(font);
    }
    std::printf("{\"count\":%d,\"failed\":%d,\"maxChannelDifference\":%d,\"tolerance\":3,\"nativeGlyphMask\":true}\n",count,failed,maximum);
    return failed?1:0;
}}
