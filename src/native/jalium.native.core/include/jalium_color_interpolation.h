#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <vector>
#include <stdexcept>
#include "jalium_types.h"

namespace jalium {

// Wire colors remain encoded sRGB (including extended values). The mode
// matches managed ColorInterpolationMode: 0=linear light, 1=encoded sRGB.
inline float GradientSrgbToLinear(float value) {
    return value <= 0.04045f ? value / 12.92f
        : std::pow((value + 0.055f) / 1.055f, 2.4f);
}
inline float GradientLinearToSrgb(float value) {
    return value <= 0.0031308f ? value * 12.92f
        : 1.055f * std::pow(value, 1.0f / 2.4f) - 0.055f;
}
inline float InterpolateGradientChannel(float from, float to, float t, uint32_t mode) {
    if (mode == 0) {
        from = GradientSrgbToLinear(from); to = GradientSrgbToLinear(to);
        return std::clamp(GradientLinearToSrgb(from + (to - from) * t), 0.0f, 1.0f);
    }
    return std::clamp(from + (to - from) * t, 0.0f, 1.0f);
}

// Existing encoded-sRGB shader/vertex pipelines consume a bounded adaptive
// ramp. Refine both straight and premultiplied RGB, keeping the display error
// below a quarter of one 8-bit code value without changing their GPU ABI.
inline std::vector<JaliumGradientStop> PrepareSrgbGradientStops(
    std::vector<JaliumGradientStop> source, uint32_t mode) {
    std::stable_sort(source.begin(), source.end(), [](const auto& a, const auto& b) { return a.position < b.position; });
    std::vector<JaliumGradientStop> result;
    constexpr size_t budget = 1024u * 1024u;
    if (source.empty()) return result;
    result.reserve(source.size());
    auto display = [](JaliumGradientStop s) { s.r=std::clamp(s.r,0.0f,1.0f);s.g=std::clamp(s.g,0.0f,1.0f);s.b=std::clamp(s.b,0.0f,1.0f);return s; };
    result.push_back(display(source.front()));
    for (size_t i = 1; i < source.size(); ++i) {
        const auto a = source[i-1], b = source[i];
        auto sample = [&](float t) {
            return JaliumGradientStop{a.position + (b.position-a.position)*t,
                InterpolateGradientChannel(a.r,b.r,t,mode), InterpolateGradientChannel(a.g,b.g,t,mode),
                InterpolateGradientChannel(a.b,b.b,t,mode), a.a+(b.a-a.a)*t};
        };
        auto append = [&](const JaliumGradientStop& value) {
            if (result.size() >= budget) throw std::length_error("Gradient interpolation ramp exceeds its budget");
            result.push_back(display(value));
        };
        auto refine = [&](auto&& self, float lo, float hi, const JaliumGradientStop& first,
                          const JaliumGradientStop& last, int depth) -> void {
            float error = 0;
            for (float u : {0.25f,0.5f,0.75f}) {
                auto actual = sample(lo+(hi-lo)*u);
                const float x[] = {first.r,first.g,first.b,first.r*first.a,first.g*first.a,first.b*first.a};
                const float y[] = {last.r,last.g,last.b,last.r*last.a,last.g*last.a,last.b*last.a};
                const float expected[] = {actual.r,actual.g,actual.b,actual.r*actual.a,actual.g*actual.a,actual.b*actual.a};
                for (int c=0;c<6;++c) error=(std::max)(error,std::abs(expected[c]-(x[c]+(y[c]-x[c])*u)));
            }
            if (error <= 1.0f/1024.0f) { append(last); return; }
            const float mid=(lo+hi)*0.5f;
            auto center=sample(mid);
            if (depth >= 24 || center.position == first.position || center.position == last.position)
                throw std::length_error("Gradient interpolation cannot meet display precision");
            self(self,lo,mid,first,center,depth+1); self(self,mid,hi,center,last,depth+1);
        };
        if (b.position <= a.position) append(b);
        else {
            // Replace an out-of-gamut source endpoint with its display value;
            // subsequent samples still evaluate the original unbounded pair.
            result.back()=sample(0);
            refine(refine,0,1,sample(0),sample(1),0);
        }
    }
    return result;
}
}
