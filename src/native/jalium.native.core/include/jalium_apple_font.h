#pragma once
#ifdef __APPLE__
#include "jalium_font_resource.h"
#include <CoreText/CoreText.h>

namespace jalium {
// One resolver for drawing and AppKit accessibility. The caller owns the
// returned CTFont; private bytes stay alive through that object. An optional
// resource pin preserves a text format's existing alias lifetime contract.
JALIUM_API CTFontRef CreateAppleTextFont(CFStringRef family, float size, int32_t weight, int32_t style,
    float widthPercentage = 100, bool matchWidth = false, FontResourceReference* resource = nullptr);
}
#endif
