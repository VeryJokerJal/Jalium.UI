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

    private void TryLoadSection()
    {
        var name = SectionName;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!RazorExpressionRegistry.TryGetGlobalSection(name, out var xaml))
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
