#include "jalium_api.h"
#include <algorithm>
#include <cmath>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>
#import <CoreText/CoreText.h>

extern "C" void jalium_metal_init();
extern "C" {
JaliumInkLayerBitmap* jalium_ink_layer_bitmap_create(JaliumContext*,int32_t,int32_t);
void jalium_ink_layer_bitmap_destroy(JaliumInkLayerBitmap*);
void jalium_ink_layer_bitmap_clear(JaliumInkLayerBitmap*,float,float,float,float);
JaliumBrushShader* jalium_brush_shader_create(JaliumContext*,const char*,const char*,int32_t);
void jalium_brush_shader_destroy(JaliumBrushShader*);
int32_t jalium_ink_layer_bitmap_dispatch_brush(JaliumInkLayerBitmap*,JaliumBrushShader*,const void*,
    int32_t,const void*,const void*,int32_t);
void jalium_render_target_blit_ink_layer(JaliumRenderTarget*,JaliumInkLayerBitmap*,float,float,float);
}
static void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
struct Surface {
    std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context;
    NSView* view;
    std::unique_ptr<JaliumRenderTarget,decltype(&jalium_render_target_destroy)> target;
    int width,height;float scale;
    Surface(float dpi,uint32_t msaa,int w=128,int h=96)
        : context(jalium_context_create(JALIUM_BACKEND_METAL),jalium_context_destroy),
          view([[NSView alloc] initWithFrame:NSMakeRect(0,0,w,h)]),
          target(nullptr,jalium_render_target_destroy),width(std::lround(w*dpi)),
          height(std::lround(h*dpi)),scale(dpi)
    {
        Check(context!=nullptr,"Metal context failed");JaliumSurfaceDescriptor d{};
        d.platform=JALIUM_PLATFORM_MACOS;d.kind=JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        d.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(),&d,width,height));
        Check(target!=nullptr,"Metal effect target failed");
        jalium_render_target_set_dpi(target.get(),96*dpi,96*dpi);
        jalium_render_target_set_path_msaa(target.get(),msaa);
    }
    JaliumRenderTarget* Rt(){return target.get();}
    void Begin(bool readback=true){if(readback)Check(jalium_render_target_request_readback(Rt())==JALIUM_OK,"readback request failed");
        Check(jalium_render_target_begin_draw(Rt())==JALIUM_OK,"BeginDraw failed");jalium_render_target_clear(Rt(),0,0,0,0);}
    std::vector<uint8_t> End(){Check(jalium_render_target_end_draw(Rt())==JALIUM_OK,"EndDraw failed");
        std::vector<uint8_t> data(width*height*4);int32_t w=0,h=0;
        Check(jalium_render_target_fetch_readback(Rt(),data.data(),width*4,&w,&h)==JALIUM_OK&&w==width&&h==height,
            "effect readback failed");return data;}
    const uint8_t* Pixel(const std::vector<uint8_t>& data,float x,float y){
        return data.data()+(static_cast<int>(y*scale)*width+static_cast<int>(x*scale))*4;}
    void Rect(float x,float y,float w,float h,float r,float g,float b,float a=1){
        auto* brush=jalium_brush_create_solid(context.get(),r,g,b,a);
        jalium_draw_fill_rectangle(Rt(),x,y,w,h,brush);jalium_brush_destroy(brush);}
    void Capture(float r=1,float g=0,float b=0,float a=1){jalium_effect_begin_capture(Rt(),16,16,96,64);
        Rect(32,32,48,32,r,g,b,a);jalium_effect_end_capture(Rt());}
};
static JaliumEllipticalRectClip Contour(float x=32,float y=24,float w=64,float h=48){
    JaliumEllipticalRectClip c{};c.structSize=sizeof(c);c.edges=15;c.x=x;c.y=y;c.width=w;c.height=h;
    for(int i=0;i<4;++i){c.radiusX[i]=16;c.radiusY[i]=8;}return c;}
static const char* kIdentity=R"HLSL(
Texture2D image : register(t0); SamplerState imageSampler : register(s0);
float4 main(float2 uv : TEXCOORD0) : SV_Target { return image.Sample(imageSampler,uv); })HLSL";
static const char* kInvert=R"HLSL(
Texture2D image : register(t0); SamplerState imageSampler : register(s0);
cbuffer Constants : register(b0) { float4 amount; }
float4 main(float2 uv : TEXCOORD0) : SV_Target { float4 c=image.Sample(imageSampler,uv);
return float4(lerp(c.rgb,c.a-c.rgb,amount.x),c.a); })HLSL";
static void Shader(Surface& s,const char* source,const float* values=nullptr,uint32_t count=0,
    float x=16,float y=16,float w=96,float h=64){
    jalium_draw_shader_effect_hlsl(s.Rt(),x,y,w,h,source,values,count);
    Check(jalium_render_target_get_last_shader_effect_result(s.Rt())==JALIUM_OK,"runtime HLSL shader did not compile/render");}
static int Differences(const std::vector<uint8_t>& a,const std::vector<uint8_t>& b,int tolerance=2){
    int count=0;for(size_t i=0;i<a.size();++i)if(std::abs(int(a[i])-int(b[i]))>tolerance)++count;return count;}

static void TestCustom(float scale,uint32_t msaa){
    Surface s(scale,msaa);s.Begin();s.Capture(1,0,0,0.5);float amount[]={1};Shader(s,kInvert,amount,1);
    auto data=s.End();auto c=s.Pixel(data,48,48);
    Check(c[0]>=126&&c[1]>=126&&c[2]<=2&&c[3]>=126&&c[3]<=129,"custom shader constants/alpha are incorrect");
    s.Begin();s.Capture();Shader(s,R"HLSL(
float4 main(float2 uv : TEXCOORD0) : SV_Target { return float4(uv,0,1); })HLSL");
    data=s.End();c=s.Pixel(data,64,48);Check(c[2]>120&&c[2]<140&&c[1]>120&&c[1]<140,
        "custom shader receives screen UVs instead of capture-local UVs");
    s.Begin();s.Capture();Shader(s,R"HLSL(
cbuffer Constants : register(b0) { float4 registers[300]; }
float4 main(float2 uv : TEXCOORD0) : SV_Target { return float4(registers[299].xyz,1); })HLSL",amount,1);
    data=s.End();c=s.Pixel(data,64,48);Check(c[0]+c[1]+c[2]==0&&c[3]==255,"reflected constants were not zero-filled");
    s.Begin();s.Capture();jalium_draw_shader_effect_hlsl(s.Rt(),16,16,96,64,"invalid HLSL",nullptr,0);
    Check(jalium_render_target_get_last_shader_effect_result(s.Rt())!=JALIUM_OK,"invalid HLSL falsely reports success");
    data=s.End();Check(s.Pixel(data,48,48)[2]==255,"invalid shader lost captured content");
    s.Begin();s.Capture();jalium_draw_shader_effect_hlsl(s.Rt(),16,16,96,64,R"HLSL(
cbuffer Unsupported : register(b1) { float4 value; }
float4 main(float2 uv : TEXCOORD0) : SV_Target { return value; })HLSL",amount,1);
    Check(jalium_render_target_get_last_shader_effect_result(s.Rt())!=JALIUM_OK,"unsupported binding was accepted");s.End();
}
static void TestBrushShader(float scale,uint32_t msaa){
    Surface s(scale,msaa);
    std::unique_ptr<JaliumInkLayerBitmap,decltype(&jalium_ink_layer_bitmap_destroy)> layer(
        jalium_ink_layer_bitmap_create(s.context.get(),128,96),jalium_ink_layer_bitmap_destroy);
    Check(layer!=nullptr,"GPU ink layer failed");
    const char* source=R"HLSL(
cbuffer Extra : register(b1) { float4 color; }
float4 BrushMain(float2 pixel) {
    float2 distance=SdfPolyline(pixel);
    float coverage=saturate(StrokeWidth*0.5-distance.x+0.5);
    return color*coverage;
})HLSL";
    std::unique_ptr<JaliumBrushShader,decltype(&jalium_brush_shader_destroy)> shader(
        jalium_brush_shader_create(s.context.get(),"metal-effects-test",source,0),jalium_brush_shader_destroy);
    Check(shader!=nullptr,"custom BrushMain shader failed");
    struct Constants {float color[4],width,height,time;uint32_t seed;
        float minimum[2],maximum[2];uint32_t pointCount,taper,ignorePressure,fit;float viewport[2],pad[2];};
    static_assert(sizeof(Constants)==80);Constants c{};c.color[0]=c.color[3]=1;
    c.width=12;c.minimum[0]=c.minimum[1]=16;c.maximum[0]=112;c.maximum[1]=80;c.ignorePressure=1;
    const float points[]={24,48,1,0,104,48,1,0};const float color[]={0,0.75,0.25,1};
    jalium_ink_layer_bitmap_clear(layer.get(),0,0,0,0);
    Check(jalium_ink_layer_bitmap_dispatch_brush(layer.get(),shader.get(),points,2,&c,color,sizeof(color))==0,
        "custom brush dispatch failed");
    s.Begin();jalium_render_target_blit_ink_layer(s.Rt(),layer.get(),0,0,1);auto data=s.End();auto pixel=s.Pixel(data,64,48);
    Check(pixel[1]>180&&pixel[0]>60&&pixel[2]<2&&pixel[3]==255,
        "custom BrushMain/b1 used the fallback pen instead of authored HLSL");
    Check(s.Pixel(data,64,24)[3]==0,"custom brush t0 polyline buffer is incorrect");
}
static void TestClipOpacity(float scale,uint32_t msaa){
    Surface s(scale,msaa);auto paint=[&](bool effect,int depth){s.Begin();jalium_push_opacity(s.Rt(),0.5);
        for(int i=0;i<depth;++i)jalium_push_rounded_rect_clip(s.Rt(),24.25,20.25,64,56,12,8);
        if(effect)jalium_effect_begin_capture(s.Rt(),16,16,96,64);s.Rect(16,16,96,64,1,0,0,0.5);
        if(effect){jalium_effect_end_capture(s.Rt());Shader(s,kIdentity);}
        for(int i=0;i<depth;++i)jalium_pop_clip(s.Rt());jalium_pop_opacity(s.Rt());return s.End();};
    for(int depth:{1,12,22}){auto ordinary=paint(false,depth),effect=paint(true,depth);
        Check(Differences(ordinary,effect,2)==0,"shader changes nested clip coverage or applies opacity twice");
        Check(std::abs(int(s.Pixel(effect,48,40)[3])-64)<=1,"effect opacity is not applied exactly once");}
}
static void TestTransformedInput(float scale,uint32_t msaa){
    Surface s(scale,msaa);const float transform[]={0.8660254,0.5,-0.5,0.8660254,22.5744,-25.5692};
    auto paint=[&](bool effect){s.Begin();jalium_push_transform(s.Rt(),transform);
        if(effect)jalium_effect_begin_capture(s.Rt(),16.25,16.25,95.5,63.5);
        s.Rect(32,32,24,32,1,0,0);s.Rect(56,32,24,32,0,0,1);
        if(effect){jalium_effect_end_capture(s.Rt());Shader(s,kIdentity,nullptr,0,16.25,16.25,95.5,63.5);}
        jalium_pop_transform(s.Rt());return s.End();};
    auto original=paint(false),effect=paint(true);int checked=0;
    for(size_t i=0;i<original.size();i+=4)if(original[i+3]==255&&effect[i+3]==255){
        ++checked;for(int channel=0;channel<3;++channel)Check(std::abs(int(original[i+channel])-int(effect[i+channel]))<=2,
            "custom HLSL rotates/rescales screen capture or shifts fractional input");}
    Check(checked>800*scale*scale,"transformed shader lost captured geometry");
}
static void TestMatricesEmboss(float scale,uint32_t msaa){
    Surface s(scale,msaa);float matrix[20]={1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,1,0};
    s.Begin();s.Capture(0.75,0.25,0.5,0.5);jalium_draw_color_matrix_effect(s.Rt(),16,16,96,64,matrix);
    auto data=s.End();auto c=s.Pixel(data,48,48);Check(std::abs(int(c[2])-96)<=2&&std::abs(int(c[3])-128)<=1,
        "color matrix alpha parity failed");
    float chain[40]={};for(int j=0;j<2;++j){for(int i=0;i<3;++i)chain[j*20+i*5]=j?0.5f:2.0f;chain[j*20+15]=1;}
    s.Begin();s.Capture(0.75,0,0);Check(jalium_draw_color_matrix_chain_effect(s.Rt(),16,16,96,64,chain,2)==JALIUM_OK,
        "ordered color matrix extension missing");data=s.End();
    Check(std::abs(int(s.Pixel(data,48,48)[2])-128)<=2,"matrix chain fused stages before clamping");
    s.Begin();s.Capture(1,0,0,0.5);jalium_draw_emboss_effect(s.Rt(),16,16,96,64,0,4,3,2);
    data=s.End();c=s.Pixel(data,48,48);Check(std::abs(int(c[2])-64)<=2&&std::abs(int(c[3])-128)<=1,"emboss lost source alpha");
}
static void TestShadows(float scale,uint32_t msaa){
    Surface s(scale,msaa);s.Begin();s.Capture();jalium_draw_blur_effect(s.Rt(),16,16,96,64,12,0,0);
    auto data=s.End();Check(s.Pixel(data,29,48)[3]>10&&s.Pixel(data,48,48)[3]>230,"Gaussian blur does not spread alpha");
    auto gaussian=data;s.Begin();s.Capture();
    Check(jalium_draw_blur_effect_kernel(s.Rt(),16,16,96,64,12,1,0,0)==JALIUM_OK,
        "element Box blur extension missing");data=s.End();
    Check(Differences(gaussian,data)>100&&s.Pixel(data,48,48)[3]>150,
        "element Box blur uses the Gaussian kernel or loses captured alpha");
    s.Begin();s.Capture();Check(jalium_draw_blur_effect_kernel(s.Rt(),16,16,96,64,12,2,0,0)==
        JALIUM_ERROR_INVALID_ARGUMENT,"element blur accepts an invalid kernel");s.End();
    s.Begin();s.Capture();jalium_draw_outer_glow_effect(s.Rt(),32,32,48,32,12,0,1,0,1,2,16,16,0,0,0,0);
    data=s.End();Check(s.Pixel(data,29,48)[1]>20&&s.Pixel(data,48,48)[2]>250,"outer glow lost glow or source");
    s.Begin();jalium_effect_begin_capture(s.Rt(),16,16,96,64);s.Rect(24,32,12,32,1,1,1);s.Rect(64,32,12,32,1,1,1);
    jalium_effect_end_capture(s.Rt());Check(jalium_draw_filter_drop_shadow_effect(s.Rt(),24,32,52,32,16,16,96,64,
        0,8,0,0,0,1,1)==JALIUM_OK,"alpha-mask shadow extension missing");data=s.End();
    Check(s.Pixel(data,40,48)[0]>250&&s.Pixel(data,52,48)[3]==0,"filter shadow uses a box instead of input alpha");
    auto contour=Contour(),spread=Contour(24,16,80,64);s.Begin();jalium_effect_begin_capture(s.Rt(),16,8,96,80);
    jalium_effect_end_capture(s.Rt());Check(jalium_draw_css_box_shadow_effect_elliptical(s.Rt(),32,24,64,48,6,0,0,1,0,0,1,
        16,16,&contour,&spread)==JALIUM_OK,"CSS spread/knockout extension missing");data=s.End();
    Check(s.Pixel(data,64,48)[3]==0&&s.Pixel(data,28,48)[2]>80,"CSS outer shadow fills its transparent border box");
    s.Begin();jalium_effect_begin_capture(s.Rt(),32,24,64,48);s.Rect(32,24,64,48,1,1,1,0.5);jalium_effect_end_capture(s.Rt());
    Check(jalium_draw_inner_shadow_effect_elliptical(s.Rt(),32,24,64,48,6,4,0,2,0,0,0,1,0,0,&contour)==JALIUM_OK,
        "elliptical inset extension missing");data=s.End();Check(s.Pixel(data,34,48)[2]<s.Pixel(data,64,48)[2]&&
        s.Pixel(data,28,48)[3]==0,"inset direction/spread/contour failed");
    s.Begin();jalium_effect_begin_capture(s.Rt(),16,8,96,80);s.Rect(32,24,64,48,1,1,1,0.25);jalium_effect_end_capture(s.Rt());
    jalium_draw_inner_shadow_effect_elliptical(s.Rt(),32,24,64,48,3,3,0,0,0,0,0,0.5,16,16,&contour);
    jalium_draw_inner_shadow_layer_elliptical(s.Rt(),32,24,64,48,3,-3,0,0,0,0,1,0.5,16,16,&contour);
    data=s.End();Check(std::abs(int(s.Pixel(data,64,48)[3])-64)<=1,"later inset composites source twice");
    s.Begin();s.Capture();const float layers[]={0,28,0,1,0,0,0.8, 0,28,0,0,0,1,0.8};
    Check(jalium_draw_css_text_shadows_only(s.Rt(),32,32,48,32,16,16,96,64,layers,2)==JALIUM_OK,
        "text shadow extension missing");data=s.End();Check(s.Pixel(data,90,48)[2]>s.Pixel(data,90,48)[0]&&
        s.Pixel(data,40,48)[3]==0,"text shadow order/isolation failed");
    // Layer providers also work before any effect capture. They must paint
    // only their shadow and leave the transparent center untouched.
    s.Begin();
    Check(jalium_paint_css_outer_shadow_layer_elliptical(s.Rt(),32,24,64,48,
        6,0,0,1,0,0,1,&contour,&spread)==JALIUM_OK,"standalone CSS outer layer failed");
    Check(jalium_paint_css_inner_shadow_layer_elliptical(s.Rt(),32,24,64,48,
        6,4,0,2,0,0,1,1,&contour)==JALIUM_OK,"standalone CSS inner layer failed");
    data=s.End();Check(s.Pixel(data,28,48)[2]>80&&s.Pixel(data,34,48)[0]>40&&
        s.Pixel(data,64,48)[3]<5,"standalone CSS layers composite content or ignore knockout");
}
static void TestNestedAndDirty(float scale,uint32_t msaa){
    Surface s(scale,msaa);s.Begin();jalium_effect_begin_capture(s.Rt(),16,16,96,64);s.Rect(24,24,16,16,0,0,1);
    jalium_effect_begin_capture(s.Rt(),48,32,32,32);s.Rect(48,32,32,32,1,0,0);jalium_effect_end_capture(s.Rt());
    Shader(s,kIdentity,nullptr,0,48,32,32,32);jalium_effect_end_capture(s.Rt());Shader(s,kIdentity);auto data=s.End();
    Check(s.Pixel(data,28,28)[0]==255&&s.Pixel(data,60,44)[2]==255,"nested capture lost parent or child");
    auto paint=[&]{s.Begin();s.Capture();jalium_draw_blur_effect(s.Rt(),16,16,96,64,9,0,0);return s.End();};
    auto reference=paint();jalium_render_target_add_dirty_rect(s.Rt(),24.25,24.25,12.5,12.5);auto partial=paint();
    Check(Differences(reference,partial,1)==0,"partial redraw clips blur input or damages other pixels");
    jalium_render_target_add_dirty_rect(s.Rt(),25.25,24.25,9.5,9.5);
    jalium_render_target_add_dirty_rect(s.Rt(),63.25,48.25,9.5,9.5);
    partial=paint();Check(Differences(reference,partial,1)==0,"disjoint damage accumulates translucent effects between rects");
}
static JaliumBackdropMaterialDesc Material(){
    JaliumBackdropMaterialDesc d{};d.structSize=sizeof(d);d.x=24;d.y=24;d.width=80;d.height=48;
    d.brightness=d.contrast=d.saturation=d.luminosity=d.opacity=1;
    d.cornerRadiusTL=d.cornerRadiusTR=d.cornerRadiusBR=d.cornerRadiusBL=12;return d;}
static void PaintPattern(Surface& s){
    s.Rect(0,0,128,96,0.12,0.12,0.18);
    for(int x=0;x<128;x+=8)s.Rect(x,0,4,96,0.8,x/128.0f,0.3);
    s.Rect(0,42,128,5,0.1,0.8,0.9);
}
static void Glass(Surface& s,float refraction=24,float chromatic=0.5,float blur=3,
    float lightX=-1,int shape=0,float exponent=4,int count=0,float fusion=20,const float* neighbors=nullptr){
    jalium_draw_liquid_glass(s.Rt(),24,24,80,48,12,blur,refraction,chromatic,
        0.4,0.6,1,0.08,lightX,24,0.8,shape,exponent,count,fusion,neighbors);}
static void TestGlassBackdrop(float scale,uint32_t msaa){
    Surface s(scale,msaa);
    auto paint=[&](float refraction,float chromatic,float blur,float light,int shape=0){
        s.Begin();PaintPattern(s);Glass(s,refraction,chromatic,blur,light,shape);return s.End();};
    auto ordinary=paint(0,0,0,-1),refracted=paint(36,0,0,-1),dispersed=paint(36,1,0,-1);
    Check(Differences(ordinary,refracted)>200,"glass refraction is inactive");
    Check(Differences(refracted,dispersed)>100,"glass seven-color dispersion is inactive");
    auto blurred=paint(0,0,9,-1),lit=paint(0,0,9,24),super=paint(0,0,9,-1,1);
    Check(Differences(ordinary,blurred)>200,"glass background blur is inactive");
    Check(Differences(blurred,lit)>40,"glass pointer light is inactive");
    Check(Differences(blurred,super)>40,"glass continuous corner shape is inactive");
    s.Begin();jalium_draw_liquid_glass(s.Rt(),24,24,80,48,12,0,0,0,1,0,0,1,-1,-1,0,1,4,0,0,nullptr);
    auto tinted=s.End();auto center=s.Pixel(tinted,64,48);
    Check(center[2]>250&&center[3]==255,"glass tint/alpha depend on captured background alpha");
    const float neighbor[]={64,24,40,48,12};
    s.Begin();jalium_draw_liquid_glass(s.Rt(),16,24,40,48,12,0,0,0,1,0,0,1,-1,-1,0,0,4,1,32,neighbor);
    auto fused=s.End();Check(s.Pixel(fused,59,48)[3]>200&&s.Pixel(fused,68,48)[3]==0,
        "glass bridge/neighbor ownership failed");
    s.Begin();s.Rect(0,0,128,96,1,0,0);auto d=Material();d.invert=1;
    jalium_draw_backdrop_material(s.Rt(),&d);auto backdrop=s.End();center=s.Pixel(backdrop,64,48);
    Check(center[0]>250&&center[1]>250&&center[2]<3,"backdrop inverse color filter failed");
    Check(s.Pixel(backdrop,24,24)[2]>250,"backdrop per-corner clipping failed");
    s.Begin();s.Rect(0,0,128,96,0,0,1);d=Material();d.tintR=1;d.tintA=1;d.opacity=0.5;
    jalium_draw_backdrop_material(s.Rt(),&d);backdrop=s.End();center=s.Pixel(backdrop,64,48);
    Check(std::abs(int(center[2])-128)<=1&&std::abs(int(center[0])-128)<=1&&center[3]==255,
        "backdrop effect opacity did not mix with background");
    std::vector<uint8_t> kernels[3];for(int type=0;type<3;++type){s.Begin();PaintPattern(s);d=Material();
        d.blurRadius=9;d.blurType=type;d.noiseIntensity=type==2?0.08:0;
        jalium_draw_backdrop_material(s.Rt(),&d);kernels[type]=s.End();}
    Check(Differences(kernels[0],kernels[1])>100&&Differences(kernels[0],kernels[2])>100,
        "backdrop box/frosted kernels are identical to Gaussian");
}
static void TestTransitions(float scale,uint32_t msaa){
    Surface s(scale,msaa);for(int mode=0;mode<10;++mode)for(float progress:{0.0f,0.5f,1.0f}){
        s.Begin();for(int slot=0;slot<2;++slot){jalium_transition_begin_capture(s.Rt(),slot,24,24,80,48);
            s.Rect(24,24,80,48,slot?0:1,0,slot?1:0,0.5);jalium_transition_end_capture(s.Rt(),slot);}
        jalium_draw_transition_shader(s.Rt(),24,24,80,48,progress,mode,10);auto data=s.End();auto c=s.Pixel(data,64,48);
        if(progress==0)Check(c[2]>=126&&c[0]==0&&std::abs(int(c[3])-128)<=1,"transition start differs from old content");
        else if(progress==1)Check(c[0]>=126&&c[2]==0&&std::abs(int(c[3])-128)<=1,"transition end differs from new content");
        else Check(c[3]>0,"transition midpoint is empty");Check(s.Pixel(data,24,24)[3]<20,"transition rounded clip failed");
    }
    for(int depth:{1,22}){
        s.Begin();jalium_push_opacity(s.Rt(),0.5);
        for(int i=0;i<depth;++i)jalium_push_rounded_rect_clip(s.Rt(),32,24,64,48,16,8);
        jalium_transition_begin_capture(s.Rt(),0,16,16,96,64);
        s.Rect(16,16,96,64,1,0,0,0.5);jalium_transition_end_capture(s.Rt(),0);
        jalium_draw_captured_transition(s.Rt(),0,24,24,80,48,0.4);
        for(int i=0;i<depth;++i)jalium_pop_clip(s.Rt());jalium_pop_opacity(s.Rt());
        auto data=s.End();auto center=s.Pixel(data,64,48);
        Check(std::abs(int(center[3])-26)<=1&&std::abs(int(center[2])-26)<=1,
            "captured transition applies capture opacity more than once");
        Check(s.Pixel(data,28,48)[3]==0&&s.Pixel(data,32,24)[3]<2,
            "captured transition ignores inherited clips or stencil fallback");
    }
}
static void TestHighlights(float scale,uint32_t msaa){
    Surface s(scale,msaa);auto paint=[&](float phase,float trail){s.Begin();jalium_draw_glowing_border_highlight(s.Rt(),32,24,64,48,
        phase,0,1,1,2,trail,0.5,128,96);return s.End();};auto a=paint(0.2,0.1),b=paint(0.7,0.7);
    Check(Differences(a,b)>80,"highlight phase/trail do not change ribbon");
    Check(s.Pixel(b,64,48)[3]==0&&s.Pixel(b,0,0)[3]>100,"highlight interior/dimming failed");
    s.Begin();jalium_draw_glowing_border_transition(s.Rt(),8,8,24,24,88,56,24,24,0.8,0.2,0.3,0,1,1,2,0.4,0,128,96);
    a=s.End();Check(s.Pixel(a,60,44)[3]>30,"highlight transition discarded head/tail");
    s.Begin();jalium_draw_ripple_effect(s.Rt(),24,16,80,64,0.5,0,1,1,2,0,128,96);a=s.End();
    s.Begin();jalium_draw_ripple_effect(s.Rt(),24,16,80,64,0.8,0,1,1,2,0,128,96);b=s.End();
    Check(Differences(a,b)>80,"ripple rings do not animate");
}
static void TestBlurLocality(float scale,uint32_t msaa){
    // A translation must not change blur output. The large target exercises
    // compact textures with a nonzero origin; the small one also exercises
    // physical texture borders, multipass halos, and transparent captures.
    Surface small(scale,msaa,384,256),large(scale,msaa,1024,768);
    const float dx=480,dy=320;
    for(int kind=0;kind<7;++kind){
        auto paint=[&](Surface& s,float tx,float ty){
            s.Begin();const float transform[]={1,0,0,1,tx,ty};jalium_push_transform(s.Rt(),transform);
            if(kind>=5){s.Rect(-1024,-768,2048,1536,0.1,0.2,0.3);
                for(int x=96;x<288;x+=12)s.Rect(x,64,6,128,0.8,0.4,0.2);
                if(kind==5){auto d=Material();d.x=96;d.y=64;d.width=192;d.height=128;
                    d.blurRadius=20;d.blurType=2;d.noiseIntensity=0;
                    jalium_draw_backdrop_material(s.Rt(),&d);
                }else jalium_draw_liquid_glass(s.Rt(),96,64,192,128,18,12,80,1,
                    0.4,0.6,1,0.08,-1,-1,0.8,1,4,0,20,nullptr);
            }else{
                jalium_effect_begin_capture(s.Rt(),96,64,192,128);
                s.Rect(112,80,160,96,0.8,0.2,0.1,0.6);
                jalium_effect_end_capture(s.Rt());
                if(kind<2)jalium_draw_blur_effect_kernel(s.Rt(),96,64,192,128,kind?25:50,kind,0,0);
                else if(kind==2)jalium_draw_outer_glow_effect(s.Rt(),112,80,160,96,
                    20,0.1,0.7,1,0.8,1,16,16,0,0,0,0);
                else if(kind==3){auto c=Contour(112,80,160,96);
                    jalium_draw_drop_shadow_effect_elliptical(s.Rt(),112,80,160,96,
                        18,-9,6,0,0,0,0.8,16,16,&c);
                }else{auto c=Contour(112,80,160,96);
                    jalium_draw_inner_shadow_effect_elliptical(s.Rt(),112,80,160,96,
                        18,9,-6,2,0,0,0,0.8,16,16,&c);}
            }
            return s.End();
        };
        auto reference=paint(small,0,0),translated=paint(large,dx,dy);
        int errors=0;
        // Backdrops depend on the surrounding image; compare their actual
        // painted outline. Alpha effects also compare their extended tails.
        int left=kind>=5?96:0,top=kind>=5?64:0;
        int right=kind>=5?288:384,bottom=kind>=5?192:256;
        for(int y=std::lround(top*scale);y<std::lround(bottom*scale);++y)
            for(int x=std::lround(left*scale);x<std::lround(right*scale);++x){
                auto* a=reference.data()+(y*small.width+x)*4;
                auto* b=translated.data()+((y+std::lround(dy*scale))*large.width+x+std::lround(dx*scale))*4;
                for(int channel=0;channel<4;++channel)if(std::abs(int(a[channel])-int(b[channel]))>2)++errors;
            }
        if(errors)std::fprintf(stderr,"locality kind=%d errors=%d\n",kind,errors);
        Check(errors==0,"blur changes with target size or compact texture origin");
    }
}
static void Save(Surface& s,const std::vector<uint8_t>& pixels,NSString* path){
    CGColorSpaceRef space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGDataProviderRef provider=CGDataProviderCreateWithData(nullptr,pixels.data(),pixels.size(),nullptr);
    CGImageRef image=CGImageCreate(s.width,s.height,8,32,s.width*4,space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|kCGBitmapByteOrder32Little,provider,nullptr,false,kCGRenderingIntentDefault);
    auto destination=CGImageDestinationCreateWithURL((__bridge CFURLRef)[NSURL fileURLWithPath:path],CFSTR("public.png"),1,nullptr);
    Check(destination&&image,"PNG evidence creation failed");CGImageDestinationAddImage(destination,image,nullptr);
    Check(CGImageDestinationFinalize(destination),"PNG evidence write failed");
    CFRelease(destination);CGImageRelease(image);CGDataProviderRelease(provider);CGColorSpaceRelease(space);
}
static void Overview(NSString* directory,float scale){
    constexpr int columns=4,cellW=184,cellH=140;
    const char* labels[]={"Original","Gaussian blur","Drop shadow","Outer glow","Inset shadow",
        "Color matrix","Emboss","Custom HLSL","CSS spread shadow","Elliptical clip","Liquid glass",
        "Continuous glass","Frosted backdrop","CSS text shadows","Ordered matrix chain",
        "Dissolve","Pixelate","Glitch","Chromatic","Liquid","Wave","Wind","Ripple transition","Clock","Thermal",
        "Moving glow ribbon","Head / tail comet","Expanding ripples"};
    const int count=sizeof(labels)/sizeof(labels[0]),rows=(count+columns-1)/columns;
    Surface canvas(scale,4,40+columns*cellW,88+rows*cellH),tile(scale,4);
    std::vector<uint8_t> sheet(canvas.width*canvas.height*4);
    for(size_t i=0;i<sheet.size();i+=4){sheet[i]=28;sheet[i+1]=24;sheet[i+2]=20;sheet[i+3]=255;}
    auto contour=Contour();
    for(int index=0;index<count;++index){
        tile.Begin();tile.Rect(0,0,128,96,0.94,0.95,0.97);
        if(index>=15&&index<25){
            for(int slot=0;slot<2;++slot){jalium_transition_begin_capture(tile.Rt(),slot,24,24,80,48);
                tile.Rect(24,24,80,48,slot?0.08:0.9,slot?0.65:0.2,slot?0.85:0.3);
                tile.Rect(slot?72:32,32,16,32,1,1,1);jalium_transition_end_capture(tile.Rt(),slot);}
            jalium_draw_transition_shader(tile.Rt(),24,24,80,48,0.45,index-15,10);
        }else if(index==10||index==11||index==12){
            PaintPattern(tile);
            if(index==12){auto d=Material();d.blurRadius=8;d.blurType=2;d.tintR=0.7;d.tintG=0.8;
                d.tintB=1;d.tintA=0.25;d.noiseIntensity=0.04;jalium_draw_backdrop_material(tile.Rt(),&d);}
            else Glass(tile,24,0.5,3,32,index==11?1:0);
        }else if(index>=25){
            tile.Rect(0,0,128,96,0.07,0.08,0.12);
            if(index==25)jalium_draw_glowing_border_highlight(tile.Rt(),32,24,64,48,0.3,0.1,0.8,1,2,0.5,0,128,96);
            else if(index==26)jalium_draw_glowing_border_transition(tile.Rt(),8,8,24,24,88,56,24,24,
                0.8,0.2,0.3,0.1,0.8,1,2,0.5,0,128,96);
            else jalium_draw_ripple_effect(tile.Rt(),24,16,80,64,0.6,0.1,0.8,1,2,0,128,96);
        }else if(index==8){
            auto spread=Contour(24,16,80,64);jalium_effect_begin_capture(tile.Rt(),16,8,96,80);
            tile.Rect(32,24,64,48,1,1,1);jalium_effect_end_capture(tile.Rt());
            jalium_draw_css_box_shadow_effect_elliptical(tile.Rt(),32,24,64,48,7,3,3,0.1,0.2,0.5,0.8,
                16,16,&contour,&spread);
        }else{
            tile.Capture(0.9,0.2,0.3);
            switch(index){
            case 0:Shader(tile,kIdentity);break;
            case 1:jalium_draw_blur_effect(tile.Rt(),16,16,96,64,9,0,0);break;
            case 2:jalium_draw_drop_shadow_effect(tile.Rt(),32,32,48,32,8,7,7,0,0,0,0.7,16,16,0,0,0,0);break;
            case 3:jalium_draw_outer_glow_effect(tile.Rt(),32,32,48,32,15,0.1,0.6,1,1,2,16,16,0,0,0,0);break;
            case 4:{auto inner=Contour(32,32,48,32);for(int i=0;i<4;++i)inner.radiusX[i]=inner.radiusY[i]=0;
                Check(jalium_draw_inner_shadow_effect_elliptical(tile.Rt(),32,32,48,32,9,5,5,0,0,0,0,0.85,16,16,&inner)==JALIUM_OK,
                    "overview inset shadow failed");break;}
            case 5:{const float matrix[]={-1,0,0,1,0,0,-1,0,1,0,0,0,-1,1,0,0,0,0,1,0};
                jalium_draw_color_matrix_effect(tile.Rt(),16,16,96,64,matrix);break;}
            case 6:jalium_draw_emboss_effect(tile.Rt(),16,16,96,64,2,1,1,2);break;
            case 7:{const float amount=0.85;Shader(tile,kInvert,&amount,1);break;}
            case 9:jalium_effect_begin_capture(tile.Rt(),16,16,96,64);tile.Rect(16,16,96,64,0.9,0.2,0.3);
                jalium_effect_end_capture(tile.Rt());jalium_push_elliptical_rect_clip(tile.Rt(),&contour);
                Shader(tile,kIdentity);jalium_pop_clip(tile.Rt());break;
            case 13:{const float layers[]={4,14,0,0.1,0.3,0.9,0.7,2,-10,6,0.1,0.8,0.6,0.7};
                jalium_draw_css_text_shadows(tile.Rt(),32,32,48,32,16,16,96,64,layers,2);break;}
            case 14:{float chain[40]={};for(int stage=0;stage<2;++stage){
                    for(int i=0;i<3;++i)chain[stage*20+i*5]=stage?0.5f:2.0f;chain[stage*20+15]=1;}
                jalium_draw_color_matrix_chain_effect(tile.Rt(),16,16,96,64,chain,2);break;}
            }
        }
        auto pixels=tile.End();const int x=(20+(index%columns)*cellW)*scale,y=(88+(index/columns)*cellH)*scale;
        for(int row=0;row<tile.height;++row)std::memcpy(sheet.data()+((y+row)*canvas.width+x)*4,
            pixels.data()+row*tile.width*4,tile.width*4);
    }
    auto space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto context=CGBitmapContextCreate(sheet.data(),canvas.width,canvas.height,8,canvas.width*4,space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|kCGBitmapByteOrder32Little);
    Check(context!=nullptr,"overview text context failed");
    auto text=[&](NSString* label,int x,int y,int size){
        auto font=CTFontCreateWithName(CFSTR("Helvetica"),size*scale,nullptr);
        auto string=[[NSAttributedString alloc] initWithString:label attributes:@{
            (__bridge NSString*)kCTFontAttributeName:(__bridge id)font,
            (__bridge NSString*)kCTForegroundColorAttributeName:(__bridge id)NSColor.whiteColor.CGColor}];
        auto line=CTLineCreateWithAttributedString((__bridge CFAttributedStringRef)string);
        CGContextSetTextPosition(context,x*scale,canvas.height-y*scale);CTLineDraw(line,context);
        CFRelease(line);CFRelease(font);
    };
    text(@"macOS Metal shader effects",20,32,22);
    text([NSString stringWithFormat:@"GPU readback · %.0fx DPI · 4x MSAA · runtime HLSL compiler",scale],20,55,12);
    for(int index=0;index<count;++index)text([NSString stringWithUTF8String:labels[index]],
        20+(index%columns)*cellW,80+(index/columns)*cellH,12);
    CGContextRelease(context);CGColorSpaceRelease(space);
    Save(canvas,sheet,[directory stringByAppendingPathComponent:
        [NSString stringWithFormat:@"metal-shader-effects-%.0fx.png",scale]]);
}
// Use the same physical size and AA as the Gallery, rather than the tiny
// parity fixtures. Readback is excluded from timed frames; these are native
// render timings, not the managed Gallery's animation/presentation rate.
static void Benchmark(NSString* directory){
    Surface s(2,4,1500,920);
    constexpr int frames=12;
    const char* names[]={"control","gaussian-25","gaussian-50","box-25",
        "drop-shadow","outer-glow","inset-shadow","custom-hlsl","liquid-glass",
        "backdrop-20","transition"};
    if(directory)[NSFileManager.defaultManager createDirectoryAtPath:directory
        withIntermediateDirectories:YES attributes:nil error:nil];
    for(int kind=0;kind<11;++kind){
        auto paint=[&](int frame,bool readback){
            s.Begin(readback);s.Rect(0,0,1500,920,0.08,0.09,0.12);
            for(int x=480;x<940;x+=20)s.Rect(x,330,10,300,0.5,(x-480)/460.0f,0.3);
            const float value=0.3f+0.02f*(frame%12);
            if(kind==8)jalium_draw_liquid_glass(s.Rt(),600,420,200,100,18,8,24,0.5,
                0.4,0.6,1,0.08,630+frame,450,0.8,1,4,0,20,nullptr);
            else if(kind==9){auto d=Material();d.x=600;d.y=420;d.width=200;d.height=100;
                d.blurRadius=20;jalium_draw_backdrop_material(s.Rt(),&d);}
            else if(kind==10){for(int slot=0;slot<2;++slot){
                jalium_transition_begin_capture(s.Rt(),slot,600,420,200,100);
                s.Rect(600,420,200,100,slot?0.1:0.8,value,slot?0.8:0.2);
                jalium_transition_end_capture(s.Rt(),slot);}
                jalium_draw_transition_shader(s.Rt(),600,420,200,100,value,4,18);
            }else if(kind){
                jalium_effect_begin_capture(s.Rt(),550,370,300,200);
                s.Rect(600,420,200,100,0.8,value,0.2);
                s.Rect(620,440,20,60,1,1,1);
                jalium_effect_end_capture(s.Rt());
                if(kind<=3)jalium_draw_blur_effect_kernel(s.Rt(),550,370,300,200,
                    kind==2?50:25,kind==3?1:0,0,0);
                else if(kind==4)jalium_draw_drop_shadow_effect(s.Rt(),600,420,200,100,
                    15,8,8,0,0,0,0.8,50,50,0,0,0,0);
                else if(kind==5)jalium_draw_outer_glow_effect(s.Rt(),600,420,200,100,
                    20,0.1,0.7,1,0.8,1,50,50,0,0,0,0);
                else if(kind==6){auto c=Contour(600,420,200,100);
                    jalium_draw_inner_shadow_effect_elliptical(s.Rt(),600,420,200,100,
                        15,6,4,0,0,0,0,0.8,50,50,&c);}
                else Shader(s,kInvert,&value,1,550,370,300,200);
            }
            Check(jalium_render_target_end_draw(s.Rt())==JALIUM_OK,"benchmark EndDraw failed");
        };
        for(int frame=0;frame<3;++frame)paint(frame,false);
        Check(jalium_render_target_wait_for_completion(s.Rt())==JALIUM_OK,"benchmark warmup wait failed");
        double gpu=0;
        auto start=std::chrono::steady_clock::now();
        for(int frame=0;frame<frames;++frame){
            paint(frame,false);
            Check(jalium_render_target_wait_for_completion(s.Rt())==JALIUM_OK,"benchmark GPU wait failed");
            JaliumGpuTimingStats timing{};jalium_render_target_query_gpu_timing(s.Rt(),&timing);
            gpu+=timing.totalGpuNs/1e6;
        }
        double elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
        std::printf("EFFECT %-14s pixels=3000x1840 msaa=4 frames=%d frame_ms=%.3f gpu_ms=%.3f\n",
            names[kind],frames,elapsed/frames,gpu/frames);std::fflush(stdout);
        if(directory){paint(0,true);std::vector<uint8_t> pixels(s.width*s.height*4);int32_t w=0,h=0;
            Check(jalium_render_target_fetch_readback(s.Rt(),pixels.data(),s.width*4,&w,&h)==JALIUM_OK,
                "benchmark evidence readback failed");
            Save(s,pixels,[directory stringByAppendingPathComponent:
                [NSString stringWithFormat:@"%s.png",names[kind]]]);}
    }
}
int main(int argc,char** argv){
    @autoreleasepool{[NSApplication sharedApplication];jalium_metal_init();int failures=0;
        if(argc>1&&std::strcmp(argv[1],"--benchmark")==0){try{
            Benchmark(argc>2?[NSString stringWithUTF8String:argv[2]]:nil);return 0;
        }catch(const std::exception& e){std::fprintf(stderr,"FAIL benchmark: %s\n",e.what());return 1;}}
        for(float scale:{1.0f,2.0f})for(uint32_t msaa:{1u,4u}){
            auto run=[&](const char* name,auto test){try{test(scale,msaa);std::printf("PASS %s dpi=%.0f msaa=%u\n",name,scale,msaa);}
                catch(const std::exception& e){++failures;std::fprintf(stderr,"FAIL %s dpi=%.0f msaa=%u: %s\n",name,scale,msaa,e.what());}};
            run("custom HLSL/UV/constants/errors",TestCustom);run("nested clip/opacity",TestClipOpacity);
            run("custom BrushMain/extra constants",TestBrushShader);
            run("rotated/fractional shader input",TestTransformedInput);
            run("color matrices/emboss",TestMatricesEmboss);run("shadow providers/blur/glow",TestShadows);
            run("nested capture/partial redraw",TestNestedAndDirty);run("ten transition modes",TestTransitions);
            run("highlight/comet/ripples",TestHighlights);
            run("liquid glass/backdrop materials",TestGlassBackdrop);
            run("large target/compact blur locality",TestBlurLocality);
        }
        if(!failures&&argc>1){try{
            NSString* directory=[NSString stringWithUTF8String:argv[1]];
            [NSFileManager.defaultManager createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:nil error:nil];
            Overview(directory,1);Overview(directory,2);
        }catch(const std::exception& e){++failures;std::fprintf(stderr,"FAIL rendered evidence: %s\n",e.what());}}
        return failures?1:0;
    }
}
