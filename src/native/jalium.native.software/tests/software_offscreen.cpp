#include "jalium_api.h"
#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <stdexcept>
#include <vector>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
const wchar_t* U(const char16_t* text){return reinterpret_cast<const wchar_t*>(text);}
void BackendEnvironment(const char* value){
#ifdef _WIN32
    _putenv_s("JALIUM_RENDER_BACKEND",value);
#else
    setenv("JALIUM_RENDER_BACKEND",value,1);
#endif
}
}
int main(){
    int count=0;
    try {
        // A private glyph rasterizer must ignore the window's backend override.
        BackendEnvironment("metal");
        auto* context=jalium_context_create_rasterizer();Check(context,"Private rasterizer context");
        Check(jalium_context_get_backend(context)==JALIUM_BACKEND_SOFTWARE,"Actual rasterizer backend");
        Check(!jalium_render_target_create_offscreen(context,0,32),"Zero size rejected");++count;
        Check(!jalium_render_target_create_offscreen(context,16385,32),"Oversize rejected");++count;
        Check(!jalium_render_target_create_offscreen(context,16384,16384),"Pixel budget enforced");++count;
        for(int dpi:{1,2})for(float size:{14.f,28.f,40.f}){
            const int width=192*dpi,height=96*dpi;
            auto* target=jalium_render_target_create_offscreen(context,width,height);Check(target,"Headless target");
            auto* font=jalium_text_format_create(context,U(u"Georgia"),size,400,0);
            auto* brush=jalium_brush_create_solid(context,1,1,1,1);Check(font&&brush,"Native text inputs");
            jalium_render_target_set_dpi(target,96*dpi,96*dpi);
            Check(jalium_render_target_request_readback(target)==JALIUM_OK,"Readback request");
            Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"Begin headless frame");
            jalium_render_target_clear(target,0,0,0,0);
            constexpr char16_t text[]=u"CSS A\u0301 \u4e2d\u6587";
            jalium_draw_text(target,U(text),sizeof(text)/sizeof(char16_t)-1,font,4,5,180,85,brush);
            Check(jalium_render_target_end_draw(target)==JALIUM_OK,"End without presentation surface");
            std::vector<uint8_t> pixels(size_t(width)*height*4);int32_t w=0,h=0;
            Check(jalium_render_target_fetch_readback(target,pixels.data(),width*4,&w,&h)==JALIUM_OK&&w==width&&h==height,"Headless readback");
            int ink=0,transparent=0;
            for(size_t i=3;i<pixels.size();i+=4){ink+=pixels[i]!=0;transparent+=pixels[i]==0;}
            Check(ink>30&&transparent>100,"Real glyph coverage, not an empty or filled rectangle");
            jalium_brush_destroy(brush);jalium_text_format_destroy(font);jalium_render_target_destroy(target);++count;
        }
        jalium_context_destroy(context);
        std::printf("{\"count\":%d,\"failed\":0,\"headless\":true,\"environmentOverrideIgnored\":true}\n",count);return 0;
    }catch(const std::exception& error){std::fprintf(stderr,"Offscreen rasterizer: %s\n",error.what());return 1;}
}
