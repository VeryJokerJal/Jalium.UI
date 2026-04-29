#define JALIUM_MEDIA_EXPORTS
#include "and_media_init.h"

#include <android/api-level.h>
#include <android/log.h>
#include <atomic>
#include <mutex>

#define ANDLOG_TAG "jalium.native.media"
#define ANDLOGI(...) __android_log_print(ANDROID_LOG_INFO, ANDLOG_TAG, __VA_ARGS__)
#define ANDLOGW(...) __android_log_print(ANDROID_LOG_WARN, ANDLOG_TAG, __VA_ARGS__)

namespace jalium::media::android {

namespace {
    std::mutex g_initMutex;
    int        g_initCount        = 0;
    int        g_apiLevel         = 0;
    uint32_t   g_supportedCodecs  = 0;
}

jalium_media_status_t Initialize()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    if (g_initCount > 0) {
        ++g_initCount;
        return JALIUM_MEDIA_OK;
    }

    g_apiLevel = android_get_device_api_level();
    if (g_apiLevel < 0) {
        // Pre-API-24 device — should never happen since SupportedOSPlatformVersion=24.
        return JALIUM_MEDIA_E_PLATFORM;
    }

    g_supportedCodecs = ProbeSupportedCodecsViaJni();

    ANDLOGI("jalium.native.media initialized (apiLevel=%d, codecs=0x%x)",
            g_apiLevel, g_supportedCodecs);

    g_initCount = 1;
    return JALIUM_MEDIA_OK;
}

void Shutdown()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    if (g_initCount == 0) {
        return;
    }
    --g_initCount;
    if (g_initCount > 0) {
        return;
    }
    g_supportedCodecs = 0;
    g_apiLevel = 0;
    ANDLOGI("jalium.native.media shutdown");
}

bool IsInitialized()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    return g_initCount > 0;
}

uint32_t GetSupportedVideoCodecs()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    return g_supportedCodecs;
}

int GetApiLevel()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    return g_apiLevel;
}

} // namespace jalium::media::android
