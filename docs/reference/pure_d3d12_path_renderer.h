#include <windows.h>
#include <wrl/client.h>

#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

using Microsoft::WRL::ComPtr;

namespace pure_d3d12_path {

constexpr float kPi = 3.14159265358979323846f;
constexpr float kEpsilon = 1e-5f;

struct Vec2 {
    float x = 0.0f;
    float y = 0.0f;
};

struct Color {
    float r = 1.0f;
    float g = 1.0f;
    float b = 1.0f;
    float a = 1.0f;
};

struct ClipVertex {
    float x;
    float y;
};

struct Rect {
    float minX = 0.0f;
    float minY = 0.0f;
    float maxX = 0.0f;
    float maxY = 0.0f;
    bool initialized = false;

    void Expand(const Vec2& p) {
        if (!initialized) {
            minX = maxX = p.x;
            minY = maxY = p.y;
            initialized = true;
            return;
        }
        minX = std::min(minX, p.x);
        minY = std::min(minY, p.y);
        maxX = std::max(maxX, p.x);
        maxY = std::max(maxY, p.y);
    }

    void Inflate(float delta) {
        if (!initialized) {
            return;
        }
        minX -= delta;
        minY -= delta;
        maxX += delta;
        maxY += delta;
    }

    float Width() const {
        return initialized ? (maxX - minX) : 0.0f;
    }

    float Height() const {
        return initialized ? (maxY - minY) : 0.0f;
    }
};

struct Affine2D {
    float m00 = 1.0f;
    float m01 = 0.0f;
    float m10 = 0.0f;
    float m11 = 1.0f;
    float tx = 0.0f;
    float ty = 0.0f;

    static Affine2D Identity() {
        return {};
    }

    static Affine2D Translation(float x, float y) {
        Affine2D t;
        t.tx = x;
        t.ty = y;
        return t;
    }

    static Affine2D Scale(float sx, float sy) {
        Affine2D t;
        t.m00 = sx;
        t.m11 = sy;
        return t;
    }

    static Affine2D Rotation(float radians) {
        Affine2D t;
        const float c = std::cos(radians);
        const float s = std::sin(radians);
        t.m00 = c;
        t.m01 = s;
        t.m10 = -s;
        t.m11 = c;
        return t;
    }

    static Affine2D Multiply(const Affine2D& a, const Affine2D& b) {
        Affine2D c;
        c.m00 = a.m00 * b.m00 + a.m01 * b.m10;
        c.m01 = a.m00 * b.m01 + a.m01 * b.m11;
        c.m10 = a.m10 * b.m00 + a.m11 * b.m10;
        c.m11 = a.m10 * b.m01 + a.m11 * b.m11;
        c.tx = a.tx * b.m00 + a.ty * b.m10 + b.tx;
        c.ty = a.tx * b.m01 + a.ty * b.m11 + b.ty;
        return c;
    }
};

inline Vec2 operator+(const Vec2& a, const Vec2& b) { return {a.x + b.x, a.y + b.y}; }
inline Vec2 operator-(const Vec2& a, const Vec2& b) { return {a.x - b.x, a.y - b.y}; }
inline Vec2 operator-(const Vec2& v) { return {-v.x, -v.y}; }
inline Vec2 operator*(const Vec2& v, float s) { return {v.x * s, v.y * s}; }
inline Vec2 operator*(float s, const Vec2& v) { return {v.x * s, v.y * s}; }
inline Vec2 operator/(const Vec2& v, float s) { return {v.x / s, v.y / s}; }

inline float Dot(const Vec2& a, const Vec2& b) { return a.x * b.x + a.y * b.y; }
inline float Cross(const Vec2& a, const Vec2& b) { return a.x * b.y - a.y * b.x; }
inline float LengthSq(const Vec2& v) { return Dot(v, v); }
inline float Length(const Vec2& v) { return std::sqrt(LengthSq(v)); }
inline Vec2 Normalize(const Vec2& v) {
    const float len = Length(v);
    if (len <= kEpsilon) {
        return {0.0f, 0.0f};
    }
    return v / len;
}
inline Vec2 PerpCCW(const Vec2& v) { return {-v.y, v.x}; }
inline Vec2 Lerp(const Vec2& a, const Vec2& b, float t) { return a + (b - a) * t; }
inline bool NearlyEqual(float a, float b, float eps = 1e-4f) { return std::fabs(a - b) <= eps; }
inline bool NearlyEqual(const Vec2& a, const Vec2& b, float eps = 1e-4f) {
    return NearlyEqual(a.x, b.x, eps) && NearlyEqual(a.y, b.y, eps);
}
inline Vec2 TransformPoint(const Affine2D& t, const Vec2& p) {
    return {p.x * t.m00 + p.y * t.m10 + t.tx, p.x * t.m01 + p.y * t.m11 + t.ty};
}
inline Vec2 PixelToClip(const Vec2& p, float width, float height) {
    return {(p.x / width) * 2.0f - 1.0f, 1.0f - (p.y / height) * 2.0f};
}

inline float DistancePointToLine(const Vec2& p, const Vec2& a, const Vec2& b) {
    const Vec2 ab = b - a;
    const float len = Length(ab);
    if (len <= kEpsilon) {
        return Length(p - a);
    }
    return std::fabs(Cross(ab, p - a)) / len;
}

inline float NormalizeAngleDelta(float delta) {
    while (delta <= -kPi) {
        delta += 2.0f * kPi;
    }
    while (delta > kPi) {
        delta -= 2.0f * kPi;
    }
    return delta;
}

inline int ComputeArcSteps(float radius, float sweepAngle, float tolerance) {
    const float r = std::max(radius, 1e-3f);
    const float tol = std::max(tolerance, 1e-3f);
    float c = 1.0f - tol / r;
    c = std::clamp(c, -1.0f, 1.0f);
    float maxStep = 2.0f * std::acos(c);
    if (!std::isfinite(maxStep) || maxStep < 0.05f) {
        maxStep = 0.05f;
    }
    return std::max(1, static_cast<int>(std::ceil(std::fabs(sweepAngle) / maxStep)));
}

inline bool LineIntersection(const Vec2& p, const Vec2& r, const Vec2& q, const Vec2& s, Vec2& out) {
    const float rxs = Cross(r, s);
    if (std::fabs(rxs) <= 1e-6f) {
        return false;
    }
    const float t = Cross(q - p, s) / rxs;
    out = p + r * t;
    return true;
}

inline std::wstring Utf8ToWide(const std::string& s) {
    if (s.empty()) {
        return {};
    }
    const int length = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), nullptr, 0);
    std::wstring result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), static_cast<int>(s.size()), result.data(), length);
    return result;
}

inline std::string HrToString(HRESULT hr) {
    std::ostringstream oss;
    oss << "HRESULT 0x" << std::hex << static_cast<unsigned long>(hr);
    return oss.str();
}

inline void ThrowIfFailed(HRESULT hr, const char* context = nullptr) {
    if (FAILED(hr)) {
        std::string message = context ? std::string(context) + ": " + HrToString(hr) : HrToString(hr);
        throw std::runtime_error(message);
    }
}

inline UINT AlignUp(UINT value, UINT alignment) {
    return (value + alignment - 1u) & ~(alignment - 1u);
}

inline D3D12_RESOURCE_BARRIER TransitionBarrier(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after) {
    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = resource;
    barrier.Transition.StateBefore = before;
    barrier.Transition.StateAfter = after;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    return barrier;
}

inline D3D12_HEAP_PROPERTIES HeapProps(D3D12_HEAP_TYPE type) {
    D3D12_HEAP_PROPERTIES props = {};
    props.Type = type;
    props.CPUPageProperty = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
    props.MemoryPoolPreference = D3D12_MEMORY_POOL_UNKNOWN;
    props.CreationNodeMask = 1;
    props.VisibleNodeMask = 1;
    return props;
}

inline D3D12_RESOURCE_DESC BufferDesc(UINT64 sizeBytes) {
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = sizeBytes;
    desc.Height = 1;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = DXGI_FORMAT_UNKNOWN;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    return desc;
}

inline D3D12_RESOURCE_DESC Texture2DDesc(DXGI_FORMAT format, UINT width, UINT height, D3D12_RESOURCE_FLAGS flags) {
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    desc.Width = width;
    desc.Height = height;
    desc.DepthOrArraySize = 1;
    desc.MipLevels = 1;
    desc.Format = format;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    desc.Flags = flags;
    return desc;
}

inline D3D12_RASTERIZER_DESC DefaultRasterizerDesc() {
    D3D12_RASTERIZER_DESC desc = {};
    desc.FillMode = D3D12_FILL_MODE_SOLID;
    desc.CullMode = D3D12_CULL_MODE_NONE;
    desc.FrontCounterClockwise = FALSE;
    desc.DepthBias = D3D12_DEFAULT_DEPTH_BIAS;
    desc.DepthBiasClamp = D3D12_DEFAULT_DEPTH_BIAS_CLAMP;
    desc.SlopeScaledDepthBias = D3D12_DEFAULT_SLOPE_SCALED_DEPTH_BIAS;
    desc.DepthClipEnable = TRUE;
    desc.MultisampleEnable = FALSE;
    desc.AntialiasedLineEnable = FALSE;
    desc.ForcedSampleCount = 0;
    desc.ConservativeRaster = D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF;
    return desc;
}

inline D3D12_BLEND_DESC DefaultBlendDesc() {
    D3D12_BLEND_DESC desc = {};
    desc.AlphaToCoverageEnable = FALSE;
    desc.IndependentBlendEnable = FALSE;
    for (auto& rt : desc.RenderTarget) {
        rt.BlendEnable = FALSE;
        rt.LogicOpEnable = FALSE;
        rt.SrcBlend = D3D12_BLEND_ONE;
        rt.DestBlend = D3D12_BLEND_ZERO;
        rt.BlendOp = D3D12_BLEND_OP_ADD;
        rt.SrcBlendAlpha = D3D12_BLEND_ONE;
        rt.DestBlendAlpha = D3D12_BLEND_ZERO;
        rt.BlendOpAlpha = D3D12_BLEND_OP_ADD;
        rt.LogicOp = D3D12_LOGIC_OP_NOOP;
        rt.RenderTargetWriteMask = D3D12_COLOR_WRITE_ENABLE_ALL;
    }
    return desc;
}

inline D3D12_DEPTH_STENCIL_DESC DefaultDepthStencilDesc() {
    D3D12_DEPTH_STENCIL_DESC desc = {};
    desc.DepthEnable = TRUE;
    desc.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ALL;
    desc.DepthFunc = D3D12_COMPARISON_FUNC_LESS;
    desc.StencilEnable = FALSE;
    desc.StencilReadMask = D3D12_DEFAULT_STENCIL_READ_MASK;
    desc.StencilWriteMask = D3D12_DEFAULT_STENCIL_WRITE_MASK;
    desc.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
    desc.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
    desc.FrontFace.StencilPassOp = D3D12_STENCIL_OP_KEEP;
    desc.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
    desc.BackFace = desc.FrontFace;
    return desc;
}

enum class FillRule {
    NonZero,
    EvenOdd,
};

enum class PathVerb {
    MoveTo,
    LineTo,
    QuadTo,
    CubicTo,
    ArcTo,
    Close,
};

struct PathCommand {
    PathVerb verb = PathVerb::MoveTo;
    Vec2 p1{};
    Vec2 p2{};
    Vec2 p3{};
    float rx = 0.0f;
    float ry = 0.0f;
    float xAxisRotationDegrees = 0.0f;
    bool largeArc = false;
    bool sweep = true;
};

class PathBuilder {
public:
    void MoveTo(const Vec2& p) {
        PathCommand cmd;
        cmd.verb = PathVerb::MoveTo;
        cmd.p1 = p;
        m_commands.push_back(cmd);
    }

    void LineTo(const Vec2& p) {
        PathCommand cmd;
        cmd.verb = PathVerb::LineTo;
        cmd.p1 = p;
        m_commands.push_back(cmd);
    }

    void QuadTo(const Vec2& control, const Vec2& end) {
        PathCommand cmd;
        cmd.verb = PathVerb::QuadTo;
        cmd.p1 = control;
        cmd.p2 = end;
        m_commands.push_back(cmd);
    }

    void CubicTo(const Vec2& control1, const Vec2& control2, const Vec2& end) {
        PathCommand cmd;
        cmd.verb = PathVerb::CubicTo;
        cmd.p1 = control1;
        cmd.p2 = control2;
        cmd.p3 = end;
        m_commands.push_back(cmd);
    }

    void ArcTo(const Vec2& end, float rx, float ry, float xAxisRotationDegrees, bool largeArc, bool sweep) {
        PathCommand cmd;
        cmd.verb = PathVerb::ArcTo;
        cmd.p1 = end;
        cmd.rx = rx;
        cmd.ry = ry;
        cmd.xAxisRotationDegrees = xAxisRotationDegrees;
        cmd.largeArc = largeArc;
        cmd.sweep = sweep;
        m_commands.push_back(cmd);
    }

    void Close() {
        PathCommand cmd;
        cmd.verb = PathVerb::Close;
        m_commands.push_back(cmd);
    }

    void AddRect(float x, float y, float w, float h) {
        MoveTo({x, y});
        LineTo({x + w, y});
        LineTo({x + w, y + h});
        LineTo({x, y + h});
        Close();
    }

    void AddCircle(const Vec2& center, float radius, bool clockwise = false, int segments = 72) {
        const int count = std::max(8, segments);
        const float sign = clockwise ? -1.0f : 1.0f;
        MoveTo({center.x + radius, center.y});
        for (int i = 1; i < count; ++i) {
            const float angle = sign * (2.0f * kPi * static_cast<float>(i) / static_cast<float>(count));
            LineTo({center.x + std::cos(angle) * radius, center.y + std::sin(angle) * radius});
        }
        Close();
    }

    const std::vector<PathCommand>& Commands() const {
        return m_commands;
    }

private:
    std::vector<PathCommand> m_commands;
};

struct FlattenOptions {
    float tolerance = 0.35f;
    int maxCurveDepth = 16;
};

struct FlattenedContour {
    std::vector<Vec2> points;
    bool closed = false;
};

static void AddPointIfDistinct(std::vector<Vec2>& pts, const Vec2& p, float epsilon = 1e-4f) {
    if (pts.empty() || !NearlyEqual(pts.back(), p, epsilon)) {
        pts.push_back(p);
    }
}

static void FlattenQuadraticRecursive(const Vec2& p0,
                                      const Vec2& p1,
                                      const Vec2& p2,
                                      float tolerance,
                                      int depth,
                                      int maxDepth,
                                      std::vector<Vec2>& out) {
    if (depth >= maxDepth || DistancePointToLine(p1, p0, p2) <= tolerance) {
        AddPointIfDistinct(out, p2);
        return;
    }

    const Vec2 p01 = (p0 + p1) * 0.5f;
    const Vec2 p12 = (p1 + p2) * 0.5f;
    const Vec2 p012 = (p01 + p12) * 0.5f;

    FlattenQuadraticRecursive(p0, p01, p012, tolerance, depth + 1, maxDepth, out);
    FlattenQuadraticRecursive(p012, p12, p2, tolerance, depth + 1, maxDepth, out);
}

static void FlattenCubicRecursive(const Vec2& p0,
                                  const Vec2& p1,
                                  const Vec2& p2,
                                  const Vec2& p3,
                                  float tolerance,
                                  int depth,
                                  int maxDepth,
                                  std::vector<Vec2>& out) {
    const float d1 = DistancePointToLine(p1, p0, p3);
    const float d2 = DistancePointToLine(p2, p0, p3);
    if (depth >= maxDepth || std::max(d1, d2) <= tolerance) {
        AddPointIfDistinct(out, p3);
        return;
    }

    const Vec2 p01 = (p0 + p1) * 0.5f;
    const Vec2 p12 = (p1 + p2) * 0.5f;
    const Vec2 p23 = (p2 + p3) * 0.5f;
    const Vec2 p012 = (p01 + p12) * 0.5f;
    const Vec2 p123 = (p12 + p23) * 0.5f;
    const Vec2 p0123 = (p012 + p123) * 0.5f;

    FlattenCubicRecursive(p0, p01, p012, p0123, tolerance, depth + 1, maxDepth, out);
    FlattenCubicRecursive(p0123, p123, p23, p3, tolerance, depth + 1, maxDepth, out);
}

static float VectorAngle(const Vec2& u, const Vec2& v) {
    const float dot = std::clamp(Dot(u, v), -1.0f, 1.0f);
    float angle = std::acos(dot);
    if (Cross(u, v) < 0.0f) {
        angle = -angle;
    }
    return angle;
}

static void FlattenSvgArc(const Vec2& start,
                          const Vec2& end,
                          float rx,
                          float ry,
                          float xAxisRotationDegrees,
                          bool largeArc,
                          bool sweep,
                          float tolerance,
                          std::vector<Vec2>& out) {
    if (NearlyEqual(start, end) || rx <= kEpsilon || ry <= kEpsilon) {
        AddPointIfDistinct(out, end);
        return;
    }

    rx = std::fabs(rx);
    ry = std::fabs(ry);

    const float phi = xAxisRotationDegrees * kPi / 180.0f;
    const float cosPhi = std::cos(phi);
    const float sinPhi = std::sin(phi);

    const float dx = (start.x - end.x) * 0.5f;
    const float dy = (start.y - end.y) * 0.5f;

    const float x1p = cosPhi * dx + sinPhi * dy;
    const float y1p = -sinPhi * dx + cosPhi * dy;

    float rxSq = rx * rx;
    float rySq = ry * ry;
    float x1pSq = x1p * x1p;
    float y1pSq = y1p * y1p;

    const float radiiCheck = x1pSq / rxSq + y1pSq / rySq;
    if (radiiCheck > 1.0f) {
        const float scale = std::sqrt(radiiCheck);
        rx *= scale;
        ry *= scale;
        rxSq = rx * rx;
        rySq = ry * ry;
    }

    const float numerator = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
    const float denominator = rxSq * y1pSq + rySq * x1pSq;
    const float sign = (largeArc == sweep) ? -1.0f : 1.0f;
    const float factor = (denominator <= kEpsilon) ? 0.0f : sign * std::sqrt(std::max(0.0f, numerator / denominator));

    const float cxp = factor * (rx * y1p / ry);
    const float cyp = factor * (-ry * x1p / rx);

    const float cx = cosPhi * cxp - sinPhi * cyp + (start.x + end.x) * 0.5f;
    const float cy = sinPhi * cxp + cosPhi * cyp + (start.y + end.y) * 0.5f;

    const Vec2 v1 = {(x1p - cxp) / rx, (y1p - cyp) / ry};
    const Vec2 v2 = {(-x1p - cxp) / rx, (-y1p - cyp) / ry};

    float startAngle = VectorAngle({1.0f, 0.0f}, v1);
    float deltaAngle = VectorAngle(v1, v2);
    if (!sweep && deltaAngle > 0.0f) {
        deltaAngle -= 2.0f * kPi;
    } else if (sweep && deltaAngle < 0.0f) {
        deltaAngle += 2.0f * kPi;
    }

    const int steps = std::max(1, ComputeArcSteps(std::max(rx, ry), deltaAngle, tolerance));
    for (int i = 1; i <= steps; ++i) {
        const float t = static_cast<float>(i) / static_cast<float>(steps);
        const float angle = startAngle + deltaAngle * t;
        const float x = cosPhi * (rx * std::cos(angle)) - sinPhi * (ry * std::sin(angle)) + cx;
        const float y = sinPhi * (rx * std::cos(angle)) + cosPhi * (ry * std::sin(angle)) + cy;
        AddPointIfDistinct(out, {x, y});
    }
}

static std::vector<FlattenedContour> FlattenPath(const PathBuilder& builder, const FlattenOptions& options) {
    std::vector<FlattenedContour> contours;
    FlattenedContour current;
    bool hasCurrent = false;
    Vec2 currentPoint{};
    Vec2 subpathStart{};

    auto flushContour = [&]() {
        if (!hasCurrent) {
            return;
        }
        std::vector<Vec2> cleaned;
        cleaned.reserve(current.points.size());
        for (const Vec2& p : current.points) {
            AddPointIfDistinct(cleaned, p);
        }
        if (current.closed && cleaned.size() >= 2 && NearlyEqual(cleaned.front(), cleaned.back())) {
            cleaned.pop_back();
        }
        if (cleaned.size() >= 2) {
            FlattenedContour contour;
            contour.points = std::move(cleaned);
            contour.closed = current.closed;
            contours.push_back(std::move(contour));
        }
        current = {};
        hasCurrent = false;
    };

    for (const PathCommand& cmd : builder.Commands()) {
        switch (cmd.verb) {
        case PathVerb::MoveTo:
            flushContour();
            hasCurrent = true;
            current.closed = false;
            current.points.clear();
            currentPoint = cmd.p1;
            subpathStart = cmd.p1;
            AddPointIfDistinct(current.points, currentPoint);
            break;
        case PathVerb::LineTo:
            if (!hasCurrent) {
                hasCurrent = true;
                current.closed = false;
                currentPoint = cmd.p1;
                subpathStart = cmd.p1;
                AddPointIfDistinct(current.points, currentPoint);
            } else {
                currentPoint = cmd.p1;
                AddPointIfDistinct(current.points, currentPoint);
            }
            break;
        case PathVerb::QuadTo:
            if (!hasCurrent) {
                break;
            }
            FlattenQuadraticRecursive(currentPoint, cmd.p1, cmd.p2, options.tolerance, 0, options.maxCurveDepth, current.points);
            currentPoint = cmd.p2;
            break;
        case PathVerb::CubicTo:
            if (!hasCurrent) {
                break;
            }
            FlattenCubicRecursive(currentPoint, cmd.p1, cmd.p2, cmd.p3, options.tolerance, 0, options.maxCurveDepth, current.points);
            currentPoint = cmd.p3;
            break;
        case PathVerb::ArcTo:
            if (!hasCurrent) {
                break;
            }
            FlattenSvgArc(currentPoint, cmd.p1, cmd.rx, cmd.ry, cmd.xAxisRotationDegrees, cmd.largeArc, cmd.sweep, options.tolerance, current.points);
            currentPoint = cmd.p1;
            break;
        case PathVerb::Close:
            if (hasCurrent) {
                current.closed = true;
                if (!current.points.empty()) {
                    AddPointIfDistinct(current.points, subpathStart);
                }
                flushContour();
            }
            break;
        }
    }

    flushContour();
    return contours;
}

struct PathGeometry {
    std::vector<FlattenedContour> contours;
    std::vector<Vec2> fillTrianglesLocal;
    std::vector<Vec2> coverTrianglesLocal;
    Rect bounds;
};

static std::shared_ptr<PathGeometry> BuildPathGeometry(const PathBuilder& builder, const FlattenOptions& options) {
    auto geometry = std::make_shared<PathGeometry>();
    geometry->contours = FlattenPath(builder, options);

    for (const FlattenedContour& contour : geometry->contours) {
        for (const Vec2& p : contour.points) {
            geometry->bounds.Expand(p);
        }
    }

    if (!geometry->bounds.initialized) {
        return geometry;
    }

    const float margin = std::max(8.0f, std::max(geometry->bounds.Width(), geometry->bounds.Height()) + 8.0f);
    const Vec2 anchor = {geometry->bounds.minX - margin, geometry->bounds.minY - margin};

    for (const FlattenedContour& contour : geometry->contours) {
        if (!contour.closed || contour.points.size() < 3) {
            continue;
        }
        const size_t count = contour.points.size();
        for (size_t i = 0; i < count; ++i) {
            const Vec2 a = contour.points[i];
            const Vec2 b = contour.points[(i + 1) % count];
            if (NearlyEqual(a, b)) {
                continue;
            }
            geometry->fillTrianglesLocal.push_back(anchor);
            geometry->fillTrianglesLocal.push_back(a);
            geometry->fillTrianglesLocal.push_back(b);
        }
    }

    Rect cover = geometry->bounds;
    cover.Inflate(1.0f);
    geometry->coverTrianglesLocal = {
        {cover.minX, cover.minY}, {cover.maxX, cover.minY}, {cover.maxX, cover.maxY},
        {cover.minX, cover.minY}, {cover.maxX, cover.maxY}, {cover.minX, cover.maxY},
    };

    return geometry;
}

enum class LineJoin {
    Miter,
    Bevel,
    Round,
};

enum class LineCap {
    Butt,
    Square,
    Round,
};

struct StrokeStyle {
    float width = 1.0f;
    LineJoin join = LineJoin::Miter;
    LineCap startCap = LineCap::Butt;
    LineCap endCap = LineCap::Butt;
    float miterLimit = 4.0f;
    float arcTolerance = 0.35f;
};

static void AddTriangle(std::vector<Vec2>& out, const Vec2& a, const Vec2& b, const Vec2& c) {
    out.push_back(a);
    out.push_back(b);
    out.push_back(c);
}

static void EmitArcFan(std::vector<Vec2>& out, const Vec2& center, const Vec2& from, const Vec2& to, float tolerance) {
    const float radius = Length(from);
    if (radius <= kEpsilon) {
        return;
    }
    const float a0 = std::atan2(from.y, from.x);
    const float a1 = std::atan2(to.y, to.x);
    const float delta = NormalizeAngleDelta(a1 - a0);
    if (std::fabs(delta) <= 1e-5f) {
        return;
    }
    const int steps = std::max(1, ComputeArcSteps(radius, delta, tolerance));
    Vec2 previous = center + from;
    for (int i = 1; i <= steps; ++i) {
        const float t = static_cast<float>(i) / static_cast<float>(steps);
        const float angle = a0 + delta * t;
        const Vec2 current = {center.x + std::cos(angle) * radius, center.y + std::sin(angle) * radius};
        AddTriangle(out, center, previous, current);
        previous = current;
    }
}

static void EmitRoundCapStart(std::vector<Vec2>& out, const Vec2& center, const Vec2& dir, float hw, float tolerance) {
    const Vec2 n = PerpCCW(dir);
    const int steps = std::max(3, ComputeArcSteps(hw, kPi, tolerance));
    Vec2 previous = center - n * hw;
    for (int i = 1; i <= steps; ++i) {
        const float t = static_cast<float>(i) / static_cast<float>(steps);
        const float theta = -kPi * 0.5f + kPi * t;
        const Vec2 current = center + ((-dir) * std::cos(theta) + n * std::sin(theta)) * hw;
        AddTriangle(out, center, previous, current);
        previous = current;
    }
}

static void EmitRoundCapEnd(std::vector<Vec2>& out, const Vec2& center, const Vec2& dir, float hw, float tolerance) {
    const Vec2 n = PerpCCW(dir);
    const int steps = std::max(3, ComputeArcSteps(hw, kPi, tolerance));
    Vec2 previous = center - n * hw;
    for (int i = 1; i <= steps; ++i) {
        const float t = static_cast<float>(i) / static_cast<float>(steps);
        const float theta = -kPi * 0.5f + kPi * t;
        const Vec2 current = center + (dir * std::cos(theta) + n * std::sin(theta)) * hw;
        AddTriangle(out, center, previous, current);
        previous = current;
    }
}

static void EmitJoin(std::vector<Vec2>& out,
                     const Vec2& point,
                     const Vec2& prevOuter,
                     const Vec2& nextOuter,
                     const Vec2& prevDir,
                     const Vec2& nextDir,
                     const StrokeStyle& style,
                     float hw) {
    if (style.join == LineJoin::Round) {
        EmitArcFan(out, point, prevOuter - point, nextOuter - point, style.arcTolerance);
        return;
    }

    if (style.join == LineJoin::Miter) {
        Vec2 intersection{};
        if (LineIntersection(prevOuter, prevDir, nextOuter, nextDir, intersection)) {
            const float miterScale = Length(intersection - point) / std::max(hw, 1e-4f);
            if (miterScale <= style.miterLimit) {
                AddTriangle(out, prevOuter, intersection, nextOuter);
                return;
            }
        }
    }

    AddTriangle(out, point, prevOuter, nextOuter);
}

static std::vector<Vec2> CleanStrokePoints(const FlattenedContour& contour) {
    std::vector<Vec2> pts;
    pts.reserve(contour.points.size());
    for (const Vec2& p : contour.points) {
        AddPointIfDistinct(pts, p);
    }
    if (contour.closed && pts.size() >= 2 && NearlyEqual(pts.front(), pts.back())) {
        pts.pop_back();
    }
    return pts;
}

static void BuildStrokeForContour(const FlattenedContour& contour, const StrokeStyle& style, std::vector<Vec2>& out) {
    std::vector<Vec2> pts = CleanStrokePoints(contour);
    if (pts.size() < 2 || style.width <= 0.0f) {
        return;
    }

    const bool closed = contour.closed && pts.size() >= 3;
    const size_t segCount = closed ? pts.size() : pts.size() - 1;
    if (segCount == 0) {
        return;
    }

    const float hw = style.width * 0.5f;
    std::vector<Vec2> dirs(segCount);
    std::vector<Vec2> norms(segCount);

    for (size_t i = 0; i < segCount; ++i) {
        const Vec2 a = pts[i];
        const Vec2 b = pts[(i + 1) % pts.size()];
        const Vec2 d = Normalize(b - a);
        if (LengthSq(d) <= kEpsilon) {
            return;
        }
        dirs[i] = d;
        norms[i] = PerpCCW(d);
    }

    for (size_t i = 0; i < segCount; ++i) {
        const Vec2 a = pts[i];
        const Vec2 b = pts[(i + 1) % pts.size()];
        const Vec2 d = dirs[i];
        const Vec2 n = norms[i] * hw;

        float extendStart = 0.0f;
        float extendEnd = 0.0f;
        if (!closed && i == 0 && style.startCap == LineCap::Square) {
            extendStart = hw;
        }
        if (!closed && i == segCount - 1 && style.endCap == LineCap::Square) {
            extendEnd = hw;
        }

        const Vec2 p0 = a - d * extendStart + n;
        const Vec2 p1 = a - d * extendStart - n;
        const Vec2 p2 = b + d * extendEnd + n;
        const Vec2 p3 = b + d * extendEnd - n;

        AddTriangle(out, p0, p1, p2);
        AddTriangle(out, p1, p3, p2);
    }

    if (!closed) {
        if (style.startCap == LineCap::Round) {
            EmitRoundCapStart(out, pts.front(), dirs.front(), hw, style.arcTolerance);
        }
        if (style.endCap == LineCap::Round) {
            EmitRoundCapEnd(out, pts.back(), dirs.back(), hw, style.arcTolerance);
        }
    }

    const size_t pointCount = pts.size();
    for (size_t i = 0; i < pointCount; ++i) {
        if (!closed && (i == 0 || i == pointCount - 1)) {
            continue;
        }

        const size_t prevSeg = (i + segCount - 1) % segCount;
        const size_t nextSeg = i % segCount;
        const Vec2 prevDir = dirs[prevSeg];
        const Vec2 nextDir = dirs[nextSeg];
        const float cross = Cross(prevDir, nextDir);
        if (std::fabs(cross) <= 1e-5f) {
            continue;
        }

        if (cross > 0.0f) {
            const Vec2 prevOuter = pts[i] + norms[prevSeg] * hw;
            const Vec2 nextOuter = pts[i] + norms[nextSeg] * hw;
            EmitJoin(out, pts[i], prevOuter, nextOuter, prevDir, nextDir, style, hw);
        } else {
            const Vec2 prevOuter = pts[i] - norms[prevSeg] * hw;
            const Vec2 nextOuter = pts[i] - norms[nextSeg] * hw;
            EmitJoin(out, pts[i], prevOuter, nextOuter, prevDir, nextDir, style, hw);
        }
    }
}

static std::vector<Vec2> BuildStrokeTrianglesLocal(const PathGeometry& geometry, const StrokeStyle& style) {
    std::vector<Vec2> out;
    if (style.width <= 0.0f) {
        return out;
    }
    for (const FlattenedContour& contour : geometry.contours) {
        BuildStrokeForContour(contour, style, out);
    }
    return out;
}

struct UploadSlice {
    D3D12_VERTEX_BUFFER_VIEW view{};
};

struct FrameUploadBuffer {
    ComPtr<ID3D12Resource> resource;
    uint8_t* mapped = nullptr;
    UINT capacityBytes = 0;
    UINT offsetBytes = 0;
};

class PureD3D12PathRenderer {
public:
    static constexpr UINT kFrameCount = 2;
    static constexpr UINT kMsaaSampleCount = 4;
    static constexpr UINT kDefaultUploadBytes = 16 * 1024 * 1024;

    ~PureD3D12PathRenderer() {
        Shutdown();
    }

    void Initialize(HWND hwnd, UINT width, UINT height) {
        m_hwnd = hwnd;
        m_width = std::max(1u, width);
        m_height = std::max(1u, height);

#if defined(_DEBUG)
        {
            ComPtr<ID3D12Debug> debugController;
            if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController)))) {
                debugController->EnableDebugLayer();
            }
        }
#endif

        UINT factoryFlags = 0;
#if defined(_DEBUG)
        factoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
#endif
        ThrowIfFailed(CreateDXGIFactory2(factoryFlags, IID_PPV_ARGS(&m_factory)), "CreateDXGIFactory2");

        ComPtr<IDXGIAdapter1> adapter = ChooseAdapter();
        ThrowIfFailed(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&m_device)), "D3D12CreateDevice");

        CreateCommandObjects();
        CreateSwapChain();
        CreateDescriptorHeaps();
        CreateRenderTargets();
        CreateDepthStencil();
        CreateMsaaRenderTarget();
        CreateRootSignature();
        CreatePipelineStates();
        CreateDynamicUploadBuffers(kDefaultUploadBytes);

        ThrowIfFailed(m_device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&m_fence)), "CreateFence");
        m_fenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
        if (!m_fenceEvent) {
            throw std::runtime_error("CreateEvent failed");
        }
        m_fenceValues.fill(1);
        UpdateViewport();
    }

    void Shutdown() {
        if (m_device) {
            WaitForGpu();
        }

        for (FrameUploadBuffer& upload : m_uploadBuffers) {
            if (upload.resource && upload.mapped) {
                upload.resource->Unmap(0, nullptr);
                upload.mapped = nullptr;
            }
            upload.resource.Reset();
        }

        if (m_fenceEvent) {
            CloseHandle(m_fenceEvent);
            m_fenceEvent = nullptr;
        }

        m_msaaRT.Reset();
        m_depthStencil.Reset();
        for (auto& rt : m_renderTargets) {
            rt.Reset();
        }
        for (auto& allocator : m_commandAllocators) {
            allocator.Reset();
        }
        m_commandList.Reset();
        m_fence.Reset();
        m_rtvHeap.Reset();
        m_dsvHeap.Reset();
        m_swapChain.Reset();
        m_commandQueue.Reset();
        m_rootSignature.Reset();
        m_psoFillNonZeroStencil.Reset();
        m_psoFillEvenOddStencil.Reset();
        m_psoColorStencil.Reset();
        m_psoColor.Reset();
        m_device.Reset();
        m_factory.Reset();
    }

    std::shared_ptr<PathGeometry> CreateGeometry(const PathBuilder& builder, const FlattenOptions& options = {}) const {
        return BuildPathGeometry(builder, options);
    }

    void Resize(UINT width, UINT height) {
        if (!m_device || width == 0 || height == 0) {
            return;
        }
        width = std::max(1u, width);
        height = std::max(1u, height);
        if (width == m_width && height == m_height) {
            return;
        }

        WaitForGpu();
        for (auto& rt : m_renderTargets) {
            rt.Reset();
        }
        m_depthStencil.Reset();
        m_msaaRT.Reset();

        ThrowIfFailed(m_swapChain->ResizeBuffers(kFrameCount, width, height, m_backBufferFormat, 0), "ResizeBuffers");
        m_frameIndex = m_swapChain->GetCurrentBackBufferIndex();
        m_width = width;
        m_height = height;
        CreateRenderTargets();
        CreateDepthStencil();
        CreateMsaaRenderTarget();
        UpdateViewport();
    }

    UINT Width() const { return m_width; }
    UINT Height() const { return m_height; }

    void BeginFrame(const Color& clearColor) {
        if (!m_device || m_width == 0 || m_height == 0) {
            return;
        }

        FrameUploadBuffer& upload = m_uploadBuffers[m_frameIndex];
        upload.offsetBytes = 0;

        ThrowIfFailed(m_commandAllocators[m_frameIndex]->Reset(), "CommandAllocator Reset");
        ThrowIfFailed(m_commandList->Reset(m_commandAllocators[m_frameIndex].Get(), nullptr), "CommandList Reset");

        const D3D12_CPU_DESCRIPTOR_HANDLE msaaRtv = MsaaRTV();
        const D3D12_CPU_DESCRIPTOR_HANDLE dsv = m_dsvHeap->GetCPUDescriptorHandleForHeapStart();
        m_commandList->OMSetRenderTargets(1, &msaaRtv, FALSE, &dsv);
        m_commandList->ClearRenderTargetView(msaaRtv, &clearColor.r, 0, nullptr);
        m_commandList->ClearDepthStencilView(dsv, D3D12_CLEAR_FLAG_DEPTH | D3D12_CLEAR_FLAG_STENCIL, 1.0f, 0, 0, nullptr);
        m_commandList->RSSetViewports(1, &m_viewport);
        m_commandList->RSSetScissorRects(1, &m_scissorRect);
        m_commandList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        m_commandList->SetGraphicsRootSignature(m_rootSignature.Get());
    }

    void FillPath(const PathGeometry& geometry, const Affine2D& transform, const Color& color, FillRule rule) {
        if (geometry.fillTrianglesLocal.empty() || geometry.coverTrianglesLocal.empty()) {
            return;
        }

        const D3D12_CPU_DESCRIPTOR_HANDLE dsv = m_dsvHeap->GetCPUDescriptorHandleForHeapStart();
        m_commandList->ClearDepthStencilView(dsv, D3D12_CLEAR_FLAG_STENCIL, 1.0f, 0, 0, nullptr);

        UploadSlice fillSlice = UploadLocalVertices(geometry.fillTrianglesLocal, transform);
        SetColor({0.0f, 0.0f, 0.0f, 0.0f});
        m_commandList->SetPipelineState(rule == FillRule::NonZero ? m_psoFillNonZeroStencil.Get() : m_psoFillEvenOddStencil.Get());
        m_commandList->OMSetStencilRef(0);
        m_commandList->IASetVertexBuffers(0, 1, &fillSlice.view);
        m_commandList->DrawInstanced(static_cast<UINT>(geometry.fillTrianglesLocal.size()), 1, 0, 0);

        UploadSlice coverSlice = UploadLocalVertices(geometry.coverTrianglesLocal, transform);
        SetColor(color);
        m_commandList->SetPipelineState(m_psoColorStencil.Get());
        m_commandList->OMSetStencilRef(0);
        m_commandList->IASetVertexBuffers(0, 1, &coverSlice.view);
        m_commandList->DrawInstanced(static_cast<UINT>(geometry.coverTrianglesLocal.size()), 1, 0, 0);
    }

    void StrokePath(const PathGeometry& geometry, const Affine2D& transform, const Color& color, const StrokeStyle& style) {
        std::vector<Vec2> localStroke = BuildStrokeTrianglesLocal(geometry, style);
        if (localStroke.empty()) {
            return;
        }
        UploadSlice slice = UploadLocalVertices(localStroke, transform);
        SetColor(color);
        m_commandList->SetPipelineState(m_psoColor.Get());
        m_commandList->IASetVertexBuffers(0, 1, &slice.view);
        m_commandList->DrawInstanced(static_cast<UINT>(localStroke.size()), 1, 0, 0);
    }

    void EndFrame() {
        D3D12_RESOURCE_BARRIER barriers[2];
        barriers[0] = TransitionBarrier(m_msaaRT.Get(), D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_RESOLVE_SOURCE);
        barriers[1] = TransitionBarrier(m_renderTargets[m_frameIndex].Get(), D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RESOLVE_DEST);
        m_commandList->ResourceBarrier(2, barriers);

        m_commandList->ResolveSubresource(m_renderTargets[m_frameIndex].Get(), 0, m_msaaRT.Get(), 0, m_backBufferFormat);

        barriers[0] = TransitionBarrier(m_msaaRT.Get(), D3D12_RESOURCE_STATE_RESOLVE_SOURCE, D3D12_RESOURCE_STATE_RENDER_TARGET);
        barriers[1] = TransitionBarrier(m_renderTargets[m_frameIndex].Get(), D3D12_RESOURCE_STATE_RESOLVE_DEST, D3D12_RESOURCE_STATE_PRESENT);
        m_commandList->ResourceBarrier(2, barriers);

        ThrowIfFailed(m_commandList->Close(), "CommandList Close");
        ID3D12CommandList* lists[] = {m_commandList.Get()};
        m_commandQueue->ExecuteCommandLists(1, lists);
        ThrowIfFailed(m_swapChain->Present(1, 0), "Present");
        MoveToNextFrame();
    }

private:
    ComPtr<IDXGIAdapter1> ChooseAdapter() {
        ComPtr<IDXGIAdapter1> adapter;
        for (UINT index = 0; m_factory->EnumAdapters1(index, &adapter) != DXGI_ERROR_NOT_FOUND; ++index) {
            DXGI_ADAPTER_DESC1 desc = {};
            adapter->GetDesc1(&desc);
            if (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) {
                continue;
            }
            if (SUCCEEDED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, _uuidof(ID3D12Device), nullptr))) {
                return adapter;
            }
        }
        ThrowIfFailed(m_factory->EnumWarpAdapter(IID_PPV_ARGS(&adapter)), "EnumWarpAdapter");
        return adapter;
    }

    void CreateCommandObjects() {
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ThrowIfFailed(m_device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&m_commandQueue)), "CreateCommandQueue");
        for (UINT i = 0; i < kFrameCount; ++i) {
            ThrowIfFailed(m_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&m_commandAllocators[i])), "CreateCommandAllocator");
        }
        ThrowIfFailed(m_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, m_commandAllocators[0].Get(), nullptr, IID_PPV_ARGS(&m_commandList)), "CreateCommandList");
        ThrowIfFailed(m_commandList->Close(), "Initial CommandList Close");
    }

    void CreateSwapChain() {
        DXGI_SWAP_CHAIN_DESC1 desc = {};
        desc.Width = m_width;
        desc.Height = m_height;
        desc.Format = m_backBufferFormat;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = kFrameCount;
        desc.SampleDesc.Count = 1;
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
        desc.Scaling = DXGI_SCALING_STRETCH;

        ComPtr<IDXGISwapChain1> swapChain1;
        ThrowIfFailed(m_factory->CreateSwapChainForHwnd(m_commandQueue.Get(), m_hwnd, &desc, nullptr, nullptr, &swapChain1), "CreateSwapChainForHwnd");
        ThrowIfFailed(m_factory->MakeWindowAssociation(m_hwnd, DXGI_MWA_NO_ALT_ENTER), "MakeWindowAssociation");
        ThrowIfFailed(swapChain1.As(&m_swapChain), "SwapChain Cast");
        m_frameIndex = m_swapChain->GetCurrentBackBufferIndex();
    }

    void CreateDescriptorHeaps() {
        D3D12_DESCRIPTOR_HEAP_DESC rtvDesc = {};
        rtvDesc.NumDescriptors = kFrameCount + 1;
        rtvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        ThrowIfFailed(m_device->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&m_rtvHeap)), "Create RTV Heap");
        m_rtvDescriptorSize = m_device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

        D3D12_DESCRIPTOR_HEAP_DESC dsvDesc = {};
        dsvDesc.NumDescriptors = 1;
        dsvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        ThrowIfFailed(m_device->CreateDescriptorHeap(&dsvDesc, IID_PPV_ARGS(&m_dsvHeap)), "Create DSV Heap");
    }

    void CreateRenderTargets() {
        D3D12_CPU_DESCRIPTOR_HANDLE handle = m_rtvHeap->GetCPUDescriptorHandleForHeapStart();
        for (UINT i = 0; i < kFrameCount; ++i) {
            ThrowIfFailed(m_swapChain->GetBuffer(i, IID_PPV_ARGS(&m_renderTargets[i])), "GetBuffer");
            m_device->CreateRenderTargetView(m_renderTargets[i].Get(), nullptr, handle);
            handle.ptr += m_rtvDescriptorSize;
        }
    }

    void CreateDepthStencil() {
        D3D12_CLEAR_VALUE clearValue = {};
        clearValue.Format = m_depthStencilFormat;
        clearValue.DepthStencil.Depth = 1.0f;
        clearValue.DepthStencil.Stencil = 0;
        const auto heapProps = HeapProps(D3D12_HEAP_TYPE_DEFAULT);

        D3D12_RESOURCE_DESC dsDesc = {};
        dsDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        dsDesc.Width = m_width;
        dsDesc.Height = m_height;
        dsDesc.DepthOrArraySize = 1;
        dsDesc.MipLevels = 1;
        dsDesc.Format = m_depthStencilFormat;
        dsDesc.SampleDesc.Count = kMsaaSampleCount;
        dsDesc.SampleDesc.Quality = 0;
        dsDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
        dsDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;

        ThrowIfFailed(
            m_device->CreateCommittedResource(&heapProps,
                                             D3D12_HEAP_FLAG_NONE,
                                             &dsDesc,
                                             D3D12_RESOURCE_STATE_DEPTH_WRITE,
                                             &clearValue,
                                             IID_PPV_ARGS(&m_depthStencil)),
            "Create DepthStencil");

        D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
        dsvDesc.Format = m_depthStencilFormat;
        dsvDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2DMS;
        m_device->CreateDepthStencilView(m_depthStencil.Get(), &dsvDesc, m_dsvHeap->GetCPUDescriptorHandleForHeapStart());
    }

    void CreateMsaaRenderTarget() {
        D3D12_RESOURCE_DESC rtDesc = {};
        rtDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        rtDesc.Width = m_width;
        rtDesc.Height = m_height;
        rtDesc.DepthOrArraySize = 1;
        rtDesc.MipLevels = 1;
        rtDesc.Format = m_backBufferFormat;
        rtDesc.SampleDesc.Count = kMsaaSampleCount;
        rtDesc.SampleDesc.Quality = 0;
        rtDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
        rtDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

        D3D12_CLEAR_VALUE clearValue = {};
        clearValue.Format = m_backBufferFormat;

        const auto heapProps = HeapProps(D3D12_HEAP_TYPE_DEFAULT);
        ThrowIfFailed(
            m_device->CreateCommittedResource(&heapProps,
                                             D3D12_HEAP_FLAG_NONE,
                                             &rtDesc,
                                             D3D12_RESOURCE_STATE_RENDER_TARGET,
                                             &clearValue,
                                             IID_PPV_ARGS(&m_msaaRT)),
            "Create MSAA RT");

        D3D12_CPU_DESCRIPTOR_HANDLE handle = m_rtvHeap->GetCPUDescriptorHandleForHeapStart();
        handle.ptr += static_cast<SIZE_T>(kFrameCount) * m_rtvDescriptorSize;
        m_device->CreateRenderTargetView(m_msaaRT.Get(), nullptr, handle);
    }

    void CreateRootSignature() {
        D3D12_ROOT_PARAMETER parameter = {};
        parameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        parameter.ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
        parameter.Constants.Num32BitValues = 4;
        parameter.Constants.ShaderRegister = 0;
        parameter.Constants.RegisterSpace = 0;

        D3D12_ROOT_SIGNATURE_DESC desc = {};
        desc.NumParameters = 1;
        desc.pParameters = &parameter;
        desc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;

        ComPtr<ID3DBlob> serialized;
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3D12SerializeRootSignature(&desc, D3D_ROOT_SIGNATURE_VERSION_1, &serialized, &errors);
        if (FAILED(hr)) {
            std::string message = "D3D12SerializeRootSignature failed";
            if (errors) {
                message.append(": ");
                message.append(static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize());
            }
            throw std::runtime_error(message);
        }
        ThrowIfFailed(m_device->CreateRootSignature(0, serialized->GetBufferPointer(), serialized->GetBufferSize(), IID_PPV_ARGS(&m_rootSignature)), "CreateRootSignature");
    }

    static ComPtr<ID3DBlob> CompileShader(const char* source, const char* entry, const char* target) {
        UINT flags = D3DCOMPILE_ENABLE_STRICTNESS;
#if defined(_DEBUG)
        flags |= D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#endif
        ComPtr<ID3DBlob> bytecode;
        ComPtr<ID3DBlob> errors;
        HRESULT hr = D3DCompile(source, std::strlen(source), nullptr, nullptr, nullptr, entry, target, flags, 0, &bytecode, &errors);
        if (FAILED(hr)) {
            std::string message = std::string("D3DCompile failed for ") + entry + "/" + target;
            if (errors) {
                message.append(": ");
                message.append(static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize());
            }
            throw std::runtime_error(message);
        }
        return bytecode;
    }

    void CreatePipelineStates() {
        static const char* kShaderSource = R"(
cbuffer DrawColor : register(b0)
{
    float4 g_Color;
};

struct VSIn
{
    float2 position : POSITION;
};

struct VSOut
{
    float4 position : SV_POSITION;
};

VSOut VSMain(VSIn input)
{
    VSOut output;
    output.position = float4(input.position, 0.0f, 1.0f);
    return output;
}

float4 PSMain() : SV_TARGET
{
    return g_Color;
}
)";

        const ComPtr<ID3DBlob> vs = CompileShader(kShaderSource, "VSMain", "vs_5_0");
        const ComPtr<ID3DBlob> ps = CompileShader(kShaderSource, "PSMain", "ps_5_0");

        D3D12_INPUT_ELEMENT_DESC inputElement = {};
        inputElement.SemanticName = "POSITION";
        inputElement.Format = DXGI_FORMAT_R32G32_FLOAT;
        inputElement.InputSlotClass = D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA;

        D3D12_GRAPHICS_PIPELINE_STATE_DESC desc = {};
        desc.InputLayout = {&inputElement, 1};
        desc.pRootSignature = m_rootSignature.Get();
        desc.VS = {vs->GetBufferPointer(), vs->GetBufferSize()};
        desc.PS = {ps->GetBufferPointer(), ps->GetBufferSize()};
        desc.RasterizerState = DefaultRasterizerDesc();
        desc.BlendState = DefaultBlendDesc();
        desc.DepthStencilState = DefaultDepthStencilDesc();
        desc.SampleMask = UINT_MAX;
        desc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        desc.NumRenderTargets = 1;
        desc.RTVFormats[0] = m_backBufferFormat;
        desc.DSVFormat = m_depthStencilFormat;
        desc.SampleDesc.Count = kMsaaSampleCount;

        auto alphaBlend = DefaultBlendDesc();
        alphaBlend.RenderTarget[0].BlendEnable = TRUE;
        alphaBlend.RenderTarget[0].SrcBlend = D3D12_BLEND_SRC_ALPHA;
        alphaBlend.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
        alphaBlend.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
        alphaBlend.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
        alphaBlend.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
        alphaBlend.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;

        auto depthDisabled = DefaultDepthStencilDesc();
        depthDisabled.DepthEnable = FALSE;
        depthDisabled.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ZERO;

        desc.BlendState = alphaBlend;
        desc.DepthStencilState = depthDisabled;
        ThrowIfFailed(m_device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&m_psoColor)), "Create PSO Color");

        auto colorStencil = depthDisabled;
        colorStencil.StencilEnable = TRUE;
        colorStencil.StencilReadMask = 0xFF;
        colorStencil.StencilWriteMask = 0x00;
        colorStencil.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        colorStencil.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        colorStencil.FrontFace.StencilPassOp = D3D12_STENCIL_OP_KEEP;
        colorStencil.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_NOT_EQUAL;
        colorStencil.BackFace = colorStencil.FrontFace;
        desc.DepthStencilState = colorStencil;
        ThrowIfFailed(m_device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&m_psoColorStencil)), "Create PSO ColorStencil");

        auto writeMaskOff = DefaultBlendDesc();
        writeMaskOff.RenderTarget[0].RenderTargetWriteMask = 0;

        auto fillEvenOdd = depthDisabled;
        fillEvenOdd.StencilEnable = TRUE;
        fillEvenOdd.StencilReadMask = 0xFF;
        fillEvenOdd.StencilWriteMask = 0xFF;
        fillEvenOdd.FrontFace.StencilFailOp = D3D12_STENCIL_OP_KEEP;
        fillEvenOdd.FrontFace.StencilDepthFailOp = D3D12_STENCIL_OP_KEEP;
        fillEvenOdd.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INVERT;
        fillEvenOdd.FrontFace.StencilFunc = D3D12_COMPARISON_FUNC_ALWAYS;
        fillEvenOdd.BackFace = fillEvenOdd.FrontFace;
        desc.BlendState = writeMaskOff;
        desc.DepthStencilState = fillEvenOdd;
        ThrowIfFailed(m_device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&m_psoFillEvenOddStencil)), "Create PSO FillEvenOdd");

        auto fillNonZero = fillEvenOdd;
        fillNonZero.FrontFace.StencilPassOp = D3D12_STENCIL_OP_INCR;
        fillNonZero.BackFace.StencilPassOp = D3D12_STENCIL_OP_DECR;
        desc.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
        desc.DepthStencilState = fillNonZero;
        ThrowIfFailed(m_device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&m_psoFillNonZeroStencil)), "Create PSO FillNonZero");
    }

    void CreateDynamicUploadBuffers(UINT capacityBytes) {
        for (UINT i = 0; i < kFrameCount; ++i) {
            FrameUploadBuffer& upload = m_uploadBuffers[i];
            const auto heapProps = HeapProps(D3D12_HEAP_TYPE_UPLOAD);
            const auto bufDesc = BufferDesc(capacityBytes);
            ThrowIfFailed(m_device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufDesc,
                                                            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
                                                            IID_PPV_ARGS(&upload.resource)),
                          "Create Upload Buffer");
            D3D12_RANGE readRange = {};
            ThrowIfFailed(upload.resource->Map(0, &readRange, reinterpret_cast<void**>(&upload.mapped)), "Map Upload Buffer");
            upload.capacityBytes = capacityBytes;
            upload.offsetBytes = 0;
        }
    }

    void UpdateViewport() {
        m_viewport = {};
        m_viewport.TopLeftX = 0.0f;
        m_viewport.TopLeftY = 0.0f;
        m_viewport.Width = static_cast<float>(m_width);
        m_viewport.Height = static_cast<float>(m_height);
        m_viewport.MinDepth = 0.0f;
        m_viewport.MaxDepth = 1.0f;
        m_scissorRect = {0, 0, static_cast<LONG>(m_width), static_cast<LONG>(m_height)};
    }

    D3D12_CPU_DESCRIPTOR_HANDLE MsaaRTV() const {
        D3D12_CPU_DESCRIPTOR_HANDLE handle = m_rtvHeap->GetCPUDescriptorHandleForHeapStart();
        handle.ptr += static_cast<SIZE_T>(kFrameCount) * m_rtvDescriptorSize;
        return handle;
    }

    UploadSlice UploadLocalVertices(const std::vector<Vec2>& localVertices, const Affine2D& transform) {
        std::vector<ClipVertex> clipVerts;
        clipVerts.reserve(localVertices.size());
        const float w = static_cast<float>(m_width);
        const float h = static_cast<float>(m_height);
        for (const Vec2& local : localVertices) {
            const Vec2 pixel = TransformPoint(transform, local);
            const Vec2 clip = PixelToClip(pixel, w, h);
            clipVerts.push_back({clip.x, clip.y});
        }

        const UINT sizeBytes = static_cast<UINT>(clipVerts.size() * sizeof(ClipVertex));
        FrameUploadBuffer& upload = m_uploadBuffers[m_frameIndex];
        const UINT aligned = AlignUp(upload.offsetBytes, 4);
        if (aligned + sizeBytes > upload.capacityBytes) {
            UploadSlice empty{};
            return empty;
        }

        std::memcpy(upload.mapped + aligned, clipVerts.data(), sizeBytes);
        UploadSlice slice{};
        slice.view.BufferLocation = upload.resource->GetGPUVirtualAddress() + aligned;
        slice.view.SizeInBytes = sizeBytes;
        slice.view.StrideInBytes = sizeof(ClipVertex);
        upload.offsetBytes = aligned + sizeBytes;
        return slice;
    }

    void SetColor(const Color& c) {
        const float values[4] = {c.r, c.g, c.b, c.a};
        m_commandList->SetGraphicsRoot32BitConstants(0, 4, values, 0);
    }

    void WaitForGpu() {
        const UINT64 fence = m_fenceValues[m_frameIndex];
        ThrowIfFailed(m_commandQueue->Signal(m_fence.Get(), fence), "Signal");
        ThrowIfFailed(m_fence->SetEventOnCompletion(fence, m_fenceEvent), "SetEventOnCompletion");
        WaitForSingleObjectEx(m_fenceEvent, INFINITE, FALSE);
        m_fenceValues[m_frameIndex] = fence + 1;
    }

    void MoveToNextFrame() {
        const UINT64 currentFence = m_fenceValues[m_frameIndex];
        ThrowIfFailed(m_commandQueue->Signal(m_fence.Get(), currentFence), "Signal");
        m_frameIndex = m_swapChain->GetCurrentBackBufferIndex();

        if (m_fence->GetCompletedValue() < m_fenceValues[m_frameIndex]) {
            ThrowIfFailed(m_fence->SetEventOnCompletion(m_fenceValues[m_frameIndex], m_fenceEvent), "SetEventOnCompletion");
            WaitForSingleObjectEx(m_fenceEvent, INFINITE, FALSE);
        }
        m_fenceValues[m_frameIndex] = currentFence + 1;
    }

    HWND m_hwnd = nullptr;
    UINT m_width = 0;
    UINT m_height = 0;
    UINT m_frameIndex = 0;
    UINT m_rtvDescriptorSize = 0;

    DXGI_FORMAT m_backBufferFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
    DXGI_FORMAT m_depthStencilFormat = DXGI_FORMAT_D24_UNORM_S8_UINT;

    ComPtr<IDXGIFactory4> m_factory;
    ComPtr<ID3D12Device> m_device;
    ComPtr<ID3D12CommandQueue> m_commandQueue;
    ComPtr<IDXGISwapChain3> m_swapChain;
    ComPtr<ID3D12DescriptorHeap> m_rtvHeap;
    ComPtr<ID3D12DescriptorHeap> m_dsvHeap;
    std::array<ComPtr<ID3D12CommandAllocator>, kFrameCount> m_commandAllocators;
    ComPtr<ID3D12GraphicsCommandList> m_commandList;
    std::array<ComPtr<ID3D12Resource>, kFrameCount> m_renderTargets;
    ComPtr<ID3D12Resource> m_depthStencil;
    ComPtr<ID3D12Resource> m_msaaRT;

    ComPtr<ID3D12RootSignature> m_rootSignature;
    ComPtr<ID3D12PipelineState> m_psoFillNonZeroStencil;
    ComPtr<ID3D12PipelineState> m_psoFillEvenOddStencil;
    ComPtr<ID3D12PipelineState> m_psoColorStencil;
    ComPtr<ID3D12PipelineState> m_psoColor;

    ComPtr<ID3D12Fence> m_fence;
    HANDLE m_fenceEvent = nullptr;
    std::array<UINT64, kFrameCount> m_fenceValues{};

    D3D12_VIEWPORT m_viewport{};
    D3D12_RECT m_scissorRect{};

    std::array<FrameUploadBuffer, kFrameCount> m_uploadBuffers{};
};

} // namespace pure_d3d12_path
