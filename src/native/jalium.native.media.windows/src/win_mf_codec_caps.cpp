#define JALIUM_MEDIA_EXPORTS
#include "win_media_init.h"
#include "jalium_media.h"

#include <Windows.h>
#include <mfapi.h>
#include <mftransform.h>
#include <wrl/client.h>

using Microsoft::WRL::ComPtr;

namespace jalium::media::win {

namespace {

bool IsCodecAvailable(const GUID& subtype)
{
    MFT_REGISTER_TYPE_INFO inputType{};
    inputType.guidMajorType = MFMediaType_Video;
    inputType.guidSubtype   = subtype;

    IMFActivate** ppActivate = nullptr;
    UINT32 count = 0;
    HRESULT hr = MFTEnumEx(
        MFT_CATEGORY_VIDEO_DECODER,
        MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG_ASYNCMFT |
            MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG_SORTANDFILTER,
        &inputType,
        nullptr,
        &ppActivate,
        &count);

    if (ppActivate) {
        for (UINT32 i = 0; i < count; ++i) {
            if (ppActivate[i]) ppActivate[i]->Release();
        }
        CoTaskMemFree(ppActivate);
    }
    return SUCCEEDED(hr) && count > 0;
}

} // anonymous

uint32_t ProbeSupportedCodecs()
{
    uint32_t mask = 0;
    if (IsCodecAvailable(MFVideoFormat_H264)) mask |= JALIUM_CODEC_H264;
    if (IsCodecAvailable(MFVideoFormat_HEVC)) mask |= JALIUM_CODEC_HEVC;
    if (IsCodecAvailable(MFVideoFormat_VP90)) mask |= JALIUM_CODEC_VP9;
    if (IsCodecAvailable(MFVideoFormat_AV1))  mask |= JALIUM_CODEC_AV1;
    return mask;
}

} // namespace jalium::media::win
