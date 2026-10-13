#pragma once

#import <AppKit/AppKit.h>
#include <algorithm>
#include <cmath>
#include <limits>
#include <span>

namespace jalium::platform::apple {

// AppKit desktop coordinates remain points across displays. Do not compare
// rectangles multiplied by different displays' backing scales here.
inline size_t NearestStartupScreen(NSPoint point, std::span<const NSRect> frames)
{
    size_t nearest = frames.size();
    double distance = std::numeric_limits<double>::infinity();
    if (!std::isfinite(point.x) || !std::isfinite(point.y)) return nearest;
    for (size_t index = 0; index < frames.size(); ++index) {
        NSRect frame = frames[index];
        if (NSIsEmptyRect(frame) || !std::isfinite(frame.origin.x) || !std::isfinite(frame.origin.y) ||
            !std::isfinite(frame.size.width) || !std::isfinite(frame.size.height)) continue;
        if (NSPointInRect(point, frame)) return index;
        double dx = std::max({NSMinX(frame) - point.x, 0.0, point.x - NSMaxX(frame)});
        double dy = std::max({NSMinY(frame) - point.y, 0.0, point.y - NSMaxY(frame)});
        double candidate = dx * dx + dy * dy;
        if (candidate < distance) { nearest = index; distance = candidate; }
    }
    return nearest;
}

inline NSRect CenterStartupFrame(NSRect frame, NSRect reference, NSRect work)
{
    frame.origin = NSMakePoint(NSMidX(reference) - frame.size.width / 2,
        NSMidY(reference) - frame.size.height / 2);
    frame.origin.x = frame.size.width > work.size.width ? NSMinX(work)
        : std::clamp(frame.origin.x, NSMinX(work), NSMaxX(work) - frame.size.width);
    frame.origin.y = frame.size.height > work.size.height ? NSMaxY(work) - frame.size.height
        : std::clamp(frame.origin.y, NSMinY(work), NSMaxY(work) - frame.size.height);
    return frame;
}

}
