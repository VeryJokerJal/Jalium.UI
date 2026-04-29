namespace Jalium.UI.Controls;

/// <summary>
/// Aggregate counters for jalxaml deserialization, populated by
/// <c>Jalium.UI.Markup.XamlReader.LoadComponent</c> via the
/// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>
/// from Jalium.UI.Controls.csproj.  Used by <c>Window.Show</c>'s startup trace
/// to summarize "N XAML loads totaling X ms" at the moment the first window
/// becomes visible — useful when JALIUM_STARTUP_TRACE is set, zero-cost
/// otherwise (just two interlocked increments per LoadComponent call).
/// Lives in Jalium.UI.Controls (which is referenced by Jalium.UI.Xaml,
/// not the other way around) so Window.cs can read the counters without
/// pulling in a circular project dependency.
/// </summary>
internal static class XamlLoadStartupTrace
{
    internal static long LoadCallCount;
    internal static long LoadTotalTicks;
}
