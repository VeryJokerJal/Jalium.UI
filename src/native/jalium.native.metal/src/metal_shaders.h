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

float4 sample_gradient(constant float* p, float2 local)
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
        // A focal radial gradient is approximated by shifting the normalized
        // sample origin; this matches the common D3D/Vulkan UI case and keeps
        // the complete stop/spread contract on the GPU.
        float2 q = (local - mix(origin, center, 0.5)) / radius;
        t = length(q);
    }
    t = spread_value(t, uint(max(p[44], 0.0)));

    uint count = min(uint(max(p[45], 0.0)), 32u);
    if (count == 0u) return float4(p[4], p[5], p[6], p[7]);
    float4 previous = float4(p[49], p[50], p[51], p[52]);
    float previousPos = p[48];
    if (t <= previousPos) return previous;
    for (uint i = 1u; i < count; ++i) {
        const uint base = 48u + i * 5u;
        float pos = p[base];
        float4 color = float4(p[base + 1u], p[base + 2u],
                              p[base + 3u], p[base + 4u]);
        if (t <= pos) {
            float u = (t - previousPos) / max(pos - previousPos, 1e-6);
            return mix(previous, color, clamp(u, 0.0, 1.0));
        }
        previous = color;
        previousPos = pos;
    }
    return previous;
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

float clip_coverage(constant float* p, float2 pixel)
{
    const uint count = min(uint(max(p[208], 0.0)), 18u);
    float coverage = 1.0;
    for (uint i = 0u; i < count; ++i) {
        const uint base = 212u + i * 16u;
        const float2 local = float2(
            p[base + 0u] * pixel.x + p[base + 2u] * pixel.y + p[base + 4u],
            p[base + 1u] * pixel.x + p[base + 3u] * pixel.y + p[base + 5u]);
        const float4 rect = float4(p[base + 6u], p[base + 7u],
                                   p[base + 8u], p[base + 9u]);
        const float4 radii = float4(p[base + 10u], p[base + 11u],
                                    p[base + 12u], p[base + 13u]);
        const uint mode = uint(max(p[base + 14u], 0.0));
        const float d = sd_round_rect(local, rect, radii);
        float inside = (mode & 4u) != 0u
            ? (d <= 0.0 ? 1.0 : 0.0)
            : 1.0 - smoothstep(-0.75, 0.75, d);
        if ((mode & 2u) != 0u) inside = 1.0 - inside;
        coverage *= inside;
    }
    return coverage;
}

fragment void jalium_clip_stencil_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]])
{
    // Keep the full antialias fringe in the stencil. The color fragment then
    // applies exact fractional coverage (or a hard edge for aliased clips).
    if (clip_coverage(p, in.pixel) <= 0.0001) discard_fragment();
}

fragment float4 jalium_shape_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]])
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
    coverage *= clip_coverage(p, in.pixel);

    float4 color = uint(max(p[3], 0.0)) == 0u
        ? float4(p[4], p[5], p[6], p[7])
        : sample_gradient(p, local);
    color.a *= p[17] * coverage;
    color.rgb *= color.a;
    return color;
}

fragment float4 jalium_texture_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]])
{
    float4 color = source.sample(sourceSampler, in.uv);
    if (uint(max(p[179], 0.0)) == 1u) {
        float4 tint = float4(p[4], p[5], p[6], p[7]);
        color = float4(tint.rgb * tint.a * color.a, tint.a * color.a);
    }
    color *= p[17] * clip_coverage(p, in.pixel);
    return color;
}

fragment float4 jalium_yuv_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]],
    texture2d<float> lumaTexture [[texture(0)]],
    texture2d<float> chromaTexture [[texture(1)]],
    sampler sourceSampler [[sampler(0)]])
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
    float alpha = p[17] * clip_coverage(p, in.pixel);
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
    texture2d<float> toTexture [[texture(1)]],sampler sourceSampler [[sampler(0)]])
{
    float progress=clamp(p[400],0.0,1.0);int mode=int(round(p[401]));
    float4 fromMap=float4(p[404],p[405],p[406],p[407]);
    float4 toMap=float4(p[408],p[409],p[410],p[411]);
    float4 oldColor=transition_sample(fromTexture,sourceSampler,in.uv,fromMap);
    float4 newColor=transition_sample(toTexture,sourceSampler,in.uv,toMap);
    float4 color=float4(0.0);float2 resolution=max(float2(p[0],p[1]),float2(1.0));
    if(mode==0){
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
    color*=p[17]*clip_coverage(p,in.pixel);return color;
}

fragment float4 jalium_effect_fragment(VertexOut in [[stage_in]],
    constant float* p [[buffer(0)]], texture2d<float> source [[texture(0)]],
    sampler sourceSampler [[sampler(0)]])
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
        float4 neighbor=source.sample(sourceSampler,in.uv-texel);
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
        float2 local = to_local(p,in.pixel);
        float4 rect = float4(p[351],p[352],p[353],p[354]);
        float radius = min(max(p[355],0.0),min(rect.z,rect.w)*0.5);
        float selfSd = p[349] > 0.5
            ? sd_superellipse(local,rect,p[350])
            : sd_round_rect(local,rect,float4(radius));
        float combinedSd=selfSd;
        float minNeighbor=1e20;
        float2 closestCenter=float2(0.0);
        int neighborCount=min(int(max(p[360],0.0)),4);
        float2 screenDip=in.pixel/max(float2(p[358],p[359]),float2(1e-4));
        for(int i=0;i<neighborCount;++i){
            int base=365+i*5;
            float4 nr=float4(p[base],p[base+1],p[base+2],p[base+3]);
            float nd=sd_round_rect(screenDip,nr,float4(max(p[base+4],0.0)));
            combinedSd=smooth_min(combinedSd,nd,p[361]);
            if(nd<minNeighbor){minNeighbor=nd;closestCenter=nr.xy+nr.zw*0.5;}
        }
        if(neighborCount>0&&selfSd>0.0&&minNeighbor<selfSd){
            c=float4(0.0);
        }else{
            float aa=max(fwidth(combinedSd),0.5);
            if(combinedSd>aa){
                float shadow=smoothstep(24.0,0.0,combinedSd-4.0)*0.1;
                c=float4(0.0,0.0,0.0,shadow);
            }else{
                float2 e=float2(0.5,0.0);
                float dx,dy;
                if(p[349]>0.5){
                    dx=sd_superellipse(local+e,rect,p[350])-sd_superellipse(local-e,rect,p[350]);
                    dy=sd_superellipse(local+e.yx,rect,p[350])-sd_superellipse(local-e.yx,rect,p[350]);
                }else{
                    dx=sd_round_rect(local+e,rect,float4(radius))-sd_round_rect(local-e,rect,float4(radius));
                    dy=sd_round_rect(local+e.yx,rect,float4(radius))-sd_round_rect(local-e.yx,rect,float4(radius));
                }
                float2 normal=normalize(float2(dx,dy)+float2(1e-6));
                float depth=clamp(1.0-(-min(combinedSd,0.0))/max(p[340]*0.667,1.0),0.0,1.0);
                float bend=(1.0-sqrt(max(1.0-depth*depth,0.0)))*(-p[340]);
                float2 texel=1.0/max(float2(p[356],p[357]),float2(1.0));
                float2 refractedUv=in.uv+normal*bend*texel;
                float dispersion=p[341]*((local.x-(rect.x+rect.z*0.5))*(local.y-(rect.y+rect.w*0.5)) /
                    max(rect.z*rect.w*0.25,1.0));
                float2 chromaOffset=normal*bend*dispersion*texel;
                float4 center=source.sample(sourceSampler,refractedUv);
                if(p[341]>0.01){
                    center.r=source.sample(sourceSampler,refractedUv+chromaOffset).r;
                    center.b=source.sample(sourceSampler,refractedUv-chromaOffset).b;
                }
                float alpha=center.a;
                float3 color=alpha>1e-6?center.rgb/alpha:float3(0.0);
                color=mix(color,float3(p[345],p[346],p[347]),clamp(p[348],0.0,1.0));
                float2 light=float2(p[342],p[343]);
                float lightTerm=p[344];
                if(all(light>=float2(0.0))){
                    float2 lightDir=normalize(light-screenDip+float2(1e-6));
                    lightTerm+=pow(clamp(dot(-normal,lightDir),0.0,1.0),8.0)*0.8;
                }
                float rim=exp(-pow((-combinedSd-0.75)/0.75,2.0));
                color+=float3(1.0)*rim*clamp(lightTerm,0.0,1.5);
                float mask=1.0-smoothstep(-aa,aa,combinedSd);
                alpha=max(alpha,0.08+p[348]*0.25)*mask;
                c=float4(clamp(color,0.0,1.0)*alpha,alpha);
            }
        }
    }
    c *= p[17] * clip_coverage(p, in.pixel);
    return c;
}

kernel void jalium_blur(texture2d<float, access::read> source [[texture(0)]],
    texture2d<float, access::write> destination [[texture(1)]],
    constant float* p [[buffer(0)]], uint2 gid [[thread_position_in_grid]])
{
    if (gid.x >= destination.get_width() || gid.y >= destination.get_height()) return;
    int radius = clamp(int(p[0]), 0, 32);
    bool horizontal = p[1] > 0.5;
    float sigma = max(p[2], max(float(radius) / 3.0, 0.5));
    float4 sum = 0.0;
    float weightSum = 0.0;
    for (int i = -radius; i <= radius; ++i) {
        int2 q = int2(gid) + (horizontal ? int2(i, 0) : int2(0, i));
        if (p[3] > 0.5) {
            uint hash = (gid.x * 1664525u + gid.y * 1013904223u + uint(i + radius));
            int jitter = (hash & 1u) != 0u ? 1 : -1;
            q += horizontal ? int2(0,jitter) : int2(jitter,0);
        }
        q = clamp(q, int2(0), int2(int(source.get_width()) - 1,
                                  int(source.get_height()) - 1));
        float weight = uint(max(p[4],0.0)) == 1u ? 1.0
            : exp(-float(i * i) / (2.0 * sigma * sigma));
        sum += source.read(uint2(q)) * weight;
        weightSum += weight;
    }
    destination.write(sum / max(weightSum, 1e-6), gid);
}
)METAL";

} // namespace jalium
