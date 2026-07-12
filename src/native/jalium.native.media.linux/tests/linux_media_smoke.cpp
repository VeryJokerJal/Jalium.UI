#include "jalium_media.h"
#include "jalium_audio.h"

#include <cstdint>
#include <cstdio>
#include <vector>

namespace {

// 1x1 opaque red PNG.
constexpr uint8_t kPng[] = {
    0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a,
    0x00,0x00,0x00,0x0d,0x49,0x48,0x44,0x52,
    0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,
    0x08,0x04,0x00,0x00,0x00,0xb5,0x1c,0x0c,
    0x02,0x00,0x00,0x00,0x0b,0x49,0x44,0x41,
    0x54,0x78,0xda,0x63,0x64,0xf8,0x0f,0x00,
    0x01,0x05,0x01,0x01,0x27,0x18,0xe3,0x66,
    0x00,0x00,0x00,0x00,0x49,0x45,
    0x4e,0x44,0xae,0x42,0x60,0x82
};

int Fail(const char* message, jalium_media_status_t status)
{
    std::fprintf(stderr, "%s: %d (%s)\n", message, static_cast<int>(status),
                 jalium_media_status_string(status));
    return 1;
}

} // namespace

int main(int argc, char** argv)
{
    auto status = jalium_media_initialize();
    if (status != JALIUM_MEDIA_OK) return Fail("initialize", status);

    jalium_image_t image{};
    status = jalium_image_decode_memory(
        kPng, sizeof(kPng), JALIUM_PF_BGRA8, &image);
#if JALIUM_TEST_HAS_GSTREAMER
    if (status != JALIUM_MEDIA_OK) return Fail("PNG decode", status);
    if (image.width != 1 || image.height != 1 || !image.pixels) {
        std::fprintf(stderr, "PNG decode returned an invalid image\n");
        return 1;
    }
    jalium_image_free(&image);
#else
    if (status != JALIUM_MEDIA_E_UNSUPPORTED_FORMAT &&
        status != JALIUM_MEDIA_E_NOT_IMPLEMENTED) {
        return Fail("fallback PNG status", status);
    }
#endif

    jalium_video_decoder_t* decoder = nullptr;
    status = jalium_video_decoder_open_file(
        "/definitely/not/a/jalium-video.mp4", JALIUM_PF_BGRA8, &decoder);
    if (status != JALIUM_MEDIA_E_IO || decoder != nullptr) {
        return Fail("missing video", status);
    }

    if (argc > 1) {
        status = jalium_video_decoder_open_file(
            argv[1], JALIUM_PF_BGRA8, &decoder);
        if (status != JALIUM_MEDIA_OK || !decoder) return Fail("video open", status);
        jalium_video_info_t info{};
        status = jalium_video_decoder_get_info(decoder, &info);
        if (status != JALIUM_MEDIA_OK || info.width == 0 || info.height == 0) {
            jalium_video_decoder_close(decoder);
            return Fail("video info", status);
        }
        jalium_video_frame_t frame{};
        status = jalium_video_decoder_read_frame(decoder, &frame);
        if (status != JALIUM_MEDIA_OK || !frame.pixels || frame.width == 0) {
            jalium_video_decoder_close(decoder);
            return Fail("video frame", status);
        }
        status = jalium_video_decoder_seek_microseconds(decoder, 0);
        if (status != JALIUM_MEDIA_OK) {
            jalium_video_decoder_close(decoder);
            return Fail("video seek", status);
        }
        status = jalium_video_decoder_read_frame(decoder, &frame);
        if (status != JALIUM_MEDIA_OK) {
            jalium_video_decoder_close(decoder);
            return Fail("video frame after seek", status);
        }
        jalium_video_decoder_close(decoder);
        decoder = nullptr;
    }

    if (argc > 2) {
        status = jalium_audio_initialize();
        if (status != JALIUM_MEDIA_OK) return Fail("audio initialize", status);
        jalium_audio_decoder_t* audio = nullptr;
        status = jalium_audio_decoder_open_file(
            argv[2], JALIUM_ACODEC_AAC, &audio);
        if (status != JALIUM_MEDIA_OK || !audio) return Fail("AAC open", status);
        jalium_audio_info_t info{};
        status = jalium_audio_decoder_get_info(audio, &info);
        if (status != JALIUM_MEDIA_OK || info.sample_rate == 0 ||
            info.channels == 0 || info.codec != JALIUM_ACODEC_AAC) {
            jalium_audio_decoder_close(audio);
            return Fail("AAC info", status);
        }
        std::vector<float> pcm(static_cast<size_t>(info.channels) * 1024);
        uint32_t frames = 0;
        status = jalium_audio_decoder_read_frames(
            audio, pcm.data(), 1024, &frames);
        if (status != JALIUM_MEDIA_OK || frames == 0) {
            jalium_audio_decoder_close(audio);
            return Fail("AAC read", status);
        }
        status = jalium_audio_decoder_seek_us(audio, 0);
        if (status != JALIUM_MEDIA_OK) {
            jalium_audio_decoder_close(audio);
            return Fail("AAC seek", status);
        }
        jalium_audio_decoder_close(audio);
        jalium_audio_shutdown();
    }

    jalium_camera_device_t* devices = nullptr;
    uint32_t count = 0;
    status = jalium_camera_enumerate(&devices, &count);
    if (status != JALIUM_MEDIA_OK) return Fail("camera enumerate", status);
    jalium_camera_devices_free(devices, count);

    jalium_media_shutdown();
    return 0;
}
