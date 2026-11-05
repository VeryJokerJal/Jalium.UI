#pragma once
#include "jalium_gradient_sample.h"
#include "jalium_scanline_rasterizer.h"
#include "jalium_impeller_stroke.h"
#include "jalium_flatten.h"
#include <cstring>
#include <unordered_map>

namespace jalium {
struct RadialPaintVertex { float x,y,r,g,b,a; };

// Canvas Vello shaders cannot express SVG's boundary extension, and can
// discard the exact zero-radius endpoint. Rasterize only a nested endpoint
// landing on a device pixel center; other nested gradients retain GPU circles.
inline bool SvgRadialNeedsLimitRaster(const EngineBrushData& brush,const EngineTransform& transform)
{
    if(brush.type!=2||!(brush.radialFlags&1))return false;
    if(brush.radialFlags&6)return true;
    const double rx=std::fabs(brush.radiusX),ry=std::fabs(brush.radiusY),fr=brush.focalRadius;
    if(rx==fr || ((rx==0)!=(ry==0)))return false;
    const double sx=rx>0?rx:std::max({1.0,fr,std::fabs(double(brush.originX)-brush.centerX),std::fabs(double(brush.originY)-brush.centerY)});
    const double sy=ry>0?ry:sx;
    const double dx=(double(brush.centerX)-brush.originX)/sx,dy=(double(brush.centerY)-brush.originY)/sy,dr=(rx-fr)/sx;
    if(dx*dx+dy*dy>=dr*dr)return false;
    const double t=-fr/(rx-fr),x=brush.originX+t*(double(brush.centerX)-brush.originX),y=brush.originY+t*(double(brush.centerY)-brush.originY);
    const auto& m=brush.gradientToUser;
    const double ux=m.m11*x+m.m21*y+m.m31,uy=m.m12*x+m.m22*y+m.m32;
    const double px=transform.m11*ux+transform.m21*uy+transform.dx,py=transform.m12*ux+transform.m22*uy+transform.dy;
    return std::isfinite(px)&&std::isfinite(py)&&std::fabs(px-(std::floor(px)+.5))<1e-4&&std::fabs(py-(std::floor(py)+.5))<1e-4;
}
// Reuses the production stroke expander, including dash phase, cap and
// segment visibility rules, before applying circle paint once to the union.
inline std::vector<Contour> RadialStrokeContours(const std::vector<Contour>& contours,
    float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap,
    const float* dashPattern, uint32_t dashCount, float dashOffset, float scale)
{
    std::vector<RadialPaintVertex> vertices;
    std::vector<uint32_t> indices;
    std::vector<Contour> outlines;
    auto pointKey = [](float x, float y) {
        uint32_t a, b;
        std::memcpy(&a, &x, sizeof(a)); std::memcpy(&b, &y, sizeof(b));
        return (uint64_t(a) << 32) | b;
    };
    const auto defaultCap = static_cast<ImpellerCap>(std::clamp(lineCap, 0, 3));
    for (const auto& contour : contours) {
        if (contour.VertexCount() < 2) continue;
        std::vector<float> points = contour.points;
        std::vector<uint8_t> flags = contour.segmentFlags;
        flags.resize(points.size() / 2 - 1, 1);
        const bool isClosed = contour.closed || (closed && contours.size() == 1);
        if (isClosed && (points[0] != points[points.size() - 2] || points[1] != points.back())) {
            points.push_back(points[0]); points.push_back(points[1]); flags.push_back(1);
        }
        const uint32_t count = static_cast<uint32_t>(points.size() / 2);
        const auto startCap = contour.startCap < 0 ? defaultCap : static_cast<ImpellerCap>(contour.startCap);
        const auto endCap = contour.endCap < 0 ? defaultCap : static_cast<ImpellerCap>(contour.endCap);
        const auto dashCap = contour.dashCap < 0 ? defaultCap : static_cast<ImpellerCap>(contour.dashCap);
        std::unordered_map<uint64_t, uint8_t> smooth;
        for (uint32_t i = 0; i + 1 < count; ++i)
            if (flags[i] & 2) smooth[pointKey(points[i * 2], points[i * 2 + 1])] = 3;
        auto emit = [&](const std::vector<float>& polyline, bool subClosed,
                        ImpellerCap first, ImpellerCap last) {
            std::vector<uint8_t> joins(polyline.size() / 2, 1);
            for (size_t i = 0; i < joins.size(); ++i)
                if (smooth.count(pointKey(polyline[i * 2], polyline[i * 2 + 1]))) joins[i] = 3;
            ExpandStrokePath(vertices, indices, polyline.data(), uint32_t(polyline.size() / 2), strokeWidth,
                static_cast<ImpellerJoin>(std::clamp(lineJoin, 0, 2)),
                std::isfinite(miterLimit) ? std::max(1.0f, miterLimit) : 10.0f,
                // Match the software analytic path's 0.01 device-pixel cap error.
                dashCap, subClosed, 1, 1, 1, 1, &outlines, 0.08f / std::max(scale, 0.01f),
                first, last, joins.data());
        };
        struct Run { std::vector<float> points; bool start, end, closed = false; double phase; };
        std::vector<Run> runs;
        double length = 0;
        bool active = false;
        for (uint32_t i = 0; i + 1 < count; ++i) {
            const float x = points[i * 2], y = points[i * 2 + 1];
            const float nx = points[i * 2 + 2], ny = points[i * 2 + 3];
            if (flags[i] & 1) {
                if (!active) {
                    runs.push_back({{x, y}, !isClosed && i == 0, false, false, length});
                    active = true;
                }
                runs.back().points.insert(runs.back().points.end(), {nx, ny});
                runs.back().end = !isClosed && i + 2 == count;
            } else active = false;
            length += std::hypot(double(nx) - x, double(ny) - y);
        }
        if (isClosed && !runs.empty() && (flags.front() & 1) && (flags.back() & 1)) {
            if (runs.size() == 1) runs[0].closed = true;
            else {
                Run seam = std::move(runs.back()); runs.pop_back();
                seam.points.insert(seam.points.end(), runs[0].points.begin() + 2, runs[0].points.end());
                runs[0] = std::move(seam);
            }
        }
        for (const auto& run : runs) {
            if (dashPattern && dashCount) {
                struct Dash { std::vector<float> points; bool start, end; };
                std::vector<Dash> dashes;
                double cycle = 0;
                for (uint32_t d = 0; d < dashCount; ++d) cycle += dashPattern[d];
                if (dashCount % 2) cycle *= 2;
                const float phase = cycle > 0 ? float(std::fmod(double(dashOffset) + run.phase, cycle)) : dashOffset;
                WalkDashPattern(run.points.data(), uint32_t(run.points.size() / 2), dashPattern, dashCount, phase,
                    [&](const float* sub, uint32_t n, bool first, bool last) {
                        dashes.push_back({std::vector<float>(sub, sub + n * 2), first, last});
                    });
                if (run.closed && dashes.size() > 1 && dashes.front().start && dashes.back().end) {
                    auto seam = std::move(dashes.back()); dashes.pop_back();
                    seam.points.insert(seam.points.end(), dashes.front().points.begin() + 2, dashes.front().points.end());
                    seam.start = seam.end = false;
                    dashes.front() = std::move(seam);
                }
                for (const auto& dash : dashes)
                    emit(dash.points, run.closed && dashes.size() == 1 && dash.start && dash.end,
                        run.start && dash.start ? startCap : dashCap,
                        run.end && dash.end ? endCap : dashCap);
            } else emit(run.points, run.closed, run.start ? startCap : dashCap, run.end ? endCap : dashCap);
        }
    }
    return outlines;
}

// Compatibility path for vertex-color engines and SVG tangent limits that
// the Canvas-only Vello shader cannot represent. Coverage comes from the
// existing scanline rasterizer; paint is sampled at device pixel centers.
template<class Emit> bool RasterizeRadialPaint(const std::vector<Contour>& source,
    const EngineBrushData& brush, const EngineTransform& transform, FillRule rule,
    Emit emit, const PixelRect* clip=nullptr)
{
    if (brush.type!=2 || !(brush.radialFlags&1) || !brush.stops || brush.stopCount==0) return false;
    JaliumMatrix inverse;
    if (!InvertGradientTransform({transform.m11,transform.m12,transform.m21,transform.m22,transform.dx,transform.dy},inverse)) return true;
    auto contours=source;
    for(auto& c:contours)for(size_t i=0;i+1<c.points.size();i+=2){
        const float x=c.points[i],y=c.points[i+1];
        c.points[i]=transform.m11*x+transform.m21*y+transform.dx;
        c.points[i+1]=transform.m12*x+transform.m22*y+transform.dy;
    }
    std::vector<PixelRect> rects;RasterizePathToRects(contours,rule,rects,clip);
    std::vector<float> stops;FlattenGradientStops(brush,stops);
    for(const auto& r:rects)for(int y=r.y;y<r.y+r.h;++y)for(int x=r.x;x<r.x+r.w;++x){
        float px,py;TransformGradientPoint(inverse,x+.5f,y+.5f,px,py);
        auto color=SampleBrushGradient(brush,stops.data(),px,py);
        color.a*=r.alpha;
        if(color.a>0)emit(float(x),float(y),color);
    }
    return true;
}
}
