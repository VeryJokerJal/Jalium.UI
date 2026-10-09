#include "jalium_api.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#import <AppKit/AppKit.h>
#import <ImageIO/ImageIO.h>
#import <Metal/Metal.h>

extern "C" void jalium_metal_init();
static void Check(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}

static void CheckPrecompiledShaders() {
    const char* directory = std::getenv("JALIUM_METALLIB_DIR");
    if (!directory || !*directory) return; // Source-only runs may compile the embedded fallback.
    NSString* path = [[NSString stringWithUTF8String:directory]
        stringByAppendingPathComponent:@"jalium_core.metallib"];
    id<MTLDevice> device = MTLCreateSystemDefaultDevice();
    NSError* error = nil;
    id<MTLLibrary> library = [device newLibraryWithURL:[NSURL fileURLWithPath:path] error:&error];
    Check(library != nil && [library newFunctionWithName:@"jalium_core_abi_v5"] != nil,
        "Precompiled core shaders must match path rendering ABI v5; embedded fallback is not bundle validation");
    std::puts("Metal paths: precompiled shader ABI v5 verified");
}

struct Surface {
    std::unique_ptr<JaliumContext, decltype(&jalium_context_destroy)> context;
    std::unique_ptr<JaliumRenderTarget, decltype(&jalium_render_target_destroy)> target;
    NSView* view;
    int width, height;
    float scale;
    Surface(float dpiScale, uint32_t samples, int logicalSize = 128)
        : context(jalium_context_create(JALIUM_BACKEND_METAL), jalium_context_destroy),
          target(nullptr, jalium_render_target_destroy),
          view([[NSView alloc] initWithFrame:NSMakeRect(0, 0, logicalSize, logicalSize)]),
          width(std::lround(logicalSize * dpiScale)), height(width), scale(dpiScale) {
        Check(context != nullptr, "context");
        JaliumSurfaceDescriptor descriptor{};
        descriptor.platform = JALIUM_PLATFORM_MACOS;
        descriptor.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
        descriptor.handle0 = reinterpret_cast<intptr_t>((__bridge void*)view);
        target.reset(jalium_render_target_create_for_surface(context.get(), &descriptor, width, height));
        Check(target != nullptr, "target");
        jalium_render_target_set_dpi(target.get(), 96 * scale, 96 * scale);
        jalium_render_target_set_path_msaa(target.get(), samples);
    }
    void Begin() {
        Check(jalium_render_target_request_readback(target.get()) == JALIUM_OK, "readback request");
        Check(jalium_render_target_begin_draw(target.get()) == JALIUM_OK, "begin");
        jalium_render_target_clear(target.get(), 0, 0, 0, 0);
    }
    std::vector<uint8_t> End() {
        Check(jalium_render_target_end_draw(target.get()) == JALIUM_OK, "end");
        std::vector<uint8_t> pixels(width * height * 4);
        int32_t w, h;
        Check(jalium_render_target_fetch_readback(target.get(), pixels.data(), width * 4, &w, &h) == JALIUM_OK,
            "readback");
        return pixels;
    }
};

struct Path {
    CGMutablePathRef reference = CGPathCreateMutable();
    std::vector<float> commands;
    ~Path() { CGPathRelease(reference); }
    void Move(float x, float y) {
        commands.insert(commands.end(), {2, x, y});
        CGPathMoveToPoint(reference, nullptr, x, y);
    }
    void Line(float x, float y) {
        commands.insert(commands.end(), {0, x, y});
        CGPathAddLineToPoint(reference, nullptr, x, y);
    }
    void Cubic(float x1, float y1, float x2, float y2, float x, float y) {
        commands.insert(commands.end(), {1, x1, y1, x2, y2, x, y});
        CGPathAddCurveToPoint(reference, nullptr, x1, y1, x2, y2, x, y);
    }
    void Quad(float cx, float cy, float x, float y) {
        commands.insert(commands.end(), {3, cx, cy, x, y});
        CGPathAddQuadCurveToPoint(reference, nullptr, cx, cy, x, y);
    }
    void Close() { commands.push_back(5); CGPathCloseSubpath(reference); }
    void Rect(float x, float y, float size, bool reverse = false) {
        Move(x, y);
        if (reverse) { Line(x, y + size); Line(x + size, y + size); Line(x + size, y); }
        else { Line(x + size, y); Line(x + size, y + size); Line(x, y + size); }
        Close();
    }
};

static void Save(const std::vector<uint8_t>& pixels, int width, int height, const std::string& name) {
    const char* directory = std::getenv("JALIUM_PATH_ARTIFACT_DIR");
    if (!directory) return;
    CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGDataProviderRef provider = CGDataProviderCreateWithData(nullptr, pixels.data(), pixels.size(), nullptr);
    CGImageRef image = CGImageCreate(width, height, 8, 32, width * 4, space,
        kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little, provider, nullptr, false,
        kCGRenderingIntentDefault);
    NSString* output = [NSString stringWithFormat:@"%s/%s.png", directory, name.c_str()];
    CGImageDestinationRef destination = CGImageDestinationCreateWithURL(
        (__bridge CFURLRef)[NSURL fileURLWithPath:output], CFSTR("public.png"), 1, nullptr);
    Check(image && destination, "artifact");
    CGImageDestinationAddImage(destination, image, nullptr);
    Check(CGImageDestinationFinalize(destination), "encode artifact");
    CFRelease(destination); CGImageRelease(image); CGDataProviderRelease(provider); CGColorSpaceRelease(space);
}

static unsigned checks = 0;
static void Compare(Surface& s, Path& path, const char* name, bool stroke = false,
    int rule = 0, int cap = 0, int join = 0, const std::vector<float>& dash = {}, float phase = 0,
    const float* transform = nullptr, int edgeMode = -1, float thickness = 8,
    bool asClip = false, int clipDepth = 0, bool bitmap = false, int nativeJoin = -1) {
    constexpr float alpha = 0.5f;
    auto brush = std::unique_ptr<JaliumBrush, decltype(&jalium_brush_destroy)>(
        jalium_brush_create_solid(s.context.get(), 0.2f, 0.8f, 0.6f, alpha), jalium_brush_destroy);
    s.Begin();
    if (transform) jalium_push_transform(s.target.get(), transform);
    if (asClip) {
        auto commands = path.commands;
        if (edgeMode > 0) commands.insert(commands.begin(), {8, float(edgeMode)});
        const auto result = stroke ? jalium_push_stroke_path_clip(s.target.get(), 0, 0,
            commands.data(), commands.size(), thickness, 0, nativeJoin<0?join:nativeJoin, 3, cap, dash.data(), dash.size(), phase, edgeMode) :
            jalium_push_path_clip(s.target.get(), 0, 0, commands.data(), commands.size(), rule);
        Check(result == JALIUM_OK, "path clip");
        if (transform) jalium_pop_transform(s.target.get());
        for (int i = 0; i < clipDepth; ++i) jalium_push_clip(s.target.get(), 0, 0, 128, 128);
        if (bitmap) {
            const uint8_t pixel[]{153,204,51,255}; // straight BGRA, teal
            JaliumImage* image = jalium_bitmap_create_from_pixels(s.context.get(),pixel,1,1,4);
            Check(image != nullptr,"bitmap");
            jalium_draw_bitmap(s.target.get(),image,0,0,128,128,alpha);
            jalium_bitmap_destroy(image);
        } else jalium_draw_fill_rectangle(s.target.get(), 0, 0, 128, 128, brush.get());
        for (int i = 0; i <= clipDepth; ++i) jalium_pop_clip(s.target.get());
    } else if (stroke) jalium_stroke_path(s.target.get(), 0, 0, path.commands.data(), path.commands.size(), brush.get(),
        thickness, 0, nativeJoin<0?join:nativeJoin, 3, cap, dash.data(), dash.size(), phase, edgeMode);
    else jalium_fill_path(s.target.get(), 0, 0, path.commands.data(), path.commands.size(), brush.get(), rule, edgeMode);
    if (transform && !asClip) jalium_pop_transform(s.target.get());
    auto actual = s.End();
    std::vector<uint8_t> expected(actual.size());
    CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
    CGContextRef context = CGBitmapContextCreate(expected.data(), s.width, s.height, 8, s.width * 4, space,
        kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
    Check(context != nullptr, "reference context");
    CGContextTranslateCTM(context, 0, s.height); CGContextScaleCTM(context, s.scale, -s.scale);
    CGContextSetShouldAntialias(context, edgeMode != 1);
    if (transform) CGContextConcatCTM(context, CGAffineTransformMake(
        transform[0], transform[1], transform[2], transform[3], transform[4], transform[5]));
    CGContextSetRGBFillColor(context, 0.2, 0.8, 0.6, alpha);
    CGContextSetRGBStrokeColor(context, 0.2, 0.8, 0.6, alpha);
    CGContextSetLineWidth(context, thickness); CGContextSetMiterLimit(context, 3);
    CGContextSetLineCap(context, cap == 1 ? kCGLineCapSquare : cap == 2 ? kCGLineCapRound : kCGLineCapButt);
    CGContextSetLineJoin(context, join == 1 ? kCGLineJoinBevel : join == 2 ? kCGLineJoinRound : kCGLineJoinMiter);
    std::vector<CGFloat> cgDash(dash.begin(), dash.end());
    if (!dash.empty()) CGContextSetLineDash(context, phase, cgDash.data(), cgDash.size());
    CGContextAddPath(context, path.reference);
    if (stroke && std::string(name) == "closed-dash-seam") {
        // Quartz separates the first and last dash at ClosePath. The path
        // contract joins their common vertex whenever both sides are on.
        // Keep Quartz's independent dash lengths and add the closing join to
        // the same stroke operation, so the reference still paints alpha once.
        CGPathRef dashed = CGPathCreateCopyByDashingPath(path.reference, nullptr,
            phase, cgDash.data(), cgDash.size());
        CGContextBeginPath(context); CGContextAddPath(context, dashed);
        CGContextSetLineDash(context, 0, nullptr, 0);
        CGContextMoveToPoint(context, 16, 20);
        CGContextAddLineToPoint(context, 16, 16); CGContextAddLineToPoint(context, 20, 16);
        CGContextStrokePath(context); CGPathRelease(dashed);
    } else if (stroke) CGContextStrokePath(context);
    else if (rule == 0) CGContextEOFillPath(context);
    else CGContextFillPath(context);
    CGContextRelease(context); CGColorSpaceRelease(space);
    std::vector<uint8_t> boundary(s.width * s.height);
    if (edgeMode == 1) {
        // Quartz's bitmap aliased mode snaps curve edges differently. The
        // API's binary contract is instead the exact pixel-center predicate.
        CGPathRef dashed = dash.empty() ? CGPathCreateCopy(path.reference) :
            CGPathCreateCopyByDashingPath(path.reference, nullptr, phase, cgDash.data(), cgDash.size());
        CGPathRef shape = stroke ? CGPathCreateCopyByStrokingPath(dashed, nullptr, thickness,
            cap == 1 ? kCGLineCapSquare : cap == 2 ? kCGLineCapRound : kCGLineCapButt,
            join == 1 ? kCGLineJoinBevel : join == 2 ? kCGLineJoinRound : kCGLineJoinMiter, 3) :
            CGPathCreateCopy(dashed);
        const CGAffineTransform inverse = transform ? CGAffineTransformInvert(CGAffineTransformMake(
            transform[0], transform[1], transform[2], transform[3], transform[4], transform[5])) :
            CGAffineTransformIdentity;
        CGPathRef edge = CGPathCreateCopyByStrokingPath(shape, nullptr, 0.3 / s.scale,
            kCGLineCapRound, kCGLineJoinRound, 3);
        for (int y = 0; y < s.height; ++y) for (int x = 0; x < s.width; ++x) {
            const CGPoint point = CGPointApplyAffineTransform(
                CGPointMake((x + 0.5) / s.scale, (y + 0.5) / s.scale), inverse);
            const bool inside = CGPathContainsPoint(shape, nullptr, point, !stroke && rule == 0);
            const size_t p = (y * s.width + x) * 4;
            boundary[p / 4] = CGPathContainsPoint(edge, nullptr, point, false);
            expected[p] = inside ? 77 : 0; expected[p + 1] = inside ? 102 : 0;
            expected[p + 2] = inside ? 26 : 0; expected[p + 3] = inside ? 128 : 0;
        }
        CGPathRelease(edge); CGPathRelease(shape); CGPathRelease(dashed);
    }
    unsigned wrongInterior = 0, grossErrors = 0;
    double totalError = 0;
    for (size_t p = 0; p < expected.size(); p += 4) {
        const int difference = boundary[p / 4] ? 0 : std::abs(int(actual[p + 3]) - int(expected[p + 3]));
        totalError += difference;
        if (difference > 50) ++grossErrors;
        // Alpha is never allowed to build up at intersections or joins.
        if (actual[p + 3] > 130) ++wrongInterior;
        if (!boundary[p / 4] && expected[p + 3] >= 127 && actual[p + 3] < 108) {
            // A fully covered reference pixel can still touch a curve fringe;
            // different antialias sample patterns may differ there. Require a
            // full 3x3 reference neighborhood before calling this interior.
            const int x=int(p/4)%s.width,y=int(p/4)/s.width;
            bool interior=x>0 && x+1<s.width && y>0 && y+1<s.height;
            for(int dy=-1;interior && dy<=1;++dy) for(int dx=-1;dx<=1;++dx)
                if(expected[((y+dy)*s.width+x+dx)*4+3]<127) interior=false;
            if(interior) ++wrongInterior;
        }
        if (edgeMode == 1 && actual[p + 3] != 0 && actual[p + 3] != 128) ++wrongInterior;
        if (expected[p + 3] >= 127 && actual[p + 3] >= 127)
            for (int c=0;c<3;++c)
                if (std::abs(int(actual[p+c])-int(expected[p+c])) > 2) ++wrongInterior;
    }
    const double meanError = totalError / (s.width * s.height);
    if (wrongInterior > 15 || grossErrors > 15 || meanError > 1.2) {
        Save(actual, s.width, s.height, std::string(name) + "-actual");
        Save(expected, s.width, s.height, std::string(name) + "-reference");
        std::fprintf(stderr, "%s scale %.2f mode %d: interior=%u gross=%u mean=%.3f\n",
            name, s.scale, edgeMode, wrongInterior, grossErrors, meanError);
        throw std::runtime_error(name);
    }
    if (s.scale == 2 && edgeMode == -1) Save(actual, s.width, s.height, name);
    ++checks;
}

static void Matrix(Surface& s) {
    for (int rule : {0, 1}) for (bool reverse : {false, true}) {
        Path overlap; overlap.Rect(12, 12, 64); overlap.Rect(42, 42, 64, reverse);
        Compare(s, overlap, "overlap", false, rule);
        Path nested; nested.Rect(12, 12, 104, reverse); nested.Rect(24, 24, 80, !reverse);
        nested.Rect(40, 40, 48, reverse); nested.Rect(54, 54, 20, !reverse);
        Compare(s, nested, "nested", false, rule);
        Path star;
        for (int i = 0; i < 5; ++i) {
            const float angle = -1.57079633f + i * 2 * 2 * 3.14159265f / 5;
            const float x = 64 + 52 * std::cos(angle), y = 64 + 52 * std::sin(angle);
            if (i == 0) star.Move(x, y); else star.Line(x, y);
        }
        star.Close(); Compare(s, star, "self-intersection", false, rule);
    }
    Path curve; curve.Move(8, 64); curve.Cubic(8, 0, 120, 0, 120, 64);
    curve.Quad(64, 124, 8, 64); curve.Close();
    const float rotate[]{0, 0.8f, -0.8f, 0, 112, 12};
    const float shear[]{0.7f, 0.1f, 0.3f, 0.7f, 0, 5};
    const float reflect[]{-0.8f, 0, 0, 0.8f, 116, 12};
    for (int rule : {0,1}) {
        Path clip; clip.Rect(12,12,104); clip.Rect(38,38,52,true);
        Compare(s,clip,"path-clip",false,rule,0,0,{},0,shear,-1,8,true,24);
        Compare(s,clip,"bitmap-path-clip",false,rule,0,0,{},0,reflect,-1,8,true,0,true);
        clip.commands.insert(clip.commands.begin()+3,{7,0,0,0,6,0});
        Compare(s,clip,"fill-ignores-stroke-metadata",false,rule);
    }
    Compare(s,curve,"curve-clip",false,0,0,0,{},0,rotate,2,8,true);
    Compare(s,curve,"stroke-bitmap-clip",true,0,2,2,{7,4,2},-17,shear,2,8,true,24,true);
    Path gap; gap.Move(16,32); gap.Line(48,32);
    gap.commands.insert(gap.commands.end(),{6,0,0,80,32,6,1});
    CGPathMoveToPoint(gap.reference,nullptr,80,32); gap.Line(112,32);
    Compare(s,gap,"unstroked-segment",true,0,2);
    Path smooth; smooth.Move(16,100); smooth.Line(16,24);
    smooth.commands.insert(smooth.commands.end(),{6,3}); smooth.Line(100,24);
    Compare(s,smooth,"smooth-join",true,0,0,2,{},0,nullptr,2,16,false,0,false,0);
    for (auto matrix : {rotate, shear, reflect}) {
        Compare(s, curve, "transformed-curves", false, 0, 0, 0, {}, 0, matrix);
        Compare(s, curve, "transformed-stroke", true, 0, 2, 2, {}, 0, matrix);
    }
    Path corners; corners.Move(16, 104); corners.Line(64, 20); corners.Line(112, 104);
    for (int cap : {0, 1, 2}) for (int join : {0, 1, 2})
        Compare(s, corners, "caps-joins", true, 0, cap, join);
    Path mixed; mixed.Rect(16, 16, 48); mixed.Move(80, 16); mixed.Line(110, 16); mixed.Line(110, 64);
    mixed.Move(16, 96); mixed.Line(110, 96); mixed.Move(64, 72); mixed.Line(64, 116);
    Compare(s, mixed, "closed-open-crossing", true, 0, 2, 2);
    for (const std::vector<float>& pattern : {std::vector<float>{12, 5}, {7, 4, 2}, {0, 12}})
        for (float phase : {-17.0f, 0.0f, 5.0f})
            Compare(s, corners, "dashes", true, 0, 2, 2, pattern, phase);
    Path closed; closed.Rect(16, 16, 96);
    Compare(s, closed, "closed-dash-seam", true, 0, 0, 0, {18, 7}, 3);
    for (int mode : {1, 2}) {
        Compare(s, curve, "edge-mode", false, 0, 0, 0, {}, 0, nullptr, mode);
        Compare(s, corners, "stroke-edge-mode", true, 0, 2, 2, {}, 0, nullptr, mode);
    }
    Path winding;
    for (int i = 0; i < 260; ++i) winding.Rect(16, 16, 96);
    Compare(s, winding, "winding-overflow", false, 1);
    // A giant offscreen figure must be clipped before row allocation.
    Path huge; huge.Rect(-10000000, -10000000, 20000000);
    Compare(s, huge, "offscreen-bounds", false, 1);
}

static int Alpha(const Surface& s,const std::vector<uint8_t>& pixels,float x,float y) {
    int px=std::clamp(int(x*s.scale),0,s.width-1), py=std::clamp(int(y*s.scale),0,s.height-1);
    return pixels[(py*s.width+px)*4+3];
}

static void SpecialStyles(Surface& s) {
    JaliumBrush* brush=jalium_brush_create_solid(s.context.get(),1,0,0,0.5);
    Check(brush!=nullptr,"special brush");
    Path line; line.Move(16,64); line.commands.insert(line.commands.end(),{7,1,3,0}); line.Line(112,64);
    s.Begin();
    jalium_stroke_path(s.target.get(),0,0,line.commands.data(),line.commands.size(),brush,8,0,0,3,0,nullptr,0,0,2);
    auto pixels=s.End();
    Check(Alpha(s,pixels,13,67)>120,"independent square start cap");
    Check(Alpha(s,pixels,114,64)>120 && Alpha(s,pixels,114,67)<10,"independent triangle end cap");
    Path dot; dot.Move(64,64); dot.Line(64,64);
    s.Begin();
    jalium_stroke_path(s.target.get(),0,0,dot.commands.data(),dot.commands.size(),brush,12,0,0,3,2,nullptr,0,0,2);
    pixels=s.End();
    Check(Alpha(s,pixels,64,64)>120 && Alpha(s,pixels,72,64)<10,"zero-length round stroke");
    Path omitted; omitted.Move(64,64);
    omitted.commands.insert(omitted.commands.end(),{4,64,64,20,12,30,1,1});
    Compare(s,omitted,"identical-endpoint-arc",true,0,2,2);
    Path zeroRadius; zeroRadius.Move(16,32);
    zeroRadius.commands.insert(zeroRadius.commands.end(),{4,112,96,0,12,30,1,1});
    CGPathAddLineToPoint(zeroRadius.reference,nullptr,112,96);
    Compare(s,zeroRadius,"zero-radius-arc",true,0,2,2);
    jalium_brush_destroy(brush);
    checks+=3;
}

static void EllipticalArcs(Surface& s) {
    constexpr float angle=0.5235987756f;
    CGAffineTransform ellipse=CGAffineTransformMake(44*std::cos(angle),44*std::sin(angle),
        -24*std::sin(angle),24*std::cos(angle),64,64);
    for(bool large : {false,true}) for(bool clockwise : {false,true}) {
        float begin=3.1415926536f,end=begin+(large?4.2f:1.2f)*(clockwise?1:-1);
        CGPoint start=CGPointApplyAffineTransform(CGPointMake(std::cos(begin),std::sin(begin)),ellipse);
        CGPoint finish=CGPointApplyAffineTransform(CGPointMake(std::cos(end),std::sin(end)),ellipse);
        Path path; path.Move(start.x,start.y);
        path.commands.insert(path.commands.end(),{4,float(finish.x),float(finish.y),44,24,30,float(large),float(clockwise)});
        CGPathAddArc(path.reference,&ellipse,0,0,1,begin,end,!clockwise);
        Compare(s,path,"elliptical-arc",false,0,0,0,{},0,nullptr,2);
        Compare(s,path,"elliptical-arc-default",false,0);
        Compare(s,path,"elliptical-arc-stroke",true,0,2,2,{},0,nullptr,2);
        CGAffineTransform normalized=ellipse;
        normalized.a*=0.001; normalized.b*=0.001; normalized.c*=0.001; normalized.d*=0.001;
        normalized.tx*=0.001; normalized.ty*=0.001;
        Path small; small.Move(start.x*0.001,start.y*0.001);
        small.commands.insert(small.commands.end(),{4,float(finish.x*0.001),float(finish.y*0.001),0.044f,0.024f,30,float(large),float(clockwise)});
        CGPathAddArc(small.reference,&normalized,0,0,1,begin,end,!clockwise);
        const float magnify[]{1000,0,0,1000,0,0};
        Compare(s,small,"normalized-arc",false,0,0,0,{},0,magnify,2);
        Compare(s,small,"normalized-arc-default",false,0,0,0,{},0,magnify);
        Compare(s,small,"normalized-arc-stroke",true,0,2,2,{},0,magnify,2,0.008f);
    }
    Path corrected; corrected.Move(16,64);
    corrected.commands.insert(corrected.commands.end(),{4,112,64,1,0.5f,0,0,1});
    const CGAffineTransform ellipseCorrection=CGAffineTransformMake(48,0,0,24,64,64);
    CGPathAddArc(corrected.reference,&ellipseCorrection,0,0,1,3.141592653589793,2*3.141592653589793,false);
    Compare(s,corrected,"corrected-radius-arc",false,0);
}

static void GradientColors(Surface& s,int edgeMode=2) {
    Path path; path.Rect(8,8,112); path.Rect(48,48,32,true);
    for (int count : {2,32,40,1024}) for (bool radial : {false,true}) {
        std::vector<JaliumGradientStop> stops;
        for(int i=0;i<count;++i) {
            float t=float(i)/(count-1);
            // A discontinuity late in the table proves every stop is retained.
            stops.push_back({t,t<0.82f?0.0f:1.0f,1-t,t,0.5f});
        }
        JaliumBrush* brush=radial ? jalium_brush_create_radial_gradient(s.context.get(),64,64,56,40,
            42,64,stops.data(),stops.size(),0) :
            jalium_brush_create_linear_gradient(s.context.get(),8,8,120,8,stops.data(),stops.size(),0);
        Check(brush!=nullptr,"gradient brush");
        s.Begin(); jalium_fill_path(s.target.get(),0,0,path.commands.data(),path.commands.size(),brush,1,edgeMode);
        auto pixels=s.End();
        for(int y=12;y<116;y+=8) for(int x=12;x<116;x+=8) {
            float px=(int(x*s.scale)+0.5f)/s.scale, py=(int(y*s.scale)+0.5f)/s.scale;
            size_t p=(int(y*s.scale)*s.width+int(x*s.scale))*4;
            if(px>48 && px<80 && py>48 && py<80) {
                Check(pixels[p+3]==0,"gradient preserves hole"); continue;
            }
            float t=(px-8)/112;
            if(radial) {
                float fx=-22.0f/56,qx=(px-42)/56,qy=(py-64)/40;
                float a=qx*qx+qy*qy,b=fx*qx;
                t=a>1e-12f ? a/(-b+std::sqrt(b*b+a*(1-fx*fx))) : 0;
            }
            t=std::clamp(t,0.0f,1.0f);
            int left=std::min(int(t*(count-1)),count-2),right=left+1;
            float u=(t-stops[left].position)/(stops[right].position-stops[left].position);
            const float color[]{stops[left].b*(1-u)+stops[right].b*u,
                stops[left].g*(1-u)+stops[right].g*u,stops[left].r*(1-u)+stops[right].r*u,1};
            for(int c=0;c<4;++c)
                Check(std::abs(int(pixels[p+c])-int(std::lround(color[c]*127.5f)))<=2,"gradient color");
        }
        if(s.scale==2) Save(pixels,s.width,s.height,std::string(radial?"radial-":"linear-")+std::to_string(count));
        jalium_brush_destroy(brush); ++checks;
    }
}

static void DamageAndCaptures(Surface& s) {
    Path clip; clip.Rect(12,12,104); clip.Rect(48,48,32,true);
    Path content; content.Move(8,64); content.Cubic(8,0,120,0,120,64);
    content.Quad(64,124,8,64); content.Close();
    JaliumBrush* background=jalium_brush_create_solid(s.context.get(),1,1,1,1);
    JaliumBrush* brush=jalium_brush_create_solid(s.context.get(),0.2f,0.8f,0.6f,0.5f);
    auto paint=[&] {
        jalium_draw_fill_rectangle(s.target.get(),0,0,128,128,background);
        Check(jalium_push_path_clip(s.target.get(),0,0,clip.commands.data(),clip.commands.size(),1)==JALIUM_OK,"capture clip");
        jalium_effect_begin_capture(s.target.get(),0,0,128,128);
        jalium_fill_path(s.target.get(),0,0,content.commands.data(),content.commands.size(),brush,0,2);
        jalium_effect_end_capture(s.target.get());
        const float matrix[20]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1,0,0,0,0};
        Check(jalium_draw_color_matrix_chain_effect(s.target.get(),0,0,128,128,matrix,1)==JALIUM_OK,"path color matrix capture");
        jalium_pop_clip(s.target.get());
    };
    s.Begin(); paint(); auto reference=s.End();
    Check(reference[(int(64*s.scale)*s.width+int(64*s.scale))*4+2]==255,"capture preserves hole");
    Check(reference[(int(30*s.scale)*s.width+int(64*s.scale))*4+2]<220,"capture paints interior");
    for(int frame=0;frame<4;++frame) {
        jalium_render_target_add_dirty_rect(s.target.get(),10+frame*7,10,42,60);
        Check(jalium_render_target_request_readback(s.target.get())==JALIUM_OK,"damage readback");
        Check(jalium_render_target_begin_draw(s.target.get())==JALIUM_OK,"damage begin");
        paint(); auto partial=s.End();
        for(size_t p=0;p<partial.size();++p)
            Check(std::abs(int(partial[p])-int(reference[p]))<=1,"path capture dirty redraw");
        ++checks;
    }
    jalium_render_target_set_full_invalidation(s.target.get());
    s.Begin();
    void* layer=jalium_render_target_realize_layer_begin(s.target.get(),nullptr,0,0,128,128);
    Check(layer!=nullptr,"path retained layer");
    Check(jalium_push_path_clip(s.target.get(),0,0,clip.commands.data(),clip.commands.size(),1)==JALIUM_OK,"retained clip");
    jalium_fill_path(s.target.get(),0,0,content.commands.data(),content.commands.size(),brush,0,2);
    jalium_pop_clip(s.target.get());
    jalium_render_target_realize_layer_end(s.target.get(),layer);
    jalium_render_target_composite_layer(s.target.get(),layer,0,0,128,128,1);
    auto pixels=s.End();
    Check(Alpha(s,pixels,64,64)==0 && Alpha(s,pixels,64,30)>120,"retained path and hole");
    jalium_render_target_destroy_retained_layer(s.target.get(),layer);
    jalium_brush_destroy(brush); jalium_brush_destroy(background); ++checks;
}

int main() {
    @autoreleasepool {
        try {
            CheckPrecompiledShaders();
            [NSApplication sharedApplication]; jalium_metal_init();
            for (float scale : {1.0f, 1.5f, 2.0f}) for (uint32_t samples : {1u, 4u}) {
                Surface surface(scale, samples); Matrix(surface); EllipticalArcs(surface); SpecialStyles(surface);
                GradientColors(surface); GradientColors(surface,-1); DamageAndCaptures(surface);
                Check(jalium_render_target_set_engine(surface.target.get(),JALIUM_ENGINE_VELLO)==JALIUM_OK,"Vello engine");
                Check(jalium_render_target_get_engine(surface.target.get())==JALIUM_ENGINE_VELLO,"active Vello engine");
                Matrix(surface); EllipticalArcs(surface); GradientColors(surface,-1);
            }
            Surface large(2, 4, 400);
            Path overlap; overlap.Rect(16, 16, 320); overlap.Rect(80, 80, 304);
            for (int rule : {0, 1}) Compare(large, overlap, "large-stencil-overlap", false, rule);
            Path nested; nested.Rect(16, 16, 368); nested.Rect(48, 48, 304, true);
            Compare(large, nested, "large-stencil-hole", false, 1);
            Path overflow; for (int i = 0; i < 260; ++i) overflow.Rect(16, 16, 368);
            Compare(large, overflow, "large-winding-overflow", false, 1);
            Path arc; arc.Move(300,200);
            arc.commands.insert(arc.commands.end(),{4,-3300,200,-1800,1600,0,0,1,4,300,200,1800,1600,0,0,1,5});
            // Quartz's four-cubic ellipse has ~0.5 DIP geometric error at this
            // radius. Short independent Quartz arcs keep the oracle precise.
            CGAffineTransform ellipse=CGAffineTransformMake(1800,0,0,1600,-1500,200);
            for(int i=0;i<16;++i) CGPathAddArc(arc.reference,&ellipse,0,0,1,
                i*3.141592653589793/8,(i+1)*3.141592653589793/8,false);
            CGPathCloseSubpath(arc.reference);
            Compare(large,arc,"large-adaptive-arc",false,0,0,0,{},0,nullptr,2);
            Check(jalium_render_target_set_engine(large.target.get(),JALIUM_ENGINE_VELLO)==JALIUM_OK,"large Vello engine");
            Compare(large,arc,"large-default-vello-arc",false,0);
            std::printf("Metal paths: %u GPU geometry, brush and capture checks passed\n", checks);
        } catch (const std::exception& e) {
            std::fprintf(stderr, "Metal paths failed: %s\n", e.what()); return 1;
        }
    }
    return 0;
}
