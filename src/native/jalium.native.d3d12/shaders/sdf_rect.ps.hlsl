struct PsInput
{
    float4 clipPos      : SV_Position;
    float2 localPos     : TEXCOORD0;
    float2 rectSize     : TEXCOORD1;
    float4 cornerRadius : TEXCOORD2;
    float4 fillColor    : COLOR0;
    float4 borderColor  : COLOR1;
    float  borderWidth  : TEXCOORD3;
    nointerpolation uint  gradientType : TEXCOORD4;
    nointerpolation uint  stopCount    : TEXCOORD5;
    nointerpolation float4 gradGeom    : TEXCOORD6;
    nointerpolation float4 stop01PosR  : TEXCOORD7;
    nointerpolation float4 stop01AG    : TEXCOORD8;
    nointerpolation float4 stop12BA    : TEXCOORD9;
    nointerpolation float4 stop23GB    : TEXCOORD10;
    nointerpolation float4 stop3Color  : TEXCOORD11;
    nointerpolation float2 shapeParams : TEXCOORD12;
};

float sdSuperEllipseRect(float2 p, float2 halfSize, float n)
{
    float2 q = abs(p) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n) - 1.0;
    return d * min(halfSize.x, halfSize.y);
}

float sdRoundedBox(float2 p, float2 b, float4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.wz;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

float4 SampleGradient(PsInput input, float t)
{
    t = saturate(t);
    uint count = input.stopCount;
    if (count == 0) return float4(0, 0, 0, 0);

    float  pos[4];
    float4 col[4];

    pos[0] = input.stop01PosR.x;
    col[0] = float4(input.stop01PosR.yzw, input.stop01AG.x);
    pos[1] = input.stop01AG.y;
    col[1] = float4(input.stop01AG.zw, input.stop12BA.xy);
    pos[2] = input.stop12BA.z;
    col[2] = float4(input.stop12BA.w, input.stop23GB.xyz);
    pos[3] = input.stop23GB.w;
    col[3] = input.stop3Color;

    if (t <= pos[0] || count == 1) return col[0];
    if (t >= pos[count - 1]) return col[count - 1];

    [unroll]
    for (uint i = 0; i < 3; i++)
    {
        if (i + 1 < count && t >= pos[i] && t <= pos[i + 1])
        {
            float range = pos[i + 1] - pos[i];
            float local = (range > 0.0) ? (t - pos[i]) / range : 0.0;
            return lerp(col[i], col[i + 1], local);
        }
    }
    return col[count - 1];
}

float4 main(PsInput input) : SV_Target
{
    float2 halfSize = input.rectSize * 0.5;
    float2 p = input.localPos - halfSize;

    float4 r = float4(
        input.cornerRadius.y,
        input.cornerRadius.x,
        input.cornerRadius.w,
        input.cornerRadius.z);

    float maxR = min(halfSize.x, halfSize.y);
    r = min(r, maxR);

    float dist;
    if (input.shapeParams.x > 0.5)
        dist = sdSuperEllipseRect(p, halfSize, input.shapeParams.y);
    else
        dist = sdRoundedBox(p, halfSize, r);
    float aa = max(fwidth(dist), 0.0001);
    float fillAlpha = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);

    float4 fill;
    if (input.gradientType == 1)
    {
        float2 start = input.gradGeom.xy;
        float2 end_  = input.gradGeom.zw;
        float2 dir   = end_ - start;
        float lenSq  = dot(dir, dir);
        float t = (lenSq > 0.0) ? dot(input.localPos - start, dir) / lenSq : 0.0;
        fill = SampleGradient(input, t);
    }
    else if (input.gradientType == 2)
    {
        float2 center = input.gradGeom.xy;
        float2 radius = input.gradGeom.zw;
        float2 d = (input.localPos - center) / max(radius, float2(0.001, 0.001));
        float t = length(d);
        fill = SampleGradient(input, t);
    }
    else
    {
        fill = input.fillColor;
    }

    float4 color;
    if (input.borderWidth > 0.0)
    {
        float fillMask = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist + input.borderWidth);
        float borderMask = fillAlpha - fillMask;
        color = fill * fillMask + input.borderColor * max(borderMask, 0.0);
        color.a = max(color.a, 0.0);
    }
    else
    {
        color = fill * fillAlpha;
    }

    if (color.a < 1.0 / 255.0) discard;
    return color;
}
