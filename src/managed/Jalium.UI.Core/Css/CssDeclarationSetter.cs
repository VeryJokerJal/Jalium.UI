namespace Jalium.UI.Styling;

/// <summary>
/// The write-back channel handed to <see cref="FrameworkElement.TryApplyCssPropertyCore"/>.
/// Values written through <see cref="Set"/> enter the CSS value layers (CssBase/CssState),
/// participate in the apply diff, and are automatically cleared when the rule stops
/// matching — implementations must never call SetValue directly (that would pin an
/// uncollectable local value).
/// </summary>
public readonly struct CssDeclarationSetter
{
    private readonly ICssSetterSink _sink;
    private readonly FrameworkElement _element;
    private readonly string _cssName;

    internal CssDeclarationSetter(ICssSetterSink sink, FrameworkElement element, string cssName)
    {
        _sink = sink;
        _element = element;
        _cssName = cssName;
    }

    /// <summary>Maps the declaration onto a dependency property value.</summary>
    public void Set(DependencyProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);
        _sink.Set(property, value);
    }

    /// <summary>Reports a one-shot lossy-conversion diagnostic for this declaration.</summary>
    public void ReportLossy(string message)
        => CssDiagnostics.Report(_cssName, CssDiagnosticReason.LossyConversion, _element.GetType(), message);
}
