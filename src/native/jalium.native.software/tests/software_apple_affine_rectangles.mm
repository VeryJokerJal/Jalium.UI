#include "jalium_api.h"
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#include <mach-o/dyld.h>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include <stdexcept>
#include <vector>

// Existing legacy export used by managed rounded clips; absent from the C header.
extern "C" JALIUM_API void jalium_push_per_corner_rounded_rect_clip(
    JaliumRenderTarget*,float,float,float,float,float,float,float,float);

namespace {
int checks=0,scenes=0;
void Check(bool value,const char* text){if(!value)throw std::runtime_error(text);++checks;}
struct Surface {
    JaliumContext* context=jalium_context_create(JALIUM_BACKEND_SOFTWARE);
    NSView* view=[[NSView alloc]initWithFrame:NSMakeRect(0,0,160,160)];
    JaliumRenderTarget* target=nullptr;
    explicit Surface(int scale){
        Check(context!=nullptr,"Software context");JaliumSurfaceDescriptor descriptor{};
        descriptor.platform=JALIUM_PLATFORM_MACOS;descriptor.kind=JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
        descriptor.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
        target=jalium_render_target_create_for_composition_surface(context,&descriptor,160*scale,160*scale);
        Check(target!=nullptr,"actual Software composition target");jalium_render_target_set_dpi(target,96*scale,96*scale);
    }
    ~Surface(){jalium_render_target_destroy(target);jalium_context_destroy(context);}
};
CGPathRef Shape(int kind){
    auto path=CGPathCreateMutable();CGRect rect=CGRectMake(0,0,70,30);
    if(kind==0)CGPathAddRect(path,nullptr,rect);
    else if(kind==1)CGPathAddRoundedRect(path,nullptr,rect,6,4);
    else {
        CGPathMoveToPoint(path,nullptr,15,0);CGPathAddLineToPoint(path,nullptr,63,0);
        CGPathAddArcToPoint(path,nullptr,70,0,70,7,7);CGPathAddLineToPoint(path,nullptr,70,20);
        CGPathAddArcToPoint(path,nullptr,70,30,60,30,10);CGPathAddLineToPoint(path,nullptr,2,30);
        CGPathAddArcToPoint(path,nullptr,0,30,0,28,2);CGPathAddLineToPoint(path,nullptr,0,15);
        CGPathAddArcToPoint(path,nullptr,0,0,15,0,15);CGPathCloseSubpath(path);
    }
    return path;
}
void Scene(Surface& surface,int scale,const std::array<float,6>& matrix,int mode){
    ++scenes;auto* target=surface.target;auto* brush=jalium_brush_create_solid(surface.context,.2f,.4f,.8f,1);
    Check(brush!=nullptr,"opaque reference brush");Check(jalium_render_target_request_readback(target)==JALIUM_OK,"request readback");
    Check(jalium_render_target_begin_draw(target)==JALIUM_OK,"begin frame");jalium_render_target_clear(target,0,0,0,0);
    jalium_push_clip(target,20,20,120,120);
    jalium_push_transform(target,matrix.data());int kind=mode==0||mode==5?0:mode==1||mode==3||mode==6?1:2;
    bool stroke=mode==3||mode==4;
    if(mode>=5){
        if(mode==5)jalium_push_clip(target,0,0,70,30);
        else if(mode==6)jalium_push_rounded_rect_clip(target,0,0,70,30,6,4);
        else jalium_push_per_corner_rounded_rect_clip(target,0,0,70,30,40,7,10,2);
        jalium_draw_fill_rectangle(target,-20,-20,110,70,brush);jalium_pop_clip(target);
    } else if(mode==0)jalium_draw_fill_rectangle(target,0,0,70,30,brush);
    else if(mode==1)jalium_draw_fill_rounded_rectangle(target,0,0,70,30,6,4,brush);
    else if(mode==2)jalium_fill_per_corner_rounded_rectangle(target,0,0,70,30,40,7,10,2,brush);
    else if(mode==3)jalium_draw_rounded_rectangle(target,0,0,70,30,6,4,brush,5);
    else jalium_draw_per_corner_rounded_rectangle(target,0,0,70,30,40,7,10,2,brush,5);
    jalium_pop_transform(target);
    jalium_draw_fill_rectangle(target,145,145,3,3,brush); // ancestor must still clip
    jalium_pop_clip(target);
    jalium_draw_fill_rectangle(target,150,150,3,3,brush); // pop restores the parent
    Check(jalium_render_target_end_draw(target)==JALIUM_OK,"end frame");
    int width=160*scale,height=160*scale;std::vector<uint8_t> pixels(width*height*4);int32_t capturedWidth,capturedHeight;
    Check(jalium_render_target_fetch_readback(target,pixels.data(),width*4,&capturedWidth,&capturedHeight)==JALIUM_OK&&capturedWidth==width&&capturedHeight==height,"read actual BGRA");
    auto path=Shape(kind);CGPathRef outline=stroke?CGPathCreateCopyByStrokingPath(path,nullptr,5,kCGLineCapButt,kCGLineJoinMiter,10):nullptr;
    CGAffineTransform reference=CGAffineTransformMake(matrix[0]*scale,matrix[1]*scale,matrix[2]*scale,matrix[3]*scale,matrix[4]*scale,matrix[5]*scale);
    auto transformed=CGPathCreateCopyByTransformingPath(outline?outline:path,&reference);
    Check(pixels[((151*scale)*width+151*scale)*4+3]>250,"clip/transform pops restore the outer target");
    int inside=0,outside=0,mismatch=0,ink=0;
    // CoreGraphics supplies independent circle arcs, affine transforms and
    // point classification. Ignore a 1.2px edge band shared by AA/flattening.
    for(int y=0;y<height;++y)for(int x=0;x<width;++x){
        if(x>=150*scale&&x<153*scale&&y>=150*scale&&y<153*scale)continue;
        int covered=0;for(double dy:{-1.2,0.,1.2})for(double dx:{-1.2,0.,1.2})
            covered+=CGPathContainsPoint(transformed,nullptr,CGPointMake(x+.5+dx,y+.5+dy),false);
        auto* p=&pixels[(y*width+x)*4];ink+=p[3]>200;
        if(covered==9){++inside;mismatch+=p[3]<250||std::abs(int(p[0])-204)>2||std::abs(int(p[1])-102)>2||std::abs(int(p[2])-51)>2;}
        else if(covered==0){++outside;mismatch+=p[3]>2;}
    }
    if(mismatch)std::fprintf(stderr,"affine mismatch scene=%d scale=%d mode=%d inside=%d outside=%d mismatches=%d ink=%d\n",scenes,scale,mode,inside,outside,mismatch,ink);
    Check(inside>0&&outside>0&&ink>100,"transformed geometry remains visible");Check(mismatch==0,"stable pixels match independent CoreGraphics shape");
    CGPathRelease(transformed);if(outline)CGPathRelease(outline);CGPathRelease(path);jalium_brush_destroy(brush);
}
}
int main(){@autoreleasepool{try{[NSApplication sharedApplication];
    std::array<std::array<float,6>,6> matrices={{{.8660254f,-.5f,.5f,.8660254f,45,65},{0,1,-1,0,100,35},{-.5f,.8660254f,-.8660254f,-.5f,110,70},{1,.3f,.4f,1,30,35},{-1,0,0,1,110,45},{1,0,0,-1,45,100}}};
    for(int scale:{1,2}){Surface surface(scale);
        for(uint32_t i=0;i<_dyld_image_count();++i){auto* path=_dyld_get_image_name(i);if(std::strstr(path,"libjalium.native.software.dylib"))std::printf("LOADED_SOFTWARE %s\n",path);}
        for(auto& matrix:matrices)for(int mode=0;mode<8;++mode)Scene(surface,scale,matrix,mode);
    }
    std::printf("PASS Software affine rectangles: %d scenes, %d checks; independent CoreGraphics classification\n",scenes,checks);return 0;
}catch(const std::exception& error){std::fprintf(stderr,"FAIL Software affine rectangles: %s\n",error.what());return 1;}}}
