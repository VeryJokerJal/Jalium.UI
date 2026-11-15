// Dispatch/lifetime regression with real NDK declarations and fake platform
// calls. This does not substitute for MediaCodec playback on an Android device.
#include "../src/and_video_decoder.cpp"
#include <deque>
#include <iostream>
#include <map>
#include <vector>
#include <sys/eventfd.h>

struct AMediaFormat { std::map<std::string,int64_t> values; };
struct AMediaExtractor { int sample=0; };
struct AHardwareBuffer { AHardwareBuffer_Desc desc{2,2,1,0x22,AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE,2,0,0}; };
struct AImageReader { std::deque<AImage*> available; int acquired=0; };
struct AImage { AImageReader* reader; AHardwareBuffer buffer; int64_t pts; };
struct AMediaCodec { AImageReader* reader=nullptr; bool formatChanged=true; std::deque<AMediaCodecBufferInfo> output; AMediaCodecBufferInfo current{}; };
namespace {
int failures=0, apiLevel=30, readers=0, images=0, cpuReads=0, renders=0, codecs=0;
bool failGpuConfigure=false, failCpuConfigure=false, failAcquire=false, lastFrameEos=false, withFence=false;
AImageReader* configuringReader=nullptr;
uint8_t yuv[6]={128,128,128,128,128,128};
void Check(bool b,const char* m) {if(!b){std::cerr<<"FAILED: "<<m<<'\n';++failures;}}
AMediaFormat* TrackFormat() {
    auto* f=new AMediaFormat();
    f->values={{"width",2},{"height",2},{"stride",2},{"slice-height",2},{"frame-rate",30},{"durationUs",100000},
        {"color-format",21},{"color-standard",1},{"color-range",1},{"color-transfer",3}};return f;
}
}
namespace jalium::media::android {
bool IsInitialized(){return true;} int GetApiLevel(){return apiLevel;}
}
extern "C" {
const char* AMEDIAFORMAT_KEY_WIDTH="width";
const char* AMEDIAFORMAT_KEY_HEIGHT="height";
const char* AMEDIAFORMAT_KEY_STRIDE="stride";
const char* AMEDIAFORMAT_KEY_COLOR_FORMAT="color-format";
const char* AMEDIAFORMAT_KEY_MIME="mime";
const char* AMEDIAFORMAT_KEY_FRAME_RATE="frame-rate";
const char* AMEDIAFORMAT_KEY_DURATION="durationUs";
int __android_log_print(int,const char*,const char*,...){return 0;}
AMediaExtractor* AMediaExtractor_new(){return new AMediaExtractor();}
media_status_t AMediaExtractor_delete(AMediaExtractor* p){delete p;return AMEDIA_OK;}
media_status_t AMediaExtractor_setDataSourceFd(AMediaExtractor*,int,off64_t,off64_t){return AMEDIA_OK;}
size_t AMediaExtractor_getTrackCount(AMediaExtractor*){return 1;}
AMediaFormat* AMediaExtractor_getTrackFormat(AMediaExtractor*,size_t){return TrackFormat();}
media_status_t AMediaExtractor_selectTrack(AMediaExtractor*,size_t){return AMEDIA_OK;}
ssize_t AMediaExtractor_readSampleData(AMediaExtractor* p,uint8_t* data,size_t capacity){if(p->sample>=3)return -1;Check(capacity>=6,"input capacity");std::memcpy(data,yuv,6);return 6;}
int64_t AMediaExtractor_getSampleTime(AMediaExtractor* p){return int64_t(p->sample)*33333;}
bool AMediaExtractor_advance(AMediaExtractor* p){++p->sample;return p->sample<3;}
media_status_t AMediaExtractor_seekTo(AMediaExtractor* p,int64_t,SeekMode){p->sample=0;return AMEDIA_OK;}
media_status_t AMediaFormat_delete(AMediaFormat* p){delete p;return AMEDIA_OK;}
bool AMediaFormat_getInt32(AMediaFormat* p,const char* name,int32_t* value){auto it=p->values.find(name);if(it==p->values.end())return false;*value=int32_t(it->second);return true;}
bool AMediaFormat_getInt64(AMediaFormat* p,const char* name,int64_t* value){auto it=p->values.find(name);if(it==p->values.end())return false;*value=it->second;return true;}
bool AMediaFormat_getString(AMediaFormat*,const char*,const char** value){*value="video/avc";return true;}
void AMediaFormat_setInt32(AMediaFormat* p,const char* name,int32_t value){p->values[name]=value;}
AMediaCodec* AMediaCodec_createDecoderByType(const char*){++codecs;return new AMediaCodec();}
media_status_t AMediaCodec_delete(AMediaCodec* p){--codecs;delete p;return AMEDIA_OK;}
media_status_t AMediaCodec_configure(AMediaCodec* p,const AMediaFormat*,ANativeWindow* window,AMediaCrypto*,uint32_t){
    if(!window && failCpuConfigure){failCpuConfigure=false;return AMEDIA_ERROR_UNKNOWN;}
    if(window && failGpuConfigure){failGpuConfigure=false;return AMEDIA_ERROR_UNKNOWN;}
    p->reader=window ? configuringReader : nullptr;return AMEDIA_OK;
}
media_status_t AMediaCodec_start(AMediaCodec*){return AMEDIA_OK;}
media_status_t AMediaCodec_stop(AMediaCodec*){return AMEDIA_OK;}
media_status_t AMediaCodec_flush(AMediaCodec* p){p->output.clear();return AMEDIA_OK;}
ssize_t AMediaCodec_dequeueInputBuffer(AMediaCodec*,int64_t){return 0;}
uint8_t* AMediaCodec_getInputBuffer(AMediaCodec*,size_t,size_t* size){*size=6;return yuv;}
media_status_t AMediaCodec_queueInputBuffer(AMediaCodec* p,size_t,off_t,size_t size,uint64_t pts,uint32_t flags){
    if(lastFrameEos && pts==66666) flags|=AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM;
    p->output.push_back({0,int32_t(size),int64_t(pts),flags});return AMEDIA_OK;
}
ssize_t AMediaCodec_dequeueOutputBuffer(AMediaCodec* p,AMediaCodecBufferInfo* info,int64_t){
    if(p->formatChanged){p->formatChanged=false;return AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED;}
    if(p->output.empty())return AMEDIACODEC_INFO_TRY_AGAIN_LATER;
    p->current=p->output.front();p->output.pop_front();*info=p->current;return 0;
}
AMediaFormat* AMediaCodec_getOutputFormat(AMediaCodec*){return TrackFormat();}
uint8_t* AMediaCodec_getOutputBuffer(AMediaCodec*,size_t,size_t* size){++cpuReads;*size=6;return yuv;}
media_status_t AMediaCodec_releaseOutputBuffer(AMediaCodec* p,size_t,bool render){
    if(render){++renders;auto* image=new AImage{p->reader,{},p->current.presentationTimeUs};++images;p->reader->available.push_back(image);}return AMEDIA_OK;
}
media_status_t AImageReader_newWithUsage(int32_t w,int32_t h,int32_t format,uint64_t usage,int32_t maxImages,AImageReader** reader){
    Check(w==2 && h==2 && format==AIMAGE_FORMAT_PRIVATE && usage==AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE && maxImages==8,"private GPU ImageReader contract");
    ++readers;*reader=new AImageReader();configuringReader=*reader;return AMEDIA_OK;
}
void AImageReader_delete(AImageReader* reader){
    Check(reader->acquired==0,"reader outlives every acquired image");
    for(auto* image:reader->available){--images;delete image;}
    --readers;delete reader;
}
media_status_t AImageReader_getWindow(AImageReader* reader,ANativeWindow** window){*window=reinterpret_cast<ANativeWindow*>(reader);return AMEDIA_OK;}
media_status_t AImageReader_acquireNextImageAsync(AImageReader* reader,AImage** image,int* fence){
    if(failAcquire){failAcquire=false;return AMEDIA_ERROR_UNKNOWN;}
    if(reader->available.empty())return AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE;
    *image=reader->available.front();reader->available.pop_front();++reader->acquired;*fence=withFence ? eventfd(0,EFD_CLOEXEC) : -1;return AMEDIA_OK;
}
media_status_t AImage_getHardwareBuffer(const AImage* image,AHardwareBuffer** buffer){*buffer=const_cast<AHardwareBuffer*>(&image->buffer);return AMEDIA_OK;}
void AHardwareBuffer_describe(const AHardwareBuffer* buffer,AHardwareBuffer_Desc* desc){*desc=buffer->desc;}
void AImage_deleteAsync(AImage* image,int fence){if(fence>=0)close(fence);--image->reader->acquired;--images;delete image;}
media_status_t AImage_getCropRect(const AImage*,AImageCropRect* crop){*crop={0,0,2,2};return AMEDIA_OK;}
media_status_t AImage_getNumberOfPlanes(const AImage*,int32_t* planes){*planes=0;return AMEDIA_ERROR_UNKNOWN;}
media_status_t AImage_getPlaneData(const AImage*,int,uint8_t**,int*){return AMEDIA_ERROR_UNKNOWN;}
media_status_t AImage_getPlaneRowStride(const AImage*,int,int*){return AMEDIA_ERROR_UNKNOWN;}
media_status_t AImage_getPlanePixelStride(const AImage*,int,int*){return AMEDIA_ERROR_UNKNOWN;}
}
int main(){
    using namespace jalium::media::android;
    char path[]="/tmp/jalium-android-codec-XXXXXX";int fd=mkstemp(path);Check(write(fd,"x",1)==1,"fixture file");close(fd);
    auto open=[&]{jalium_video_decoder_t* d=nullptr;Check(VideoDecoderOpenFile(path,JALIUM_PF_BGRA8,&d)==JALIUM_MEDIA_OK && d,"decoder opens");return d;};
    {
        auto* d=open();jalium_video_decoder_gpu_descriptor_t a{},b{};int64_t pts=0;int32_t key=0;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && pts==0,"first GPU frame");
        Check(cpuReads==0 && renders==1,"GPU read never maps CPU output");
        Check(a.kind==3 && a.color_space==2 && (a.descriptor_flags&1) && (a.descriptor_flags&JALIUM_VIDEO_GPU_DESCRIPTOR_IMMUTABLE_READY),"GPU frame carries readiness and color metadata");
        Check(VideoDecoderReadGpuFrame(d,&b,&pts,&key)==JALIUM_MEDIA_OK && pts==33333,"second GPU frame");
        Check(images==2,"first queued image is not recycled by next decode");
        if (!a.lifetime_context || !b.lifetime_context) { VideoDecoderClose(d); unlink(path); return 1; }
        auto retain=reinterpret_cast<void(*)(uint64_t)>(a.lifetime_retain_callback);auto release=reinterpret_cast<void(*)(uint64_t)>(a.lifetime_release_callback);
        uint64_t context=a.lifetime_context;retain(context);ReleaseGpuDescriptor(a);ReleaseGpuDescriptor(a);
        VideoDecoderClose(d);Check(readers==1 && images==2,"decoder close preserves queued images and reader");
        ReleaseGpuDescriptor(b);Check(images==1,"descriptor releases its own reference");release(context);
        Check(readers==0 && images==0,"last render fence permits reader/image teardown");
    }
    {
        auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts=0;int32_t key=0;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK,"GPU before seek");ReleaseGpuDescriptor(a);
        Check(VideoDecoderSeek(d,66666)==JALIUM_MEDIA_OK,"GPU seek rebuilds reader queue");
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && pts==66666,"GPU seek skips preroll");ReleaseGpuDescriptor(a);
        Check(VideoDecoderDisableGpuOutput(d)==JALIUM_MEDIA_OK,"switch GPU to CPU");
        jalium_video_frame_t cpu{};Check(VideoDecoderReadFrame(d,&cpu)==JALIUM_MEDIA_OK && cpu.pts_microseconds==66666 && cpu.pixels,"CPU fallback replays rejected timestamp");
        VideoDecoderClose(d);
    }
    {
        auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        failAcquire=true;Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_E_PLATFORM,"failed image acquisition reports failure");
        Check(VideoDecoderDisableGpuOutput(d)==JALIUM_MEDIA_OK,"recover acquisition failure");jalium_video_frame_t cpu{};
        Check(VideoDecoderReadFrame(d,&cpu)==JALIUM_MEDIA_OK && cpu.pts_microseconds==0,"acquire failure retains exact fallback PTS");VideoDecoderClose(d);
    }
    {
        apiLevel=25;auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_E_NOT_IMPLEMENTED,"API 25 keeps CPU mode");
        jalium_video_frame_t cpu{};Check(VideoDecoderReadFrame(d,&cpu)==JALIUM_MEDIA_OK,"API 25 CPU fallback works");VideoDecoderClose(d);apiLevel=30;
    }
    {
        auto* d=open();failGpuConfigure=true;jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_E_NOT_IMPLEMENTED,"unsupported surface codec restores CPU mode");
        jalium_video_frame_t cpu{};Check(VideoDecoderReadFrame(d,&cpu)==JALIUM_MEDIA_OK && cpu.pts_microseconds==0,"configure failure does not skip first frame");VideoDecoderClose(d);
    }
    {
        auto* d=open();lastFrameEos=true;jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        for(int i=0;i<3;i++){Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && pts==i*33333,"EOS delivers its final image");ReleaseGpuDescriptor(a);}
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_E_END_OF_STREAM,"EOS after final image");VideoDecoderClose(d);lastFrameEos=false;
    }
    {
        auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        Check(VideoDecoderSeek(d,66666)==JALIUM_MEDIA_OK,"seek before first GPU request");
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && pts==66666,"initial GPU negotiation preserves requested seek");
        ReleaseGpuDescriptor(a);VideoDecoderClose(d);
    }
    {
        auto* d=open();withFence=true;jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && a.acquire_fence_fd>=0 && !(a.descriptor_flags&JALIUM_VIDEO_GPU_DESCRIPTOR_IMMUTABLE_READY),"asynchronous image exports a fence rather than premature readiness");
        if(a.lifetime_context) {
            auto* frame=reinterpret_cast<GpuImageFrame*>(a.lifetime_context);
            const int original=frame->acquireFence, duplicate=a.acquire_fence_fd;
            Check(original!=duplicate,"descriptor duplicates borrowed image fence");
            uint64_t context=a.lifetime_context;GpuImageFrame::Retain(context);
            VideoDecoderClose(d);ReleaseGpuDescriptor(a);
            Check(fcntl(duplicate,F_GETFD)<0 && fcntl(original,F_GETFD)>=0,"descriptor closes only its own fence");
            GpuImageFrame::Release(context);Check(fcntl(original,F_GETFD)<0,"image release consumes original fence after renderer completes");
        } else VideoDecoderClose(d);
        withFence=false;
    }
    {
        failCpuConfigure=true;auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK && pts==0,"Surface-only codec can open directly on GPU");
        ReleaseGpuDescriptor(a);VideoDecoderClose(d);
    }
    {
        auto* d=open();jalium_video_decoder_gpu_descriptor_t a{};int64_t pts;int32_t key;
        failAcquire=true;
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_E_PLATFORM,"pending image before seek fixture");
        Check(VideoDecoderSeek(d,66666)==JALIUM_MEDIA_OK,"seek after delayed image");
        Check(VideoDecoderReadGpuFrame(d,&a,&pts,&key)==JALIUM_MEDIA_OK,"GPU read after delayed image seek");
        if(a.lifetime_context) {
            auto* frame=reinterpret_cast<GpuImageFrame*>(a.lifetime_context);
            Check(frame->image->pts==pts && pts==66666,"seek cannot attach an old ImageReader buffer to the new timestamp");
        }
        ReleaseGpuDescriptor(a);VideoDecoderClose(d);
    }
    Check(codecs==0 && readers==0 && images==0,"every decoder, reader and image released");unlink(path);
    if(!failures)std::cout<<"Android decoder dispatch and frame lifetime tests passed.\n";return failures ? 1:0;
}
