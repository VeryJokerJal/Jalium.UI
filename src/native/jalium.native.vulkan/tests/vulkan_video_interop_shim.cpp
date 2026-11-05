// C ABI test double for the managed descriptor ownership/fallback regression.
// Kept outside native payloads; never used by the product renderer.
#include "jalium_video_surface.h"
#include "jalium_media.h"
#include <cstring>
struct jalium_video_decoder { int marker; };
struct JaliumVideoSurface { uint64_t context, release; };
namespace {
int mode=0,kind=3,references=0,descriptorReleases=0,wraps=0,disables=0,closes=0;
uint32_t observedFlags=0;
jalium_video_decoder decoder{1};
JaliumVideoSurface surface{};
uint8_t pixels[16*8*4]{};
void Retain(uint64_t){++references;}
void Release(uint64_t){--references;}
jalium_media_status_t Export(jalium_video_decoder_gpu_descriptor_t* d){
    *d={};d->acquire_fence_fd=-1;
    if(mode==3)return JALIUM_MEDIA_E_NOT_IMPLEMENTED;
    if(mode==4)return JALIUM_MEDIA_E_END_OF_STREAM;
    if(mode==5 || mode==6)return JALIUM_MEDIA_E_DECODE_FAILED;
    d->kind=kind;d->width=mode==2 ? 0:16;d->height=8;d->handle0=99;
    d->descriptor_flags=JALIUM_VIDEO_GPU_DESCRIPTOR_IMMUTABLE_READY;d->color_space=2;
    d->lifetime_context=42;d->lifetime_retain_callback=reinterpret_cast<uint64_t>(&Retain);
    d->lifetime_release_callback=reinterpret_cast<uint64_t>(&Release);Retain(42);return JALIUM_MEDIA_OK;
}
}
extern "C" {
JALIUM_API void jalium_video_test_configure(int value,int sourceKind){
    mode=value;kind=sourceKind;references=descriptorReleases=wraps=disables=closes=0;observedFlags=0;
}
JALIUM_API int jalium_video_test_counter(int index){
    switch(index){case 0:return descriptorReleases;case 1:return wraps;case 2:return references;
    case 3:return disables;case 4:return closes;case 5:return observedFlags;default:return -1;}
}
JALIUM_MEDIA_API jalium_media_status_t jalium_media_initialize(){return JALIUM_MEDIA_OK;}
JALIUM_MEDIA_API const char* jalium_media_status_string(jalium_media_status_t){return "test status";}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_open_file(const char*,jalium_pixel_format_t,jalium_video_decoder_t** d){*d=&decoder;return JALIUM_MEDIA_OK;}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_get_info(jalium_video_decoder_t*,jalium_video_info_t* info){
    *info={};info->width=16;info->height=8;info->duration_seconds=1;info->frame_rate=30;return JALIUM_MEDIA_OK;
}
JALIUM_MEDIA_API void jalium_video_decoder_close(jalium_video_decoder_t*){++closes;}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_acquire_gpu_surface_descriptor(jalium_video_decoder_t*,jalium_video_decoder_gpu_descriptor_t* d){return Export(d);}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_gpu_frame_descriptor(jalium_video_decoder_t*,jalium_video_decoder_gpu_descriptor_t* d,int64_t* pts,int32_t* key){*pts=123000;*key=1;return Export(d);}
JALIUM_MEDIA_API void jalium_video_decoder_release_gpu_surface_descriptor(jalium_video_decoder_gpu_descriptor_t* d){
    ++descriptorReleases;if(d->lifetime_context)Release(d->lifetime_context);*d={};d->acquire_fence_fd=-1;
}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_disable_gpu_output(jalium_video_decoder_t*){++disables;return mode==6 ? JALIUM_MEDIA_E_PLATFORM:JALIUM_MEDIA_OK;}
JALIUM_MEDIA_API jalium_media_status_t jalium_video_decoder_read_frame(jalium_video_decoder_t*,jalium_video_frame_t* frame){
    *frame={};frame->width=16;frame->height=8;frame->stride_bytes=64;frame->pixels=pixels;
    frame->format=JALIUM_PF_BGRA8;frame->pts_microseconds=123000;return JALIUM_MEDIA_OK;
}
JALIUM_API JaliumVideoSurface* jalium_video_surface_wrap_external(JaliumContext*,const JaliumVideoSurfaceDescriptor* d){
    ++wraps;observedFlags=d->descriptor_flags;if(mode==1)return nullptr;
    surface={d->lifetime_context,d->lifetime_release_callback};
    reinterpret_cast<void(*)(uint64_t)>(d->lifetime_retain_callback)(d->lifetime_context);return &surface;
}
JALIUM_API void jalium_video_surface_destroy(JaliumVideoSurface* s){reinterpret_cast<void(*)(uint64_t)>(s->release)(s->context);}
}
