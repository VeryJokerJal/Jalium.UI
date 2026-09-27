using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal static class RenderBackendSelector
{
    internal const string BackendOverrideEnvironmentVariable = "JALIUM_RENDER_BACKEND";
    internal const string GpuPreferenceEnvironmentVariable = "JALIUM_GPU_PREFERENCE";
    internal const string EngineOverrideEnvironmentVariable = "JALIUM_RENDERING_ENGINE";

    internal static RenderBackend GetPreferredBackend()
        => ResolvePreferredBackend();

    internal static RenderBackend ResolvePreferredBackend(
        Func<RenderBackend, bool>? isAvailable = null,
        string? backendOverride = null,
        bool? isWindows = null,
        bool? isMacOS = null,
        bool? isLinux = null,
        bool? isAndroid = null,
        bool? isIOS = null,
        bool? isTvOS = null)
    {
        isAvailable ??= backend => NativeMethods.IsBackendAvailable(backend) != 0;
        backendOverride ??= Environment.GetEnvironmentVariable(BackendOverrideEnvironmentVariable)?.Trim();
        isWindows ??= RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        isMacOS ??= RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        isLinux ??= RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        isAndroid ??= IsAndroidPlatform();
        isIOS ??= OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst();
        isTvOS ??= OperatingSystem.IsTvOS();

        if (TryParseBackend(backendOverride, out var requestedBackend) &&
            requestedBackend != RenderBackend.Auto &&
            isAvailable(requestedBackend))
        {
            return requestedBackend;
        }

        foreach (var backend in GetPreferredOrder(isWindows.Value, isMacOS.Value,
                     isLinux.Value, isAndroid.Value, isIOS.Value, isTvOS.Value))
        {
            if (isAvailable(backend))
            {
                return backend;
            }
        }

        return RenderBackend.Auto;
    }

    internal static bool IsAndroidPlatform()
        => RuntimeInformation.RuntimeIdentifier?.Contains("android", StringComparison.OrdinalIgnoreCase) ?? false;

    internal static bool TryParseBackend(string? value, out RenderBackend backend)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
                backend = RenderBackend.Auto;
                return false;
            case "auto":
            case "default":
                backend = RenderBackend.Auto;
                return true;
            case "d3d12":
            case "dx12":
            case "direct3d12":
                backend = RenderBackend.D3D12;
                return true;
            case "vulkan":
            case "vk":
                backend = RenderBackend.Vulkan;
                return true;
            case "metal":
                backend = RenderBackend.Metal;
                return true;
            case "software":
            case "cpu":
                backend = RenderBackend.Software;
                return true;
            default:
                backend = RenderBackend.Auto;
                return false;
        }
    }

    internal static GpuPreference GetPreferredGpuPreference(RenderBackend backend)
        => ResolveGpuPreference(backend);

    internal static GpuPreference ResolveGpuPreference(
        RenderBackend backend,
        string? preferenceOverride = null,
        bool? isWindows = null)
    {
        preferenceOverride ??= Environment.GetEnvironmentVariable(GpuPreferenceEnvironmentVariable)?.Trim();
        isWindows ??= RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        return preferenceOverride?.Trim().ToLowerInvariant() switch
        {
            "auto" or "default" or "os" or "system" => GpuPreference.Auto,
            "high" or "high_performance" or "discrete" => GpuPreference.HighPerformance,
            "low" or "minimum_power" or "integrated" or "igpu" => GpuPreference.MinimumPower,
            // A D3D12 UI process on a hybrid Windows machine must not silently
            // inherit the scan-out adapter. That commonly pins rendering to an
            // old integrated driver even when a discrete GPU is available. It
            // also makes a TDR recovery recreate the device on the same failing
            // adapter. Prefer the high-performance DXGI ordering by default;
            // JALIUM_GPU_PREFERENCE=auto remains the explicit opt-in to
            // monitor/OS affinity, while other backends keep their old policy.
            _ when backend == RenderBackend.D3D12 && isWindows.Value
                => GpuPreference.HighPerformance,
            _ => GpuPreference.Auto,
        };
    }

    internal static GpuPreference? GetFallbackGpuPreference(GpuPreference preference)
        => preference switch
        {
            GpuPreference.HighPerformance => GpuPreference.MinimumPower,
            GpuPreference.MinimumPower => GpuPreference.HighPerformance,
            _ => null,
        };

    private static RenderBackend[] GetPreferredOrder(bool isWindows, bool isMacOS,
        bool isLinux, bool isAndroid = false, bool isIOS = false, bool isTvOS = false)
    {
        // Platform GPU backend first, then the secondary GPU backend, then the
        // software rasterizer. Mirrors the native preferred order inside
        // jalium_context_create, so a host whose primary GPU backend is missing
        // gets the other GPU backend instead of dropping straight to the CPU.
        if (isWindows)
        {
            return [RenderBackend.D3D12, RenderBackend.Vulkan, RenderBackend.Software];
        }

        if (isMacOS || isIOS || isTvOS)
        {
            return [RenderBackend.Metal, RenderBackend.Vulkan, RenderBackend.Software];
        }

        if (isAndroid || isLinux)
        {
            return [RenderBackend.Vulkan, RenderBackend.Software];
        }

        return [RenderBackend.D3D12, RenderBackend.Vulkan, RenderBackend.Software];
    }

    // ========================================================================
    // Rendering Engine Selection
    // ========================================================================

    internal static RenderingEngine GetPreferredRenderingEngine()
        => ResolvePreferredRenderingEngine();

    internal static RenderingEngine ResolvePreferredRenderingEngine(string? engineOverride = null)
    {
        engineOverride ??= Environment.GetEnvironmentVariable(EngineOverrideEnvironmentVariable)?.Trim();

        if (TryParseRenderingEngine(engineOverride, out var engine) && engine != RenderingEngine.Auto)
        {
            return engine;
        }

        // Default: Auto (resolved at native level per-backend)
        return RenderingEngine.Auto;
    }

    internal static bool TryParseRenderingEngine(string? value, out RenderingEngine engine)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
                engine = RenderingEngine.Auto;
                return false;
            case "auto":
            case "default":
                engine = RenderingEngine.Auto;
                return true;
            case "vello":
                engine = RenderingEngine.Vello;
                return true;
            case "impeller":
            case "flutter":
                engine = RenderingEngine.Impeller;
                return true;
            default:
                engine = RenderingEngine.Auto;
                return false;
        }
    }
}
