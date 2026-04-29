#define JALIUM_MEDIA_EXPORTS
#include "win_media_init.h"

#include <atomic>
#include <mutex>

#include <Windows.h>
#include <objbase.h>
#include <mfapi.h>

namespace jalium::media::win {

namespace {
    std::mutex g_initMutex;
    int        g_initCount        = 0;
    bool       g_comInitialized   = false;
    bool       g_mfStarted        = false;
    uint32_t   g_supportedCodecs  = 0;
}

jalium_media_status_t Initialize()
{
    std::lock_guard<std::mutex> lock(g_initMutex);
    if (g_initCount > 0) {
        ++g_initCount;
        return JALIUM_MEDIA_OK;
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED | COINIT_DISABLE_OLE1DDE);
    if (hr == RPC_E_CHANGED_MODE) {
        hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    }
    if (FAILED(hr) && hr != S_FALSE && hr != RPC_E_CHANGED_MODE) {
        return JALIUM_MEDIA_E_PLATFORM;
    }
    g_comInitialized = (hr == S_OK || hr == S_FALSE);

    hr = MFStartup(MF_VERSION, MFSTARTUP_LITE);
    if (FAILED(hr)) {
        if (g_comInitialized) {
            CoUninitialize();
            g_comInitialized = false;
        }
        return JALIUM_MEDIA_E_PLATFORM;
    }
    g_mfStarted = true;

    g_supportedCodecs = ProbeSupportedCodecs();
    if (g_supportedCodecs == 0) {
        // No decoders found — at minimum H.264 should always be present on Win10+.
        g_supportedCodecs = JALIUM_CODEC_H264;
    }

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

    if (g_mfStarted) {
        MFShutdown();
        g_mfStarted = false;
    }
    if (g_comInitialized) {
        CoUninitialize();
        g_comInitialized = false;
    }
    g_supportedCodecs = 0;
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

} // namespace jalium::media::win
