#pragma once
#ifdef __APPLE__
#include <CoreText/CoreText.h>
namespace jalium {
// Replace explicit features without retaining old settings or rematching a
// registered variable instance by name. Optional cascade replaces its list.
CTFontRef CopyAppleFontFeatures(CTFontRef input, CFArrayRef settings, CFArrayRef cascade = nullptr);
// Owned result. Preserve source text and all non-font attributes.
CFAttributedStringRef CreateAppleVerticalFeatureText(CFAttributedStringRef input, bool upright);
}
#endif
