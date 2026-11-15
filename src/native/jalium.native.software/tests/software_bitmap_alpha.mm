#include "jalium_api.h"
#import <AppKit/AppKit.h>
#include <array>
#include <cstdio>
#include <stdexcept>
#include <vector>

namespace {
void Check(bool value,const char* message) {if(!value)throw std::runtime_error(message);}
int Scene(JaliumContext* context,JaliumRenderTarget* target,int dpi,bool affine,int mode,float opacity,
    const std::array<uint8_t,4>& transparent) {
    const std::array<uint8_t,8> source={0,0,255,255,transparent[0],transparent[1],transparent[2],transparent[3]};
    auto* bitmap=jalium_bitmap_create_from_pixels(context,source.data(),2,1,8);
    Check(bitmap!=nullptr,"Bitmap creation");
    Check(jalium_render_target_request_readback(target)==JALIUM_OK,"Readback request");
    Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"Begin frame");
    jalium_render_target_clear(target,1,1,1,1);
    const float m[]={.8f,.2f,-.2f,.8f,24,2};if(affine)jalium_push_transform(target,m);
    jalium_draw_bitmap_ex(target,bitmap,10,10,80,40,opacity,mode);
    if(affine)jalium_pop_transform(target);
    Check(jalium_render_target_end_draw(target)==JALIUM_OK,"End frame");
    const int size=128*dpi;int32_t width,height;std::vector<uint8_t> pixels(size*size*4);
    Check(jalium_render_target_fetch_readback(target,pixels.data(),size*4,&width,&height)==JALIUM_OK,"Fetch BGRA");
    int ink=0,bad=0;
    for(size_t i=0;i<pixels.size();i+=4) {
        // Only red contributes nonzero alpha. White composited with red keeps
        // its red channel, regardless of RGB hidden in transparent texels.
        if(pixels[i+1]<250){++ink;bad+=pixels[i+2]<254;}
    }
    jalium_bitmap_destroy(bitmap);
    if(bad)std::fprintf(stderr,"Bitmap alpha: DPI=%d affine=%d mode=%d opacity=%.2f, %d dark edge pixels\n",dpi,affine,mode,opacity,bad);
    Check(ink>100,"Bitmap has no visible pixels");Check(bad==0,"Transparent RGB polluted filtered bitmap colors");
    return ink;
}
void NearIntegerDestination(JaliumContext* context,JaliumRenderTarget* target,int dpi,int axis,int mode) {
    const int bw=56*dpi,bh=36*dpi,size=128*dpi;
    std::vector<uint8_t> source(bw*bh*4);
    for(size_t i=0;i<source.size();i+=4){source[i+2]=255;source[i+3]=255;}
    auto* bitmap=jalium_bitmap_create_from_pixels(context,source.data(),bw,bh,bw*4);
    Check(bitmap!=nullptr,"Roundoff bitmap");
    Check(jalium_render_target_request_readback(target)==JALIUM_OK,"Roundoff readback");
    Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"Roundoff begin");
    jalium_render_target_clear(target,0,0,0,0);
    const float below=0.99999988f;
    const float matrix[]={axis!=1?below:1.f,0,0,axis!=0?below:1.f,12,12};
    if(axis==2){
        const float outer[]={1,0,0,1,12,12};const float shape[]={2,.4f,.7f,1.5f,8,5};
        const float inverse[]={static_cast<float>(1.5/2.72),static_cast<float>(-.4/2.72),static_cast<float>(-.7/2.72),static_cast<float>(2/2.72),-3.125f,-2.5f};
        jalium_push_transform(target,outer);jalium_push_transform(target,shape);jalium_push_transform(target,inverse);
    }else jalium_push_transform(target,matrix);
    jalium_draw_bitmap_ex(target,bitmap,21,17,56,36,1,mode);
    jalium_pop_transform(target);if(axis==2){jalium_pop_transform(target);jalium_pop_transform(target);}
    Check(jalium_render_target_end_draw(target)==JALIUM_OK,"Roundoff end");
    std::vector<uint8_t> pixels(size*size*4);int32_t width,height;
    Check(jalium_render_target_fetch_readback(target,pixels.data(),size*4,&width,&height)==JALIUM_OK,"Roundoff pixels");
    int bad=0;
    for(int y=0;y<size;++y)for(int x=0;x<size;++x){const auto at=(y*size+x)*4;
        const int alpha=x>=33*dpi&&x<89*dpi&&y>=29*dpi&&y<65*dpi?255:0;
        bad+=pixels[at+3]!=alpha || (alpha&&pixels[at+2]!=255);
    }
    jalium_bitmap_destroy(bitmap);
    if(bad)std::fprintf(stderr,"Near-integer destination: DPI=%d axis=%d mode=%d bad=%d\n",dpi,axis,mode,bad);
    Check(bad==0,"Pixel-aligned bitmap lost rows at floating-point roundoff");
}
}
int main() {@autoreleasepool {try {
    [NSApplication sharedApplication];int scenes=0,pixels=0;
    auto* context=jalium_context_create(JALIUM_BACKEND_SOFTWARE);Check(context!=nullptr,"Software context");
    NSView* view=[[NSView alloc]initWithFrame:NSMakeRect(0,0,128,128)];
    JaliumSurfaceDescriptor descriptor{};descriptor.platform=JALIUM_PLATFORM_MACOS;descriptor.kind=JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    descriptor.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
    const std::array<std::array<uint8_t,4>,3> hidden={{{0,0,0,0},{0,0,255,0},{255,255,0,0}}};
    for(int dpi:{1,2}) {
        auto* target=jalium_render_target_create_for_composition_surface(context,&descriptor,128*dpi,128*dpi);
        Check(target!=nullptr,"Software composition target");jalium_render_target_set_dpi(target,96*dpi,96*dpi);
        for(bool affine:{false,true})for(int mode:{0,3,6})for(float opacity:{1.f,.5f})for(const auto& color:hidden)
            {pixels+=Scene(context,target,dpi,affine,mode,opacity,color);++scenes;}
        for(int axis:{0,1,2})for(int mode:{3,6}){NearIntegerDestination(context,target,dpi,axis,mode);++scenes;}
        jalium_render_target_destroy(target);
    }
    jalium_context_destroy(context);std::printf("PASS bitmap alpha: %d cached/affine/DPI/filter/opacity scenes, %d visible pixels\n",scenes,pixels);return 0;
} catch(const std::exception& error) {std::fprintf(stderr,"FAIL %s\n",error.what());return 1;}}}
