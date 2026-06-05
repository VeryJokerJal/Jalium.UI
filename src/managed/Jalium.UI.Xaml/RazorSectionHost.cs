using Jalium.UI.Controls;

namespace Jalium.UI.Markup;

/// <summary>
/// A placeholder control that renders the content of a globally registered
/// <c>@section</c>. When the named section is registered (possibly after this
/// control is loaded), the host automatically parses the section XAML and
/// displays it as its child.
/// </summary>
public sealed class RazorSectionHost : ContentControl
{
    public static readonly DependencyProperty SectionNameProperty =
        DependencyProperty.Register(nameof(SectionName), typeof(string), typeof(RazorSectionHost),
            new PropertyMetadata(null, OnSectionNameChanged));

    public string? SectionName
    {
        get => (string?)GetValue(SectionNameProperty);
        set => SetValue(SectionNameProperty, value);
    }

    public RazorSectionHost()
    {
        RazorExpressionRegistry.SectionRegistered += OnGlobalSectionRegistered;
        RazorExpressionRegistry.SectionUnregistered += OnGlobalSectionUnregistered;
    }

    private static void OnSectionNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RazorSectionHost)d).TryLoadSection();
    }

    private void OnGlobalSectionRegistered(string name)
    {
        if (string.Equals(name, SectionName, StringComparison.Ordinal))
            TryLoadSection();
    }

    private void OnGlobalSectionUnregistered(string name)
    {
        if (string.Equals(name, SectionName, StringComparison.Ordinal))
            Content = null;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Only the runtime-preprocessor fallback reaches XamlReader.Parse, and that path runs solely for @section bodies that were loaded through the runtime XAML reader rather than the source generator. The RUC contract ('XAML loading uses XamlTypeRegistry types whose ctors / overrides may use reflection on user-supplied targets, and may invoke Razor reflection.') is already declared at the public XamlReader.Parse boundary; preserving those XamlTypeRegistry/Razor reflection targets is the documented prerequisite for using the runtime XAML reader under trimming, not a defect of this site. SG-compiled @section bodies take the factory branch above and never touch XamlReader.")]
    private void TryLoadSection()
    {
        var name = SectionName;
        if (string.IsNullOrWhiteSpace(name)) return;

        // SG-compiled @section: invoke the registered factory directly — the section
        // body was lowered to straight-line C# at build time, so there is no XAML
        // string to re-parse here. This is the path that removes XamlReader.Parse
        // from section rendering entirely.
        if (RazorExpressionRegistry.TryGetSectionFactory(name!, out var factory))
        {
            try
            {
                Content = factory();
                InvalidateMeasure();
            }
            catch
            {
                // Compiled section factory threw — leave Content unchanged.
            }
            return;
        }

        // Runtime-preprocessor @section: only the raw XAML string was registered
        // (the defining document was loaded via the runtime parser, not the SG), so
        // fall back to parsing it. Kept for interop with non-SG-compiled documents.
        if (!RazorExpressionRegistry.TryGetGlobalSection(name!, out var xaml))
            return;

        try
        {
            var wrapped = $"<Border xmlns=\"http://schemas.jalium.ui/2024\">{xaml}</Border>";
            Content = XamlReader.Parse(wrapped);
            InvalidateMeasure();
        }
        catch
        {
            // Section XAML failed to parse
        }
    }
}
