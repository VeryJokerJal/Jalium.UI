#include "jalium_text_api.h"
#include "text_engine.h"
#include "text_layout.h"
#include "jalium_string_util.h"

#include <cmath>
#include <cstring>
#include <memory>
#include <string>

#ifndef _WIN32
extern "C" JALIUM_TEXT_API int32_t jalium_text_copy_outline_path(
    const uint16_t* text, uint32_t textLength,
    const uint16_t* family, uint32_t familyLength,
    float fontSize, int32_t fontWeight, int32_t fontStyle,
    float* width, float* baseline, char* buffer, int32_t bufferSize)
{
    if (width) *width = 0;
    if (baseline) *baseline = 0;
    if (!text || !family || !width || !baseline || textLength > 4096 ||
        familyLength == 0 || familyLength > 256 ||
        !std::isfinite(fontSize) || fontSize <= 0 || fontSize > 35791 ||
        bufferSize < 0) return 0;
    try {
        auto content = jalium::ManagedToWString(text, textLength);
        auto familyName = jalium::ManagedToWString(family, familyLength);
        jalium::TextEngine engine;
        if (engine.Initialize() != JALIUM_OK) return 0;
        std::unique_ptr<jalium::TextFormat> base(
            engine.CreateTextFormat(familyName.c_str(), fontSize, fontWeight, fontStyle));
        auto* format = static_cast<jalium::JaliumTextFormat*>(base.get());
        if (!format) return 0;
        format->SetWordWrapping(JALIUM_WORD_WRAP_NONE);
        std::string path;
        if (!format->BuildOutlinePath(content.c_str(),
                static_cast<uint32_t>(content.size()), path, *width, *baseline) ||
            path.size() >= 8u * 1024u * 1024u) return 0;
        const auto required = static_cast<int32_t>(path.size() + 1);
        if (buffer && bufferSize >= required) std::memcpy(buffer, path.c_str(), required);
        return required;
    } catch (...) { return 0; }
}
#endif
