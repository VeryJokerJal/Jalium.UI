namespace Jalium.UI.Hosting;

/// <summary>
/// Root options object bound to the <c>Jalium</c> section of <see cref="Jalium.Extensions.Configuration.IConfiguration"/>.
/// Consumed by the framework through <see cref="Jalium.Extensions.Options.IOptions{TOptions}"/>,
/// which means callers can update settings at build time via
/// <c>builder.Configuration</c> / <c>appsettings.json</c> or at construction time via
/// <c>builder.Services.Configure&lt;JaliumRuntimeOptions&gt;(opts =&gt; ...)</c>.
/// </summary>
public sealed class JaliumRuntimeOptions
{
    /// <summary>
    /// Configuration section name used by <c>builder.Configuration.GetSection(JaliumRuntimeOptions.SectionName)</c>.
    /// </summary>
    public const string SectionName = "Jalium";

    /// <summary>
    /// Working-set trimming / memory-pressure options.
    /// </summary>
    public JaliumWorkingSetOptions WorkingSet { get; set; } = new();

    /// <summary>
    /// Rendering pipeline / debug visualization options.
    /// </summary>
    public JaliumRenderOptions Render { get; set; } = new();

    /// <summary>
    /// Frame-time / FPS metrics collection options.
    /// </summary>
    public JaliumMetricsOptions Metrics { get; set; } = new();

    /// <summary>
    /// MVVM / view resolution options.
    /// </summary>
    public JaliumMvvmOptions Mvvm { get; set; } = new();
}

/// <summary>
/// Working-set trimming configuration. Mirrors the <c>JALIUM_WORKING_SET_*</c>
/// environment variables consumed by <c>WorkingSetTrimController</c>.
/// </summary>
public sealed class JaliumWorkingSetOptions
{
    /// <summary>
    /// When <see langword="true"/> (or when the <c>JALIUM_WORKING_SET_TRIM</c>
    /// env variable is set), enable periodic <c>EmptyWorkingSet</c> calls.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// "normal" or "ultra" trim profile. Maps to <c>TrimProfile</c> in
    /// <c>WorkingSetTrimController</c>. Defaults to <c>"normal"</c>.
    /// </summary>
    public string Profile { get; set; } = "normal";

    /// <summary>
    /// Hard working-set limit in megabytes (0 = no hard limit).
    /// </summary>
    public int HardLimitMegabytes { get; set; }

    /// <summary>
    /// Soft trim trigger in megabytes (0 = use default).
    /// </summary>
    public int TriggerMegabytes { get; set; }
}

/// <summary>
/// Rendering pipeline / debug options. Maps to the existing
/// <c>JALIUM_DEBUG_RENDER</c> / <c>JALIUM_D3D12_FORCE_FULL_REPLAY</c> env vars.
/// </summary>
public sealed class JaliumRenderOptions
{
    /// <summary>
    /// When <see langword="true"/>, overlays debug visualizations (dirty rects,
    /// overdraw heatmap, etc.) on every frame.
    /// </summary>
    public bool DebugRender { get; set; }

    /// <summary>
    /// When <see langword="true"/>, forces full-frame replay on the D3D12 backend
    /// (disables dirty-region optimization). Useful when diagnosing missed
    /// invalidations.
    /// </summary>
    public bool ForceFullReplay { get; set; }

    /// <summary>
    /// Preferred rendering backend (e.g. <c>"Auto"</c>, <c>"D3D12"</c>, <c>"Vulkan"</c>,
    /// <c>"Software"</c>). Empty / null lets the framework pick.
    /// </summary>
    public string Backend { get; set; } = "Auto";
}

/// <summary>
/// Options controlling the <see cref="JaliumMeter"/> FPS / frame-time metrics.
/// </summary>
public sealed class JaliumMetricsOptions
{
    /// <summary>
    /// When <see langword="true"/>, instruments frame rendering with counters
    /// and histograms exposed through the <c>Jalium.UI</c> <c>Meter</c>. Off by
    /// default — turn on only when observability is needed (there's a small
    /// per-frame cost).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Rolling window size (in frames) used to compute the FPS gauge.
    /// </summary>
    public int FpsWindowFrames { get; set; } = 60;
}

/// <summary>
/// MVVM / view resolution options.
/// </summary>
public sealed class JaliumMvvmOptions
{
    /// <summary>
    /// When <see langword="true"/> (default), <c>Frame</c> and other navigation
    /// hosts ask the DI container for views before falling back to
    /// <see cref="System.Activator.CreateInstance(System.Type)"/>. Views registered
    /// with <c>AddView&lt;TView, TViewModel&gt;()</c> get their DataContext set
    /// automatically.
    /// </summary>
    public bool EnableViewFactory { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, elements tagged with
    /// <see cref="ViewModelLocator.AutoWireViewModelProperty"/> will have their
    /// <see cref="FrameworkElement.DataContext"/> resolved from DI even when
    /// constructed outside a navigation host (e.g. XAML-created controls).
    /// </summary>
    public bool EnableAutoWireDataContext { get; set; } = true;
}
