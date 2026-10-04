namespace Jalium.UI;

/// <summary>
/// Compile-time native library names shared by all generated P/Invoke stubs.
/// Embedded Apple applications statically link JaliumNative.xcframework, so
/// their symbols live in the main executable and must be addressed through
/// <c>__Internal</c>. Desktop and Android/Linux builds keep modular names.
/// </summary>
internal static class JaliumNativeLibraryNames
{
#if JALIUM_APPLE_STATIC
    public const string Core = "__Internal";
    public const string Platform = "__Internal";
    public const string Metal = "__Internal";
    public const string Software = "__Internal";
    public const string Text = "__Internal";
    public const string Media = "__Internal";
    public const string Browser = "__Internal";
    public const string Aot = "__Internal";
#else
    public const string Core = "jalium.native.core";
    public const string Platform = "jalium.native.platform";
    public const string Metal = "jalium.native.metal";
    public const string Software = "jalium.native.software";
    public const string Text = "jalium.native.text";
    public const string Media = "jalium.native.media";
    public const string Browser = "jalium.native.browser";
    public const string Aot = "jalium.native.aot";
#endif

    // These backends never exist in the embedded Apple image. Keep their
    // modular names so the AOT linker does not require inert symbols merely
    // because their managed wrappers are present after trimming.
    public const string D3D12 = "jalium.native.d3d12";
    public const string Vulkan = "jalium.native.vulkan";
}
