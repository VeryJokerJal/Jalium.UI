using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// Backend registration entry point for NativeAOT / fully-static-linked builds.
/// </summary>
/// <remarks>
/// In the standard shared-DLL build each backend's DllMain registers itself with
/// the core BackendRegistry on load, so this class is never used. When the
/// process is statically linked (NativeAOT consumes <c>jalium.native.aot.static.lib</c>
/// alongside the per-backend <c>.static.lib</c> archives) there are no DLLs and
/// therefore no DllMain — the application MUST call
/// <see cref="RegisterAllBackends"/> exactly once before any
/// <c>jalium_context_create</c> call so the backend registry is populated.
/// <para>
/// The entry point lives in <c>jalium.native.aot</c>. In the AOT image this is
/// resolved by <c>&lt;DirectPInvoke Include="jalium.native.aot" /&gt;</c>; in a
/// dynamic build there is no such DLL and the call would throw
/// <see cref="System.DllNotFoundException"/>, which is fine because nobody calls
/// it in that mode.
/// </para>
/// </remarks>
public static class JaliumStaticInit
{
    private const string AotLib = JaliumNativeLibraryNames.Aot;

    [DllImport(AotLib, EntryPoint = "jalium_aot_register_all_backends", CallingConvention = CallingConvention.Cdecl)]
    public static extern void RegisterAllBackends();
}
