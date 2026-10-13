using System.Reflection;

namespace Jalium.UI.Styling;

public static partial class Css
{
    /// <summary>
    /// Loads a CSS resource into this element's scoped style sheets. Unqualified paths
    /// are relative to the root of the assembly containing the JALXAML declaration;
    /// component URIs can reference another assembly. Changing or clearing the source
    /// replaces only the sheet owned by this property. Nested @import rules are loaded.
    /// </summary>
    public static readonly DependencyProperty StyleSheetSourceProperty =
        DependencyProperty.RegisterAttached(
            "StyleSheetSource", typeof(string), typeof(Css),
            new PropertyMetadata(string.Empty, OnStyleSheetSourceChanged));

    public static string GetStyleSheetSource(DependencyObject element)
        => (string)(element.GetValue(StyleSheetSourceProperty) ?? string.Empty);

    public static void SetStyleSheetSource(DependencyObject element, string? value)
        => element.SetValue(StyleSheetSourceProperty, value);

    private static void OnStyleSheetSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not (FrameworkElement or FrameworkContentElement)) return;
        if (CssXmlIdentity.DesignerContext(d) is { EnableCss: false }) return;

        var element = CssNode.Get(d);
        var state = CssEngine.EnsureState(element);
        CssStyleSheet? replacement = null;
        if (e.NewValue is string source && !string.IsNullOrWhiteSpace(source))
        {
            // Finish loading before changing the cascade: a bad replacement must not
            // remove a working sheet or leave a partially applied import graph.
            Uri? uri = null;
            try
            {
                uri = ResolveStyleSheetSource(d, source);
                replacement = CssStyleSheet.LoadAsync(uri).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                ArgumentException or UriFormatException or InvalidOperationException or System.Net.Http.HttpRequestException)
            {
                throw new InvalidOperationException(
                    $"Unable to load CSS style sheet '{source}'" +
                    (uri is null ? "." : $" (resolved as '{uri}')."), exception);
            }
        }

        var sheets = GetStyleSheets(d);
        var index = state.SourceStyleSheet is { } previous ? sheets.IndexOf(previous) : -1;
        state.SourceStyleSheet = replacement;
        if (index >= 0)
        {
            if (replacement is null) sheets.RemoveAt(index);
            else sheets[index] = replacement;
        }
        else if (replacement is not null)
        {
            // Authored stylesheet text overrides the external sheet regardless of the
            // order of the two attributes in markup.
            var declaredIndex = state.DeclaredStyleSheet is { } declared ? sheets.IndexOf(declared) : -1;
            if (declaredIndex >= 0) sheets.Insert(declaredIndex, replacement);
            else sheets.Add(replacement);
        }
    }

    private static Uri ResolveStyleSheetSource(DependencyObject element, string source)
    {
        var path = source.Trim().Replace('\\', '/');
        const string packPrefix = "pack://application:,,,";
        if (path.StartsWith(packPrefix, StringComparison.OrdinalIgnoreCase)) path = path[packPrefix.Length..];
        if (path.StartsWith('/') && path.Contains(";component/", StringComparison.OrdinalIgnoreCase))
            return new Uri(path, UriKind.Relative);
        if (path.Contains(':') && Uri.TryCreate(path, UriKind.Absolute, out var absolute)) return absolute;

        if (CssXmlIdentity.DesignerContext(element)?.BaseUri is { } designerBaseUri)
            return new Uri(designerBaseUri, path.TrimStart('/'));

        var assembly = CssXmlIdentity.SourceAssembly(element) ??
            (element.GetType().Assembly == typeof(FrameworkElement).Assembly
                ? Assembly.GetEntryAssembly() : element.GetType().Assembly);
        if (assembly is null) return new Uri(path, UriKind.Relative);

        var segments = new List<string>();
        foreach (var segment in path.TrimStart('/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new ArgumentException("CSS resource paths cannot escape the assembly root.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return new Uri($"/{assembly.GetName().Name};component/{string.Join('/', segments)}", UriKind.Relative);
    }
}
