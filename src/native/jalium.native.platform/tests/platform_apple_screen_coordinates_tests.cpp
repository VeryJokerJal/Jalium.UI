#ifdef NDEBUG
#undef NDEBUG
#endif
#include "../src/platform_apple_screen_coordinates.h"
#include <cassert>
#include <cmath>
#include <initializer_list>

int main()
{
    using namespace jalium::platform::apple;
    // Synthetic desktop coordinates; this does not exercise AppKit or hardware.
    assert(ScreenCoordinate(100, 0, 2, 2) == 200);
    assert(ScreenCoordinate(1540, 1440, 2, 1) == 2980);
    assert(ScreenCoordinate(-1820, -1920, 2, 1) == -3740);
    assert(ScreenCoordinate(-800, -900, 1, 2) == -700);
    assert(ScreenCoordinate(1000, 900, 2, 1) == 1900);
    assert(ScreenCoordinate(150, 100, 2, 1) == 250); // work-area inset
    for (double origin : {-1920.0, 0.0, 1440.0})
        for (double primary : {1.0, 2.0})
            for (double local : {1.0, 2.0}) {
                double point = origin + 123.25;
                assert(std::abs(ScreenPoint(ScreenCoordinate(point, origin, primary, local), origin, primary, local) - point) < 1e-9);
            }
}
