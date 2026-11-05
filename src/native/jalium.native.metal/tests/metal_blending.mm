#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <vector>

namespace {
int checks=0,scenes=0;
void Check(bool value,const char* message) { if(!value)throw std::runtime_error(message);++checks; }
// Independent Chromium Canvas samples; the implementation under test is never
// used to manufacture its expected colors. RGB is straight, alpha is 235/255.
constexpr std::array<std::array<int,4>,17> colors={{{153,90,156,235},{63,77,145,235},{158,158,214,235},
    {79,120,204,235},{67,90,156,235},{154,145,204,235},{141,175,224,235},{46,55,186,235},
    {132,100,204,235},{86,129,204,235},{132,111,131,235},{141,137,151,235},{182,93,173,235},
    {80,141,192,235},{166,102,168,235},{55,133,191,235},{162,166,255,255}}};
struct Surface {
    std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context;
    NSView* view;
    std::unique_ptr<JaliumRenderTarget,decltype(&jalium_render_target_destroy)> target;
    int dpi,width,height; bool premultiplied;
    Surface(JaliumBackend backend,int scale,int engine):context(jalium_context_create(backend),jalium_context_destroy),
        view([[NSView alloc]initWithFrame:NSMakeRect(0,0,80,64)]),target(nullptr,jalium_render_target_destroy),dpi(scale),width(80*scale),height(64*scale),premultiplied(backend==JALIUM_BACKEND_METAL) {
        Check(context!=nullptr,"Context");JaliumSurfaceDescriptor d{};d.platform=JALIUM_PLATFORM_MACOS;
        d.kind=JALIUM_SURFACE_KIND_COMPOSITION_TARGET;d.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_composition_surface(context.get(),&d,width,height));Check(target!=nullptr,"Surface");
        jalium_render_target_set_dpi(target.get(),96*dpi,96*dpi);Check(jalium_render_target_set_engine(target.get(),static_cast<JaliumRenderingEngine>(engine))==0,"Engine");
        Check(jalium_render_target_supports_blend_modes(target.get())==1,"Blend capability");
    }
    void Begin(bool transparent=true) {Check(jalium_render_target_request_readback(target.get())==0,"Readback");Check(jalium_render_target_begin_draw(target.get())==0,"Begin");jalium_render_target_clear(target.get(),transparent?0:1,0,0,transparent?0:1);}
    void Rect(float r,float g,float b,float a=1) {auto brush=jalium_brush_create_solid(context.get(),r,g,b,a);Check(brush!=nullptr,"Brush");jalium_draw_fill_rectangle(target.get(),8,8,48,40,brush);jalium_brush_destroy(brush);}
    void Capture(){Check(jalium_alpha_mask_begin_capture(target.get(),0,0,80,64)==0,"Capture");}
    void Blend(int mode){jalium_effect_end_capture(target.get());Check(jalium_draw_blend_effect(target.get(),mode,0,0,80,64)==0,"Blend");}
    std::vector<uint8_t> End(){Check(jalium_render_target_end_draw(target.get())==0,"End");std::vector<uint8_t> p(width*height*4);int32_t w{},h{};Check(jalium_render_target_fetch_readback(target.get(),p.data(),width*4,&w,&h)==0&&w==width&&h==height,"Fetch");if(premultiplied)for(size_t i=0;i<p.size();i+=4)if(p[i+3]>0&&p[i+3]<255)
            for(int c=0;c<3;c++)p[i+c]=uint8_t(std::min(255,(p[i+c]*255+p[i+3]/2)/p[i+3]));
        ++scenes;return p;}
    void Pixel(const std::vector<uint8_t>& p,int x,int y,std::array<int,4> expected,int tolerance=3){auto at=p.data()+(size_t(y*dpi)*width+x*dpi)*4;int actual[]={at[2],at[1],at[0],at[3]};
        for(int c=0;c<4;c++)if(std::abs(actual[c]-expected[c])>tolerance){std::fprintf(stderr,"scene=%d pixel=%d,%d RGBA=%d,%d,%d,%d expected=%d,%d,%d,%d\n",scenes,x,y,actual[0],actual[1],actual[2],actual[3],expected[0],expected[1],expected[2],expected[3]);throw std::runtime_error("Independent blend pixel");}++checks;}
};
void Run(Surface& s) {
    for(int mode=0;mode<17;mode++) {
        s.Begin();s.Rect(39/255.f,164/255.f,219/255.f,193/255.f);s.Capture();s.Rect(197/255.f,62/255.f,133/255.f,171/255.f);s.Blend(mode);
        auto p=s.End();s.Pixel(p,24,24,colors[mode]);s.Pixel(p,4,4,{0,0,0,0});
    }
    for(bool isolate:{false,true})for(bool transform:{false,true})for(bool clipped:{false,true}) {
        s.Begin(false);if(clipped)jalium_push_clip(s.target.get(),16,16,24,24);
        const float m[]={.75f,.25f,-.25f,.75f,16,0};if(transform)jalium_push_transform(s.target.get(),m);
        if(isolate)s.Capture();s.Capture();s.Rect(0,0,1);s.Blend(1);if(isolate)s.Blend(0);
        if(transform)jalium_pop_transform(s.target.get());if(clipped)jalium_pop_clip(s.target.get());auto p=s.End();
        s.Pixel(p,24,24,isolate?std::array<int,4>{0,0,255,255}:std::array<int,4>{0,0,0,255});s.Pixel(p,4,4,{255,0,0,255});
    }
    s.Begin(false);jalium_push_opacity(s.target.get(),.5f);s.Capture();s.Capture();s.Rect(0,0,1);s.Blend(1);s.Blend(0);jalium_pop_opacity(s.target.get());auto p=s.End();s.Pixel(p,24,24,{128,0,128,255});
    s.Begin(false);s.Capture();s.Capture();s.Rect(0,0,1);jalium_effect_end_capture(s.target.get());const uint8_t mask[]={128};
    Check(jalium_draw_alpha_mask_effect(s.target.get(),mask,1,1,1,1,0,0,80,64,0,0,80,64)==0,"Nested mask");s.Blend(1);p=s.End();s.Pixel(p,24,24,{127,0,0,255});
    s.Begin();s.Capture();s.Rect(1,0,0);jalium_effect_end_capture(s.target.get());Check(jalium_draw_blend_effect(s.target.get(),17,0,0,80,64)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid mode accepted");
    Check(jalium_draw_blend_effect(s.target.get(),0,0,0,0,64)==JALIUM_ERROR_INVALID_ARGUMENT,"Empty capture accepted");s.End();
}
}
int main(){@autoreleasepool{try{[NSApplication sharedApplication];for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE})for(int dpi:{1,2})for(int engine:{1,2}){Surface s(backend,dpi,engine);Run(s);}std::printf("PASS CSS blend: %d scenes, %d checks\n",scenes,checks);return 0;}catch(const std::exception& error){std::fprintf(stderr,"FAIL %s (%d checks)\n",error.what(),checks);return 1;}}}
