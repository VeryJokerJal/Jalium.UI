#pragma once

namespace jalium::platform::apple {
// The shared desktop origin uses the primary display's scale. Only the
// displacement within a display uses that display's backing scale.
inline double ScreenCoordinate(double point, double screenOrigin,
    double primaryScale, double localScale)
{
    return screenOrigin * primaryScale + (point - screenOrigin) * localScale;
}
inline double ScreenPoint(double coordinate, double screenOrigin,
    double primaryScale, double localScale)
{
    return screenOrigin + (coordinate - screenOrigin * primaryScale) / localScale;
}
}
