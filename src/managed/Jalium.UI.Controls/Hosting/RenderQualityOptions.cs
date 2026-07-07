namespace Jalium.UI.Hosting;

/// <summary>
/// Anti-aliasing quality for filled vector paths. Selected via
/// <c>app.UsePathAntiAliasing(...)</c>. Higher MSAA is smoother but multiplies the
/// per-path GPU cost (measured ~6–7 ms per 8× path on a weak iGPU); the analytic
/// mode matches how WPF and Chromium/Skia actually anti-alias 2D vector content.
/// </summary>
public enum PathAntiAliasing
{
    /// <summary>
    /// CPU analytic coverage anti-aliasing (no GPU MSAA) — the cheapest option and
    /// the approach WPF and Chromium/Skia use for 2D vector fills. Best for weak
    /// integrated GPUs.
    /// </summary>
    Analytic = 0,

    /// <summary>4× MSAA — the common baseline when GPU MSAA is used (Chromium's default sample count).</summary>
    Msaa4x = 4,

    /// <summary>8× MSAA — the framework's historical default. Smooth but the heaviest common tier.</summary>
    Msaa8x = 8,

    /// <summary>16× MSAA — maximum smoothness; only on GPUs that support it, otherwise clamped down.</summary>
    Msaa16x = 16,
}

/// <summary>
/// Process-wide rendering-quality selection. Activated by
/// <c>app.UsePathAntiAliasing(PathAntiAliasing)</c>; defaults to
/// <see cref="PathAntiAliasing.Msaa8x"/> (the framework's historical behavior, so
/// omitting the call changes nothing).
/// </summary>
/// <remarks>
/// The chosen mode is applied to every window's render target via
/// <c>RenderTarget.SetPathMsaaSampleCount</c> at creation (and re-applied on
/// device-lost recovery). Lowering the path MSAA level is the single biggest lever
/// for the per-present GPU cost on weak GPUs, where filled paths (8× stencil-then-
/// cover) dominate the frame.
/// </remarks>
public sealed class RenderQualityOptions
{
    /// <summary>The single process-wide instance.</summary>
    public static RenderQualityOptions Current { get; } = new();

    /// <summary>
    /// The anti-aliasing quality for filled vector paths. Defaults to
    /// <see cref="PathAntiAliasing.Msaa8x"/> (unchanged historical behavior), or the
    /// <c>JALIUM_PATH_AA</c> environment override (<c>16</c>/<c>8</c>/<c>4</c>/<c>analytic</c>)
    /// when set — handy for A/B measuring without a code change. <c>app.UsePathAntiAliasing</c>
    /// overrides both.
    /// </summary>
    public PathAntiAliasing PathAntiAliasing { get; set; } = ResolveEnvDefault();

    private static PathAntiAliasing ResolveEnvDefault()
    {
        var env = Environment.GetEnvironmentVariable("JALIUM_PATH_AA");
        return env switch
        {
            "16" => PathAntiAliasing.Msaa16x,
            "8" => PathAntiAliasing.Msaa8x,
            "4" => PathAntiAliasing.Msaa4x,
            "1" or "0" or "analytic" or "Analytic" or "aa" or "AA" => PathAntiAliasing.Analytic,
            _ => PathAntiAliasing.Msaa8x,
        };
    }

    /// <summary>
    /// The value to hand to <c>RenderTarget.SetPathMsaaSampleCount</c> for the
    /// current <see cref="PathAntiAliasing"/>. MSAA tiers map to their sample count;
    /// <see cref="PathAntiAliasing.Analytic"/> maps to <c>0</c>, the sentinel that
    /// tells the native side to route solid fills to the analytic-coverage
    /// rasterizer instead of the MSAA stencil path. The native side clamps MSAA
    /// counts down to what the GPU actually supports.
    /// </summary>
    public uint ResolvePathMsaaSampleCount() => PathAntiAliasing switch
    {
        PathAntiAliasing.Msaa16x => 16u,
        PathAntiAliasing.Msaa8x => 8u,
        PathAntiAliasing.Msaa4x => 4u,
        PathAntiAliasing.Analytic => 0u,
        _ => 8u,
    };
}
