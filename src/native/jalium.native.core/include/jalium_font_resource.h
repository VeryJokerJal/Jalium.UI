#pragma once
#include "jalium_api.h"
#include <memory>
#include <vector>

namespace jalium {
using FontResourceReference = std::shared_ptr<JaliumFontResource>;
// Internal native callers pass host wchar_t strings, unlike the UTF-16 C ABI.
JALIUM_API FontResourceReference AcquireFontResource(const wchar_t* family);
JALIUM_API std::shared_ptr<const std::vector<uint8_t>> AcquireRegisteredFontData(const wchar_t* family);
}
