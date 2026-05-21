using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Forwards <see cref="TextOptions.ProcessTextRenderingMode"/> changes into the
/// native glyph atlas through <c>jalium_text_set_global_antialias_mode</c>. The
/// module initializer also pushes the current value once at process startup so
/// the native side reflects the managed default (<see cref="TextRenderingMode.Auto"/>)
/// even when the application never explicitly assigns it.
/// </summary>
/// <remarks>
/// <para>
/// Public so the Media layer can locate it via reflection when the
/// <see cref="ModuleInitializerAttribute"/> path got removed by the IL trimmer
/// — <see cref="TextOptions.ProcessTextRenderingMode"/> calls
/// <c>EnsureInitialized()</c> on first assignment as a defensive fallback.
/// </para>
/// </remarks>
public static class TextRenderingBridge
{
    private static int s_initialized;

    [ModuleInitializer]
    internal static void ModuleInit()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// Idempotently subscribes to <see cref="TextOptions.ProcessTextRenderingModeChanged"/>
    /// and pushes the current managed value into the native glyph atlas. Safe
    /// to call repeatedly; the second and subsequent calls return immediately.
    /// Invoked automatically through <see cref="ModuleInitializerAttribute"/>
    /// in normal builds, and manually from <see cref="TextOptions"/> via
    /// reflection when the trimmer dropped the module initializer path.
    /// </summary>
    public static void EnsureInitialized()
    {
        if (System.Threading.Interlocked.Exchange(ref s_initialized, 1) != 0)
            return;

        TextOptions.ProcessTextRenderingModeChanged += OnModeChanged;
        Push((int)TextOptions.ProcessTextRenderingMode);
    }

    private static void OnModeChanged(TextRenderingMode mode)
    {
        Push((int)mode);
    }

    private static void Push(int mode)
    {
        try
        {
            NativeMethods.SetGlobalTextAntialiasMode(mode);
        }
        catch (System.DllNotFoundException) { }
        catch (System.EntryPointNotFoundException) { }
    }
}
