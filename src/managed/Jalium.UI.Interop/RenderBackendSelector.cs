using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

internal static class RenderBackendSelector
{
    internal const string BackendOverrideEnvironmentVariable = "JALIUM_RENDER_BACKEND";

    internal static RenderBackend GetPreferredBackend()
        => ResolvePreferredBackend();

    internal static RenderBackend ResolvePreferredBackend(
        Func<RenderBackend, bool>? isAvailable = null,
        string? backendOverride = null,
        bool? isWindows = null,
        bool? isMacOS = null,
        bool? isLinux = null)
    {
        isAvailable ??= backend => NativeMethods.IsBackendAvailable(backend) != 0;
        backendOverride ??= Environment.GetEnvironmentVariable(BackendOverrideEnvironmentVariable);
        isWindows ??= RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        isMacOS ??= RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        isLinux ??= RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        if (TryParseBackend(backendOverride, out var requestedBackend) &&
            requestedBackend != RenderBackend.Auto &&
            isAvailable(requestedBackend))
        {
            return requestedBackend;
        }

        foreach (var backend in GetPreferredOrder(isWindows.Value, isMacOS.Value, isLinux.Value))
        {
            if (isAvailable(backend))
            {
                return backend;
            }
        }

        return RenderBackend.Auto;
    }

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

    private static RenderBackend[] GetPreferredOrder(bool isWindows, bool isMacOS, bool isLinux)
    {
        if (isWindows)
        {
            return
            [
                RenderBackend.D3D12,
                RenderBackend.Vulkan,
                RenderBackend.Software
            ];
        }

        if (isMacOS)
        {
            return
            [
                RenderBackend.Metal,
                RenderBackend.Vulkan,
                RenderBackend.Software
            ];
        }

        if (isLinux)
        {
            return
            [
                RenderBackend.Vulkan,
                RenderBackend.Software
            ];
        }

        return
        [
            RenderBackend.D3D12,
            RenderBackend.Metal,
            RenderBackend.Vulkan,
            RenderBackend.Software
        ];
    }
}
