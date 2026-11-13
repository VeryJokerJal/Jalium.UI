#pragma once

#include "jalium_types.h"
#include <cmath>

namespace jalium {

inline bool InvertGradientTransform(const JaliumMatrix& m, JaliumMatrix& inverse)
{
    const float values[] = {m.m11,m.m12,m.m21,m.m22,m.m31,m.m32};
    for (float value : values) if (!std::isfinite(value)) return false;
    const double determinant = double(m.m11)*m.m22-double(m.m12)*m.m21;
    if (determinant == 0 || !std::isfinite(determinant)) return false;
    inverse = {float(m.m22/determinant),float(-m.m12/determinant),
        float(-m.m21/determinant),float(m.m11/determinant),
        float((double(m.m21)*m.m32-double(m.m22)*m.m31)/determinant),
        float((double(m.m12)*m.m31-double(m.m11)*m.m32)/determinant)};
    const float result[] = {inverse.m11,inverse.m12,inverse.m21,inverse.m22,inverse.m31,inverse.m32};
    for (float value : result) if (!std::isfinite(value)) return false;
    return true;
}

inline void TransformGradientPoint(const JaliumMatrix& m, float x, float y, float& ox, float& oy)
{
    ox=m.m11*x+m.m21*y+m.m31;
    oy=m.m12*x+m.m22*y+m.m32;
}

// Independent capability preserves the existing Brush vtable and factories.
class GradientPaintTransformProvider {
public:
    virtual JaliumResult SetPaintTransform(const JaliumMatrix& transform) = 0;
protected:
    ~GradientPaintTransformProvider() = default;
};

class GradientPaintTransformState : public GradientPaintTransformProvider {
public:
    JaliumMatrix gradientToUser{1,0,0,1,0,0};
    JaliumMatrix userToGradient{1,0,0,1,0,0};
    JaliumResult SetPaintTransform(const JaliumMatrix& transform) override {
        JaliumMatrix inverse;
        if (!InvertGradientTransform(transform,inverse)) return JALIUM_ERROR_INVALID_ARGUMENT;
        gradientToUser=transform;userToGradient=inverse;return JALIUM_OK;
    }
    template<class T> void CopyPaintTransform(T& target) const {
        target.gradientToUser=gradientToUser;target.userToGradient=userToGradient;
    }
};
}
