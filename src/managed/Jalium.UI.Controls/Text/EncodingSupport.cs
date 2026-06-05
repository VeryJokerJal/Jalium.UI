using System.Runtime.CompilerServices;
using System.Text;

namespace Jalium.UI.Controls;

/// <summary>
/// Registers <see cref="CodePagesEncodingProvider"/> when the module loads, so
/// that legacy / non-Unicode code pages — GBK (936), GB18030, Shift-JIS (932),
/// Big5 (950), EUC-KR and the rest — are available to
/// <see cref="Encoding.GetEncoding(int)"/> and
/// <see cref="Encoding.GetEncoding(string)"/> everywhere in the framework.
/// </summary>
/// <remarks>
/// .NET ships only UTF-8/16/32, ASCII and Latin-1 in the box; every other
/// encoding throws <see cref="System.NotSupportedException"/> from
/// <c>Encoding.GetEncoding</c> until this provider is registered. The
/// <see cref="Terminal"/> control's configurable encoding and the QR encoder's
/// Shift-JIS mode both rely on it. Registration is idempotent and order-
/// independent, so running it from a module initializer is safe.
/// </remarks>
internal static class EncodingSupport
{
    /// <summary>
    /// Runs once, automatically, when the <c>Jalium.UI.Controls</c> module is
    /// first loaded.
    /// </summary>
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "The encoding provider must be registered before any control touches Encoding.GetEncoding for a legacy code page; a module initializer is the only place that reliably runs ahead of all consumer code, and registration is idempotent and order-independent.")]
    internal static void Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
