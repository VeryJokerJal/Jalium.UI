namespace Jalium.UI.Media.Effects;

/// <summary>
/// Base class for GPU-based transition effects that blend two textures based on progress.
/// Uses two sampler inputs: s0 (old content) and s1 (new content) with a Progress uniform.
/// </summary>
public abstract class TransitionEffect : ShaderEffect
{
    /// <summary>
    /// Identifies the Progress dependency property (register c0).
    /// </summary>
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(TransitionEffect),
            new PropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    /// <summary>
    /// Identifies the NewContentBrush dependency property (sampler s1).
    /// </summary>
    public static readonly DependencyProperty NewContentBrushProperty =
        RegisterPixelShaderSamplerProperty("NewContentBrush", typeof(TransitionEffect), 1);

    /// <summary>
    /// Identifies the Resolution dependency property (register c1).
    /// </summary>
    public static readonly DependencyProperty ResolutionProperty =
        DependencyProperty.Register(nameof(Resolution), typeof(Size), typeof(TransitionEffect),
            new PropertyMetadata(new Size(800, 600), PixelShaderConstantCallback(1)));

    /// <summary>
    /// Gets or sets the transition progress (0.0 to 1.0).
    /// </summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty)!;
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush providing the new content texture.
    /// </summary>
    public Brush? NewContentBrush
    {
        get => (Brush?)GetValue(NewContentBrushProperty);
        set => SetValue(NewContentBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the resolution of the transition area.
    /// </summary>
    public Size Resolution
    {
        get => (Size)GetValue(ResolutionProperty)!;
        set => SetValue(ResolutionProperty, value);
    }
}

/// <summary>
/// Contains HLSL source code for all transition effects.
/// Shaders use ps_3_0 profile with two samplers and uniform registers.
/// </summary>
public static class TransitionShaderSources
{
    /// <summary>
    /// Common HLSL functions used by multiple transition shaders.
    /// </summary>
    public const string Common = """
        sampler2D OldContent : register(s0);
        sampler2D NewContent : register(s1);
        float Progress : register(c0);
        float2 Resolution : register(c1);

        float hash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float noise2D(float2 st)
        {
            float2 i = floor(st);
            float2 f = frac(st);
            float a = hash(i);
            float b = hash(i + float2(1.0, 0.0));
            float c = hash(i + float2(0.0, 1.0));
            float d = hash(i + float2(1.0, 1.0));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
        }
        """;

    public const string Dissolve = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float n = noise2D(uv * 40.0);
            float threshold = Progress * 1.2 - 0.1;
            float edge = smoothstep(threshold - 0.05, threshold + 0.05, n);
            float4 oldColor = tex2D(OldContent, uv);
            float4 newColor = tex2D(NewContent, uv);
            // Edge glow: orange-ish border at the dissolve front
            float edgeMask = smoothstep(threshold - 0.08, threshold - 0.03, n) *
                             (1.0 - smoothstep(threshold - 0.03, threshold + 0.02, n));
            float3 edgeColor = float3(1.0, 0.5, 0.1) * edgeMask * 2.0;
            float4 result = lerp(newColor, oldColor, edge);
            result.rgb += edgeColor;
            return result;
        }
        """;

    public const string Pixelate = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float maxBlock = 40.0;
            // Block size peaks at progress=0.5 then shrinks
            float blockSize = max(1.0, maxBlock * sin(Progress * 3.14159));
            float2 blockUV = floor(uv * Resolution / blockSize) * blockSize / Resolution;

            if (Progress < 0.5)
                return tex2D(OldContent, blockUV);
            else
                return tex2D(NewContent, blockUV);
        }
        """;

    public const string Glitch = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float intensity = sin(Progress * 3.14159) * 0.8 + 0.2;

            // Random horizontal line displacement
            float lineNoise = hash(float2(floor(uv.y * 30.0), floor(Progress * 20.0)));
            float displacement = (lineNoise - 0.5) * 0.15 * intensity;

            // RGB channel separation
            float shift = displacement * intensity;
            float2 uvR = uv + float2(shift, 0);
            float2 uvG = uv;
            float2 uvB = uv - float2(shift, 0);

            // Randomly switch between old and new content per block
            float blockSwitch = hash(float2(floor(uv.x * 8.0), floor(uv.y * 12.0 + Progress * 5.0)));
            bool useNew = blockSwitch < Progress;

            float r, g, b;
            if (useNew)
            {
                r = tex2D(NewContent, uvR).r;
                g = tex2D(NewContent, uvG).g;
                b = tex2D(NewContent, uvB).b;
            }
            else
            {
                r = tex2D(OldContent, uvR).r;
                g = tex2D(OldContent, uvG).g;
                b = tex2D(OldContent, uvB).b;
            }

            // Scanline effect
            float scanline = sin(uv.y * Resolution.y * 2.0) * 0.03 * intensity;

            return float4(r + scanline, g + scanline, b + scanline, 1.0);
        }
        """;

    public const string ChromaticSplit = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float spread = (1.0 - Progress) * 0.08;

            // Old content: RGB channels spread apart
            float oldR = tex2D(OldContent, uv + float2(spread, spread * 0.5)).r;
            float oldG = tex2D(OldContent, uv).g;
            float oldB = tex2D(OldContent, uv - float2(spread, spread * 0.5)).b;
            float4 oldSplit = float4(oldR, oldG, oldB, 1.0);

            // New content: RGB channels converge
            float newSpread = Progress * 0.08;
            float newR = tex2D(NewContent, uv + float2(newSpread, newSpread * 0.5)).r;
            float newG = tex2D(NewContent, uv).g;
            float newB = tex2D(NewContent, uv - float2(newSpread, newSpread * 0.5)).b;
            float4 newSplit = float4(newR, newG, newB, 1.0);

            return lerp(oldSplit, newSplit, Progress);
        }
        """;

    public const string LiquidMorph = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float time = Progress * 6.28318;
            float strength = sin(Progress * 3.14159) * 0.12;

            float2 distortion = float2(
                sin(uv.y * 15.0 + time) * strength,
                cos(uv.x * 15.0 + time * 1.3) * strength
            );

            // Add secondary wave for more organic feel
            distortion += float2(
                sin(uv.y * 8.0 - time * 0.7) * strength * 0.5,
                cos(uv.x * 8.0 - time * 0.5) * strength * 0.5
            );

            float4 oldColor = tex2D(OldContent, uv + distortion);
            float4 newColor = tex2D(NewContent, uv - distortion * 0.5);

            // Smooth blend with slight bias toward center timing
            float blend = smoothstep(0.2, 0.8, Progress);
            return lerp(oldColor, newColor, blend);
        }
        """;

    public const string WaveDistortion = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float amplitude = sin(Progress * 3.14159) * 0.15;
            float frequency = 8.0;
            float speed = Progress * 12.56636;

            float wave = sin(uv.y * frequency + speed) * amplitude;
            float2 oldUV = uv + float2(wave, wave * 0.3);
            float2 newUV = uv - float2(wave * 0.5, wave * 0.2);

            float4 oldColor = tex2D(OldContent, oldUV);
            float4 newColor = tex2D(NewContent, newUV);

            return lerp(oldColor, newColor, Progress);
        }
        """;

    public const string WindBlow = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            // Column-based displacement: each vertical strip has different noise
            float columnNoise = hash(float2(floor(uv.x * 30.0), 0));
            float rowNoise = hash(float2(0, floor(uv.y * 50.0)));

            // Threshold determines which pixels have "blown away"
            float threshold = Progress * 1.5 - columnNoise * 0.3 - rowNoise * 0.2;

            if (threshold > 0.5)
            {
                // Pixel has blown away, show new content
                return tex2D(NewContent, uv);
            }

            // Pixel is being displaced by wind
            float displacement = max(0, threshold) * 0.5;
            float2 blownUV = uv + float2(
                displacement * (1.0 + columnNoise),
                displacement * 0.3 * sin(uv.y * 20.0)
            );

            // Clamp to bounds
            blownUV = clamp(blownUV, 0.0, 1.0);

            float4 oldColor = tex2D(OldContent, blownUV);
            // Fade based on displacement
            oldColor.a *= 1.0 - displacement * 2.0;

            float4 newColor = tex2D(NewContent, uv);
            return lerp(newColor, oldColor, oldColor.a);
        }
        """;

    public const string RippleReveal = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 center = float2(0.5, 0.5);
            float dist = length(uv - center);
            float maxDist = length(float2(0.5, 0.5)); // ~0.707

            // Ripple front expands from center
            float rippleRadius = Progress * maxDist * 1.3;
            float rippleWidth = 0.08;

            // Inside ripple = new content, outside = old content
            float mask = smoothstep(rippleRadius, rippleRadius - rippleWidth, dist);

            // Add wave distortion at the ripple front
            float rippleDist = abs(dist - rippleRadius);
            float waveStrength = (1.0 - smoothstep(0.0, rippleWidth * 2.0, rippleDist))
                               * sin(rippleDist * 60.0) * 0.015
                               * (1.0 - Progress); // Waves diminish as transition completes
            float2 waveOffset = normalize(uv - center + 0.001) * waveStrength;

            float4 oldColor = tex2D(OldContent, uv + waveOffset);
            float4 newColor = tex2D(NewContent, uv);

            return lerp(oldColor, newColor, mask);
        }
        """;

    public const string ClockWipe = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float2 center = float2(0.5, 0.5);
            float2 dir = uv - center;

            // Angle from 12 o'clock position (top), clockwise
            // atan2(x, -y) gives angle from top, increasing clockwise
            float angle = atan2(dir.x, -dir.y);
            // Normalize to 0..1
            float normalizedAngle = (angle + 3.14159) / 6.28318;

            // Wipe threshold
            float threshold = Progress;

            if (normalizedAngle < threshold)
                return tex2D(NewContent, uv);
            else
                return tex2D(OldContent, uv);
        }
        """;

    public const string ThermalFade = """
        float4 main(float2 uv : TEXCOORD) : COLOR
        {
            float4 oldColor = tex2D(OldContent, uv);
            float4 newColor = tex2D(NewContent, uv);

            // Convert to luminance
            float lum = dot(oldColor.rgb, float3(0.299, 0.587, 0.114));

            // Heat map: blue → cyan → green → yellow → red → white
            float3 thermal;
            if (lum < 0.2)
                thermal = lerp(float3(0, 0, 0.5), float3(0, 0, 1), lum / 0.2);
            else if (lum < 0.4)
                thermal = lerp(float3(0, 0, 1), float3(0, 1, 0), (lum - 0.2) / 0.2);
            else if (lum < 0.6)
                thermal = lerp(float3(0, 1, 0), float3(1, 1, 0), (lum - 0.4) / 0.2);
            else if (lum < 0.8)
                thermal = lerp(float3(1, 1, 0), float3(1, 0, 0), (lum - 0.6) / 0.2);
            else
                thermal = lerp(float3(1, 0, 0), float3(1, 1, 1), (lum - 0.8) / 0.2);

            // Phase 1 (0-0.4): normal → thermal
            // Phase 2 (0.4-0.6): thermal holds with white-hot glow
            // Phase 3 (0.6-1.0): thermal → new content
            float4 result;
            if (Progress < 0.4)
            {
                float t = Progress / 0.4;
                result = lerp(oldColor, float4(thermal, 1), t);
            }
            else if (Progress < 0.6)
            {
                float t = (Progress - 0.4) / 0.2;
                float glow = t * 0.3;
                result = float4(thermal + glow, 1);
            }
            else
            {
                float t = (Progress - 0.6) / 0.4;
                // New content also goes through thermal briefly
                float newLum = dot(newColor.rgb, float3(0.299, 0.587, 0.114));
                float3 newThermal = float3(1, max(0.5, newLum), newLum * 0.5);
                float4 thermalNew = lerp(float4(newThermal, 1), newColor, t);
                result = lerp(float4(thermal, 1), thermalNew, t);
            }

            return result;
        }
        """;
}
