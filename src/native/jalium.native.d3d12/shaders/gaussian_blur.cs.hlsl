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
    float g_Radius;        // kernel radius in OUTPUT texels (extent; ~3 sigma)
    uint  g_TexWidth;      // output texture width  (= source width  when g_SrcMode == 0)
    uint  g_TexHeight;     // output texture height (= source height when g_SrcMode == 0)
    // Source remap, horizontal pass only. g_SrcMode == 0 is the historical
    // path: integer Load of the input at the output coordinate. g_SrcMode == 1
    // samples the input through a linear-clamp sampler at
    //   src = g_SrcOffset + (out + 0.5) * g_SrcScale
    // so one pass both crops a sub-rect of a larger source (backdrop region
    // inside the full-frame snapshot) and downsamples it (g_SrcScale = 2 / 4).
    uint  g_SrcMode;
    float g_SrcOffsetX;    // source-space origin of output texel (0, 0), in texels
    float g_SrcOffsetY;
    float g_SrcScale;      // source texels per output texel
    float g_Sigma;         // Gaussian sigma in output texels; 0 = g_Radius / 3
    float g_SrcClampW;     // valid source extent in texels (g_SrcMode == 1): the
    float g_SrcClampH;     //   captured viewport, not the grow-only allocation
    uint  g_Pad0;
    float g_InvSrcAllocW;  // 1 / source allocation size, for uv normalisation
    float g_InvSrcAllocH;
    uint  g_Pad1;
    uint  g_Pad2;
};

Texture2D<float4>   g_Input  : register(t0);
RWTexture2D<float4> g_Output : register(u0);
SamplerState        g_SrcSampler : register(s0);   // linear, clamp (g_SrcMode == 1)

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

// Gaussian weights depend only on (k, sigma), both uniform across the whole
// dispatch, yet the accumulation loop used to call exp() once per tap per
// pixel — 37 transcendentals per pixel per pass at the 18px radius the default
// backdrop uses, times two passes. They are computed once per thread group
// here instead, which is 256x fewer exp() calls for the same result.
groupshared float sharedWeights[2 * MAX_KERNEL_RADIUS + 1];

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

    // Sigma is explicit when the caller has one (backdrop materials carry
    // BlurSigma; Box kernels arrive as radius / sqrt(3)); otherwise the D2D
    // convention radius ~ 3 * sigma.
    float sigma = g_Sigma > 0.0f ? g_Sigma : g_Radius / 3.0f;
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
    // Remapped source (horizontal pass of a cropped / downsampled backdrop
    // region): sample the full-size source through the clamp sampler instead
    // of loading the output coordinate. The line is NOT clamped to the output
    // extent here — the apron deliberately reads the true neighbours outside
    // the region (the sampler clamps at the source texture edge), which is
    // what makes the region's edge pixels blur like the rest of the frame.
    const bool remapSource = (g_Direction == 0) && (g_SrcMode != 0);
    const float2 invSrcAlloc = float2(g_InvSrcAllocW, g_InvSrcAllocH);
    const float2 srcClampLo = float2(0.5f, 0.5f);
    const float2 srcClampHi = max(srcClampLo, float2(g_SrcClampW, g_SrcClampH) - 0.5f);

    for (int i = (int)groupIndex; i < THREAD_GROUP_SIZE + 2 * kernelRadius; i += THREAD_GROUP_SIZE)
    {
        int coord = cacheBase + i;
        float4 sample;
        if (remapSource)
        {
            float2 src = float2(g_SrcOffsetX + ((float)coord + 0.5f) * g_SrcScale,
                                g_SrcOffsetY + ((float)lineIndex + 0.5f) * g_SrcScale);
            // Clamp to the captured extent: the allocation beyond it holds
            // stale pixels from an earlier, larger viewport.
            src = clamp(src, srcClampLo, srcClampHi);
            sample = g_Input.SampleLevel(g_SrcSampler, src * invSrcAlloc, 0);
        }
        else
        {
            coord = clamp(coord, 0, lineLen - 1);

            int2 texCoord;
            if (g_Direction == 0)
                texCoord = int2(coord, lineIndex);
            else
                texCoord = int2(lineIndex, coord);

            sample = g_Input.Load(int3(texCoord, 0));
        }

        // The source is premultiplied sRGB. Decode its straight color, then
        // premultiply in linear light before accumulating Gaussian weights.
        sample.rgb = sample.a > 0.000001f
            ? SrgbToLinear(saturate(sample.rgb / sample.a)) * sample.a
            : float3(0, 0, 0);
        sharedCache[i] = sample;
    }

    // Fill the shared weight table in the same pre-barrier phase — the loop is
    // strided by group size so a 129-tap kernel costs each thread at most one
    // exp(), and the existing barrier below already covers it.
    for (int wi = (int)groupIndex; wi <= 2 * kernelRadius; wi += THREAD_GROUP_SIZE)
    {
        sharedWeights[wi] = GaussianWeight((float)(wi - kernelRadius), sigma);
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
        float w = sharedWeights[k + kernelRadius];
        sum += sharedCache[cacheCenter + k] * w;
        weightSum += w;
    }

    sum /= max(weightSum, 0.0001f);

    // Restore premultiplied sRGB. Applying the transfer curve directly to
    // linear premultiplied RGB would make translucent fringes too bright.
    sum.rgb = sum.a > 0.000001f
        ? LinearToSrgb(saturate(sum.rgb / sum.a)) * sum.a
        : float3(0, 0, 0);

    // Write output
    int2 outCoord;
    if (g_Direction == 0)
        outCoord = int2(pos, lineIndex);
    else
        outCoord = int2(lineIndex, pos);

    g_Output[outCoord] = sum;
}
