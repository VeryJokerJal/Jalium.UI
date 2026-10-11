#pragma once

namespace jalium {

// Core renderer MSL. Built-in production packages compile the same source to
// metallib at build time; newLibraryWithSource keeps native tests and source
// checkouts self-contained and is also the fallback when the packaged library
// version does not match the C++ parameter ABI.
inline constexpr const char* kMetalCoreShaderSource = R"METAL(
#include <metal_stdlib>
using namespace metal;

struct VertexIn { packed_float4 data; };
kernel void jalium_core_abi_v5() {}
struct VertexOut {
    float4 position [[position]];
    float2 uv [[user(locn0)]];
    float2 pixel [[user(locn1)]];
};

vertex VertexOut jalium_vertex(
    const device VertexIn* vertices [[buffer(0)]],
    constant float* p [[buffer(1)]],
    uint vid [[vertex_id]])
{
    const float4 v = float4(vertices[vid].data);
    const float2 viewport = max(float2(p[0], p[1]), float2(1.0));
    VertexOut out;
    out.position = float4(v.x / viewport.x * 2.0 - 1.0,
                          1.0 - v.y / viewport.y * 2.0, 0.0, 1.0);
    out.uv = v.zw;
    out.pixel = v.xy;
    return out;
}

float spread_value(float t, uint spread)
{
    if (spread == 1u) return fract(t);
    if (spread == 2u) {
        float q = fmod(abs(t), 2.0);
        return q <= 1.0 ? q : 2.0 - q;
    }
    return clamp(t, 0.0, 1.0);
}

float4 sample_gradient(constant float* p, const device float* stops, float2 local)
{
    const uint type = uint(max(p[3], 0.0));
    float t = 0.0;
    if (type == 1u) {
        float2 a = float2(p[36], p[37]);
        float2 b = float2(p[38], p[39]);
        float2 d = b - a;
        t = dot(local - a, d) / max(dot(d, d), 1e-6);
    } else {
        float2 center = float2(p[36], p[37]);
        float2 radius = max(abs(float2(p[38], p[39])), float2(1e-4));
        float2 origin = float2(p[40], p[41]);
        float2 focal = (origin - center) / radius;
        // Clamp an external focal point to the ellipse's interior, then find
        // the intersection of its sample ray with the normalized unit circle.
        focal *= min(1.0, 0.99999 / max(length(focal), 1e-6));
        float2 q = (local - center) / radius - focal;
        float a = dot(q, q), b = dot(focal, q);
        float hit = -b + sqrt(max(b*b + a*(1.0-dot(focal,focal)), 0.0));
        t = a > 1e-12 ? a / max(hit, 1e-12) : 0.0;
    }
    t = spread_value(t, uint(max(p[44], 0.0)));

    uint count = uint(max(p[45], 0.0));
    if (count == 0u) return float4(p[4], p[5], p[6], p[7]);
    // Upper bound preserves a discontinuity at coincident gradient stops.
    uint lo = 0u, hi = count;
    while (lo < hi) {
        uint mid = lo + (hi-lo)/2u;
        if (stops[mid*5u] <= t) lo = mid+1u; else hi = mid;
    }
    uint left = lo == 0u ? 0u : lo-1u;
    uint right = min(lo, count-1u);
    uint a = left*5u, b = right*5u;
    float4 ca = float4(stops[a+1u],stops[a+2u],stops[a+3u],stops[a+4u]);
    float4 cb = float4(stops[b+1u],stops[b+2u],stops[b+3u],stops[b+4u]);
    float u = (t-stops[a])/max(stops[b]-stops[a],1e-6);
    return mix(ca,cb,clamp(u,0.0,1.0));
}

float2 to_local(constant float* p, float2 pixel)
{
    return float2(p[20] * pixel.x + p[22] * pixel.y + p[24],
                  p[21] * pixel.x + p[23] * pixel.y + p[25]);
}

float sd_round_rect(float2 point, float4 rect, float4 radii)
{
    float2 center = rect.xy + rect.zw * 0.5;
    float2 q0 = point - center;
    float radius = q0.x < 0.0
        ? (q0.y < 0.0 ? radii.x : radii.w)
        : (q0.y < 0.0 ? radii.y : radii.z);
    float2 q = abs(q0) - rect.zw * 0.5 + radius;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
}

float sd_ellipse(float2 point, float4 rect)
{
    float2 r = max(rect.zw * 0.5, float2(1e-5));
    float2 q = (point - (rect.xy + r)) / r;
    return (length(q) - 1.0) * min(r.x, r.y);
}

float sd_superellipse(float2 point, float4 rect, float exponent)
{
    float2 r = max(rect.zw * 0.5, float2(1e-5));
    float2 q = abs((point - (rect.xy + r)) / r);
    float n = max(exponent, 1.0);
    float v = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n) - 1.0;
    return v * min(r.x, r.y);
}

float smooth_min(float a, float b, float radius)
{
    if (radius <= 1e-5) return min(a,b);
    float h = clamp(0.5 + 0.5 * (b-a) / radius, 0.0, 1.0);
    return mix(b,a,h) - radius * h * (1.0-h);
}

float sd_elliptical_box(float2 point, float4 rect, float4 rx, float4 ry, uint edges)
{
    if (rect.z <= 0.0 || rect.w <= 0.0) return 1e10;
    float2 q = point - rect.xy;
    float d = -1e10;
    if (edges & 1u) d = max(d, -q.x);
    if (edges & 2u) d = max(d, -q.y);
    if (edges & 4u) d = max(d, q.x - rect.z);
    if (edges & 8u) d = max(d, q.y - rect.w);
    float4 dx = float4(q.x, rect.z-q.x, rect.z-q.x, q.x);
    float4 dy = float4(q.y, q.y, rect.w-q.y, rect.w-q.y);
    uint4 adjoining = uint4(3u, 6u, 12u, 9u);
    for (uint corner = 0u; corner < 4u; ++corner) {
        float2 radius = float2(rx[corner], ry[corner]);
        if ((edges & adjoining[corner]) != adjoining[corner] ||
            any(radius <= 0.0) || dx[corner] >= radius.x || dy[corner] >= radius.y) continue;
        float2 v = (float2(dx[corner],dy[corner])-radius)/radius;
        float len = length(v), gradient = length(v/radius);
        if (gradient > 0.0) d = max(d, len*(len-1.0)/gradient);
    }
    return d;
}

float contour_coverage(constant float* p, float2 local, uint base)
{
    float d = sd_elliptical_box(local,
        float4(p[base],p[base+1],p[base+2],p[base+3]),
        float4(p[base+4],p[base+5],p[base+6],p[base+7]),
        float4(p[base+8],p[base+9],p[base+10],p[base+11]),uint(p[base+12]));
    float aa = max(fwidth(d), 1e-4);
    return 1.0-smoothstep(-0.5*aa,0.5*aa,d);
}

fragment float4 jalium_contour_mask_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float coverage = contour_coverage(p,to_local(p,in.pixel),420u);
    if (p[180] > 0.5) coverage = 1.0-coverage;
    return float4(coverage);
}

float clip_coverage(constant float* p, float2 pixel, texture2d<float> pathClip)
{
    const uint count = min(uint(max(p[508], 0.0)), 18u);
    float coverage = 1.0;
    for (uint i = 0u; i < count; ++i) {
        const uint base = 512u + i * 20u;
        const float2 local = float2(
            p[base + 0u] * pixel.x + p[base + 2u] * pixel.y + p[base + 4u],
            p[base + 1u] * pixel.x + p[base + 3u] * pixel.y + p[base + 5u]);
        const float4 rect = float4(p[base + 6u], p[base + 7u],
                                   p[base + 8u], p[base + 9u]);
        const float4 radii = float4(p[base + 10u], p[base + 11u],
                                    p[base + 12u], p[base + 13u]);
        const uint mode = uint(max(p[base + 14u], 0.0));
        const float4 radiiY = float4(p[base+15u],p[base+16u],p[base+17u],p[base+18u]);
        const float d = sd_elliptical_box(local,rect,radii,radiiY,uint(p[base+19u]));
        const float aa = max(fwidth(d),1e-4);
        float inside = (mode & 4u) != 0u
            ? (d <= 0.0 ? 1.0 : 0.0)
            : 1.0 - smoothstep(-0.5*aa, 0.5*aa, d);
        if ((mode & 2u) != 0u) inside = 1.0 - inside;
        coverage *= inside;
    }
    if (p[30] > 0.5) {
        const float2 location = floor(pixel) - float2(p[31], p[32]);
        if (any(location < 0.0) || any(location >= float2(p[33], p[34]))) return 0.0;
        coverage *= pathClip.read(uint2(location)).r;
    }
    return coverage;
}

fragment void jalium_clip_stencil_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> pathClip [[texture(30)]])
{
    // Keep the full antialias fringe in the stencil. The color fragment then
    // applies exact fractional coverage (or a hard edge for aliased clips).
    if (clip_coverage(p, in.pixel, pathClip) <= 0.0001) discard_fragment();
}

fragment float4 jalium_shape_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], const device float* stops [[buffer(1)]],
    texture2d<float> pathClip [[texture(30)]])
{
    const uint primitive = uint(max(p[2], 0.0));
    const float2 local = to_local(p, in.pixel);
    const float4 rect = float4(p[8], p[9], p[10], p[11]);
    float distance = -1.0;
    if (primitive == 1u) {
        distance = sd_round_rect(local, rect, float4(p[12], p[13], p[14], p[15]));
    } else if (primitive == 2u) {
        distance = sd_ellipse(local, rect);
    } else if (primitive == 3u) {
        distance = sd_superellipse(local, rect, p[18]);
    }

    float coverage = primitive == 0u ? 1.0 : 1.0 - smoothstep(-0.75, 0.75, distance);
    float stroke = p[16];
    if (stroke > 0.0 && primitive != 0u) {
        float outer = 1.0 - smoothstep(-0.75, 0.75, distance);
        float inner = 1.0 - smoothstep(-0.75, 0.75, distance + stroke);
        coverage = max(outer - inner, 0.0);
    }
    // Analytic paths batch pixel rectangles in one draw. Each rectangle carries
    // constant coverage in u, independent of the brush's color and opacity.
    if (p[26] > 0.5) coverage *= in.uv.x;
    coverage *= clip_coverage(p, in.pixel, pathClip);

    float4 color = uint(max(p[3], 0.0)) == 0u
        ? float4(p[4], p[5], p[6], p[7])
        : sample_gradient(p, stops, local);
    color.a *= p[17] * coverage;
    color.rgb *= color.a;
    return color;
}

fragment float4 jalium_path_mask_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], const device float* stops [[buffer(1)]],
    texture2d<float> mask [[texture(0)]],
    sampler maskSampler [[sampler(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float coverage = mask.sample(maskSampler, in.uv).a * clip_coverage(p, in.pixel, pathClip);
    float4 color = uint(max(p[3], 0.0)) == 0u
        ? float4(p[4], p[5], p[6], p[7]) : sample_gradient(p, stops, to_local(p, in.pixel));
    color.a *= p[17] * coverage;
    color.rgb *= color.a;
    return color;
}

fragment float4 jalium_texture_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float4 color = source.sample(sourceSampler, in.uv);
    if (uint(max(p[179], 0.0)) == 1u) {
        float4 tint = float4(p[4], p[5], p[6], p[7]);
        color = float4(tint.rgb * tint.a * color.a, tint.a * color.a);
    }
    color *= p[17] * clip_coverage(p, in.pixel, pathClip);
    return color;
}

fragment float4 jalium_yuv_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> lumaTexture [[texture(0)]],
    texture2d<float> chromaTexture [[texture(1)]],
    sampler sourceSampler [[sampler(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float y = lumaTexture.sample(sourceSampler, in.uv).r;
    float2 chroma = chromaTexture.sample(sourceSampler, in.uv).rg;
    float2 cbcr;
    if (p[300] > 0.5) {
        y = (y - 16.0/255.0) * (255.0/219.0);
        cbcr = (chroma - 128.0/255.0) * (255.0/224.0);
    } else cbcr = chroma - 0.5;
    float3 rgb;
    uint matrix=uint(max(p[301],0.0));
    if(matrix==0u){
        rgb.r=y+1.4020*cbcr.y;rgb.g=y-0.344136*cbcr.x-0.714136*cbcr.y;
        rgb.b=y+1.7720*cbcr.x;
    }else if(matrix==2u){
        rgb.r=y+1.4746*cbcr.y;rgb.g=y-0.164553*cbcr.x-0.571353*cbcr.y;
        rgb.b=y+1.8814*cbcr.x;
    }else{
        rgb.r = y + 1.5748 * cbcr.y;
        rgb.g = y - 0.1873 * cbcr.x - 0.4681 * cbcr.y;
        rgb.b = y + 1.8556 * cbcr.x;
    }
    float alpha = p[17] * clip_coverage(p, in.pixel, pathClip);
    return float4(clamp(rgb, 0.0, 1.0) * alpha, alpha);
}

float transition_hash(float2 p)
{
    float3 p3=fract(float3(p.x,p.y,p.x)*0.1031);
    p3+=dot(p3,p3.yzx+33.33);
    return fract((p3.x+p3.y)*p3.z);
}

float transition_noise(float2 st)
{
    float2 i=floor(st),f=fract(st);float a=transition_hash(i);
    float b=transition_hash(i+float2(1,0));float c=transition_hash(i+float2(0,1));
    float d=transition_hash(i+float2(1,1));float2 u=f*f*(3.0-2.0*f);
    return mix(a,b,u.x)+(c-a)*u.y*(1.0-u.x)+(d-b)*u.x*u.y;
}

float4 transition_sample(texture2d<float> source,sampler sourceSampler,
    float2 uv,float4 mapping)
{
    float4 color=source.sample(sourceSampler,mapping.xy+clamp(uv,0.0,1.0)*mapping.zw);
    if(color.a>0.0001)color.rgb/=color.a;return color;
}

fragment float4 jalium_transition_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],texture2d<float> fromTexture [[texture(0)]],
    texture2d<float> toTexture [[texture(1)]],sampler sourceSampler [[sampler(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float progress=clamp(p[400],0.0,1.0);int mode=int(round(p[401]));
    float4 fromMap=float4(p[404],p[405],p[406],p[407]);
    float4 toMap=float4(p[408],p[409],p[410],p[411]);
    float4 oldColor=transition_sample(fromTexture,sourceSampler,in.uv,fromMap);
    float4 newColor=transition_sample(toTexture,sourceSampler,in.uv,toMap);
    float4 color=float4(0.0);float2 resolution=max(float2(p[412],p[413]),float2(1.0));
    if(progress<=0.0)color=oldColor;
    else if(progress>=1.0)color=newColor;
    else if(mode==0){
        float n=transition_noise(in.uv*40.0),threshold=progress*1.2-0.1;
        float edge=smoothstep(threshold-0.05,threshold+0.05,n);
        float edgeMask=smoothstep(threshold-0.08,threshold-0.03,n)*
            (1.0-smoothstep(threshold-0.03,threshold+0.02,n));
        color=mix(newColor,oldColor,edge);color.rgb+=float3(1.0,0.5,0.1)*edgeMask*2.0;
    }else if(mode==1){
        float blockSize=max(1.0,40.0*sin(progress*3.14159));
        float2 uv=floor(in.uv*resolution/blockSize)*blockSize/resolution;
        color=progress<0.5?transition_sample(fromTexture,sourceSampler,uv,fromMap):
            transition_sample(toTexture,sourceSampler,uv,toMap);
    }else if(mode==2){
        float intensity=sin(progress*3.14159)*0.8+0.2;
        float lineNoise=transition_hash(float2(floor(in.uv.y*30.0),floor(progress*20.0)));
        float shift=(lineNoise-0.5)*0.15*intensity*intensity;
        float selector=transition_hash(float2(floor(in.uv.x*8.0),
            floor(in.uv.y*12.0+progress*5.0)));bool useNew=selector<progress;
        float4 fromR=transition_sample(fromTexture,sourceSampler,in.uv+float2(shift,0),fromMap);
        float4 fromG=transition_sample(fromTexture,sourceSampler,in.uv,fromMap);
        float4 fromB=transition_sample(fromTexture,sourceSampler,in.uv-float2(shift,0),fromMap);
        float4 toR=transition_sample(toTexture,sourceSampler,in.uv+float2(shift,0),toMap);
        float4 toG=transition_sample(toTexture,sourceSampler,in.uv,toMap);
        float4 toB=transition_sample(toTexture,sourceSampler,in.uv-float2(shift,0),toMap);
        float scan=sin(in.uv.y*resolution.y*2.0)*0.03*intensity;
        color=float4((useNew?toR.r:fromR.r)+scan,(useNew?toG.g:fromG.g)+scan,
            (useNew?toB.b:fromB.b)+scan,1.0);
    }else if(mode==3){
        float spread=(1.0-progress)*0.08,newSpread=progress*0.08;
        float4 oldR=transition_sample(fromTexture,sourceSampler,in.uv+float2(spread,spread*0.5),fromMap);
        float4 oldG=transition_sample(fromTexture,sourceSampler,in.uv,fromMap);
        float4 oldB=transition_sample(fromTexture,sourceSampler,in.uv-float2(spread,spread*0.5),fromMap);
        float4 newR=transition_sample(toTexture,sourceSampler,in.uv+float2(newSpread,newSpread*0.5),toMap);
        float4 newG=transition_sample(toTexture,sourceSampler,in.uv,toMap);
        float4 newB=transition_sample(toTexture,sourceSampler,in.uv-float2(newSpread,newSpread*0.5),toMap);
        color=mix(float4(oldR.r,oldG.g,oldB.b,1),float4(newR.r,newG.g,newB.b,1),progress);
    }else if(mode==4){
        float time=progress*6.28318,strength=sin(progress*3.14159)*0.12;
        float2 distortion=float2(sin(in.uv.y*15.0+time),cos(in.uv.x*15.0+time*1.3))*strength;
        distortion+=float2(sin(in.uv.y*8.0-time*0.7),cos(in.uv.x*8.0-time*0.5))*strength*0.5;
        color=mix(transition_sample(fromTexture,sourceSampler,in.uv+distortion,fromMap),
            transition_sample(toTexture,sourceSampler,in.uv-distortion*0.5,toMap),
            smoothstep(0.2,0.8,progress));
    }else if(mode==5){
        float amplitude=sin(progress*3.14159)*0.15;
        float wave=sin(in.uv.y*8.0+progress*12.56636)*amplitude;
        color=mix(transition_sample(fromTexture,sourceSampler,in.uv+float2(wave,wave*0.3),fromMap),
            transition_sample(toTexture,sourceSampler,in.uv-float2(wave*0.5,wave*0.2),toMap),progress);
    }else if(mode==6){
        float column=transition_hash(float2(floor(in.uv.x*30.0),0));
        float row=transition_hash(float2(0,floor(in.uv.y*50.0)));
        float threshold=progress*1.5-column*0.3-row*0.2;
        if(threshold>0.5)color=newColor;else{float displacement=max(0.0,threshold)*0.5;
            float2 uv=clamp(in.uv+float2(displacement*(1.0+column),
                displacement*0.3*sin(in.uv.y*20.0)),0.0,1.0);
            float4 blown=transition_sample(fromTexture,sourceSampler,uv,fromMap);
            blown.a*=1.0-displacement*2.0;color=mix(newColor,blown,blown.a);}
    }else if(mode==7){
        float2 center=float2(0.5),direction=in.uv-center;float distance=length(direction);
        float rippleRadius=progress*0.707107*1.3,width=0.08;
        float mask=smoothstep(rippleRadius,rippleRadius-width,distance);
        float rippleDistance=abs(distance-rippleRadius);
        float strength=(1.0-smoothstep(0.0,width*2.0,rippleDistance))*
            sin(rippleDistance*60.0)*0.015*(1.0-progress);
        float2 offset=normalize(direction+float2(0.001))*strength;
        color=mix(transition_sample(fromTexture,sourceSampler,in.uv+offset,fromMap),newColor,mask);
    }else if(mode==8){
        float2 direction=in.uv-float2(0.5);float angle=atan2(direction.x,-direction.y);
        color=(angle+3.14159)/6.28318<progress?newColor:oldColor;
    }else{
        float luminance=dot(oldColor.rgb,float3(0.299,0.587,0.114));float3 thermal;
        if(luminance<0.2)thermal=mix(float3(0,0,0.5),float3(0,0,1),luminance/0.2);
        else if(luminance<0.4)thermal=mix(float3(0,0,1),float3(0,1,0),(luminance-0.2)/0.2);
        else if(luminance<0.6)thermal=mix(float3(0,1,0),float3(1,1,0),(luminance-0.4)/0.2);
        else if(luminance<0.8)thermal=mix(float3(1,1,0),float3(1,0,0),(luminance-0.6)/0.2);
        else thermal=mix(float3(1,0,0),float3(1), (luminance-0.8)/0.2);
        if(progress<0.4)color=mix(oldColor,float4(thermal,1),progress/0.4);
        else if(progress<0.6)color=float4(thermal+(progress-0.4)/0.2*0.3,1);
        else{float t=(progress-0.6)/0.4,newLum=dot(newColor.rgb,float3(0.299,0.587,0.114));
            float3 next=float3(1,max(0.5,newLum),newLum*0.5);
            color=mix(float4(thermal,1),mix(float4(next,1),newColor,t),t);}
    }
    color.a*=clamp(p[402],0.0,1.0);color.rgb*=color.a;
    color*=p[17]*clip_coverage(p,in.pixel,pathClip);return color;
}

float glow_gaussian(float distance,float radius)
{
    return exp(-2.0*distance*distance/max(radius*radius,1e-4));
}

fragment float4 jalium_highlight_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float2 local=to_local(p,in.pixel);
    float4 rect=float4(p[181],p[182],p[183],p[184]);
    float stroke=max(p[188],0.5),progress=clamp(p[192],0.0,1.0);
    uint mode=uint(p[180]);float glow=0.0;
    float4 dimRect=rect;
    if(mode==0u){
        float2 closest=clamp(local,rect.xy,rect.xy+rect.zw);
        bool inside=all(local>=rect.xy)&&all(local<=rect.xy+rect.zw);
        if(!inside){
            float perimeter=max(2.0*(rect.z+rect.w),1.0),position;
            if(closest.y<=rect.y)position=closest.x-rect.x;
            else if(closest.x>=rect.x+rect.z)position=rect.z+closest.y-rect.y;
            else if(closest.y>=rect.y+rect.w)position=rect.z+rect.w+rect.x+rect.z-closest.x;
            else position=perimeter-(closest.y-rect.y);
            float trail=clamp(p[189],0.05,1.0);
            float lag=fract(p[190]-position/perimeter);
            float taper=lag<trail?sin(3.14159265*lag/trail):0.0;
            float distance=length(local-closest);
            float core=max(stroke*2.0,2.5)*taper,halo=max(stroke*5.0,9.0)*taper;
            glow=taper*(0.95*glow_gaussian(distance,core)+0.30*glow_gaussian(distance,halo));
            glow+=0.26*glow_gaussian(distance,max(stroke,1.0))+
                0.14*glow_gaussian(distance,max(stroke*2.5,1.0))+
                0.06*glow_gaussian(distance,max(stroke*4.0,1.0));
        }
    }else if(mode==1u){
        float4 target=float4(p[194],p[195],p[196],p[197]);
        dimRect=mix(rect,target,progress);
        float2 fromCenter=rect.xy+rect.zw*0.5,toCenter=target.xy+target.zw*0.5;
        float2 head=mix(fromCenter,toCenter,progress),tail=mix(fromCenter,toCenter,clamp(p[193],0.0,1.0));
        float2 segment=head-tail;
        float t=clamp(dot(local-tail,segment)/max(dot(segment,segment),1e-4),0.0,1.0);
        float taper=sin(3.14159265*t),distance=length(local-mix(tail,head,t));
        glow=taper*(0.95*glow_gaussian(distance,max(stroke*2.2,2.5)*taper)+
            0.20*glow_gaussian(distance,max(stroke*11.0,16.0)*taper));
        float border=abs(sd_round_rect(local,target,float4(0.0)));
        glow+=0.3*progress*(1.0-smoothstep(0.0,max(fwidth(border),0.75),border));
    }else{
        for(uint i=0u;i<3u;++i){
            float delay=float(i)*0.1;
            if(progress<delay)continue;
            float t=clamp((progress-delay)/(1.0-delay),0.0,1.0);
            float2 size=rect.zw*t;
            float4 ring=float4(rect.xy+(rect.zw-size)*0.5,size);
            float radius=min(size.x,size.y)*0.05;
            float d=abs(sd_round_rect(local,ring,float4(radius)));
            float width=max(stroke*1.5*(1.0-t*0.5),1.0);
            float opacity=(1.0-pow(t,1.0+float(i)*0.5))*0.9;
            glow+=opacity*(1.0-smoothstep(width*0.5,width*0.5+max(fwidth(d),0.5),d));
        }
        float border=abs(sd_round_rect(local,rect,float4(0.0)));
        glow+=(0.6+0.4*progress)*(1.0-smoothstep(0.0,max(fwidth(border),0.75),border));
    }
    float expand=stroke*10.0;
    float dim=sd_round_rect(local,float4(dimRect.xy-expand,dimRect.zw+expand*2.0),float4(0.0))>0.0?
        clamp(p[191],0.0,1.0):0.0;
    glow=clamp(glow,0.0,1.0);
    float4 color=float4(float3(p[185],p[186],p[187])*glow,glow+dim*(1.0-glow));
    return color*p[17]*clip_coverage(p,in.pixel,pathClip);
}

// Continuous corners are local quarter-Lame patches. A whole-rect Lame curve
// bows the straight sides of wide glass panels and does not match the other
// backends' SuperEllipse geometry.
float glass_shape(float2 point,float4 rect,float radius,float shape,float exponent)
{
    float2 halfSize=max(rect.zw*0.5,float2(0.0));
    float r=clamp(radius,0.0,min(halfSize.x,halfSize.y));
    if(shape<0.5 || r<0.0001)return sd_round_rect(point,rect,float4(r));
    float2 a=abs(point-rect.xy-halfSize),q=a-(halfSize-r);
    if(q.x<=0.0 || q.y<=0.0)return max(a.x-halfSize.x,a.y-halfSize.y);
    float n=exponent>=2.0&&exponent<=16.0?exponent:4.0;
    float2 u=max(q/r,float2(0.0));
    float lp=pow(pow(u.x,n)+pow(u.y,n),1.0/n);
    float power=pow(max(lp,0.0001),n-1.0);
    float2 gradient=pow(u,float2(n-1.0))/(r*power);
    return (lp-1.0)/max(length(gradient),0.0001);
}

float glass_self_sd(constant float* p,float2 pixel,bool softened=false)
{
    float4 rect=float4(p[351],p[352],p[353],p[354]);
    float radius=p[355];
    if(softened&&p[349]<0.5)radius*=1.5;
    return glass_shape(to_local(p,pixel),rect,radius,p[349],p[350])*p[385];
}

float glass_combined_sd(constant float* p,float2 pixel,bool softened=false)
{
    int count=clamp(int(p[360]),0,4);
    float sd=glass_self_sd(p,pixel,softened&&count==0);
    float2 screenDip=pixel/max(float2(p[358],p[359]),float2(0.0001));
    for(int i=0;i<count;++i){
        int base=365+i*5;
        float4 rect=float4(p[base],p[base+1],p[base+2],p[base+3]);
        float radius=clamp(p[base+4],0.0,max(min(rect.z,rect.w)*0.5,0.0));
        float neighbor=sd_round_rect(screenDip,rect,float4(radius))*p[386];
        sd=smooth_min(sd,neighbor,p[361]*p[386]);
    }
    return sd;
}

float2 glass_gradient(constant float* p,float2 pixel)
{
    float2 e=float2(0.5,0.0);
    float2 gradient=float2(glass_combined_sd(p,pixel+e,true)-glass_combined_sd(p,pixel-e,true),
        glass_combined_sd(p,pixel+e.yx,true)-glass_combined_sd(p,pixel-e.yx,true));
    return length(gradient)>0.001?normalize(gradient):float2(0.0,1.0);
}

float3 glass_sample(texture2d<float> source,sampler sourceSampler,float2 uv)
{
    float4 value=source.sample(sourceSampler,uv);
    return value.a>0.0001?value.rgb/value.a:float3(0.0);
}

fragment float4 jalium_effect_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]], texture2d<float> pathClip [[texture(30)]])
{
    float4 c = source.sample(sourceSampler, in.uv);
    uint mode = uint(max(p[180], 0.0));
    if (mode == 1u) {
        float alpha=c.a;float3 rgb=alpha>0.0001?c.rgb/alpha:float3(0.0);
        float nr=clamp(p[181]*rgb.r+p[182]*rgb.g+p[183]*rgb.b+p[184]*alpha+p[185],0.0,1.0);
        float ng=clamp(p[186]*rgb.r+p[187]*rgb.g+p[188]*rgb.b+p[189]*alpha+p[190],0.0,1.0);
        float nb=clamp(p[191]*rgb.r+p[192]*rgb.g+p[193]*rgb.b+p[194]*alpha+p[195],0.0,1.0);
        float na=clamp(p[196]*rgb.r+p[197]*rgb.g+p[198]*rgb.b+p[199]*alpha+p[200],0.0,1.0);
        c=float4(float3(nr,ng,nb)*na,na);
    } else if (mode == 2u) {
        float2 texel = float2(p[181], p[182]);
        float2 lo = float2(p[184],p[185]), hi = float2(p[186],p[187]);
        float4 neighbor=source.sample(sourceSampler,clamp(in.uv-texel,lo,hi));
        float3 centerRgb=c.a>0.0001?c.rgb/c.a:float3(0.0);
        float3 neighborRgb=neighbor.a>0.0001?neighbor.rgb/neighbor.a:float3(0.0);
        float centerLuma=dot(centerRgb,float3(0.299,0.587,0.114));
        float neighborLuma=dot(neighborRgb,float3(0.299,0.587,0.114));
        float value=clamp(0.5+(centerLuma-neighborLuma)*p[183],0.0,1.0);
        c=float4(float3(value)*c.a,c.a);
    } else if (mode == 3u) {
        float alpha = c.a;
        float3 color = alpha > 1e-6 ? c.rgb / alpha : float3(0.0);
        color *= max(p[320], 0.0);
        color = (color - 0.5) * max(p[321], 0.0) + 0.5;
        float luma = dot(color, float3(0.299, 0.587, 0.114));
        color = mix(float3(luma), color, max(p[322], 0.0));
        float hue = p[323];
        if (abs(hue) > 0.0001) {
            float yv = dot(color, float3(0.299, 0.587, 0.114));
            float iv = dot(color, float3(0.596, -0.274, -0.322));
            float qv = dot(color, float3(0.211, -0.523, 0.312));
            float hc = cos(hue), hs = sin(hue);
            float i2 = iv * hc - qv * hs;
            float q2 = iv * hs + qv * hc;
            color = float3(yv + 0.956 * i2 + 0.621 * q2,
                           yv - 0.272 * i2 - 0.647 * q2,
                           yv - 1.106 * i2 + 1.703 * q2);
        }
        luma = dot(color, float3(0.299, 0.587, 0.114));
        color = mix(color, float3(luma), clamp(p[324], 0.0, 1.0));
        float3 sepia = float3(dot(color, float3(0.393,0.769,0.189)),
                              dot(color, float3(0.349,0.686,0.168)),
                              dot(color, float3(0.272,0.534,0.131)));
        color = mix(color, sepia, clamp(p[325], 0.0, 1.0));
        color = mix(color, 1.0 - color, clamp(p[326], 0.0, 1.0));
        color = mix(color, float3(p[328],p[329],p[330]),
                    clamp(p[331],0.0,1.0));
        color *= max(p[327], 0.0);
        if (p[332] > 0.0) {
            uint2 pixel = uint2(max(in.pixel, float2(0.0)));
            uint n = pixel.x * 1597334677u ^ pixel.y * 3812015801u ^ 795330728u;
            n ^= n >> 16; n *= 0x7feb352du; n ^= n >> 15;
            n *= 0x846ca68bu; n ^= n >> 16;
            color += (float(n) * (1.0 / 4294967295.0) - 0.5) * p[332];
        }
        alpha *= clamp(p[333], 0.0, 1.0);
        c = float4(clamp(color,0.0,1.0) * alpha, alpha);
    } else if (mode == 4u) {
        float scale=max(p[385],0.0001),dpi=max(p[386],0.0001);
        float2 local=to_local(p,in.pixel),center=float2(p[387],p[388]);
        float4 rect=float4(p[351],p[352],p[353],p[354]);
        float2 centered=local-rect.xy-rect.zw*0.5;
        float selfSd=glass_self_sd(p,in.pixel),sd=glass_combined_sd(p,in.pixel);
        int count=clamp(int(p[360]),0,4);
        bool yield=false;
        if(count>0&&selfSd>0.0){
            float minNeighbor=1e20;float2 closestCenter=float2(0.0);
            float2 screenDip=in.pixel/max(float2(p[358],p[359]),float2(0.0001));
            for(int i=0;i<count;++i){
                int base=365+i*5;
                float4 nr=float4(p[base],p[base+1],p[base+2],p[base+3]);
                float nd=sd_round_rect(screenDip,nr,float4(clamp(p[base+4],0.0,min(nr.z,nr.w)*0.5)))*dpi;
                if(nd<minNeighbor){minNeighbor=nd;closestCenter=(nr.xy+nr.zw*0.5)*float2(p[358],p[359]);}
            }
            yield=minNeighbor<selfSd || (minNeighbor-selfSd<0.5 &&
                (center.x>closestCenter.x+0.01 || (abs(center.x-closestCenter.x)<=0.01&&center.y>closestCenter.y+0.01)));
        }
        float aa=max(fwidth(sd),0.5);
        float2 normal=glass_gradient(p,in.pixel);
        if(yield)c=float4(0.0);
        else if(sd>aa){
            float shadowSd=glass_combined_sd(p,in.pixel-float2(0.0,4.0*scale));
            float shadow=shadowSd>0.0? (1.0-smoothstep(0.0,24.0*scale,shadowSd))*0.1:0.0;
            c=float4(0.0,0.0,0.0,shadow);
        }else{
            float mask=1.0-smoothstep(-aa,aa,sd);
            float2 texel=1.0/max(float2(p[356],p[357]),float2(1.0));
            float height=min(p[340]*0.667,40.0)*scale,bend=0.0;
            float2 grad=normal;
            if(height>0.0001&&-sd<height){
                float depth=clamp(1.0-(-min(sd,0.0))/height,0.0,1.0);
                bend=(1.0-sqrt(max(1.0-depth*depth,0.0)))*(-p[340]*scale);
                float2 radial=in.pixel-center;
                radial/=max(length(radial),0.001);
                float blend=count>0?clamp(-selfSd/(8.0*scale),0.0,1.0):1.0;
                grad+=radial*blend;grad/=max(length(grad),0.001);
            }
            float2 uv=in.uv+bend*grad*texel;
            float3 color;
            if(p[341]>0.01&&bend!=0.0){
                float dispersion=p[341]*centered.x*centered.y/max(rect.z*rect.w*0.25,1.0);
                float2 offset=bend*grad*dispersion*texel;
                float3 red=glass_sample(source,sourceSampler,uv+offset);
                float3 orange=glass_sample(source,sourceSampler,uv+offset*(2.0/3.0));
                float3 yellow=glass_sample(source,sourceSampler,uv+offset*(1.0/3.0));
                float3 green=glass_sample(source,sourceSampler,uv);
                float3 cyan=glass_sample(source,sourceSampler,uv-offset*(1.0/3.0));
                float3 blue=glass_sample(source,sourceSampler,uv-offset*(2.0/3.0));
                float3 purple=glass_sample(source,sourceSampler,uv-offset);
                color=float3((red.r+orange.r+yellow.r)/3.5+purple.r/7.0,
                    orange.g/7.0+(yellow.g+green.g+cyan.g)/3.5,
                    (cyan.b+blue.b+purple.b)/3.0);
            }else color=glass_sample(source,sourceSampler,uv);
            float luma=dot(color,float3(0.213,0.715,0.072));
            color=mix(float3(luma),color,1.5);
            color=mix(color,float3(p[345],p[346],p[347]),clamp(p[348],0.0,1.0));
            float edge=-sd/scale;
            float highlight=exp(-pow(edge-0.75,2.0)/0.5)+exp(-edge*edge/18.0)*0.15;
            float lightMod;
            if(p[342]>=0.0){
                float2 toLight=float2(p[342]*p[358],p[343]*p[359])-in.pixel;
                float distance=length(toLight),falloff=max(rect.z,rect.w)*0.75*scale;
                float direction=smoothstep(-0.3,1.0,dot(normal,toLight/max(distance,0.001)));
                float radial=pow(1.0-clamp(distance/max(falloff,0.001),0.0,1.0),2.0);
                float spec=exp(-distance*distance/max(falloff*falloff*0.15,0.001))*0.6;
                lightMod=direction*(radial*0.8+0.2)+spec;
            }else{
                float first=smoothstep(-0.2,1.0,dot(normal,normalize(float2(-1.0,-1.0))));
                float second=smoothstep(-0.2,1.0,dot(normal,normalize(float2(1.0,1.0))))*0.5;
                lightMod=mix(0.15,0.31,max(first,second));
            }
            color+=highlight*lightMod*(0.55+p[344]*0.3);
            float shadowSd=glass_combined_sd(p,in.pixel+float2(0.0,3.0*scale))/scale;
            float shadow=smoothstep(-8.0,0.0,shadowSd);
            float edgeShadow=1.0-smoothstep(0.0,8.0,-selfSd/scale);
            float edgeMask=smoothstep(0.0,4.0,-selfSd/scale);
            color*=1.0-max(shadow,edgeShadow*0.2)*0.12*edgeMask;
            c=float4(clamp(color,0.0,1.0)*mask,mask);
        }
    } else if (mode == 5u) {
        float coverage = c.a;
        if (p[185] > 0.5) {
            float inside = contour_coverage(p,to_local(p,in.pixel),420u);
            coverage *= p[185] > 1.5 ? 1.0-inside : inside;
        }
        float alpha = clamp(coverage*p[184],0.0,1.0);
        c = float4(float3(p[181],p[182],p[183])*alpha,alpha);
    }
    c *= p[17] * clip_coverage(p, in.pixel, pathClip);
    return c;
}

kernel void jalium_blur(texture2d<float, access::sample> source [[texture(0)]],
    texture2d<float, access::write> destination [[texture(1)]],
    constant float* p [[buffer(0)]], uint2 gid [[thread_position_in_grid]])
{
    if (gid.x >= uint(p[8]) || gid.y >= uint(p[9])) return;
    gid += uint2(p[6],p[7]);
    int radius = clamp(int(p[0]), 0, 32);
    bool horizontal = p[1] > 0.5;
    float4 sum = 0.0;
    if(p[3]<0.5){
        // Two weighted neighboring texels are exactly one linear sample.
        // Keep the original discrete kernel and edge rules while halving
        // texture fetches. Frosted taps have different perpendicular jitter
        // and must retain the individual-read path below.
        constexpr sampler blurSampler(coord::pixel,address::clamp_to_edge,filter::linear);
        for(int i=0;i<int(p[17]);++i){
            float offset=p[85+i];
            float2 q=float2(gid)+(horizontal?float2(offset,0):float2(0,offset));
            float coverage=1.0;
            if(p[5]>0.5){
                float axis=horizontal?q.x:q.y,extent=horizontal?p[13]:p[14];
                coverage=clamp(axis+1.0,0.0,1.0)*clamp(extent-axis,0.0,1.0);
            }
            q=clamp(q,float2(0.0),float2(p[13]-1,p[14]-1));
            sum+=source.sample(blurSampler,q+0.5-float2(p[11],p[12]))*(p[118+i]*coverage);
        }
        destination.write(sum*p[10],gid-uint2(p[15],p[16]));return;
    }
    for (int i = -radius; i <= radius; ++i) {
        int2 q = int2(gid) + (horizontal ? int2(i, 0) : int2(0, i));
        if (p[3] > 0.5) {
            uint hash = (gid.x * 1664525u + gid.y * 1013904223u + uint(i + radius));
            int jitter = (hash & 1u) != 0u ? 1 : -1;
            q += horizontal ? int2(0,jitter) : int2(jitter,0);
        }
        bool outside = any(q < int2(0)) || q.x >= int(p[13]) || q.y >= int(p[14]);
        q = clamp(q, int2(0), int2(p[13]-1,p[14]-1));
        float weight = p[52+i];
        sum += (outside && p[5] > 0.5 ? float4(0.0) :
            source.read(uint2(q-int2(p[11],p[12])))) * weight;
    }
    destination.write(sum * p[10], gid-uint2(p[15],p[16]));
}
)METAL";

} // namespace jalium
