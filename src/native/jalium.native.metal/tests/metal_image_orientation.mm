#include "jalium_api.h"
#include "jalium_media.h"
#include "software_backend.h"
#define STB_IMAGE_STATIC
#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#include "stb_image.h"
#include <array>
#include <cstdlib>
#include <cstdio>
#include <cstring>
#include <memory>
#include <stdexcept>
#include <vector>
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>

extern "C" void jalium_metal_init();
static void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }

// Rows are specified explicitly in top-down order. Saturated channels make
// premultiplication/unpremultiplication exact, including alpha 0 and 128.
static std::vector<uint8_t> Fixture(int w, int h)
{
    constexpr uint8_t colors[][4] = {{0,0,255,255}, {0,255,0,255},
        {255,0,0,255}, {0,255,255,255}, {0,0,0,0}, {255,0,255,128}};
    std::vector<uint8_t> pixels(w*h*4);
    for (int y=0; y<h; ++y) for (int x=0; x<w; ++x) {
        int color = (x + 3*y) % 6;
        if (w>1 && h>1) {
            if (x==0 && y==0) color=0;
            if (x==w-1 && y==0) color=1;
            if (x==0 && y==h-1) color=2;
            if (x==w-1 && y==h-1) color=3;
        }
        std::memcpy(pixels.data()+(y*w+x)*4, colors[color], 4);
    }
    return pixels;
}
static NSData* Encode(const std::vector<uint8_t>& straight, int w, int h, int orientation=0)
{
    auto pixels=straight;
    for (size_t i=0; i<pixels.size(); i+=4) for (int c=0; c<3; ++c)
        pixels[i+c]=(pixels[i+c]*pixels[i+3]+127)/255;
    auto space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    auto provider=CGDataProviderCreateWithData(nullptr,pixels.data(),pixels.size(),nullptr);
    auto image=CGImageCreate(w,h,8,32,w*4,space,
        static_cast<CGBitmapInfo>(kCGImageAlphaPremultipliedFirst)|kCGBitmapByteOrder32Little,
        provider,nullptr,false,kCGRenderingIntentDefault);
    NSMutableData* encoded=[NSMutableData data];
    auto destination=CGImageDestinationCreateWithData((__bridge CFMutableDataRef)encoded,
        orientation?CFSTR("public.tiff"):CFSTR("public.png"),1,nullptr);
    Check(image && destination,"PNG fixture creation failed");
    NSDictionary* properties=orientation?@{(NSString*)kCGImagePropertyOrientation:@(orientation)}:nil;
    CGImageDestinationAddImage(destination,image,(__bridge CFDictionaryRef)properties);
    bool ok=CGImageDestinationFinalize(destination);
    CFRelease(destination); CGImageRelease(image); CGDataProviderRelease(provider); CGColorSpaceRelease(space);
    Check(ok,"PNG fixture encoding failed");
    return encoded;
}
static void Pixels(NSData* png, const std::vector<uint8_t>& expected, int w, int h)
{
    // Independent stb decoder catches a common-mode ImageIO fixture error.
    int sw=0,sh=0,channels=0;
    std::unique_ptr<stbi_uc,decltype(&stbi_image_free)> decoded(stbi_load_from_memory(
        static_cast<const stbi_uc*>(png.bytes),static_cast<int>(png.length),&sw,&sh,&channels,4),
        stbi_image_free);
    Check(decoded && sw==w && sh==h,"Independent PNG dimensions");
    for (size_t i=0;i<expected.size();++i) {
        int c=i%4; size_t j=(c==0 || c==2)?i-c+(2-c):i;
        Check(decoded.get()[i]==expected[j],"Independent PNG decoder disagrees with fixture row order");
    }

    auto data=CFDataCreate(nullptr,static_cast<const uint8_t*>(png.bytes),png.length);
    auto source=CGImageSourceCreateWithData(data,nullptr);
    auto image=CGImageSourceCreateImageAtIndex(source,0,nullptr);
    auto space=CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    Check(image && space,"CoreGraphics fixture decode");
    for (bool flip : {false,true}) {
        std::vector<uint8_t> buffer(expected.size(),0);
        auto context=CGBitmapContextCreate(buffer.data(),w,h,8,w*4,space,
            kCGImageAlphaPremultipliedFirst|kCGBitmapByteOrder32Little);
        Check(context,"CoreGraphics bitmap context");
        if (flip) { CGContextTranslateCTM(context,0,h); CGContextScaleCTM(context,1,-1); }
        CGContextSetBlendMode(context,kCGBlendModeCopy);
        CGContextDrawImage(context,CGRectMake(0,0,w,h),image);
        CGContextRelease(context);
        for (int y=0; y<h; ++y) for (int x=0; x<w; ++x) for (int c=0;c<4;++c) {
            size_t input=((flip?h-1-y:y)*w+x)*4;
            int value=c==3?expected[input+c]:(expected[input+c]*expected[input+3]+127)/255;
            Check(std::abs(int(buffer[(y*w+x)*4+c])-value)<=1,
                "CoreGraphics raw-buffer row order or premultiplied BGRA differs");
        }
    }
    CGColorSpaceRelease(space); CGImageRelease(image); CFRelease(source); CFRelease(data);
    for (auto format : {JALIUM_PF_BGRA8,JALIUM_PF_RGBA8}) {
        jalium_image_t media{};
        Check(jalium_image_decode_memory(static_cast<const uint8_t*>(png.bytes),png.length,
            format,&media)==JALIUM_MEDIA_OK,"Apple media decode");
        bool ok=media.width==unsigned(w) && media.height==unsigned(h) && media.stride_bytes==unsigned(w*4);
        for (size_t i=0; ok && i<expected.size(); ++i) {
            int c=i%4; size_t j=i;
            if (format==JALIUM_PF_RGBA8 && (c==0 || c==2)) j=i-c+(2-c);
            ok=std::abs(int(media.pixels[i])-expected[j])<=1;
        }
        jalium_image_free(&media);
        Check(ok,"Apple media top-down straight-alpha BGRA/RGBA contract");
    }
    std::printf("PASS CPU raw Quartz + software + Apple BGRA/RGBA %dx%d\n",w,h);
}
static void Exif()
{
    const int w=3,h=5; auto input=Fixture(w,h);
    for (int orientation=1;orientation<=8;++orientation) {
        NSData* tiff=Encode(input,w,h,orientation);
        const int ow=orientation>=5?h:w,oh=orientation>=5?w:h;
        std::vector<uint8_t> expected(input.size());
        for (int y=0;y<h;++y) for (int x=0;x<w;++x) {
            int ox=x,oy=y;
            switch (orientation) {
                case 2: ox=w-1-x; break;
                case 3: ox=w-1-x; oy=h-1-y; break;
                case 4: oy=h-1-y; break;
                case 5: ox=y; oy=x; break;
                case 6: ox=h-1-y; oy=x; break;
                case 7: ox=h-1-y; oy=w-1-x; break;
                case 8: ox=y; oy=w-1-x; break;
            }
            std::memcpy(expected.data()+(oy*ow+ox)*4,input.data()+(y*w+x)*4,4);
        }
        jalium_image_t media{};
        Check(jalium_image_decode_memory(static_cast<const uint8_t*>(tiff.bytes),tiff.length,
            JALIUM_PF_BGRA8,&media)==JALIUM_MEDIA_OK,"EXIF media decode");
        bool ok=media.width==unsigned(ow) && media.height==unsigned(oh);
        for (int y=0;ok && y<oh;++y) for (int x=0;ok && x<ow*4;++x)
            ok=std::abs(int(media.pixels[y*media.stride_bytes+x])-expected[y*ow*4+x])<=1;
        jalium_image_free(&media);
        Check(ok,"Apple media EXIF orientation/dimensions/alpha");
        std::printf("PASS CPU EXIF orientation=%d %dx%d\n",orientation,ow,oh);
    }
}
static void Render(NSData* png, const std::vector<uint8_t>& expected, int w, int h, int scale)
{
    const int rw=w*scale,rh=h*scale;
    jalium::SoftwareBackend software;
    std::unique_ptr<jalium::RenderTarget> cpu(software.CreateRenderTarget(nullptr,rw,rh));
    std::unique_ptr<jalium::Bitmap> reference(software.CreateBitmapFromPixels(expected.data(),w,h,w*4));
    Check(cpu && reference,"Software reference creation");
    Check(cpu->BeginDraw()==JALIUM_OK,"Software BeginDraw");
    cpu->Clear(0,0,0,1); cpu->DrawBitmap(reference.get(),0,0,rw,rh,1,3);
    Check(cpu->EndDraw()==JALIUM_OK,"Software EndDraw");
    auto* framebuffer=dynamic_cast<jalium::SoftwareRenderTarget*>(cpu.get());
    Check(framebuffer,"Software framebuffer");
    const auto& baseline=framebuffer->GetFramebuffer().pixels;

    std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context(
        jalium_context_create(JALIUM_BACKEND_METAL),jalium_context_destroy);
    Check(context!=nullptr,"Metal context (GPU required)");
    NSView* view=[[NSView alloc] initWithFrame:NSMakeRect(0,0,rw,rh)];
    JaliumSurfaceDescriptor descriptor{};
    descriptor.platform=JALIUM_PLATFORM_MACOS; descriptor.kind=JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    descriptor.handle0=reinterpret_cast<intptr_t>((__bridge void*)view);
    std::unique_ptr<JaliumRenderTarget,decltype(&jalium_render_target_destroy)> target(
        jalium_render_target_create_for_surface(context.get(),&descriptor,rw,rh),jalium_render_target_destroy);
    Check(target!=nullptr,"Metal target");
    jalium_render_target_set_dpi(target.get(),96,96);
    for (bool direct : {false,true}) {
        jalium_image_t media{};
        if (!direct) Check(jalium_image_decode_memory(static_cast<const uint8_t*>(png.bytes),png.length,
            JALIUM_PF_BGRA8,&media)==JALIUM_MEDIA_OK,"Apple media render input");
        std::unique_ptr<JaliumImage,decltype(&jalium_bitmap_destroy)> bitmap(direct?
            jalium_bitmap_create_from_memory(context.get(),static_cast<const uint8_t*>(png.bytes),png.length):
            jalium_bitmap_create_from_pixels(context.get(),media.pixels,media.width,media.height,media.stride_bytes),
            jalium_bitmap_destroy);
        jalium_image_free(&media);
        Check(bitmap!=nullptr,"Metal bitmap decode/upload");
        Check(jalium_render_target_request_readback(target.get())==JALIUM_OK,"Metal readback request");
        Check(jalium_render_target_begin_draw(target.get())==JALIUM_OK,"Metal BeginDraw");
        jalium_render_target_clear(target.get(),0,0,0,1);
        jalium_draw_bitmap_ex(target.get(),bitmap.get(),0,0,rw,rh,1,3);
        Check(jalium_render_target_end_draw(target.get())==JALIUM_OK,"Metal EndDraw");
        std::vector<uint8_t> actual(rw*rh*4); int32_t aw=0,ah=0;
        Check(jalium_render_target_fetch_readback(target.get(),actual.data(),rw*4,&aw,&ah)==JALIUM_OK
            && aw==rw && ah==rh,"Metal readback failed; orientation remains unverified");
        Check(baseline.size()==actual.size(),"Reference dimensions");
        for (size_t i=0;i<actual.size();++i) Check(std::abs(int(actual[i])-baseline[i])<=1,
            "Metal decoded render differs from software orientation/alpha/scaling reference");
        std::printf("PASS GPU %s vs software %dx%d scale=%d\n",direct?"direct":"media",w,h,scale);
    }
}
int main(int argc,char** argv)
{
    @autoreleasepool {
        try {
            Exif();
            bool pixelsOnly=argc>1 && std::strcmp(argv[1],"--pixels")==0;
            if (!pixelsOnly) { [NSApplication sharedApplication]; jalium_metal_init(); }
            for (auto size : {std::array<int,2>{2,2},{3,5},{7,3},{1,9},{9,1}}) {
                auto expected=Fixture(size[0],size[1]); NSData* png=Encode(expected,size[0],size[1]);
                Pixels(png,expected,size[0],size[1]);
                if (!pixelsOnly) for (int scale : {1,2}) Render(png,expected,size[0],size[1],scale);
            }
            return 0;
        } catch (const std::exception& e) { std::fprintf(stderr,"FAIL %s\n",e.what()); return 1; }
    }
}
