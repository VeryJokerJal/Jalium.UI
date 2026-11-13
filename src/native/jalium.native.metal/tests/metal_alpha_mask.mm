#include "jalium_api.h"
#include "jalium_alpha_mask.h"
#import <AppKit/AppKit.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <memory>
#include <limits>
#include <stdexcept>
#include <vector>
namespace {
int checks=0;
void Check(bool v,const char* message){if(!v)throw std::runtime_error(message);++checks;}
void SamplerChecks(){
    uint8_t pixels[]={0,64,128,255};float identity[]={1,0,0,1,0,0};
    Check(jalium::AlphaMaskSampler::Valid(pixels,4,2,2,2,0,0,2,2),"Valid A8 rejected");
    Check(!jalium::AlphaMaskSampler::Valid(pixels,3,2,2,2,0,0,2,2),"Truncated A8 accepted");
    jalium::AlphaMaskSampler original(pixels,2,2,2,0,0,2,2,identity);
    Check(original.At(1,1)==112,"Independent bilinear weights");
    for(auto matrix:std::vector<std::vector<float>>{{2,0,0,3,7,9},{1,.5f,-.25f,1,8,4},{0,1,-1,0,10,10}}){
        jalium::AlphaMaskSampler transformed(pixels,2,2,2,0,0,2,2,matrix.data());
        for(float y:{.25f,.5f,1.f,1.5f,1.75f})for(float x:{.25f,.5f,1.f,1.5f,1.75f})
            // Inverting a non-dyadic determinant can cross an exact half-byte
            // rounding boundary. Compare within one quantization level.
            Check(std::abs(int(transformed.At(matrix[0]*x+matrix[2]*y+matrix[4],matrix[1]*x+matrix[3]*y+matrix[5]))-int(original.At(x,y)))<=1,"Inverse affine sampling");
    }
    float singular[]={1,1,1,1,0,0};jalium::AlphaMaskSampler empty(pixels,2,2,2,0,0,2,2,singular);
    Check(empty.At(1,1)==0&&original.At(-.01f,1)==0&&original.At(2,1)==0,"Mask domain or singular transform");
}
struct Surface {
    std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context;
    NSView* view;
    std::unique_ptr<JaliumRenderTarget,decltype(&jalium_render_target_destroy)> target;
    int width=96,height=80;float scale;
    Surface(JaliumBackend backend,float dpi,int engine):context(jalium_context_create(backend),jalium_context_destroy),
        view([[NSView alloc]initWithFrame:NSMakeRect(0,0,96,80)]),target(nullptr,jalium_render_target_destroy),scale(dpi) {
        Check(context!=nullptr,"Context unavailable");JaliumSurfaceDescriptor d{};d.platform=JALIUM_PLATFORM_MACOS;
        d.kind=JALIUM_SURFACE_KIND_NATIVE_WINDOW;d.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);width=std::lround(width*dpi);height=std::lround(height*dpi);
        target.reset(jalium_render_target_create_for_surface(context.get(),&d,width,height));Check(target!=nullptr,"Target unavailable");
        jalium_render_target_set_dpi(target.get(),96*dpi,96*dpi);
        Check(jalium_render_target_set_engine(target.get(),static_cast<JaliumRenderingEngine>(engine))==JALIUM_OK,"Rendering engine");
    }
    void Rect(float x,float y,float w,float h,float r,float g,float b,float a=1){auto brush=jalium_brush_create_solid(context.get(),r,g,b,a);Check(brush!=nullptr,"Brush");jalium_draw_fill_rectangle(target.get(),x,y,w,h,brush);jalium_brush_destroy(brush);}
    void Begin(){Check(jalium_render_target_request_readback(target.get())==0,"Readback request");Check(jalium_render_target_begin_draw(target.get())==0,"Begin frame");jalium_render_target_clear(target.get(),1,1,1,1);}
    std::vector<uint8_t> End(){Check(jalium_render_target_end_draw(target.get())==0,"End frame");std::vector<uint8_t> pixels(width*height*4);int32_t w{},h{};Check(jalium_render_target_fetch_readback(target.get(),pixels.data(),width*4,&w,&h)==0&&w==width&&h==height,"Readback");return pixels;}
    void Pixel(const std::vector<uint8_t>& data,int x,int y,int r,int g,int b){auto p=data.data()+(size_t(int(y*scale))*width+int(x*scale))*4;
        if(std::abs(int(p[2])-r)>1||std::abs(int(p[1])-g)>1||std::abs(int(p[0])-b)>1){std::fprintf(stderr,"pixel %d,%d: %d %d %d != %d %d %d\n",x,y,p[2],p[1],p[0],r,g,b);throw std::runtime_error("Group mask pixel");}++checks;}
    void Capture(float x,float y,float w,float h){Check(jalium_alpha_mask_begin_capture(target.get(),x,y,w,h)==JALIUM_OK,"Mask capture");}
    void Mask(std::vector<uint8_t>& alpha,float x=8,float y=8,float w=64,float h=56){Check(jalium_draw_alpha_mask_effect(target.get(),alpha.data(),alpha.size(),4,4,4,x,y,w,h,0,0,96,80)==0,"Mask composite");}
};
void Run(Surface& s){
    for(int alpha:{0,64,128,255})for(bool opacity:{false,true}) {
        s.Begin();if(opacity)jalium_push_opacity(s.target.get(),.5);
        s.Capture(0,0,96,80);s.Rect(8,8,48,48,1,0,0);s.Rect(24,24,48,40,1,0,0);jalium_effect_end_capture(s.target.get());
        std::vector<uint8_t> mask(16,alpha);s.Mask(mask);std::fill(mask.begin(),mask.end(),255-alpha);
        if(opacity)jalium_pop_opacity(s.target.get());s.Rect(80,8,8,8,0,0,1);
        auto pixels=s.End();int other=255-std::lround(alpha*(opacity?.5:1));s.Pixel(pixels,12,12,255,other,other);s.Pixel(pixels,32,32,255,other,other);
        s.Pixel(pixels,68,60,255,other,other);s.Pixel(pixels,4,4,255,255,255);s.Pixel(pixels,84,12,0,0,255);
    }
    s.Begin();s.Capture(0,0,96,80);s.Rect(0,0,96,80,0,1,0);jalium_effect_end_capture(s.target.get());std::vector<uint8_t> mask(16,255);
    Check(jalium_draw_alpha_mask_effect(s.target.get(),mask.data(),15,4,4,4,0,0,96,80,0,0,96,80)==JALIUM_ERROR_INVALID_ARGUMENT,"Short mask accepted");
    Check(jalium_draw_alpha_mask_effect(s.target.get(),mask.data(),16,4,4,3,0,0,96,80,0,0,96,80)==JALIUM_ERROR_INVALID_ARGUMENT,"Short stride accepted");s.Mask(mask,32,16,16,16);
    auto pixels=s.End();s.Pixel(pixels,36,20,0,255,0);s.Pixel(pixels,4,4,255,255,255);

    // A child mask is composited into its parent's capture; the parent mask
    // and opacity then apply to that completed image exactly once.
    s.Begin();jalium_push_opacity(s.target.get(),.5f);jalium_push_clip(s.target.get(),16,16,48,40);
    s.Capture(0,0,96,80);
    s.Capture(0,0,96,80);s.Rect(8,8,64,56,1,0,0);jalium_effect_end_capture(s.target.get());
    std::fill(mask.begin(),mask.end(),128);s.Mask(mask);
    jalium_effect_end_capture(s.target.get());s.Mask(mask);jalium_pop_clip(s.target.get());jalium_pop_opacity(s.target.get());
    pixels=s.End();s.Pixel(pixels,32,32,255,223,223);s.Pixel(pixels,12,12,255,255,255);

    // Scaling/shearing maps all four capture corners. Sampling the mask must
    // use that same transform and DPI, and restoring it must leave later draws intact.
    s.Begin();float matrix[]={.75f,.25f,-.25f,.75f,24,8};jalium_push_transform(s.target.get(),matrix);
    s.Capture(0,0,64,64);s.Rect(0,0,64,64,1,0,0);jalium_effect_end_capture(s.target.get());
    Check(jalium_draw_alpha_mask_effect(s.target.get(),mask.data(),mask.size(),4,4,4,0,0,64,64,0,0,64,64)==0,"Transformed mask");
    jalium_pop_transform(s.target.get());s.Rect(80,8,8,8,0,0,1);pixels=s.End();
    s.Pixel(pixels,40,40,255,127,127);s.Pixel(pixels,4,4,255,255,255);s.Pixel(pixels,84,12,0,0,255);

    s.Begin();s.Capture(0,0,96,80);
    jalium_effect_begin_capture(s.target.get(),std::numeric_limits<float>::quiet_NaN(),0,10,10);jalium_effect_end_capture(s.target.get());
    s.Rect(8,8,64,56,1,0,0);jalium_effect_end_capture(s.target.get());s.Mask(mask);pixels=s.End();s.Pixel(pixels,32,32,255,127,127);

    s.Begin();const float triangle[]={2,8,8,0,72,8,0,8,64,5};
    Check(jalium_push_path_clip(s.target.get(),0,0,triangle,sizeof(triangle)/sizeof(float),1)==JALIUM_OK,"Parent path clip");
    s.Capture(0,0,96,80);s.Rect(0,0,96,80,1,0,0);jalium_effect_end_capture(s.target.get());s.Mask(mask);
    jalium_pop_clip(s.target.get());s.Rect(80,8,8,8,0,0,1);pixels=s.End();
    s.Pixel(pixels,16,16,255,127,127);s.Pixel(pixels,64,56,255,255,255);s.Pixel(pixels,84,12,0,0,255);

    s.Begin();s.Capture(200,200,30,30);s.Rect(200,200,30,30,1,0,0);jalium_effect_end_capture(s.target.get());
    Check(jalium_draw_alpha_mask_effect(s.target.get(),mask.data(),mask.size(),4,4,4,200,200,30,30,200,200,30,30)==JALIUM_OK,"Offscreen mask must be a no-op");
    pixels=s.End();s.Pixel(pixels,16,16,255,255,255);

    if(jalium_render_target_supports_retained_layers(s.target.get())){
        s.Begin();void* layer=jalium_render_target_realize_layer_begin(s.target.get(),nullptr,0,0,96,80);Check(layer!=nullptr,"Retained mask layer");
        s.Capture(0,0,96,80);s.Rect(8,8,64,56,1,0,0);jalium_effect_end_capture(s.target.get());s.Mask(mask);
        jalium_render_target_realize_layer_end(s.target.get(),layer);jalium_render_target_composite_layer(s.target.get(),layer,0,0,96,80,.5f);
        pixels=s.End();s.Pixel(pixels,32,32,255,191,191);s.Pixel(pixels,4,4,255,255,255);jalium_render_target_destroy_retained_layer(s.target.get(),layer);
    }
}
}
int main(){@autoreleasepool{try{SamplerChecks();[NSApplication sharedApplication];for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE})for(float dpi:{1.f,2.f})for(int engine:{1,2}){Surface s(backend,dpi,engine);Run(s);}std::printf("Alpha mask compositor: %d checks\n",checks);return 0;}catch(const std::exception& e){std::fprintf(stderr,"FAIL %s at %d\n",e.what(),checks);return 1;}}}
