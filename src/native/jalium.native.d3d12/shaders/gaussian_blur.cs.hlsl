// ============================================================================
// Separable Gaussian Blur Compute Shader
//
// Single shader handles both horizontal and vertical passes via cbuffer constant.
// Uses groupshared memory to cache texture samples for the blur kernel window.
//
// Dispatch: ceil(width/256) x height groups for horizontal pass
//           width x ceil(height/256) groups for vertical pass
// ============================================================================

cbuffer BlurConstants : register(b0)
{
    uint  g_Direction;     // 0 = horizontal, 1 = vertical
    float g_Radius;        // blur radius in pixels (standard deviation for Gaussian)
    uint  g_TexWidth;      // source texture width
    uint  g_TexHeight;     // source texture height
};

Texture2D<float4>   g_Input  : register(t0);
RWTexture2D<float4> g_Output : register(u0);

// sRGB ↔ linear conversion so blurring happens in linear light space.
// This avoids the perceptual darkening that occurs when averaging sRGB values.
float SrgbToLinearCh(float s)
{
    return (s <= 0.04045) ? s / 12.92 : pow((s + 0.055) / 1.055, 2.4);
}
float3 SrgbToLinear(float3 s)
{
    return float3(SrgbToLinearCh(s.x), SrgbToLinearCh(s.y), SrgbToLinearCh(s.z));
}

float LinearToSrgbCh(float l)
{
    return (l <= 0.0031308) ? l * 12.92 : 1.055 * pow(l, 1.0 / 2.4) - 0.055;
}
float3 LinearToSrgb(float3 l)
{
    return float3(LinearToSrgbCh(l.x), LinearToSrgbCh(l.y), LinearToSrgbCh(l.z));
}

// Kernel radius is clamped so total taps fit in shared memory.
// Max kernel radius = 64 -> diameter 129 taps. More than enough for any UI blur.
#define MAX_KERNEL_RADIUS 64
#define THREAD_GROUP_SIZE 256
#define CACHE_SIZE (THREAD_GROUP_SIZE + 2 * MAX_KERNEL_RADIUS)

groupshared float4 sharedCache[CACHE_SIZE];

// Approximate Gaussian weight: exp(-0.5 * (d/sigma)^2).
// We normalise the full kernel after summation so the constant factor cancels.
float GaussianWeight(float d, float sigma)
{
    float x = d / max(sigma, 0.0001f);
    return exp(-0.5f * x * x);
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void main(uint3 groupId : SV_GroupID,
          uint  groupIndex : SV_GroupIndex,
          uint3 dispatchId : SV_DispatchThreadID)
{
    // Clamp kernel radius
    int kernelRadius = (int)min(g_Radius, (float)MAX_KERNEL_RADIUS);
    if (kernelRadius < 1) kernelRadius = 1;

    float sigma = g_Radius / 3.0f;  // match D2D convention: radius ~ 3*sigma
    if (sigma < 0.5f) sigma = 0.5f;

    // Determine the 1-D coordinate along the blur axis
    int lineLen, lineCount;
    if (g_Direction == 0) {
        // Horizontal pass: threads sweep along X; one group per row-tile
        lineLen   = (int)g_TexWidth;
        lineCount = (int)g_TexHeight;
    } else {
        // Vertical pass: threads sweep along Y; one group per column-tile
        lineLen   = (int)g_TexHeight;
        lineCount = (int)g_TexWidth;
    }

    // Which line (row or column) this group is processing
    int lineIndex = (int)groupId.y;
    if (lineIndex >= lineCount) return;

    // Base position along the line for this group tile
    int tileStart = (int)groupId.x * THREAD_GROUP_SIZE;

    // ------------------------------------------------------------------
    // Fill shared memory cache: each thread loads its main sample + apron
    // ------------------------------------------------------------------
    int cacheBase = tileStart - kernelRadius; // start of cache in line coords

    // Each thread may need to load multiple apron entries
    for (int i = (int)groupIndex; i < THREAD_GROUP_SIZE + 2 * kernelRadius; i += THREAD_GROUP_SIZE)
    {
        int coord = cacheBase + i;
        coord = clamp(coord, 0, lineLen - 1);

        int2 texCoord;
        if (g_Direction == 0)
            texCoord = int2(coord, lineIndex);
        else
            texCoord = int2(lineIndex, coord);

        // Convert from sRGB to linear on load so blurring is physically correct.
        float4 sample = g_Input.Load(int3(texCoord, 0));
        sample.rgb = SrgbToLinear(sample.rgb);
        sharedCache[i] = sample;
    }

    GroupMemoryBarrierWithGroupSync();

    // ------------------------------------------------------------------
    // Compute blurred value from cached samples (in linear space)
    // ------------------------------------------------------------------
    int pos = tileStart + (int)groupIndex;
    if (pos >= lineLen) return;

    float4 sum = float4(0, 0, 0, 0);
    float  weightSum = 0.0f;

    int cacheCenter = (int)groupIndex + kernelRadius;

    for (int k = -kernelRadius; k <= kernelRadius; k++)
    {
        float w = GaussianWeight((float)k, sigma);
        sum += sharedCache[cacheCenter + k] * w;
        weightSum += w;
    }

    sum /= max(weightSum, 0.0001f);

    // Convert back to sRGB for storage
    sum.rgb = LinearToSrgb(sum.rgb);

    // Write output
    int2 outCoord;
    if (g_Direction == 0)
        outCoord = int2(pos, lineIndex);
    else
        outCoord = int2(lineIndex, pos);

    g_Output[outCoord] = sum;
}
